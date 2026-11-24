namespace FaunaLens.Services;

// Regras de comparação entre embeddings. Ficam fora do Android para poderem ser testadas.
public static class MatchingPolicy
{
    // Quantas referências mais parecidas entram na média do modo "vizinhos".
    public const int NearestK = 3;

    // Média ponderada pelo número de referências que cada embedding representa.
    // Sem peso, uma espécie em dois packs pesaria o mesmo que outra com 50 referências a mais.
    public static float[] WeightedMean(float[] a, int countA, float[] b, int countB)
    {
        if (a.Length != b.Length)
            throw new ArgumentException("Os embeddings precisam ter o mesmo tamanho.");

        // Embedding sem contagem conhecida conta como uma referência.
        var weightA = countA > 0 ? countA : 1;
        var weightB = countB > 0 ? countB : 1;
        var total = (float)(weightA + weightB);

        var result = new float[a.Length];
        for (var i = 0; i < a.Length; i++)
            result[i] = (a[i] * weightA + b[i] * weightB) / total;
        return result;
    }

    // Média das k referências mais parecidas com a consulta.
    public static float TopKMean(float[] query, IReadOnlyList<float[]> references, int k)
    {
        if (references.Count == 0)
            return 0;

        return (float)references
            .Select(reference => Cosine(query, reference))
            .OrderByDescending(score => score)
            .Take(Math.Min(k, references.Count))
            .Average();
    }

    public static float Cosine(float[] a, float[] b)
    {
        double dot = 0, aa = 0, bb = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            aa += a[i] * a[i];
            bb += b[i] * b[i];
        }

        return aa == 0 || bb == 0
            ? 0
            : (float)(dot / (Math.Sqrt(aa) * Math.Sqrt(bb)));
    }
}
