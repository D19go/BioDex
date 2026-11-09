using Android.App;
using Android.Content.Res;

namespace FaunaLens.Services;

public static class ThemeController
{
    // Escolhe o tema antes de a tela ser montada: por isso deve ser chamado antes de base.OnCreate.
    public static void Apply(Activity activity)
    {
        var night = (activity.Resources?.Configuration?.UiMode & UiMode.NightMask) == UiMode.NightYes;
        var dark = ThemeModes.UseDark(AppSettings.GetThemeMode(), night);
        activity.SetTheme(dark ? Resource.Style.AppThemeDark : Resource.Style.AppThemeLight);
    }
}
