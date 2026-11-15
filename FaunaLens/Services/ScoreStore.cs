using Android.App;
using Android.Content;
using FaunaLens.Models;

namespace FaunaLens.Services;

public sealed record ScoreOutcome(string SpeciesKey, SpeciesRisk Risk, int Awarded, bool AlreadyScored, int Total);

// Pontuação local do usuário (prévia do ranking). Cada espécie pontua uma vez;
// faixa 1 e ameaçadas são registradas, mas não pontuam.
public sealed class ScoreStore
{
    const string PreferencesName = "faunalens_ranking";
    const string PointsKey = "points";
    const string ScoredKey = "scored_species_v1";

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
                return new ScoreOutcome(key, risk, 0, false, 0);

            var scored = (preferences.GetString(ScoredKey, "") ?? "")
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var total = preferences.GetInt(PointsKey, 0);
            if (scored.Contains(key))
                return new ScoreOutcome(key, risk, 0, true, total);

            var awarded = ScoringPolicy.PointsFor(risk);
            scored.Add(key);
            total += awarded;

            preferences.Edit()!
                .PutInt(PointsKey, total)!
                .PutString(ScoredKey, string.Join("\n", scored))!
                .Apply();

            return new ScoreOutcome(key, risk, awarded, false, total);
        }
        catch
        {
            return new ScoreOutcome(key, risk, 0, false, TotalPoints);
        }
    }

    static ISharedPreferences? Prefs() =>
        Application.Context.GetSharedPreferences(PreferencesName, FileCreationMode.Private);
}
