using System.Text.Json;
using FaunaLens.Services;

namespace FaunaLens.Models;

public sealed class SpeciesCatalog
{
    readonly List<Species> species = new();

    public int Count => species.Count;
    public string SourceName { get; private set; } = "catálogo padrão";

    public void Load(Stream stream, string sourceName = "catálogo padrão")
    {
        var data = JsonSerializer.Deserialize<List<Species>>(
            stream,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        species.Clear();
        if (data is not null)
            species.AddRange(data);

        SourceName = sourceName;
    }

    public void Clear()
    {
        species.Clear();
        SourceName = "";
    }

    // FaunaPackData pertence ao mesmo namespace Models.
    public void LoadPack(FaunaPackData pack)
    {
        species.Clear();
        species.AddRange(pack.Entries.Select(x => x.Species));
        SourceName = pack.Manifest.PackName;
    }

    // Vários packs ativos ao mesmo tempo. Uma espécie que aparece em mais de um pack vira uma entrada só:
    // embedding médio ponderado pelo número de referências, e imagens dos dois.
    public void LoadPacks(IEnumerable<FaunaPackData> packs)
    {
        var list = packs.ToList();
        var byKey = new Dictionary<string, Species>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in list.SelectMany(pack => pack.Entries.Select(x => x.Species)))
        {
            var key = string.IsNullOrWhiteSpace(entry.ScientificName) ? entry.Id : entry.ScientificName;
            if (!byKey.TryGetValue(key, out var existing))
            {
                byKey[key] = entry;
                continue;
            }

            MergeInto(existing, entry);
        }

        species.Clear();
        species.AddRange(byKey.Values);
        SourceName = string.Join(", ", list.Select(pack => pack.Manifest.PackName));
    }

    static void MergeInto(Species existing, Species other)
    {
        if (existing.Embedding.Length > 0 && existing.Embedding.Length == other.Embedding.Length)
        {
            existing.Embedding = MatchingPolicy.WeightedMean(
                existing.Embedding, existing.ReferenceCount,
                other.Embedding, other.ReferenceCount);
        }

        existing.ReferenceEmbeddings = existing.ReferenceEmbeddings.Concat(other.ReferenceEmbeddings).ToArray();
        existing.ReferenceCount += other.ReferenceCount;
        existing.ImagePaths = existing.ImagePaths.Concat(other.ImagePaths).Distinct().ToList();
    }

    // Quando true, a espécie é comparada pelas referências mais parecidas (média dos k melhores),
    // e não só pelo embedding médio. Fica desligado até a medição com o conjunto de teste confirmar a melhora.
    public bool UseNearestReferences { get; set; }

    public IReadOnlyList<SpeciesMatch> Search(float[] query, int count)
    {
        return species
            .Where(x => x.Embedding.Length == query.Length && x.Embedding.Length > 0)
            .Select(x => new SpeciesMatch(x, ScoreOf(query, x)))
            .OrderByDescending(x => x.Score)
            .Take(count)
            .ToList();
    }

    float ScoreOf(float[] query, Species candidate) =>
        UseNearestReferences && candidate.ReferenceEmbeddings.Length > 0
            ? MatchingPolicy.TopKMean(query, candidate.ReferenceEmbeddings, MatchingPolicy.NearestK)
            : MatchingPolicy.Cosine(query, candidate.Embedding);
}
