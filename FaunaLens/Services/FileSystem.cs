namespace FaunaLens.Services;

public static class FileSystem
{
    public static string Root => Android.App.Application.Context.FilesDir!.AbsolutePath;

    // Estado do download: privado do app, não precisa sobreviver à desinstalação.
    public static string PrivateModelStateDirectory => Path.Combine(Root, "models");
    public static string ModelDownloadStatePath => Path.Combine(PrivateModelStateDirectory, "bioclip_download.json");

    // Modelo: Download/BioDex/Biodex_Models, para continuar existindo depois de desinstalar o app.
    public static string ModelDirectory => ModelLocation.ModelDirectory(ModelStorage.PublicDownloadsDirectory);
    public static string ModelPath => ModelLocation.ModelPath(ModelStorage.PublicDownloadsDirectory);
    public static string ModelTempPath => ModelPath + ".download";

    // Versões anteriores guardavam o modelo na pasta privada do app; usado apenas para migrar.
    public static string LegacyModelPath => Path.Combine(PrivateModelStateDirectory, ModelLocation.ModelFileName);
    public static string LegacyModelTempPath => LegacyModelPath + ".download";

    // Versões anteriores guardavam o modelo em Download/Biodex_Models; usado apenas para migrar.
    public static string LegacyPublicModelPath =>
        Path.Combine(ModelLocation.LegacyModelDirectory(ModelStorage.PublicDownloadsDirectory), ModelLocation.ModelFileName);
    public static string LegacyPublicModelTempPath => LegacyPublicModelPath + ".download";

    // Packs: Download/BioDex/FaunaPacks, compartilhados por todos os apps BioDex (inclusive clones).
    public static string PackDirectory => ModelLocation.PackDirectory(ModelStorage.PublicDownloadsDirectory);

    // Versões anteriores guardavam os packs na pasta privada do app; usado apenas para migrar.
    public static string LegacyPackDirectory => Path.Combine(Root, "faunapacks");

    public static string PhotoDirectory => Path.Combine(Root, "captures");
    public static string PackImportPath => Path.Combine(Root, "faunapack.install");

    // Estado da criação de packs em segundo plano (acompanhado pela tela de criação).
    public static string PackBuildStatePath => Path.Combine(Root, "pack_build_state.json");
}
