using Android.Content;

namespace FaunaLens.Services;

// Preferências do usuário (tema, criação automática por região) e o estado da detecção de região.
public static class AppSettings
{
    const string PreferencesName = "faunalens_settings";
    const string ThemeKey = "theme_mode";
    const string AutoRegionKey = "auto_region_packs";
    const string LastRegionKey = "region_last";
    const string PendingRegionKey = "region_pending";
    const string LastCheckKey = "region_last_check_ticks";

    static ISharedPreferences? Preferences() =>
        Android.App.Application.Context.GetSharedPreferences(PreferencesName, FileCreationMode.Private);

    public static string GetThemeMode() =>
        ThemeModes.Normalize(Preferences()?.GetString(ThemeKey, null));

    public static void SetThemeMode(string mode) =>
        Preferences()?.Edit()?.PutString(ThemeKey, ThemeModes.Normalize(mode))?.Apply();

    public static bool IsAutoRegionEnabled() =>
        Preferences()?.GetBoolean(AutoRegionKey, false) ?? false;

    public static void SetAutoRegionEnabled(bool enabled) =>
        Preferences()?.Edit()?.PutBoolean(AutoRegionKey, enabled)?.Apply();

    public static RegionState GetRegionState()
    {
        var preferences = Preferences();
        return new RegionState(
            preferences?.GetString(LastRegionKey, null) ?? string.Empty,
            preferences?.GetString(PendingRegionKey, null) ?? string.Empty);
    }

    public static void SetRegionState(RegionState state) =>
        Preferences()?.Edit()?
            .PutString(LastRegionKey, state.LastRegion)?
            .PutString(PendingRegionKey, state.PendingRegion)?
            .Apply();

    public static DateTime GetLastRegionCheckUtc()
    {
        var ticks = Preferences()?.GetLong(LastCheckKey, 0L) ?? 0L;
        return ticks == 0 ? DateTime.MinValue : new DateTime(ticks, DateTimeKind.Utc);
    }

    public static void SetLastRegionCheckUtc(DateTime utc) =>
        Preferences()?.Edit()?.PutLong(LastCheckKey, utc.Ticks)?.Apply();
}
