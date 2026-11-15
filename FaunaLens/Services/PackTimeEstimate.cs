namespace FaunaLens.Services;

// PROVISÓRIO: estimativa de tempo de criação de um pack do zero.
// Base: no aparelho, 43 espécies × 20 imagens levaram cerca de 5 h (≈ 20 s por imagem).
// Ampliar um pack existente costuma levar menos, porque as imagens que ele já tem não são baixadas de novo.
public static class PackTimeEstimate
{
    public const double SecondsPerImage = 20;
    public const double SecondsPerSpecies = 5;

    public static TimeSpan Estimate(int species, int imagesPerSpecies)
    {
        var seconds = Math.Max(0, species) * (Math.Max(0, imagesPerSpecies) * SecondsPerImage + SecondsPerSpecies);
        return TimeSpan.FromSeconds(seconds);
    }

    public static string Format(TimeSpan time)
    {
        if (time.TotalHours >= 1)
            return $"{(int)time.TotalHours} h {time.Minutes:00} min";

        return $"{Math.Max(1, (int)Math.Ceiling(time.TotalMinutes))} min";
    }
}
