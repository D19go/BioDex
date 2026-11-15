using Android.App;
using Android.Content;
using FaunaLens.Models;

namespace FaunaLens.Services;

// PreviousRecords: quantas vezes a espécie já tinha sido registrada antes desta identificação.
public sealed record ScoreOutcome(string SpeciesKey, SpeciesRisk Risk, int Awarded, int PreviousRecords, int Total);

// Pontuação local do usuário (prévia do ranking). Repetições pontuam cada vez menos;
// faixa 1 e ameaçadas são registradas, mas não pontuam.
public sealed class ScoreStore
{
    const string PreferencesName = "faunalens_ranking";
    const string PointsKey = "points";
    const string CountsKey = "species_counts_v2";

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
