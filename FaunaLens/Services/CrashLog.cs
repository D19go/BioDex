using System.Text;
using System.Threading.Tasks;
using Android.OS;
using Android.Runtime;

namespace FaunaLens.Services;

// Registra em TXT as exceções que o app não trata (inclusive as de tarefas em segundo plano). Sem isso,
// uma queda dessas some sem deixar nenhum vestígio. O arquivo fica em Download/BioDex/Logs, como os relatórios de download.
// Nunca lança exceção: se o arquivo não puder ser gravado, a falha original segue seu curso.
public static class CrashLog
{
    static bool installed;
    static readonly object gate = new();

    public static void Install()
    {
        lock (gate)
        {
            if (installed)
                return;
            installed = true;
        }

        AndroidEnvironment.UnhandledExceptionRaiser += (_, e) => Write("Exceção não tratada", e.Exception);
        TaskScheduler.UnobservedTaskException += (_, e) => Write("Tarefa sem tratamento", e.Exception);
    }

    public static string? Write(string title, Exception ex)
    {
        try
        {
            var text = Compose(title, ex);
            return Save(text);
        }
        catch
        {
            return null;
        }
    }

    static string Compose(string title, Exception ex)
    {
        var text = new StringBuilder();
        text.AppendLine("BioDex - relatório de erro do app");
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
        text.AppendLine("== DETALHES TÉCNICOS ==");
        text.AppendLine(ex.ToString());
        text.AppendLine();
        text.AppendLine("== ESTADO DA CRIAÇÃO DE PACK ==");
        try
        {
            var state = PackBuildStateStore.Load(FileSystem.PackBuildStatePath);
            text.AppendLine($"Status: {state.Status} · {state.Percent}% · pack \"{state.PackName}\"");
            text.AppendLine("Últimas linhas do registro:");
            foreach (var line in state.Log.TakeLast(40))
                text.AppendLine(line);
        }
        catch (Exception stateError)
        {
            text.AppendLine($"(não foi possível ler: {stateError.Message})");
        }

        return text.ToString();
    }

    static string Save(string text)
    {
        var name = $"erro-app-{DateTime.Now:yyyyMMdd-HHmmss}.txt";
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
