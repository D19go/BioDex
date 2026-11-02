using System.Text.Json.Serialization;

namespace FaunaLens.Models;

public sealed class FaunaPackManifest
{
    public int FormatVersion { get; set; }
    public string PackId { get; set; } = "";
    public string PackName { get; set; } = "";
    public string CreatedUtc { get; set; } = "";
    public string EmbeddingModel { get; set; } = "";
    public int EmbeddingDimension { get; set; }
    public int ReferencesPerSpecies { get; set; }
    public int SpeciesCount { get; set; }
    public string EmbeddingFile { get; set; } = "embeddings.bin";
    public string ImageFolder { get; set; } = "images";
}

public sealed class FaunaPackSpeciesEntry
{
    public Species Species { get; set; } = new();
    public List<FaunaPackReference> References { get; set; } = new();
}

public sealed class FaunaPackReference
{
    public int Index { get; set; }
    public string File { get; set; } = "";
    public long GbifId { get; set; }
    public string SourceUrl { get; set; } = "";
    public string OccurrenceUrl { get; set; } = "";
    public string? License { get; set; }
    public string? Creator { get; set; }
    public string? RightsHolder { get; set; }
    public string? Dataset { get; set; }
}

public sealed record FaunaPackData(
    FaunaPackManifest Manifest,
    IReadOnlyList<FaunaPackSpeciesEntry> Entries,
    string DirectoryPath);
