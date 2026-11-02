using FaunaLens.Models;
using System.Text;

namespace FaunaLens.Services;

public static class ResultFormatter
{
    public static string Format(IReadOnlyList<SpeciesMatch> matches)
    {
        if (matches.Count == 0) return "Nenhuma espécie com embedding disponível no pacote atual.";
        var best = matches[0];
        var sb = new StringBuilder();
        sb.AppendLine($"Provável identificação: {best.Species.CommonName}");
        sb.AppendLine(best.Species.ScientificName);
        sb.AppendLine($"Similaridade: {best.Score:P1}");
        sb.AppendLine();
        sb.AppendLine("FICHA");
        sb.AppendLine($"Altura: {best.Species.Height}");
        sb.AppendLine($"Peso: {best.Species.Weight}");
        sb.AppendLine($"Dieta: {best.Species.Diet}");
        sb.AppendLine($"Habitat: {best.Species.Habitat}");
        sb.AppendLine();
        sb.AppendLine("CURIOSIDADE");
        sb.AppendLine(best.Species.Curiosities);
        if (matches.Count > 1)
        {
            sb.AppendLine();
            sb.AppendLine("OUTRAS POSSIBILIDADES");
            foreach (var m in matches.Skip(1)) sb.AppendLine($"• {m.Species.CommonName}: {m.Score:P1}");
        }
        return sb.ToString();
    }
}
