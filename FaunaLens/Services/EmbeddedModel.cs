using Android.Content;

namespace FaunaLens.Services;

// Modelo BioCLIP embutido no APK (pasta de assets). Só existe quando o CI baixou o arquivo antes do build;
// sem ele, o app continua pedindo o download pelo botão, como antes.
public static class EmbeddedModel
{
    public const string AssetName = ModelLocation.ModelFileName;

    public static bool IsBundled(Context context)
    {
        try
        {
            using var stream = context.Assets!.Open(AssetName);
            return stream is not null;
        }
        catch
        {
            return false;
        }
    }

    // Copia o modelo do APK para o armazenamento privado. Confere se abre como ONNX antes de trocar o arquivo final.
    public static void CopyToPrivateStorage(Context context)
    {
        var target = FileSystem.ModelPath;
        var temporary = FileSystem.ModelTempPath;
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);

        try
        {
            using (var input = context.Assets!.Open(AssetName))
            using (var output = File.Create(temporary))
            {
                input.CopyTo(output);
                output.Flush();
            }

            BioClipOnnxEngine.Validate(temporary);
            File.Move(temporary, target, true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }
}
