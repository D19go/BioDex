using System.Text.Json.Serialization;

namespace FaunaLens.Models;

public sealed class Species
{
    // Quantas imagens de referência mostrar (na identificação e nas propriedades do pack).
    public const int ShownReferenceImages = 2;

    public string Id { get; set; } = "";
    public string CommonName { get; set; } = "";
    public string ScientificName { get; set; } = "";
    public string Height { get; set; } = "";
    public string Weight { get; set; } = "";
    public string Diet { get; set; } = "";
    public string Habitat { get; set; } = "";
    public string Curiosities { get; set; } = "";
    public float[] Embedding { get; set; } = Array.Empty<float>();

    // Caminhos completos das imagens de referência no pack instalado. Só existe em memória.
    [JsonIgnore]
    public List<string> ImagePaths { get; set; } = new();
}

public sealed record SpeciesMatch(Species Species, float Score);
