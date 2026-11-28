namespace FaunaLens.Services;

// Tudo que o app usa fica no armazenamento privado (Context.FilesDir): não pede acesso a todos os arquivos.
// Pastas antigas (Download/BioDex e pastas privadas de versões anteriores) só aparecem na migração.
public static class FileSystem
{
    public static string Root => Android.App.Application.Context.FilesDir!.AbsolutePath;

    // Estado do download: privado do app, não precisa sobreviver à desinstalação.
    public static string PrivateModelStateDirectory => Path.Combine(Root, "models");
    public static string ModelDownloadStatePath => Path.Combine(PrivateModelStateDirectory, "bioclip_download.json");

    // Modelo BioCLIP: vem embutido no APK e é copiado para cá na primeira abertura (ver EmbeddedModel).
    public static string ModelDirectory => Path.Combine(Root, "bioclip");
    public static string ModelPath => Path.Combine(ModelDirectory, ModelLocation.ModelFileName);
    public static string ModelTempPath => ModelPath + ".download";

    // Packs instalados (um diretório por pack).
    public static string PackDirectory => Path.Combine(Root, "packs");

    public static string PhotoDirectory => Path.Combine(Root, "captures");
    public static string PackImportPath => Path.Combine(Root, "faunapack.install");

    // Estado da criação de packs em segundo plano (acompanhado pela tela de criação).
    public static string PackBuildStatePath => Path.Combine(Root, "pack_build_state.json");

    // ---- Migração: caminhos de versões anteriores, usados só para trazer os arquivos para cá. ----

    // Versão anterior: Download/BioDex/Biodex_Models e Download/BioDex/FaunaPacks (precisa de acesso a arquivos).
    public static string PublicModelPath => ModelLocation.ModelPath(ModelStorage.PublicDownloadsDirectory);
    public static string PublicModelTempPath => PublicModelPath + ".download";
    public static string PublicPackDirectory => ModelLocation.PackDirectory(ModelStorage.PublicDownloadsDirectory);

    // Versão anterior ainda: Download/Biodex_Models, direto em Download.
    public static string LegacyPublicModelPath =>
        Path.Combine(ModelLocation.LegacyModelDirectory(ModelStorage.PublicDownloadsDirectory), ModelLocation.ModelFileName);

    // Versões intermediárias: modelo e packs dentro do próprio app, em outras pastas.
    public static string LegacyModelPath => Path.Combine(PrivateModelStateDirectory, ModelLocation.ModelFileName);
    public static string LegacyModelTempPath => LegacyModelPath + ".download";
    public static string LegacyPackDirectory => Path.Combine(Root, "faunapacks");
}
