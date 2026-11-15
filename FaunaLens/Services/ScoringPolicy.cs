namespace FaunaLens.Services;

public enum SpeciesRisk
{
    Normal,
    Attention,
    Dangerous,
    Threatened
}

// PROVISÓRIO: a tabela de pontos e as listas de risco ainda não foram definidas.
// Valores e gêneros abaixo servem só para a prévia do ranking e devem ser substituídos.
public static class ScoringPolicy
{
    public const int PlaceholderNormalPoints = 10;
    public const int PlaceholderAttentionPoints = 20;

    // Faixa 1 provisória (por gênero). Registro e aviso, sem pontos.
    static readonly string[] DangerousGenera =
    {
        "Panthera", "Puma", "Crocodylus", "Caiman", "Alligator",
        "Bothrops", "Crotalus", "Lachesis", "Micrurus",
        "Phoneutria", "Loxosceles", "Tityus",
        "Potamotrygon", "Carcharhinus", "Sphyrna"
    };

    // Lista ICMBio ainda não integrada: nenhuma espécie é marcada como ameaçada por enquanto.
    static readonly HashSet<string> ThreatenedNames = new(StringComparer.OrdinalIgnoreCase);

    public static SpeciesRisk Classify(string? scientificName)
    {
        var name = scientificName?.Trim() ?? "";
        if (name.Length == 0)
            return SpeciesRisk.Normal;

        if (ThreatenedNames.Contains(name))
            return SpeciesRisk.Threatened;

        var genus = name.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        return DangerousGenera.Contains(genus, StringComparer.OrdinalIgnoreCase)
            ? SpeciesRisk.Dangerous
            : SpeciesRisk.Normal;
    }

    // Faixa 1 e ameaçada nunca pontuam, em nenhuma situação.
    public static int PointsFor(SpeciesRisk risk) => risk switch
    {
        SpeciesRisk.Normal => PlaceholderNormalPoints,
        SpeciesRisk.Attention => PlaceholderAttentionPoints,
        _ => 0
    };

    // PROVISÓRIO: cada repetição tira RepeatStepPoints, sem descer abaixo de MinimumPoints.
    // Ex.: comum 10, 8, 6, 5, 5... Pode virar uma porcentagem fixa do valor base depois.
    public const int RepeatStepPoints = 2;
    public const int MinimumPoints = 5;

    // previousRecords = quantas vezes a espécie já foi registrada antes desta vez (0 = primeiro registro).
    public static int PointsForRepeat(SpeciesRisk risk, int previousRecords)
    {
        var basePoints = PointsFor(risk);
        if (basePoints == 0)
            return 0;

        var points = basePoints - RepeatStepPoints * Math.Max(0, previousRecords);
        return Math.Max(MinimumPoints, points);
    }
}
