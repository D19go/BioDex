using Android.Content;

namespace FaunaLens.Services;

// Controle da primeira abertura. Nenhuma permissão é pedida em lote: cada uma é pedida quando a função precisa:
// câmera ao tirar foto, localização ao escolher o pack da região, notificações ao criar um pack.
public static class FirstRunPermissions
{
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

    static ISharedPreferences Preferences(Context context) =>
        context.GetSharedPreferences(PreferencesName, FileCreationMode.Private)!;
}
