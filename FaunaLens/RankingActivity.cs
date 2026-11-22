using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using Android.Widget;
using FaunaLens.Services;

namespace FaunaLens;

[Activity(Label = "Ranking (prévia)", Exported = false, ScreenOrientation = ScreenOrientation.Portrait)]
public sealed class RankingActivity : Activity
{
    // Nomes e pontos fictícios: só para visualizar como o ranking vai ficar.
    static readonly (string Name, int Points)[] SampleFriends =
    {
        ("Ana (exemplo)", 180),
        ("Bruno (exemplo)", 135),
        ("Carla (exemplo)", 90),
        ("Diego (exemplo)", 40)
    };

    sealed record Row(string Name, int Points, bool Mine);

    LinearLayout? list;
    EditText? nameInput;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        ThemeController.Apply(this);
        base.OnCreate(savedInstanceState);
        BuildUi();
        Render();
    }

    protected override void OnResume()
    {
        base.OnResume();
        ScoreStore.Sync();
        Render();
    }

    void BuildUi()
    {
        var root = new LinearLayout(this) { Orientation = Orientation.Vertical };
        root.SetPadding(32, 24, 32, 24);
        root.SetOnApplyWindowInsetsListener(new SystemBarInsetsListener(32, 24));

        var scroll = new ScrollView(this);
        list = new LinearLayout(this) { Orientation = Orientation.Vertical };
        scroll.AddView(list);
        root.AddView(scroll);
        SetContentView(root);
        HudBackground.Apply(root, this);
    }

    // Atualizado a cada volta para a tela, então a sua linha reflete as identificações feitas.
    void Render()
    {
        list!.RemoveAllViews();

        list.AddView(new TextView(this)
        {
            Text = "PRÉVIA: os exemplos são fictícios. Os apps clones deste aparelho aparecem aqui " +
                   "quando têm acesso a Download/BioDex. Cada app precisa de acesso separado.",
            TextSize = 13
        });

        list.AddView(new TextView(this) { Text = "Seu nome no placar" });
        nameInput = new EditText(this) { Text = ScoreStore.DisplayName };
        list.AddView(nameInput);

        var saveName = new Button(this) { Text = "Salvar nome" };
        saveName.Click += (_, _) =>
        {
            ScoreStore.SetDisplayName(nameInput!.Text ?? "");
            Render();
        };
        list.AddView(saveName);

        list.AddView(new TextView(this)
        {
            Text = "Pontuação: 22 a 65 pts por espécie, conforme raridade, dificuldade, porte e conservação. " +
                   "Bônus: primeira descoberta +10, colecionador +15 (segunda espécie do grupo) e explorador +5. " +
                   "Ameaçadas: só registro, 0 pts. Repetições descontam 5 pts por vez, até 80% da pontuação individual.",
            TextSize = 12
        });

        var rows = BuildRows();
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var view = new TextView(this)
            {
                Text = $"{i + 1}. {row.Name}{(row.Mine ? " (você)" : "")} — {row.Points} pts",
                TextSize = 18
            };
            view.SetPadding(0, 16, 0, 16);
            if (row.Mine)
                view.SetTypeface(null, Android.Graphics.TypefaceStyle.Bold);
            list.AddView(view);
        }
    }

    static List<Row> BuildRows()
    {
        var own = ScoreStore.PackageId;
        var real = SharedScoreboard.Read()
            .Select(entry => new Row(entry.Name, entry.Points, entry.Id == own))
            .ToList();

        // Sem acesso ao placar compartilhado, a própria linha vem da pontuação local.
        if (!real.Any(row => row.Mine))
            real.Add(new Row(ScoreStore.DisplayName, ScoreStore.TotalPoints, true));

        return SampleFriends
            .Select(friend => new Row(friend.Name, friend.Points, false))
            .Concat(real)
            .OrderByDescending(row => row.Points)
            .ToList();
    }
}
