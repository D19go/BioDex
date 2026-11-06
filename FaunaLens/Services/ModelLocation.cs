namespace FaunaLens.Services;

// Lógica de caminhos do modelo, sem dependências do Android para poder ser testada no CI.
public static class ModelLocation
{
    public const string FolderName = "Biodex_Models";
    public const string ModelFileName = "bioclip_image.onnx";

    public static string ModelDirectory(string downloadsDirectory) =>
        Path.Combine(downloadsDirectory, FolderName);

    public static string ModelPath(string downloadsDirectory) =>
        Path.Combine(ModelDirectory(downloadsDirectory), ModelFileName);

    // Copia da versão anterior (pasta privada) só quando o arquivo ainda não existe na pasta pública.
    public static bool ShouldMigrateLegacy(bool legacyExists, bool publicExists) =>
        legacyExists && !publicExists;
}
