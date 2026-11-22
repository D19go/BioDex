using Android.App;
using Android.Content;
using FaunaLens.Models;

namespace FaunaLens.Services;

// Resultado de uma identificação. Awarded é a pontuação individual (já com a repetição); os bônus vêm à parte.
// PreviousRecords: quantas vezes a espécie já tinha sido identificada antes desta vez.
public sealed record ScoreOutcome(
    string SpeciesKey,
    SpeciesRisk Risk,
    int Awarded,
    int PreviousRecords,
    int Total,
    int FirstDiscovery = 0,
    int Collector = 0,
    int Explorer = 0,
    string Group = "",
    List<string>? NewHabitats = null)
{
    public int Earned => Awarded + FirstDiscovery + Collector + Explorer;
}

// Pontuação deste app, conforme o documento "Sistema de Pontuação" (versão 1.1).
// Espécies ameaçadas são registradas, mas não pontuam. Cada instalação grava a própria pontuação no placar compartilhado.
public sealed class ScoreStore
{
    const string PreferencesName = "faunalens_ranking";
    const string PointsKey = "points";
    const string CountsKey = "species_counts_v2";
    const string DiscoveredKey = "discovered_v1";       // linhas "espécie<TAB>grupo"
    const string CollectorKey = "collector_groups_v1";  // grupos que já receberam o bônus
    const string HabitatsKey = "explored_habitats_v1";  // habitats já explorados
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
        var threatened = risk == SpeciesRisk.Threatened;

        try
        {
            var preferences = Prefs();
            if (preferences is null)
                return new ScoreOutcome(key, risk, 0, 0, 0);

            var counts = ReadCounts(preferences);
            counts.TryGetValue(key, out var previous);
            var discovered = ReadPairs(preferences, DiscoveredKey);
            var collectorGroups = ReadSet(preferences, CollectorKey);
            var explored = ReadSet(preferences, HabitatsKey);

            // Pontuação individual e repetição.
            var individual = ScoringRules.Individual(
                Or(species.Rarity, ScoringRules.DefaultRarity),
                Or(species.Difficulty, ScoringRules.DefaultDifficulty),
                Or(species.Size, ScoringRules.DefaultSize),
                species.Conservation);
            var awarded = threatened ? 0 : ScoringRules.Repeat(individual, previous);

            // Bônus só na primeira identificação da espécie, e nunca para ameaçadas.
            var firstDiscovery = !threatened && previous == 0;
            var group = (species.Group ?? "").Trim();
            var firstBonus = firstDiscovery ? ScoringRules.FirstDiscoveryBonus : 0;

            var collector = 0;
            if (firstDiscovery && group.Length > 0)
            {
                var sameGroupBefore = discovered.Count(pair => pair.Value.Equals(group, StringComparison.OrdinalIgnoreCase));
                if (ScoringRules.CollectorEarned(sameGroupBefore, collectorGroups.Contains(group)))
                {
                    collector = ScoringRules.CollectorBonus;
                    collectorGroups.Add(group);
                }
            }

            var newHabitats = firstDiscovery
                ? ScoringRules.NewHabitats(ScoringRules.HabitatsFrom(species.Habitat), explored)
                : new List<string>();
            var explorer = newHabitats.Count > 0 ? ScoringRules.ExplorerBonus : 0;
            foreach (var habitat in newHabitats)
                explored.Add(habitat);

            if (firstDiscovery)
                discovered[key] = group;
            counts[key] = previous + 1;

            var earned = awarded + firstBonus + collector + explorer;
            var total = preferences.GetInt(PointsKey, 0) + earned;

            preferences.Edit()!
                .PutInt(PointsKey, total)!
                .PutString(CountsKey, WriteCounts(counts))!
                .PutString(DiscoveredKey, WritePairs(discovered))!
                .PutString(CollectorKey, string.Join("\n", collectorGroups))!
                .PutString(HabitatsKey, string.Join("\n", explored))!
                .Apply();

            SharedScoreboard.Publish(PackageId, DisplayName, total);
            return new ScoreOutcome(key, risk, awarded, previous, total, firstBonus, collector, explorer, group, newHabitats);
        }
        catch
        {
            return new ScoreOutcome(key, risk, 0, 0, TotalPoints);
        }
    }

    static int Or(int value, int fallback) => value > 0 ? value : fallback;

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

    // Formato: uma linha por espécie, "espécie<TAB>grupo".
    static Dictionary<string, string> ReadPairs(ISharedPreferences preferences, string key)
    {
        var pairs = new Dictionary<string, string>();
        var lines = (preferences.GetString(key, "") ?? "")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            var parts = line.Split('\t');
            if (parts.Length == 2)
                pairs[parts[0]] = parts[1];
        }

        return pairs;
    }

    static string WritePairs(Dictionary<string, string> pairs) =>
        string.Join("\n", pairs.Select(pair => $"{pair.Key}\t{pair.Value}"));

    static HashSet<string> ReadSet(ISharedPreferences preferences, string key) =>
        new((preferences.GetString(key, "") ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries));

    static ISharedPreferences? Prefs() =>
        Application.Context.GetSharedPreferences(PreferencesName, FileCreationMode.Private);
}
