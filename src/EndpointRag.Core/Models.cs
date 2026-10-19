using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EndpointRag.Core;

public static class RagJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);
    public static string Hash(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}

public sealed class KnowledgeCatalog
{
    public string Generator { get; set; } = "csharp-endpoint-rag";
    public string SchemaVersion { get; set; } = "1.0";
    public string SourceFile { get; set; } = "C# controller source";
    public DateTimeOffset SourceGeneratedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset SourceSnapshotAt { get; set; } = DateTimeOffset.UtcNow;
    public int DocumentCount => Documents.Count;
    public string[] SafetyNotes { get; set; } = [
        "This catalog is retrieval/reference data, not authorization to execute an endpoint.",
        "Always enforce authenticated user scope and backend authorization.",
        "Restricted or unverified endpoints require human review before tool selection.",
        "Runtime verification is required: source metadata does not prove deployment.",
        "AI business mapping is inferred and is not a verified business contract."
    ];
    public List<EndpointDocument> Documents { get; set; } = [];
}

public sealed class EndpointDocument
{
    public string Id { get; set; } = "";
    public string Api { get; set; } = "";
    public string Label { get; set; } = "";
    public string Domain { get; set; } = "unknown";
    public string Module { get; set; } = "unknown";
    public string[] Intents { get; set; } = [];
    public string Scope { get; set; } = "unknown";
    public string Operation { get; set; } = "unknown";
    public string AssistantExposure { get; set; } = "restricted";
    public EndpointContract Endpoint { get; set; } = new();
    public EndpointJob Job { get; set; } = new();
    public List<RequestParameter> Request { get; set; } = [];
    public string[] OutputTypes { get; set; } = [];
    public string[] ImportantFields { get; set; } = [];
    public string[] Capabilities { get; set; } = [];
    public string[] Keywords { get; set; } = [];
    public string RagInstruction { get; set; } = "";
    public bool RuntimeVerificationRequired { get; set; } = true;
    public DocumentQuality Quality { get; set; } = new();
    // Supplementary information lives in provenance.json / RAG chunks, preserving the supplied JSON shape.
    [JsonIgnore] public SourceEvidence Evidence { get; set; } = new();
    [JsonIgnore] public string[] ExampleQuestions { get; set; } = [];
    [JsonIgnore] public string[] RelatedKnowledgeIds { get; set; } = [];
    [JsonIgnore] public string[] MappingWarnings { get; set; } = [];
    [JsonIgnore] public string MappingStatus { get; set; } = "static";
    [JsonIgnore] public string ControllerSummary { get; set; } = "";
    [JsonIgnore] public string ActionSummary { get; set; } = "";
    [JsonIgnore] public string? MappingModel { get; set; }
    [JsonIgnore] public string StaticOperation { get; set; } = "unknown";
    [JsonIgnore] public string OperationEvidence { get; set; } = "";
    [JsonIgnore] public bool Selected { get; set; } = true;
}

public sealed class EndpointContract
{
    public string Method { get; set; } = "";
    public string Path { get; set; } = "";
    public string Controller { get; set; } = "";
    public string Action { get; set; } = "";
    public string AuthSource { get; set; } = "Not proven by source";
}
public sealed class EndpointJob
{
    public string Description { get; set; } = "";
    public string JobType { get; set; } = "unknown";
    public bool IsReport { get; set; }
    public bool BusinessMeaningVerified { get; set; }
}
public sealed record RequestParameter(string Name, string Binding, string Type, bool Nullable);
public sealed class DocumentQuality
{
    public string[] MissingSchemaIds { get; set; } = [];
    public string[] UnresolvedSchemaIds { get; set; } = [];
    public string[] DynamicSchemaIds { get; set; } = [];
    public bool RagTextTruncated { get; set; }
}
public sealed class SourceEvidence
{
    public string File { get; set; } = "";
    public int Line { get; set; }
    public string ContentHash { get; set; } = "";
    public string[] Warnings { get; set; } = [];
    [JsonIgnore] public string SafeContext { get; set; } = "";
    public Dictionary<string, string[]> Schemas { get; set; } = [];
}
public sealed record ControllerOverview(string Controller, string Summary, string File, int Line, int EndpointCount);
public sealed record ScanResult(KnowledgeCatalog Catalog, string[] Warnings, int SourceFileCount, IReadOnlyList<ControllerOverview> Controllers);
public sealed record ScanProgress(int Completed, int Total, string Message);
public sealed record KnowledgeChunk(string Id, string Source, string Title, string Text, string ContentHash);
public sealed record RagChunk(string Id, string Kind, string Source, string Title, string Text,
    string[] Keywords, string[] EndpointIds, string[] RelatedKnowledgeIds, string ContentHash);

public sealed class AiMapping
{
    public string Label { get; set; } = "";
    public string Domain { get; set; } = "unknown";
    public string Module { get; set; } = "unknown";
    public string Scope { get; set; } = "unknown";
    public string Operation { get; set; } = "unknown";
    public string Description { get; set; } = "";
    public bool IsReport { get; set; }
    public string[] Intents { get; set; } = [];
    public string[] Capabilities { get; set; } = [];
    public string[] Keywords { get; set; } = [];
    public string[] ExampleQuestions { get; set; } = [];
    public string[] RelatedKnowledgeIds { get; set; } = [];
    public string[] Warnings { get; set; } = [];
}
