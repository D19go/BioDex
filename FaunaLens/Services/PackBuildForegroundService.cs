using Android.App;
using Android.Content;
using Android.OS;
using AndroidX.Core.App;
using FaunaLens.Models;

namespace FaunaLens.Services;

// Cria o FaunaPack em segundo plano, como serviço em primeiro plano (com notificação), para continuar com o app
// minimizado. Proteções contra o sistema:
// - wake lock parcial: a CPU segue acordada com a tela apagada;
// - limite de tempo por execução (RunBudget): o Android 15+ encerra serviços dataSync depois de 6 h por dia,
//   então a criação para antes, salva o progresso e fica "pausada";
// - rodadas: se o pack ainda estiver incompleto e a rodada trouxe novidades, uma nova rodada amplia o pack;
// - se o sistema matar o processo, o app retoma do checkpoint na próxima abertura (ver PackBuildLauncher).
[Service(Exported = false, ForegroundServiceType = Android.Content.PM.ForegroundService.TypeDataSync)]
public sealed class PackBuildForegroundService : Service
{
    public const string ActionStart = "FaunaLens.PackBuild.START";
    public const string ActionCancel = "FaunaLens.PackBuild.CANCEL";

    // Tempo máximo de uma execução. Passa disso, o Android pode encerrar o serviço sem aviso.
    public static readonly TimeSpan RunBudget = TimeSpan.FromHours(5.5);

    // Quantas rodadas tentar para completar o pack (50 espécies, 50 fotos cada).
    const int MaxRounds = 6;

    const int NotificationId = 7102;
    const string ChannelId = "pack_build";

    CancellationTokenSource? cancellation;
    Task? worker;
    bool startFailed;

    public override void OnCreate()
    {
        base.OnCreate();
        CrashLog.Install();
        CreateNotificationChannel();
        try
        {
            StartForeground(NotificationId, BuildNotification("Preparando a criação do FaunaPack...", 0, true, true));
        }
        catch (Exception ex)
        {
            // O Android recusou o primeiro plano (por exemplo, limite diário). Sem ele, a criação seria morta.
            startFailed = true;
            CrashLog.Write("Criação de FaunaPack sem permissão de primeiro plano", ex);
            PackBuildStateStore.Update(FileSystem.PackBuildStatePath, s =>
            {
                s.Status = "paused";
                s.Error = null;
                s.AddLog("O Android não deixou a criação rodar em segundo plano agora. Tente de novo mais tarde.");
            });
        }
    }

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        if (startFailed)
        {
            StopSelf();
            return StartCommandResult.NotSticky;
        }

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
        // O timer roda fora do try principal: uma exceção aqui derrubaria o app sem registro. Por isso é protegida.
        using var heartbeat = new System.Threading.Timer(_ =>
        {
            try
            {
                journal.Save();
            }
            catch (Exception ex)
            {
                CrashLog.Write("Heartbeat da criação do FaunaPack", ex);
            }
        }, null, TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(15));

        var wakeLock = AcquireWakeLock();
        var runStartedUtc = DateTime.UtcNow;
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

            FaunaPackData? lastPack = null;
            for (var round = 1; round <= MaxRounds; round++)
            {
                token.ThrowIfCancellationRequested();

                // Cada rodada recebe só o tempo que sobrou da execução. Estourar o limite cancela a rodada,
                // que salva o checkpoint antes de sair (ver AppPackBuilder.BuildToFileAsync).
                var remaining = RunBudget - (DateTime.UtcNow - runStartedUtc);
                if (remaining <= TimeSpan.Zero)
                    throw new OperationCanceledException("limite de tempo da execução");
                using var limit = CancellationTokenSource.CreateLinkedTokenSource(token);
                limit.CancelAfter(remaining);
                var roundToken = limit.Token;

                // Se já existe um pack desta região (ou o escolhido para atualizar), ele é ampliado em vez de refeito.
                var baseline = await new FaunaPackManager().FindBaselineAsync(options, roundToken);
                var before = (Species: 0, Images: 0);
                if (baseline is not null)
                {
                    if (baseline.Region.Length > 0)
                        options.StateProvince = baseline.Region == BrazilRegions.WholeCountry ? "" : baseline.Region;
                    var installed = await new FaunaPackReader().LoadInstalledAsync(baseline.DirectoryPath, roundToken);
                    before = Stats(installed);
                    journal.Log($"Já existe o pack \"{baseline.PackName}\" desta região: ele será ampliado, sem repetir imagens.");
                }

                if (round > 1)
                    journal.Log($"Rodada {round} de {MaxRounds}: o pack ainda não está completo; ampliando.");

                var builder = new AppPackBuilder(engine, journal.Log, journal.Progress);
                // Checkpoints instalam o pack parcial: se a criação parar, o que já foi baixado continua no pack.
                var zip = await builder.BuildToFileAsync(options, baseline, roundToken, async path =>
                {
                    await new FaunaPackManager().InstallAsync(path, CancellationToken.None);
                });

                FaunaPackData pack;
                try
                {
                    journal.Log("Instalando e ativando o pack...");
                    pack = await new FaunaPackManager().InstallAsync(zip, roundToken);
                }
                finally
                {
                    if (File.Exists(zip))
                        File.Delete(zip);
                }

                lastPack = pack;
                var after = Stats(pack);
                journal.Log($"Pack com {after.Species} espécies e {after.Images} imagens.");

                if (IsComplete(pack, options))
                {
                    journal.Log("Pack completo.");
                    break;
                }

                if (after == before)
                {
                    // Sem nada novo na rodada: o GBIF não tem mais o que oferecer nesta região.
                    journal.Log("Esta rodada não trouxe imagens novas; a criação termina aqui.");
                    break;
                }
            }

