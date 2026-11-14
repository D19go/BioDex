using System.Text.Json.Serialization;

namespace FaunaPackBuilder;

public sealed class SpeciesInput
{
    public string Id { get; set; } = "";
    public string CommonName { get; set; } = "";
    public string ScientificName { get; set; } = "";
    public string Height { get; set; } = "";
    public string Weight { get; set; } = "";
    public string Diet { get; set; } = "";
    public string Habitat { get; set; } = "";
    public string Curiosities { get; set; } = "";
    public long GbifTaxonKey { get; set; }
}

public sealed class GbifSearchResponse
{
    [JsonPropertyName("results")]
    public List<GbifOccurrence> Results { get; set; } = new();

    [JsonPropertyName("count")]
    public int Count { get; set; }

    [JsonPropertyName("facets")]
    public List<GbifFacet> Facets { get; set; } = new();
}

public sealed class GbifFacet
{
    [JsonPropertyName("field")]
    public string? Field { get; set; }

    [JsonPropertyName("counts")]
    public List<GbifFacetCount> Counts { get; set; } = new();
}

public sealed class GbifFacetCount
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("count")]
    public int Count { get; set; }
}

public sealed class GbifTaxonResponse
{
    [JsonPropertyName("key")]
    public long Key { get; set; }

    [JsonPropertyName("scientificName")]
    public string? ScientificName { get; set; }

    [JsonPropertyName("canonicalName")]
    public string? CanonicalName { get; set; }

    [JsonPropertyName("rank")]
    public string? Rank { get; set; }

    [JsonPropertyName("kingdom")]
    public string? Kingdom { get; set; }
}

public sealed class GbifVernacularResponse
{
    [JsonPropertyName("results")]
    public List<GbifVernacularName> Results { get; set; } = new();
}

public sealed class GbifVernacularName
{
    [JsonPropertyName("vernacularName")]
    public string? VernacularName { get; set; }

    [JsonPropertyName("language")]
    public string? Language { get; set; }

    [JsonPropertyName("preferred")]
    public bool? Preferred { get; set; }
}

public sealed class GbifOccurrence
{
    [JsonPropertyName("key")]
    public long Key { get; set; }

    [JsonPropertyName("scientificName")]
    public string? ScientificName { get; set; }

    [JsonPropertyName("species")]
    public string? Species { get; set; }

    [JsonPropertyName("speciesKey")]
    public long? SpeciesKey { get; set; }

    [JsonPropertyName("datasetName")]
    public string? DatasetName { get; set; }

    [JsonPropertyName("publisher")]
    public string? Publisher { get; set; }

    [JsonPropertyName("rightsHolder")]
    public string? RightsHolder { get; set; }

    [JsonPropertyName("references")]
    public string? References { get; set; }

    [JsonPropertyName("media")]
    public List<GbifMedia> Media { get; set; } = new();
}

public sealed class GbifMedia
{
    [JsonPropertyName("identifier")]
    public string? Identifier { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("format")]
    public string? Format { get; set; }

    [JsonPropertyName("license")]
    public string? License { get; set; }

    [JsonPropertyName("creator")]
    public string? Creator { get; set; }

    [JsonPropertyName("rightsHolder")]
    public string? RightsHolder { get; set; }

    [JsonPropertyName("references")]
    public string? References { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }
}

public sealed class ReferenceImageInfo
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

public sealed class PackSpecies
{
    public SpeciesInput Species { get; set; } = new();
    public List<ReferenceImageInfo> References { get; set; } = new();
    public List<float[]> Embeddings { get; set; } = new();
}

public sealed class PackManifest
{
    public int FormatVersion { get; set; } = 1;
    public string PackId { get; set; } = "";
    public string PackName { get; set; } = "";
    public string CreatedUtc { get; set; } = "";
    public string EmbeddingModel { get; set; } = "BioCLIP-v1-image-encoder";
    public int EmbeddingDimension { get; set; } = 512;
    public int ReferencesPerSpecies { get; set; } = 2;
    public int SpeciesCount { get; set; }
    public string EmbeddingFile { get; set; } = "embeddings.bin";
    public string ImageFolder { get; set; } = "images";
    public string Source { get; set; } = "GBIF occurrence search API";

    // Como o pack foi criado: permite refazê-lo com as mesmas opções (por exemplo, com mais imagens).
    public PackBuildSettings? BuildSettings { get; set; }
}

public sealed class PackBuildSettings
{
    public string Country { get; set; } = "BR";
    public string StateProvince { get; set; } = "";
    public int MaxSpecies { get; set; }
    public int SearchPages { get; set; }
    public bool AllowBySa { get; set; }
    public int ImagesPerSpecies { get; set; }
}
