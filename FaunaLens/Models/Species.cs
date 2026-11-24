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

    // Dados do documento de pontuação. Zero = ainda não curado (o app usa o padrão de ScoringRules).
    public string Group { get; set; } = "";
    public int Rarity { get; set; }
    public int Difficulty { get; set; }
    public int Size { get; set; }
    public int Conservation { get; set; }
    public float[] Embedding { get; set; } = Array.Empty<float>();

    // Caminhos completos das imagens de referência no pack instalado. Só existe em memória.
    [JsonIgnore]
    public List<string> ImagePaths { get; set; } = new();

    // Quantas referências deram origem ao Embedding (usado para a média ponderada entre packs).
    [JsonIgnore]
    public int ReferenceCount { get; set; }

    // Embedding de cada referência, para o modo "vizinhos". Só existe em memória.
    [JsonIgnore]
    public float[][] ReferenceEmbeddings { get; set; } = Array.Empty<float[]>();
}

public sealed record SpeciesMatch(Species Species, float Score);
