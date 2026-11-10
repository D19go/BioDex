using System.Security.Cryptography;
using FaunaPackBuilder;
// Alias em vez de "using Android.Graphics": o namespace tem um tipo Path que conflita com System.IO.Path.
using Bitmap = Android.Graphics.Bitmap;

namespace FaunaLens.Services;

// Mesma lógica do FaunaPack Builder (busca no GBIF, licenças, formato do pacote), mas os
// embeddings são gerados pelo BioCLIP do próprio celular, o mesmo usado na identificação.
public sealed class AppPackBuilder
{
    const string CountryCode = "BR";
    const int ThumbnailSize = 512;
    const int ThumbnailQuality = 82;
    const int MinImageBytes = 5_000;
    const int EmbeddingDimension = 512;

    readonly BioClipOnnxEngine engine;
    readonly Action<string> log;
    readonly Action<int> progress;

    public AppPackBuilder(BioClipOnnxEngine engine, Action<string> log, Action<int> progress)
    {
        this.engine = engine;
        this.log = log;
        this.progress = progress;
    }

    // Devolve o caminho de um .faunapack temporário, pronto para ser instalado.
    public async Task<string> BuildToFileAsync(PackBuildOptions requested, CancellationToken ct)
    {
        var options = requested.Normalized();
        if (!engine.IsReady)
            throw new InvalidOperationException("O BioCLIP não está carregado.");

        var buildRoot = Path.Combine(FileSystem.Root, "pack-build");
        var work = Path.Combine(buildRoot, Guid.NewGuid().ToString("N"));
        var output = Path.Combine(buildRoot, $"{Guid.NewGuid():N}.faunapack");
        Directory.CreateDirectory(Path.Combine(work, "references"));

        try
        {
            using var gbif = new GbifClient { Log = log };
            var where = string.IsNullOrWhiteSpace(options.StateProvince) ? "Brasil" : options.StateProvince;
            log($"Descobrindo até {options.MaxSpecies} espécies no GBIF ({where})...");

            var species = await gbif.DiscoverSpeciesAsync(
                CountryCode,
                options.StateProvince,
                options.MaxSpecies,
                1,
                options.SearchPages,
                ct,
                log);

            if (species.Count == 0)
                throw new InvalidOperationException("O GBIF não retornou espécies para esta região.");

            log($"Espécies para processar: {species.Count}");
            var built = new List<PackSpecies>();

            for (var index = 0; index < species.Count; index++)
            {
                ct.ThrowIfCancellationRequested();
                var item = species[index];
                log($"[{index + 1}/{species.Count}] {item.CommonName} — {item.ScientificName}");

                var result = await BuildSpeciesAsync(gbif, item, options, work, ct);
                if (result is null)
                {
                    log($"  IGNORADA: não foi possível obter {options.ImagesPerSpecies} imagens licenciadas.");
                }
                else
                {
                    built.Add(result);
                    log($"  OK: {options.ImagesPerSpecies} imagens + embeddings.");
                }

                progress((index + 1) * 100 / species.Count);
            }

            if (built.Count == 0)
                throw new InvalidOperationException("Nenhuma espécie conseguiu a quantidade de imagens licenciadas configurada.");

            log($"Escrevendo pack com {built.Count} espécies...");
            var packId = options.UpdatePackId ?? BuildPackId(options.PackName);
            var settings = new PackBuildSettings
            {
                Country = CountryCode,
                StateProvince = options.StateProvince,
                MaxSpecies = options.MaxSpecies,
                SearchPages = options.SearchPages,
                AllowBySa = options.AllowBySa
            };
            PackWriter.Write(output, options.PackName, built, work, packId, settings);
            return output;
        }
        catch
        {
            if (File.Exists(output)) File.Delete(output);
            throw;
        }
        finally
        {
            if (Directory.Exists(work)) Directory.Delete(work, true);
        }
    }

    async Task<PackSpecies?> BuildSpeciesAsync(
        GbifClient gbif,
        SpeciesInput species,
        PackBuildOptions options,
        string work,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(species.ScientificName))
            return null;

