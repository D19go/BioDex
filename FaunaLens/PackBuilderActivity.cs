using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using Android.Widget;
using FaunaLens.Services;

namespace FaunaLens;

[Activity(Label = "Criar FaunaPack", Exported = false, ScreenOrientation = ScreenOrientation.Portrait)]
public sealed class PackBuilderActivity : Activity
{
    static readonly string[] Regions =
    {
        "Brasil inteiro", "Acre", "Alagoas", "Amapá", "Amazonas", "Bahia", "Ceará", "Distrito Federal",
        "Espírito Santo", "Goiás", "Maranhão", "Mato Grosso", "Mato Grosso do Sul", "Minas Gerais", "Pará",
        "Paraíba", "Paraná", "Pernambuco", "Piauí", "Rio de Janeiro", "Rio Grande do Norte", "Rio Grande do Sul",
        "Rondônia", "Roraima", "Santa Catarina", "São Paulo", "Sergipe", "Tocantins"
    };

    static readonly int[] SpeciesOptions = { 10, 25, 50, 100 };
    static readonly int[] ImageOptions = { 2, 3, 5 };
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
    TextView? logView;
    CancellationTokenSource? cancellation;
    bool running;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        BuildUi();
    }

    protected override void OnDestroy()
    {
        cancellation?.Cancel();
        base.OnDestroy();
    }

    void BuildUi()
    {
        var root = new LinearLayout(this) { Orientation = Orientation.Vertical };
        root.SetPadding(32, 24, 32, 24);
        root.SetOnApplyWindowInsetsListener(new SystemBarInsetsListener(32, 24));

        var scroll = new ScrollView(this);
        var content = new LinearLayout(this) { Orientation = Orientation.Vertical };
        scroll.AddView(content);
        root.AddView(scroll);
        SetContentView(root);

        content.AddView(new TextView(this)
        {
            Text = "Baixa fotos licenciadas do GBIF para a região escolhida e gera os embeddings no seu celular. " +
                   "Precisa de internet e o app deve ficar aberto até terminar. Depois de criado, o pack fica ativo para identificação offline."
        });

        content.AddView(new TextView(this) { Text = "Nome do pack" });
        nameInput = new EditText(this) { Text = "Meu pack" };
        content.AddView(nameInput);

        var rioGrandeIndex = Math.Max(0, Array.IndexOf(Regions, "Rio Grande do Sul"));
        regionSpinner = AddSpinner(content, "Região", Regions, rioGrandeIndex);
        speciesSpinner = AddSpinner(content, "Espécies (até)", Labels(SpeciesOptions), 2);
        imagesSpinner = AddSpinner(content, "Imagens por espécie", Labels(ImageOptions), 0);
        pagesSpinner = AddSpinner(content, "Páginas de busca no GBIF (300 registros cada)", Labels(PageOptions), 2);

        allowBySa = new CheckBox(this) { Text = "Aceitar CC BY-SA", Checked = true };
        content.AddView(allowBySa);

        buildButton = new Button(this) { Text = "Criar FaunaPack" };
        buildButton.Click += async (_, _) => await StartBuildAsync();
        content.AddView(buildButton);

        cancelButton = new Button(this) { Text = "Cancelar", Enabled = false };
        cancelButton.Click += (_, _) => cancellation?.Cancel();
        content.AddView(cancelButton);

        progress = new ProgressBar(this, null, Android.Resource.Attribute.ProgressBarStyleHorizontal) { Max = 100 };
        content.AddView(progress);

        logView = new TextView(this);
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

    void SetRunning(bool value)
    {
        running = value;
        buildButton!.Enabled = !value;
        cancelButton!.Enabled = value;
        nameInput!.Enabled = !value;
        regionSpinner!.Enabled = !value;
        speciesSpinner!.Enabled = !value;
        imagesSpinner!.Enabled = !value;
        pagesSpinner!.Enabled = !value;
        allowBySa!.Enabled = !value;
    }

    void AppendLog(string message)
    {
        logView!.Append(message + "\n");
    }

    async Task StartBuildAsync()
    {
        if (running) return;

        var name = nameInput!.Text?.Trim() ?? "";
        if (name.Length == 0)
        {
            AppendLog("Informe o nome do pack.");
            return;
        }

        if (!ModelStorage.HasAccess(this))
        {
            AppendLog("Libere o acesso a arquivos para usar o BioCLIP em Download/Biodex_Models.");
            ModelStorage.RequestAccess(this);
            return;
        }

        if (!File.Exists(FileSystem.ModelPath))
        {
            AppendLog("O BioCLIP ainda não está instalado. Baixe-o na tela principal primeiro.");
            return;
        }

        var regionIndex = regionSpinner!.SelectedItemPosition;
        var options = new PackBuildOptions(
            PackName: name,
            StateProvince: regionIndex == 0 ? "" : Regions[regionIndex],
            MaxSpecies: SpeciesOptions[speciesSpinner!.SelectedItemPosition],
            ImagesPerSpecies: ImageOptions[imagesSpinner!.SelectedItemPosition],
            SearchPages: PageOptions[pagesSpinner!.SelectedItemPosition],
            AllowBySa: allowBySa!.Checked);

        logView!.Text = "";
        progress!.Progress = 0;
        var source = new CancellationTokenSource();
        cancellation = source;
        SetRunning(true);

        BioClipOnnxEngine? builderEngine = null;
        string? zip = null;

        try
        {
            AppendLog("Carregando o BioCLIP...");
            builderEngine = await Task.Run(() => new BioClipOnnxEngine(FileSystem.ModelPath));
            if (!builderEngine.IsReady)
                throw new InvalidOperationException("Não foi possível carregar o BioCLIP.");

            var builder = new AppPackBuilder(
                builderEngine,
                message => RunOnUiThread(() => AppendLog(message)),
                value => RunOnUiThread(() => progress!.Progress = value));

            zip = await builder.BuildToFileAsync(options, source.Token);

            AppendLog("Instalando e ativando o pack...");
            var pack = await new FaunaPackManager().InstallAsync(zip, source.Token);
            AppendLog($"Pack \"{pack.Manifest.PackName}\" criado com {pack.Entries.Count} espécies e ativado. Toque em voltar para identificar com ele.");
            SetResult(Result.Ok);
        }
        // System.OperationCanceledException explícito: Android.OS também define um tipo com esse nome.
        catch (System.OperationCanceledException)
        {
            AppendLog("Operação cancelada.");
        }
        catch (Exception ex)
        {
            AppendLog("ERRO: " + ex.Message);
        }
        finally
        {
            builderEngine?.Dispose();
            if (zip is not null && File.Exists(zip)) File.Delete(zip);
            source.Dispose();
            cancellation = null;
            SetRunning(false);
        }
    }
}
