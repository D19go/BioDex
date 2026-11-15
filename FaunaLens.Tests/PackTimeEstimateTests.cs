using FaunaLens.Services;
using Xunit;

namespace FaunaLens.Tests;

public sealed class PackTimeEstimateTests
{
    [Fact]
    public void MatchesTheMeasuredFortyThreeSpeciesRun()
    {
        // Medição real: 43 espécies × 20 imagens levaram cerca de 5 h.
        var time = PackTimeEstimate.Estimate(43, 20);

        Assert.InRange(time.TotalHours, 4.5, 5.5);
    }

    [Fact]
    public void GrowsWithSpeciesAndImages()
    {
        Assert.True(PackTimeEstimate.Estimate(50, 20) > PackTimeEstimate.Estimate(25, 20));
        Assert.True(PackTimeEstimate.Estimate(25, 50) > PackTimeEstimate.Estimate(25, 20));
    }

    [Fact]
    public void ZeroSpeciesTakesNoTime()
    {
        Assert.Equal(TimeSpan.Zero, PackTimeEstimate.Estimate(0, 50));
    }

    [Fact]
    public void FormatsHoursAndMinutes()
    {
        Assert.Equal("4 h 51 min", PackTimeEstimate.Format(TimeSpan.FromMinutes(291)));
        Assert.Equal("12 min", PackTimeEstimate.Format(TimeSpan.FromMinutes(12)));
        Assert.Equal("1 min", PackTimeEstimate.Format(TimeSpan.FromSeconds(20)));
    }
}
