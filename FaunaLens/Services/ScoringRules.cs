using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace FaunaLens.Services;

// Regras do documento "Sistema de Pontuação" (versão 1.1). Puro, sem Android, para ser testado.
public static class ScoringRules
{
    public const int BasePoints = 15;
    public const int MinIndividual = 22; // 15 + 5 + 1 + 1 + 0
    public const int MaxIndividual = 65; // 15 + 20 + 20 + 5 + 5

    // Repetição: desconta 5 por identificação, sem passar de 80% da pontuação individual (arredondado para cima).
    public const int RepeatStep = 5;
    public const int RepeatFloorPercent = 80;

    public const int FirstDiscoveryBonus = 10;
    public const int CollectorBonus = 15;
    public const int ExplorerBonus = 5;

    // Valores padrão para espécies sem nota no pack (packs antigos ou curadoria pendente).
    public const int DefaultRarity = 10;
    public const int DefaultDifficulty = 10;
    public const int DefaultSize = 3;

    // Critérios mostrados no resultado da identificação. Os números vêm das constantes acima,
    // para o texto não ficar desatualizado em relação ao cálculo.
    public static IReadOnlyList<string> CriteriaLines() => new[]
    {
        $"Individual: {BasePoints} base + raridade (5 a 20) + dificuldade de encontrar (1 a 20) + tamanho (1 a 5) + conservação (0 a 5). Total entre {MinIndividual} e {MaxIndividual}.",
        $"Repetição: −{RepeatStep} a cada registro anterior da mesma espécie, sem passar de {RepeatFloorPercent}% da pontuação individual.",
        $"Primeira descoberta: +{FirstDiscoveryBonus} na primeira vez que você registra a espécie.",
        $"Colecionador: +{CollectorBonus} ao registrar a segunda espécie distinta de um grupo (uma vez por grupo).",
        $"Explorador: +{ExplorerBonus} por habitat novo (floresta, savana, aquático etc.).",
        "Espécie perigosa: recebe pontos e aviso.",
        "Espécie ameaçada: não recebe pontos."
    };

    public static int Individual(int rarity, int difficulty, int size, int conservation)
    {
        var total = BasePoints
            + Math.Clamp(rarity, 5, 20)
            + Math.Clamp(difficulty, 1, 20)
            + Math.Clamp(size, 1, 5)
            + Math.Clamp(conservation, 0, 5);
        return Math.Clamp(total, MinIndividual, MaxIndividual);
    }

    // previousRecords: quantas vezes a espécie já foi identificada antes desta vez (0 = primeira vez).
    public static int Repeat(int individual, int previousRecords)
    {
        if (previousRecords <= 0)
            return individual;

        // ceil(individual * 0,8) em inteiros.
        var floor = (individual * RepeatFloorPercent + 99) / 100;
        return Math.Max(floor, individual - RepeatStep * previousRecords);
    }

    // Colecionador: ao identificar mais um animal do mesmo grupo (a segunda espécie distinta), uma vez por grupo.
    public static bool CollectorEarned(int sameGroupBefore, bool alreadyGranted) =>
        sameGroupBefore >= 1 && !alreadyGranted;

    // Explorador: habitats ainda não explorados pelo usuário.
    public static List<string> NewHabitats(IEnumerable<string> habitats, ISet<string> explored) =>
        habitats.Where(habitat => !explored.Contains(habitat)).Distinct().ToList();

    // Habitats do documento, por palavra-chave no texto de habitat da espécie. Provisório até a curadoria do catálogo.
    static readonly (string Name, string Pattern)[] HabitatPatterns =
    {
        ("Floresta", @"\b(floresta|florestas|florestal|mata|matas)\b"),
        ("Savana", @"\b(savana|savanas|cerrado|cerrados)\b"),
        ("Deserto", @"\b(deserto|desertos)\b"),
        ("Aquático", @"\b(aquatic[oa]s?|marinh[oa]s?|oceanos?|rios?|lagos?|aguas?)\b"),
        ("Subterrâneo", @"\b(subterraneo|subterraneos|subterranea|subterraneas|caverna|cavernas)\b"),
        ("Arborícola", @"\b(arboric[oa]s?|arvores?)\b")
    };

    public static List<string> HabitatsFrom(string? text)
    {
        var normalized = RemoveAccents(text ?? "").ToLowerInvariant();
        return HabitatPatterns
            .Where(habitat => Regex.IsMatch(normalized, habitat.Pattern))
            .Select(habitat => habitat.Name)
            .ToList();
    }

    static string RemoveAccents(string text)
    {
        var decomposed = text.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
                builder.Append(character);
        }
        return builder.ToString();
    }
}
