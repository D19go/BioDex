namespace FaunaLens.Services;

public static class FileSystem
{
    public static string Root => Android.App.Application.Context.FilesDir!.AbsolutePath;

    // Estado do download: privado do app, não precisa sobreviver à desinstalação.
    public static string PrivateModelStateDirectory => Path.Combine(Root, "models");
    public static string ModelDownloadStatePath => Path.Combine(PrivateModelStateDirectory, "bioclip_download.json");

    // Modelo: Download/Biodex_Models, para continuar existindo depois de desinstalar o app.
    public static string ModelDirectory => ModelLocation.ModelDirectory(ModelStorage.PublicDownloadsDirectory);
    public static string ModelPath => ModelLocation.ModelPath(ModelStorage.PublicDownloadsDirectory);
    public static string ModelTempPath => ModelPath + ".download";

    // Versões anteriores guardavam o modelo aqui; usado apenas para migrar.
    public static string LegacyModelPath => Path.Combine(PrivateModelStateDirectory, ModelLocation.ModelFileName);
    public static string LegacyModelTempPath => LegacyModelPath + ".download";

    public static string PhotoDirectory => Path.Combine(Root, "captures");
    public static string PackDirectory => Path.Combine(Root, "faunapacks");
    public static string PackImportPath => Path.Combine(Root, "faunapack.install");
}
