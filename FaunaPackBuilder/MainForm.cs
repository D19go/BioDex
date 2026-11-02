using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text;
using System.Text.Encodings.Web;

namespace FaunaPackBuilder;

public sealed class MainForm : Form
{
    readonly TextBox packName = new();
    readonly TextBox catalogPath = new();
    readonly TextBox modelPath = new();
    readonly TextBox outputPath = new();
    readonly CheckBox autoCatalog = new();
    readonly NumericUpDown maxSpecies = new();
    readonly NumericUpDown imagesPerSpecies = new();
    readonly NumericUpDown searchPages = new();
    readonly CheckBox allowBySa = new();
    readonly CheckBox keepCache = new();
    readonly Button localButton = new();
    string countryCode = "BR";
    string stateProvince = "Rio Grande do Sul";
    readonly ProgressBar progress = new();
    readonly TextBox log = new();
    readonly Button buildButton = new();
    readonly Button cancelButton = new();
    CancellationTokenSource? cancellation;

    public MainForm()
    {
        Text = "FaunaPack Builder 1.2.2";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(900, 700);
        Size = new Size(1080, 780);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 12,
            Padding = new Padding(12),
            AutoScroll = true
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        for (var i = 0; i < 8; i++) root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        Controls.Add(root);

        AddRow(root, 0, "Nome do pack", packName, null);
        packName.Text = "Pampa";

        autoCatalog.Text = "Descobrir espécies automaticamente no GBIF";
        autoCatalog.Checked = true;
        autoCatalog.AutoSize = true;
        autoCatalog.CheckedChanged += (_, _) => UpdateCatalogMode();
        root.Controls.Add(new Label { Text = "Catálogo", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 1);
        root.Controls.Add(autoCatalog, 1, 1);
        root.SetColumnSpan(autoCatalog, 2);

        AddRow(root, 2, "Catálogo JSON", catalogPath, BrowseCatalog);
        AddRow(root, 3, "Modelo BioCLIP", modelPath, BrowseModel);
        AddRow(root, 4, "Saída .faunapack", outputPath, BrowseOutput);

        var options = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false };
        AddNumeric(options, "Espécies:", maxSpecies, 1, 500, 50, 65);
        AddNumeric(options, "Imagens/espécie:", imagesPerSpecies, 1, 50, 2, 65);
        AddNumeric(options, "Páginas GBIF:", searchPages, 1, 30, 10, 55);
        allowBySa.Text = "Aceitar CC BY-SA";
        allowBySa.AutoSize = true;
        allowBySa.Checked = true;
        options.Controls.Add(allowBySa);
        keepCache.Text = "Manter cache";
        keepCache.AutoSize = true;
        keepCache.Checked = true;
        options.Controls.Add(keepCache);
        localButton.Text = "📍 Local";
        localButton.AutoSize = true;
        localButton.Click += (_, _) => ShowLocationDialog();
        options.Controls.Add(localButton);
        root.Controls.Add(new Label { Text = "Opções", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 5);
        root.Controls.Add(options, 1, 5);
        root.SetColumnSpan(options, 2);

        var strategy = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
        root.Controls.Add(new Label { Text = "Estratégia", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 6);
        root.Controls.Add(strategy, 1, 6);
        root.SetColumnSpan(strategy, 2);
        imagesPerSpecies.ValueChanged += (_, _) => UpdateStrategyLabel(strategy);
        maxSpecies.ValueChanged += (_, _) => UpdateStrategyLabel(strategy);
        UpdateStrategyLabel(strategy);

        var source = new Label
        {
            Text = "",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true
        };
        root.Controls.Add(new Label { Text = "Fonte", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 7);
        root.Controls.Add(source, 1, 7);
        root.SetColumnSpan(source, 2);

        progress.Dock = DockStyle.Fill;
        root.Controls.Add(progress, 0, 8);
        root.SetColumnSpan(progress, 3);

        log.Multiline = true;
        log.ReadOnly = true;
        log.ScrollBars = ScrollBars.Vertical;
        log.Dock = DockStyle.Fill;
        log.Font = new Font(FontFamily.GenericMonospace, 9);
        root.Controls.Add(log, 0, 9);
        root.SetColumnSpan(log, 3);

        buildButton.Text = "Criar FaunaPack";
        buildButton.Dock = DockStyle.Fill;
        buildButton.Click += async (_, _) => await BuildAsync();
        cancelButton.Text = "Cancelar";
        cancelButton.Dock = DockStyle.Fill;
        cancelButton.Enabled = false;
        cancelButton.Click += (_, _) => cancellation?.Cancel();
        root.Controls.Add(buildButton, 1, 10);
        root.Controls.Add(cancelButton, 2, 10);

        var note = new Label
        {
            Text = "A versão automática pode descobrir até 500 espécies. Espécies sem imagens licenciadas suficientes são ignoradas. O pack final contém somente espécies que atingiram exatamente a quantidade configurada.",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true
        };
        root.Controls.Add(note, 0, 11);
        root.SetColumnSpan(note, 3);

        FormClosing += (_, _) => cancellation?.Cancel();
        UpdateCatalogMode();
        UpdateLocationLabel(source);
    }

    void UpdateLocationLabel(Label label)
    {
        label.Text = $"GBIF: país={countryCode}" +
                     (string.IsNullOrWhiteSpace(stateProvince) ? "" : $" • estado/província={stateProvince}") +
                     " • somente Animalia • StillImage • present • SPECIES.";
    }

    void ShowLocationDialog()
    {
        using var dialog = new LocationForm(countryCode, stateProvince);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        countryCode = dialog.CountryCode;
        stateProvince = dialog.StateProvince;
        Log($"Local selecionado: país={countryCode}" +
            (string.IsNullOrWhiteSpace(stateProvince) ? "" : $" • estado/província={stateProvince}"));
        foreach (Control control in Controls)
            UpdateLocationLabelsRecursive(control);
    }

    void UpdateLocationLabelsRecursive(Control control)
    {
        if (control is TableLayoutPanel table)
        {
            foreach (Control child in table.Controls)
            {
                if (child is Label label && label.Text.StartsWith("GBIF: país=", StringComparison.Ordinal))
                    UpdateLocationLabel(label);
                UpdateLocationLabelsRecursive(child);
            }
        }
    }

    static void AddNumeric(FlowLayoutPanel panel, string label, NumericUpDown control, int min, int max, int value, int width)
    {
        panel.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(6, 6, 2, 0) });
        control.Minimum = min;
        control.Maximum = max;
        control.Value = value;
        control.Width = width;
        control.TextAlign = HorizontalAlignment.Center;
        panel.Controls.Add(control);
    }

    void AddRow(TableLayoutPanel root, int row, string label, TextBox box, Action? browse)
    {
        root.Controls.Add(new Label { Text = label, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, row);
        box.Dock = DockStyle.Fill;
        root.Controls.Add(box, 1, row);
        var button = new Button { Text = "...", Dock = DockStyle.Fill, Enabled = browse is not null };
        button.Click += (_, _) => browse?.Invoke();
        root.Controls.Add(button, 2, row);
    }

    void UpdateCatalogMode()
    {
        catalogPath.Enabled = !autoCatalog.Checked;
    }

    void BrowseCatalog()
    {
        using var dialog = new OpenFileDialog { Filter = "JSON (*.json)|*.json|Todos (*.*)|*.*" };
        if (dialog.ShowDialog(this) == DialogResult.OK) catalogPath.Text = dialog.FileName;
    }

    void BrowseModel()
    {
        using var dialog = new OpenFileDialog { Filter = "ONNX (*.onnx)|*.onnx|Todos (*.*)|*.*" };
        if (dialog.ShowDialog(this) == DialogResult.OK) modelPath.Text = dialog.FileName;
    }

    void BrowseOutput()
    {
        using var dialog = new SaveFileDialog
        {
            Filter = "FaunaPack (*.faunapack)|*.faunapack",
            FileName = $"{SanitizeFileName(packName.Text)}.faunapack"
        };
        if (dialog.ShowDialog(this) == DialogResult.OK) outputPath.Text = dialog.FileName;
    }

    async Task BuildAsync()
    {
        if (string.IsNullOrWhiteSpace(packName.Text))
        {
            MessageBox.Show(this, "Informe o nome do pack.", "FaunaPack Builder", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (!autoCatalog.Checked && (string.IsNullOrWhiteSpace(catalogPath.Text) || !File.Exists(catalogPath.Text)))
        {
            MessageBox.Show(this, "Selecione um catálogo JSON válido ou ative a descoberta automática.", "FaunaPack Builder", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(outputPath.Text))
        {
            MessageBox.Show(this, "Escolha o arquivo .faunapack de saída.", "FaunaPack Builder", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        cancellation?.Dispose();
        cancellation = new CancellationTokenSource();
        buildButton.Enabled = false;
        cancelButton.Enabled = true;
        progress.Value = 0;
        log.Clear();

        var temp = Path.Combine(Path.GetTempPath(), "FaunaPackBuilder", Guid.NewGuid().ToString("N"));
        var cacheRoot = Path.Combine(AppContext.BaseDirectory, "cache", "images");

        try
        {
            var ct = cancellation.Token;
            List<SpeciesInput> species;

            using var gbif = new GbifClient();
            gbif.Log = Log;

            if (autoCatalog.Checked)
            {
                Log($"Descobrindo até {maxSpecies.Value} espécies no GBIF para {countryCode}" +
                    (string.IsNullOrWhiteSpace(stateProvince) ? "" : $" / {stateProvince}") + "...");
                species = await gbif.DiscoverSpeciesAsync(
                    countryCode,
                    stateProvince,
                    (int)maxSpecies.Value,
                    minimumOccurrences: 1,
                    (int)searchPages.Value,
                    ct,
                    Log);

                if (species.Count == 0)
                    throw new InvalidOperationException("O GBIF não retornou espécies para a área selecionada.");

                var catalogDirectory = Path.Combine(
                    Path.GetDirectoryName(Path.GetFullPath(outputPath.Text))!, "catalogs");
                Directory.CreateDirectory(catalogDirectory);
                var generatedCatalog = Path.Combine(catalogDirectory, "pampa_gbif_catalog.json");
                await SaveCatalogAsync(generatedCatalog, species, ct);
                Log($"Catálogo automático salvo em: {generatedCatalog}");
            }
            else
            {
                species = await LoadCatalogAsync(catalogPath.Text, ct);
            }

            if (species.Count == 0)
                throw new InvalidOperationException("O catálogo não contém espécies.");

            var requestedImages = (int)imagesPerSpecies.Value;
            Log($"Espécies para processar: {species.Count}");
            Log($"Imagens por espécie: {requestedImages}");
            Log($"Páginas de busca por espécie: {searchPages.Value}");

            var model = modelPath.Text;
            if (string.IsNullOrWhiteSpace(model))
            {
                model = Path.Combine(AppContext.BaseDirectory, "Models", "bioclip_image.onnx");
                modelPath.Text = model;
            }

            if (!File.Exists(model))
            {
                Log("Modelo BioCLIP ONNX não encontrado. Baixando...");
                await ModelDownloader.DownloadAsync(model, new Progress<int>(SetProgress), ct);
                Log("Modelo baixado.");
            }

            Directory.CreateDirectory(Path.Combine(temp, "references"));
            if (keepCache.Checked)
                Directory.CreateDirectory(cacheRoot);

            Log("Carregando o modelo BioCLIP no ONNX Runtime...");
            BioClipEngine engine;
            try
            {
                engine = await Task.Run(() => new BioClipEngine(model), ct);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    "Não foi possível inicializar o ONNX Runtime com o modelo BioCLIP. " +
                    "Verifique as DLLs nativas, o modelo ONNX e a arquitetura x64. Detalhe: " + ex.Message, ex);
            }

            var built = new List<PackSpecies>();
            using (engine)
            {
                for (var index = 0; index < species.Count; index++)
                {
                    ct.ThrowIfCancellationRequested();
                    var item = species[index];
                    Log($"[{index + 1}/{species.Count}] {item.CommonName} — {item.ScientificName}");

                    var result = await BuildSpeciesAsync(
                        item, gbif, engine, temp, cacheRoot, requestedImages,
                        (int)searchPages.Value, ct);

                    if (result is null)
                    {
                        Log($"  IGNORADA: não foi possível obter {requestedImages} imagens licenciadas.");
                    }
                    else
                    {
                        built.Add(result);
                        Log($"  OK: {requestedImages} imagens + {requestedImages} embeddings.");
                    }

                    SetProgress((index + 1) * 100 / species.Count);
                }
            }

            if (built.Count == 0)
                throw new InvalidOperationException("Nenhuma espécie conseguiu atingir a quantidade configurada de imagens licenciadas.");

            Log($"Escrevendo pack com {built.Count} espécies...");
            PackWriter.Write(outputPath.Text, packName.Text.Trim(), built, temp);

            var compatibility = Path.Combine(
                Path.GetDirectoryName(Path.GetFullPath(outputPath.Text))!,
                Path.GetFileNameWithoutExtension(outputPath.Text) + "_offline_compat.json");
            PackWriter.WriteCompatibilityJson(compatibility, built);

            Log($"Pack criado: {outputPath.Text}");
            Log($"Espécies incluídas: {built.Count}/{species.Count}");
            Log($"Compatibilidade criada: {compatibility}");

            MessageBox.Show(
                this,
                $"FaunaPack criado com {built.Count} espécies.\n\n" +
                $"Foram solicitadas {requestedImages} imagens por espécie.\n" +
                "Espécies sem imagens suficientes/licenciadas foram ignoradas.",
                "Concluído",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (OperationCanceledException)
        {
            Log("Operação cancelada.");
        }
        catch (Exception ex)
        {
            Log("ERRO: " + ex.Message);
            MessageBox.Show(this, ex.ToString(), "Erro", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            try
            {
                if (!keepCache.Checked && Directory.Exists(cacheRoot))
                    Directory.Delete(cacheRoot, true);
                if (Directory.Exists(temp))
                    Directory.Delete(temp, true);
            }
            catch { }

            cancellation?.Dispose();
            cancellation = null;
            buildButton.Enabled = true;
            cancelButton.Enabled = false;
        }
    }

    async Task<PackSpecies?> BuildSpeciesAsync(
        SpeciesInput species,
        GbifClient gbif,
        BioClipEngine engine,
        string temp,
        string cacheRoot,
        int requestedImages,
        int maxPages,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(species.ScientificName))
            return null;

        var candidates = await gbif.SearchImagesAsync(
            species.ScientificName,
            countryCode,
            stateProvince,
            maxPages,
            ct,
            (page, total) => Log($"  busca GBIF: página {page}/{total}"));

        Log($"  candidatos GBIF: {candidates.Count}");

        var usedMedia = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var usedHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new PackSpecies { Species = species };
        var folder = Path.Combine(temp, "references", species.Id);
        Directory.CreateDirectory(folder);

        foreach (var occurrence in candidates)
        {
            foreach (var media in occurrence.Media)
            {
                ct.ThrowIfCancellationRequested();
                if (result.References.Count >= requestedImages) break;
                if (!IsAllowedLicense(media.License)) continue;
                if (string.IsNullOrWhiteSpace(media.Identifier)) continue;
                if (!usedMedia.Add(media.Identifier)) continue;

                try
                {
                    var mediaHash = Convert.ToHexString(
                        SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(media.Identifier))).ToLowerInvariant();
                    var cacheFile = Path.Combine(cacheRoot, mediaHash + ".jpg");
                    byte[] thumbnail;

                    if (keepCache.Checked && File.Exists(cacheFile))
                    {
                        thumbnail = await File.ReadAllBytesAsync(cacheFile, ct);
                    }
                    else
                    {
                        var raw = await gbif.DownloadImageAsync(occurrence, media, ct);
                        if (raw.Length < 5_000) continue;
                        thumbnail = BioClipEngine.MakeThumbnail(raw);
                        if (keepCache.Checked)
                            await File.WriteAllBytesAsync(cacheFile, thumbnail, ct);
                    }

                    if (thumbnail.Length < 5_000) continue;
                    var hash = Convert.ToHexString(SHA256.HashData(thumbnail));
                    if (!usedHashes.Add(hash)) continue;

                    var index = result.References.Count + 1;
                    var localPath = Path.Combine(folder, $"{index}.jpg");
                    await File.WriteAllBytesAsync(localPath, thumbnail, ct);

                    var vector = await Task.Run(() => engine.Encode(thumbnail), ct);
                    if (vector.Length != 512)
                        throw new InvalidOperationException($"Embedding inesperado: {vector.Length} dimensões.");

                    result.Embeddings.Add(vector);
                    result.References.Add(new ReferenceImageInfo
                    {
                        Index = index,
                        File = $"images/{species.Id}/{index}.jpg",
                        GbifId = occurrence.Key,
                        SourceUrl = media.Identifier,
                        OccurrenceUrl = $"https://www.gbif.org/occurrence/{occurrence.Key}",
                        License = media.License,
                        Creator = media.Creator ?? occurrence.RightsHolder,
                        RightsHolder = media.RightsHolder ?? occurrence.RightsHolder,
                        Dataset = occurrence.DatasetName
                    });

                    Log($"  imagem {index}/{requestedImages}: GBIF {occurrence.Key}, licença {media.License}");
                }
                catch (Exception ex)
                {
                    Log($"  imagem ignorada: {ex.Message}");
                }
            }

            if (result.References.Count >= requestedImages) break;
        }

        return result.References.Count == requestedImages ? result : null;
    }

    bool IsAllowedLicense(string? license)
    {
        if (string.IsNullOrWhiteSpace(license)) return false;
        var value = license.Trim().ToLowerInvariant();

        if (value.Contains("publicdomain/zero") || value.Contains("cc0")) return true;
        if (value.Contains("creativecommons.org/licenses/by/")) return true;
        if (allowBySa.Checked && value.Contains("creativecommons.org/licenses/by-sa/")) return true;
        return false;
    }

    static async Task<List<SpeciesInput>> LoadCatalogAsync(string path, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        var data = await JsonSerializer.DeserializeAsync<List<SpeciesInput>>(
            stream,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true },
            ct);
        return data ?? new List<SpeciesInput>();
    }

    static async Task SaveCatalogAsync(string path, List<SpeciesInput> species, CancellationToken ct)
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
        var json = JsonSerializer.Serialize(species, options);
        await File.WriteAllTextAsync(path, json, new UTF8Encoding(false), ct);
    }

    void UpdateStrategyLabel(Label label)
    {
        label.Text = autoCatalog.Checked
            ? $"Automático: até {maxSpecies.Value} espécies • {imagesPerSpecies.Value} imagens por espécie • busca paginada no GBIF"
            : $"Catálogo manual • {imagesPerSpecies.Value} imagens por espécie • busca paginada no GBIF";
    }

    static string SanitizeFileName(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "FaunaPack";
        var invalid = Path.GetInvalidFileNameChars();
        return new string(value.Where(c => !invalid.Contains(c)).ToArray()).Trim();
    }

    void SetProgress(int value)
    {
        if (InvokeRequired) { BeginInvoke(() => SetProgress(value)); return; }
        progress.Value = Math.Clamp(value, 0, 100);
    }

    void Log(string message)
    {
        if (InvokeRequired) { BeginInvoke(() => Log(message)); return; }
        log.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
    }
}
