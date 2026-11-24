using FaunaLens.Services;
using Xunit;

namespace FaunaLens.Tests;

public sealed class ScoringRulesTests
{
    [Fact]
    public void OncaPardaIndividualIs45()
    {
        Assert.Equal(45, ScoringRules.Individual(rarity: 11, difficulty: 12, size: 4, conservation: 3));
    }

    [Fact]
    public void HarpiaIndividualIs60()
    {
        Assert.Equal(60, ScoringRules.Individual(rarity: 19, difficulty: 17, size: 4, conservation: 5));
    }

    [Fact]
    public void MinimumIndividualIs22()
    {
        Assert.Equal(22, ScoringRules.Individual(rarity: 5, difficulty: 1, size: 1, conservation: 0));
    }

    [Fact]
    public void MaximumIndividualIs65()
    {
        Assert.Equal(65, ScoringRules.Individual(rarity: 20, difficulty: 20, size: 5, conservation: 5));
    }

    [Fact]
    public void OncaRepeatsDiscountFiveUntilEightyPercent()
    {
        var points = Enumerable.Range(0, 5).Select(i => ScoringRules.Repeat(45, i)).ToArray();
        Assert.Equal(new[] { 45, 40, 36, 36, 36 }, points);
    }

    [Fact]
    public void HarpiaRepeatsStopAtEightyPercent()
    {
        var points = Enumerable.Range(0, 5).Select(i => ScoringRules.Repeat(60, i)).ToArray();
        Assert.Equal(new[] { 60, 55, 50, 48, 48 }, points);
    }

    [Fact]
    public void FirstIdentificationOfOncaWithBonusesIs60()
    {
        var individual = ScoringRules.Individual(11, 12, 4, 3);
        Assert.Equal(60, individual + ScoringRules.FirstDiscoveryBonus + ScoringRules.ExplorerBonus);
    }

    [Theory]
    [InlineData(0, false, false)]
    [InlineData(1, false, true)]
    [InlineData(1, true, false)]
    [InlineData(3, false, true)]
    public void CollectorIsGrantedOncePerGroupOnSecondSpecies(int sameGroupBefore, bool alreadyGranted, bool expected)
    {
        Assert.Equal(expected, ScoringRules.CollectorEarned(sameGroupBefore, alreadyGranted));
    }

    [Fact]
    public void NewHabitatsSkipsExploredOnes()
    {
        var explored = new HashSet<string> { "Floresta" };
        var result = ScoringRules.NewHabitats(new[] { "Floresta", "Aquático", "Aquático" }, explored);
        Assert.Equal(new[] { "Aquático" }, result);
    }

    [Fact]
    public void HabitatsAreFoundByKeyword()
    {
        Assert.Equal(new[] { "Floresta" }, ScoringRules.HabitatsFrom("Mata Atlântica e floresta tropical"));
        Assert.Contains("Aquático", ScoringRules.HabitatsFrom("Rios e lagos"));
        Assert.Contains("Subterrâneo", ScoringRules.HabitatsFrom("Vive em cavernas"));
        Assert.Empty(ScoringRules.HabitatsFrom("Texto sem habitat"));
    }
}
