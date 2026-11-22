using System.Text.Json;
using Android.App;

namespace FaunaLens.Services;

public sealed class ScoreboardEntry
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public int Points { get; set; }
}

public sealed class ScoreboardFile
{
    public List<ScoreboardEntry> Entries { get; set; } = new();
}

// Placar compartilhado entre os apps clones no mesmo aparelho.
// Um único arquivo em Download/BioDex/placar.json, com uma entrada por instalação (pacote).
// Cada app só altera a própria entrada. Sem acesso a Download/BioDex, nada é lido nem gravado aqui.
public static class SharedScoreboard
{
    static string FilePath => Path.Combine(
        ModelLocation.RootDirectory(ModelStorage.PublicDownloadsDirectory),
        "placar.json");

    public static List<ScoreboardEntry> Read()
    {
        if (!ModelStorage.HasAccess(Application.Context))
            return new List<ScoreboardEntry>();

        try
        {
            if (!File.Exists(FilePath))
                return new List<ScoreboardEntry>();

            var file = JsonSerializer.Deserialize<ScoreboardFile>(File.ReadAllText(FilePath));
            return file?.Entries ?? new List<ScoreboardEntry>();
        }
        catch
        {
            return new List<ScoreboardEntry>();
        }
    }

    public static void Publish(string id, string name, int points)
    {
        if (string.IsNullOrWhiteSpace(id) || !ModelStorage.HasAccess(Application.Context))
            return;

        try
        {
            var entries = Read().Where(entry => entry.Id != id).ToList();
            entries.Add(new ScoreboardEntry { Id = id, Name = name, Points = points });

            var path = FilePath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            // Grava em arquivo temporário e troca no fim, para não deixar o placar pela metade.
            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(new ScoreboardFile { Entries = entries }));
            File.Move(temp, path, true);
        }
        catch
        {
            // Placar é best-effort: sem gravação, o app continua funcionando com a pontuação local.
        }
    }
}
