using FaunaLens.Services;
using Xunit;

namespace FaunaLens.Tests;

public sealed class ScoringPolicyTests
{
    [Theory]
    [InlineData("Panthera onca")]
    [InlineData("panthera onca")]
    [InlineData("Bothrops jararaca")]
    [InlineData("Potamotrygon motoro")]
    public void DangerousGeneraAreFaixaUm(string scientificName)
    {
        Assert.Equal(SpeciesRisk.Dangerous, ScoringPolicy.Classify(scientificName));
    }

    [Theory]
    [InlineData("Turdus rufiventris")]
    [InlineData("Eira barbara")]
    [InlineData("")]
    [InlineData(null)]
    public void OrdinaryOrEmptyNamesAreNormal(string? scientificName)
    {
        Assert.Equal(SpeciesRisk.Normal, ScoringPolicy.Classify(scientificName));
    }

    [Fact]
    public void FaixaUmAndThreatenedNeverScore()
    {
        Assert.Equal(0, ScoringPolicy.PointsFor(SpeciesRisk.Dangerous));
        Assert.Equal(0, ScoringPolicy.PointsFor(SpeciesRisk.Threatened));
    }

    [Fact]
    public void RepeatsDecreaseStepByStepDownToMinimum()
    {
        Assert.Equal(10, ScoringPolicy.PointsForRepeat(SpeciesRisk.Normal, 0));
        Assert.Equal(8, ScoringPolicy.PointsForRepeat(SpeciesRisk.Normal, 1));
        Assert.Equal(6, ScoringPolicy.PointsForRepeat(SpeciesRisk.Normal, 2));
        Assert.Equal(ScoringPolicy.MinimumPoints, ScoringPolicy.PointsForRepeat(SpeciesRisk.Normal, 3));
        Assert.Equal(ScoringPolicy.MinimumPoints, ScoringPolicy.PointsForRepeat(SpeciesRisk.Normal, 50));
    }

    [Fact]
    public void DangerousAndThreatenedStayAtZeroOnRepeats()
    {
        Assert.Equal(0, ScoringPolicy.PointsForRepeat(SpeciesRisk.Dangerous, 0));
        Assert.Equal(0, ScoringPolicy.PointsForRepeat(SpeciesRisk.Threatened, 4));
    }

    [Fact]
    public void NormalAndAttentionScorePlaceholderValues()
    {
        Assert.Equal(ScoringPolicy.PlaceholderNormalPoints, ScoringPolicy.PointsFor(SpeciesRisk.Normal));
        Assert.Equal(ScoringPolicy.PlaceholderAttentionPoints, ScoringPolicy.PointsFor(SpeciesRisk.Attention));
        Assert.True(ScoringPolicy.PlaceholderNormalPoints > 0);
    }
}
