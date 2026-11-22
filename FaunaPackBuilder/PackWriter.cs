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
        string? packIdOverride = null,
        PackBuildSettings? buildSettings = null)
    {
        if (species.Count == 0)
            throw new InvalidOperationException("O pack precisa ter pelo menos 1 espécie.");

        // Cada espécie pode ter menos imagens que as outras (pack parcial ou ampliado).
        var counts = species.Select(x => x.References.Count).ToList();
        if (counts.Min() < 1)
            throw new InvalidOperationException("Toda espécie do pack precisa de pelo menos 1 imagem.");
        if (species.Any(x => x.Embeddings.Count != x.References.Count))
            throw new InvalidOperationException("Cada imagem do pack precisa de um embedding.");

        // Mesma quantidade em todas as espécies: formato 1, igual ao de sempre. Caso contrário: formato 2.
        var formatVersion = counts.All(c => c == counts[0]) ? 1 : 2;
        var referencesPerSpecies = counts.Max();

        var packId = packIdOverride ?? Slug(packName);
        var manifest = new PackManifest
        {
            FormatVersion = formatVersion,
            PackId = packId,
            PackName = packName,
            CreatedUtc = DateTime.UtcNow.ToString("O"),
            ReferencesPerSpecies = referencesPerSpecies,
            SpeciesCount = species.Count,
            BuildSettings = buildSettings
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
            writer.Write(formatVersion);
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
