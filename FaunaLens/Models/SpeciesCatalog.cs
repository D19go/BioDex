using System.Text.Json;

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

    public IReadOnlyList<SpeciesMatch> Search(float[] query, int count)
    {
        return species
            .Where(x => x.Embedding.Length == query.Length && x.Embedding.Length > 0)
            .Select(x => new SpeciesMatch(x, Cosine(query, x.Embedding)))
            .OrderByDescending(x => x.Score)
            .Take(count)
            .ToList();
    }

    static float Cosine(float[] a, float[] b)
    {
        double dot = 0, aa = 0, bb = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            aa += a[i] * a[i];
            bb += b[i] * b[i];
        }

        return aa == 0 || bb == 0
            ? 0
            : (float)(dot / (Math.Sqrt(aa) * Math.Sqrt(bb)));
    }
}
