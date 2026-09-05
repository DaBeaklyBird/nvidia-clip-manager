using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClipManager;

public sealed class Settings
{
    public string ClipsFolder { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "NVIDIA");
    public string BackupFolder { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "NVIDIA Clip Backups");
    public bool ICloudEnabled { get; set; }
    public string ICloudFolder { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Pictures", "iCloud Photos", "Photos");
    public bool StartWithWindows { get; set; }
    public bool Watching { get; set; }
    public string RecordingTimeZone { get; set; } = TimeZoneInfo.Local.Id;
    public void Validate()
    {
        if (!Directory.Exists(ClipsFolder)) throw new IOException("Choose an existing clips folder.");
        if (string.IsNullOrWhiteSpace(BackupFolder) || Paths.Overlaps(ClipsFolder, BackupFolder))
            throw new IOException("Backups must be outside the clips folder, not a parent of it.");
        if (ICloudEnabled && (!Directory.Exists(ICloudFolder) || Paths.Overlaps(ClipsFolder, ICloudFolder) || Paths.Overlaps(BackupFolder, ICloudFolder)))
            throw new IOException("Choose your existing iCloud Photos folder, separate from clips and backups. Enable Photos in iCloud for Windows first.");
        _ = TimeZoneInfo.FindSystemTimeZoneById(RecordingTimeZone);
    }
}

public static class Paths
{
    public static bool Inside(string path, string root) => Path.GetFullPath(path).StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    public static bool Overlaps(string a, string b) => string.Equals(Path.GetFullPath(a).TrimEnd('\\','/'), Path.GetFullPath(b).TrimEnd('\\','/'), StringComparison.OrdinalIgnoreCase) || Inside(a,b) || Inside(b,a);
    public static void NoLinks(string path)
    {
        for (var p = Path.GetFullPath(path); p != null; p = Path.GetDirectoryName(p))
            if ((File.Exists(p) || Directory.Exists(p)) && (File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Symbolic links and junctions are not processed: " + p);
    }
    public static string CleanName(string input, bool repaired)
    {
        var stem = Path.GetFileNameWithoutExtension(input);
        if (stem.EndsWith(".DVR", StringComparison.OrdinalIgnoreCase)) stem = stem[..^4];
        if (repaired && !stem.EndsWith(" repaired", StringComparison.OrdinalIgnoreCase)) stem += " repaired";
        return stem + ".mp4";
    }
    public static string Available(string proposed)
    {
        var candidate = proposed;
        for (int i = 2; File.Exists(candidate); i++)
            candidate = Path.Combine(Path.GetDirectoryName(proposed)!, $"{Path.GetFileNameWithoutExtension(proposed)} ({i}).mp4");
        return candidate;
    }
}

public sealed class Job
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Source { get; set; } = "";
    public string Output { get; set; } = "";
    public string Backup { get; set; } = "";
    public string Temporary { get; set; } = "";
    public string SourceHash { get; set; } = "";
    public string OutputHash { get; set; } = "";
    public string State { get; set; } = "Queued";
    public string Detail { get; set; } = "";
    public string CloudState { get; set; } = "Not requested";
    public string CloudPath { get; set; } = "";
    public bool CloudRequested { get; set; }
    public bool Repaired { get; set; }
    public DateTimeOffset? RecordedAt { get; set; }
    public DateTimeOffset Updated { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class Store
{
    public static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
    public string Root { get; }
    public Store(string root) { Root = root; Directory.CreateDirectory(Path.Combine(root,"jobs")); }
    public Settings LoadSettings() => Load<Settings>(Path.Combine(Root,"settings.json")) ?? new();
    public void SaveSettings(Settings settings) => Atomic(Path.Combine(Root,"settings.json"), settings);
    public void Save(Job job) { job.Updated = DateTimeOffset.UtcNow; Atomic(Path.Combine(Root,"jobs",job.Id+".json"),job); }
    public List<Job> Jobs() => Directory.EnumerateFiles(Path.Combine(Root,"jobs"),"*.json").Select(Load<Job>).OfType<Job>().OrderByDescending(x=>x.Updated).ToList();
    private static T? Load<T>(string path) { if (!File.Exists(path)) return default; return JsonSerializer.Deserialize<T>(File.ReadAllText(path)); }
    public static void Atomic<T>(string path,T item)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path+".new";
        using (var stream = new FileStream(temp,FileMode.Create,FileAccess.Write,FileShare.None)) { JsonSerializer.Serialize(stream,item,Json); stream.Flush(true); }
        File.Move(temp,path,true);
    }
}
