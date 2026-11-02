using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace FaunaPackBuilder;

public sealed class BioClipEngine : IDisposable
{
    const int Size = 224;
    readonly InferenceSession session;
    readonly string inputName;

    public BioClipEngine(string modelPath)
    {
        if (!File.Exists(modelPath))
            throw new FileNotFoundException("Modelo BioCLIP ONNX não encontrado.", modelPath);

        var options = new SessionOptions
        {
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_BASIC,
            ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
            IntraOpNumThreads = 1,
            InterOpNumThreads = 1
        };

        session = new InferenceSession(modelPath, options);
        inputName = session.InputMetadata.Keys.First();
    }

    public float[] Encode(byte[] imageBytes)
    {
        using var input = new MemoryStream(imageBytes);
        using var source = new Bitmap(input);
        return Encode(source);
    }

    public float[] Encode(string imagePath)
    {
        using var source = new Bitmap(imagePath);
        return Encode(source);
    }

    float[] Encode(Bitmap source)
    {
        var values = Preprocess(source);
        var tensor = new DenseTensor<float>(values, new[] { 1, 3, Size, Size });
        using var results = session.Run(new[] { NamedOnnxValue.CreateFromTensor(inputName, tensor) });
        var output = results.First().AsTensor<float>().ToArray();
        Normalize(output);
        return output;
    }

    public static byte[] MakeThumbnail(byte[] imageBytes, int maxSize = 512, long quality = 82)
    {
        using var input = new MemoryStream(imageBytes);
        using var source = new Bitmap(input);
        var scale = Math.Min(1d, Math.Min((double)maxSize / source.Width, (double)maxSize / source.Height));
        var width = Math.Max(1, (int)Math.Round(source.Width * scale));
        var height = Math.Max(1, (int)Math.Round(source.Height * scale));

        using var bitmap = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.CompositingQuality = CompositingQuality.HighQuality;
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.SmoothingMode = SmoothingMode.HighQuality;
            graphics.DrawImage(source, 0, 0, width, height);
        }

        using var output = new MemoryStream();
        var codec = ImageCodecInfo.GetImageEncoders().First(x => x.FormatID == ImageFormat.Jpeg.Guid);
        using var parameters = new EncoderParameters(1);
        parameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, quality);
        bitmap.Save(output, codec, parameters);
        return output.ToArray();
    }

    static float[] Preprocess(Bitmap source)
    {
        var scale = Math.Max((float)Size / source.Width, (float)Size / source.Height);
        var width = Math.Max(Size, (int)Math.Round(source.Width * scale));
        var height = Math.Max(Size, (int)Math.Round(source.Height * scale));
        using var resized = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        using (var graphics = Graphics.FromImage(resized))
        {
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.DrawImage(source, 0, 0, width, height);
        }

        var left = Math.Max(0, (width - Size) / 2);
        var top = Math.Max(0, (height - Size) / 2);
        using var bitmap = resized.Clone(new Rectangle(left, top, Size, Size), PixelFormat.Format24bppRgb);

        var data = new float[3 * Size * Size];
        var pixels = new int[Size * Size];
        for (var y = 0; y < Size; y++)
            for (var x = 0; x < Size; x++)
            {
                var c = bitmap.GetPixel(x, y);
                var i = y * Size + x;
                data[i] = (c.R / 255f - 0.48145466f) / 0.26862954f;
                data[Size * Size + i] = (c.G / 255f - 0.4578275f) / 0.26130258f;
                data[2 * Size * Size + i] = (c.B / 255f - 0.40821073f) / 0.27577711f;
            }
        return data;
    }

    static void Normalize(float[] vector)
    {
        double sum = 0;
        foreach (var value in vector) sum += value * value;
        var norm = Math.Sqrt(sum);
        if (norm == 0) return;
        for (var i = 0; i < vector.Length; i++) vector[i] = (float)(vector[i] / norm);
    }

    public void Dispose() => session.Dispose();
}
