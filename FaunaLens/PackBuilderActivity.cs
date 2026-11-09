using System.Text.Json;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using Android.Widget;
using FaunaLens.Models;
using FaunaLens.Services;

namespace FaunaLens;

[Activity(Label = "Criar FaunaPack", Exported = false, ScreenOrientation = ScreenOrientation.Portrait)]
public sealed class PackBuilderActivity : Activity
{
    // Pasta de um pack já instalado: a tela refaz esse pack no mesmo lugar, com as opções escolhidas.
    public const string ExtraUpdatePackDirectory = "update_pack_directory";

    const int NotificationPermissionRequest = 4120;

    const string IntroText =
        "Baixa fotos licenciadas do GBIF para a região escolhida e gera os embeddings no seu celular. " +
        "Precisa de internet. A criação roda em segundo plano: você pode sair do app e acompanhar pela notificação.";

    static readonly string[] Regions = BrazilRegions.SpinnerItems;

    static readonly int[] SpeciesOptions = { 10, 25, 50, 100 };
    static readonly int[] ImageOptions = { 2, 3, 5, 10, 20, 30, 40, 50 };
    static readonly int[] PageOptions = { 2, 5, 10, 20 };

    EditText? nameInput;
    Spinner? regionSpinner;
    Spinner? speciesSpinner;
    Spinner? imagesSpinner;
    Spinner? pagesSpinner;
    CheckBox? allowBySa;
    Button? buildButton;
    Button? cancelButton;
    ProgressBar? progress;
    TextView? statusText;
    TextView? logView;
    Handler? handler;
    string? updatePackId;
    string shownLog = "";
    bool polling;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        ThemeController.Apply(this);
        base.OnCreate(savedInstanceState);
        handler = new Handler(Looper.MainLooper!);

        var directory = Intent?.GetStringExtra(ExtraUpdatePackDirectory);
        var target = string.IsNullOrWhiteSpace(directory) ? null : ReadPackManifest(directory);
        updatePackId = target?.PackId;

