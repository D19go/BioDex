using Android.App;
using Android.Content;
using Android.OS;
using Android.Views;
using Android.Widget;
using FaunaLens.Services;

namespace FaunaLens;

// Abre uma imagem de referência do pack em tamanho maior.
[Activity(Label = "Imagem de referência", Exported = false)]
public sealed class ImageViewerActivity : Activity
{
    public const string ExtraPath = "image_path";

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        var image = new ImageView(this);
        image.SetBackgroundColor(Android.Graphics.Color.Black);
        image.SetScaleType(ImageView.ScaleType.FitCenter);
        SetContentView(image);

        var path = Intent?.GetStringExtra(ExtraPath);
        if (string.IsNullOrWhiteSpace(path))
        {
            Finish();
            return;
        }

        _ = LoadAsync(path, image);
    }

    async Task LoadAsync(string path, ImageView view)
    {
        var bitmap = await Task.Run(() =>
        {
            try
            {
                return ImageLoader.Load(path, 1200);
            }
            catch
            {
                return null;
            }
        });

        if (bitmap is null)
        {
            Toast.MakeText(this, "Não foi possível abrir a imagem.", ToastLength.Short)?.Show();
            Finish();
            return;
        }

        view.SetImageBitmap(bitmap);
    }

    public static Intent CreateIntent(Context context, string path) =>
        new Intent(context, typeof(ImageViewerActivity)).PutExtra(ExtraPath, path);
}
