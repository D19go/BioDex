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
        CancellationToken cancellationToken,
        bool allowRestart = true)
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
            catch (Exception validation) when (!IsRuntimeFailure(validation))
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

        // 416 com parcial: o servidor não tem bytes além do que já está salvo, então o parcial deve estar inteiro.
        // Valida o parcial antes de qualquer coisa; só se o arquivo estiver ruim, apaga e baixa de novo, uma única vez.
        if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable && existingLength > 0)
        {
            Trace($"Corpo da resposta: {await ReadBodyAsync(response)}");
            Trace("416 com parcial: validando o parcial antes de baixar de novo");
            try
            {
                ValidateModel(temporaryPath);
            }
            catch (Exception validation) when (!IsRuntimeFailure(validation))
            {
                if (!allowRestart)
                    throw;

                Trace($"Parcial não validou ({validation.Message}); apagando e baixando do zero, uma vez");
                File.Delete(temporaryPath);
                await DownloadCoreAsync(url, destinationPath, progress, cancellationToken, allowRestart: false);
                return;
            }

            Trace("Parcial válido; movendo para o destino final");
            File.Move(temporaryPath, destinationPath, true);
            progress?.Invoke(existingLength, existingLength);
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

    // Falha da própria biblioteca ONNX Runtime (ex.: libonnxruntime.so ausente), e não do arquivo baixado.
    // Nesses casos o arquivo é mantido: apagar não resolve e obrigaria a baixar tudo de novo.
    static bool IsRuntimeFailure(Exception error) =>
        error is DllNotFoundException or EntryPointNotFoundException or TypeInitializationException
        || error.InnerException is DllNotFoundException;

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