        var candidates = await gbif.SearchImagesAsync(
            species.ScientificName,
            CountryCode,
            options.StateProvince,
            options.SearchPages,
            ct,
            (page, total) => log($"  busca GBIF: página {page}/{total}"));

        log($"  candidatos GBIF: {candidates.Count}");

        var usedMedia = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var usedHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new PackSpecies { Species = species };
        var folder = Path.Combine(work, "references", species.Id);
        Directory.CreateDirectory(folder);

        foreach (var occurrence in candidates)
        {
            foreach (var media in occurrence.Media)
            {
                ct.ThrowIfCancellationRequested();
                if (result.References.Count >= options.ImagesPerSpecies) break;
                if (!GbifLicense.IsAllowed(media.License, options.AllowBySa)) continue;
                if (string.IsNullOrWhiteSpace(media.Identifier)) continue;
                if (!usedMedia.Add(media.Identifier)) continue;

                try
                {
                    log($"  GBIF {occurrence.Key}: baixando imagem...");
                    var started = DateTime.UtcNow;
                    var raw = await gbif.DownloadImageAsync(occurrence, media, ct);
                    var downloadSeconds = (DateTime.UtcNow - started).TotalSeconds;
                    log($"  GBIF {occurrence.Key}: {raw.Length / 1024} KB em {downloadSeconds:0.0} s");
                    if (raw.Length < MinImageBytes) continue;

                    var thumbnail = await Task.Run(() => MakeThumbnail(raw, work), ct);
                    if (thumbnail.Length < MinImageBytes) continue;

                    var hash = Convert.ToHexString(SHA256.HashData(thumbnail));
                    if (!usedHashes.Add(hash)) continue;

                    var index = result.References.Count + 1;
                    var localPath = Path.Combine(folder, $"{index}.jpg");
                    await File.WriteAllBytesAsync(localPath, thumbnail, ct);

                    var embedStarted = DateTime.UtcNow;
                    var vector = await Task.Run(() => engine.EncodeImage(localPath), ct);
                    var embedMs = (DateTime.UtcNow - embedStarted).TotalMilliseconds;
                    if (vector.Length != EmbeddingDimension)
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

                    log($"  imagem {index}/{options.ImagesPerSpecies} pronta (embedding {embedMs:0} ms)");
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    log($"  imagem ignorada: {ex.Message}");
                }
            }

            if (result.References.Count >= options.ImagesPerSpecies) break;
        }

        return result.References.Count == options.ImagesPerSpecies ? result : null;
    }

    // Id único por criação: dois packs com o mesmo nome não se substituem.
    static string BuildPackId(string packName)
    {
        var label = new string(packName.Where(char.IsLetterOrDigit).Take(40).ToArray());
        var stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
        return label.Length == 0 ? "pack" + stamp : label + stamp;
    }

    // Reduz para no máximo 512 px e salva em JPEG, como o Builder faz. A rotação EXIF é corrigida
    // pelo ImageLoader, para que o embedding seja calculado com a foto na orientação certa.
    static byte[] MakeThumbnail(byte[] raw, string work)
    {
        var rawPath = Path.Combine(work, $"raw_{Guid.NewGuid():N}.bin");
        File.WriteAllBytes(rawPath, raw);

        try
        {
            using var bitmap = ImageLoader.Load(rawPath, ThumbnailSize);
            var scale = Math.Min(1d, (double)ThumbnailSize / Math.Max(bitmap.Width, bitmap.Height));
            var target = scale < 1d
                ? Bitmap.CreateScaledBitmap(
                    bitmap,
                    Math.Max(1, (int)Math.Round(bitmap.Width * scale)),
                    Math.Max(1, (int)Math.Round(bitmap.Height * scale)),
                    true)!
                : bitmap;

            try
            {
                using var output = new MemoryStream();
                if (!target.Compress(Bitmap.CompressFormat.Jpeg, ThumbnailQuality, output))
                    throw new InvalidOperationException("Não foi possível gerar a miniatura da imagem.");
                return output.ToArray();
            }
            finally
            {
                if (!ReferenceEquals(target, bitmap))
                    target.Dispose();
            }
        }
        finally
        {
            File.Delete(rawPath);
        }
    }
}
