using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.OS;
using Android.Provider;
using Android.Views;
using Android.Widget;
using AndroidX.Core.Content;
using FaunaLens.Models;
using FaunaLens.Services;
using Uri = Android.Net.Uri;

namespace FaunaLens;

// Sem Label: o nome do ícone vem do ApplicationTitle do build (Biodex, BioDex1, BioDex2, BioDex3...).
[Activity(MainLauncher = true, Exported = true, ScreenOrientation = ScreenOrientation.Portrait, LaunchMode = LaunchMode.SingleTop)]
public sealed class MainActivity : Activity
{
    const int CameraRequest = 4101;
    const int PermissionRequest = 4102;
    const int ModelRequest = 4103;
    const int GalleryRequest = 4104;
    const int PackRequest = 4105;
    const int NotificationPermissionRequest = 4106;
    const int ManagePacksRequest = 4107;
    public static bool OpenIdentifyOnResume;
    static readonly TimeSpan RegionCheckInterval = TimeSpan.FromMinutes(10);

    ImageView? preview;
    LinearLayout? matchImages;
    TextView? alternatives;
    MirroredStatus? status;
    TextView? identifyStatus;
    TextView? drawerStatus;
    Button? pointsChip;
    Button? postFromIdentifyButton;
    string? identifiedPhotoPath;
    int lastAwardedPoints;
    LinearLayout? identifyOverlay;
    LinearLayout? drawerPanel;
    LinearLayout? feedLayout;
    View? drawerScrim;
    TextView? result;
    Button? identifyButton;
    Button? downloadModelButton;
    EditText? endpointInput;
    Uri? pendingUri;
    string? pendingPath;

    // Fotos processadas na identificação. A partir da 6ª, as anteriores saem da memória.
    const int ReleaseEarlierPhotosAt = 6;
    const string PendingPathKey = "pending_camera_path";
    int processedPhotoCount;
    readonly List<Bitmap> matchBitmaps = new();

