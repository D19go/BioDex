using FaunaLens.Models;
using FaunaLens.Services;
using Xunit;

namespace FaunaLens.Tests;

public sealed class MatchingPolicyTests
{
    [Fact]
    public void WeightedMeanFollowsReferenceCounts()
    {
        // 1 referência em [1,0] e 3 em [0,1]: o resultado deve pesar 3x mais o segundo vetor.
        var mean = MatchingPolicy.WeightedMean(new float[] { 1, 0 }, 1, new float[] { 0, 1 }, 3);

        Assert.Equal(0.25f, mean[0], 4);
        Assert.Equal(0.75f, mean[1], 4);
    }

    [Fact]
    public void WeightedMeanTreatsMissingCountAsOneReference()
    {
        var mean = MatchingPolicy.WeightedMean(new float[] { 1, 0 }, 0, new float[] { 0, 1 }, 0);

        Assert.Equal(0.5f, mean[0], 4);
        Assert.Equal(0.5f, mean[1], 4);
    }

    [Fact]
    public void TopKMeanAveragesTheClosestReferences()
    {
        var query = new float[] { 1, 0 };
        var references = new[]
        {
            new float[] { 1, 0 },    // cosseno 1
            new float[] { 0.6f, 0.8f }, // cosseno 0,6
            new float[] { 0, 1 }     // cosseno 0
        };

        var score = MatchingPolicy.TopKMean(query, references, 2);

        Assert.Equal(0.8f, score, 4);
    }

    [Fact]
    public void TopKMeanWithoutReferencesIsZero()
    {
        Assert.Equal(0f, MatchingPolicy.TopKMean(new float[] { 1, 0 }, Array.Empty<float[]>(), 3));
    }

    [Fact]
    public void MergingPacksKeepsTheWeightedEmbeddingAndAllReferences()
    {
        var first = new Species
        {
            ScientificName = "Hydrochoerus hydrochaeris",
            Embedding = new float[] { 1, 0 },
            ReferenceCount = 1,
            ReferenceEmbeddings = new[] { new float[] { 1, 0 } }
        };
        var second = new Species
        {
            ScientificName = "Hydrochoerus hydrochaeris",
            Embedding = new float[] { 0, 1 },
            ReferenceCount = 3,
            ReferenceEmbeddings = new[] { new float[] { 0, 1 }, new float[] { 0, 1 }, new float[] { 0, 1 } }
        };

        var catalog = new SpeciesCatalog();
        catalog.LoadPacks(new[] { PackOf(first), PackOf(second) });

        var merged = catalog.Search(new float[] { 0, 1 }, 1).Single().Species;
        Assert.Equal(0.75f, merged.Embedding[1], 4);
        Assert.Equal(4, merged.ReferenceEmbeddings.Length);
        Assert.Equal(4, merged.ReferenceCount);
    }

    [Fact]
    public void NearestReferencesModeScoresBySimilarReferences()
    {
        // O embedding médio aponta para outro lado, mas a única referência é idêntica à foto.
        var species = new Species
        {
            ScientificName = "Puma concolor",
            Embedding = new float[] { 0, 1 },
            ReferenceCount = 1,
            ReferenceEmbeddings = new[] { new float[] { 1, 0 } }
        };
        var catalog = new SpeciesCatalog();
        catalog.LoadPacks(new[] { PackOf(species) });
        var query = new float[] { 1, 0 };

        var centroidScore = catalog.Search(query, 1).Single().Score;
        catalog.UseNearestReferences = true;
        var nearestScore = catalog.Search(query, 1).Single().Score;

        Assert.Equal(0f, centroidScore, 4);
        Assert.Equal(1f, nearestScore, 4);
    }

    static FaunaPackData PackOf(Species species)
    {
        var manifest = new FaunaPackManifest { PackName = "teste" };
        var entries = new List<FaunaPackSpeciesEntry>
        {
            new()
            {
                Species = species,
                References = Enumerable.Range(0, Math.Max(1, species.ReferenceCount))
                    .Select(i => new FaunaPackReference { Index = i, File = $"{i}.jpg" })
                    .ToList()
            }
        };
        return new FaunaPackData(manifest, entries, "");
    }
}