        BuildUi(target);
        RefreshFromState();
    }

    protected override void OnResume()
    {
        base.OnResume();
        if (RefreshFromState())
            StartPolling();
    }

    protected override void OnPause()
    {
        base.OnPause();
        polling = false;
    }

    // Sair desta tela não cancela a criação: ela segue no serviço, com a notificação.
    void StartPolling()
    {
        if (polling)
            return;

        polling = true;
        handler!.PostDelayed(Poll, 1000);
    }

    void Poll()
    {
        if (!polling)
            return;

        if (RefreshFromState())
            handler!.PostDelayed(Poll, 1000);
        else
            polling = false;
    }

    // Mostra o estado gravado pelo serviço. Devolve true enquanto a criação estiver ativa.
    bool RefreshFromState()
    {
        var state = PackBuildStateStore.Load(FileSystem.PackBuildStatePath);
        var active = state.IsActive(DateTime.UtcNow);

        var log = string.Join("\n", state.Log);
        if (log != shownLog)
        {
            logView!.Text = log;
            shownLog = log;
        }

        progress!.Progress = state.Percent;
        statusText!.Text = DescribeState(state, active);
        SetFormEnabled(!active);
        cancelButton!.Enabled = active;
        return active;
    }

    string DescribeState(PackBuildState state, bool active)
    {
        if (active)
            return $"Criando \"{state.PackName}\"... {state.Percent}%. Pode sair desta tela: o progresso fica na notificação.";

        return state.Status switch
        {
            "completed" => $"Pack \"{state.PackName}\" criado e ativado com {state.ResultSpeciesCount} espécies.",
            "failed" => $"A criação falhou: {state.Error}",
            "cancelled" => "A última criação foi cancelada.",
            "queued" or "running" => "A última criação foi interrompida (o sistema encerrou o app). Crie de novo.",
            _ => ""
        };
    }

    void BuildUi(FaunaPackManifest? target)
    {
        var root = new LinearLayout(this) { Orientation = Orientation.Vertical };
        root.SetPadding(32, 24, 32, 24);
        root.SetOnApplyWindowInsetsListener(new SystemBarInsetsListener(32, 24));

        var scroll = new ScrollView(this);
        var content = new LinearLayout(this) { Orientation = Orientation.Vertical };
        scroll.AddView(content);
        root.AddView(scroll);
        SetContentView(root);

        var settings = target?.BuildSettings;

        content.AddView(new TextView(this)
        {
            Text = target is null
                ? IntroText
                : $"Refaz \"{target.PackName}\" com as opções abaixo. O pack atual só é trocado quando o novo estiver pronto."
        });

        statusText = new TextView(this);
        statusText.SetPadding(0, 12, 0, 4);
        content.AddView(statusText);

        content.AddView(new TextView(this) { Text = "Nome do pack" });
        nameInput = new EditText(this) { Text = target?.PackName ?? "Meu pack" };
        content.AddView(nameInput);

        var rioGrandeIndex = Math.Max(0, Array.IndexOf(Regions, "Rio Grande do Sul"));
        regionSpinner = AddSpinner(content, "Região", Regions, settings is null ? rioGrandeIndex : RegionIndex(settings.StateProvince));
        speciesSpinner = AddSpinner(content, "Espécies (até)", Labels(SpeciesOptions), IndexOf(SpeciesOptions, settings?.MaxSpecies ?? 50, 2));
        imagesSpinner = AddSpinner(content, "Imagens por espécie (até 50)", Labels(ImageOptions), IndexOf(ImageOptions, target?.ReferencesPerSpecies ?? 2, 0));
        pagesSpinner = AddSpinner(content, "Páginas de busca no GBIF (300 registros cada)", Labels(PageOptions), IndexOf(PageOptions, settings?.SearchPages ?? 10, 2));

        allowBySa = new CheckBox(this) { Text = "Aceitar CC BY-SA", Checked = settings?.AllowBySa ?? true };
        content.AddView(allowBySa);

        buildButton = new Button(this) { Text = target is null ? "Criar FaunaPack" : "Atualizar FaunaPack" };
        buildButton.Click += (_, _) => StartBuild();
        content.AddView(buildButton);

        cancelButton = new Button(this) { Text = "Cancelar criação", Enabled = false };
        cancelButton.Click += (_, _) => CancelBuild();
        content.AddView(cancelButton);

        progress = new ProgressBar(this, null, Android.Resource.Attribute.ProgressBarStyleHorizontal) { Max = 100 };
        content.AddView(progress);

        logView = new TextView(this) { TextSize = 12 };
        content.AddView(logView);
    }

    Spinner AddSpinner(LinearLayout content, string label, string[] items, int selectedIndex)
    {
        content.AddView(new TextView(this) { Text = label });
        var spinner = new Spinner(this);
        spinner.Adapter = new ArrayAdapter<string>(this, Android.Resource.Layout.SimpleSpinnerItem, items);
        spinner.SetSelection(selectedIndex);
        content.AddView(spinner);
        return spinner;
    }

    static string[] Labels(int[] values) => values.Select(value => value.ToString()).ToArray();

    static int IndexOf(int[] options, int value, int fallback)
    {
        var index = Array.IndexOf(options, value);
        return index >= 0 ? index : fallback;
    }

    static int RegionIndex(string? stateProvince) =>
        string.IsNullOrWhiteSpace(stateProvince) ? 0 : Math.Max(0, Array.IndexOf(Regions, stateProvince));

    static FaunaPackManifest? ReadPackManifest(string directory)
    {
        try
        {
            var path = Path.Combine(directory, "manifest.json");
            if (!File.Exists(path))
                return null;

            return JsonSerializer.Deserialize<FaunaPackManifest>(
                File.ReadAllText(path),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch
        {
            return null;
        }
    }

    void SetFormEnabled(bool enabled)
    {
        nameInput!.Enabled = enabled;
        regionSpinner!.Enabled = enabled;
        speciesSpinner!.Enabled = enabled;
        imagesSpinner!.Enabled = enabled;
        pagesSpinner!.Enabled = enabled;
        allowBySa!.Enabled = enabled;
        buildButton!.Enabled = enabled;
    }

    void ShowMessage(string text)
    {
        Toast.MakeText(this, text, ToastLength.Long)?.Show();
    }

    void StartBuild()
    {
        if (PackBuildStateStore.Load(FileSystem.PackBuildStatePath).IsActive(DateTime.UtcNow))
        {
            RefreshFromState();
            return;
        }

        var name = nameInput!.Text?.Trim() ?? "";
        if (name.Length == 0)
        {
            ShowMessage("Informe o nome do pack.");
            return;
        }

        if (!ModelStorage.HasAccess(this))
        {
            ShowMessage("Libere o acesso a arquivos para usar o BioCLIP em Download/Biodex_Models.");
            ModelStorage.RequestAccess(this);
            return;
        }

        if (!File.Exists(FileSystem.ModelPath))
        {
            ShowMessage("O BioCLIP ainda não está instalado. Baixe-o na tela principal primeiro.");
            return;
        }

        RequestNotificationPermissionIfNeeded();

        var regionIndex = regionSpinner!.SelectedItemPosition;
        var options = new PackBuildOptions
        {
            PackName = name,
            StateProvince = regionIndex == 0 ? "" : Regions[regionIndex],
            MaxSpecies = SpeciesOptions[speciesSpinner!.SelectedItemPosition],
            ImagesPerSpecies = ImageOptions[imagesSpinner!.SelectedItemPosition],
            SearchPages = PageOptions[pagesSpinner!.SelectedItemPosition],
            AllowBySa = allowBySa!.Checked,
            UpdatePackId = updatePackId
        }.Normalized();

        // O estado é gravado antes de iniciar o serviço, para que a tela já mostre a criação como ativa.
        PackBuildLauncher.Start(this, options);

        StartPolling();
        RefreshFromState();
    }

    void CancelBuild()
    {
        var intent = new Intent(this, typeof(PackBuildForegroundService));
        intent.SetAction(PackBuildForegroundService.ActionCancel);
        StartService(intent);
    }

    void RequestNotificationPermissionIfNeeded()
    {
        if (Build.VERSION.SdkInt < BuildVersionCodes.Tiramisu)
            return;

        const string permission = "android.permission.POST_NOTIFICATIONS";
        if (CheckSelfPermission(permission) != Permission.Granted)
            RequestPermissions(new[] { permission }, NotificationPermissionRequest);
    }
}
