namespace FaunaLens.Services;

// Lógica de caminhos do BioDex, sem dependências do Android para poder ser testada no CI.
// Tudo fica em Download/BioDex: Biodex_Models (modelo BioCLIP) e FaunaPacks (packs instalados).
// Todos os apps BioDex (inclusive clones) usam essas mesmas pastas.
public static class ModelLocation
{
    public const string RootFolderName = "BioDex";
    public const string ModelFolderName = "Biodex_Models";
    public const string PackFolderName = "FaunaPacks";
    public const string ModelFileName = "bioclip_image.onnx";

    // Local usado nas versões anteriores (Download/Biodex_Models, direto em Download); usado só para migrar.
    public const string LegacyModelFolderName = "Biodex_Models";

    public static string RootDirectory(string downloadsDirectory) =>
        Path.Combine(downloadsDirectory, RootFolderName);

    public static string ModelDirectory(string downloadsDirectory) =>
        Path.Combine(RootDirectory(downloadsDirectory), ModelFolderName);

    public static string ModelPath(string downloadsDirectory) =>
        Path.Combine(ModelDirectory(downloadsDirectory), ModelFileName);

    public static string PackDirectory(string downloadsDirectory) =>
        Path.Combine(RootDirectory(downloadsDirectory), PackFolderName);

    public static string LegacyModelDirectory(string downloadsDirectory) =>
        Path.Combine(downloadsDirectory, LegacyModelFolderName);

    // Copia da versão anterior só quando o arquivo ainda não existe na pasta nova.
    public static bool ShouldMigrateLegacy(bool legacyExists, bool publicExists) =>
        legacyExists && !publicExists;
}
