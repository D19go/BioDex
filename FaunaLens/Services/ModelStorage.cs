using Android.App;
using Android.Content;
using Android.OS;

namespace FaunaLens.Services;

// Acesso à pasta Download/Biodex_Models (Android 11+ exige "acesso a todos os arquivos").
public static class ModelStorage
{
    public const int StorageRequest = 4107;

    public static string PublicDownloadsDirectory
    {
        get
        {
#pragma warning disable CS0618
            return Android.OS.Environment.GetExternalStoragePublicDirectory(
                Android.OS.Environment.DirectoryDownloads)!.AbsolutePath;
#pragma warning restore CS0618
        }
    }

    public static bool HasAccess(Activity activity)
    {
        if (Build.VERSION.SdkInt >= BuildVersionCodes.R)
        {
#pragma warning disable CA1416
            return Android.OS.Environment.IsExternalStorageManager;
#pragma warning restore CA1416
        }

        return activity.CheckSelfPermission("android.permission.WRITE_EXTERNAL_STORAGE")
            == Android.Content.PM.Permission.Granted;
    }

    // Abre a tela do sistema onde o usuário libera o acesso. Depois de voltar, toque no botão de novo.
    public static void RequestAccess(Activity activity)
    {
        if (Build.VERSION.SdkInt >= BuildVersionCodes.R)
        {
            var intent = new Intent(Android.Provider.Settings.ActionManageAppAllFilesAccessPermission);
            intent.SetData(Android.Net.Uri.Parse("package:" + activity.PackageName));
            activity.StartActivity(intent);
            return;
        }

        activity.RequestPermissions(
            new[]
            {
                "android.permission.READ_EXTERNAL_STORAGE",
                "android.permission.WRITE_EXTERNAL_STORAGE"
            },
            StorageRequest);
    }

    // Move o modelo (e um download parcial) da pasta privada antiga para Download/Biodex_Models.
    // Falhas são ignoradas: o arquivo antigo continua no lugar e o download é refeito.
    public static void MigrateLegacyModels()
    {
        try
        {
            MigrateIfNeeded(FileSystem.LegacyModelPath, FileSystem.ModelPath);
            MigrateIfNeeded(FileSystem.LegacyModelTempPath, FileSystem.ModelTempPath);
        }
        catch
        {
        }
    }

    static void MigrateIfNeeded(string legacyPath, string targetPath)
    {
        if (!ModelLocation.ShouldMigrateLegacy(File.Exists(legacyPath), File.Exists(targetPath)))
            return;

        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        var copying = targetPath + ".copying";
        File.Copy(legacyPath, copying, true);
        File.Move(copying, targetPath, true);
        File.Delete(legacyPath);
    }
}
