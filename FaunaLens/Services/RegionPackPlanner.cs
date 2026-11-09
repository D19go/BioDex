using System.Globalization;
using System.Text;

namespace FaunaLens.Services;

// Estados brasileiros usados como região dos FaunaPacks. Puro (sem Android) para poder ser testado.
public static class BrazilRegions
{
    public const string WholeCountry = "Brasil inteiro";

    public static readonly string[] States =
    {
        "Acre", "Alagoas", "Amapá", "Amazonas", "Bahia", "Ceará", "Distrito Federal",
        "Espírito Santo", "Goiás", "Maranhão", "Mato Grosso", "Mato Grosso do Sul", "Minas Gerais", "Pará",
        "Paraíba", "Paraná", "Pernambuco", "Piauí", "Rio de Janeiro", "Rio Grande do Norte", "Rio Grande do Sul",
        "Rondônia", "Roraima", "Santa Catarina", "São Paulo", "Sergipe", "Tocantins"
    };

    // Opções do seletor de região: "Brasil inteiro" primeiro, depois os estados.
    public static readonly string[] SpinnerItems = new[] { WholeCountry }.Concat(States).ToArray();

    // Nome canônico do estado (ignora acento, caixa e espaços extras); null se não for um estado.
    public static string? Canonical(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;

        var key = Key(name);
        return States.FirstOrDefault(state => Key(state) == key);
    }

    public static bool Same(string? first, string? second)
    {
        var key = Key(first);
        return key.Length > 0 && key == Key(second);
    }

    static string Key(string? text)
    {
        var decomposed = (text ?? string.Empty).Trim().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
                continue;

            builder.Append(char.ToLowerInvariant(character));
        }

        return string.Join(" ", builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}

// Estado da detecção de região, guardado entre aberturas do app.
// LastRegion: último estado detectado. PendingRegion: estado cujo pack ainda precisa ser criado.
public sealed record RegionState(string LastRegion = "", string PendingRegion = "");

public sealed record RegionPlan(RegionState Next, string? StartRegion);

public static class RegionPackPlanner
{
    // Regras:
    // - a primeira detecção só registra o estado (não cria pack);
    // - mudança de estado marca a região como pendente;
    // - a pendência vira criação quando não há pack da região, nenhuma criação está em andamento
    //   e o BioCLIP está pronto; senão continua pendente para a próxima abertura;
    // - sem detecção (sem localização ou sem internet), vale o estado guardado.
    public static RegionPlan Plan(
        RegionState current,
        string? detectedRegion,
        IEnumerable<string> installedRegions,
        bool buildActive,
        bool modelReady)
    {
        var last = BrazilRegions.Canonical(current.LastRegion) ?? string.Empty;
        var pending = BrazilRegions.Canonical(current.PendingRegion) ?? string.Empty;

        var detected = BrazilRegions.Canonical(detectedRegion);
        if (detected is not null)
        {
            if (last.Length == 0)
            {
                last = detected;
            }
            else if (!BrazilRegions.Same(detected, last))
            {
                last = detected;
                pending = detected;
            }
        }

        if (pending.Length == 0)
            return new RegionPlan(new RegionState(last), null);

        if (installedRegions.Any(region => BrazilRegions.Same(region, pending)))
            return new RegionPlan(new RegionState(last), null);

        if (buildActive || !modelReady)
            return new RegionPlan(new RegionState(last, pending), null);

        return new RegionPlan(new RegionState(last), pending);
    }
}
