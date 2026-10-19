using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace EndpointRag.Core;

/// <summary>Reads syntax/metadata only. Never loads user assemblies or executes MSBuild/project code.</summary>
public sealed class ControllerScanner
{
    private static readonly HashSet<string> Excluded = new(StringComparer.OrdinalIgnoreCase)
        { "bin", "obj", ".git", ".vs", "node_modules", "packages", "artifacts", "test", "tests", "samples", "reff", "reference" };
    private static readonly HashSet<string> Wrappers = new(StringComparer.Ordinal)
        { "Task", "ValueTask", "ActionResult", "IEnumerable", "IReadOnlyList", "IReadOnlyCollection", "List", "IQueryable", "Nullable", "OpxApiResponse", "OpxApiResult" };

    public ScanResult Scan(string folder, string apiName, IProgress<ScanProgress>? progress, CancellationToken token)
    {
        folder = Path.GetFullPath(folder);
        if (!Directory.Exists(folder)) throw new DirectoryNotFoundException("Folder API tidak ditemukan.");
        if (string.IsNullOrWhiteSpace(apiName)) throw new ArgumentException("Nama API wajib diisi.");
        var warnings = new List<string>();
        var trees = new List<SyntaxTree>();
        var primaryFiles = SourceFiles(folder, warnings, token).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var allFiles = new HashSet<string>(primaryFiles, StringComparer.OrdinalIgnoreCase);
        foreach (var referenceFolder in ReferenceFolders(folder, warnings, token))
            foreach (var file in SourceFiles(referenceFolder, warnings, token)) allFiles.Add(file);
        foreach (var file in allFiles.Order(StringComparer.OrdinalIgnoreCase))
        {
            token.ThrowIfCancellationRequested();
            if (trees.Count >= 10000) throw new InvalidOperationException("Lebih dari 10.000 file C#. Pilih folder API yang lebih spesifik.");
            if (new FileInfo(file).Length > 2_000_000) { warnings.Add($"File terlalu besar, dilewati: {Path.GetRelativePath(folder, file)}"); continue; }
            try
            {
                var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(file), new CSharpParseOptions(LanguageVersion.Preview), file, cancellationToken: token);
                trees.Add(tree);
                if (tree.GetDiagnostics(token).Any(x => x.Severity == DiagnosticSeverity.Error))
                    warnings.Add($"Syntax/preprocessor perlu diperiksa: {Path.GetRelativePath(folder, file)}");
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            { warnings.Add($"File tidak bisa dibaca: {Path.GetRelativePath(folder, file)}"); }
            progress?.Report(new(trees.Count, 0, "Membaca source C#"));
        }
        var compilationTrees = trees.Concat([CSharpSyntaxTree.ParseText("global using System; global using System.Collections.Generic; global using System.Linq; global using System.Threading; global using System.Threading.Tasks;", new CSharpParseOptions(LanguageVersion.Preview))]);
        var compilation = CSharpCompilation.Create("SourceInspection", compilationTrees, References(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var catalog = new KnowledgeCatalog { SourceFile = "C# controller source", SourceSnapshotAt = DateTimeOffset.UtcNow };
        var classes = trees.Where(t => primaryFiles.Contains(t.FilePath)).SelectMany(t => t.GetRoot(token).DescendantNodes().OfType<ClassDeclarationSyntax>()).ToList();
        var done = 0;
        var overviews = new List<ControllerOverview>();
        foreach (var cls in classes)
        {
            token.ThrowIfCancellationRequested();
            var model = compilation.GetSemanticModel(cls.SyntaxTree);
            if (cls.Modifiers.Any(SyntaxKind.AbstractKeyword)) continue;
            var classSymbol = model.GetDeclaredSymbol(cls, token);
            var classAttrs = InheritedAttributes(cls, classSymbol, compilation).ToList();
            if (!cls.Identifier.Text.EndsWith("Controller", StringComparison.Ordinal)
                && !classAttrs.Any(a => AttrName(a.Syntax) is "ApiController" or "Controller")) continue;
            if (classAttrs.Any(a => AttrName(a.Syntax) == "NonController")) continue;
            // First evidence read for every controller: its XML documentation summary.
            var controllerSummary = Summary(cls);
            var beforeCount = catalog.Documents.Count;
            if (controllerSummary.Length == 0) warnings.Add($"Controller summary tidak tersedia: {cls.Identifier.Text}");
            var routes = classAttrs.Where(a => AttrName(a.Syntax) == "Route").Select(a => RouteValue(a.Syntax, a.Model, warnings)).ToList();
            if (routes.Count == 0) routes.Add("");
            var methods = cls.Members.OfType<MethodDeclarationSyntax>().ToList();
            // Include inherited actions declared in source, respecting overrides/hiding.
            var currentNames = methods.Select(x => x.Identifier.Text).ToHashSet();
            for (var b = classSymbol?.BaseType; b is not null; b = b.BaseType)
                foreach (var declaration in b.DeclaringSyntaxReferences.Select(r => r.GetSyntax()).OfType<ClassDeclarationSyntax>())
                    foreach (var m in declaration.Members.OfType<MethodDeclarationSyntax>())
                        if (!currentNames.Contains(m.Identifier.Text)) methods.Add(m);
            foreach (var method in methods)
            {
                var methodModel = compilation.GetSemanticModel(method.SyntaxTree);
                var attrs = method.AttributeLists.SelectMany(a => a.Attributes).ToList();
                if (!method.Modifiers.Any(SyntaxKind.PublicKeyword) || method.Modifiers.Any(SyntaxKind.StaticKeyword) || attrs.Any(a => AttrName(a) == "NonAction")) continue;
                var verbs = attrs.Where(a => AttrName(a).StartsWith("Http", StringComparison.Ordinal) || AttrName(a) == "AcceptVerbs").ToList();
                if (verbs.Count == 0)
                {
                    warnings.Add($"Action public tanpa atribut HTTP tidak diekspor: {cls.Identifier.Text}.{method.Identifier.Text} (conventional routing belum didukung)");
                    continue;
                }
                var action = attrs.FirstOrDefault(a => AttrName(a) == "ActionName") is { } an
                    ? Constant(an.ArgumentList?.Arguments.FirstOrDefault()?.Expression, methodModel) ?? method.Identifier.Text : method.Identifier.Text;
                foreach (var verb in verbs)
                {
                    var verbName = AttrName(verb);
                    var httpMethods = verbName == "AcceptVerbs"
                        ? verb.ArgumentList?.Arguments.Where(a => a.NameEquals is null).Select(a => Constant(a.Expression, methodModel)?.ToUpperInvariant() ?? "UNRESOLVED").ToArray() ?? []
                        : [verbName[4..].ToUpperInvariant()];
                    var verbRoute = verbName == "AcceptVerbs" ? "" : RouteValue(verb, methodModel, warnings);
                    var actionRoutes = attrs.Where(a => AttrName(a) == "Route").Select(a => RouteValue(a, methodModel, warnings)).ToList();
                    if (actionRoutes.Count == 0 || verbRoute.Length > 0) actionRoutes = [verbRoute];
                    foreach (var http in httpMethods)
                    foreach (var prefix in routes)
                    foreach (var suffix in actionRoutes)
                    {
                        var path = CombineRoute(prefix, suffix, cls.Identifier.Text, action);
                        var doc = CreateDocument(folder, apiName.Trim(), cls, method, classAttrs, attrs, methodModel, compilation, http, path, controllerSummary);
                        catalog.Documents.Add(doc);
                    }
                }
            }
            overviews.Add(new(cls.Identifier.Text, controllerSummary, Path.GetRelativePath(folder, cls.SyntaxTree.FilePath),
                cls.GetLocation().GetLineSpan().StartLinePosition.Line + 1, catalog.Documents.Count - beforeCount));
            progress?.Report(new(++done, classes.Count, $"Controller {cls.Identifier.Text}"));
        }
        var groups = catalog.Documents.GroupBy(d => d.Id).ToList();
        foreach (var duplicate in groups.Where(g => g.Count() > 1))
        {
            warnings.Add($"Duplikasi action/route: {duplicate.First().Endpoint.Method} {duplicate.First().Endpoint.Path}");
            duplicate.First().Evidence.Warnings = [..duplicate.First().Evidence.Warnings, "Duplicate endpoint declaration"];
        }
        catalog.Documents = groups.Select(g => g.First()).OrderBy(d => d.Endpoint.Path).ThenBy(d => d.Endpoint.Method).ToList();
        return new(catalog, warnings.ToArray(), trees.Count, overviews);
    }

    private static EndpointDocument CreateDocument(string root, string api, ClassDeclarationSyntax cls, MethodDeclarationSyntax method,
        List<(AttributeSyntax Syntax, SemanticModel Model)> classAttrs, List<AttributeSyntax> attrs, SemanticModel model,
        CSharpCompilation compilation, string http, string path, string controllerSummary)
    {
        var warn = new List<string>();
        var className = cls.Identifier.Text;
        var methodName = method.Identifier.Text;
        var actionSummary = Summary(method);
        var controller = className.EndsWith("Controller", StringComparison.Ordinal) ? className[..^10] : className;
        var doc = new EndpointDocument
        {
            Api = api, Id = api + "-" + RagJson.Hash($"{api}|{http}|{path}|{className}|{methodName}")[..24],
            Label = $"{controller} — {methodName}", Module = controller.ToLowerInvariant(),
            Endpoint = new() { Method = http, Path = path, Controller = className, Action = methodName },
            Job = new() { Description = $"Action {className}.{methodName} ({http} {path}); makna bisnis perlu ditinjau." },
            Keywords = [controller.ToLowerInvariant(), methodName.ToLowerInvariant()],
            ControllerSummary = controllerSummary, ActionSummary = actionSummary,
            RagInstruction = "Kandidat endpoint; tinjau makna bisnis, scope, dan authorization sebelum digunakan."
        };
        if (controllerSummary.Length == 0) warn.Add("Controller XML summary missing");
        if (actionSummary.Length > 0) doc.Job.Description = actionSummary;
        else warn.Add("Action XML summary missing");
        var operation = OperationClassifier.Classify(method, model, compilation, actionSummary, controllerSummary);
        doc.Operation = operation.Operation; doc.StaticOperation = operation.Operation; doc.Job.JobType = operation.Operation;
        doc.OperationEvidence = operation.Reason;
        if (operation.Conflict) warn.Add("Read summary conflicts with detected persistence call; classified as write for review");
        var authAttrs = classAttrs.Concat(attrs.Select(a => (Syntax: a, Model: model))).ToList();
        doc.Endpoint.AuthSource = authAttrs.Any(a => AttrName(a.Syntax) == "AllowAnonymous") ? "AllowAnonymous" :
            string.Join("; ", authAttrs.Where(a => AttrName(a.Syntax) == "Authorize").Select(a => a.Syntax.ToString()).Distinct());
        if (doc.Endpoint.AuthSource.Length == 0) doc.Endpoint.AuthSource = "Not proven by source (global policy/filter may apply)";
        if (path.Contains("[unresolved]", StringComparison.Ordinal) || http == "UNRESOLVED") warn.Add("Unresolved route/HTTP constant");
        if (path.Contains('[') && !path.Contains("[unresolved]", StringComparison.Ordinal)) warn.Add("Unresolved route token/transformer");
        var requestTypes = new List<ITypeSymbol>();
        foreach (var p in method.ParameterList.Parameters)
        {
            var type = model.GetTypeInfo(p.Type!, default).Type;
            var typeName = TypeName(type, p.Type?.ToString() ?? "unknown");
            if (typeName is "System.Threading.CancellationToken" or "CancellationToken") continue;
            var bindingAttrs = p.AttributeLists.SelectMany(a => a.Attributes).ToList();
            if (bindingAttrs.Any(a => AttrName(a) == "FromServices")) continue;
            if (type is not null) requestTypes.Add(type);
            var bind = bindingAttrs.FirstOrDefault(a => AttrName(a).StartsWith("From", StringComparison.Ordinal));
            var binding = bind is null ? (Regex.IsMatch(path, @"\{" + Regex.Escape(p.Identifier.Text) + @"(?:[:?=}]|$)") ? "route" : "inferred") : AttrName(bind)[4..].ToLowerInvariant();
            var nameArg = bind?.ArgumentList?.Arguments.FirstOrDefault(a => a.NameEquals?.Name.Identifier.Text == "Name");
            var name = Constant(nameArg?.Expression, model) ?? p.Identifier.Text;
            doc.Request.Add(new(name, binding, typeName, p.Type is NullableTypeSyntax || p.Default is not null || type?.NullableAnnotation == NullableAnnotation.Annotated));
            if (binding == "inferred") warn.Add($"Binding parameter {name} requires review");
        }
        var outputs = new List<ITypeSymbol>();
        if (model.GetDeclaredSymbol(method) is { } methodSymbol) outputs.Add(methodSymbol.ReturnType);
        foreach (var attr in attrs.Where(a => AttrName(a) == "ProducesResponseType"))
            foreach (var typeOf in attr.DescendantNodes().OfType<TypeOfExpressionSyntax>())
                if (model.GetTypeInfo(typeOf.Type).Type is { } type) outputs.Add(type);
        var outputVisited = new HashSet<string>();
        if (method.ExpressionBody is not null) TraceOutput(method.ExpressionBody.Expression, model, compilation, outputs, outputVisited, 0);
        foreach (var returned in method.DescendantNodes().OfType<ReturnStatementSyntax>())
            if (returned.Expression is not null) TraceOutput(returned.Expression, model, compilation, outputs, outputVisited, 0);
        // OPX async Task actions commonly use `var response = await service.Call(); await OkOrFailAsync(response);`.
        foreach (var responseCall in method.DescendantNodes().OfType<InvocationExpressionSyntax>().Where(i => InvocationName(i) == "OkOrFailAsync"))
            foreach (var argument in responseCall.ArgumentList.Arguments.Take(1)) TraceOutput(argument.Expression, model, compilation, outputs, outputVisited, 0);
        var context = new List<string> { "FIRST: controller summary: " + (controllerSummary.Length == 0 ? "[missing]" : controllerSummary),
            "SECOND: action summary: " + (actionSummary.Length == 0 ? "[missing]" : actionSummary),
            "Controller attributes: " + string.Join(" ", classAttrs.Select(a => a.Syntax.ToString())), SourceRedactor.Clean(method.ToFullString()) };
        foreach (var invoke in method.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            var symbol = ResolveMethod(invoke, model);
            if (symbol is null) continue;
            // OPX returns Task without a generic result; the service call inside the response wrapper supplies output evidence.
            if (IsResponseValue(invoke) && !IsEnvelope(symbol.ReturnType)) outputs.Add(symbol.ReturnType);
            foreach (var reference in symbol.DeclaringSyntaxReferences.Take(1))
            {
                if (reference.GetSyntax() is MethodDeclarationSyntax serviceMethod)
                    context.Add(SourceRedactor.Clean(serviceMethod.ToFullString()));
            }
        }
        doc.OutputTypes = outputs.SelectMany(Unwrap).Where(t => !IsEnvelope(t)).Select(t => TypeName(t, t.Name)).Where(IsDataType).Distinct().ToArray();
        if (doc.OutputTypes.Length == 0) { doc.Quality.DynamicSchemaIds = [method.ReturnType.ToString()]; warn.Add("Output schema not statically resolved"); }
        var missing = new HashSet<string>();
        var visited = new HashSet<string>();
        var schemaTypes = requestTypes.Concat(outputs.SelectMany(Unwrap).Where(t => !IsEnvelope(t)));
        foreach (var t in schemaTypes) CollectSchema(t, doc.Evidence.Schemas, missing, visited, 0);
        doc.ImportantFields = doc.Evidence.Schemas.Values.SelectMany(x => x).Select(x => x.Split(':')[0]).Distinct().Order().ToArray();
        doc.Quality.MissingSchemaIds = missing.ToArray();
        foreach (var id in missing) warn.Add($"Schema {id} unresolved: type/source metadata unavailable, dynamic payload, or traversal limit reached. Do not infer missing fields.");
        doc.Quality.UnresolvedSchemaIds = doc.Request.Where(p => p.Binding == "inferred").Select(p => p.Type).Distinct().ToArray();
        context.Add("DTO properties: " + RagJson.Serialize(doc.Evidence.Schemas));
        var safe = SourceRedactor.Clean(string.Join("\n\n", context));
        if (safe.Length > 24000) { safe = safe[..24000]; doc.Quality.RagTextTruncated = true; warn.Add("AI source context truncated to 24,000 characters"); }
        doc.Evidence.File = Path.GetRelativePath(root, method.SyntaxTree.FilePath);
        doc.Evidence.Line = method.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
        doc.Evidence.ContentHash = RagJson.Hash(method.ToFullString());
        doc.Evidence.SafeContext = safe;
        doc.Evidence.Warnings = warn.ToArray();
        return doc;
    }

    private static IMethodSymbol? ResolveMethod(InvocationExpressionSyntax invoke, SemanticModel model)
    {
        var info = model.GetSymbolInfo(invoke);
        if (info.Symbol is IMethodSymbol method) return ResolveImplementation(method, model.Compilation);
        var methods = info.CandidateSymbols.OfType<IMethodSymbol>().ToArray();
        return methods.Length == 1 ? ResolveImplementation(methods[0], model.Compilation) : null;
    }
    private static IMethodSymbol? ResolveImplementation(IMethodSymbol method, Compilation compilation)
    {
        if (method.ContainingType.TypeKind != TypeKind.Interface) return method;
        var matches = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        foreach (var tree in compilation.SyntaxTrees)
        foreach (var declaration in tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>().Where(c => c.BaseList is not null))
        {
            var type = compilation.GetSemanticModel(tree).GetDeclaredSymbol(declaration);
            if (type is null || type.IsAbstract || !type.AllInterfaces.Contains(method.ContainingType, SymbolEqualityComparer.Default)) continue;
            if (type.FindImplementationForInterfaceMember(method) is IMethodSymbol implementation)
                matches.Add(implementation);
            if (matches.Count > 1) return null; // DI runtime selection cannot be inferred from multiple implementations.
        }
        return matches.Count == 1 ? (IMethodSymbol)matches.Single() : method;
    }

    private static IEnumerable<string> ReferenceFolders(string root, List<string> warnings, CancellationToken token)
    {
        var pending = new Queue<string>(); pending.Enqueue(root);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { root };
        while (pending.TryDequeue(out var folder))
        {
            token.ThrowIfCancellationRequested();
            var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
            foreach (var project in Directory.EnumerateFiles(folder, "*.csproj", options)
                .Where(p => !Path.GetRelativePath(folder, p).Split(Path.DirectorySeparatorChar).Any(Excluded.Contains)))
            {
                XDocument xml;
                try { xml = XDocument.Load(project); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Xml.XmlException)
                { warnings.Add("Project reference tidak dapat dibaca: " + Path.GetFileName(project)); continue; }
                foreach (var reference in xml.Descendants().Where(x => x.Name.LocalName == "ProjectReference"))
                {
                    var include = reference.Attribute("Include")?.Value;
                    if (string.IsNullOrWhiteSpace(include) || include.IndexOfAny(['$', '*', '?', '%', ';']) >= 0
                        || reference.AncestorsAndSelf().Any(x => x.Attribute("Condition") is not null))
                    { warnings.Add("ProjectReference dinamis/conditional tidak dievaluasi: " + Path.GetFileName(project)); continue; }
                    var target = Path.GetFullPath(include, Path.GetDirectoryName(project)!);
                    if (!File.Exists(target) || !target.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
                    { warnings.Add("ProjectReference tidak ditemukan: " + Path.GetFileName(target)); continue; }
                    var next = Path.GetDirectoryName(target)!;
                    if (seen.Contains(next)) continue;
                    if (seen.Count >= 32) throw new InvalidOperationException("Lebih dari 32 folder project reference.");
                    seen.Add(next); pending.Enqueue(next); yield return next;
                }
            }
        }
    }
    private static string InvocationName(InvocationExpressionSyntax invoke) => invoke.Expression switch
    {
        MemberAccessExpressionSyntax member => member.Name.Identifier.Text,
        SimpleNameSyntax simple => simple.Identifier.Text,
        _ => invoke.Expression.ToString()
    };
    private static bool IsEnvelope(ITypeSymbol t) => Unwrap(t).Any(x => x.Name is "AppResult" or "IActionResult" or "IResult" or "object"
        || x.SpecialType == SpecialType.System_Object || x.AllInterfaces.Any(i => i.Name == "IActionResult"));

    private static void TraceOutput(ExpressionSyntax expression, SemanticModel model, CSharpCompilation compilation,
        List<ITypeSymbol> outputs, HashSet<string> visited, int depth)
    {
        if (depth > 16) return;
        var key = expression.SyntaxTree.FilePath + ":" + expression.SpanStart + ":" + expression.Span.Length + ":" + expression.RawKind;
        if (!visited.Add(key)) return;
        if (expression is AwaitExpressionSyntax awaited) { TraceOutput(awaited.Expression, model, compilation, outputs, visited, depth + 1); return; }
        if (expression is ParenthesizedExpressionSyntax paren) { TraceOutput(paren.Expression, model, compilation, outputs, visited, depth + 1); return; }
        if (expression is PostfixUnaryExpressionSyntax suppressed && suppressed.IsKind(SyntaxKind.SuppressNullableWarningExpression))
        { TraceOutput(suppressed.Operand, model, compilation, outputs, visited, depth + 1); return; }
        if (expression is CastExpressionSyntax cast)
        {
            if (model.GetTypeInfo(cast).Type is { } castType && !IsEnvelope(castType)) outputs.Add(castType);
            TraceOutput(cast.Expression, model, compilation, outputs, visited, depth + 1); return;
        }
        if (expression is MemberAccessExpressionSyntax dataAccess && dataAccess.Name.Identifier.Text == "Data"
            && model.GetTypeInfo(dataAccess.Expression).Type is { } envelope
            && Unwrap(envelope).Any(t => t.Name == "AppResult"))
        {
            TraceOutput(dataAccess.Expression, model, compilation, outputs, visited, depth + 1); return;
        }
        if (expression is ConditionalExpressionSyntax conditional)
        {
            TraceOutput(conditional.WhenTrue, model, compilation, outputs, visited, depth + 1);
            TraceOutput(conditional.WhenFalse, model, compilation, outputs, visited, depth + 1); return;
        }
        if (expression is BaseObjectCreationExpressionSyntax creation && model.GetTypeInfo(creation).Type is { } createdType && IsEnvelope(createdType))
        {
            // Map only AppResult.Data, never wrapper properties.
            if (creation.Initializer is not null)
                foreach (var assignment in creation.Initializer.Expressions.OfType<AssignmentExpressionSyntax>().Where(a => a.Left.ToString() == "Data"))
                    TraceOutput(assignment.Right, model, compilation, outputs, visited, depth + 1);
            var ctor = model.GetSymbolInfo(creation).Symbol as IMethodSymbol;
            if (creation.ArgumentList is not null && ctor is not null)
                for (var i = 0; i < creation.ArgumentList.Arguments.Count; i++)
                {
                    var arg = creation.ArgumentList.Arguments[i];
                    var parameterName = arg.NameColon?.Name.Identifier.Text ?? (i < ctor.Parameters.Length ? ctor.Parameters[i].Name : "");
                    if (parameterName.Equals("data", StringComparison.OrdinalIgnoreCase)) TraceOutput(arg.Expression, model, compilation, outputs, visited, depth + 1);
                }
            return;
        }
        if (expression is InvocationExpressionSyntax invocation)
        {
            var name = InvocationName(invocation);
            if (name is "Error" or "ValidationError" or "NotFound" or "Unauthorized" or "Forbid" or "BadRequest") return;
            if (name is "Success" or "Ok" or "OkOrFailAsync" or "Json" or "FromResult" or "Created" or "CreatedAtAction")
            {
                var arg = name is "Created" or "CreatedAtAction" ? invocation.ArgumentList.Arguments.LastOrDefault() : invocation.ArgumentList.Arguments.FirstOrDefault();
                if (arg is not null) TraceOutput(arg.Expression, model, compilation, outputs, visited, depth + 1);
                return;
            }
            if (name is "ExecuteAsync" or "Execute")
            {
                foreach (var lambda in invocation.ArgumentList.Arguments.Select(a => a.Expression).OfType<LambdaExpressionSyntax>())
                {
                    if (lambda.Body is ExpressionSyntax body) TraceOutput(body, model, compilation, outputs, visited, depth + 1);
                    foreach (var ret in lambda.DescendantNodes().OfType<ReturnStatementSyntax>())
                        if (ret.Expression is not null) TraceOutput(ret.Expression, model, compilation, outputs, visited, depth + 1);
                }
                return;
            }
            var method = ResolveMethod(invocation, model);
            if (method is null) return;
            if (!IsEnvelope(method.ReturnType) && Unwrap(method.ReturnType).Any()) { outputs.Add(method.ReturnType); return; }
            foreach (var reference in method.DeclaringSyntaxReferences)
            {
                if (reference.GetSyntax() is not MethodDeclarationSyntax declaration) continue;
                var methodModel = compilation.GetSemanticModel(declaration.SyntaxTree);
                if (declaration.ExpressionBody is not null) TraceOutput(declaration.ExpressionBody.Expression, methodModel, compilation, outputs, visited, depth + 1);
                foreach (var ret in declaration.DescendantNodes().OfType<ReturnStatementSyntax>())
                    if (ret.Expression is not null) TraceOutput(ret.Expression, methodModel, compilation, outputs, visited, depth + 1);
            }
            return;
        }
        var type = model.GetTypeInfo(expression).Type;
        if (type is not null && type.TypeKind != TypeKind.Error && !IsEnvelope(type)) outputs.Add(type);
        if (expression is IdentifierNameSyntax identifier && model.GetSymbolInfo(identifier).Symbol is { } symbol)
        {
            foreach (var reference in symbol.DeclaringSyntaxReferences)
                if (reference.GetSyntax() is VariableDeclaratorSyntax variable && variable.Initializer is not null)
                    TraceOutput(variable.Initializer.Value, compilation.GetSemanticModel(variable.SyntaxTree), compilation, outputs, visited, depth + 1);
            if (type is not null && IsEnvelope(type))
                foreach (var assignment in expression.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault()?.DescendantNodes().OfType<AssignmentExpressionSyntax>() ?? [])
                    if (assignment.Left is MemberAccessExpressionSyntax member && member.Name.Identifier.Text == "Data"
                        && SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(member.Expression).Symbol, symbol))
                        TraceOutput(assignment.Right, model, compilation, outputs, visited, depth + 1);
        }
        if (expression is QueryExpressionSyntax query)
            foreach (var select in query.DescendantNodes().OfType<SelectClauseSyntax>()) TraceOutput(select.Expression, model, compilation, outputs, visited, depth + 1);
    }

    private static bool IsResponseValue(InvocationExpressionSyntax invoke)
    {
        SyntaxNode node = invoke;
        while (node.Parent is AwaitExpressionSyntax or ParenthesizedExpressionSyntax) node = node.Parent;
        if (node.Parent is ReturnStatementSyntax or ArrowExpressionClauseSyntax) return true;
        if (node.Parent is ArgumentSyntax argument && argument.Parent?.Parent is InvocationExpressionSyntax outer)
        {
            var name = outer.Expression is MemberAccessExpressionSyntax member ? member.Name.Identifier.Text : outer.Expression.ToString();
            return new[] { "Ok", "OkOrFailAsync", "OkAsync", "Json", "Created", "CreatedAtAction" }.Contains(name)
                && outer.ArgumentList.Arguments.Last().Expression == node;
        }
        return false;
    }

    private static void CollectSchema(ITypeSymbol type, Dictionary<string, string[]> schemas, HashSet<string> missing, HashSet<string> visited, int depth)
    {
        foreach (var t in Unwrap(type))
        {
            var name = TypeName(t, t.Name);
            if (!IsDataType(name) || !visited.Add(name)) continue;
            if (t.SpecialType == SpecialType.System_Object || t.TypeKind == TypeKind.Dynamic)
            { missing.Add(name); continue; }
            if (IsEnvelope(t)) continue;
            if (t.TypeKind == TypeKind.Error) { missing.Add(name); continue; }
            if (t.SpecialType != SpecialType.None || t.TypeKind == TypeKind.Enum
                || t.ToDisplayString() is "System.Guid" or "System.DateTime" or "System.DateTimeOffset" or "System.TimeSpan" or "System.DateOnly" or "System.TimeOnly") continue;
            if ((t.DeclaringSyntaxReferences.Length == 0 && !t.IsAnonymousType) || depth > 4)
            { missing.Add(name); continue; }
            var props = new List<IPropertySymbol>();
            for (var current = t as INamedTypeSymbol; current is not null; current = current.BaseType)
                props.AddRange(current.GetMembers().OfType<IPropertySymbol>().Where(p => p.DeclaredAccessibility == Accessibility.Public && !p.IsStatic && !p.IsIndexer
                    && !p.GetAttributes().Any(a => a.AttributeClass?.Name == "JsonIgnoreAttribute")));
            schemas[name] = props.Select(p =>
            {
                var jsonName = p.GetAttributes().FirstOrDefault(a => a.AttributeClass?.Name == "JsonPropertyNameAttribute")?.ConstructorArguments.FirstOrDefault().Value as string;
                return $"{jsonName ?? System.Text.Json.JsonNamingPolicy.CamelCase.ConvertName(p.Name)}: {TypeName(p.Type, p.Type.Name)}";
            }).Distinct().ToArray();
            foreach (var p in props) CollectSchema(p.Type, schemas, missing, visited, depth + 1);
        }
    }

    private static string Summary(SyntaxNode node)
    {
        // Some existing APIs place documentation between attribute lists. Include leading trivia of attributes too.
        var docs = node.GetLeadingTrivia().Concat(node.ChildNodes().OfType<AttributeListSyntax>().SelectMany(a => a.GetLeadingTrivia()))
            .Where(t => t.GetStructure() is DocumentationCommentTriviaSyntax).Select(t => t.GetStructure()!.ToFullString()).Distinct();
        var xml = Regex.Replace(string.Join("\n", docs), @"(?m)^\s*///\s?", "");
        xml = xml.Replace("/**", "", StringComparison.Ordinal).Replace("*/", "", StringComparison.Ordinal);
        try
        {
            var root = XElement.Parse("<doc>" + xml + "</doc>");
            var summary = root.Element("summary");
            if (summary is null) return "";
            foreach (var see in summary.Descendants().Where(x => x.Name.LocalName is "see" or "paramref" or "typeparamref").ToArray())
                see.ReplaceWith(new XText(see.Attribute("cref")?.Value ?? see.Attribute("name")?.Value ?? see.Value));
            return SourceRedactor.RedactText(Regex.Replace(summary.Value, @"\s+", " ").Trim());
        }
        catch (System.Xml.XmlException) { return ""; }
    }
    private static IEnumerable<ITypeSymbol> Unwrap(ITypeSymbol type)
    {
        if (type is IArrayTypeSymbol array) return Unwrap(array.ElementType);
        if (type is INamedTypeSymbol named && Wrappers.Contains(named.Name)) return named.TypeArguments.SelectMany(Unwrap);
        return [type];
    }
    private static bool IsDataType(string name) => !new[] { "void", "Task", "ValueTask", "IActionResult", "ActionResult", "IResult", "CancellationToken" }.Contains(name.Split('.').Last())
        && !name.StartsWith("System.Threading.", StringComparison.Ordinal);
    private static string TypeName(ITypeSymbol? type, string fallback) => type?.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat) ?? fallback;
    private static IEnumerable<(AttributeSyntax Syntax, SemanticModel Model)> InheritedAttributes(ClassDeclarationSyntax cls, INamedTypeSymbol? symbol, CSharpCompilation compilation)
    {
        var own = cls.AttributeLists.SelectMany(x => x.Attributes).ToList();
        foreach (var attr in own) yield return (attr, compilation.GetSemanticModel(cls.SyntaxTree));
        var ownRoutes = own.Any(a => AttrName(a) == "Route");
        for (var b = symbol?.BaseType; b is not null; b = b.BaseType)
            foreach (var baseCls in b.DeclaringSyntaxReferences.Select(r => r.GetSyntax()).OfType<ClassDeclarationSyntax>())
                foreach (var attr in baseCls.AttributeLists.SelectMany(a => a.Attributes))
                {
                    if (AttrName(attr) == "Route" && ownRoutes) continue;
                    yield return (attr, compilation.GetSemanticModel(baseCls.SyntaxTree));
                }
    }
    private static string AttrName(AttributeSyntax attr)
    {
        var n = attr.Name.ToString().Split('.').Last();
        return n.EndsWith("Attribute", StringComparison.Ordinal) ? n[..^9] : n;
    }
    private static string? Constant(ExpressionSyntax? expr, SemanticModel model) => expr is null ? null : model.GetConstantValue(expr) is { HasValue: true, Value: string s } ? s : null;
    private static string RouteValue(AttributeSyntax attr, SemanticModel model, List<string> warnings)
    {
        var arg = attr.ArgumentList?.Arguments.FirstOrDefault(a => a.NameEquals is null);
        if (arg is null) return "";
        var value = Constant(arg.Expression, model);
        if (value is null) warnings.Add("Route constant unresolved: " + Path.GetFileName(attr.SyntaxTree.FilePath));
        return value ?? "[unresolved]";
    }
    private static string CombineRoute(string prefix, string suffix, string controller, string action)
    {
        var path = suffix.StartsWith('/') || suffix.StartsWith("~/", StringComparison.Ordinal) ? suffix.TrimStart('~', '/') : prefix.Trim('/') + "/" + suffix.Trim('/');
        return "/" + path.Trim('/').Replace("[controller]", controller.EndsWith("Controller", StringComparison.Ordinal) ? controller[..^10] : controller, StringComparison.OrdinalIgnoreCase)
            .Replace("[action]", action, StringComparison.OrdinalIgnoreCase);
    }
    private static IEnumerable<string> SourceFiles(string root, List<string> warnings, CancellationToken token)
    {
        var stack = new Stack<string>(); stack.Push(root);
        while (stack.Count > 0)
        {
            token.ThrowIfCancellationRequested();
            var dir = stack.Pop(); string[] files; string[] dirs;
            try { files = Directory.GetFiles(dir, "*.cs"); dirs = Directory.GetDirectories(dir); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { warnings.Add("Folder tidak bisa dibaca: " + Path.GetRelativePath(root, dir)); continue; }
            foreach (var file in files.Order()) if (!file.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase) && !file.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase)) yield return file;
            foreach (var sub in dirs.OrderDescending())
                if (!Excluded.Contains(Path.GetFileName(sub)) && !new DirectoryInfo(sub).Attributes.HasFlag(FileAttributes.ReparsePoint)) stack.Push(sub);
        }
    }
    private static IEnumerable<MetadataReference> References()
    {
        var paths = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries).ToList();
        // Metadata references improve MVC attribute/DTO resolution without requiring the target project's build.
        var dotnet = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", "packs", "Microsoft.AspNetCore.App.Ref");
        if (Directory.Exists(dotnet))
        {
            var pack = Directory.GetDirectories(dotnet).Where(d => Path.GetFileName(d).StartsWith("10.", StringComparison.Ordinal)).OrderDescending().FirstOrDefault();
            if (pack is not null && Directory.Exists(Path.Combine(pack, "ref", "net10.0"))) paths.AddRange(Directory.GetFiles(Path.Combine(pack, "ref", "net10.0"), "Microsoft.AspNetCore.*.dll"));
        }
        return paths.Distinct(StringComparer.OrdinalIgnoreCase).Select(p => MetadataReference.CreateFromFile(p));
    }
}

internal static class SourceRedactor
{
    public static string Clean(string source)
    {
        // AI receives method structure and identifiers, but no literal values (including SQL/credentials).
        var tree = CSharpSyntaxTree.ParseText(source);
        var rewritten = new LiteralRedactor().Visit(tree.GetRoot())!.ToFullString();
        return RedactText(rewritten);
    }
    public static string RedactText(string value) => Regex.Replace(value, @"(?i)(api[_-]?key|password|secret|token|connectionstring)\s*[:=]\s*[^\s,;]+", "$1=[REDACTED]");
    private sealed class LiteralRedactor : CSharpSyntaxRewriter
    {
        public override SyntaxNode? VisitLiteralExpression(LiteralExpressionSyntax node) =>
            node.IsKind(SyntaxKind.StringLiteralExpression) || node.IsKind(SyntaxKind.Utf8StringLiteralExpression)
                ? SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal("[literal omitted]")).WithTriviaFrom(node) : base.VisitLiteralExpression(node);
        public override SyntaxNode? VisitInterpolatedStringExpression(InterpolatedStringExpressionSyntax node) =>
            SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal("[interpolated literal omitted]")).WithTriviaFrom(node);
    }
}
