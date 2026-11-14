using System.Globalization;
using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using Android.Widget;
using FaunaLens.Models;
using FaunaLens.Services;

namespace FaunaLens;

// Propriedades de um FaunaPack: dados gerais e, para cada espécie, o nome e quantas imagens o pack tem.
[Activity(Label = "Propriedades do FaunaPack", Exported = false, ScreenOrientation = ScreenOrientation.Portrait)]
public sealed class PackPropertiesActivity : Activity
{
    public const string ExtraPackDirectory = "pack_directory";

    LinearLayout? content;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        ThemeController.Apply(this);
        base.OnCreate(savedInstanceState);

        var root = new LinearLayout(this) { Orientation = Orientation.Vertical };
        root.SetPadding(32, 24, 32, 24);
        root.SetOnApplyWindowInsetsListener(new SystemBarInsetsListener(32, 24));

        var scroll = new ScrollView(this);
        content = new LinearLayout(this) { Orientation = Orientation.Vertical };
        scroll.AddView(content);
        root.AddView(scroll, new LinearLayout.LayoutParams(-1, -1));
        SetContentView(root);

        var directory = Intent?.GetStringExtra(ExtraPackDirectory);
        _ = ShowAsync(directory);
    }

    async Task ShowAsync(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            AddText("Pack não informado.");
            return;
        }

        try
        {
            var pack = await new FaunaPackReader().LoadInstalledAsync(directory);
            Render(pack);
        }
        catch (Exception ex)
        {
            AddText($"Não foi possível ler o pack: {ex.Message}");
        }
    }

    void Render(FaunaPackData pack)
    {
        var manifest = pack.Manifest;
        var settings = manifest.BuildSettings;
        var target = manifest.ReferencesPerSpecies;
        var totalImages = pack.Entries.Sum(entry => entry.References.Count);
        var partial = pack.Entries.Count(entry => entry.References.Count < target);

        var region = settings is null
            ? "não informada"
            : string.IsNullOrWhiteSpace(settings.StateProvince) ? BrazilRegions.WholeCountry : settings.StateProvince;

        var created = DateTime.TryParse(manifest.CreatedUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var utc)
            ? utc.ToLocalTime().ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture)
            : "não informada";

        AddTitle(manifest.PackName);
        AddText($"Identificador: {manifest.PackId}\nRegião: {region}\nCriado em: {created}");

        AddHeading("Resumo");
        var summary = $"{pack.Entries.Count} espécies · {totalImages} imagens";
        summary += partial == 0
            ? $"\nTodas as espécies têm {target} imagens."
            : $"\n{partial} espécie(s) com menos de {target} imagens (pack parcial).";
        AddText(summary);

        if (settings is not null)
        {
            var licenses = settings.AllowBySa ? "CC0, CC BY e CC BY-SA" : "CC0 e CC BY";
            var perRound = settings.ImagesPerSpecies > 0 ? $"{settings.ImagesPerSpecies} imagens por espécie em cada criação" : "";
            AddText($"Criação: até {settings.MaxSpecies} espécies, {settings.SearchPages} páginas do GBIF, licenças {licenses}.\n{perRound}".TrimEnd());
        }

        AddHeading("Espécies");
        foreach (var entry in pack.Entries)
        {
            var species = entry.Species;
            var name = string.IsNullOrWhiteSpace(species.CommonName) ? species.ScientificName : species.CommonName;
            var count = entry.References.Count;
            var line = new TextView(this) { TextSize = 15 };
            line.Text = $"{name}\n{species.ScientificName} · {count} {(count == 1 ? "imagem" : "imagens")}";
            line.SetPadding(0, 12, 0, 12);
            content!.AddView(line);
        }
    }

    void AddTitle(string text)
    {
        var view = new TextView(this) { Text = text, TextSize = 24 };
        view.SetTypeface(null, Android.Graphics.TypefaceStyle.Bold);
        content!.AddView(view);
    }

    void AddHeading(string text)
    {
        var view = new TextView(this) { Text = text, TextSize = 18 };
        view.SetTypeface(null, Android.Graphics.TypefaceStyle.Bold);
        view.SetPadding(0, 32, 0, 8);
        content!.AddView(view);
    }

    void AddText(string text)
    {
        content!.AddView(new TextView(this) { Text = text });
    }
}
