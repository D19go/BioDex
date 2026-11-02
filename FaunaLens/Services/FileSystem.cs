namespace FaunaLens.Services;

public static class FileSystem
{
    public static string Root => Android.App.Application.Context.FilesDir!.AbsolutePath;
    public static string ModelDirectory => Path.Combine(Root, "models");
    public static string ModelPath => Path.Combine(ModelDirectory, "bioclip_image.onnx");
    public static string ModelTempPath => ModelPath + ".download";
    public static string ModelDownloadStatePath => Path.Combine(ModelDirectory, "bioclip_download.json");
    public static string PhotoDirectory => Path.Combine(Root, "captures");
    public static string PackDirectory => Path.Combine(Root, "faunapacks");
    public static string PackImportPath => Path.Combine(Root, "faunapack.install");
}
