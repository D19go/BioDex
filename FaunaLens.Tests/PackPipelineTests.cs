using FaunaLens.Services;
using FaunaPackBuilder;
using Xunit;

namespace FaunaLens.Tests;

// Garante que um pack gravado pelo código do app (PackWriter) é lido pelo leitor do app (FaunaPackReader).
public sealed class PackPipelineTests : IDisposable
{
    readonly string workDirectory = Path.Combine(Path.GetTempPath(), "faunalens-pipeline-" + Guid.NewGuid().ToString("N"));

    public PackPipelineTests() => Directory.CreateDirectory(workDirectory);

    public void Dispose()
    {
        try { Directory.Delete(workDirectory, true); } catch { /* limpeza best-effort */ }
    }

    static float[] RandomUnitVector(Random random)
    {
        var vector = new float[512];
        for (var i = 0; i < vector.Length; i++) vector[i] = (float)(random.NextDouble() * 2 - 1);
        var norm = Math.Sqrt(vector.Sum(x => (double)x * x));
        for (var i = 0; i < vector.Length; i++) vector[i] = (float)(vector[i] / norm);
        return vector;
    }

    [Fact]
    public async Task PackWrittenByTheAppIsReadBackByTheReader()
    {
        var temp = Path.Combine(workDirectory, "work");
        var random = new Random(7);
        var species = new List<PackSpecies>();

        for (var s = 0; s < 3; s++)
        {
            var info = new SpeciesInput
            {
                Id = $"sp-{s}",
                CommonName = $"Espécie {s}",
                ScientificName = $"Genus species{s}",
                GbifTaxonKey = 100 + s
            };
            var item = new PackSpecies { Species = info };
            var folder = Path.Combine(temp, "references", info.Id);
            Directory.CreateDirectory(folder);

            for (var r = 1; r <= 2; r++)
            {
                File.WriteAllBytes(Path.Combine(folder, $"{r}.jpg"), new byte[6000]);
                item.References.Add(new ReferenceImageInfo
                {
                    Index = r,
                    File = $"images/{info.Id}/{r}.jpg",
                    GbifId = 1,
                    SourceUrl = "https://example.org/x.jpg",
                    License = "CC0"
                });
                item.Embeddings.Add(RandomUnitVector(random));
            }

            species.Add(item);
        }

        var packPath = Path.Combine(workDirectory, "teste.faunapack");
        PackWriter.Write(packPath, "Teste do app", species, temp, "meupack20261108120000");

        var installed = await new FaunaPackReader().InstallAsync(packPath, Path.Combine(workDirectory, "installed"));

        Assert.Equal("meupack20261108120000", installed.Manifest.PackId);
        Assert.Equal(3, installed.Entries.Count);
        Assert.Equal(2, installed.Manifest.ReferencesPerSpecies);
        Assert.Equal("Espécie 1", installed.Entries[1].Species.CommonName);
        Assert.All(installed.Entries, entry => Assert.Equal(512, entry.Species.Embedding.Length));
    }
}

public sealed class GbifLicenseTests
{
    [Theory]
    [InlineData("http://creativecommons.org/publicdomain/zero/1.0/", false, true)]
    [InlineData("https://creativecommons.org/licenses/by/4.0/", false, true)]
    [InlineData("https://creativecommons.org/licenses/by-sa/4.0/", false, false)]
    [InlineData("https://creativecommons.org/licenses/by-sa/4.0/", true, true)]
    [InlineData("https://creativecommons.org/licenses/by-nc/4.0/", true, false)]
    [InlineData(null, true, false)]
    public void OnlyOpenLicensesAreAccepted(string? license, bool allowBySa, bool expected)
    {
        Assert.Equal(expected, GbifLicense.IsAllowed(license, allowBySa));
    }
}
