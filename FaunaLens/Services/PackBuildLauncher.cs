using Android.Content;
using Android.OS;

namespace FaunaLens.Services;

// Único ponto que grava o pedido e inicia o serviço de criação. Usado pela tela e pela criação automática.
public static class PackBuildLauncher
{
    public static void Start(Context context, PackBuildOptions options)
    {
        PackBuildStateStore.Save(FileSystem.PackBuildStatePath, new PackBuildState
        {
            Status = "queued",
            PackName = options.PackName,
            Request = options,
            Acknowledged = false,
            UpdatedUtc = DateTime.UtcNow
        });

        var intent = new Intent(context, typeof(PackBuildForegroundService));
        intent.SetAction(PackBuildForegroundService.ActionStart);
        if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
            context.StartForegroundService(intent);
        else
            context.StartService(intent);
    }
}
