using Android.App;
using Android.Content.PM;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.OS;
using Android.Views;
using Android.Widget;
using FaunaLens.Services;

namespace FaunaLens;

// Tela cheia de uma publicação: fundo preto, voltar e menu no topo, fotos em largura total (deslize para ver as outras).
[Activity(Label = "Publicação", Exported = false, ScreenOrientation = ScreenOrientation.Portrait)]
public sealed class PostViewerActivity : Activity
{
    public const string ExtraPaths = "paths";

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        var paths = Intent?.GetStringArrayListExtra(ExtraPaths)?.ToList() ?? new List<string>();
        var width = Resources!.DisplayMetrics!.WidthPixels;

        var root = new FrameLayout(this);
        root.SetBackgroundColor(Color.Black);

        var scroll = new HorizontalScrollView(this) { HorizontalScrollBarEnabled = false };
        var row = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        row.SetGravity(GravityFlags.Center);
        foreach (var path in paths)
        {
            var image = new ImageView(this);
            image.SetScaleType(ImageView.ScaleType.FitCenter);
            try
            {
                image.SetImageBitmap(ImageLoader.Load(path, 1600));
            }
            catch
            {
                image.SetBackgroundColor(Color.Rgb(40, 40, 40));
            }
            row.AddView(image, new LinearLayout.LayoutParams(width, -1));
        }
        scroll.AddView(row, new FrameLayout.LayoutParams(-2, -1));
        root.AddView(scroll, new FrameLayout.LayoutParams(-1, -1));

        // Barra superior, no mesmo padrão da referência: voltar à esquerda e menu à direita.
        var top = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        top.SetGravity(GravityFlags.CenterVertical);
        top.SetPadding(Dp(16), Dp(12), Dp(16), Dp(12));
        top.SetOnApplyWindowInsetsListener(new SystemBarInsetsListener(16, 12));

        var back = CircleLabel("←");
        back.Click += (_, _) => Finish();
        top.AddView(back, new LinearLayout.LayoutParams(Dp(44), Dp(44)));
        top.AddView(new View(this), new LinearLayout.LayoutParams(0, 0, 1f));
        top.AddView(CircleLabel("⋮"), new LinearLayout.LayoutParams(Dp(44), Dp(44)));
        root.AddView(top, new FrameLayout.LayoutParams(-1, -2, GravityFlags.Top));

        if (paths.Count > 1)
        {
            var hint = new TextView(this) { Text = $"{paths.Count} fotos · deslize para ver", TextSize = 13 };
            hint.SetTextColor(Color.White);
            hint.Gravity = GravityFlags.Center;
            hint.SetPadding(0, 0, 0, Dp(24));
            root.AddView(hint, new FrameLayout.LayoutParams(-1, -2, GravityFlags.Bottom));
        }

        SetContentView(root);
    }

    TextView CircleLabel(string text)
    {
        var label = new TextView(this) { Text = text, TextSize = 18 };
        label.Gravity = GravityFlags.Center;
        label.SetTextColor(Color.White);
        var shape = new GradientDrawable();
        shape.SetColor(Color.Rgb(40, 40, 40));
        shape.SetCornerRadius(Dp(999));
        label.Background = shape;
        return label;
    }

    int Dp(int value) => (int)(value * Resources!.DisplayMetrics!.Density);
}
