using System.Text.RegularExpressions;

namespace EndpointRag.Core;

public static class KnowledgeFiles
{
    public static IReadOnlyList<KnowledgeChunk> Read(IEnumerable<string> files)
    {
        var chunks = new List<KnowledgeChunk>();
        var total = 0;
        foreach (var file in files.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (Path.GetExtension(file).ToLowerInvariant() is not (".md" or ".txt")) throw new InvalidOperationException("Knowledge harus berupa .md atau .txt.");
            var info = new FileInfo(file);
            if (info.Length > 1_000_000) throw new InvalidOperationException("File knowledge maksimal 1 MB.");
            var text = File.ReadAllText(file);
            total += text.Length;
            if (total > 2_000_000) throw new InvalidOperationException("Total knowledge maksimal 2 juta karakter.");
            // Existing HR CHUNK blocks and plain-text numbered sections are preserved as independent knowledge.
            var sections = Regex.Split(text, @"(?m)(?=^#{1,3}\s+(?:CHUNK|\d+\.)\s)|\r?\n={10,}\r?\n");
            foreach (var section in sections.Where(s => !string.IsNullOrWhiteSpace(s)))
            {
                var clean = section.Trim();
                var title = clean.Split('\n').First().Trim('#', ' ', '\r');
                for (var start = 0; start < clean.Length; start += 5500)
                {
                    var block = clean.Substring(start, Math.Min(6000, clean.Length - start));
                    var hash = RagJson.Hash(block);
                    chunks.Add(new("knowledge-" + RagJson.Hash(Path.GetFileName(file) + "|" + hash)[..24], Path.GetFileName(file), title, block, hash));
                    if (start + 6000 >= clean.Length) break;
                }
            }
        }
        return chunks.GroupBy(c => c.Id).Select(g => g.First()).ToArray();
    }

    public static KnowledgeChunk[] Candidates(EndpointDocument doc, IReadOnlyList<KnowledgeChunk> knowledge)
    {
        var words = Words(string.Join(' ', doc.Label, doc.Endpoint.Controller, doc.Endpoint.Action, doc.Endpoint.Path,
            doc.ControllerSummary, doc.ActionSummary, doc.Job.Description, string.Join(" ", doc.Intents),
            string.Join(" ", doc.Capabilities), string.Join(" ", doc.ImportantFields), string.Join(" ", doc.Keywords)));
        // Zero-overlap chunks used to be sent merely because they sorted into the first six. That made the
        // external model see unrelated policy and encouraged invented relationships.
        return knowledge.Select(k => (Chunk: k, Score: Words(k.Source + " " + k.Title + " " + k.Text).Intersect(words).Count()))
            .Where(x => x.Score > 0).OrderByDescending(x => x.Score).ThenBy(x => x.Chunk.Id)
            .Take(6).Select(x => x.Chunk).ToArray();
    }
    internal static HashSet<string> Words(string value) => Regex.Matches(value.ToLowerInvariant(), @"[\p{L}\p{N}]{3,}")
        .Select(m => Canonical(m.Value)).Where(word => !StopWords.Contains(word)).ToHashSet();

    private static string Canonical(string word) => word switch
    {
        "ijin" or "izin" or "perijinan" or "perizinan" or "cuti" or "leave" or "absence" or "unattendance" => "leave",
        "approval" or "approve" or "approved" or "persetujuan" or "menyetujui" or "disetujui" => "approval",
        "cancel" or "cancellation" or "pembatalan" or "membatalkan" => "cancel",
        "employee" or "employees" or "karyawan" or "pegawai" => "employee",
        "schedule" or "scheduling" or "jadwal" or "shift" => "schedule",
        "attendance" or "kehadiran" or "absensi" => "attendance",
        "overtime" or "lembur" => "overtime",
        _ => word
    };
    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    { "yang", "dan", "atau", "untuk", "dengan", "dari", "pada", "dalam", "oleh", "adalah", "the", "and", "for", "with", "from", "this", "that",
      "read", "get", "data", "source", "result", "response", "controller", "action" };
}
