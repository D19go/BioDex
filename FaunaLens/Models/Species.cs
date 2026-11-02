namespace FaunaLens.Models;

public sealed class Species
{
    public string Id { get; set; } = "";
    public string CommonName { get; set; } = "";
    public string ScientificName { get; set; } = "";
    public string Height { get; set; } = "";
    public string Weight { get; set; } = "";
    public string Diet { get; set; } = "";
    public string Habitat { get; set; } = "";
    public string Curiosities { get; set; } = "";
    public float[] Embedding { get; set; } = Array.Empty<float>();
}

public sealed record SpeciesMatch(Species Species, float Score);
