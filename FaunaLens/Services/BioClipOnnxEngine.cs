using Android.Graphics;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace FaunaLens.Services;

public sealed class BioClipOnnxEngine : IDisposable
{
    const int ImageSize = 224;

    readonly InferenceSession? session;

    public bool IsReady => session is not null;

    public BioClipOnnxEngine(string modelPath)
    {
        if (File.Exists(modelPath))
            session = new InferenceSession(modelPath);
    }

    public float[] EncodeImage(string imagePath)
    {
        if (session is null)
            throw new InvalidOperationException("Modelo BioCLIP offline não encontrado.");

        using var source = BitmapFactory.DecodeFile(imagePath)
            ?? throw new InvalidOperationException("Não foi possível abrir a foto.");

        var input = Preprocess(source);
        var inputName = session.InputMetadata.Keys.First();
        var tensor = new DenseTensor<float>(input, new[] { 1, 3, ImageSize, ImageSize });

        using var results = session.Run(new[]
        {
            NamedOnnxValue.CreateFromTensor(inputName, tensor)
        });

        var output = results.First().AsTensor<float>().ToArray();
        Normalize(output);
        return output;
    }

    static float[] Preprocess(Bitmap source)
    {
        var scale = Math.Max(
            (float)ImageSize / source.Width,
            (float)ImageSize / source.Height);

        var width = Math.Max(ImageSize, (int)Math.Round(source.Width * scale));
        var height = Math.Max(ImageSize, (int)Math.Round(source.Height * scale));

        using var resized = Bitmap.CreateScaledBitmap(source, width, height, true);

        var left = Math.Max(0, (width - ImageSize) / 2);
        var top = Math.Max(0, (height - ImageSize) / 2);

        using var bitmap = Bitmap.CreateBitmap(
            resized,
            left,
            top,
            ImageSize,
            ImageSize);

        var pixels = new int[ImageSize * ImageSize];
        bitmap.GetPixels(pixels, 0, ImageSize, 0, 0, ImageSize, ImageSize);

        var data = new float[3 * ImageSize * ImageSize];

        const float rMean = 0.48145466f;
        const float gMean = 0.4578275f;
        const float bMean = 0.40821073f;
        const float rStd = 0.26862954f;
        const float gStd = 0.26130258f;
        const float bStd = 0.27577711f;

        var plane = ImageSize * ImageSize;

        for (var i = 0; i < pixels.Length; i++)
        {
            var pixel = pixels[i];
            var r = ((pixel >> 16) & 255) / 255f;
            var g = ((pixel >> 8) & 255) / 255f;
            var b = (pixel & 255) / 255f;

            data[i] = (r - rMean) / rStd;
            data[plane + i] = (g - gMean) / gStd;
            data[2 * plane + i] = (b - bMean) / bStd;
        }

        return data;
    }

    static void Normalize(float[] vector)
    {
        double sum = 0;
        foreach (var value in vector)
            sum += value * value;

        var norm = Math.Sqrt(sum);
        if (norm == 0)
            return;

        for (var i = 0; i < vector.Length; i++)
            vector[i] = (float)(vector[i] / norm);
    }

    public void Dispose() => session?.Dispose();
}
