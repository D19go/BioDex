using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace FaunaPackBuilder;

public sealed class GbifClient : IDisposable
{
    const string ApiRoot = "https://api.gbif.org/v1";
    readonly HttpClient client;
    readonly JsonSerializerOptions json = new(JsonSerializerDefaults.Web);

    public GbifClient()
    {
        client = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("FaunaPackBuilder/1.2 (Fauna Lens)");
    }

    public async Task<List<GbifOccurrence>> SearchImagesAsync(
        string scientificName,
        string countryCode,
        string stateProvince,
        int maxPages,
        CancellationToken ct,
        Action<int, int>? pageProgress = null)
    {
        if (string.IsNullOrWhiteSpace(scientificName))
            return new();

        const int pageSize = 300;
        var results = new List<GbifOccurrence>();
        var seenKeys = new HashSet<long>();

        for (var page = 0; page < maxPages; page++)
        {
            ct.ThrowIfCancellationRequested();
            var offset = page * pageSize;
            var url = $"{ApiRoot}/occurrence/search" +
                      $"?scientificName={Uri.EscapeDataString(scientificName)}" +
                      $"&country={Uri.EscapeDataString(countryCode)}" +
                      (string.IsNullOrWhiteSpace(stateProvince) ? "" : $"&stateProvince={Uri.EscapeDataString(stateProvince)}") +
                      "&kingdom=Animalia" +
                      "&media_type=StillImage" +
                      "&occurrenceStatus=present" +
                      "&taxonRank=SPECIES" +
                      $"&limit={pageSize}&offset={offset}";

            var result = await GetJsonAsync<GbifSearchResponse>(url, ct);
            if (result is null || result.Results.Count == 0)
                break;

            foreach (var occurrence in result.Results)
            {
                if (occurrence.Key > 0 && seenKeys.Add(occurrence.Key))
                    results.Add(occurrence);
            }

            pageProgress?.Invoke(page + 1, maxPages);

            if (result.Results.Count < pageSize || results.Count >= 6000)
                break;
        }

        return results;
    }

    public async Task<List<SpeciesInput>> DiscoverSpeciesAsync(
        string countryCode,
        string stateProvince,
        int maximumSpecies,
        int minimumOccurrences,
        int maxPages,
        CancellationToken ct,
        Action<string>? log = null)
    {
        maximumSpecies = Math.Clamp(maximumSpecies, 1, 500);
        minimumOccurrences = Math.Clamp(minimumOccurrences, 1, 1000);
        maxPages = Math.Clamp(maxPages, 1, 30);

        // Não dependemos de facets aqui. A API do GBIF pode responder a uma
        // busca válida sem devolver a faceta speciesKey, então descobrimos
        // as espécies diretamente nos resultados paginados.
        const int pageSize = 300;
        var counts = new Dictionary<long, int>();
        var lastPage = 0;

        // A mesma configuração de páginas da interface controla a descoberta.
        // Cada página pode trazer até 300 ocorrências.
        var discoveryPages = maxPages;

        for (var page = 0; page < discoveryPages; page++)
        {
            ct.ThrowIfCancellationRequested();
            lastPage = page + 1;
            var offset = page * pageSize;
            var url = $"{ApiRoot}/occurrence/search" +
                      $"?country={Uri.EscapeDataString(countryCode)}" +
                      (string.IsNullOrWhiteSpace(stateProvince) ? "" : $"&stateProvince={Uri.EscapeDataString(stateProvince)}") +
                      "&kingdom=Animalia" +
                      "&media_type=StillImage" +
                      "&occurrenceStatus=present" +
                      "&taxonRank=SPECIES" +
                      $"&limit={pageSize}&offset={offset}";

            var search = await GetJsonAsync<GbifSearchResponse>(url, ct);
            if (search is null || search.Results.Count == 0)
                break;

            foreach (var occurrence in search.Results)
            {
                if (occurrence.SpeciesKey is not > 0)
                    continue;

                counts.TryGetValue(occurrence.SpeciesKey.Value, out var count);
                counts[occurrence.SpeciesKey.Value] = count + 1;
            }

            var eligible = counts.Count(x => x.Value >= minimumOccurrences);
            log?.Invoke($"  descoberta GBIF: página {lastPage}/{discoveryPages} • {eligible} espécies candidatas");

            if (eligible >= maximumSpecies || search.Results.Count < pageSize)
                break;
        }

        var selected = counts
            .Where(x => x.Value >= minimumOccurrences)
            .OrderByDescending(x => x.Value)
            .Take(maximumSpecies)
            .ToList();

        if (selected.Count == 0)
            throw new InvalidOperationException(
                "O GBIF retornou ocorrências, mas nenhuma delas possui speciesKey suficiente para montar o catálogo automático.");

        log?.Invoke($"GBIF encontrou {selected.Count} espécies candidatas na área consultada.");

        var output = new List<SpeciesInput>();
        foreach (var item in selected)
        {
            ct.ThrowIfCancellationRequested();
            var key = item.Key;

            var taxon = await GetJsonAsync<GbifTaxonResponse>($"{ApiRoot}/species/{key}", ct);
            if (taxon is null)
                continue;

            if (!string.Equals(taxon.Kingdom, "Animalia", StringComparison.OrdinalIgnoreCase))
            {
                log?.Invoke($"  ignorado GBIF {key}: reino não é Animalia ({taxon.Kingdom ?? "desconhecido"}).");
                continue;
            }

            if (!string.Equals(taxon.Rank, "SPECIES", StringComparison.OrdinalIgnoreCase))
            {
                log?.Invoke($"  ignorado GBIF {key}: rank não é SPECIES ({taxon.Rank ?? "desconhecido"}).");
                continue;
            }

            var scientific = taxon.CanonicalName ?? taxon.ScientificName;
            if (string.IsNullOrWhiteSpace(scientific))
                continue;

            var common = await GetBestVernacularNameAsync(key, ct);
            output.Add(new SpeciesInput
            {
                Id = $"gbif-{key}",
                GbifTaxonKey = key,
                ScientificName = scientific.Trim(),
                CommonName = string.IsNullOrWhiteSpace(common) ? scientific.Trim() : common.Trim()
            });

            log?.Invoke($"  catálogo: {output.Count}/{maximumSpecies} — {output[^1].CommonName} ({scientific}) — {item.Value} ocorrências");
            if (output.Count >= maximumSpecies)
                break;
        }

        return output;
    }

