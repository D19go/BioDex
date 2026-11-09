namespace FaunaLens.Services;

// Escolha de tema do usuário. Puro (sem Android) para poder ser testado.
public static class ThemeModes
{
    public const string FollowSystem = "system";
    public const string Light = "light";
    public const string Dark = "dark";

    public static readonly string[] Values = { FollowSystem, Light, Dark };
    public static readonly string[] Labels = { "Sistema", "Claro", "Escuro" };

    // Valor desconhecido ou ausente volta para "seguir o sistema".
    public static string Normalize(string? mode) => mode is Light or Dark ? mode : FollowSystem;

    public static bool UseDark(string? mode, bool systemIsNight) => Normalize(mode) switch
    {
        Dark => true,
        Light => false,
        _ => systemIsNight
    };

    public static int IndexOf(string? mode) => Array.IndexOf(Values, Normalize(mode));
}
