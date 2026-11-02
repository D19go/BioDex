using Android.App;
using Android.Content;
using Android.OS;
using AndroidX.Core.App;

namespace FaunaLens.Services;

[Service(Exported = false, ForegroundServiceType = Android.Content.PM.ForegroundService.TypeDataSync)]
public sealed class BioClipDownloadForegroundService : Service
{
    public const string ActionStart = "FaunaLens.BioClipDownload.START";
    public const string ActionCancel = "FaunaLens.BioClipDownload.CANCEL";
    public const string BroadcastAction = "FaunaLens.BioClipDownload.STATE";

    const int NotificationId = 7101;
    const string ChannelId = "bioclip_download";

    CancellationTokenSource? cancellation;
    Task? worker;
    bool stopping;

    public override void OnCreate()
    {
        base.OnCreate();
        CreateNotificationChannel();
        StartForeground(NotificationId, BuildNotification("Preparando download do BioCLIP...", 0, false));
    }

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        if (intent?.Action == ActionCancel)
        {
            cancellation?.Cancel();
            stopping = true;
            StopSelfResult(startId);
            return StartCommandResult.NotSticky;
        }

        if (worker is null || worker.IsCompleted)
        {
            stopping = false;
            cancellation?.Dispose();
            cancellation = new CancellationTokenSource();
            worker = RunAsync(cancellation.Token);
        }

        return StartCommandResult.Sticky;
    }

    async Task RunAsync(CancellationToken token)
    {
        try
        {
            var state = BioClipDownloadState.Load();
            state.Status = File.Exists(FileSystem.ModelTempPath) ? "resuming" : "downloading";
            state.Error = null;
            state.UpdatedUtc = DateTime.UtcNow;
            BioClipDownloadState.Save(state);
            Broadcast(state);

            var downloader = new BioClipDownloadService();
            var lastReport = DateTime.MinValue;
            await downloader.DownloadAsync(
                ModelConfiguration.BioClipOnnxUrl,
                FileSystem.ModelPath,
                (downloaded, total) =>
                {
                    // Gravar o estado e atualizar a notificação a cada bloco de 128 KB é excessivo;
                    // limita a uma atualização por segundo (e sempre no final).
                    var now = DateTime.UtcNow;
                    var finished = total > 0 && downloaded >= total;
                    if (!finished && now - lastReport < TimeSpan.FromSeconds(1))
                        return;
                    lastReport = now;

                    var current = BioClipDownloadState.Load();
                    current.Status = "downloading";
                    current.DownloadedBytes = downloaded;
                    current.TotalBytes = total;
                    current.Percent = total > 0
                        ? (int)Math.Clamp(downloaded * 100L / total, 0, 100)
                        : current.Percent;
                    current.UpdatedUtc = DateTime.UtcNow;
                    BioClipDownloadState.Save(current);
                    UpdateNotification(current);
                    Broadcast(current);
                },
                token);

            var completed = BioClipDownloadState.Load();
            completed.Status = "completed";
            completed.Percent = 100;
            completed.DownloadedBytes = File.Exists(FileSystem.ModelPath)
                ? new FileInfo(FileSystem.ModelPath).Length
                : completed.DownloadedBytes;
            completed.TotalBytes = completed.DownloadedBytes;
            completed.Error = null;
            completed.UpdatedUtc = DateTime.UtcNow;
            BioClipDownloadState.Save(completed);
            UpdateNotification(completed);
            Broadcast(completed);
        }
        catch (System.OperationCanceledException)
        {
            var paused = BioClipDownloadState.Load();
            paused.Status = "paused";
            paused.Error = null;
            paused.UpdatedUtc = DateTime.UtcNow;
            BioClipDownloadState.Save(paused);
            Broadcast(paused);
        }
        catch (Exception ex)
        {
            var failed = BioClipDownloadState.Load();
            failed.Status = "interrupted";
            failed.Error = ex.Message;
            failed.UpdatedUtc = DateTime.UtcNow;
            BioClipDownloadState.Save(failed);
            UpdateNotification(failed);
            Broadcast(failed);
        }
        finally
        {
            cancellation?.Dispose();
            cancellation = null;
            worker = null;
            if (!stopping)
                StopSelf();
        }
    }

    void Broadcast(BioClipDownloadState state)
    {
        var intent = new Intent(BroadcastAction);
        intent.SetPackage(PackageName);
        intent.PutExtra("status", state.Status);
        intent.PutExtra("percent", state.Percent);
        intent.PutExtra("downloaded", state.DownloadedBytes);
        intent.PutExtra("total", state.TotalBytes);
        intent.PutExtra("error", state.Error ?? string.Empty);
        SendBroadcast(intent);
    }

    void CreateNotificationChannel()
    {
        if (Build.VERSION.SdkInt < BuildVersionCodes.O)
            return;

        var manager = (NotificationManager?)GetSystemService(NotificationService);
        manager?.CreateNotificationChannel(new NotificationChannel(
            ChannelId,
            "Downloads do Fauna Lens",
            NotificationImportance.Low)
        {
            Description = "Progresso do download do modelo BioCLIP offline."
        });
    }

    Notification BuildNotification(string text, int progress, bool indeterminate)
    {
        var builder = new NotificationCompat.Builder(this, ChannelId)
            .SetContentTitle("Fauna Lens")
            .SetContentText(text)
            .SetSmallIcon(Android.Resource.Drawable.IcMenuGallery)
            .SetOngoing(true)
            .SetOnlyAlertOnce(true)
            .SetCategory(NotificationCompat.CategoryProgress);

        if (indeterminate)
            builder.SetProgress(100, 0, true);
        else
            builder.SetProgress(100, Math.Clamp(progress, 0, 100), false);

        return builder.Build();
    }

    void UpdateNotification(BioClipDownloadState state)
    {
        var text = state.Status switch
        {
            "completed" => "BioCLIP offline instalado.",
            "paused" => "Download pausado. Será retomado depois.",
            "interrupted" => "Download interrompido. Abra o app para tentar novamente.",
            _ => $"Baixando BioCLIP offline... {state.Percent}%"
        };

        var notification = BuildNotification(text, state.Percent, state.TotalBytes <= 0 && state.Status is ("downloading" or "resuming"));
        NotificationManagerCompat.From(this).Notify(NotificationId, notification);
    }

    public override IBinder? OnBind(Intent? intent) => null;

    public override void OnDestroy()
    {
        cancellation?.Cancel();
        base.OnDestroy();
    }
}
