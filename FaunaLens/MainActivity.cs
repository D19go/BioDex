using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Graphics;
using Android.OS;
using Android.Provider;
using Android.Views;
using Android.Widget;
using AndroidX.Core.Content;
using FaunaLens.Models;
using FaunaLens.Services;
using Uri = Android.Net.Uri;

namespace FaunaLens;

[Activity(Label = "Fauna Lens", MainLauncher = true, Exported = true, ScreenOrientation = ScreenOrientation.Portrait)]
public sealed class MainActivity : Activity
{
    const int CameraRequest = 4101;
    const int PermissionRequest = 4102;
    const int ModelRequest = 4103;
    const int GalleryRequest = 4104;
    const int PackRequest = 4105;
    const int NotificationPermissionRequest = 4106;

    ImageView? preview;
    TextView? status;
    TextView? result;
    Button? identifyButton;
    Button? downloadModelButton;
    EditText? endpointInput;
    Uri? pendingUri;
    string? pendingPath;

    readonly SpeciesCatalog catalog = new();
    readonly FaunaPackManager packManager = new();
    BioClipOnnxEngine? engine;
    FaunaPackData? installedPack;
    Handler? uiHandler;
    Bitmap? previewBitmap;
    bool pollingDownload;
    bool engineLoading;
    bool engineLoadFailed;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        uiHandler = new Handler(Looper.MainLooper!);
        BuildUi();
        _ = StartupAsync();
    }

    async Task StartupAsync()
    {
        status!.Text = "Carregando catálogo de espécies...";
        await LoadCatalogOrInstalledPackAsync();
        RecoverBackgroundDownload();
        await InitializeEngineAsync();
        StartDownloadPolling();
    }

    protected override void OnResume()
    {
        base.OnResume();
        RefreshDownloadState();
        StartDownloadPolling();
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
                : "Baixando BioCLIP offline... preparando...";
            return;
        }

        downloadModelButton!.Enabled = true;

        // O download terminou em segundo plano: carrega o modelo sem exigir reiniciar o app.
        if (engine?.IsReady != true && !engineLoading && !engineLoadFailed && File.Exists(FileSystem.ModelPath))
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
        var root = new LinearLayout(this) { Orientation = Orientation.Vertical };
        root.SetPadding(32, 24, 32, 24);

        var scroll = new ScrollView(this);
        var content = new LinearLayout(this) { Orientation = Orientation.Vertical };
        scroll.AddView(content);

        var title = new TextView(this)
        {
            Text = "Fauna Lens",
            TextSize = 28,
            Gravity = GravityFlags.Center
        };
        title.SetTypeface(null, TypefaceStyle.Bold);
        content.AddView(title, new LinearLayout.LayoutParams(-1, -2));

        var subtitle = new TextView(this)
        {
            Text = "Identificação de fauna com BioCLIP offline + BioCLIP 2 online",
            TextSize = 15,
            Gravity = GravityFlags.Center
        };
        content.AddView(subtitle, new LinearLayout.LayoutParams(-1, -2));

        preview = new ImageView(this);
        preview.SetBackgroundColor(Color.Rgb(238, 238, 238));
        preview.SetScaleType(ImageView.ScaleType.CenterCrop);
        content.AddView(preview, new LinearLayout.LayoutParams(-1, Dp(300)));

        var camera = new Button(this) { Text = "Tirar foto" };
        camera.Click += (_, _) => TakePhoto();
        content.AddView(camera);

        var gallery = new Button(this) { Text = "Escolher da galeria" };
        gallery.Click += (_, _) => PickImage();
        content.AddView(gallery);

        identifyButton = new Button(this) { Text = "Identificar" };
        identifyButton.Enabled = false;
        identifyButton.Click += async (_, _) => await IdentifyAsync();
        content.AddView(identifyButton);

        var importPackButton = new Button(this) { Text = "📦 Importar FaunaPack" };
        importPackButton.Click += async (_, _) => await PickPackAsync();
        content.AddView(importPackButton);

        var managePackButton = new Button(this) { Text = "📚 Gerenciar FaunaPacks" };
        managePackButton.Click += async (_, _) => await ShowPackManagerAsync();
        content.AddView(managePackButton);

        downloadModelButton = new Button(this) { Text = "Baixar BioCLIP offline" };
        downloadModelButton.Click += async (_, _) => await DownloadModelAsync();
        content.AddView(downloadModelButton);

        var importModelButton = new Button(this) { Text = "Importar BioCLIP ONNX" };
        importModelButton.Click += (_, _) => PickModel();
        content.AddView(importModelButton);

        endpointInput = new EditText(this)
        {
            Hint = "Servidor BioCLIP 2 opcional (ex.: https://servidor)"
        };
        endpointInput.Text = GetPreferences(FileCreationMode.Private)?.GetString("remote_endpoint", "") ?? "";
        content.AddView(endpointInput);

        status = new TextView(this) { TextSize = 13 };
        status.SetPadding(0, 12, 0, 4);
        content.AddView(status);

        result = new TextView(this) { TextSize = 15 };
        result.SetPadding(0, 4, 0, 0);
        content.AddView(result);

        root.AddView(scroll, new LinearLayout.LayoutParams(-1, -1));
        SetContentView(root);
    }

    async Task LoadCatalogOrInstalledPackAsync()
    {
        try
        {
            Directory.CreateDirectory(FileSystem.PackDirectory);
            var activeId = packManager.GetActivePackId();
            string? selectedDirectory = null;

            if (!string.IsNullOrWhiteSpace(activeId))
            {
                var candidate = System.IO.Path.Combine(FileSystem.PackDirectory, activeId);
                if (Directory.Exists(candidate))
                    selectedDirectory = candidate;
            }

            if (selectedDirectory is null)
            {
                selectedDirectory = Directory.GetDirectories(FileSystem.PackDirectory)
                    .Select(path => new DirectoryInfo(path))
                    .OrderByDescending(x => x.LastWriteTimeUtc)
                    .Select(x => x.FullName)
                    .FirstOrDefault();
            }

            FaunaPackData? pack;
            if (!string.IsNullOrWhiteSpace(selectedDirectory))
            {
                var directory = selectedDirectory;
                pack = await Task.Run(() => packManager.LoadAsync(directory).GetAwaiter().GetResult());
            }
            else
            {
                // Primeira abertura: instala o pacote de demonstração que acompanha o APK.
                pack = await InstallBundledPackAsync();
            }

            if (pack is not null)
            {
                installedPack = pack;
                catalog.LoadPack(pack);
                return;
            }
        }
        catch
        {
            installedPack = null;
        }

        using var stream = Assets!.Open("Data/pampa_species_offline.json");
        catalog.Load(stream, "Pampa — demonstração");
    }

    async Task<FaunaPackData?> InstallBundledPackAsync()
    {
        var temporaryPath = System.IO.Path.Combine(FileSystem.Root, $"bundled_{Guid.NewGuid():N}.install");
        try
        {
            using (var input = Assets!.Open("Data/Pampa.faunapack"))
            using (var output = File.Create(temporaryPath))
                await input.CopyToAsync(output);

            return await Task.Run(() => packManager.InstallAsync(temporaryPath).GetAwaiter().GetResult());
        }
        catch
        {
            return null;
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    void UpdateEngineStatus()
    {
        var modelStatus = engine?.IsReady == true
            ? "BioCLIP offline pronto."
            : "BioCLIP offline não instalado. Toque em “Baixar BioCLIP offline”.";

        var packStatus = installedPack is null
            ? $"Catálogo: {catalog.SourceName} ({catalog.Count} espécies)."
            : $"FaunaPack ativo: {installedPack.Manifest.PackName} ({catalog.Count} espécies; {installedPack.Manifest.ReferencesPerSpecies} referência(s)/espécie).";

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

    void PickModel()
    {
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

            using (var validation = new Microsoft.ML.OnnxRuntime.InferenceSession(temporaryPath))
            {
                if (validation.InputMetadata.Count == 0 || validation.OutputMetadata.Count == 0)
                    throw new InvalidOperationException("O arquivo selecionado não é um modelo ONNX utilizável.");
            }

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

            installedPack = pack;
            catalog.LoadPack(pack);
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

    async Task ShowPackManagerAsync()
    {
        status!.Text = "Lendo FaunaPacks instalados...";

        IReadOnlyList<InstalledFaunaPack> packs;
        try
        {
            packs = await packManager.ListAsync();
        }
        catch (Exception ex)
        {
            result!.Text = $"Falha ao listar FaunaPacks: {ex.Message}";
            UpdateEngineStatus();
            return;
        }

        if (packs.Count == 0)
        {
            new AlertDialog.Builder(this)
                .SetTitle("FaunaPacks")
                .SetMessage("Nenhum FaunaPack está instalado.\n\nUse “Importar FaunaPack” para adicionar um pacote.")
                .SetPositiveButton("OK", (_, _) => { })
                .Show();
            UpdateEngineStatus();
            return;
        }

        var activeId = packManager.GetActivePackId();
        var labels = packs
            .Select(x => x.PackName + (x.PackId == activeId ? "  ✓ ativo" : ""))
            .ToArray();

        var dialog = new AlertDialog.Builder(this)
            .SetTitle("FaunaPacks instalados")
            .SetItems(labels, async (_, args) =>
            {
                await SelectPackAsync(packs[args.Which]);
            })
            .SetNegativeButton("Fechar", (_, _) => { })
            .Create();

        dialog.Show();
    }

    async Task SelectPackAsync(InstalledFaunaPack pack)
    {
        try
        {
            status!.Text = $"Carregando {pack.PackName}...";
            var loaded = await Task.Run(
                () => packManager.LoadAsync(pack.DirectoryPath).GetAwaiter().GetResult());

            installedPack = loaded;
            catalog.LoadPack(loaded);
            result!.Text =
                $"FaunaPack ativo: {loaded.Manifest.PackName}\n" +
                $"Espécies carregadas: {loaded.Entries.Count}\n" +
                $"Referências por espécie: {loaded.Manifest.ReferencesPerSpecies}";
            UpdateEngineStatus();
        }
        catch (Exception ex)
        {
            result!.Text = $"Não foi possível carregar o FaunaPack: {ex.Message}";
            UpdateEngineStatus();
        }
    }

    void PickImage()
    {
        var intent = new Intent(Intent.ActionOpenDocument);
        intent.AddCategory(Intent.CategoryOpenable);
        intent.SetType("image/*");
        StartActivityForResult(intent, GalleryRequest);
    }

    void LoadImageFromFile(string path, string sourceText)
    {
        var displayBitmap = ImageLoader.Load(path, 1024);
        preview!.SetImageBitmap(displayBitmap);
        previewBitmap?.Recycle();
        previewBitmap?.Dispose();
        previewBitmap = displayBitmap;
        pendingPath = path;
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
        if (resultCode != Result.Ok)
            return;

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
            result!.Text = ResultFormatter.Format(matches);
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
