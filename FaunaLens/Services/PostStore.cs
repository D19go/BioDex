using System.Text.Json;
using Android.Content;

namespace FaunaLens.Services;

public sealed class SavedPost
{
    public string Id { get; set; } = "";
    public string Author { get; set; } = "";
    public string Caption { get; set; } = "";
    public List<string> ImagePaths { get; set; } = new();
    public long CreatedAtUtcTicks { get; set; }
}

// Publicações criadas pelo usuário. Ficam em files/posts (as fotos são copiadas para o próprio app),
// então continuam disponíveis mesmo que a galeria mude ou apague o arquivo original.
public static class PostStore
{
    static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    static string Root(Context context) => Path.Combine(context.FilesDir!.AbsolutePath, "posts");

    static string IndexPath(Context context) => Path.Combine(Root(context), "posts.json");

    public static string FolderFor(Context context, string postId) => Path.Combine(Root(context), postId);

    // Mais recentes primeiro. Arquivo ausente ou corrompido vira lista vazia.
    public static List<SavedPost> Load(Context context)
    {
        try
        {
            var path = IndexPath(context);
            if (!File.Exists(path))
                return new();

            return JsonSerializer.Deserialize<List<SavedPost>>(File.ReadAllText(path)) ?? new();
        }
        catch
        {
            return new();
        }
    }

    public static void Add(Context context, SavedPost post)
    {
        var posts = Load(context);
        posts.Insert(0, post);

        Directory.CreateDirectory(Root(context));
        var temp = IndexPath(context) + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(posts, Options));
        File.Move(temp, IndexPath(context), true);
    }
}
