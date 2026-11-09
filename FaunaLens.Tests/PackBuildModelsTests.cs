using FaunaLens.Services;
using FaunaPackBuilder;
using Xunit;

namespace FaunaLens.Tests;

public sealed class PackBuildModelsTests : IDisposable
{
    readonly string directory = Path.Combine(Path.GetTempPath(), "faunalens-buildstate-" + Guid.NewGuid().ToString("N"));

    public PackBuildModelsTests() => Directory.CreateDirectory(directory);

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

    [Fact]
    public void ImagesPerSpeciesIsCappedAtFifty()
    {
        var options = new PackBuildOptions { PackName = "  Teste  ", ImagesPerSpecies = 500 }.Normalized();

        Assert.Equal(50, options.ImagesPerSpecies);
        Assert.Equal("Teste", options.PackName);
    }

    [Fact]
    public void ImagesPerSpeciesIsAtLeastOne()
    {
        Assert.Equal(1, new PackBuildOptions { ImagesPerSpecies = 0 }.Normalized().ImagesPerSpecies);
    }

    [Fact]
    public void UpdateIdIsTrimmedAndBlankIsIgnored()
    {
        Assert.Null(new PackBuildOptions { UpdatePackId = "   " }.Normalized().UpdatePackId);
        Assert.Equal("meupack1", new PackBuildOptions { UpdatePackId = " meupack1 " }.Normalized().UpdatePackId);
    }

    [Fact]
    public void StateRoundTripsThroughFile()
    {
        var path = Path.Combine(directory, "state.json");
        var state = new PackBuildState
        {
            Status = "running",
            PackName = "Meu pack",
            Percent = 40,
            Request = new PackBuildOptions { PackName = "Meu pack", ImagesPerSpecies = 50, StateProvince = "Rio Grande do Sul", UpdatePackId = "meupack1" }
        };
        state.AddLog("linha 1");
        PackBuildStateStore.Save(path, state);

        var loaded = PackBuildStateStore.Load(path);

        Assert.Equal("running", loaded.Status);
        Assert.Equal(40, loaded.Percent);
        Assert.Equal(50, loaded.Request!.ImagesPerSpecies);
        Assert.Equal("meupack1", loaded.Request.UpdatePackId);
        Assert.Single(loaded.Log);
        Assert.Contains("linha 1", loaded.Log[0]);
    }

    [Fact]
    public void MissingStateFileLoadsAsIdle()
    {
        var loaded = PackBuildStateStore.Load(Path.Combine(directory, "inexistente.json"));

        Assert.Equal("idle", loaded.Status);
        Assert.False(loaded.IsActive(DateTime.UtcNow));
    }

    [Fact]
    public void LogKeepsOnlyTheLatestLines()
    {
        var state = new PackBuildState();
        for (var i = 0; i < PackBuildState.MaxLogLines + 25; i++)
            state.AddLog($"linha {i}");

        Assert.Equal(PackBuildState.MaxLogLines, state.Log.Count);
        Assert.Contains("linha 324", state.Log[^1]);
    }

    [Fact]
    public void RunningStateWithoutHeartbeatIsNotActive()
    {
        var now = DateTime.UtcNow;
        var fresh = new PackBuildState { Status = "running", UpdatedUtc = now };
        var stale = new PackBuildState { Status = "running", UpdatedUtc = now.AddMinutes(-10) };
        var finished = new PackBuildState { Status = "completed", UpdatedUtc = now };

        Assert.True(fresh.IsActive(now));
        Assert.False(stale.IsActive(now));
        Assert.False(finished.IsActive(now));
    }

    [Fact]
    public void AcknowledgeMarksResultAsSeen()
    {
        var path = Path.Combine(directory, "ack.json");
        PackBuildStateStore.Save(path, new PackBuildState { Status = "completed" });

        PackBuildStateStore.Acknowledge(path);

        Assert.True(PackBuildStateStore.Load(path).Acknowledged);
    }

    [Fact]
    public async Task PackWithFiftyImagesKeepsBuildSettingsAndReadsBack()
    {
        var temp = Path.Combine(directory, "work");
        var random = new Random(11);
        var species = new List<PackSpecies>();

        for (var s = 0; s < 2; s++)
        {
            var info = new SpeciesInput
            {
                Id = $"sp-{s}",
                CommonName = $"Espécie {s}",
                ScientificName = $"Genus species{s}",
                GbifTaxonKey = s
            };
            var item = new PackSpecies { Species = info };
            var folder = Path.Combine(temp, "references", info.Id);
            Directory.CreateDirectory(folder);

            for (var r = 1; r <= 50; r++)
            {
                File.WriteAllBytes(Path.Combine(folder, $"{r}.jpg"), new byte[6000]);
                item.References.Add(new ReferenceImageInfo
                {
                    Index = r,
                    File = $"images/{info.Id}/{r}.jpg",
                    GbifId = r,
                    SourceUrl = "https://example.org/x.jpg",
                    License = "CC0"
                });
                item.Embeddings.Add(RandomUnitVector(random));
            }

            species.Add(item);
        }

        var packPath = Path.Combine(directory, "grande.faunapack");
        PackWriter.Write(
            packPath,
            "Grande",
            species,
            temp,
            "grande20261108",
            new PackBuildSettings { StateProvince = "Rio Grande do Sul", MaxSpecies = 50, SearchPages = 10, AllowBySa = true });

        var installed = await new FaunaPackReader().InstallAsync(packPath, Path.Combine(directory, "installed"));

        Assert.Equal("grande20261108", installed.Manifest.PackId);
        Assert.Equal(50, installed.Manifest.ReferencesPerSpecies);
        Assert.Equal("Rio Grande do Sul", installed.Manifest.BuildSettings!.StateProvince);
        Assert.True(installed.Manifest.BuildSettings.AllowBySa);
        Assert.All(installed.Entries, entry => Assert.Equal(512, entry.Species.Embedding.Length));
    }
}
