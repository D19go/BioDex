using FaunaLens.Services;
using FaunaPackBuilder;
using Xunit;

namespace FaunaLens.Tests;

public sealed class PackGrowthPolicyTests
{
    [Fact]
    public void FiveNewAnimalsThenOneRepeated()
    {
        var newOnes = Enumerable.Range(1, 10).Select(i => $"n{i}").ToList();
        var repeated = new List<string> { "r1", "r2" };

        var order = PackGrowthPolicy.Order(newOnes, repeated);

        Assert.Equal(
            new[] { "n1", "n2", "n3", "n4", "n5", "r1", "n6", "n7", "n8", "n9", "n10", "r2" },
            order);
    }

    [Fact]
    public void RepeatedAnimalsFillInWhenThereAreNoNewOnes()
    {
        var order = PackGrowthPolicy.Order(new List<string>(), new List<string> { "r1", "r2", "r3" });

        Assert.Equal(new[] { "r1", "r2", "r3" }, order);
    }

    [Fact]
    public void NewAnimalsKeepTheirOrderWhenThereAreNoRepeats()
    {
        var order = PackGrowthPolicy.Order(new List<string> { "a", "b", "c" }, new List<string>());

        Assert.Equal(new[] { "a", "b", "c" }, order);
    }

    [Theory]
    [InlineData(0, 2, 2)]
    [InlineData(49, 2, 1)]
    [InlineData(50, 2, 0)]
    [InlineData(10, 99, 40)]
    public void NewImagesNeverPassTheLimit(int existing, int requested, int expected)
    {
        Assert.Equal(expected, PackGrowthPolicy.NewImagesAllowed(existing, requested));
    }
}

public sealed class VernacularNameTests
{
    static GbifVernacularName Name(string name, string? language, bool? preferred = null) =>
        new() { VernacularName = name, Language = language, Preferred = preferred };

    [Fact]
    public void PortugueseComesBeforeIndigenousAndOtherLanguages()
    {
        var names = new[]
        {
            Name("Tiger", "eng"),
            Name("Yaguareté", "gn"),
            Name("Onça-pintada", "por")
        };

        Assert.Equal("Onça-pintada", GbifClient.PickVernacularName(names));
    }

    [Fact]
    public void BrazilianPortugueseCodeIsAccepted()
    {
        Assert.Equal("Capivara", GbifClient.PickVernacularName(new[] { Name("Capivara", "pt-BR") }));
    }

    [Fact]
    public void IndigenousNameIsUsedWhenThereIsNoPortuguese()
    {
        var names = new[] { Name("Tiger", "eng"), Name("Jaguarete", "tpn") };

        Assert.Equal("Jaguarete", GbifClient.PickVernacularName(names));
    }

    [Fact]
    public void OtherLanguagesAreIgnored()
    {
        var names = new[] { Name("Kaplan", "tr"), Name("Tigre", "es") };

        Assert.Null(GbifClient.PickVernacularName(names));
    }

    [Fact]
    public void PreferredNameWinsWithinTheSameLanguage()
    {
        var names = new[] { Name("Onça", "por", false), Name("Onça-pintada", "por", true) };

        Assert.Equal("Onça-pintada", GbifClient.PickVernacularName(names));
    }

    [Fact]
    public void NamesWithoutLanguageOrText_AreIgnored()
    {
        var names = new[] { Name("", "por"), Name("Jaguar", null) };

        Assert.Null(GbifClient.PickVernacularName(names));
        Assert.Null(GbifClient.PickVernacularName(null));
    }
}

// Pack parcial: espécies com quantidades diferentes de imagens, gravado e lido de volta.
public sealed class PartialPackTests : IDisposable
{
    readonly string directory = Path.Combine(Path.GetTempPath(), "faunalens-partial-" + Guid.NewGuid().ToString("N"));

    public PartialPackTests() => Directory.CreateDirectory(directory);

    public void Dispose()
    {
        try { Directory.Delete(directory, true); } catch { /* limpeza best-effort */ }
    }

    static float[] RandomUnitVector(Random random)
    {
        var vector = new float[512];
        for (var i = 0; i < vector.Length; i++) vector[i] = (float)(random.NextDouble() * 2 - 1);
        var norm = Math.Sqrt(vector.Sum(x => (double)x * x));
        for (var i = 0; i < vector.Length; i++) vector[i] = (float)(vector[i] / norm);
        return vector;
    }

    static PackSpecies Species(string id, int images, Random random, string work)
    {
        var item = new PackSpecies
        {
            Species = new SpeciesInput { Id = id, CommonName = "Nome " + id, ScientificName = "Genus " + id, GbifTaxonKey = 1 }
        };

        var folder = Path.Combine(work, "references", id);
        Directory.CreateDirectory(folder);
        for (var r = 1; r <= images; r++)
        {
            File.WriteAllBytes(Path.Combine(folder, $"{r}.jpg"), new byte[6000 + r]);
            item.References.Add(new ReferenceImageInfo
            {
                Index = r,
                File = $"images/{id}/{r}.jpg",
                GbifId = r,
                SourceUrl = $"https://example.org/{id}/{r}.jpg",
                License = "CC0"
            });
            item.Embeddings.Add(RandomUnitVector(random));
        }

        return item;
    }

    [Fact]
    public async Task PartialPackKeepsTheImageCountOfEachSpecies()
    {
        var work = Path.Combine(directory, "work");
        var random = new Random(3);
        var species = new List<PackSpecies> { Species("a", 1, random, work), Species("b", 3, random, work) };

        var path = Path.Combine(directory, "parcial.faunapack");
        PackWriter.Write(path, "Parcial", species, work, "parcial20261114");

        var reader = new FaunaPackReader();
        var pack = await reader.InstallAsync(path, Path.Combine(directory, "installed"));

        Assert.Equal(2, pack.Manifest.FormatVersion);
        Assert.Equal(3, pack.Manifest.ReferencesPerSpecies);
        Assert.Equal(new[] { 1, 3 }, pack.Entries.Select(entry => entry.References.Count).ToArray());

        var vectors = await reader.ReadReferenceEmbeddingsAsync(pack);
        Assert.Equal(1, vectors[0].Count);
        Assert.Equal(3, vectors[1].Count);

        // O embedding gravado é o mesmo que foi escrito (a leitura só normaliza).
        var original = species[1].Embeddings[2];
        var similarity = original.Zip(vectors[1][2]).Sum(pair => pair.First * pair.Second);
        Assert.True(similarity > 0.999f);
    }

    [Fact]
    public async Task UniformPackStaysOnFormatOne()
    {
        var work = Path.Combine(directory, "work");
        var random = new Random(5);
        var species = new List<PackSpecies> { Species("a", 2, random, work), Species("b", 2, random, work) };

        var path = Path.Combine(directory, "uniforme.faunapack");
        PackWriter.Write(path, "Uniforme", species, work, "uniforme20261114");

        var pack = await new FaunaPackReader().InstallAsync(path, Path.Combine(directory, "installed"));

        Assert.Equal(1, pack.Manifest.FormatVersion);
        Assert.Equal(2, pack.Manifest.ReferencesPerSpecies);
    }
}
