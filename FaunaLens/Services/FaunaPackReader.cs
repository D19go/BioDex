using System.IO.Compression;
using System.Text;
using System.Text.Json;
using FaunaLens.Models;

namespace FaunaLens.Services;

public sealed class FaunaPackReader
{
    const string Magic = "FLPK";
    const int ExpectedVersion = 1;
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
            if (entry.References.Count != manifest.ReferencesPerSpecies)
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

        var embeddings = await ReadEmbeddingsAsync(Path.Combine(packDirectory, manifest.EmbeddingFile), entries.Count, manifest.EmbeddingDimension, manifest.ReferencesPerSpecies, cancellationToken);
        for (var i = 0; i < entries.Count; i++) entries[i].Species.Embedding = embeddings[i];
        return new FaunaPackData(manifest, entries, packDirectory);
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

    static async Task<List<float[]>> ReadEmbeddingsAsync(string path, int speciesCount, int dimension, int referencesPerSpecies, CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) throw new InvalidDataException("embeddings.bin não encontrado.");
        var expectedVectors = checked(speciesCount * referencesPerSpecies);
        var expectedBytes = checked(4L + sizeof(int) * 4L + expectedVectors * dimension * sizeof(float));
        var actualBytes = new FileInfo(path).Length;
        if (actualBytes != expectedBytes) throw new InvalidDataException($"embeddings.bin possui tamanho incompatível. Esperado: {expectedBytes} bytes; recebido: {actualBytes}.");

        var output = new List<float[]>(speciesCount);
        await using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: false);
        var magic = Encoding.ASCII.GetString(reader.ReadBytes(4));
        var version = reader.ReadInt32();
        var fileSpeciesCount = reader.ReadInt32();
        var fileReferences = reader.ReadInt32();
        var fileDimension = reader.ReadInt32();
        if (magic != Magic) throw new InvalidDataException("embeddings.bin não é um FaunaPack válido.");
        if (version != ExpectedVersion) throw new InvalidDataException($"Versão de FaunaPack não suportada: {version}.");
        if (fileSpeciesCount != speciesCount) throw new InvalidDataException("A quantidade de espécies não coincide com species.json.");
        if (fileReferences != referencesPerSpecies) throw new InvalidDataException("A quantidade de referências não coincide com o manifesto.");
        if (fileDimension != dimension || dimension != ExpectedDimension) throw new InvalidDataException("A dimensão dos embeddings não é compatível com BioCLIP v1.");

        for (var speciesIndex = 0; speciesIndex < speciesCount; speciesIndex++)
        {
            var vectors = new List<float[]>(referencesPerSpecies);
            for (var referenceIndex = 0; referenceIndex < referencesPerSpecies; referenceIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var vector = new float[dimension];
                for (var i = 0; i < dimension; i++) vector[i] = reader.ReadSingle();
                Normalize(vector);
                vectors.Add(vector);
            }
            var mean = new float[dimension];
            foreach (var vector in vectors) for (var i = 0; i < dimension; i++) mean[i] += vector[i] / vectors.Count;
            Normalize(mean);
            output.Add(mean);
        }
        return output;
    }

    static void ValidateManifest(FaunaPackManifest manifest)
    {
        if (manifest.FormatVersion != ExpectedVersion) throw new InvalidDataException($"Versão de FaunaPack não suportada: {manifest.FormatVersion}.");
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

