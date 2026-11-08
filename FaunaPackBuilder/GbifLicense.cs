namespace FaunaPackBuilder;

// Licenças aceitas para as imagens de referência. Compartilhado entre o Builder e o app.
public static class GbifLicense
{
    public static bool IsAllowed(string? license, bool allowBySa)
    {
        if (string.IsNullOrWhiteSpace(license)) return false;
        var value = license.Trim().ToLowerInvariant();

        if (value.Contains("publicdomain/zero") || value.Contains("cc0")) return true;
        if (value.Contains("creativecommons.org/licenses/by/")) return true;
        if (allowBySa && value.Contains("creativecommons.org/licenses/by-sa/")) return true;
        return false;
    }
}
