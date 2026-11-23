using Android.Content;
using Android.Content.PM;
using Android.OS;

namespace FaunaLens.Services;

// Pedido de permissões na primeira abertura: câmera, localização aproximada, notificações, armazenamento
// e acesso a todos os arquivos. Acontece uma vez; depois disso, cada tela ainda pede o que faltar.
public static class FirstRunPermissions
{
    public const int RequestCode = 4310;

    const string PreferencesName = "faunalens_permissions";
    const string DoneKey = "primeira_abertura_concluida";

    public static bool IsDone(Context context) =>
        Preferences(context).GetBoolean(DoneKey, false);

    public static void MarkDone(Context context)
    {
        var editor = Preferences(context).Edit();
        editor?.PutBoolean(DoneKey, true);
        editor?.Apply();
    }

    // Permissões de execução que ainda faltam, conforme a versão do Android.
    public static string[] MissingRuntimePermissions(Context context)
    {
        var wanted = new List<string>
        {
            "android.permission.CAMERA",
            "android.permission.ACCESS_COARSE_LOCATION"
        };

        // Notificações só existem como permissão de execução a partir do Android 13.
        if ((int)Build.VERSION.SdkInt >= 33)
            wanted.Add("android.permission.POST_NOTIFICATIONS");

        // Armazenamento antigo: a própria permissão só vale até o Android 12L (API 32) e, para escrita, até o 10 (API 29).
        if ((int)Build.VERSION.SdkInt <= 32)
            wanted.Add("android.permission.READ_EXTERNAL_STORAGE");

        if ((int)Build.VERSION.SdkInt <= 29)
            wanted.Add("android.permission.WRITE_EXTERNAL_STORAGE");

        return wanted
            .Where(permission => context.CheckSelfPermission(permission) != Permission.Granted)
            .ToArray();
    }

    static ISharedPreferences Preferences(Context context) =>
        context.GetSharedPreferences(PreferencesName, FileCreationMode.Private)!;
}
