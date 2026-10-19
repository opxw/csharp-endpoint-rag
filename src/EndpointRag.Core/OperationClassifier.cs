using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Text.RegularExpressions;

namespace EndpointRag.Core;

internal static class OperationClassifier
{
    public static (string Operation, string Reason, bool Conflict) Classify(MethodDeclarationSyntax action,
        SemanticModel model, CSharpCompilation compilation, string actionSummary, string controllerSummary)
    {
        var summary = actionSummary.Length > 0 ? actionSummary : controllerSummary;
        var cleaned = Regex.Replace(summary.ToLowerInvariant(),
            @"\b(tidak|tanpa|bukan|never|without|does not|do not|no)\s+(mengubah|menyimpan|menghapus|membuat|menambah|memperbarui|update|updating|write|writing|save|saving|delete|deleting|insert|inserting)\b", "");
        var read = Regex.IsMatch(cleaned, @"\b(membaca|pembacaan|menampilkan|mengambil|mencari|read|reads|reading|returns?|list|search|query|fetch)\b");
        var write = Regex.IsMatch(cleaned, @"\b(menyimpan|mengubah|menghapus|membuat|menambah|memperbarui|mengupdate|mengirim|submit|create|update|delete|insert|save|write|posting|approve)\b");
        var pending = new Queue<(MethodDeclarationSyntax Method, SemanticModel Model, int Depth)>();
        pending.Enqueue((action, model, 0));
        var visited = new HashSet<string>();
        while (pending.Count > 0 && visited.Count < 60)
        {
            var item = pending.Dequeue();
            if (!visited.Add(item.Method.SyntaxTree.FilePath + ":" + item.Method.SpanStart)) continue;
            foreach (var invocation in item.Method.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                var name = invocation.Expression is MemberAccessExpressionSyntax member ? member.Name.Identifier.Text :
                    invocation.Expression is SimpleNameSyntax simple ? simple.Identifier.Text : "";
                var info = item.Model.GetSymbolInfo(invocation);
                var symbol = info.Symbol as IMethodSymbol ?? (info.CandidateSymbols.Length == 1 ? info.CandidateSymbols[0] as IMethodSymbol : null);
                var receiver = invocation.Expression is MemberAccessExpressionSyntax access ? access.Expression.ToString() : "";
                var symbolNamespace = symbol?.ContainingNamespace?.ToDisplayString() ?? "";
                var database = symbolNamespace.Contains("LinqToDB", StringComparison.Ordinal)
                    || symbolNamespace.Contains("EntityFrameworkCore", StringComparison.Ordinal)
                    || Regex.IsMatch(receiver, @"(?i)\b(_?db|_?database|_?connection|dbcontext)\b");
                if (Regex.IsMatch(name, @"^(SaveChanges|ExecuteNonQuery|BulkCopy)(Async)?$")
                    || database && Regex.IsMatch(name, @"^(Insert|Update|Delete|Merge|InsertOrUpdate|BulkInsert|BulkUpdate|BulkDelete)(Async|With.*)?$"))
                    return ("write", "Detected persistence call: " + name, read && !write);
                if (symbol is null || item.Depth >= 3) continue;
                foreach (var reference in symbol.DeclaringSyntaxReferences)
                    if (reference.GetSyntax() is MethodDeclarationSyntax next)
                        pending.Enqueue((next, compilation.GetSemanticModel(next.SyntaxTree), item.Depth + 1));
            }
        }
        if (read && !write) return ("read", actionSummary.Length > 0 ? "Read declared by action XML summary" : "Read declared by controller XML summary", false);
        if (write && !read) return ("write", actionSummary.Length > 0 ? "Write declared by action XML summary" : "Write declared by controller XML summary", false);
        return ("unknown", read && write ? "Summary describes both read and write" : "No conclusive operation evidence; HTTP verb alone is insufficient", false);
    }
}
