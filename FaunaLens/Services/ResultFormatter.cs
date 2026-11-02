using FaunaLens.Models;
using System.Text;

namespace FaunaLens.Services;

public static class ResultFormatter
{
    // Valor inicial; precisa ser calibrado com fotos reais no aparelho
    // (fotos de espécies do pacote vs. fotos de outras coisas).
    public const float MinConfidentScore = 0.60f;

    public static string Format(IReadOnlyList<SpeciesMatch> matches)
    {
        if (matches.Count == 0) return "Nenhuma espécie com embedding disponível no pacote atual.";

        var best = matches[0];
        var sb = new StringBuilder();

        if (best.Score < MinConfidentScore)
        {
            sb.AppendLine("Não consegui reconhecer esse animal com confiança.");
            sb.AppendLine("Ele pode não estar no catálogo ativo, ou a foto pode estar distante/escura.");
            sb.AppendLine();
            sb.AppendLine("CANDIDATOS DE BAIXA CONFIANÇA");
            foreach (var m in matches) sb.AppendLine($"• {m.Species.CommonName}: {m.Score:P1}");
            return sb.ToString();
        }

        sb.AppendLine($"Provável identificação: {best.Species.CommonName}");
        if (!string.IsNullOrWhiteSpace(best.Species.ScientificName))
            sb.AppendLine(best.Species.ScientificName);
        sb.AppendLine($"Similaridade: {best.Score:P1}");

        var fields = new (string Label, string Value)[]
        {
            ("Altura", best.Species.Height),
            ("Peso", best.Species.Weight),
            ("Dieta", best.Species.Diet),
            ("Habitat", best.Species.Habitat)
        }.Where(x => !string.IsNullOrWhiteSpace(x.Value)).ToList();

        if (fields.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("FICHA");
            foreach (var (label, value) in fields) sb.AppendLine($"{label}: {value}");
        }

        if (!string.IsNullOrWhiteSpace(best.Species.Curiosities))
        {
            sb.AppendLine();
            sb.AppendLine("CURIOSIDADE");
            sb.AppendLine(best.Species.Curiosities);
        }

        if (matches.Count > 1)
        {
            sb.AppendLine();
            sb.AppendLine("OUTRAS POSSIBILIDADES");
            foreach (var m in matches.Skip(1)) sb.AppendLine($"• {m.Species.CommonName}: {m.Score:P1}");
        }

        return sb.ToString();
    }
}
