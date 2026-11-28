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
    // "checkpoint" recebe um pack parcial (com o que já foi baixado) sempre que ele precisa ser salvo:
    // a cada alguns passos, e também ao cancelar ou falhar. Quem o recebe instala o pack.
    public async Task<string> BuildToFileAsync(
        PackBuildOptions requested,
        InstalledFaunaPack? baseline,
        CancellationToken ct,
        Func<string, Task>? checkpoint = null)
    {
        var options = requested.Normalized();
        if (!engine.IsReady)
            throw new InvalidOperationException("O BioCLIP não está carregado.");

        var buildRoot = Path.Combine(FileSystem.Root, "pack-build");
        var work = Path.Combine(buildRoot, Guid.NewGuid().ToString("N"));
        var output = Path.Combine(buildRoot, $"{Guid.NewGuid():N}.faunapack");
        Directory.CreateDirectory(Path.Combine(work, "references"));

        // Estado da criação. Fica fora do try para que a rotina de salvamento parcial (catch) enxergue tudo.
        var all = new List<PackSpecies>();
        var byId = new Dictionary<string, PackSpecies>(StringComparer.OrdinalIgnoreCase);
        FaunaPackData? existing = null;
        PackSpecies? inProgress = null;
        string? resolvedPackId = null;

        string PackNameNow() => existing?.Manifest.PackName ?? options.PackName;

        // Id fixo durante toda a criação: os checkpoints e o pack final substituem o mesmo pack.
        string PackIdNow() => resolvedPackId ??= existing?.Manifest.PackId ?? options.UpdatePackId ?? BuildPackId(PackNameNow());

        PackBuildSettings MakeSettings() => new()
        {
            Country = CountryCode,
            StateProvince = options.StateProvince,
            MaxSpecies = options.MaxSpecies,
            SearchPages = options.SearchPages,
            AllowBySa = options.AllowBySa,
            ImagesPerSpecies = options.ImagesPerSpecies
        };

        // Espécies já processadas com pelo menos uma imagem, mais a que estava em andamento.
        List<PackSpecies> Snapshot()
        {
            var list = all.Where(x => x.References.Count > 0).ToList();
            if (inProgress is not null && inProgress.References.Count > 0 && !list.Contains(inProgress))
                list.Add(inProgress);
            return list;
        }

        async Task SaveCheckpointAsync()
        {
            if (checkpoint is null)
                return;

            var snapshot = Snapshot();
            if (snapshot.Count == 0)
                return;

            PackWriter.Write(output, PackNameNow(), snapshot, work, PackIdNow(), MakeSettings());
            await checkpoint(output);
            log($"Progresso salvo no pack: {snapshot.Count} espécies, {snapshot.Sum(x => x.References.Count)} imagens.");
        }

        // Salva por tempo, e não por número de espécies: com 50 imagens por espécie, 5 espécies levam
        // dezenas de minutos e o sistema pode encerrar o app antes de qualquer salvamento.
        // Uma falha aqui é registrada e não interrompe a criação.
        DateTime lastSavedUtc = DateTime.UtcNow;
        async Task CheckpointIfDueAsync()
        {
            if (!PackGrowthPolicy.ShouldCheckpoint(lastSavedUtc, DateTime.UtcNow, PackGrowthPolicy.CheckpointInterval))
                return;

            try
            {
                await SaveCheckpointAsync();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log($"Não foi possível salvar o progresso agora: {ex.Message}");
            }
            finally
            {
                lastSavedUtc = DateTime.UtcNow;
            }
        }

        try
        {
            if (baseline is not null)
            {
                existing = await new FaunaPackReader().LoadInstalledAsync(baseline.DirectoryPath, ct);
                var vectors = await new FaunaPackReader().ReadReferenceEmbeddingsAsync(existing, ct);
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
                inProgress = entry;

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
                    : await CollectImagesAsync(gbif, entry, options, wanted, work, ct, CheckpointIfDueAsync);

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

                inProgress = null;
                progress((index + 1) * 100 / queue.Count);
                await CheckpointIfDueAsync();
            }

            if (all.Count == 0)
                throw new InvalidOperationException("Nenhuma espécie conseguiu imagens licenciadas.");

            var totalImages = all.Sum(x => x.References.Count);
            log($"Escrevendo pack com {all.Count} espécies e {totalImages} imagens...");
            PackWriter.Write(output, PackNameNow(), all, work, PackIdNow(), MakeSettings());
            return output;
        }
        catch
        {
            // Salva o que já foi baixado (também ao cancelar ou falhar): a próxima criação da região continua daqui.
            try { await SaveCheckpointAsync(); }
            catch { /* o erro original é o que importa */ }

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

    // Quantas imagens são baixadas ao mesmo tempo. Enquanto o celular converte e mede uma imagem,
    // as próximas já estão chegando do GBIF. Só a rede roda em paralelo; o resto segue em ordem.
    const int DownloadWindow = 4;

    // Imagem já baixada, esperando ser processada na ordem de prioridade.
    sealed record FetchedImage(GbifOccurrence Occurrence, GbifMedia Media, int Limit, byte[] Raw, double Seconds, Exception? Error);

    // Acrescenta até "wanted" imagens novas à espécie. Nunca repete uma mídia do GBIF nem um arquivo
    // (mesmo conteúdo, mesmo hash) que a espécie já tenha. Devolve quantas entraram.
    // Prioridade a espécimes novos: primeiro uma foto de cada registro GBIF que o pack ainda não tem;
    // depois outras fotos de registros novos; só por último, fotos de registros que o pack já tem.
    async Task<int> CollectImagesAsync(
        GbifClient gbif,
        PackSpecies entry,
        PackBuildOptions options,
        int wanted,
        string work,
        CancellationToken ct,
        Func<Task> afterImage)
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
            (page, total) => log($"  busca GBIF: página {page}/{total}"),
            taxonKey: species.GbifTaxonKey);

        log($"  candidatos GBIF: {candidates.Count}");

        var usedMedia = new HashSet<string>(entry.References.Select(x => x.SourceUrl), StringComparer.OrdinalIgnoreCase);
        var usedHashes = new HashSet<string>(ExistingHashes(work, species.Id, entry.References), StringComparer.OrdinalIgnoreCase);
        var usedOccurrences = new HashSet<long>(entry.References.Select(x => x.GbifId));
        var fresh = candidates.Where(o => !usedOccurrences.Contains(o.Key)).ToList();
        var reused = candidates.Where(o => usedOccurrences.Contains(o.Key)).ToList();
        var passes = new (List<GbifOccurrence> Group, int Limit)[]
        {
            (fresh, 1),
            (fresh, int.MaxValue),
            (reused, int.MaxValue)
        };
        var takenPerOccurrence = new Dictionary<long, int>();

        var folder = Path.Combine(work, "references", species.Id);
        Directory.CreateDirectory(folder);

        // Mídias na ordem de prioridade, já filtradas pela licença. O limite por registro é conferido ao usar.
        IEnumerable<(GbifOccurrence Occurrence, GbifMedia Media, int Limit)> Ordered()
        {
            foreach (var (group, limit) in passes)
            {
                foreach (var occurrence in group)
                {
                    foreach (var media in occurrence.Media)
                    {
                        if (!GbifLicense.IsAllowed(media.License, options.AllowBySa)) continue;
                        if (string.IsNullOrWhiteSpace(media.Identifier)) continue;
                        if (!usedMedia.Add(media.Identifier)) continue;
                        yield return (occurrence, media, limit);
                    }
                }
            }
        }

        // Só baixa. Não toca em log, lista nem arquivo: roda em paralelo com as outras.
        async Task<FetchedImage> FetchAsync(GbifOccurrence occurrence, GbifMedia media, int limit)
        {
            var started = DateTime.UtcNow;
            try
            {
                var raw = await gbif.DownloadImageAsync(occurrence, media, ct);
                return new FetchedImage(occurrence, media, limit, raw, (DateTime.UtcNow - started).TotalSeconds, null);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return new FetchedImage(occurrence, media, limit, Array.Empty<byte>(), 0, ex);
            }
        }

        using var sequence = Ordered().GetEnumerator();
        var inFlight = new Queue<Task<FetchedImage>>();
        var exhausted = false;

        // Mantém até DownloadWindow downloads em andamento.
        void Refill()
        {
            while (!exhausted && inFlight.Count < DownloadWindow)
            {
                if (!sequence.MoveNext())
                {
                    exhausted = true;
                    break;
                }

                var next = sequence.Current;
                inFlight.Enqueue(FetchAsync(next.Occurrence, next.Media, next.Limit));
            }
        }

        var added = 0;
        Refill();
        while (added < wanted && inFlight.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var fetched = await inFlight.Dequeue();
            Refill();

            var key = fetched.Occurrence.Key;
            if (takenPerOccurrence.GetValueOrDefault(key) >= fetched.Limit)
            {
                await afterImage();
                continue;
            }

            log($"  GBIF {key}: {fetched.Raw.Length / 1024} KB em {fetched.Seconds:0.0} s");
            try
            {
                if (fetched.Error is not null)
                {
                    log($"  imagem ignorada: {fetched.Error.Message}");
                }
                else if (await TryAcceptAsync(fetched, entry, folder, work, usedHashes, ct))
                {
                    added++;
                    takenPerOccurrence[key] = takenPerOccurrence.GetValueOrDefault(key) + 1;
                    log($"  imagem {entry.References.Count} pronta ({added}/{wanted} nesta rodada)");
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                log($"  imagem ignorada: {ex.Message}");
            }

            // Fora do try acima: uma falha no salvamento não vira "imagem ignorada".
            await afterImage();
        }

        // Downloads que sobrarem continuam em segundo plano só na memória; não são usados.
        return added;
    }

    // Miniatura, hash e embedding de uma imagem baixada. Devolve false se ela não serve (pequena ou repetida).
    async Task<bool> TryAcceptAsync(
        FetchedImage fetched,
        PackSpecies entry,
        string folder,
        string work,
        HashSet<string> usedHashes,
        CancellationToken ct)
    {
        if (fetched.Raw.Length < MinImageBytes)
            return false;

        var thumbnail = await Task.Run(() => MakeThumbnail(fetched.Raw, work), ct);
        if (thumbnail.Length < MinImageBytes)
            return false;

        var hash = Convert.ToHexString(SHA256.HashData(thumbnail));
        if (!usedHashes.Add(hash))
            return false;

        var species = entry.Species;
        var index = entry.References.Count + 1;
        var localPath = Path.Combine(folder, $"{index}.jpg");
        await File.WriteAllBytesAsync(localPath, thumbnail, ct);

        var embedStarted = DateTime.UtcNow;
        var vector = await Task.Run(() => engine.EncodeImage(localPath), ct);
        var embedMs = (DateTime.UtcNow - embedStarted).TotalMilliseconds;
        if (vector.Length != EmbeddingDimension)
            throw new InvalidOperationException($"Embedding inesperado: {vector.Length} dimensões.");

        var occurrence = fetched.Occurrence;
        var media = fetched.Media;
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

        log($"    embedding {embedMs:0} ms");
        return true;
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
        GbifTaxonKey = ParseTaxonKey(species.Id),
        Group = species.Group,
        Rarity = species.Rarity,
        Difficulty = species.Difficulty,
        Size = species.Size,
        Conservation = species.Conservation
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
