namespace FaunaPackBuilder;

public static class ModelDownloader
{
    public const string BioClipOnnxUrl =
        "https://huggingface.co/m3r1al/naturalist-bioclip-v1/resolve/main/image_encoder.onnx?download=true";

    static readonly HttpClient Client = new() { Timeout = TimeSpan.FromMinutes(20) };

    public static async Task DownloadAsync(
        string destination,
        IProgress<int>? progress,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(destination))
            throw new ArgumentException("O caminho do modelo não foi informado.", nameof(destination));

        var fullDestination = Path.GetFullPath(destination);
        var directory = Path.GetDirectoryName(fullDestination)
            ?? throw new InvalidOperationException("Não foi possível determinar a pasta do modelo.");

        Directory.CreateDirectory(directory);

        // Evita baixar novamente um modelo que já existe.
        // Isso também evita tentar substituir um arquivo que esteja sendo usado
        // pelo próprio ONNX Runtime ou por outro processo.
        if (File.Exists(fullDestination))
        {
            progress?.Report(100);
            return;
        }

        // O arquivo temporário é único por execução. Assim, duas instâncias do
        // Builder não disputam o mesmo .download.
        var temp = Path.Combine(
            directory,
            $".{Path.GetFileName(fullDestination)}.{Guid.NewGuid():N}.download");

        try
        {
            using (var response = await Client.GetAsync(
                       BioClipOnnxUrl,
                       HttpCompletionOption.ResponseHeadersRead,
                       ct))
            {
                response.EnsureSuccessStatusCode();
                var total = response.Content.Headers.ContentLength;
                long done = 0;

                await using (var input = await response.Content.ReadAsStreamAsync(ct))
                await using (var output = new FileStream(
                                 temp,
                                 FileMode.CreateNew,
                                 FileAccess.Write,
                                 FileShare.Read,
                                 128 * 1024,
                                 useAsync: true))
                {
                    var buffer = new byte[128 * 1024];
                    int read;

                    while ((read = await input.ReadAsync(buffer, ct)) > 0)
                    {
                        await output.WriteAsync(buffer.AsMemory(0, read), ct);
                        done += read;

                        if (total is > 0)
                        {
                            progress?.Report((int)Math.Clamp(
                                done * 100L / total.Value,
                                0,
                                100));
                        }
                    }

                    await output.FlushAsync(ct);
                }
            }

            // Um antivírus/indexador pode manter o arquivo recém-criado aberto
            // por alguns instantes. O Move agora possui algumas tentativas antes
            // de falhar definitivamente.
            MoveWithRetry(temp, fullDestination, ct);
            progress?.Report(100);
        }
        catch
        {
            TryDelete(temp);
            throw;
        }
    }

    static void MoveWithRetry(string source, string destination, CancellationToken ct)
    {
        const int attempts = 12;
        const int delayMilliseconds = 500;

        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                // Se outra instância terminou o download enquanto esta ainda
                // estava trabalhando, o arquivo existente pode ser reutilizado.
                if (File.Exists(destination))
                {
                    TryDelete(source);
                    return;
                }

                File.Move(source, destination);
                return;
            }
            catch (IOException) when (attempt < attempts)
            {
                Thread.Sleep(delayMilliseconds);
            }
        }

        // Última tentativa sem filtro para preservar a exceção original do SO.
        File.Move(source, destination);
    }

    static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // O arquivo temporário será removido posteriormente pelo sistema
            // caso algum processo externo ainda esteja terminando de liberá-lo.
        }
    }
}
