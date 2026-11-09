using FaunaLens.Services;
using Xunit;

namespace FaunaLens.Tests;

public sealed class RegionPackPlannerTests
{
    static readonly string[] NoPacks = Array.Empty<string>();

    [Fact]
    public void FirstDetectionOnlyRecordsTheState()
    {
        var plan = RegionPackPlanner.Plan(new RegionState(), "São Paulo", NoPacks, false, true);

        Assert.Equal(new RegionState("São Paulo", ""), plan.Next);
        Assert.Null(plan.StartRegion);
    }

    [Fact]
    public void StayingInTheSameStateDoesNothing()
    {
        var plan = RegionPackPlanner.Plan(new RegionState("São Paulo"), "São Paulo", NoPacks, false, true);

        Assert.Equal(new RegionState("São Paulo", ""), plan.Next);
        Assert.Null(plan.StartRegion);
    }

    [Fact]
    public void ChangingStateStartsThePackOfTheNewState()
    {
        var plan = RegionPackPlanner.Plan(new RegionState("São Paulo"), "Minas Gerais", new[] { "São Paulo" }, false, true);

        Assert.Equal("Minas Gerais", plan.StartRegion);
        Assert.Equal(new RegionState("Minas Gerais", ""), plan.Next);
    }

    [Fact]
    public void ChangingToAStateThatAlreadyHasAPackDoesNotRebuildIt()
    {
        var plan = RegionPackPlanner.Plan(new RegionState("São Paulo"), "Minas Gerais", new[] { "Minas Gerais" }, false, true);

        Assert.Null(plan.StartRegion);
        Assert.Equal(new RegionState("Minas Gerais", ""), plan.Next);
    }

    [Fact]
    public void ExistingPackMatchesIgnoringAccentsCaseAndSpaces()
    {
        var plan = RegionPackPlanner.Plan(new RegionState("Minas Gerais"), "São Paulo", new[] { "sao  paulo" }, false, true);

        Assert.Null(plan.StartRegion);
        Assert.Equal("São Paulo", plan.Next.LastRegion);
    }

    [Fact]
    public void ChangeWhileAnotherBuildRunsIsKeptForLater()
    {
        var plan = RegionPackPlanner.Plan(new RegionState("São Paulo"), "Minas Gerais", NoPacks, true, true);

        Assert.Null(plan.StartRegion);
        Assert.Equal(new RegionState("Minas Gerais", "Minas Gerais"), plan.Next);
    }

    [Fact]
    public void PendingRegionStartsWhenTheBuildFinishes()
    {
        var plan = RegionPackPlanner.Plan(new RegionState("Minas Gerais", "Minas Gerais"), null, NoPacks, false, true);

        Assert.Equal("Minas Gerais", plan.StartRegion);
        Assert.Equal(new RegionState("Minas Gerais", ""), plan.Next);
    }

    [Fact]
    public void PendingRegionWaitsForTheModel()
    {
        var plan = RegionPackPlanner.Plan(new RegionState("Minas Gerais", "Minas Gerais"), null, NoPacks, false, false);

        Assert.Null(plan.StartRegion);
        Assert.Equal(new RegionState("Minas Gerais", "Minas Gerais"), plan.Next);
    }

    [Fact]
    public void PendingRegionIsDroppedWhenItsPackAlreadyExists()
    {
        var plan = RegionPackPlanner.Plan(new RegionState("Minas Gerais", "Minas Gerais"), null, new[] { "Minas Gerais" }, false, true);

        Assert.Null(plan.StartRegion);
        Assert.Equal(new RegionState("Minas Gerais", ""), plan.Next);
    }

    [Fact]
    public void MissingDetectionKeepsTheStoredState()
    {
        var plan = RegionPackPlanner.Plan(new RegionState("São Paulo"), null, NoPacks, false, true);

        Assert.Equal(new RegionState("São Paulo", ""), plan.Next);
        Assert.Null(plan.StartRegion);
    }

    [Fact]
    public void NameThatIsNotAStateIsNotAStateChange()
    {
        var plan = RegionPackPlanner.Plan(new RegionState("São Paulo"), "Brasil", NoPacks, false, true);

        Assert.Equal(new RegionState("São Paulo", ""), plan.Next);
        Assert.Null(plan.StartRegion);
    }

    [Fact]
    public void BrazilRegionsListsTheStatesAndTheWholeCountryOption()
    {
        Assert.Equal(27, BrazilRegions.States.Length);
        Assert.Equal(BrazilRegions.WholeCountry, BrazilRegions.SpinnerItems[0]);
        Assert.Equal(28, BrazilRegions.SpinnerItems.Length);
    }

    [Theory]
    [InlineData("sao paulo", "São Paulo")]
    [InlineData("  RIO GRANDE DO SUL ", "Rio Grande do Sul")]
    [InlineData("Goias", "Goiás")]
    public void CanonicalNameIgnoresAccentsCaseAndExtraSpaces(string input, string expected) =>
        Assert.Equal(expected, BrazilRegions.Canonical(input));

    [Theory]
    [InlineData("Brasil")]
    [InlineData("")]
    [InlineData(null)]
    public void NonStatesHaveNoCanonicalName(string? input) =>
        Assert.Null(BrazilRegions.Canonical(input));
}
