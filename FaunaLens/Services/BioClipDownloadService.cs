using Microsoft.ML.OnnxRuntime;
using System.Net;
using System.Net.Http.Headers;

namespace FaunaLens.Services;

public sealed class BioClipDownloadService
{
    static readonly HttpClient Client = CreateClient();

    static HttpClient CreateClient()
    {
        return new HttpClient
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
    }

    public async Task DownloadAsync(
        string url,
        string destinationPath,
        Action<long, long>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(url))
            throw new InvalidOperationException("A URL do modelo BioCLIP não está configurada.");

        if (!Uri.TryCreate(url, UriKind.Absolute, out var modelUri) ||
            modelUri.Scheme is not ("https" or "http"))
        {
            throw new InvalidOperationException("A URL do modelo BioCLIP é inválida.");
        }

        var directory = Path.GetDirectoryName(destinationPath);
        if (string.IsNullOrWhiteSpace(directory))
            throw new InvalidOperationException("O diretório do modelo é inválido.");

        Directory.CreateDirectory(directory);

        if (File.Exists(destinationPath))
        {
            try
            {
                ValidateModel(destinationPath);
                progress?.Invoke(new FileInfo(destinationPath).Length, new FileInfo(destinationPath).Length);
                return;
            }
            catch
            {
                File.Delete(destinationPath);
            }
        }

        var temporaryPath = destinationPath + ".download";
        var existingLength = File.Exists(temporaryPath) ? new FileInfo(temporaryPath).Length : 0L;

        using var request = new HttpRequestMessage(HttpMethod.Get, modelUri);
        if (existingLength > 0)
            request.Headers.Range = new RangeHeaderValue(existingLength, null);

        using var response = await Client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        var append = existingLength > 0 && response.StatusCode == HttpStatusCode.PartialContent;
        if (!append && existingLength > 0)
        {
            existingLength = 0;
            File.Delete(temporaryPath);
        }

        var contentLength = response.Content.Headers.ContentLength ?? -1L;
        var totalBytes = contentLength < 0 ? -1L : existingLength + contentLength;
        var downloadedBytes = existingLength;

        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = new FileStream(
            temporaryPath,
            append ? FileMode.Append : FileMode.Create,
            FileAccess.Write,
            FileShare.Read,
            1024 * 128,
            useAsync: true);

        progress?.Invoke(downloadedBytes, totalBytes);
        var buffer = new byte[1024 * 128];

        while (true)
        {
            var read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
            if (read <= 0)
                break;

            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            downloadedBytes += read;
            progress?.Invoke(downloadedBytes, totalBytes);
        }

        await output.FlushAsync(cancellationToken);
        output.Close();

        ValidateModel(temporaryPath);
        File.Move(temporaryPath, destinationPath, true);
        progress?.Invoke(downloadedBytes, totalBytes > 0 ? totalBytes : downloadedBytes);
    }

    static void ValidateModel(string path)
    {
        using var session = new InferenceSession(path);
        if (session.InputMetadata.Count == 0 || session.OutputMetadata.Count == 0)
            throw new InvalidOperationException("O arquivo baixado não contém uma sessão ONNX utilizável.");
    }
}
