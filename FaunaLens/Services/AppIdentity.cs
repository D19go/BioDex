namespace FaunaLens.Services;

// Nomes que o app mostra para o modelo. Na variante Super aparecem como "Biodex Premium",
// para identificar a versão de teste sem depender do nome do modelo.
public static class AppIdentity
{
#if SUPER
    public const string Model = "Biodex Premium";
    public const string Online = "Biodex Premium";
#else
    public const string Model = "BioCLIP";
    public const string Online = "BioCLIP 2";
#endif
}
