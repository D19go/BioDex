using Android.Content;
using Android.OS;

namespace FaunaLens.Services;

// Único ponto que grava o pedido e inicia o serviço de criação. Usado pela tela e pela criação automática.
public static class PackBuildLauncher
{
    // Cria um pedido novo (zera o log).
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

        StartService(context);
    }

    // Retoma uma criação que o Android encerrou antes do fim (o serviço parou sem terminar).
    // Mantém o pedido e o log. Devolve true se retomou.
    public static bool ResumeIfInterrupted(Context context)
    {
        var path = FileSystem.PackBuildStatePath;
        var state = PackBuildStateStore.Load(path);
        if (state.Request is null || state.Status is not ("running" or "queued") || state.IsActive(DateTime.UtcNow))
            return false;

        state.Status = "queued";
        state.Error = null;
        state.AddLog("O Android encerrou a criação antes do fim. Retomando do ponto salvo...");
        state.UpdatedUtc = DateTime.UtcNow;
        PackBuildStateStore.Save(path, state);

        StartService(context);
        return true;
    }

    static void StartService(Context context)
    {
        var intent = new Intent(context, typeof(PackBuildForegroundService));
        intent.SetAction(PackBuildForegroundService.ActionStart);
        try
        {
            if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
                context.StartForegroundService(intent);
            else
                context.StartService(intent);
        }
        catch (Exception ex)
        {
            // O Android recusou iniciar o serviço (por exemplo, o limite diário de execução). O progresso fica salvo.
            CrashLog.Write("Início da criação de FaunaPack recusado", ex);
            PackBuildStateStore.Update(FileSystem.PackBuildStatePath, s =>
            {
                s.Status = "paused";
                s.Error = null;
                s.AddLog("O Android não deixou a criação começar agora. O progresso está salvo; tente de novo mais tarde.");
            });
        }
    }
}
