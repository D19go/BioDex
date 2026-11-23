using System.Text;
using System.Text.Json;
using Android.OS;

namespace FaunaLens.Services;

// Relatório em TXT para cada falha de download. Fica em Download/BioDex/Logs, para abrir pelo próprio celular
// (sem computador). Se a pasta pública não estiver disponível, usa a pasta privada do app.
// Nunca lança exceção: se nem o arquivo puder ser gravado, devolve null.
public static class DownloadErrorReport
{
    // Chave em Exception.Data com o caminho do relatório, para não gerar dois relatórios do mesmo erro.
    public const string DataKey = "BioDexReport";

    public static string? Write(string title, Exception ex, IReadOnlyList<string> trace)
    {
        try
        {
            var text = Compose(title, ex, trace);
            var path = Save(text);
            ex.Data[DataKey] = path;
            return path;
        }
        catch
        {
            return null;
        }
    }

    static string Compose(string title, Exception ex, IReadOnlyList<string> trace)
    {
        var text = new StringBuilder();
        text.AppendLine("BioDex - relatório de erro de download");
        text.AppendLine($"Gerado (UTC): {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}");
        text.AppendLine($"Gerado (local): {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        text.AppendLine($"Operação: {title}");
        text.AppendLine($"Aparelho: {Build.Manufacturer} {Build.Model}");
        text.AppendLine($"Android: {Build.VERSION.Release} (API {(int)Build.VERSION.SdkInt})");
        text.AppendLine();

        text.AppendLine("== ERRO ==");
        text.AppendLine($"Tipo: {ex.GetType().FullName}");
        text.AppendLine($"Mensagem: {ex.Message}");
        text.AppendLine();

        text.AppendLine("== DETALHES TÉCNICOS (inclui causas internas) ==");
        text.AppendLine(ex.ToString());
        text.AppendLine();

        text.AppendLine("== REGISTRO DO DOWNLOAD (passo a passo) ==");
        if (trace.Count == 0)
            text.AppendLine("(nenhum passo registrado antes da falha)");
        foreach (var line in trace)
            text.AppendLine(line);
        text.AppendLine();

        text.AppendLine("== ARQUIVOS NO ARMAZENAMENTO ==");
        text.AppendLine(FileLine("Modelo final", FileSystem.ModelPath));
        text.AppendLine(FileLine("Parcial (.download)", FileSystem.ModelTempPath));
        text.AppendLine();

        text.AppendLine("== ESTADO SALVO DO DOWNLOAD ==");
        try
        {
            text.AppendLine(JsonSerializer.Serialize(BioClipDownloadState.Load(), new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception stateError)
        {
            text.AppendLine($"(não foi possível ler: {stateError.Message})");
        }

        return text.ToString();
    }

    static string FileLine(string label, string path)
    {
        if (!File.Exists(path))
            return $"{label}: não existe ({path})";

        return $"{label}: {new FileInfo(path).Length} bytes ({path})";
    }

    static string Save(string text)
    {
        var name = $"erro-download-{DateTime.Now:yyyyMMdd-HHmmss}.txt";
        try
        {
            var folder = Path.Combine(ModelLocation.RootDirectory(ModelStorage.PublicDownloadsDirectory), "Logs");
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, name);
            File.WriteAllText(path, text);
            return path;
        }
        catch
        {
            var folder = Path.Combine(FileSystem.Root, "logs");
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, name);
            File.WriteAllText(path, text);
            return path;
        }
    }
}