    readonly SpeciesCatalog catalog = new();
    readonly FaunaPackManager packManager = new();
    BioClipOnnxEngine? engine;
    IReadOnlyList<FaunaPackData> installedPacks = Array.Empty<FaunaPackData>();
    Handler? uiHandler;
    Bitmap? previewBitmap;
    bool pollingDownload;
    bool engineLoading;
    bool engineLoadFailed;
    bool startupFinished;
    bool regionCheckRunning;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        CrashLog.Install();
        ThemeController.Apply(this);
        base.OnCreate(savedInstanceState);
        // A câmera pode fechar o app para liberar memória; sem isso, a foto tirada não teria caminho ao voltar.
        pendingPath = savedInstanceState?.GetString(PendingPathKey) ?? pendingPath;
        uiHandler = new Handler(Looper.MainLooper!);
        BuildUi();
        if (!FirstRunPermissions.IsDone(this))
            AskFirstRunPermissions();
        _ = StartupAsync();
    }

    // Primeira abertura: pede de uma vez as permissões que faltam e, em seguida, o acesso a todos os arquivos.
    void AskFirstRunPermissions()
    {
        var missing = FirstRunPermissions.MissingRuntimePermissions(this);
        if (missing.Length > 0)
        {
            RequestPermissions(missing, FirstRunPermissions.RequestCode);
            return;
        }

        AskAllFilesAccessOnFirstRun();
    }

    void AskAllFilesAccessOnFirstRun()
    {
        FirstRunPermissions.MarkDone(this);
        if (!ModelStorage.HasAccess(this))
            ModelStorage.RequestAccess(this);
    }

    async Task StartupAsync()
    {
        status!.Text = "Carregando catálogo de espécies...";
        await LoadCatalogOrInstalledPackAsync();
        RecoverBackgroundDownload();
        await InitializeEngineAsync();
        StartDownloadPolling();
        startupFinished = true;
        _ = CheckRegionPacksAsync();
    }

    protected override void OnResume()
    {
        base.OnResume();
        RefreshPoints();
        RefreshFeed();

        // A tela de publicação pede para abrir a identificação ao sair.
        if (OpenIdentifyOnResume)
        {
            OpenIdentifyOnResume = false;
            SetIdentifyOpen(true);
        }
        RefreshDownloadState();
        StartDownloadPolling();
        CheckPackBuildResult();

        // Antes do fim da inicialização, quem carrega o pack e verifica a região é a própria inicialização.
        if (startupFinished)
        {
            _ = SyncActivePackAsync();
            _ = CheckRegionPacksAsync();
        }
    }

    protected override void OnPause()
    {
        base.OnPause();
        pollingDownload = false;
    }

    // Consulta o estado gravado pelo serviço de download enquanto a tela está aberta,
    // para mostrar o progresso e carregar o modelo assim que ele terminar de baixar.
    void StartDownloadPolling()
    {
        if (pollingDownload)
            return;

        pollingDownload = true;
        uiHandler?.PostDelayed(PollDownload, 1000);
    }

    void PollDownload()
    {
        if (!pollingDownload)
            return;

        RefreshDownloadState();

        var state = BioClipDownloadState.Load();
        if (state.Status is "downloading" or "resuming")
            uiHandler?.PostDelayed(PollDownload, 1000);
        else
            pollingDownload = false;
    }

    void RecoverBackgroundDownload()
    {
        try
        {
            if (File.Exists(FileSystem.ModelPath))
                return;

            var state = BioClipDownloadState.Load();
            if (File.Exists(FileSystem.ModelTempPath) || state.Status is "downloading" or "resuming" or "paused" or "interrupted")
                StartBioClipDownloadService();
        }
        catch
        {
            // A falha de recuperação não impede o aplicativo de abrir.
        }
    }

    async Task InitializeEngineAsync()
    {
        if (engineLoading)
            return;

        engineLoading = true;
        try
        {
            var modelPath = FileSystem.ModelPath;
            if (File.Exists(modelPath))
            {
                status!.Text = "Carregando BioCLIP offline...";
                var newEngine = await Task.Run(() => new BioClipOnnxEngine(modelPath));
                engine?.Dispose();
                engine = newEngine;
                engineLoadFailed = false;
            }
        }
        catch (Exception ex)
        {
            engineLoadFailed = true;
            result!.Text = $"Não foi possível carregar o BioCLIP offline: {ex.Message}";
        }
        finally
        {
            engineLoading = false;
        }

        RefreshDownloadState();
        UpdateEngineStatus();
    }

    void RefreshDownloadState()
    {
        var state = BioClipDownloadState.Load();
        if (state.Status is "downloading" or "resuming")
        {
            downloadModelButton!.Enabled = false;
            status!.Text = state.TotalBytes > 0
                ? $"Baixando BioCLIP offline... {state.Percent}%"
                : "Conectando ao servidor de download... pode levar alguns segundos.";
            return;
        }

        downloadModelButton!.Enabled = true;

        // O download terminou em segundo plano: carrega o modelo sem exigir reiniciar o app.
        if (engine?.IsReady != true && !engineLoading && !engineLoadFailed && File.Exists(FileSystem.ModelPath)
            && !PackBuildStateStore.Load(FileSystem.PackBuildStatePath).IsActive(DateTime.UtcNow))
        {
            _ = InitializeEngineAsync();
            return;
        }

        if (state.Status == "paused" || state.Status == "interrupted")
        {
            status!.Text = File.Exists(FileSystem.ModelTempPath)
                ? $"Download do BioCLIP interrompido em {state.Percent}%. Toque para retomar."
                : "Download do BioCLIP interrompido. Toque para tentar novamente.";
            if (!string.IsNullOrWhiteSpace(state.Error))
                result!.Text = state.Error;
        }
    }

    void RequestNotificationPermissionIfNeeded()
    {
        if (Build.VERSION.SdkInt < BuildVersionCodes.Tiramisu)
            return;

        const string permission = "android.permission.POST_NOTIFICATIONS";
        if (CheckSelfPermission(permission) != Permission.Granted)
            RequestPermissions(new[] { permission }, NotificationPermissionRequest);
    }

    void BuildUi()
    {
        var frame = new FrameLayout(this);

        // Tela principal (HUD): cabeçalho de ponta a ponta (avatar, logo e pontuação) e feed de exemplo.
        var page = new LinearLayout(this) { Orientation = Orientation.Vertical };
        ApplyHudBackground(page);
        page.SetOnApplyWindowInsetsListener(new SystemBarInsetsListener(0, 0));

        var header = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        header.SetGravity(GravityFlags.CenterVertical);
        header.SetPadding(Dp(12), Dp(10), Dp(12), Dp(10));
        header.Background = Rounded(HudCard, 0);

        // Avatar circular com contorno verde (foto real entra com a rede social).
        var avatar = new Button(this) { Text = ScoreStore.DisplayName.Substring(0, 1).ToUpperInvariant(), TextSize = 18 };
        avatar.SetAllCaps(false);
        avatar.SetTextColor(HudText);
        avatar.Background = Circle(HudCard, HudGreen, 2);
        avatar.Click += (_, _) => OpenDrawer();
        header.AddView(avatar, new LinearLayout.LayoutParams(Dp(52), Dp(52)));

        // Logo centralizada: a espaço fixo à esquerda e à direita mantém o centro da tela.
        var logo = new ImageView(this);
        logo.SetImageResource(Resource.Drawable.logo_biodex);
        logo.SetScaleType(ImageView.ScaleType.FitCenter);
        header.AddView(logo, new LinearLayout.LayoutParams(0, Dp(56), 1f));

        // Painel de usuário e pontuação: abre o ranking.
        pointsChip = new Button(this) { TextSize = 13 };
        pointsChip.SetAllCaps(false);
        pointsChip.SetTextColor(HudText);
        pointsChip.Background = Rounded(HudCard, 14, HudGreen, 2);
        pointsChip.Click += (_, _) => StartActivity(new Intent(this, typeof(RankingActivity)));
        header.AddView(pointsChip, new LinearLayout.LayoutParams(-2, -2));
        page.AddView(header, new LinearLayout.LayoutParams(-1, -2));

        // Linha verde luminosa separando o cabeçalho do feed.
        var headerLine = new View(this);
        headerLine.SetBackgroundColor(HudGreen);
        page.AddView(headerLine, new LinearLayout.LayoutParams(-1, Dp(2)));

        var feedScroll = new ScrollView(this);
        feedLayout = new LinearLayout(this) { Orientation = Orientation.Vertical };
        feedLayout.SetPadding(Dp(16), Dp(16), Dp(16), Dp(120));
        RefreshFeed();
        feedScroll.AddView(feedLayout);
        page.AddView(feedScroll, new LinearLayout.LayoutParams(-1, 0, 1f));
        frame.AddView(page, new FrameLayout.LayoutParams(-1, -1));

        // Painel da lupa: foto, identificação e resultados.
        identifyOverlay = new LinearLayout(this) { Orientation = Orientation.Vertical, Visibility = ViewStates.Gone };
        ApplyHudBackground(identifyOverlay);
        identifyOverlay.SetPadding(Dp(16), Dp(12), Dp(16), Dp(16));
        identifyOverlay.SetOnApplyWindowInsetsListener(new SystemBarInsetsListener(16, 12));

        var closeIdentify = new Button(this) { Text = "✕ Fechar" };
        closeIdentify.Click += (_, _) => SetIdentifyOpen(false);
        identifyOverlay.AddView(closeIdentify, new LinearLayout.LayoutParams(-2, -2));

        var identifyScroll = new ScrollView(this);
        // Espaço embaixo do conteúdo para o rodapé (que fica por cima do painel) não cobrir os botões.
        var identifyContent = new LinearLayout(this) { Orientation = Orientation.Vertical };
        identifyContent.SetPadding(0, 0, 0, Dp(72));
        identifyScroll.AddView(identifyContent);
        identifyOverlay.AddView(identifyScroll, new LinearLayout.LayoutParams(-1, 0, 1f));

        preview = new ImageView(this);
        preview.SetBackgroundColor(Color.Rgb(238, 238, 238));
        preview.SetScaleType(ImageView.ScaleType.FitCenter);
        identifyContent.AddView(preview, new LinearLayout.LayoutParams(-1, Dp(300)));

        var camera = new Button(this) { Text = "Tirar foto" };
        camera.Click += (_, _) => TakePhoto();
        identifyContent.AddView(camera);

        var gallery = new Button(this) { Text = "Escolher da galeria" };
        gallery.Click += (_, _) => PickImage();
        identifyContent.AddView(gallery);

        identifyButton = new Button(this) { Text = "Identificar" };
        identifyButton.Enabled = false;
        identifyButton.Click += async (_, _) => await IdentifyAsync();
        identifyContent.AddView(identifyButton);

        identifyStatus = new TextView(this) { TextSize = 13 };
        identifyStatus.SetPadding(0, 12, 0, 4);
        identifyContent.AddView(identifyStatus);

        result = new TextView(this) { TextSize = 15 };
        result.SetPadding(0, 4, 0, 0);
        identifyContent.AddView(result);

        matchImages = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        matchImages.SetPadding(0, 12, 0, 0);
        identifyContent.AddView(matchImages);

        alternatives = new TextView(this) { TextSize = 15 };
        alternatives.SetPadding(0, 12, 0, 0);
        identifyContent.AddView(alternatives);

        // Aparece depois de uma identificação: leva a foto e a pontuação para a tela de publicação.
        postFromIdentifyButton = new Button(this) { Text = "📝 Postar esta foto" };
        postFromIdentifyButton.SetAllCaps(false);
        postFromIdentifyButton.Visibility = ViewStates.Gone;
        postFromIdentifyButton.Click += (_, _) =>
        {
            var intent = new Intent(this, typeof(PostComposerActivity));
            if (!string.IsNullOrWhiteSpace(identifiedPhotoPath))
                intent.PutExtra(PostComposerActivity.ExtraPhotoPath, identifiedPhotoPath);
            intent.PutExtra(PostComposerActivity.ExtraPoints, lastAwardedPoints);
            StartActivity(intent);
        };
        identifyContent.AddView(postFromIdentifyButton, new LinearLayout.LayoutParams(-1, -2) { TopMargin = Dp(12) });
        frame.AddView(identifyOverlay, new FrameLayout.LayoutParams(-1, -1));

        // Rodapé de navegação (casinha, +, lupa, avião): fica sobre a tela inicial e o painel de identificação.
        frame.AddView(BottomNavigation.Create(this, () => SetIdentifyOpen(true), CloseHomePanels),
            new FrameLayout.LayoutParams(-1, -2, GravityFlags.Bottom));

        // Painel lateral (ao tocar no avatar): tema, packs e modelo offline.
        drawerScrim = new View(this) { Visibility = ViewStates.Gone };
        drawerScrim.SetBackgroundColor(Color.Argb(110, 0, 0, 0));
        drawerScrim.Click += (_, _) => CloseDrawer();
        frame.AddView(drawerScrim, new FrameLayout.LayoutParams(-1, -1));

        drawerPanel = new LinearLayout(this) { Orientation = Orientation.Vertical, Visibility = ViewStates.Gone };
        ApplyHudBackground(drawerPanel);
        drawerPanel.SetPadding(Dp(16), Dp(24), Dp(16), Dp(16));
        drawerPanel.SetOnApplyWindowInsetsListener(new SystemBarInsetsListener(16, 24));

        var drawerHeader = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        drawerHeader.SetGravity(GravityFlags.CenterVertical);
        var brand = new TextView(this) { Text = "Biodex", TextSize = 22 };
        brand.SetTypeface(null, TypefaceStyle.Bold);
        drawerHeader.AddView(brand, new LinearLayout.LayoutParams(0, -2, 1f));
        var closeDrawer = new Button(this) { Text = "✕" };
        closeDrawer.Click += (_, _) => CloseDrawer();
        drawerHeader.AddView(closeDrawer, new LinearLayout.LayoutParams(-2, -2));
        drawerPanel.AddView(drawerHeader, new LinearLayout.LayoutParams(-1, -2));

        var drawerScroll = new ScrollView(this);
        var drawerContent = new LinearLayout(this) { Orientation = Orientation.Vertical };
        drawerScroll.AddView(drawerContent);
        drawerPanel.AddView(drawerScroll, new LinearLayout.LayoutParams(-1, 0, 1f));

        AddSection(drawerContent, "Aparência e sistema");
        var themeRow = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        themeRow.SetGravity(GravityFlags.CenterVertical);
        themeRow.AddView(new TextView(this) { Text = "Tema", TextSize = 15 }, new LinearLayout.LayoutParams(0, -2, 1f));

        var themeSpinner = new Spinner(this);
        themeSpinner.Adapter = new ArrayAdapter<string>(this, Android.Resource.Layout.SimpleSpinnerItem, ThemeModes.Labels);
        themeSpinner.SetSelection(ThemeModes.IndexOf(AppSettings.GetThemeMode()));
        themeSpinner.ItemSelected += (_, args) => OnThemeSelected(args.Position);
        themeRow.AddView(themeSpinner, new LinearLayout.LayoutParams(0, -2, 2f));
        drawerContent.AddView(themeRow, new LinearLayout.LayoutParams(-1, -2));

        AddSection(drawerContent, "FaunaPacks");
        var importPackButton = new Button(this) { Text = "📦 Importar FaunaPack" };
        importPackButton.Click += async (_, _) => await PickPackAsync();
        drawerContent.AddView(importPackButton);

        var managePackButton = new Button(this) { Text = "📚 Gerenciar FaunaPacks" };
        managePackButton.Click += (_, _) => StartManagePacks();
        drawerContent.AddView(managePackButton);

        var createPackButton = new Button(this) { Text = "🧭 Criar FaunaPack por região" };
        createPackButton.Click += (_, _) => StartPackBuilder();
        drawerContent.AddView(createPackButton);

        AddSection(drawerContent, "BioCLIP offline");
        downloadModelButton = new Button(this) { Text = "Baixar BioCLIP offline" };
        downloadModelButton.Click += async (_, _) => await DownloadModelAsync();
        drawerContent.AddView(downloadModelButton);

        var importModelButton = new Button(this) { Text = "Importar BioCLIP ONNX" };
        importModelButton.Click += (_, _) => PickModel();
        drawerContent.AddView(importModelButton);

        endpointInput = new EditText(this)
        {
            Hint = "Servidor BioCLIP 2 opcional (ex.: https://servidor)"
        };
        endpointInput.Text = GetPreferences(FileCreationMode.Private)?.GetString("remote_endpoint", "") ?? "";
        drawerContent.AddView(endpointInput);

        drawerStatus = new TextView(this) { TextSize = 13 };
        drawerStatus.SetPadding(0, 12, 0, 4);
        drawerContent.AddView(drawerStatus);

        // Mensagens de status aparecem no painel lateral e no de identificação.
        status = new MirroredStatus(identifyStatus, drawerStatus);

        frame.AddView(drawerPanel, new FrameLayout.LayoutParams(
            Math.Min(Dp(340), (int)(Resources!.DisplayMetrics!.WidthPixels * 0.85f)),
            -1,
            GravityFlags.Start));

        SetContentView(frame);
        RefreshPoints();
    }

    void AddSection(LinearLayout content, string title)
    {
        var label = new TextView(this) { Text = title, TextSize = 13 };
        label.SetTypeface(null, TypefaceStyle.Bold);
        label.SetPadding(0, Dp(16), 0, Dp(4));
        content.AddView(label);
    }

    void RefreshPoints()
    {
        pointsChip!.Text = $"🏆 {ScoreStore.DisplayName}\n★ {ScoreStore.TotalPoints:N0} pts";
    }

    // Cores do HUD, seguindo a especificação: verde-escuro com verde luminoso no escuro; claro no tema claro.
    bool IsDarkMode => HudBackground.IsDark(this);
    Color HudCard => IsDarkMode ? Color.Rgb(0, 29, 10) : Color.Rgb(255, 255, 255);
    Color HudGreen => Color.Rgb(32, 230, 0);
    Color HudText => IsDarkMode ? Color.Rgb(245, 245, 245) : Color.Rgb(16, 24, 16);
    Color HudMuted => IsDarkMode ? Color.Rgb(168, 168, 168) : Color.Rgb(90, 100, 90);

    Android.Graphics.Drawables.GradientDrawable Rounded(Color fill, int radiusDp, Color? stroke = null, int strokeDp = 0)
    {
        var shape = new Android.Graphics.Drawables.GradientDrawable();
        shape.SetCornerRadius(Dp(radiusDp));
        shape.SetColor(fill);
        if (stroke is Color line && strokeDp > 0)
            shape.SetStroke(Dp(strokeDp), line);
        return shape;
    }

    Android.Graphics.Drawables.GradientDrawable Circle(Color fill, Color stroke, int strokeDp)
    {
        var shape = Rounded(fill, 999, stroke, strokeDp);
        shape.SetShape(Android.Graphics.Drawables.ShapeType.Oval);
        return shape;
    }

    void Toast(string text)
    {
        Android.Widget.Toast.MakeText(this, text, ToastLength.Short)?.Show();
    }

    // Publicações do usuário (mais recentes primeiro), seguidas dos exemplos.
    void RefreshFeed()
    {
        if (feedLayout is null)
            return;

        feedLayout.RemoveAllViews();
        foreach (var post in PostStore.Load(this))
            feedLayout.AddView(BuildUserPostCard(post), new LinearLayout.LayoutParams(-1, -2) { BottomMargin = Dp(16) });

        AddSamplePosts(feedLayout);
    }

    View BuildUserPostCard(SavedPost post)
    {
        var card = new LinearLayout(this) { Orientation = Orientation.Vertical };
        card.SetPadding(Dp(14), Dp(14), Dp(14), Dp(14));
        card.Background = Rounded(HudCard, 16, HudGreen, 2);

        var top = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        top.SetGravity(GravityFlags.CenterVertical);

        var initial = string.IsNullOrWhiteSpace(post.Author) ? "?" : post.Author.Substring(0, 1).ToUpperInvariant();
        var avatar = new TextView(this) { Text = initial, TextSize = 16 };
        avatar.Gravity = GravityFlags.Center;
        avatar.SetTextColor(Color.White);
        avatar.Background = Circle(Color.Rgb(40, 110, 40), HudGreen, 2);
        top.AddView(avatar, new LinearLayout.LayoutParams(Dp(46), Dp(46)));

        var name = new TextView(this) { Text = post.Author, TextSize = 16 };
        name.SetTypeface(null, TypefaceStyle.Bold);
        name.SetTextColor(HudText);
        name.SetPadding(Dp(12), 0, 0, 0);
        top.AddView(name, new LinearLayout.LayoutParams(0, -2, 1f));
        card.AddView(top);

        if (!string.IsNullOrWhiteSpace(post.Caption))
        {
            var caption = new TextView(this) { Text = post.Caption, TextSize = 15 };
            caption.SetTextColor(HudText);
            caption.SetPadding(0, Dp(10), 0, 0);
            card.AddView(caption);
        }

        // Ordem: legenda, pontos da identificação, fotos.
        if (post.IdentificationPoints > 0)
        {
            var points = new TextView(this) { Text = $"🏆 +{post.IdentificationPoints} pontos da identificação", TextSize = 13 };
            points.SetTextColor(HudGreen);
            points.SetPadding(0, Dp(8), 0, 0);
            card.AddView(points);
        }

        if (post.ImagePaths.Count > 0)
            card.AddView(BuildPostMedia(post.ImagePaths), new LinearLayout.LayoutParams(-1, -2) { TopMargin = Dp(12) });

        return card;
    }

    // Mídia da publicação no feed, no estilo de X, Instagram e Threads.
    // Uma foto ocupa a largura inteira, com altura máxima de 4:5 (retrato). Várias fotos formam uma grade:
    // 2 lado a lado; 3 com uma grande à esquerda; 4 em grade 2x2. Com mais de 4, a última mostra "+N".
    // A largura é a da área útil do card: tela menos as margens do feed (16 dp) e do card (14 dp).
    View BuildPostMedia(IReadOnlyList<string> paths)
    {
        var width = Resources!.DisplayMetrics!.WidthPixels - Dp(60);
        var gap = Dp(3);
        var half = (width - gap) / 2;

        if (paths.Count == 1)
        {
            var single = LoadMedia(paths[0]);
            var height = single is null || single.Width == 0
                ? width
                : Math.Min((int)(width * (double)single.Height / single.Width), width * 5 / 4);
            height = Math.Max(height, Dp(120));
            var wrapper = new LinearLayout(this) { Orientation = Orientation.Vertical };
            wrapper.AddView(MediaCell(single, width, height, paths, 0), new LinearLayout.LayoutParams(width, height));
            return wrapper;
        }

        var overflow = paths.Count > 4 ? paths.Count - 4 : 0;
        var shown = paths.Take(4).Select(LoadMedia).ToList();

        if (paths.Count == 2)
        {
            var row = new LinearLayout(this) { Orientation = Orientation.Horizontal };
            row.AddView(MediaCell(shown[0], half, half, paths, 0), new LinearLayout.LayoutParams(half, half) { RightMargin = gap });
            row.AddView(MediaCell(shown[1], half, half, paths, 0), new LinearLayout.LayoutParams(half, half));
            return row;
        }

        if (paths.Count == 3)
        {
            var row = new LinearLayout(this) { Orientation = Orientation.Horizontal };
            row.AddView(MediaCell(shown[0], half, half * 2 + gap, paths, 0), new LinearLayout.LayoutParams(half, half * 2 + gap) { RightMargin = gap });
            var column = new LinearLayout(this) { Orientation = Orientation.Vertical };
            column.AddView(MediaCell(shown[1], half, half, paths, 0), new LinearLayout.LayoutParams(half, half) { BottomMargin = gap });
            column.AddView(MediaCell(shown[2], half, half, paths, 0), new LinearLayout.LayoutParams(half, half));
            row.AddView(column, new LinearLayout.LayoutParams(half, half * 2 + gap));
            return row;
        }

        var grid = new LinearLayout(this) { Orientation = Orientation.Vertical };
        var top = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        top.AddView(MediaCell(shown[0], half, half, paths, 0), new LinearLayout.LayoutParams(half, half) { RightMargin = gap, BottomMargin = gap });
        top.AddView(MediaCell(shown[1], half, half, paths, 0), new LinearLayout.LayoutParams(half, half) { BottomMargin = gap });
        grid.AddView(top);
        var bottom = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        bottom.AddView(MediaCell(shown[2], half, half, paths, 0), new LinearLayout.LayoutParams(half, half) { RightMargin = gap });
        bottom.AddView(MediaCell(shown[3], half, half, paths, overflow), new LinearLayout.LayoutParams(half, half));
        grid.AddView(bottom);
        return grid;
    }

    Bitmap? LoadMedia(string path)
    {
        try
        {
            return ImageLoader.Load(path, 1080);
        }
        catch
        {
            return null;
        }
    }

    // Uma célula da grade: a foto recortada para preencher a área; toque abre a publicação.
    // overflow > 0 mostra "+N" sobre a foto (fotos que não cabem na grade).
    View MediaCell(Bitmap? bitmap, int width, int height, IReadOnlyList<string> all, int overflow)
    {
        var frame = new FrameLayout(this);
        frame.SetBackgroundColor(Color.Rgb(30, 30, 30));

        var image = new ImageView(this);
        image.SetScaleType(ImageView.ScaleType.CenterCrop);
        if (bitmap is not null)
            image.SetImageBitmap(bitmap);
        frame.AddView(image, new FrameLayout.LayoutParams(-1, -1));

        if (overflow > 0)
        {
            var more = new TextView(this) { Text = $"+{overflow}", TextSize = 26 };
            more.Gravity = GravityFlags.Center;
            more.SetTextColor(Color.White);
            more.SetTypeface(null, TypefaceStyle.Bold);
            more.SetBackgroundColor(Color.Argb(140, 0, 0, 0));
            frame.AddView(more, new FrameLayout.LayoutParams(-1, -1));
        }

        frame.Click += (_, _) => OpenPost(all);
        return frame;
    }

    // Abre a publicação em tela cheia, com as fotos lado a lado.
    void OpenPost(IReadOnlyList<string> paths)
    {
        var intent = new Intent(this, typeof(PostViewerActivity));
        intent.PutStringArrayListExtra(PostViewerActivity.ExtraPaths, paths.ToList());
        StartActivity(intent);
    }

    // Publicações de exemplo, só para visualizar o feed. Nomes e imagens são fictícios.
    void AddSamplePosts(LinearLayout feed)
    {
        var samples = new (string Name, int Points, Color Tint)[]
        {
            ("MiauPlayer", 25, Color.Rgb(120, 90, 60)),
            ("DogMaster", 25, Color.Rgb(200, 160, 90)),
            ("PetLover", 25, Color.Rgb(40, 40, 40))
        };

        foreach (var sample in samples)
            feed.AddView(BuildPostCard(sample.Name, sample.Points, sample.Tint),
                new LinearLayout.LayoutParams(-1, -2) { BottomMargin = Dp(16) });
    }

    View BuildPostCard(string name, int points, Color tint)
    {
        var card = new LinearLayout(this) { Orientation = Orientation.Vertical };
        card.SetPadding(Dp(14), Dp(14), Dp(14), Dp(14));
        card.Background = Rounded(HudCard, 16, HudGreen, 2);

        var top = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        top.SetGravity(GravityFlags.CenterVertical);

        var avatar = new TextView(this) { Text = name.Substring(0, 1), TextSize = 16 };
        avatar.Gravity = GravityFlags.Center;
        avatar.SetTextColor(Color.White);
        avatar.Background = Circle(tint, HudGreen, 2);
        top.AddView(avatar, new LinearLayout.LayoutParams(Dp(46), Dp(46)));

        var texts = new LinearLayout(this) { Orientation = Orientation.Vertical };
        texts.SetPadding(Dp(12), 0, 0, 0);

        var nameView = new TextView(this) { Text = name, TextSize = 16 };
        nameView.SetTypeface(null, TypefaceStyle.Bold);
        nameView.SetTextColor(HudText);
        texts.AddView(nameView);

        var description = new TextView(this) { Text = "Adicione uma descrição…", TextSize = 13 };
        description.SetTextColor(HudMuted);
        texts.AddView(description);

        var reward = new TextView(this) { Text = $"🏆 + {points}", TextSize = 13 };
        reward.SetTextColor(HudGreen);
        texts.AddView(reward);
        top.AddView(texts, new LinearLayout.LayoutParams(0, -2, 1f));

        var menu = new TextView(this) { Text = "⋮", TextSize = 22 };
        menu.SetTextColor(HudMuted);
        menu.Click += (_, _) => Toast("Opções da publicação entram com a rede social.");
        top.AddView(menu, new LinearLayout.LayoutParams(Dp(32), -2));
        card.AddView(top);

        // Espaço da imagem (proporção próxima de 16:9). A foto real entra com a rede social.
        var image = new FrameLayout(this);
        image.Background = Rounded(tint, 12);
        var caption = new TextView(this) { Text = "imagem de exemplo", TextSize = 13 };
        caption.Gravity = GravityFlags.Center;
        caption.SetTextColor(Color.White);
        image.AddView(caption, new FrameLayout.LayoutParams(-1, -1));
        card.AddView(image, new LinearLayout.LayoutParams(-1, Dp(200)) { TopMargin = Dp(12) });

        return card;
    }

    // Botão de início dentro da tela inicial: fecha a lupa e o painel lateral, deixando o feed à vista.
    void CloseHomePanels()
    {
        SetIdentifyOpen(false);
        CloseDrawer();
    }

    void SetIdentifyOpen(bool open)
    {
        identifyOverlay!.Visibility = open ? ViewStates.Visible : ViewStates.Gone;
    }

    void OpenDrawer()
    {
        drawerScrim!.Visibility = ViewStates.Visible;
        drawerPanel!.Visibility = ViewStates.Visible;
    }

    void CloseDrawer()
    {
        drawerScrim!.Visibility = ViewStates.Gone;
        drawerPanel!.Visibility = ViewStates.Gone;
    }

    // Botão voltar fecha primeiro o painel lateral, depois o de identificação.
    public override void OnBackPressed()
    {
        if (drawerPanel?.Visibility == ViewStates.Visible)
        {
            CloseDrawer();
            return;
        }

        if (identifyOverlay?.Visibility == ViewStates.Visible)
        {
            SetIdentifyOpen(false);
            return;
        }

        base.OnBackPressed();
    }

    // Fundo do app (claro ou escuro, conforme o tema): ver HudBackground.
    void ApplyHudBackground(View view) => HudBackground.Apply(view, this);

    async Task LoadCatalogOrInstalledPackAsync()
    {
        try
        {
            // Packs e modelo de versões antigas passam para Download/BioDex (só quando há acesso).
            await Task.Run(ModelStorage.MigrateLegacyFolders);

            // Sem FaunaPack marcado, o app começa vazio: não há demonstração embutida no APK.
            var packs = await packManager.LoadEnabledAsync();
            installedPacks = packs;
            catalog.LoadPacks(packs);
        }
        catch
        {
            installedPacks = Array.Empty<FaunaPackData>();
            catalog.Clear();
        }
    }

    void UpdateEngineStatus()
    {
        var modelStatus = engine?.IsReady == true
            ? "BioCLIP offline pronto."
            : "BioCLIP offline não instalado. Toque em “Baixar BioCLIP offline”.";

        var packStatus = installedPacks.Count == 0
            ? "Nenhum FaunaPack ativo. Crie um em “Criar FaunaPack por região” ou importe um .faunapack."
            : $"FaunaPacks ativos: {installedPacks.Count} ({catalog.Count} espécies). Marque ou desmarque em “Gerenciar FaunaPacks”.";

        status!.Text = modelStatus + "\n" + packStatus;
    }

    void TakePhoto()
    {
        if (CheckSelfPermission(Android.Manifest.Permission.Camera) != Permission.Granted)
        {
            RequestPermissions(new[] { Android.Manifest.Permission.Camera }, PermissionRequest);
            return;
        }

        var directory = new Java.IO.File(FileSystem.PhotoDirectory);
        directory.Mkdirs();

        pendingPath = System.IO.Path.Combine(
            directory.AbsolutePath,
            $"capture_{DateTime.UtcNow:yyyyMMdd_HHmmssfff}.jpg");

        var file = new Java.IO.File(pendingPath);
        pendingUri = FileProvider.GetUriForFile(this, $"{PackageName}.fileprovider", file);

        var intent = new Intent(MediaStore.ActionImageCapture);
        intent.PutExtra(MediaStore.ExtraOutput, pendingUri);
        intent.AddFlags(ActivityFlags.GrantWriteUriPermission | ActivityFlags.GrantReadUriPermission);
        StartActivityForResult(intent, CameraRequest);
    }

    public override void OnRequestPermissionsResult(int requestCode, string[] permissions, Permission[] grantResults)
    {
        base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
        if (requestCode == FirstRunPermissions.RequestCode)
        {
            AskAllFilesAccessOnFirstRun();
            return;
        }

        if (requestCode == PermissionRequest && grantResults.Length > 0 && grantResults[0] == Permission.Granted)
            TakePhoto();
    }

    async Task DownloadModelAsync()
    {
        if (engine?.IsReady == true)
        {
            result!.Text = "BioCLIP offline já está instalado.";
            return;
        }

        if (!ModelStorage.HasAccess(this))
        {
            status!.Text = "Libere o acesso a arquivos para salvar o BioCLIP em Download/BioDex/Biodex_Models. Depois toque em \"Baixar BioCLIP offline\" de novo.";
            ModelStorage.RequestAccess(this);
            return;
        }

        await Task.Run(ModelStorage.MigrateLegacyModels);

        var state = BioClipDownloadState.Load();
        if (state.Status is "downloading" or "resuming")
        {
            status!.Text = $"O download já está em andamento ({state.Percent}%). Ele continua mesmo se o app for fechado.";
            return;
        }

        result!.Text = string.Empty;
        status!.Text = File.Exists(FileSystem.ModelTempPath)
            ? "Retomando download do BioCLIP offline..."
            : "Iniciando download do BioCLIP offline...";

        try
        {
            engineLoadFailed = false;
            RequestNotificationPermissionIfNeeded();
            StartBioClipDownloadService();
            downloadModelButton!.Enabled = false;
            StartDownloadPolling();
            await Task.Delay(250);
            RefreshDownloadState();
        }
        catch (Exception ex)
        {
            downloadModelButton!.Enabled = true;
            status!.Text = "Não foi possível iniciar o download em segundo plano.";
            result!.Text = ex.Message;
        }
    }

    void StartBioClipDownloadService()
    {
        var intent = new Intent(this, typeof(BioClipDownloadForegroundService));
        intent.SetAction(BioClipDownloadForegroundService.ActionStart);

        if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
            StartForegroundService(intent);
        else
            StartService(intent);
    }

    void StartPackBuilder()
    {
        // A criação roda em segundo plano e carrega o próprio BioCLIP. Libera o desta tela para não manter dois na memória.
        engine?.Dispose();
        engine = null;
        StartActivity(new Intent(this, typeof(PackBuilderActivity)));
    }

    void StartPackUpdate(string directory)
    {
        if (PackBuildStateStore.Load(FileSystem.PackBuildStatePath).IsActive(DateTime.UtcNow))
        {
            result!.Text = "Já existe um FaunaPack sendo criado. Aguarde terminar ou cancele pela tela de criação.";
            return;
        }

        engine?.Dispose();
        engine = null;
        var intent = new Intent(this, typeof(PackBuilderActivity));
        intent.PutExtra(PackBuilderActivity.ExtraUpdatePackDirectory, directory);
        StartActivity(intent);
    }

    // Mostra uma única vez o resultado de uma criação feita em segundo plano e recarrega o pack, se foi criado.
    void CheckPackBuildResult()
    {
        var state = PackBuildStateStore.Load(FileSystem.PackBuildStatePath);
        if (state.Acknowledged || !state.IsFinished)
            return;

        PackBuildStateStore.Acknowledge(FileSystem.PackBuildStatePath);
        if (state.Status == "completed")
        {
            result!.Text = $"FaunaPack \"{state.PackName}\" pronto: {state.ResultSpeciesCount} espécies.";
            _ = ReloadPackAsync();
            return;
        }

        result!.Text = state.Status == "failed"
            ? $"A criação do FaunaPack falhou: {state.Error}"
            : "Criação do FaunaPack cancelada.";
    }

    async Task ReloadPackAsync()
    {
        await LoadCatalogOrInstalledPackAsync();
        if (File.Exists(FileSystem.ModelPath))
            await InitializeEngineAsync();
        UpdateEngineStatus();
    }

    void PickModel()
    {
        if (!ModelStorage.HasAccess(this))
        {
            status!.Text = "Libere o acesso a arquivos para importar o modelo em Download/BioDex/Biodex_Models.";
            ModelStorage.RequestAccess(this);
            return;
        }

        var intent = new Intent(Intent.ActionOpenDocument);
        intent.AddCategory(Intent.CategoryOpenable);
        intent.SetType("application/octet-stream");
        StartActivityForResult(intent, ModelRequest);
    }

    async Task PickPackAsync()
    {
        var intent = new Intent(Intent.ActionOpenDocument);
        intent.AddCategory(Intent.CategoryOpenable);
        intent.SetType("*/*");
        StartActivityForResult(intent, PackRequest);
        await Task.CompletedTask;
    }

    void InstallModel(Uri uri)
    {
        Directory.CreateDirectory(FileSystem.ModelDirectory);
        var temporaryPath = FileSystem.ModelTempPath;

        try
        {
            using var input = ContentResolver!.OpenInputStream(uri)
                ?? throw new InvalidOperationException("Não foi possível abrir o arquivo selecionado.");
            using var output = File.Create(temporaryPath);
            input.CopyTo(output);
            output.Flush();

            BioClipOnnxEngine.Validate(temporaryPath);

            File.Move(temporaryPath, FileSystem.ModelPath, true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }

        engine?.Dispose();
        engine = new BioClipOnnxEngine(FileSystem.ModelPath);
        if (!engine.IsReady)
            throw new InvalidOperationException("O arquivo selecionado não é um modelo ONNX válido.");

        UpdateEngineStatus();
    }

    async Task InstallPackAsync(Uri uri)
    {
        Directory.CreateDirectory(FileSystem.PackDirectory);
        var temporaryPath = System.IO.Path.Combine(
            FileSystem.Root,
            $"faunapack_{Guid.NewGuid():N}.install");

        try
        {
            status!.Text = "Copiando FaunaPack para o armazenamento do aplicativo...";
            await using (var input = ContentResolver!.OpenInputStream(uri)
                ?? throw new InvalidOperationException("Não foi possível abrir o FaunaPack selecionado."))
            await using (var output = new System.IO.FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await input.CopyToAsync(output);
                await output.FlushAsync();
            }

            status.Text = "Validando e instalando FaunaPack...";
            var pack = await Task.Run(
                () => packManager.InstallAsync(temporaryPath).GetAwaiter().GetResult());

            await LoadCatalogOrInstalledPackAsync();
            UpdateEngineStatus();

            result!.Text =
                "FaunaPack instalado e carregado com sucesso.\n\n" +
                $"Pack: {pack.Manifest.PackName}\n" +
                $"Espécies: {pack.Entries.Count}\n" +
                $"Referências por espécie: {pack.Manifest.ReferencesPerSpecies}\n" +
                "\nO BioCLIP offline agora pode comparar a foto com este pacote.";
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    void StartManagePacks()
    {
        StartActivityForResult(new Intent(this, typeof(PackManagerActivity)), ManagePacksRequest);
    }

    // A seleção é feita na tela de gerenciamento: ao voltar, carrega o pack que ficou ativo.
    async Task SyncActivePackAsync()
    {
        try
        {
            await LoadCatalogOrInstalledPackAsync();
            UpdateEngineStatus();
        }
        catch (Exception ex)
        {
            result!.Text = $"Não foi possível carregar os FaunaPacks: {ex.Message}";
        }
    }

    // Só com a opção ligada: detecta o estado pela localização e cria o pack da região quando ela muda.
    async Task CheckRegionPacksAsync()
    {
        if (regionCheckRunning || !AppSettings.IsAutoRegionEnabled())
            return;

        regionCheckRunning = true;
        try
        {
            string? detected = null;
            var now = DateTime.UtcNow;
            if (now - AppSettings.GetLastRegionCheckUtc() >= RegionCheckInterval && LocationRegionDetector.HasPermission(this))
            {
                AppSettings.SetLastRegionCheckUtc(now);
                detected = await LocationRegionDetector.DetectStateAsync(this, CancellationToken.None);
            }

            var installedRegions = (await packManager.ListAsync()).Select(pack => pack.Region).ToList();
            var buildActive = PackBuildStateStore.Load(FileSystem.PackBuildStatePath).IsActive(DateTime.UtcNow);
            var modelReady = File.Exists(FileSystem.ModelPath) && ModelStorage.HasAccess(this);

            var plan = RegionPackPlanner.Plan(AppSettings.GetRegionState(), detected, installedRegions, buildActive, modelReady);
            AppSettings.SetRegionState(plan.Next);

            if (plan.StartRegion is not null)
                StartRegionBuild(plan.StartRegion);
        }
        catch (Exception)
        {
            // Localização indisponível ou arquivo ilegível: tenta de novo na próxima abertura.
        }
        finally
        {
            regionCheckRunning = false;
        }
    }

    void StartRegionBuild(string region)
    {
        // Como na tela de criação, libera o BioCLIP desta tela: o serviço carrega o próprio.
        engine?.Dispose();
        engine = null;

        RequestNotificationPermissionIfNeeded();
        PackBuildLauncher.Start(this, new PackBuildOptions { PackName = $"{region} (automático)", StateProvince = region }.Normalized());
        result!.Text = $"Mudança de região detectada: criando um FaunaPack de {region} em segundo plano. Acompanhe pela notificação.";
    }

    void OnThemeSelected(int position)
    {
        if (position < 0 || position >= ThemeModes.Values.Length)
            return;

        var mode = ThemeModes.Values[position];
        if (mode == AppSettings.GetThemeMode())
            return;

        // Recria a tela para aplicar o tema. A foto aberta não é mantida nessa troca.
        AppSettings.SetThemeMode(mode);
        Recreate();
    }

    protected override void OnSaveInstanceState(Bundle outState)
    {
        base.OnSaveInstanceState(outState);
        if (!string.IsNullOrWhiteSpace(pendingPath))
            outState.PutString(PendingPathKey, pendingPath);
    }

    void PickImage()
    {
        StartActivityForResult(ImagePicker.Create(multiple: false, max: 1), GalleryRequest);
    }

    void LoadImageFromFile(string path, string sourceText)
    {
        var displayBitmap = ImageLoader.Load(path, 1024);
        preview!.SetImageBitmap(displayBitmap);
        previewBitmap?.Recycle();
        previewBitmap?.Dispose();
        previewBitmap = displayBitmap;
        pendingPath = path;

        processedPhotoCount++;
        if (processedPhotoCount >= ReleaseEarlierPhotosAt)
            ReleaseEarlierMatchBitmaps();
        identifyButton!.Enabled = true;
        status!.Text = engine?.IsReady == true
            ? $"{sourceText} pronta para identificação offline.\nCatálogo: {catalog.SourceName}."
            : $"{sourceText} carregada. Baixe o BioCLIP offline para identificar.";
        result!.Text = string.Empty;
    }

    void ImportGalleryImage(Uri uri)
    {
        Directory.CreateDirectory(FileSystem.PhotoDirectory);
        var path = System.IO.Path.Combine(FileSystem.PhotoDirectory, $"gallery_{DateTime.UtcNow:yyyyMMdd_HHmmssfff}.jpg");

        using (var input = ContentResolver!.OpenInputStream(uri)
            ?? throw new InvalidOperationException("Não foi possível abrir a imagem selecionada."))
        using (var output = File.Create(path))
            input.CopyTo(output);

        try
        {
            LoadImageFromFile(path, "Imagem da galeria");
        }
        catch
        {
            if (File.Exists(path))
                File.Delete(path);
            throw;
        }
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        // Alguns apps de câmera devolvem "cancelado" mesmo com a foto salva; por isso a câmera é verificada pelo arquivo.
        if (resultCode != Result.Ok && requestCode != CameraRequest)
            return;

        // A tela de gerenciamento devolve OK só quando o usuário pede para atualizar um pack.
        if (requestCode == ManagePacksRequest)
        {
            var directory = data?.GetStringExtra(PackBuilderActivity.ExtraUpdatePackDirectory);
            if (!string.IsNullOrWhiteSpace(directory))
                StartPackUpdate(directory);
            return;
        }

        if (requestCode == ModelRequest && data?.Data is not null)
        {
            try
            {
                InstallModel(data.Data);
                result!.Text = "BioCLIP offline instalado com sucesso.";
            }
            catch (Exception ex)
            {
                result!.Text = $"Falha ao instalar o modelo: {ex.Message}";
            }
            return;
        }

        if (requestCode == PackRequest && data?.Data is not null)
        {
            _ = InstallPackFromActivityResultAsync(data.Data);
            return;
        }

        if (requestCode == GalleryRequest && data?.Data is not null)
        {
            try
            {
                ImportGalleryImage(data.Data);
            }
            catch (Exception ex)
            {
                result!.Text = $"Falha ao carregar a imagem: {ex.Message}";
                status!.Text = "Não foi possível carregar a imagem da galeria.";
            }
            return;
        }

        if (requestCode != CameraRequest || string.IsNullOrWhiteSpace(pendingPath))
            return;

        if (!File.Exists(pendingPath) || new FileInfo(pendingPath).Length == 0)
        {
            result!.Text = "A câmera não salvou a foto. Tente de novo.";
            return;
        }

        try
        {
            LoadImageFromFile(pendingPath, "Foto capturada");
        }
        catch (Exception ex)
        {
            result!.Text = $"Falha ao carregar a foto: {ex.Message}";
            status!.Text = "Não foi possível carregar a foto capturada.";
        }
    }

    async Task InstallPackFromActivityResultAsync(Uri uri)
    {
        try
        {
            await InstallPackAsync(uri);
        }
        catch (Exception ex)
        {
            result!.Text = $"Falha ao instalar o FaunaPack: {ex.Message}";
            UpdateEngineStatus();
        }
    }

    // Libera as fotos de referência de identificações anteriores. Remove as views antes, para nunca desenhar bitmap reciclado.
    void ReleaseEarlierMatchBitmaps()
    {
        matchImages?.RemoveAllViews();
        foreach (var bitmap in matchBitmaps)
        {
            bitmap.Recycle();
            bitmap.Dispose();
        }
        matchBitmaps.Clear();
    }

    // Mostra as imagens de referência do animal mais provável, só quando a identificação é confiante.
    // Outras possibilidades ficam logo abaixo das fotos. Toque numa foto para abri-la em tamanho maior.
    void ShowMatchImages(IReadOnlyList<SpeciesMatch> matches)
    {
        matchImages!.RemoveAllViews();
        alternatives!.Text = ResultFormatter.FormatAlternatives(matches);
        if (matches.Count == 0 || matches[0].Score < ResultFormatter.MinConfidentScore)
            return;

        foreach (var path in matches[0].Species.ImagePaths.Take(Species.ShownReferenceImages))
        {
            try
            {
                var view = new ImageView(this);
                view.SetBackgroundColor(Color.Rgb(238, 238, 238));
                view.SetScaleType(ImageView.ScaleType.CenterCrop);
                var bitmap = ImageLoader.Load(path, 320);
                matchBitmaps.Add(bitmap);
                view.SetImageBitmap(bitmap);
                view.Click += (_, _) => StartActivity(ImageViewerActivity.CreateIntent(this, path));

                var layout = new LinearLayout.LayoutParams(0, Dp(160), 1f) { RightMargin = Dp(8) };
                matchImages.AddView(view, layout);
            }
            catch
            {
                // Uma imagem que não abre não impede as outras nem o resultado.
            }
        }
    }

    // Só identificações confiantes geram pontos (ou registro). Texto curto, adicionado ao resultado.
    string ScoreNote(IReadOnlyList<SpeciesMatch> matches)
    {
        if (matches.Count == 0 || matches[0].Score < ResultFormatter.MinConfidentScore)
            return "";

        var outcome = new ScoreStore().Record(matches[0].Species);
        lastAwardedPoints = outcome.Earned;

        var lines = new List<string>();
        if (outcome.Risk == SpeciesRisk.Threatened)
        {
            lines.Add("Espécie ameaçada: registrada, sem pontos.");
        }
        else
        {
            var repeat = outcome.PreviousRecords > 0 ? $" (registro nº {outcome.PreviousRecords + 1})" : "";
            lines.Add($"Pontuação individual: {outcome.Awarded}{repeat}");
            if (outcome.FirstDiscovery > 0)
                lines.Add($"+{outcome.FirstDiscovery} primeira descoberta");
            if (outcome.Collector > 0)
                lines.Add($"+{outcome.Collector} colecionador ({outcome.Group})");
            if (outcome.Explorer > 0)
                lines.Add($"+{outcome.Explorer} explorador ({string.Join(", ", outcome.NewHabitats ?? new List<string>())})");
        }

        if (outcome.Risk == SpeciesRisk.Dangerous)
            lines.Add("Aviso: espécie perigosa. Não se aproxime.");

        lines.Add($"Ganhou {outcome.Earned} pontos. Total: {outcome.Total}.");

        var criteria = ScoringRules.CriteriaLines().Select(line => "• " + line);
        return "\n\nPONTUAÇÃO\n" + string.Join("\n", lines)
            + "\n\nCRITÉRIOS DOS PONTOS\n" + string.Join("\n", criteria);
    }

    async Task IdentifyAsync()
    {
        if (engine?.IsReady != true || string.IsNullOrWhiteSpace(pendingPath))
        {
            result!.Text = "Baixe o BioCLIP offline e tire uma foto antes de identificar.";
            return;
        }

        if (catalog.Count == 0)
        {
            result!.Text = "Nenhuma espécie com embedding disponível no pacote atual.";
            return;
        }

        identifyButton!.Enabled = false;
        postFromIdentifyButton!.Visibility = ViewStates.Gone;
        lastAwardedPoints = 0;
        status!.Text = "Analisando foto com BioCLIP offline...";

        try
        {
            var endpoint = endpointInput?.Text?.Trim();
            var preferences = GetPreferences(FileCreationMode.Private)
                ?? throw new InvalidOperationException("Não foi possível acessar as preferências do aplicativo.");
            preferences.Edit()!.PutString("remote_endpoint", endpoint ?? "").Apply();

            var imagePath = pendingPath;
            var embedding = await Task.Run(() => engine.EncodeImage(imagePath));
            var matches = catalog.Search(embedding, 5);
            result!.Text = ResultFormatter.Format(matches, includeAlternatives: false) + ScoreNote(matches);
            ShowMatchImages(matches);
            identifiedPhotoPath = imagePath;
            postFromIdentifyButton.Visibility = ViewStates.Visible;
            status!.Text = "Resultado offline concluído.";

            if (!string.IsNullOrWhiteSpace(endpoint))
            {
                try
                {
                    status.Text = "Offline concluído. Consultando BioCLIP 2 online...";
                    var remote = await new RemoteIdentificationClient().IdentifyAsync(endpoint, imagePath);
                    if (remote is { Count: > 0 })
                    {
                        result.Text += "\n\nONLINE — BioCLIP 2\n" + ResultFormatter.Format(remote);
                        status.Text = "Resultado híbrido concluído.";
                    }
                    else
                    {
                        status.Text = "Offline concluído; servidor online não retornou candidatos.";
                    }
                }
                catch
                {
                    status.Text = "Offline concluído; BioCLIP 2 online indisponível.";
                }
            }
        }
        catch (Exception ex)
        {
            result!.Text = $"Falha na identificação: {ex.Message}";
            status!.Text = "Erro no mecanismo offline.";
        }
        finally
        {
            identifyButton.Enabled = true;
        }
    }

    int Dp(int value) => (int)(value * Resources!.DisplayMetrics!.Density);

    protected override void OnDestroy()
    {
        pollingDownload = false;
        engine?.Dispose();
        engine = null;
        previewBitmap?.Dispose();
        previewBitmap = null;
        base.OnDestroy();
    }
}
