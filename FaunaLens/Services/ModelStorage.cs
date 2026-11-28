using Android.App;
using Android.Content;
using Android.OS;

namespace FaunaLens.Services;

// Acesso a Download/BioDex. Modelo e packs ficam no armazenamento privado do app; esta pasta só é usada
// pelo placar compartilhado entre clones e para trazer arquivos de versões anteriores.
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

    // Abre a tela do sistema onde o usuário libera o acesso. Só é chamado quando a função precisa dele.
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

    // Traz modelo e packs de versões anteriores para o armazenamento privado.
    // Cópia e depois remoção da origem; o que falhar fica onde estava e tenta de novo na próxima abertura.
    public static void MigrateToPrivateStorage()
    {
        // Download/BioDex só pode ser lido com acesso liberado. Sem acesso, a migração espera.
        if (HasAccess(Android.App.Application.Context))
        {
            Try(() => MoveFileIfNeeded(FileSystem.PublicModelPath, FileSystem.ModelPath));
            Try(() => MoveFileIfNeeded(FileSystem.PublicModelTempPath, FileSystem.ModelTempPath));
            Try(() => MoveFileIfNeeded(FileSystem.LegacyPublicModelPath, FileSystem.ModelPath));
            Try(() => MoveDirectories(FileSystem.PublicPackDirectory, FileSystem.PackDirectory));
        }

        Try(() => MoveFileIfNeeded(FileSystem.LegacyModelPath, FileSystem.ModelPath));
        Try(() => MoveFileIfNeeded(FileSystem.LegacyModelTempPath, FileSystem.ModelTempPath));
        Try(() => MoveDirectories(FileSystem.LegacyPackDirectory, FileSystem.PackDirectory));
    }

    // Copia o arquivo de origem para um temporário no destino, troca pelo final e só então apaga a origem.
    // Se o destino já existe, a origem não é tocada (o que já foi migrado vale).
    static void MoveFileIfNeeded(string sourcePath, string targetPath)
    {
        if (!ModelLocation.ShouldMigrateLegacy(File.Exists(sourcePath), File.Exists(targetPath)))
            return;

        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        var copying = targetPath + ".copying";
        File.Copy(sourcePath, copying, true);
        File.Move(copying, targetPath, true);
        File.Delete(sourcePath);
    }

    // Cada pack antigo vai para a pasta nova. Pack que já existe no destino não é sobrescrito.
    static void MoveDirectories(string source, string target)
    {
        if (!Directory.Exists(source))
            return;

        Directory.CreateDirectory(target);
        foreach (var directory in Directory.GetDirectories(source))
        {
            Try(() =>
            {
                var destination = Path.Combine(target, Path.GetFileName(directory));
                if (Directory.Exists(destination))
                    return;

                var copying = destination + ".copying";
                if (Directory.Exists(copying))
                    Directory.Delete(copying, true);

                CopyDirectory(directory, copying);
                Directory.Move(copying, destination);
                Directory.Delete(directory, true);
            });
        }
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
            // Migração é best-effort.
        }
    }
}
