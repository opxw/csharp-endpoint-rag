using System.Text.Json;

namespace EndpointRag.Core;

public sealed class RagProject
{
    public string Format { get; set; } = "csharp-endpoint-rag-project";
    public int Version { get; set; } = 1;
    public KnowledgeCatalog Catalog { get; set; } = new();
    public List<ProjectEvidence> Evidence { get; set; } = [];
    public List<KnowledgeChunk> Knowledge { get; set; } = [];
    public ProjectOptions Options { get; set; } = new();
    public string ControllerOverview { get; set; } = "";
    public string? MappingModel { get; set; }

    public void CaptureEvidence() => Evidence = Catalog.Documents.Select(d => new ProjectEvidence
    {
        Id = d.Id, Source = d.Evidence, SafeContext = d.Evidence.SafeContext,
        Selected = d.Selected, ControllerSummary = d.ControllerSummary, ActionSummary = d.ActionSummary,
        StaticOperation = d.StaticOperation, OperationEvidence = d.OperationEvidence,
        MappingStatus = d.MappingStatus, MappingModel = d.MappingModel, MappingWarnings = d.MappingWarnings,
        ExampleQuestions = d.ExampleQuestions, RelatedKnowledgeIds = d.RelatedKnowledgeIds
    }).ToList();

    public static async Task SaveAsync(string path, RagProject project, CancellationToken token)
    {
        project.CaptureEvidence();
        var bytes = JsonSerializer.SerializeToUtf8Bytes(project, RagJson.Options);
        if (bytes.Length > 20_000_000) throw new InvalidDataException("Proyek melebihi batas 20 MB.");
        path = Path.GetFullPath(path);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(temporary, bytes, token);
            token.ThrowIfCancellationRequested();
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public static async Task<RagProject> LoadAsync(string path, CancellationToken token)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > 20_000_000) throw new InvalidDataException("Proyek melebihi batas 20 MB.");
        var p = await JsonSerializer.DeserializeAsync<RagProject>(stream, RagJson.Options, token);
        if (p is null || p.Format != "csharp-endpoint-rag-project" || p.Version != 1 || p.Catalog?.Documents is null
            || p.Evidence is null || p.Knowledge is null || p.Options is null || p.ControllerOverview is null
            || p.Options.FolderApi is null || p.Options.ApiName is null || p.Options.Domain is null || p.Options.AssistantName is null || p.Options.Model is null
            || p.Catalog.SafetyNotes is null)
            throw new InvalidDataException("Format/versi proyek tidak didukung.");
        var ids = new HashSet<string>();
        foreach (var k in p.Knowledge)
            if (k is null || string.IsNullOrWhiteSpace(k.Id) || !ids.Add(k.Id) || k.Text is null || k.Source is null || k.Title is null || RagJson.Hash(k.Text) != k.ContentHash)
                throw new InvalidDataException("Knowledge proyek tidak valid.");
        var endpointIds = new HashSet<string>();
        foreach (var d in p.Catalog.Documents)
        {
            if (d is null || string.IsNullOrWhiteSpace(d.Id) || !endpointIds.Add(d.Id) || d.Endpoint is null || d.Job is null || d.Quality is null || d.Request is null
                || d.Intents is null || d.Keywords is null || d.Capabilities is null || d.OutputTypes is null || d.ImportantFields is null)
                throw new InvalidDataException("Endpoint proyek tidak valid.");
            var e = p.Evidence.SingleOrDefault(e => e is not null && e.Id == d.Id);
            if (e?.Source is null || e.Source.Schemas is null || e.Source.Warnings is null || e.SafeContext is null || e.ControllerSummary is null || e.ActionSummary is null
                || e.ExampleQuestions is null || e.MappingWarnings is null || e.RelatedKnowledgeIds is null)
                throw new InvalidDataException("Bukti endpoint proyek tidak lengkap.");
            d.Evidence = e.Source; d.Evidence.SafeContext = e.SafeContext; d.Selected = e.Selected;
            d.ControllerSummary = e.ControllerSummary; d.ActionSummary = e.ActionSummary;
            d.StaticOperation = e.StaticOperation; d.OperationEvidence = e.OperationEvidence;
            d.MappingStatus = e.MappingStatus; d.MappingModel = e.MappingModel;
            d.MappingWarnings = e.MappingWarnings; d.ExampleQuestions = e.ExampleQuestions;
            d.RelatedKnowledgeIds = e.RelatedKnowledgeIds.Where(ids.Contains).ToArray();
            d.RuntimeVerificationRequired = true; d.Job.BusinessMeaningVerified = false;
            if (d.StaticOperation == "write") d.Operation = "write";
            if (d.Operation is not ("read" or "write")) d.Operation = "unknown";
            if (d.Operation != "read" || d.AssistantExposure != "read" || d.Endpoint.Method is not ("GET" or "HEAD") || d.Scope == "unknown" || d.Evidence.Warnings.Length > 0
                || d.Quality.MissingSchemaIds.Length > 0 || d.Quality.UnresolvedSchemaIds.Length > 0 || d.Quality.DynamicSchemaIds.Length > 0)
                d.AssistantExposure = "restricted";
        }
        return p;
    }
}

// Deliberately excludes API keys and encrypted credentials.
public sealed class ProjectOptions
{
    public string FolderApi { get; set; } = "";
    public string ApiName { get; set; } = "my-api";
    public string Domain { get; set; } = "general";
    public string AssistantName { get; set; } = "API Assistant";
    public string Model { get; set; } = "gpt-4.1-mini";
    public int OperationFilter { get; set; }
    public bool SkipMapped { get; set; } = true;
    public bool ReadOnlySelection { get; set; }
}

public sealed class ProjectEvidence
{
    public string Id { get; set; } = "";
    public SourceEvidence Source { get; set; } = new();
    public string SafeContext { get; set; } = "";
    public bool Selected { get; set; }
    public string ControllerSummary { get; set; } = "";
    public string ActionSummary { get; set; } = "";
    public string StaticOperation { get; set; } = "unknown";
    public string OperationEvidence { get; set; } = "";
    public string MappingStatus { get; set; } = "static";
    public string? MappingModel { get; set; }
    public string[] MappingWarnings { get; set; } = [];
    public string[] ExampleQuestions { get; set; } = [];
    public string[] RelatedKnowledgeIds { get; set; } = [];
}
