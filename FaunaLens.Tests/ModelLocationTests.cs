using FaunaLens.Services;
using Xunit;

namespace FaunaLens.Tests;

public sealed class ModelLocationTests
{
    const string Downloads = "/storage/emulated/0/Download";

    [Fact]
    public void ModelFolderIsBiodexModelsInsideBioDexFolder()
    {
        var directory = ModelLocation.ModelDirectory(Downloads);

        Assert.Equal("Biodex_Models", Path.GetFileName(directory));
        Assert.Equal("/storage/emulated/0/Download/BioDex", Path.GetDirectoryName(directory));
    }

    [Fact]
    public void PackFolderIsFaunaPacksInsideBioDexFolder()
    {
        var directory = ModelLocation.PackDirectory(Downloads);

        Assert.Equal("FaunaPacks", Path.GetFileName(directory));
        Assert.Equal("/storage/emulated/0/Download/BioDex", Path.GetDirectoryName(directory));
    }

    [Fact]
    public void ModelFileLivesInsideTheModelFolder()
    {
        var path = ModelLocation.ModelPath(Downloads);

        Assert.Equal(ModelLocation.ModelDirectory(Downloads), Path.GetDirectoryName(path));
        Assert.Equal("bioclip_image.onnx", Path.GetFileName(path));
    }

    [Fact]
    public void LegacyModelFolderIsDirectlyInDownloads()
    {
        Assert.Equal("/storage/emulated/0/Download/Biodex_Models", ModelLocation.LegacyModelDirectory(Downloads));
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