            if (lastPack is null)
                throw new InvalidOperationException("Nenhum pack foi gerado.");

            journal.Mutate(s =>
            {
                s.Status = "completed";
                s.Percent = 100;
                s.PackName = lastPack.Manifest.PackName;
                s.ResultPackId = lastPack.Manifest.PackId;
                s.ResultSpeciesCount = lastPack.Entries.Count;
                s.Acknowledged = false;
            });
            journal.Log($"Pack \"{lastPack.Manifest.PackName}\" pronto com {lastPack.Entries.Count} espécies e ativado.");
        }
        catch (OperationCanceledException)
        {
            if (token.IsCancellationRequested)
            {
                journal.Mutate(s => s.Status = "cancelled");
                journal.Log("Criação cancelada. O que já foi baixado continua salvo no pack.");
            }
            else
            {
                // Não foi o usuário: foi o limite de tempo da execução. O progresso já está salvo.
                journal.Mutate(s =>
                {
                    s.Status = "paused";
                    s.Error = null;
                });
                journal.Log("Pausada: o limite de tempo desta execução foi atingido (o Android restringe a criação em segundo plano). " +
                    "O que já foi baixado está salvo. Abra a tela de criação e toque em \"Atualizar FaunaPack\" para continuar.");
            }
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
            ReleaseWakeLock(wakeLock);
            journal.Save();
            cancellation?.Dispose();
            cancellation = null;
            worker = null;
            StopForeground(false); // mantém a notificação final visível
            StopSelf();
        }
    }

    // Espécies e imagens do pack: usado para saber se a rodada trouxe algo novo.
    static (int Species, int Images) Stats(FaunaPackData pack) =>
        (pack.Entries.Count, pack.Entries.Sum(e => e.References.Count));

    // Completo quando há 50 espécies e cada uma tem 50 fotos (ou o que foi pedido).
    static bool IsComplete(FaunaPackData pack, PackBuildOptions options) =>
        pack.Entries.Count >= options.MaxSpecies
        && pack.Entries.All(e => e.References.Count >= options.ImagesPerSpecies);

    // Mantém a CPU ativa com a tela apagada. Liberado no fim da execução.
    PowerManager.WakeLock? AcquireWakeLock()
    {
        try
        {
            var power = (PowerManager?)GetSystemService(PowerService);
            var wakeLock = power?.NewWakeLock(WakeLockFlags.Partial, "Biodex:PackBuild");
            // Tempo máximo do próprio wake lock, para não ficar preso se o serviço travar.
            wakeLock?.Acquire((long)TimeSpan.FromHours(6).TotalMilliseconds);
            return wakeLock;
        }
        catch (Exception ex)
        {
            CrashLog.Write("Wake lock da criação de FaunaPack", ex);
            return null;
        }
    }

    static void ReleaseWakeLock(PowerManager.WakeLock? wakeLock)
    {
        try
        {
            if (wakeLock is { IsHeld: true })
                wakeLock.Release();
        }
        catch (Exception ex)
        {
            CrashLog.Write("Liberar o wake lock da criação de FaunaPack", ex);
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
            "paused" => "Criação pausada pelo Android. Abra o app para continuar.",
            _ => $"Criando FaunaPack \"{state.PackName}\"... {state.Percent}%"
        };

        var notification = BuildNotification(text, state.Percent, !finished && state.Percent == 0, !finished);
        NotificationManagerCompat.From(this).Notify(NotificationId, notification);
    }

    Notification BuildNotification(string text, int progress, bool indeterminate, bool ongoing)
    {
        var builder = new NotificationCompat.Builder(this, ChannelId)
            .SetContentTitle("Biodex")
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
