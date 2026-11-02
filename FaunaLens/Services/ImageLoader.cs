using Android.Graphics;

namespace FaunaLens.Services;

public static class ImageLoader
{
    /// <summary>
    /// Abre a imagem reduzida (lado maior entre maxSide e 2×maxSide) e já corrigida
    /// pela orientação EXIF, para não estourar a memória com fotos grandes da câmera.
    /// </summary>
    public static Bitmap Load(string path, int maxSide)
    {
        var bounds = new BitmapFactory.Options { InJustDecodeBounds = true };
        BitmapFactory.DecodeFile(path, bounds);
        if (bounds.OutWidth <= 0 || bounds.OutHeight <= 0)
            throw new InvalidOperationException("Não foi possível abrir a imagem selecionada.");

        var sample = 1;
        var largest = Math.Max(bounds.OutWidth, bounds.OutHeight);
        while (largest / (sample * 2) >= maxSide)
            sample *= 2;

        var options = new BitmapFactory.Options { InSampleSize = sample };
        var bitmap = BitmapFactory.DecodeFile(path, options)
            ?? throw new InvalidOperationException("Não foi possível abrir a imagem selecionada.");

        var rotation = ReadRotation(path);
        if (rotation == 0)
            return bitmap;

        using var matrix = new Matrix();
        matrix.PostRotate(rotation);
        var rotated = Bitmap.CreateBitmap(bitmap, 0, 0, bitmap.Width, bitmap.Height, matrix, true);
        if (!ReferenceEquals(rotated, bitmap))
            bitmap.Dispose();

        return rotated;
    }

    static int ReadRotation(string path)
    {
        try
        {
            using var exif = new Android.Media.ExifInterface(path);
            return exif.GetAttributeInt(Android.Media.ExifInterface.TagOrientation, 1) switch
            {
                3 => 180,
                6 => 90,
                8 => 270,
                _ => 0
            };
        }
        catch
        {
            return 0;
        }
    }
}
