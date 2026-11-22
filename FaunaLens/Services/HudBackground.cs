using Android.Content;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Views;

namespace FaunaLens.Services;

// Fundo visual do app (traduzido do CSS do projeto): gradiente vertical, brilhos verdes e manchas ovais.
// Vale para todas as telas e painéis, nos dois temas. O tom segue o tema efetivo do app (ThemeController).
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
        var metrics = context.Resources!.DisplayMetrics!;
        var dark = IsDark(context);
        var layers = new List<Drawable>(dark
            ? DarkLayers(metrics.WidthPixels)
            : LightLayers(metrics.WidthPixels));
        layers.Add(new BitmapDrawable(context.Resources,
            Ornaments(metrics.WidthPixels, metrics.HeightPixels, metrics.Density, context.Resources!, dark)));
        view.Background = new LayerDrawable(layers.ToArray());
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

    // Manchas ovais do CSS (.drawer-panel.dark::before): elipses com gradiente radial que se desfaz a 75% do raio.
    // Posições em % da tela; tamanhos em dp. Desenhadas na metade da resolução para economizar memória.
    static Bitmap Ornaments(int width, int height, float density, Android.Content.Res.Resources resources, bool dark)
    {
        const float scale = 0.5f;
        var bitmap = Bitmap.CreateBitmap((int)(width * scale), (int)(height * scale), Bitmap.Config.Argb8888!)!;
        var canvas = new Canvas(bitmap);
        canvas.Scale(scale, scale);

        Blob(canvas, 0.04f, 0.20f, 55, 115, 20, 100, 25, 0.25f, width, height, density);
        Blob(canvas, 0.96f, 0.38f, 65, 130, 15, 85, 25, 0.28f, width, height, density);
        Blob(canvas, 0.07f, 0.72f, 45, 100, 30, 115, 35, 0.20f, width, height, density);
        Blob(canvas, 0.94f, 0.90f, 75, 140, 20, 105, 30, 0.23f, width, height, density);
        Blob(canvas, 0.50f, 0.50f, 100, 55, 15, 75, 20, 0.10f, width, height, density);

        using var leaf = BitmapFactory.DecodeResource(resources, Resource.Drawable.leaf_bg);
        if (leaf is not null)
            DrawLeaves(canvas, leaf, width, height, density, dark);
        return bitmap;
    }

    static void Blob(Canvas canvas, float xPercent, float yPercent, float radiusXDp, float radiusYDp,
        int red, int green, int blue, float opacity, int width, int height, float density)
    {
        var alpha = (int)Math.Round(opacity * 255);
        canvas.Save();
        canvas.Translate(xPercent * width, yPercent * height);
        canvas.Scale(radiusXDp * density, radiusYDp * density);

        var paint = new Paint { AntiAlias = true };
        paint.SetShader(new RadialGradient(
            0f, 0f, 1f,
            new[] { Argb(alpha, red, green, blue), Argb(0, red, green, blue) },
            new[] { 0f, 0.75f },
            Shader.TileMode.Clamp));

        // Quadrado de -1 a 1 no espaço já escalado: o gradiente circular vira elipse.
        canvas.DrawRect(-1f, -1f, 1f, 1f, paint);
        canvas.Restore();
    }

    // Folhas pequenas (menores que os botões) espalhadas pela tela toda, em posições e ângulos fixos.
    static void DrawLeaves(Canvas canvas, Bitmap leaf, int width, int height, float density, bool dark)
    {
        var random = new Random(7);
        var cell = 110f * density;
        var leafWidth = 34f * density;
        var paint = new Paint { AntiAlias = true };
        paint.Alpha = dark ? 150 : 170;

        for (var y = 0f; y < height + cell; y += cell)
        {
            for (var x = 0f; x < width + cell; x += cell)
            {
                var px = x + (float)random.NextDouble() * cell;
                var py = y + (float)random.NextDouble() * cell;
                var angle = (float)random.NextDouble() * 360f;
                var scale = leafWidth / leaf.Width * (0.8f + (float)random.NextDouble() * 0.4f);

                canvas.Save();
                canvas.Translate(px, py);
                canvas.Rotate(angle);
                canvas.Scale(scale, scale);
                canvas.DrawBitmap(leaf, -leaf.Width / 2f, -leaf.Height / 2f, paint);
                canvas.Restore();
            }
        }
    }

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

    // Cor ARGB em inteiro, o formato que o GradientDrawable e o RadialGradient aceitam.
    static int Argb(int alpha, int red, int green, int blue) =>
        (alpha << 24) | (red << 16) | (green << 8) | blue;
}
