using Android.App;
using Android.Content;
using Android.OS;

namespace FaunaLens.Services;

// Acesso à pasta Download/BioDex (Android 11+ exige "acesso a todos os arquivos").
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

    public static bool HasAccess(Context context)
    {
        if (Build.VERSION.SdkInt >= BuildVersionCodes.R)
        {
#pragma warning disable CA1416
            return Android.OS.Environment.IsExternalStorageManager;
#pragma warning restore CA1416
        }

        return context.CheckSelfPermission("android.permission.WRITE_EXTERNAL_STORAGE")
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

    // Traz modelo e packs de versões anteriores para Download/BioDex. Só roda com acesso liberado.
    // Falhas são ignoradas: o que não migrou continua na pasta antiga.
    public static void MigrateLegacyFolders()
    {
        if (!HasAccess(Android.App.Application.Context))
            return;

        MigrateLegacyModels();
        MigrateLegacyPacks();
    }

    // Move o modelo (e um download parcial) das pastas antigas para Download/BioDex/Biodex_Models.
    public static void MigrateLegacyModels()
    {
        Try(() => MigrateFileIfNeeded(FileSystem.LegacyModelPath, FileSystem.ModelPath));
        Try(() => MigrateFileIfNeeded(FileSystem.LegacyModelTempPath, FileSystem.ModelTempPath));
        Try(() => MigrateFileIfNeeded(FileSystem.LegacyPublicModelPath, FileSystem.ModelPath));
        Try(() => MigrateFileIfNeeded(FileSystem.LegacyPublicModelTempPath, FileSystem.ModelTempPath));
    }

    // Copia cada pack antigo (pasta privada) para Download/BioDex/FaunaPacks e só então apaga o antigo.
    // Pack que já existe no destino não é sobrescrito.
    public static void MigrateLegacyPacks()
    {
        var legacy = FileSystem.LegacyPackDirectory;
        if (!Directory.Exists(legacy))
            return;

        var target = FileSystem.PackDirectory;
        Directory.CreateDirectory(target);

        foreach (var source in Directory.GetDirectories(legacy))
        {
            Try(() =>
            {
                var destination = Path.Combine(target, Path.GetFileName(source));
                if (Directory.Exists(destination))
                    return;

                var copying = destination + ".copying";
                if (Directory.Exists(copying))
                    Directory.Delete(copying, true);

                CopyDirectory(source, copying);
                Directory.Move(copying, destination);
                Directory.Delete(source, true);
            });
        }
    }

    static void MigrateFileIfNeeded(string legacyPath, string targetPath)
    {
        if (!ModelLocation.ShouldMigrateLegacy(File.Exists(legacyPath), File.Exists(targetPath)))
            return;

        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        var copying = targetPath + ".copying";
        File.Copy(legacyPath, copying, true);
        File.Move(copying, targetPath, true);
        File.Delete(legacyPath);
    }

    static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)), true);
        foreach (var directory in Directory.GetDirectories(source))
            CopyDirectory(directory, Path.Combine(target, Path.GetFileName(directory)));
    }

    static void Try(Action action)
    {
        try
        {
            action();
        }
        catch
        {
            // Migração é best-effort: o item fica na pasta antiga e tenta de novo na próxima vez.
        }
    }
}
