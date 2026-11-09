using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FaunaLens.Services;

// Pedido de criação de FaunaPack. Com UpdatePackId, o pack existente é refeito com o mesmo id.
// Sem dependências do Android, para que os testes consigam compilar este arquivo.
public sealed class PackBuildOptions
{
    public const int MinImagesPerSpecies = 1;
    public const int MaxImagesPerSpecies = 50;

    public string PackName { get; set; } = "";
    public string StateProvince { get; set; } = "";
    public int MaxSpecies { get; set; } = 50;
    public int ImagesPerSpecies { get; set; } = 2;
    public int SearchPages { get; set; } = 10;
    public bool AllowBySa { get; set; } = true;
    public string? UpdatePackId { get; set; }

    // Mantém os valores dentro dos limites suportados, mesmo que a tela envie algo fora deles.
    public PackBuildOptions Normalized() => new()
    {
        PackName = (PackName ?? "").Trim(),
        StateProvince = (StateProvince ?? "").Trim(),
        MaxSpecies = Math.Clamp(MaxSpecies, 1, 500),
        ImagesPerSpecies = Math.Clamp(ImagesPerSpecies, MinImagesPerSpecies, MaxImagesPerSpecies),
        SearchPages = Math.Clamp(SearchPages, 1, 30),
        AllowBySa = AllowBySa,
        UpdatePackId = string.IsNullOrWhiteSpace(UpdatePackId) ? null : UpdatePackId.Trim()
    };
}

// Estado da criação de packs. Fica em arquivo para que a tela acompanhe uma criação que roda em
// segundo plano, mesmo depois de ser fechada e reaberta.
public sealed class PackBuildState
{
    public const int MaxLogLines = 300;

    // Sem atualização (heartbeat) por este tempo, o serviço provavelmente foi encerrado pelo sistema.
    public static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(90);

    // idle, queued, running, completed, failed, cancelled
    public string Status { get; set; } = "idle";
    public int Percent { get; set; }
    public string PackName { get; set; } = "";
    public List<string> Log { get; set; } = new();
    public string? Error { get; set; }
    public string? ResultPackId { get; set; }
    public int ResultSpeciesCount { get; set; }
    public PackBuildOptions? Request { get; set; }
    public bool Acknowledged { get; set; }
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

    [JsonIgnore]
    public bool IsFinished => Status is "completed" or "failed" or "cancelled";

    public bool IsActive(DateTime utcNow) =>
        (Status is "queued" or "running") && utcNow - UpdatedUtc < StaleAfter;

    public void AddLog(string line)
    {
        Log.Add($"{DateTime.Now:HH:mm:ss}  {line}");
        if (Log.Count > MaxLogLines)
            Log.RemoveRange(0, Log.Count - MaxLogLines);
    }
}

public static class PackBuildStateStore
{
    static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static PackBuildState Load(string path)
    {
        try
        {
            if (!File.Exists(path))
                return new PackBuildState();

            return JsonSerializer.Deserialize<PackBuildState>(File.ReadAllText(path), Options) ?? new PackBuildState();
        }
        catch
        {
            return new PackBuildState();
        }
    }

    public static void Save(string path, PackBuildState state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(state, Options), new UTF8Encoding(false));
        File.Move(temporary, path, true);
    }

    // Marca o resultado como já exibido na tela principal, para não mostrar de novo.
    public static void Acknowledge(string path)
    {
        var state = Load(path);
        state.Acknowledged = true;
        Save(path, state);
    }
}
