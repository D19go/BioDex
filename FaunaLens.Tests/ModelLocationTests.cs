using FaunaLens.Services;
using Xunit;

namespace FaunaLens.Tests;

public sealed class ModelLocationTests
{
    [Fact]
    public void ModelFolderIsBiodexModelsInsideDownloads()
    {
        var directory = ModelLocation.ModelDirectory("/storage/emulated/0/Download");

        Assert.Equal("Biodex_Models", Path.GetFileName(directory));
        Assert.Equal("/storage/emulated/0/Download", Path.GetDirectoryName(directory));
    }

    [Fact]
    public void ModelFileLivesInsideTheModelFolder()
    {
        var path = ModelLocation.ModelPath("/storage/emulated/0/Download");

        Assert.Equal(ModelLocation.ModelDirectory("/storage/emulated/0/Download"), Path.GetDirectoryName(path));
        Assert.Equal("bioclip_image.onnx", Path.GetFileName(path));
    }

    [Theory]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    public void MigratesOnlyWhenLegacyExistsAndPublicDoesNot(bool legacyExists, bool publicExists, bool expected)
    {
        Assert.Equal(expected, ModelLocation.ShouldMigrateLegacy(legacyExists, publicExists));
    }
}