    public async Task<byte[]> DownloadImageAsync(
        GbifOccurrence occurrence,
        GbifMedia media,
        CancellationToken ct)
    {
        if (occurrence.Key <= 0 || string.IsNullOrWhiteSpace(media.Identifier))
            throw new InvalidOperationException("Registro GBIF sem ID ou identificador de mídia.");

        var hash = Convert.ToHexString(
            MD5.HashData(Encoding.UTF8.GetBytes(media.Identifier))).ToLowerInvariant();
        var url = $"{ApiRoot}/image/cache/800x/occurrence/{occurrence.Key}/media/{hash}";
        using var response = await client.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync(ct);
    }

    async Task<string?> GetBestVernacularNameAsync(long key, CancellationToken ct)
    {
        try
        {
            var data = await GetJsonAsync<GbifVernacularResponse>(
                $"{ApiRoot}/species/{key}/vernacularNames?limit=200", ct);

            return PickVernacularName(data?.Results);
        }
        catch
        {
            return null;
        }
    }

    // Prefere português; na falta dele, inglês. Nunca usa um idioma arbitrário (já apareceu
    // um nome em turco): sem nome em pt/en o catálogo usa o nome científico.
    internal static string? PickVernacularName(IEnumerable<GbifVernacularName>? names)
    {
        if (names is null)
            return null;

        var list = names.Where(x => !string.IsNullOrWhiteSpace(x.VernacularName)).ToList();

        static bool Is(GbifVernacularName x, params string[] codes) =>
            codes.Any(c => string.Equals(x.Language, c, StringComparison.OrdinalIgnoreCase));

        return list.FirstOrDefault(x => Is(x, "por", "pt"))?.VernacularName
            ?? list.FirstOrDefault(x => Is(x, "eng", "en"))?.VernacularName;
    }

    async Task<T?> GetJsonAsync<T>(string url, CancellationToken ct)
    {
        using var response = await client.GetAsync(url, ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return default;
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        return await JsonSerializer.DeserializeAsync<T>(stream, json, ct);
    }

    public Action<string>? Log { get; set; }

    public void Dispose() => client.Dispose();
}
