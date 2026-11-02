using System.IO.Compression;
using System.Text;
using FaunaLens.Models;
using FaunaLens.Services;
using Xunit;

namespace FaunaLens.Tests;

public sealed class CoreLogicTests : IDisposable
{
    readonly string workDirectory = Path.Combine(Path.GetTempPath(), "faunalens-tests-" + Guid.NewGuid().ToString("N"));

    public CoreLogicTests() => Directory.CreateDirectory(workDirectory);

    public void Dispose()
    {
        try { Directory.Delete(workDirectory, true); } catch { /* limpeza best-effort */ }
    }

    static string PampaPackPath => Path.Combine(AppContext.BaseDirectory, "Pampa.faunapack");

    async Task<FaunaPackData> InstallPampaAsync()
    {
        var destination = Path.Combine(workDirectory, "packs");
        return await new FaunaPackReader().InstallAsync(PampaPackPath, destination);
    }

    [Fact]
    public async Task PampaPack_InstallsAndLoadsFiveSpeciesWithUnitEmbeddings()
    {
        var pack = await InstallPampaAsync();

        Assert.Equal(5, pack.Entries.Count);
        Assert.Equal(512, pack.Manifest.EmbeddingDimension);

        foreach (var entry in pack.Entries)
        {
            Assert.Equal(512, entry.Species.Embedding.Length);
            var norm = Math.Sqrt(entry.Species.Embedding.Sum(x => (double)x * x));
            Assert.InRange(norm, 0.999, 1.001);
        }
    }

    [Fact]
    public async Task Catalog_SearchWithASpeciesOwnEmbedding_ReturnsThatSpeciesFirst()
    {
        var pack = await InstallPampaAsync();
        var catalog = new SpeciesCatalog();
        catalog.LoadPack(pack);

        var target = pack.Entries[2].Species;
        var matches = catalog.Search(target.Embedding, 3);

        Assert.Equal(3, matches.Count);
        Assert.Equal(target.Id, matches[0].Species.Id);
        Assert.InRange(matches[0].Score, 0.999f, 1.001f);
    }

    [Fact]
    public void Catalog_WithoutEmbeddings_ReturnsNoMatches()
    {
        var json = """[{"id":"a","commonName":"A","scientificName":"Aus a"}]""";
        var catalog = new SpeciesCatalog();
        catalog.Load(new MemoryStream(Encoding.UTF8.GetBytes(json)), "sem embeddings");

        Assert.Empty(catalog.Search(new float[512], 5));
    }

    [Fact]
    public void Formatter_LowScore_SaysNotRecognized()
    {
        var species = new Species { Id = "a", CommonName = "Capivara" };
        var text = ResultFormatter.Format(new[] { new SpeciesMatch(species, 0.30f) });

        Assert.Contains("Não consegui reconhecer", text);
        Assert.DoesNotContain("Provável identificação", text);
    }

    [Fact]
    public void Formatter_HighScore_HidesEmptyFields()
    {
        var species = new Species { Id = "a", CommonName = "Capivara" };
        var text = ResultFormatter.Format(new[] { new SpeciesMatch(species, 0.90f) });

        Assert.Contains("Provável identificação: Capivara", text);
        Assert.DoesNotContain("FICHA", text);
        Assert.DoesNotContain("Altura", text);
    }

    [Fact]
    public async Task PackReader_RejectsPathTraversalInsideTheArchive()
    {
        var malicious = Path.Combine(workDirectory, "malicious.faunapack");
        using (var zip = ZipFile.Open(malicious, ZipArchiveMode.Create))
        {
            var entry = zip.CreateEntry("../evil.txt");
            await using var writer = new StreamWriter(entry.Open());
            await writer.WriteAsync("x");
        }

        var destination = Path.Combine(workDirectory, "packs-malicious");
        await Assert.ThrowsAsync<InvalidDataException>(
            () => new FaunaPackReader().InstallAsync(malicious, destination));
    }
}
