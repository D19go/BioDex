using System.IO.Compression;
using System.Text;
using System.Text.Json;
using FaunaLens.Models;

namespace FaunaLens.Services;

public sealed class FaunaPackReader
{
    const string Magic = "FLPK";

    // 1: todas as espécies têm a mesma quantidade de imagens. 2: cada espécie tem a sua (pack parcial ou ampliado).
    const int MaxFormatVersion = 2;
    const int ExpectedDimension = 512;
    const long MaxExtractedBytes = 512L * 1024 * 1024;
    const int MaxSpecies = 5000;
    const int MaxReferencesPerSpecies = 200;

    static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public async Task<FaunaPackData> InstallAsync(string sourcePath, string destinationDirectory, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(sourcePath)) throw new FileNotFoundException("FaunaPack não encontrado.", sourcePath);
        Directory.CreateDirectory(destinationDirectory);
        var temporaryDirectory = destinationDirectory + ".installing";
        if (Directory.Exists(temporaryDirectory)) Directory.Delete(temporaryDirectory, true);
        Directory.CreateDirectory(temporaryDirectory);

        try
        {
            await Task.Run(() => ExtractAndValidate(sourcePath, temporaryDirectory, cancellationToken), cancellationToken);
            var manifest = await ReadJsonAsync<FaunaPackManifest>(Path.Combine(temporaryDirectory, "manifest.json"), cancellationToken);
            ValidateManifest(manifest);
            var finalDirectory = Path.Combine(destinationDirectory, SanitizePackId(manifest.PackId));
            if (Directory.Exists(finalDirectory)) Directory.Delete(finalDirectory, true);
            Directory.Move(temporaryDirectory, finalDirectory);
            return await LoadInstalledAsync(finalDirectory, cancellationToken);
        }
        catch
        {
            if (Directory.Exists(temporaryDirectory)) Directory.Delete(temporaryDirectory, true);
            throw;
        }
    }

    public async Task<FaunaPackData> LoadInstalledAsync(string packDirectory, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(packDirectory)) throw new DirectoryNotFoundException("Pasta do FaunaPack não encontrada.");
        var manifest = await ReadJsonAsync<FaunaPackManifest>(Path.Combine(packDirectory, "manifest.json"), cancellationToken);
        ValidateManifest(manifest);
        var entries = await ReadJsonAsync<List<FaunaPackSpeciesEntry>>(Path.Combine(packDirectory, "species.json"), cancellationToken)
            ?? throw new InvalidDataException("species.json está vazio ou inválido.");

        if (entries.Count == 0) throw new InvalidDataException("O FaunaPack não possui espécies.");

        // Pacotes gerados por versões antigas do Builder não gravavam a contagem no manifesto.
        if (manifest.SpeciesCount == 0) manifest.SpeciesCount = entries.Count;
        if (manifest.SpeciesCount < 1 || manifest.SpeciesCount > MaxSpecies) throw new InvalidDataException("A quantidade de espécies do pack não é suportada.");
        if (entries.Count != manifest.SpeciesCount) throw new InvalidDataException("A quantidade de espécies não coincide com o manifesto.");

        foreach (var entry in entries)
        {
            if (entry.Species is null || string.IsNullOrWhiteSpace(entry.Species.Id))
                throw new InvalidDataException("Uma espécie do FaunaPack possui ID inválido.");
            if (entry.References.Count < 1 || entry.References.Count > MaxReferencesPerSpecies)
                throw new InvalidDataException($"A espécie '{entry.Species.CommonName}' tem {entry.References.Count} imagens; o limite é de 1 a {MaxReferencesPerSpecies}.");
            if (manifest.FormatVersion == 1 && entry.References.Count != manifest.ReferencesPerSpecies)
                throw new InvalidDataException($"A espécie '{entry.Species.CommonName}' possui {entry.References.Count} referências; o manifesto informa {manifest.ReferencesPerSpecies}.");

            foreach (var reference in entry.References)
            {
                if (string.IsNullOrWhiteSpace(reference.File)) throw new InvalidDataException("Uma referência não possui arquivo.");
                var relative = reference.File.Replace('\\', '/').TrimStart('/');
                if (relative.Contains("../", StringComparison.Ordinal) || relative.Equals("..", StringComparison.Ordinal) || Path.IsPathRooted(relative))
                    throw new InvalidDataException("O FaunaPack contém uma referência de arquivo inválida.");
                var imagePath = Path.GetFullPath(Path.Combine(packDirectory, relative));
                var root = Path.GetFullPath(packDirectory) + Path.DirectorySeparatorChar;
                if (!imagePath.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !File.Exists(imagePath))
                    throw new InvalidDataException($"Imagem de referência não encontrada: {reference.File}");
            }
        }

        var counts = entries.Select(entry => entry.References.Count).ToList();
        var vectors = await ReadVectorsAsync(Path.Combine(packDirectory, manifest.EmbeddingFile), counts, manifest.ReferencesPerSpecies, cancellationToken);
        for (var i = 0; i < entries.Count; i++) entries[i].Species.Embedding = Mean(vectors[i]);
        return new FaunaPackData(manifest, entries, packDirectory);
    }

    // Vetor de cada imagem (já normalizado), na ordem de species.json. Permite ampliar um pack
    // reaproveitando os embeddings que ele já tem, sem recalculá-los.
    public async Task<List<List<float[]>>> ReadReferenceEmbeddingsAsync(FaunaPackData data, CancellationToken cancellationToken = default)
    {
        var counts = data.Entries.Select(entry => entry.References.Count).ToList();
        return await ReadVectorsAsync(
            Path.Combine(data.DirectoryPath, data.Manifest.EmbeddingFile),
            counts,
            data.Manifest.ReferencesPerSpecies,
            cancellationToken);
    }

    static void ExtractAndValidate(string sourcePath, string destinationDirectory, CancellationToken cancellationToken)
    {
        using var archive = ZipFile.OpenRead(sourcePath);
        long extractedBytes = 0;
        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(entry.FullName)) continue;
            var relative = entry.FullName.Replace('\\', '/').TrimStart('/');
            if (relative.Contains("../", StringComparison.Ordinal) || relative.Equals("..", StringComparison.Ordinal) || Path.IsPathRooted(relative))
                throw new InvalidDataException("O FaunaPack contém um caminho de arquivo inválido.");
            if (entry.Length > MaxExtractedBytes || extractedBytes > MaxExtractedBytes - entry.Length)
                throw new InvalidDataException("O FaunaPack excede o limite de armazenamento permitido.");
            var target = Path.GetFullPath(Path.Combine(destinationDirectory, relative));
            var root = Path.GetFullPath(destinationDirectory) + Path.DirectorySeparatorChar;
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("O FaunaPack contém um caminho fora da pasta de instalação.");
            if (string.IsNullOrEmpty(entry.Name)) { Directory.CreateDirectory(target); continue; }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using var input = entry.Open();
            using var output = File.Create(target);
            input.CopyTo(output);
            extractedBytes += entry.Length;
        }
    }

    static async Task<T> ReadJsonAsync<T>(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) throw new InvalidDataException($"Arquivo obrigatório não encontrado: {Path.GetFileName(path)}.");
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken)
            ?? throw new InvalidDataException($"Arquivo JSON inválido: {Path.GetFileName(path)}.");
    }

    // Lê os vetores de todas as imagens. counts[i] é a quantidade de imagens da espécie i.
    static async Task<List<List<float[]>>> ReadVectorsAsync(string path, IReadOnlyList<int> counts, int referencesPerSpecies, CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) throw new InvalidDataException("embeddings.bin não encontrado.");

        long expectedVectors = 0;
        foreach (var count in counts) expectedVectors = checked(expectedVectors + count);
        var expectedBytes = checked(4L + sizeof(int) * 4L + expectedVectors * ExpectedDimension * sizeof(float));
        var actualBytes = new FileInfo(path).Length;
        if (actualBytes != expectedBytes) throw new InvalidDataException($"embeddings.bin possui tamanho incompatível. Esperado: {expectedBytes} bytes; recebido: {actualBytes}.");

        var output = new List<List<float[]>>(counts.Count);
        await using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: false);
        var magic = Encoding.ASCII.GetString(reader.ReadBytes(4));
        var version = reader.ReadInt32();
        var fileSpeciesCount = reader.ReadInt32();
        var fileReferences = reader.ReadInt32();
        var fileDimension = reader.ReadInt32();
        if (magic != Magic) throw new InvalidDataException("embeddings.bin não é um FaunaPack válido.");
        if (version is < 1 or > MaxFormatVersion) throw new InvalidDataException($"Versão de FaunaPack não suportada: {version}.");
        if (fileSpeciesCount != counts.Count) throw new InvalidDataException("A quantidade de espécies não coincide com species.json.");
        if (fileReferences != referencesPerSpecies) throw new InvalidDataException("A quantidade de referências não coincide com o manifesto.");
        if (fileDimension != ExpectedDimension) throw new InvalidDataException("A dimensão dos embeddings não é compatível com BioCLIP v1.");

        for (var speciesIndex = 0; speciesIndex < counts.Count; speciesIndex++)
        {
            var vectors = new List<float[]>(counts[speciesIndex]);
            for (var referenceIndex = 0; referenceIndex < counts[speciesIndex]; referenceIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var vector = new float[ExpectedDimension];
                for (var i = 0; i < ExpectedDimension; i++) vector[i] = reader.ReadSingle();
                Normalize(vector);
                vectors.Add(vector);
            }
            output.Add(vectors);
        }
        return output;
    }

    // Embedding da espécie: média normalizada das imagens.
    static float[] Mean(List<float[]> vectors)
    {
        var mean = new float[ExpectedDimension];
        foreach (var vector in vectors)
            for (var i = 0; i < ExpectedDimension; i++) mean[i] += vector[i] / vectors.Count;
        Normalize(mean);
        return mean;
    }

    static void ValidateManifest(FaunaPackManifest manifest)
    {
        if (manifest.FormatVersion is < 1 or > MaxFormatVersion) throw new InvalidDataException($"Versão de FaunaPack não suportada: {manifest.FormatVersion}.");
        if (manifest.EmbeddingDimension != ExpectedDimension) throw new InvalidDataException("O FaunaPack não usa embeddings BioCLIP v1 de 512 dimensões.");
        if (manifest.ReferencesPerSpecies < 1 || manifest.ReferencesPerSpecies > MaxReferencesPerSpecies) throw new InvalidDataException("A quantidade de referências por espécie não é suportada.");
        if (!string.Equals(manifest.EmbeddingFile, "embeddings.bin", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Arquivo de embeddings não suportado pelo formato atual.");
        if (!string.Equals(manifest.ImageFolder, "images", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Pasta de imagens não suportada pelo formato atual.");
        if (!manifest.EmbeddingModel.Contains("BioCLIP", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("O modelo de embeddings do pack não é compatível com este Fauna Lens.");
    }

    static string SanitizePackId(string value)
    {
        var chars = value.Where(char.IsLetterOrDigit).ToArray();
        if (chars.Length == 0) throw new InvalidDataException("PackId inválido.");
        return new string(chars);
    }

    static void Normalize(float[] vector)
    {
        double sum = 0;
        foreach (var value in vector) sum += value * value;
        var norm = Math.Sqrt(sum);
        if (norm == 0) return;
        for (var i = 0; i < vector.Length; i++) vector[i] = (float)(vector[i] / norm);
    }
}
