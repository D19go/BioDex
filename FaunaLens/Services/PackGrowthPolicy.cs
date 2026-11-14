namespace FaunaLens.Services;

// Regras de ampliação de um pack. Sem dependências do Android, para que os testes consigam compilar este arquivo.
public static class PackGrowthPolicy
{
    // Limite de imagens por espécie em um pack.
    public const int MaxImagesPerSpecies = 50;

    // A cada N espécies novas, uma espécie que já está no pack recebe imagens novas.
    public const int NewAnimalsPerRepeat = 5;

    // Quantas imagens novas uma espécie ainda pode receber sem passar do limite.
    public static int NewImagesAllowed(int existingImages, int requested)
    {
        var room = MaxImagesPerSpecies - Math.Max(0, existingImages);
        return Math.Clamp(Math.Min(requested, room), 0, MaxImagesPerSpecies);
    }

    // Ordem de processamento: 5 espécies novas, 1 já existente, e assim por diante.
    // Quando uma das listas acaba, a outra segue sozinha.
    public static List<T> Order<T>(IReadOnlyList<T> newAnimals, IReadOnlyList<T> repeatedAnimals)
    {
        var output = new List<T>(newAnimals.Count + repeatedAnimals.Count);
        var nextNew = 0;
        var nextRepeat = 0;
        var newSinceRepeat = 0;

        while (nextNew < newAnimals.Count || nextRepeat < repeatedAnimals.Count)
        {
            var repeatTurn = nextRepeat < repeatedAnimals.Count
                && (newSinceRepeat >= NewAnimalsPerRepeat || nextNew >= newAnimals.Count);

            if (repeatTurn)
            {
                output.Add(repeatedAnimals[nextRepeat++]);
                newSinceRepeat = 0;
            }
            else
            {
                output.Add(newAnimals[nextNew++]);
                newSinceRepeat++;
            }
        }

        return output;
    }
}
