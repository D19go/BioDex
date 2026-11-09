using FaunaLens.Services;
using Xunit;

namespace FaunaLens.Tests;

public sealed class ThemeModesTests
{
    [Theory]
    [InlineData(null, "system")]
    [InlineData("", "system")]
    [InlineData("qualquer", "system")]
    [InlineData("light", "light")]
    [InlineData("dark", "dark")]
    public void UnknownValuesFollowTheSystem(string? input, string expected) =>
        Assert.Equal(expected, ThemeModes.Normalize(input));

    [Theory]
    [InlineData("dark", false, true)]
    [InlineData("light", true, false)]
    [InlineData("system", true, true)]
    [InlineData("system", false, false)]
    [InlineData(null, true, true)]
    public void ChoosesDarkFromTheModeOrTheSystem(string? mode, bool systemIsNight, bool expectedDark) =>
        Assert.Equal(expectedDark, ThemeModes.UseDark(mode, systemIsNight));

    [Fact]
    public void SpinnerLabelsMatchTheValues()
    {
        Assert.Equal(ThemeModes.Values.Length, ThemeModes.Labels.Length);
        Assert.Equal(0, ThemeModes.IndexOf(ThemeModes.FollowSystem));
        Assert.Equal(2, ThemeModes.IndexOf(ThemeModes.Dark));
    }
}
