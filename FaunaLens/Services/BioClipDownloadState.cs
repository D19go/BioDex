using System.Text.Json;
using System.Text.Encodings.Web;

namespace FaunaLens.Services;

public sealed class BioClipDownloadState
{
    public string Status { get; set; } = "idle";
    public int Percent { get; set; }
    public long DownloadedBytes { get; set; }
    public long TotalBytes { get; set; }
    public string? Error { get; set; }
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

    public bool IsActive => Status is "downloading" or "resuming";

    public static BioClipDownloadState Load()
    {
        try
        {
            if (!File.Exists(FileSystem.ModelDownloadStatePath))
                return new BioClipDownloadState();

            var json = File.ReadAllText(FileSystem.ModelDownloadStatePath);
            return JsonSerializer.Deserialize<BioClipDownloadState>(json) ?? new BioClipDownloadState();
        }
        catch
        {
            return new BioClipDownloadState();
        }
    }

    public static void Save(BioClipDownloadState state)
    {
        Directory.CreateDirectory(FileSystem.ModelDirectory);
        var temporaryPath = FileSystem.ModelDownloadStatePath + ".tmp";
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
        var json = JsonSerializer.Serialize(state, options);
        File.WriteAllText(temporaryPath, json, new System.Text.UTF8Encoding(false));
        File.Move(temporaryPath, FileSystem.ModelDownloadStatePath, true);
    }
}
