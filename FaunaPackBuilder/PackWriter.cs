using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Encodings.Web;

namespace FaunaPackBuilder;

public static class PackWriter
{
    static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static void Write(
        string outputPath,
        string packName,
        IReadOnlyList<PackSpecies> species,
        string tempDirectory,
        string? packIdOverride = null)
    {
        var referencesPerSpecies = species.Count == 0 ? 0 : species[0].References.Count;
        if (referencesPerSpecies < 1)
            throw new InvalidOperationException("O pack precisa ter pelo menos 1 imagem por espécie.");

        if (species.Any(x => x.References.Count != referencesPerSpecies || x.Embeddings.Count != referencesPerSpecies))
            throw new InvalidOperationException("Todas as espécies precisam ter a mesma quantidade de imagens e embeddings.");

        var packId = packIdOverride ?? Slug(packName);
        var manifest = new PackManifest
        {
            PackId = packId,
            PackName = packName,
            CreatedUtc = DateTime.UtcNow.ToString("O"),
            ReferencesPerSpecies = referencesPerSpecies,
            SpeciesCount = species.Count
        };

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        if (File.Exists(outputPath)) File.Delete(outputPath);

        var staging = Path.Combine(tempDirectory, "pack");
        if (Directory.Exists(staging)) Directory.Delete(staging, true);
        Directory.CreateDirectory(Path.Combine(staging, "images"));

        var cleanSpecies = species.Select(x => new
        {
            x.Species,
            x.References
        }).ToList();

        File.WriteAllText(Path.Combine(staging, "manifest.json"), JsonSerializer.Serialize(manifest, JsonOptions), new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(staging, "species.json"), JsonSerializer.Serialize(cleanSpecies, JsonOptions), new UTF8Encoding(false));

        using (var stream = File.Create(Path.Combine(staging, "embeddings.bin")))
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write(Encoding.ASCII.GetBytes("FLPK"));
            writer.Write(1);
            writer.Write(species.Count);
            writer.Write(referencesPerSpecies);
            writer.Write(512);
            foreach (var item in species)
                foreach (var vector in item.Embeddings)
                    foreach (var value in vector)
                        writer.Write(value);
        }

        foreach (var item in species)
        {
            foreach (var reference in item.References)
            {
                var source = Path.Combine(tempDirectory, "references", item.Species.Id, $"{reference.Index}.jpg");
                var target = Path.Combine(staging, reference.File.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(source, target, true);
            }
        }

        ZipFile.CreateFromDirectory(staging, outputPath, CompressionLevel.Optimal, false);
    }

    public static void WriteCompatibilityJson(string outputPath, IReadOnlyList<PackSpecies> species)
    {
        var output = species.Select(item =>
        {
            var mean = new float[512];
            foreach (var vector in item.Embeddings)
                for (var i = 0; i < mean.Length && i < vector.Length; i++)
                    mean[i] += vector[i] / item.Embeddings.Count;
            Normalize(mean);
            return new
            {
                item.Species.Id,
                item.Species.CommonName,
                item.Species.ScientificName,
                item.Species.Height,
                item.Species.Weight,
                item.Species.Diet,
                item.Species.Habitat,
                item.Species.Curiosities,
                Embedding = mean,
                ReferenceCount = item.References.Count,
                EmbeddingModel = "bioclip-v1-image-encoder"
            };
        }).ToList();

        File.WriteAllText(outputPath, JsonSerializer.Serialize(output, JsonOptions), new UTF8Encoding(false));
    }

    static void Normalize(float[] vector)
    {
        double sum = 0;
        foreach (var value in vector) sum += value * value;
        var norm = Math.Sqrt(sum);
        if (norm == 0) return;
        for (var i = 0; i < vector.Length; i++) vector[i] = (float)(vector[i] / norm);
    }

    static string Slug(string value)
    {
        var chars = value.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray();
        return new string(chars).Trim('-');
    }
}
