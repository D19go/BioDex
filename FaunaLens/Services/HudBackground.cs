using Android.Content;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Views;

namespace FaunaLens.Services;

// Fundo visual do app (traduzido do CSS do projeto): gradiente vertical com brilhos verdes suaves.
// Vale para todas as telas e painéis. O tom segue o tema efetivo do app (ThemeController).
public static class HudBackground
{
    // Mesma decisão de tema que ThemeController.Apply, para cores e fundo ficarem coerentes com a escolha do usuário.
    public static bool IsDark(Context context)
    {
        var night = (context.Resources?.Configuration?.UiMode & Android.Content.Res.UiMode.NightMask)
            == Android.Content.Res.UiMode.NightYes;
        return ThemeModes.UseDark(AppSettings.GetThemeMode(), night);
    }

    public static void Apply(View view, Context context)
    {
        var width = context.Resources!.DisplayMetrics!.WidthPixels;
        var layers = IsDark(context) ? DarkLayers(width) : LightLayers(width);
        view.Background = new LayerDrawable(layers);
    }

    // Escuro: #001006 → #001A08 → #001006, com brilhos verdes.
    static Drawable[] DarkLayers(int width) => new Drawable[]
    {
        Base(Argb(255, 0, 16, 6), Argb(255, 0, 26, 8), Argb(255, 0, 16, 6)),
        Glow(width, 0.5f, 0.2f, 0.6f, 60, 35, 130, 35),
        Glow(width, 0.0f, 0.5f, 0.5f, 40, 57, 185, 0),
        Glow(width, 1.0f, 0.8f, 0.5f, 40, 32, 230, 0),
    };

    // Claro: mesmo desenho com base esverdeada muito clara e brilhos mais suaves.
    static Drawable[] LightLayers(int width) => new Drawable[]
    {
        Base(Argb(255, 246, 251, 244), Argb(255, 233, 245, 227), Argb(255, 246, 251, 244)),
        Glow(width, 0.5f, 0.2f, 0.6f, 50, 110, 190, 80),
        Glow(width, 0.0f, 0.5f, 0.5f, 35, 80, 200, 20),
        Glow(width, 1.0f, 0.8f, 0.5f, 35, 40, 200, 40),
    };

    static GradientDrawable Base(int top, int middle, int bottom) =>
        new(GradientDrawable.Orientation.TopBottom, new[] { top, middle, bottom });

    static GradientDrawable Glow(int width, float centerX, float centerY, float relativeRadius,
        int alpha, int red, int green, int blue)
    {
        var glow = new GradientDrawable();
        glow.SetGradientType(Android.Graphics.Drawables.GradientType.RadialGradient);
        glow.SetColors(new[] { Argb(alpha, red, green, blue), Argb(0, red, green, blue) });
        glow.SetGradientCenter(centerX, centerY);
        // O raio do gradiente é em pixels: a referência é a largura da tela.
        glow.SetGradientRadius(width * relativeRadius);
        return glow;
    }

    // Cor ARGB em inteiro, o formato que o GradientDrawable aceita.
    static int Argb(int alpha, int red, int green, int blue) =>
        (alpha << 24) | (red << 16) | (green << 8) | blue;
}
