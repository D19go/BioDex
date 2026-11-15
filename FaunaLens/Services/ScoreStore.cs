using Android.App;
using Android.Content;
using FaunaLens.Models;

namespace FaunaLens.Services;

// PreviousRecords: quantas vezes a espécie já tinha sido registrada antes desta identificação.
public sealed record ScoreOutcome(string SpeciesKey, SpeciesRisk Risk, int Awarded, int PreviousRecords, int Total);

// Pontuação deste app (prévia do ranking). Repetições pontuam cada vez menos;
// faixa 1 e ameaçadas são registradas, mas não pontuam.
// Cada instalação grava a própria pontuação no placar compartilhado (SharedScoreboard), quando há acesso.
public sealed class ScoreStore
{
    const string PreferencesName = "faunalens_ranking";
    const string PointsKey = "points";
    const string CountsKey = "species_counts_v2";
    const string NameKey = "display_name";
    const string DefaultName = "Jogador";

    public static int TotalPoints
    {
        get
        {
            try
            {
                return Prefs()?.GetInt(PointsKey, 0) ?? 0;
            }
            catch
            {
                return 0;
            }
        }
    }

    // Identifica esta instalação no placar: o pacote é único em cada clone (com.dsc.faunalens, ...1, ...2, ...3).
    public static string PackageId => Application.Context.PackageName ?? "";

    public static string DisplayName
    {
        get
        {
            try
            {
                var stored = ScoringPolicy.CleanName(Prefs()?.GetString(NameKey, null));
                return stored.Length > 0 ? stored : DefaultName;
            }
            catch
            {
                return DefaultName;
            }
        }
    }

    public static void SetDisplayName(string name)
    {
        var clean = ScoringPolicy.CleanName(name);
        if (clean.Length == 0)
            return;

        try
        {
            Prefs()?.Edit()?.PutString(NameKey, clean)?.Apply();
        }
        catch
        {
            // Sem preferências, o nome volta ao padrão na próxima abertura.
        }

        Sync();
    }

    // Garante que esta instalação aparece no placar compartilhado, mesmo sem pontos ainda.
    public static void Sync() => SharedScoreboard.Publish(PackageId, DisplayName, TotalPoints);

    public ScoreOutcome Record(Species species)
    {
        var key = string.IsNullOrWhiteSpace(species.ScientificName) ? species.CommonName : species.ScientificName;
        var risk = ScoringPolicy.Classify(species.ScientificName);

        try
        {
            var preferences = Prefs();
            if (preferences is null)
                return new ScoreOutcome(key, risk, 0, 0, 0);

            var counts = ReadCounts(preferences);
            counts.TryGetValue(key, out var previous);

            var awarded = ScoringPolicy.PointsForRepeat(risk, previous);
            var total = preferences.GetInt(PointsKey, 0) + awarded;
            counts[key] = previous + 1;

            preferences.Edit()!
                .PutInt(PointsKey, total)!
                .PutString(CountsKey, WriteCounts(counts))!
                .Apply();

            SharedScoreboard.Publish(PackageId, DisplayName, total);
            return new ScoreOutcome(key, risk, awarded, previous, total);
        }
        catch
        {
            return new ScoreOutcome(key, risk, 0, 0, TotalPoints);
        }
    }

    // Formato: uma linha por espécie, "nome<TAB>contagem".
    static Dictionary<string, int> ReadCounts(ISharedPreferences preferences)
    {
        var counts = new Dictionary<string, int>();
        var lines = (preferences.GetString(CountsKey, "") ?? "")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            var parts = line.Split('\t');
            if (parts.Length == 2 && int.TryParse(parts[1], out var count))
                counts[parts[0]] = count;
        }

        return counts;
    }

    static string WriteCounts(Dictionary<string, int> counts) =>
        string.Join("\n", counts.Select(pair => $"{pair.Key}\t{pair.Value}"));

    static ISharedPreferences? Prefs() =>
        Application.Context.GetSharedPreferences(PreferencesName, FileCreationMode.Private);
}
