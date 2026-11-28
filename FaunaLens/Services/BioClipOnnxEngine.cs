using AI.Onnxruntime;
using Android.Graphics;
using Java.Nio;

namespace FaunaLens.Services;

// Modelo BioCLIP no celular, pelo ONNX Runtime oficial para Android (AAR em FaunaLens.OnnxAndroid).
public sealed class BioClipOnnxEngine : IDisposable
{
    const int ImageSize = 224;

    readonly OrtEnvironment? environment;
    readonly OrtSession? session;
    readonly string inputName = "";
    readonly string outputName = "";

    public bool IsReady => session is not null;

    public BioClipOnnxEngine(string modelPath)
    {
        if (!File.Exists(modelPath))
            return;

        environment = OrtEnvironment.GetEnvironment(OrtEnvironment.DefaultName);
        // Sessão com as opções padrão do ONNX Runtime (CPU). Uma tentativa anterior com XNNPACK fazia o
        // grafo do BioCLIP falhar em todas as imagens ("cannot be reshaped", Reshape_10), então foi removida.
        session = environment.CreateSession(modelPath);
        inputName = session.InputNames.First();
        outputName = session.OutputNames.First();
    }

    public float[] EncodeImage(string imagePath)
    {
        if (session is null || environment is null)
            throw new InvalidOperationException("Modelo BioCLIP offline não encontrado.");

        using var source = ImageLoader.Load(imagePath, ImageSize * 4);

        var input = Preprocess(source);
        var floats = FloatBuffer.Wrap(input);
        var tensor = OnnxTensor.CreateTensor(environment, floats, new long[] { 1, 3, ImageSize, ImageSize });
        try
        {
            var inputs = new Dictionary<string, OnnxTensorLike> { [inputName] = tensor };

            var results = session.Run(inputs);
            try
            {
                var output = ((OnnxTensor)results.Get(outputName).Get()).FloatBuffer;
                var vector = new float[output.Remaining()];
                output.Get(vector);
                Normalize(vector);
                return vector;
            }
            finally
            {
                results.Close();
            }
        }
        finally
        {
            tensor.Close();
        }
    }

    // Confere se o arquivo abre como modelo ONNX utilizável. Não mantém a sessão aberta.
    public static void Validate(string path)
    {
        var environment = OrtEnvironment.GetEnvironment(OrtEnvironment.DefaultName);
        var session = environment.CreateSession(path);
        try
        {
            if (session.InputNames.Count == 0 || session.OutputNames.Count == 0)
                throw new InvalidOperationException("O arquivo baixado não contém uma sessão ONNX utilizável.");
        }
        finally
        {
            session.Close();
        }
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

    public void Dispose() => session?.Close();
}
