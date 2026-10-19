using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EndpointRag.WinForms;

public sealed class AppSettings
{
    public int Version { get; set; } = 1;
    public string FolderApi { get; set; } = "";
    public string ApiName { get; set; } = "my-api";
    public string Domain { get; set; } = "general";
    public string AssistantName { get; set; } = "API Assistant";
    public string Model { get; set; } = "gpt-4.1-mini";
    public int OperationFilter { get; set; }
    public bool SkipMapped { get; set; } = true;
    public bool ReadOnlySelection { get; set; }
    public string ExportFolder { get; set; } = "";
    public string[] KnowledgeFiles { get; set; } = [];
    public string ProtectedApiKey { get; set; } = "";
    [JsonIgnore] public string ApiKey { get; set; } = "";
}

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true, MaxDepth = 16 };
    public string FilePath { get; }
    public SettingsStore() : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CSharpEndpointRag", "settings.json")) { }
    internal SettingsStore(string path) => FilePath = Path.GetFullPath(path);

    public AppSettings Load(out string? warning)
    {
        warning = null;
        if (!File.Exists(FilePath)) return new();
        AppSettings settings;
        try
        {
            if (new FileInfo(FilePath).Length > 64_000) throw new InvalidDataException();
            settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), JsonOptions) ?? throw new InvalidDataException();
            if (settings.Version != 1) throw new InvalidDataException();
            settings.FolderApi ??= ""; settings.ApiName ??= "my-api"; settings.Domain ??= "general";
            settings.AssistantName ??= "API Assistant"; settings.Model ??= "gpt-4.1-mini";
            settings.ExportFolder ??= ""; settings.KnowledgeFiles ??= []; settings.ProtectedApiKey ??= "";
            settings.OperationFilter = Math.Clamp(settings.OperationFilter, 0, 3);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        { warning = "Setting terakhir tidak dapat dibaca; aplikasi memakai nilai default."; return new(); }
        if (settings.ProtectedApiKey.Length == 0) return settings;
        byte[]? plain = null;
        try
        {
            if (settings.ProtectedApiKey.Length > 8000) throw new CryptographicException();
            plain = ProtectedData.Unprotect(Convert.FromBase64String(settings.ProtectedApiKey), null, DataProtectionScope.CurrentUser);
            settings.ApiKey = Encoding.UTF8.GetString(plain);
            if (settings.ApiKey.Length > 512 || settings.ApiKey.Any(char.IsControl)) throw new CryptographicException();
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        { settings.ApiKey = ""; warning = "API key tersimpan tidak dapat dibuka pada akun Windows ini. Masukkan key kembali."; }
        finally { if (plain is not null) CryptographicOperations.ZeroMemory(plain); }
        return settings;
    }

    public void Save(AppSettings settings)
    {
        if (settings.ApiKey.Length > 512 || settings.ApiKey.Any(char.IsControl)) throw new InvalidDataException("API key tidak valid untuk disimpan.");
        byte[]? plain = null;
        try
        {
            plain = Encoding.UTF8.GetBytes(settings.ApiKey);
            settings.ProtectedApiKey = plain.Length == 0 ? "" : Convert.ToBase64String(ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser));
        }
        finally { if (plain is not null) CryptographicOperations.ZeroMemory(plain); }
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        if (Encoding.UTF8.GetByteCount(json) > 64_000) throw new InvalidDataException("Setting terlalu besar untuk disimpan.");
        var directory = Path.GetDirectoryName(FilePath)!;
        Directory.CreateDirectory(directory);
        var staging = Path.Combine(directory, ".settings-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            File.WriteAllText(staging, json, new UTF8Encoding(false));
            File.Move(staging, FilePath, true);
        }
        finally { if (File.Exists(staging)) File.Delete(staging); }
    }
}
