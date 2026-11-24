namespace FaunaLens.Services;

// Regras de ampliação de um pack. Sem dependências do Android, para que os testes consigam compilar este arquivo.
public static class PackGrowthPolicy
{
    // Limite de imagens por espécie em um pack.
    public const int MaxImagesPerSpecies = 50;

    // A cada N espécies novas, uma espécie que já está no pack recebe imagens novas.
    public const int NewAnimalsPerRepeat = 5;

    // Intervalo entre salvamentos parciais da criação. Cada salvamento regrava o pack inteiro,
    // então não pode ser curto demais; e precisa ser curto o bastante para não perder muito trabalho.
    public static readonly TimeSpan CheckpointInterval = TimeSpan.FromMinutes(3);

    // Indica se já passou tempo suficiente desde o último salvamento parcial.
    public static bool ShouldCheckpoint(DateTime lastSavedUtc, DateTime nowUtc, TimeSpan interval) =>
        nowUtc - lastSavedUtc >= interval;

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
