using Android.App;
using Android.Content.PM;
using Android.Content;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.OS;
using Android.Text;
using Android.Views;
using Android.Widget;
using FaunaLens.Services;
using Path = System.IO.Path;
using Uri = Android.Net.Uri;

namespace FaunaLens;

// Criação de publicação: legenda, até 5 fotos em carrossel, botões de fotos e de marcar pessoas, e publicar ou cancelar.
[Activity(Label = "Nova publicação", Exported = false, ScreenOrientation = ScreenOrientation.Portrait)]
public sealed class PostComposerActivity : Activity
{
    const int MaxPhotos = 5;
    const int PickRequest = 4201;
    const int PhotoWidthDp = 230;
    const int PhotoHeightDp = 290;

    public const string ExtraPhotoPath = "photo_path";
    public const string ExtraPoints = "points";

    // Cópias temporárias das fotos escolhidas, na ordem do carrossel (a primeira é a capa).
    readonly List<string> photos = new();

    TextView? pointsBanner;
    int identificationPoints;
    EditText? captionInput;
    LinearLayout? photoRow;
    TextView? counter;
    Button? addPhotoButton;
    Button? publishButton;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        ThemeController.Apply(this);
        base.OnCreate(savedInstanceState);
        BuildUi();
        ApplyIncomingExtras();
        RenderPhotos();
    }

    bool Dark => HudBackground.IsDark(this);
    Color CardColor => Dark ? Color.Rgb(0, 29, 10) : Color.Rgb(255, 255, 255);
    Color GreenColor => Color.Rgb(32, 230, 0);
    Color TextColor => Dark ? Color.Rgb(245, 245, 245) : Color.Rgb(16, 24, 16);
    Color MutedColor => Dark ? Color.Rgb(168, 168, 168) : Color.Rgb(90, 100, 90);

    void BuildUi()
    {
        var root = new LinearLayout(this) { Orientation = Orientation.Vertical };
        root.SetPadding(Dp(16), Dp(12), Dp(16), Dp(16));
        root.SetOnApplyWindowInsetsListener(new SystemBarInsetsListener(16, 12));
        HudBackground.Apply(root, this);

        var title = new TextView(this) { Text = "Nova publicação", TextSize = 18 };
        title.SetTypeface(null, TypefaceStyle.Bold);
        title.SetTextColor(TextColor);
        root.AddView(title, new LinearLayout.LayoutParams(-1, -2) { BottomMargin = Dp(8) });

        var scroll = new ScrollView(this);
        var content = new LinearLayout(this) { Orientation = Orientation.Vertical };
        scroll.AddView(content);
        root.AddView(scroll, new LinearLayout.LayoutParams(-1, 0, 1f));

        // 1. Legenda do post.
        captionInput = new EditText(this)
        {
            Hint = "Escreva uma legenda…",
            Gravity = GravityFlags.Top | GravityFlags.Start
        };
        captionInput.SetMinLines(3);
        captionInput.SetMaxLines(6);
        captionInput.SetTextColor(TextColor);
        captionInput.SetHintTextColor(MutedColor);
        captionInput.SetPadding(Dp(12), Dp(12), Dp(12), Dp(12));
        captionInput.Background = Rounded(CardColor, 12, GreenColor, 2);
        captionInput.InputType = InputTypes.TextFlagMultiLine | InputTypes.TextFlagCapSentences;
        content.AddView(captionInput, new LinearLayout.LayoutParams(-1, -2));

        pointsBanner = new TextView(this) { TextSize = 14, Visibility = ViewStates.Gone };
        pointsBanner.SetTextColor(GreenColor);
        pointsBanner.SetPadding(Dp(4), Dp(8), 0, 0);
        content.AddView(pointsBanner, new LinearLayout.LayoutParams(-1, -2));

        // 2-3. Fotos lado a lado, em carrossel. A primeira é a capa.
        var carousel = new HorizontalScrollView(this) { HorizontalScrollBarEnabled = false };
        photoRow = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        photoRow.SetPadding(0, Dp(12), 0, Dp(12));
        carousel.AddView(photoRow);
        content.AddView(carousel, new LinearLayout.LayoutParams(-1, -2) { TopMargin = Dp(12) });

        // 4 e 5. Botões pequenos abaixo das fotos: adicionar fotos e marcar pessoas (só visual por enquanto).
        var smallRow = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        smallRow.SetGravity(GravityFlags.CenterVertical);

        addPhotoButton = SmallButton("🖼", "Adicionar fotos");
        addPhotoButton.Click += (_, _) => PickPhotos();
        smallRow.AddView(addPhotoButton, new LinearLayout.LayoutParams(Dp(48), Dp(48)));

        var tagButton = SmallButton("👤", "Marcar pessoas");
        tagButton.Click += (_, _) => Toast("Marcar pessoas entra com a rede social.");
        smallRow.AddView(tagButton, new LinearLayout.LayoutParams(Dp(48), Dp(48)) { LeftMargin = Dp(10) });

        counter = new TextView(this) { TextSize = 13 };
        counter.SetTextColor(MutedColor);
        smallRow.AddView(counter, new LinearLayout.LayoutParams(0, -2, 1f) { LeftMargin = Dp(12) });
        content.AddView(smallRow, new LinearLayout.LayoutParams(-1, -2) { TopMargin = Dp(8) });

        // 6. Cancelar (à esquerda) e publicar (à direita).
        var actions = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        actions.SetGravity(GravityFlags.CenterVertical);

        var cancel = CircleButton("✕");
        cancel.Click += (_, _) => Finish();
        actions.AddView(cancel, new LinearLayout.LayoutParams(Dp(60), Dp(60)));

        actions.AddView(new View(this), new LinearLayout.LayoutParams(0, 0, 1f));

        publishButton = CircleButton("✅");
        publishButton.Click += async (_, _) => await PublishAsync();
        actions.AddView(publishButton, new LinearLayout.LayoutParams(Dp(60), Dp(60)));

        root.AddView(actions, new LinearLayout.LayoutParams(-1, -2) { TopMargin = Dp(12) });
        root.AddView(BuildFooter(), new LinearLayout.LayoutParams(-1, -2) { TopMargin = Dp(12) });
        SetContentView(root);
    }

    // Veio da identificação? Recebe a foto usada e a pontuação para mostrar no post.
    void ApplyIncomingExtras()
    {
        identificationPoints = Intent?.GetIntExtra(ExtraPoints, 0) ?? 0;
        if (Intent?.HasExtra(ExtraPoints) == true)
        {
            pointsBanner!.Text = $"🏆 Pontuação da identificação: +{identificationPoints}";
            pointsBanner.Visibility = ViewStates.Visible;
        }

        var source = Intent?.GetStringExtra(ExtraPhotoPath);
        if (string.IsNullOrWhiteSpace(source) || !File.Exists(source) || photos.Count >= MaxPhotos)
            return;

        try
        {
            // Cópia própria: a foto original da identificação pode ser apagada depois.
            var copy = Path.Combine(CacheDir!.AbsolutePath, $"pick_{Guid.NewGuid():N}.img");
            File.Copy(source, copy, true);
            photos.Add(copy);
        }
        catch
        {
            Toast("Não foi possível trazer a foto da identificação.");
        }
    }

    // Rodapé de navegação: só aparece nesta tela.
    View BuildFooter()
    {
        var bar = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        bar.SetGravity(GravityFlags.CenterVertical);
        bar.SetPadding(Dp(8), Dp(6), Dp(8), Dp(6));
        bar.Background = Rounded(CardColor, 18, GreenColor, 2);

        var footerSlot = new LinearLayout.LayoutParams(0, Dp(50), 1f);
        bar.AddView(FooterButton("🏠", "Início", () => Finish()), footerSlot);
        bar.AddView(FooterButton("+", "Publicação atual", null, active: true), footerSlot);
        bar.AddView(FooterButton("🔍", "Identificação", () =>
        {
            MainActivity.OpenIdentifyOnResume = true;
            Finish();
        }), footerSlot);
        bar.AddView(FooterButton("➤", "Chats", () => Toast("Chats entram em uma versão futura.")), footerSlot);
        return bar;
    }

    Button FooterButton(string text, string description, Action? onClick, bool active = false)
    {
        var button = new Button(this) { Text = text, TextSize = 18 };
        button.SetAllCaps(false);
        button.ContentDescription = description;
        button.SetTextColor(active ? Color.Black : GreenColor);
        button.Background = Rounded(active ? GreenColor : Color.Transparent, 999);
        if (onClick is not null)
            button.Click += (_, _) => onClick();
        return button;
    }

    Button SmallButton(string text, string description)
    {
        var button = new Button(this) { Text = text, TextSize = 18 };
        button.SetAllCaps(false);
        button.SetTextColor(GreenColor);
        button.Background = Rounded(CardColor, 10, GreenColor, 1);
        button.ContentDescription = description;
        return button;
    }

    Button CircleButton(string text)
    {
        var button = new Button(this) { Text = text, TextSize = 20 };
        button.SetAllCaps(false);
        button.SetTextColor(GreenColor);
        button.Background = Rounded(CardColor, 999, GreenColor, 2);
        return button;
    }

    void RenderPhotos()
    {
        photoRow!.RemoveAllViews();

        if (photos.Count == 0)
            photoRow.AddView(EmptyPhoto(), PhotoLayout());

        for (var index = 0; index < photos.Count; index++)
            photoRow.AddView(PhotoCard(photos[index], index), PhotoLayout());

        counter!.Text = $"{photos.Count}/{MaxPhotos} fotos";
        addPhotoButton!.Enabled = photos.Count < MaxPhotos;
        addPhotoButton.Alpha = photos.Count < MaxPhotos ? 1f : 0.4f;

        // Sem foto não há publicação: o botão de publicar fica desabilitado.
        publishButton!.Enabled = photos.Count > 0;
        publishButton.Alpha = photos.Count > 0 ? 1f : 0.4f;
    }

    LinearLayout.LayoutParams PhotoLayout() =>
        new(Dp(PhotoWidthDp), Dp(PhotoHeightDp)) { RightMargin = Dp(12) };

    View EmptyPhoto()
    {
        var box = new TextView(this)
        {
            Text = "Adicione até 5 fotos",
            TextSize = 14,
            Gravity = GravityFlags.Center
        };
        box.SetTextColor(MutedColor);
        box.Background = Rounded(CardColor, 14, MutedColor, 1);
        return box;
    }

    View PhotoCard(string path, int index)
    {
        var frame = new FrameLayout(this);
        frame.Background = Rounded(CardColor, 14, GreenColor, 2);
        frame.SetPadding(Dp(3), Dp(3), Dp(3), Dp(3));

        var image = new ImageView(this);
        image.SetScaleType(ImageView.ScaleType.CenterCrop);
        try
        {
            image.SetImageBitmap(ImageLoader.Load(path, 600));
        }
        catch
        {
            image.SetBackgroundColor(Color.Rgb(80, 80, 80));
        }
        frame.AddView(image, new FrameLayout.LayoutParams(-1, -1));

        if (index == 0)
        {
            var cover = new TextView(this) { Text = "1ª · capa", TextSize = 12 };
            cover.SetTextColor(Color.Black);
            cover.SetPadding(Dp(8), Dp(4), Dp(8), Dp(4));
            cover.Background = Rounded(GreenColor, 10);
            frame.AddView(cover, new FrameLayout.LayoutParams(-2, -2, GravityFlags.Top | GravityFlags.Start)
            {
                LeftMargin = Dp(10),
                TopMargin = Dp(10)
            });
        }

        // Toque no ✕ tira a foto da lista.
        var remove = new TextView(this) { Text = "✕", TextSize = 14 };
        remove.Gravity = GravityFlags.Center;
        remove.SetTextColor(Color.Black);
        remove.Background = Rounded(GreenColor, 999);
        remove.Click += (_, _) => RemovePhoto(index);
        frame.AddView(remove, new FrameLayout.LayoutParams(Dp(30), Dp(30), GravityFlags.Top | GravityFlags.End)
        {
            RightMargin = Dp(10),
            TopMargin = Dp(10)
        });

        return frame;
    }

    void RemovePhoto(int index)
    {
        if (index < 0 || index >= photos.Count)
            return;

        TryDelete(photos[index]);
        photos.RemoveAt(index);
        RenderPhotos();
    }

    void PickPhotos()
    {
        if (photos.Count >= MaxPhotos)
        {
            Toast($"Limite de {MaxPhotos} fotos por publicação.");
            return;
        }

        var intent = new Intent(Intent.ActionOpenDocument);
        intent.AddCategory(Intent.CategoryOpenable);
        intent.SetType("image/*");
        intent.PutExtra("android.intent.extra.ALLOW_MULTIPLE", true);
        StartActivityForResult(intent, PickRequest);
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        if (requestCode != PickRequest || resultCode != Result.Ok || data is null)
            return;

        var uris = new List<Uri>();
        if (data.ClipData is { } clip)
        {
            for (var i = 0; i < clip.ItemCount; i++)
            {
                var uri = clip.GetItemAt(i)?.Uri;
                if (uri is not null)
                    uris.Add(uri);
            }
        }
        else if (data.Data is { } single)
        {
            uris.Add(single);
        }

        var room = MaxPhotos - photos.Count;
        foreach (var uri in uris.Take(room))
        {
            var copy = CopyToCache(uri);
            if (copy is not null)
                photos.Add(copy);
        }

        if (uris.Count > room)
            Toast($"Só cabem {MaxPhotos} fotos; as extras foram ignoradas.");

        RenderPhotos();
    }

    string? CopyToCache(Uri uri)
    {
        try
        {
            var target = Path.Combine(CacheDir!.AbsolutePath, $"pick_{Guid.NewGuid():N}.img");
            using var input = ContentResolver!.OpenInputStream(uri);
            if (input is null)
                return null;

            using var output = File.Create(target);
            input.CopyTo(output);
            return target;
        }
        catch
        {
            return null;
        }
    }

    async Task PublishAsync()
    {
        if (photos.Count == 0)
        {
            Toast("Escolha pelo menos uma foto.");
            return;
        }

        publishButton!.Enabled = false;
        try
        {
            var id = Guid.NewGuid().ToString("N");
            var folder = PostStore.FolderFor(this, id);
            var sources = photos.ToList();

            // Redimensiona e salva em JPEG no próprio app, em segundo plano.
            var saved = await Task.Run(() =>
            {
                Directory.CreateDirectory(folder);
                var paths = new List<string>();
                for (var index = 0; index < sources.Count; index++)
                {
                    var target = Path.Combine(folder, $"{index + 1}.jpg");
                    using var bitmap = ImageLoader.Load(sources[index], 1080);
                    using var stream = File.Create(target);
                    bitmap.Compress(Bitmap.CompressFormat.Jpeg!, 88, stream);
                    paths.Add(target);
                }
                return paths;
            });

            foreach (var source in sources)
                TryDelete(source);
            photos.Clear();

            PostStore.Add(this, new SavedPost
            {
                Id = id,
                Author = ScoreStore.DisplayName,
                Caption = captionInput!.Text?.Trim() ?? "",
                ImagePaths = saved,
                CreatedAtUtcTicks = DateTime.UtcNow.Ticks,
                IdentificationPoints = identificationPoints
            });

            Finish();
        }
        catch (Exception ex)
        {
            Toast($"Não foi possível publicar: {ex.Message}");
            publishButton.Enabled = true;
        }
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
            // Arquivo temporário: se não apagar, o sistema limpa o cache depois.
        }
    }

    int Dp(int value) => (int)(value * Resources!.DisplayMetrics!.Density);

    GradientDrawable Rounded(Color fill, int radiusDp, Color? stroke = null, int strokeDp = 0)
    {
        var shape = new GradientDrawable();
        shape.SetCornerRadius(Dp(radiusDp));
        shape.SetColor(fill);
        if (stroke is Color line && strokeDp > 0)
            shape.SetStroke(Dp(strokeDp), line);
        return shape;
    }

    void Toast(string text)
    {
        Android.Widget.Toast.MakeText(this, text, ToastLength.Short)?.Show();
    }
}
