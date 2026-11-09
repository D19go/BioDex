using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using Android.Widget;
using FaunaLens.Services;

namespace FaunaLens;

// Lista os FaunaPacks instalados, escolhe o usado na identificação e liga/desliga a criação automática por região.
[Activity(Label = "Gerenciar FaunaPacks", Exported = false, ScreenOrientation = ScreenOrientation.Portrait)]
public sealed class PackManagerActivity : Activity
{
    const int LocationPermissionRequest = 4130;

    readonly FaunaPackManager packManager = new();
    IReadOnlyList<InstalledFaunaPack> packs = Array.Empty<InstalledFaunaPack>();
    string? selectedPackId;
    LinearLayout? packList;
    Button? updateButton;
    CheckBox? autoCheck;
    TextView? message;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        ThemeController.Apply(this);
        base.OnCreate(savedInstanceState);
        BuildUi();
        _ = RefreshPacksAsync();
    }

    void BuildUi()
    {
        var root = new LinearLayout(this) { Orientation = Orientation.Vertical };
        root.SetPadding(32, 24, 32, 24);
        root.SetOnApplyWindowInsetsListener(new SystemBarInsetsListener(32, 24));

        var scroll = new ScrollView(this);
        var content = new LinearLayout(this) { Orientation = Orientation.Vertical };
        scroll.AddView(content);
        root.AddView(scroll, new LinearLayout.LayoutParams(-1, -1));
        SetContentView(root);

        var title = new TextView(this) { Text = "Gerenciar FaunaPacks", TextSize = 24 };
        title.SetTypeface(null, Android.Graphics.TypefaceStyle.Bold);
        content.AddView(title);

        content.AddView(new TextView(this)
        {
            Text = "O pack selecionado é o usado na identificação. Toque em um pack para usá-lo."
        });

        packList = new LinearLayout(this) { Orientation = Orientation.Vertical };
        packList.SetPadding(0, 16, 0, 16);
        content.AddView(packList);

        updateButton = new Button(this) { Text = "Atualizar pack selecionado com mais imagens", Enabled = false };
        updateButton.Click += (_, _) => RequestUpdate();
        content.AddView(updateButton);

        var autoTitle = new TextView(this) { Text = "Criação automática por região", TextSize = 18 };
        autoTitle.SetTypeface(null, Android.Graphics.TypefaceStyle.Bold);
        autoTitle.SetPadding(0, 32, 0, 8);
        content.AddView(autoTitle);

        autoCheck = new CheckBox(this)
        {
            Text = "Criar pack ao mudar de estado",
            Checked = AppSettings.IsAutoRegionEnabled()
        };
        autoCheck.Click += (_, _) => OnAutoToggled(autoCheck.Checked);
        content.AddView(autoCheck);

        content.AddView(new TextView(this)
        {
            TextSize = 13,
            Text = "Usa a localização aproximada do celular, verificada ao abrir o app (no máximo a cada 10 minutos). " +
                   "A primeira detecção só registra o estado. Quando o estado muda, cria sozinho um pack da nova região " +
                   "(50 espécies, 2 imagens cada) em segundo plano. Se a região já tiver um pack, não cria outro."
        });

        message = new TextView(this) { TextSize = 13 };
        message.SetPadding(0, 16, 0, 0);
        content.AddView(message);
    }

    async Task RefreshPacksAsync()
    {
        try
        {
            packs = await packManager.ListAsync();
        }
        catch (Exception ex)
        {
            message!.Text = $"Falha ao listar FaunaPacks: {ex.Message}";
            return;
        }

        var activeId = packManager.GetActivePackId();
        selectedPackId = packs.Any(pack => pack.PackId == activeId) ? activeId : null;

        packList!.RemoveAllViews();
        if (packs.Count == 0)
        {
            packList.AddView(new TextView(this)
            {
                Text = "Nenhum FaunaPack instalado. Crie um por região ou importe um .faunapack na tela principal."
            });
        }
        else
        {
            // RadioGroup garante que só um pack fica selecionado por vez.
            var group = new RadioGroup(this) { Orientation = Orientation.Vertical };
            foreach (var pack in packs)
            {
                var id = pack.PackId;
                var radio = new RadioButton(this)
                {
                    Text = Describe(pack),
                    Checked = id == selectedPackId
                };
                radio.Click += (_, _) => SelectPack(id);
                group.AddView(radio);
            }

            packList.AddView(group);
        }

        updateButton!.Enabled = selectedPackId is not null;
    }

    static string Describe(InstalledFaunaPack pack)
    {
        var scope = pack.Region.Length == 0 ? string.Empty : $" · {pack.Region}";
        return $"{pack.PackName}\n{pack.SpeciesCount} espécies × {pack.ReferencesPerSpecies} imagens{scope}";
    }

    void SelectPack(string packId)
    {
        selectedPackId = packId;
        packManager.SetActivePackId(packId);
        updateButton!.Enabled = true;
        message!.Text = "Pack selecionado para a identificação. A tela principal carrega ele ao voltar.";
    }

    // A atualização refaz o pack na tela de criação, então a tela principal é quem a abre (libera o BioCLIP dela antes).
    void RequestUpdate()
    {
        var pack = packs.FirstOrDefault(x => x.PackId == selectedPackId);
        if (pack is null)
        {
            message!.Text = "Escolha um pack primeiro.";
            return;
        }

        var intent = new Intent().PutExtra(PackBuilderActivity.ExtraUpdatePackDirectory, pack.DirectoryPath);
        SetResult(Result.Ok, intent);
        Finish();
    }

    void OnAutoToggled(bool enable)
    {
        if (!enable)
        {
            AppSettings.SetAutoRegionEnabled(false);
            message!.Text = "Criação automática desligada.";
            return;
        }

        if (LocationRegionDetector.HasPermission(this))
        {
            EnableAutoRegion();
            return;
        }

        RequestPermissions(new[] { LocationRegionDetector.CoarseLocationPermission }, LocationPermissionRequest);
    }

    void EnableAutoRegion()
    {
        AppSettings.SetAutoRegionEnabled(true);
        // Faz a verificação já na próxima vez que a tela principal aparecer, sem esperar o intervalo.
        AppSettings.SetLastRegionCheckUtc(DateTime.MinValue);
        message!.Text = "Criação automática ligada. O estado é verificado ao abrir o app.";
    }

    public override void OnRequestPermissionsResult(int requestCode, string[] permissions, Permission[] grantResults)
    {
        base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
        if (requestCode != LocationPermissionRequest)
            return;

        if (grantResults.Length > 0 && grantResults[0] == Permission.Granted)
        {
            EnableAutoRegion();
            return;
        }

        autoCheck!.Checked = false;
        AppSettings.SetAutoRegionEnabled(false);
        message!.Text = "Sem permissão de localização, a criação automática fica desligada.";
    }
}
