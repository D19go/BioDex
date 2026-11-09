using Android.App;
using Android.Content;
using Android.OS;
using AndroidX.Core.App;

namespace FaunaLens.Services;

// Cria o FaunaPack em segundo plano. Roda como serviço em primeiro plano (com notificação), então
// continua com o app minimizado. Se o sistema encerrar o processo, a criação para e pode ser refeita:
// não há retomada parcial.
[Service(Exported = false, ForegroundServiceType = Android.Content.PM.ForegroundService.TypeDataSync)]
public sealed class PackBuildForegroundService : Service
{
    public const string ActionStart = "FaunaLens.PackBuild.START";
    public const string ActionCancel = "FaunaLens.PackBuild.CANCEL";

    const int NotificationId = 7102;
    const string ChannelId = "pack_build";

    CancellationTokenSource? cancellation;
    Task? worker;

    public override void OnCreate()
    {
        base.OnCreate();
        CreateNotificationChannel();
        StartForeground(NotificationId, BuildNotification("Preparando a criação do FaunaPack...", 0, true, true));
    }

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        if (intent?.Action == ActionCancel)
        {
            cancellation?.Cancel();
            return StartCommandResult.NotSticky;
        }

        if (worker is null || worker.IsCompleted)
        {
            cancellation?.Dispose();
            cancellation = new CancellationTokenSource();
            worker = RunAsync(cancellation.Token);
        }

        return StartCommandResult.NotSticky;
    }

    async Task RunAsync(CancellationToken token)
    {
        var state = PackBuildStateStore.Load(FileSystem.PackBuildStatePath);
        var journal = new BuildJournal(this, state);

        // Atualiza o estado a cada 15 s mesmo sem progresso: é o sinal de que o serviço continua vivo.
        using var heartbeat = new System.Threading.Timer(_ => journal.Save(), null, TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(15));

        try
        {
            var options = state.Request ?? throw new InvalidOperationException("Pedido de criação não encontrado.");
            journal.Mutate(s =>
            {
                s.Status = "running";
                s.Percent = 0;
                s.Error = null;
            });

            if (!File.Exists(FileSystem.ModelPath))
                throw new InvalidOperationException("O BioCLIP ainda não está instalado. Baixe-o na tela principal primeiro.");

            journal.Log("Carregando o BioCLIP...");
            using var engine = await Task.Run(() => new BioClipOnnxEngine(FileSystem.ModelPath), token);
            if (!engine.IsReady)
                throw new InvalidOperationException("Não foi possível carregar o BioCLIP.");

            var builder = new AppPackBuilder(engine, journal.Log, journal.Progress);
            var zip = await builder.BuildToFileAsync(options, token);
            try
            {
                journal.Log("Instalando e ativando o pack...");
                var pack = await new FaunaPackManager().InstallAsync(zip, token);
                journal.Mutate(s =>
                {
                    s.Status = "completed";
                    s.Percent = 100;
                    s.PackName = pack.Manifest.PackName;
                    s.ResultPackId = pack.Manifest.PackId;
                    s.ResultSpeciesCount = pack.Entries.Count;
                    s.Acknowledged = false;
                });
                journal.Log($"Pack \"{pack.Manifest.PackName}\" pronto com {pack.Entries.Count} espécies e ativado.");
            }
            finally
            {
                if (File.Exists(zip))
                    File.Delete(zip);
            }
        }
        catch (System.OperationCanceledException)
        {
            journal.Mutate(s => s.Status = "cancelled");
            journal.Log("Criação cancelada.");
        }
        catch (Exception ex)
        {
            journal.Mutate(s =>
            {
                s.Status = "failed";
                s.Error = ex.Message;
            });
            journal.Log("ERRO: " + ex.Message);
        }
        finally
        {
            journal.Save();
            cancellation?.Dispose();
            cancellation = null;
            worker = null;
            StopForeground(false); // mantém a notificação final visível
            StopSelf();
        }
    }

    // Único ponto que altera o estado: serializa as escritas e limita a frequência de gravação em disco.
    sealed class BuildJournal
    {
        readonly object gate = new();
        readonly PackBuildForegroundService owner;
        readonly PackBuildState state;
        DateTime lastSave = DateTime.MinValue;

        public BuildJournal(PackBuildForegroundService owner, PackBuildState state)
        {
            this.owner = owner;
            this.state = state;
        }

        public void Mutate(Action<PackBuildState> change)
        {
            lock (gate)
            {
                change(state);
                SaveNow();
            }
        }

        public void Log(string line)
        {
            lock (gate)
            {
                state.AddLog(line);
                if (DateTime.UtcNow - lastSave >= TimeSpan.FromSeconds(1))
                    SaveNow();
            }
        }

        public void Progress(int percent)
        {
            lock (gate)
            {
                state.Percent = Math.Clamp(percent, 0, 100);
                if (DateTime.UtcNow - lastSave >= TimeSpan.FromSeconds(1))
                    SaveNow();
            }
        }

        public void Save() => Mutate(_ => { });

        void SaveNow()
        {
            state.UpdatedUtc = DateTime.UtcNow;
            PackBuildStateStore.Save(FileSystem.PackBuildStatePath, state);
            lastSave = DateTime.UtcNow;
            owner.UpdateNotification(state);
        }
    }

    void CreateNotificationChannel()
    {
        if (Build.VERSION.SdkInt < BuildVersionCodes.O)
            return;

        var manager = (NotificationManager?)GetSystemService(NotificationService);
        manager?.CreateNotificationChannel(new NotificationChannel(
            ChannelId,
            "Criação de FaunaPack",
            NotificationImportance.Low)
        {
            Description = "Progresso da criação de FaunaPacks em segundo plano."
        });
    }

    void UpdateNotification(PackBuildState state)
    {
        var finished = state.IsFinished;
        var text = state.Status switch
        {
            "completed" => $"FaunaPack \"{state.PackName}\" pronto e ativado.",
            "failed" => "A criação do FaunaPack falhou. Abra o app para ver o motivo.",
            "cancelled" => "Criação do FaunaPack cancelada.",
            _ => $"Criando FaunaPack \"{state.PackName}\"... {state.Percent}%"
        };

        var notification = BuildNotification(text, state.Percent, !finished && state.Percent == 0, !finished);
        NotificationManagerCompat.From(this).Notify(NotificationId, notification);
    }

    Notification BuildNotification(string text, int progress, bool indeterminate, bool ongoing)
    {
        var builder = new NotificationCompat.Builder(this, ChannelId)
            .SetContentTitle("Fauna Lens")
            .SetContentText(text)
            .SetSmallIcon(Android.Resource.Drawable.IcMenuGallery)
            .SetOngoing(ongoing)
            .SetOnlyAlertOnce(true)
            .SetCategory(NotificationCompat.CategoryProgress)
            .SetContentIntent(OpenScreenIntent());

        if (indeterminate)
            builder.SetProgress(100, 0, true);
        else
            builder.SetProgress(100, Math.Clamp(progress, 0, 100), false);

        return builder.Build();
    }

    // Tocar na notificação abre a tela de criação com o progresso atual.
    PendingIntent OpenScreenIntent()
    {
        var intent = new Intent(this, typeof(PackBuilderActivity));
        return PendingIntent.GetActivity(this, 0, intent, PendingIntentFlags.Immutable)!;
    }

    public override IBinder? OnBind(Intent? intent) => null;

    public override void OnDestroy()
    {
        cancellation?.Cancel();
        base.OnDestroy();
    }
}
