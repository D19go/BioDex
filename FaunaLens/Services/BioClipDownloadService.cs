using Microsoft.ML.OnnxRuntime;
using System.Net;
using System.Net.Http.Headers;

namespace FaunaLens.Services;

public sealed class BioClipDownloadService
{
    static readonly HttpClient Client = CreateClient();

    // Passos desta tentativa, gravados no relatório de erro se algo falhar.
    readonly List<string> trace = new();

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
        try
        {
            await DownloadCoreAsync(url, destinationPath, progress, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            DownloadErrorReport.Write("Download do BioCLIP", ex, trace);
            throw;
        }
    }

    async Task DownloadCoreAsync(
        string url,
        string destinationPath,
        Action<long, long>? progress,
        CancellationToken cancellationToken)
    {
        Trace($"URL: {url}");
        Trace($"Destino: {destinationPath}");

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
            Trace($"Arquivo final já existe: {new FileInfo(destinationPath).Length} bytes; validando");
            try
            {
                ValidateModel(destinationPath);
                Trace("Arquivo final válido; download não é necessário");
                progress?.Invoke(new FileInfo(destinationPath).Length, new FileInfo(destinationPath).Length);
                return;
            }
            catch (Exception validation)
            {
                Trace($"Arquivo final não validou, será apagado: {validation.Message}");
                File.Delete(destinationPath);
            }
        }

        var temporaryPath = destinationPath + ".download";
        var existingLength = File.Exists(temporaryPath) ? new FileInfo(temporaryPath).Length : 0L;
        Trace($"Parcial existente: {existingLength} bytes");

        using var request = new HttpRequestMessage(HttpMethod.Get, modelUri);
        if (existingLength > 0)
            request.Headers.Range = new RangeHeaderValue(existingLength, null);
        Trace($"Pedido: GET, Range: {(existingLength > 0 ? $"bytes={existingLength}-" : "nenhum")}");

        using var response = await Client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        Trace($"Resposta: {(int)response.StatusCode} {response.ReasonPhrase}");
        Trace("Cabeçalhos: " + string.Join("; ", response.Headers
            .Concat(response.Content.Headers)
            .Select(h => $"{h.Key}={string.Join(",", h.Value)}")));

        // 416: o parcial salvo não confere com o arquivo do servidor (maior ou de outra versão).
        // Apaga o parcial e baixa tudo de novo; sem parcial, o pedido não tem Range e não cai aqui.
        if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable && existingLength > 0)
        {
            Trace($"Corpo da resposta: {await ReadBodyAsync(response)}");
            Trace("416 com parcial: apagando o parcial e recomeçando do zero");
            File.Delete(temporaryPath);
            await DownloadCoreAsync(url, destinationPath, progress, cancellationToken);
            return;
        }

        if (!response.IsSuccessStatusCode)
            Trace($"Corpo da resposta de erro: {await ReadBodyAsync(response)}");

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
        Trace($"Gravando: modo {(append ? "continuar" : "criar")}, Content-Length {contentLength}, total esperado {totalBytes}");

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
        Trace($"Leitura terminou: {downloadedBytes} bytes gravados no parcial");

        Trace("Validando o arquivo baixado com ONNX Runtime");
        ValidateModel(temporaryPath);
        Trace("Validação ok; movendo para o destino final");
        File.Move(temporaryPath, destinationPath, true);
        progress?.Invoke(downloadedBytes, totalBytes > 0 ? totalBytes : downloadedBytes);
    }

    static void ValidateModel(string path)
    {
        using var session = new InferenceSession(path);
        if (session.InputMetadata.Count == 0 || session.OutputMetadata.Count == 0)
            throw new InvalidOperationException("O arquivo baixado não contém uma sessão ONNX utilizável.");
    }

    void Trace(string line) => trace.Add($"{DateTime.Now:HH:mm:ss.fff}  {line}");

    // Corpo da resposta (ex.: mensagem de erro da API), cortado para não gerar relatório enorme.
    static async Task<string> ReadBodyAsync(HttpResponseMessage response)
    {
        try
        {
            var text = await response.Content.ReadAsStringAsync();
            return text.Length > 4000 ? text[..4000] + " ... (cortado)" : text;
        }
        catch (Exception error)
        {
            return $"(não foi possível ler: {error.Message})";
        }
    }
}
