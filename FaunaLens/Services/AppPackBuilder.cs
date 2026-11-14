using System.Security.Cryptography;
using FaunaLens.Models;
using FaunaPackBuilder;
// Alias em vez de "using Android.Graphics": o namespace tem um tipo Path que conflita com System.IO.Path.
using Bitmap = Android.Graphics.Bitmap;

namespace FaunaLens.Services;

// Mesma lógica do FaunaPack Builder (busca no GBIF, licenças, formato do pacote), mas os
// embeddings são gerados pelo BioCLIP do próprio celular, o mesmo usado na identificação.
// Com um pack de base (mesma região ou escolhido para atualização), a criação amplia esse pack:
// mantém as imagens que ele já tem e baixa só imagens novas, priorizando espécies novas.
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
    // O pack é salvo mesmo se algumas espécies ficarem com menos imagens que o pedido.
    public async Task<string> BuildToFileAsync(PackBuildOptions requested, InstalledFaunaPack? baseline, CancellationToken ct)
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
            var reader = new FaunaPackReader();
            var all = new List<PackSpecies>();
            var byId = new Dictionary<string, PackSpecies>(StringComparer.OrdinalIgnoreCase);
            FaunaPackData? existing = null;

            if (baseline is not null)
            {
                existing = await reader.LoadInstalledAsync(baseline.DirectoryPath, ct);
                var vectors = await reader.ReadReferenceEmbeddingsAsync(existing, ct);
                log($"Ampliando \"{existing.Manifest.PackName}\": {existing.Entries.Count} espécies já estão no pack.");

                for (var s = 0; s < existing.Entries.Count; s++)
                {
                    var item = CopyExisting(existing, existing.Entries[s], vectors[s], work);
                    all.Add(item);
                    byId[item.Species.Id] = item;
                }
            }

            using var gbif = new GbifClient { Log = log };
            var where = string.IsNullOrWhiteSpace(options.StateProvince) ? "Brasil" : options.StateProvince;
            log($"Descobrindo espécies no GBIF ({where})...");

            // Busca além das já existentes, para ainda sobrar espécies novas para trazer.
            var discovered = await gbif.DiscoverSpeciesAsync(
                CountryCode,
                options.StateProvince,
                options.MaxSpecies + byId.Count,
                1,
                options.SearchPages,
                ct,
                log);

            if (discovered.Count == 0 && byId.Count == 0)
                throw new InvalidOperationException("O GBIF não retornou espécies para esta região.");

            var newOnes = discovered.Where(x => !byId.ContainsKey(x.Id)).ToList();
            var repeated = discovered.Where(x => byId.ContainsKey(x.Id)).ToList();
            var queue = PackGrowthPolicy.Order(newOnes, repeated);
            log($"Espécies novas: {newOnes.Count} · já no pack: {repeated.Count}");

            if (queue.Count == 0)
                throw new InvalidOperationException("O GBIF não trouxe espécies para ampliar este pack.");

            for (var index = 0; index < queue.Count; index++)
            {
                ct.ThrowIfCancellationRequested();
                var item = queue[index];
                var known = byId.TryGetValue(item.Id, out var current);
                var entry = current ?? new PackSpecies { Species = item };
                if (known)
                {
                    // Nome atualizado pelo GBIF (português ou indígena), sem o fallback antigo.
                    entry.Species.CommonName = item.CommonName;
                    entry.Species.ScientificName = item.ScientificName;
                }

                log($"[{index + 1}/{queue.Count}] {(known ? "já no pack" : "nova")}: {entry.Species.CommonName} — {entry.Species.ScientificName}");

                var wanted = PackGrowthPolicy.NewImagesAllowed(entry.References.Count, options.ImagesPerSpecies);
                var added = wanted == 0
                    ? 0
                    : await CollectImagesAsync(gbif, entry, options, wanted, work, ct);

                if (known)
                {
                    log(wanted == 0
                        ? $"  limite de {PackGrowthPolicy.MaxImagesPerSpecies} imagens atingido"
                        : added == 0
                            ? "  sem imagens novas licenciadas"
                            : $"  +{added} imagem(ns) nova(s); total {entry.References.Count}");
                }
                else if (entry.References.Count == 0)
                {
                    log("  IGNORADA: nenhuma imagem licenciada encontrada.");
                }
                else
                {
                    all.Add(entry);
                    byId[entry.Species.Id] = entry;
                    log(entry.References.Count >= options.ImagesPerSpecies
                        ? $"  OK: {entry.References.Count} imagens + embeddings."
                        : $"  PARCIAL: {entry.References.Count} de {options.ImagesPerSpecies} imagens (salva mesmo assim).");
                }

                progress((index + 1) * 100 / queue.Count);
            }

            if (all.Count == 0)
                throw new InvalidOperationException("Nenhuma espécie conseguiu imagens licenciadas.");

            var totalImages = all.Sum(x => x.References.Count);
            var packName = existing?.Manifest.PackName ?? options.PackName;
            var packId = existing?.Manifest.PackId ?? options.UpdatePackId ?? BuildPackId(packName);
            log($"Escrevendo pack com {all.Count} espécies e {totalImages} imagens...");

            var settings = new PackBuildSettings
            {
                Country = CountryCode,
                StateProvince = options.StateProvince,
                MaxSpecies = options.MaxSpecies,
                SearchPages = options.SearchPages,
                AllowBySa = options.AllowBySa,
                ImagesPerSpecies = options.ImagesPerSpecies
            };
            PackWriter.Write(output, packName, all, work, packId, settings);
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

    // Copia as imagens de um pack existente para a pasta de trabalho, numeradas de novo (1, 2, ...).
    static PackSpecies CopyExisting(FaunaPackData pack, FaunaPackSpeciesEntry source, List<float[]> vectors, string work)
    {
        var id = source.Species.Id;
        var folder = Path.Combine(work, "references", id);
        Directory.CreateDirectory(folder);

        var item = new PackSpecies { Species = ToSpeciesInput(source.Species) };
        for (var r = 0; r < source.References.Count; r++)
        {
            var reference = source.References[r];
            var index = r + 1;
            var from = Path.Combine(pack.DirectoryPath, reference.File.Replace('/', Path.DirectorySeparatorChar));
            File.Copy(from, Path.Combine(folder, $"{index}.jpg"), true);

            item.References.Add(new ReferenceImageInfo
            {
                Index = index,
                File = $"images/{id}/{index}.jpg",
                GbifId = reference.GbifId,
                SourceUrl = reference.SourceUrl,
                OccurrenceUrl = reference.OccurrenceUrl,
                License = reference.License,
                Creator = reference.Creator,
                RightsHolder = reference.RightsHolder,
                Dataset = reference.Dataset
            });
            item.Embeddings.Add(vectors[r]);
        }

        return item;
    }

    // Acrescenta até "wanted" imagens novas à espécie. Nunca repete uma mídia do GBIF nem um arquivo
    // (mesmo conteúdo, mesmo hash) que a espécie já tenha. Devolve quantas entraram.
    async Task<int> CollectImagesAsync(
        GbifClient gbif,
        PackSpecies entry,
        PackBuildOptions options,
        int wanted,
        string work,
        CancellationToken ct)
    {
        var species = entry.Species;
        if (string.IsNullOrWhiteSpace(species.ScientificName))
            return 0;

        var candidates = await gbif.SearchImagesAsync(
            species.ScientificName,
            CountryCode,
            options.StateProvince,
            options.SearchPages,
            ct,
            (page, total) => log($"  busca GBIF: página {page}/{total}"));

        log($"  candidatos GBIF: {candidates.Count}");

        var usedMedia = new HashSet<string>(entry.References.Select(x => x.SourceUrl), StringComparer.OrdinalIgnoreCase);
        var usedHashes = new HashSet<string>(ExistingHashes(work, species.Id, entry.References), StringComparer.OrdinalIgnoreCase);
        var folder = Path.Combine(work, "references", species.Id);
        Directory.CreateDirectory(folder);

        var added = 0;
        foreach (var occurrence in candidates)
        {
            foreach (var media in occurrence.Media)
            {
                ct.ThrowIfCancellationRequested();
                if (added >= wanted)
                    return added;
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

                    var index = entry.References.Count + 1;
                    var localPath = Path.Combine(folder, $"{index}.jpg");
                    await File.WriteAllBytesAsync(localPath, thumbnail, ct);

                    var embedStarted = DateTime.UtcNow;
                    var vector = await Task.Run(() => engine.EncodeImage(localPath), ct);
                    var embedMs = (DateTime.UtcNow - embedStarted).TotalMilliseconds;
                    if (vector.Length != EmbeddingDimension)
                        throw new InvalidOperationException($"Embedding inesperado: {vector.Length} dimensões.");

                    entry.Embeddings.Add(vector);
                    entry.References.Add(new ReferenceImageInfo
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

                    added++;
                    log($"  imagem {index} pronta ({added}/{wanted} nesta rodada; embedding {embedMs:0} ms)");
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
        }

        return added;
    }

    // Hash de cada imagem que a espécie já tem, para não aceitar a mesma foto de novo com outra URL.
    static IEnumerable<string> ExistingHashes(string work, string id, IEnumerable<ReferenceImageInfo> references)
    {
        foreach (var reference in references)
        {
            var path = Path.Combine(work, "references", id, $"{reference.Index}.jpg");
            if (!File.Exists(path)) continue;
            yield return Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        }
    }

    static SpeciesInput ToSpeciesInput(Species species) => new()
    {
        Id = species.Id,
        CommonName = species.CommonName,
        ScientificName = species.ScientificName,
        Height = species.Height,
        Weight = species.Weight,
        Diet = species.Diet,
        Habitat = species.Habitat,
        Curiosities = species.Curiosities,
        GbifTaxonKey = ParseTaxonKey(species.Id)
    };

    // IDs de espécies do GBIF têm a forma "gbif-{chave}".
    static long ParseTaxonKey(string id) =>
        id.StartsWith("gbif-", StringComparison.OrdinalIgnoreCase) &&
        long.TryParse(id.AsSpan(5), out var key)
            ? key
            : 0;

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
