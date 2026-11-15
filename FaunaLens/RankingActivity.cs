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

    LinearLayout? list;

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
    }

    // Atualizado a cada volta para a tela, então a sua linha reflete as identificações feitas.
    void Render()
    {
        list!.RemoveAllViews();

        list.AddView(new TextView(this)
        {
            Text = "PRÉVIA: nomes e pontos dos outros são fictícios. A rede social ainda não existe; " +
                   "a sua linha usa a pontuação real deste aparelho.",
            TextSize = 13
        });

        list.AddView(new TextView(this)
        {
            Text = $"Pontuação provisória: comum {ScoringPolicy.PlaceholderNormalPoints} pts, " +
                   $"atenção {ScoringPolicy.PlaceholderAttentionPoints} pts. " +
                   "Faixa 1 e ameaçadas: só registro, 0 pts. Cada espécie pontua uma vez.",
            TextSize = 12
        });

        var rows = SampleFriends
            .Select(friend => (Name: friend.Name, Points: friend.Points, Mine: false))
            .Append((Name: "Você", Points: ScoreStore.TotalPoints, Mine: true))
            .OrderByDescending(row => row.Points)
            .ToList();

        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var view = new TextView(this)
            {
                Text = $"{i + 1}. {row.Name} — {row.Points} pts",
                TextSize = 18
            };
            view.SetPadding(0, 16, 0, 16);
            if (row.Mine)
                view.SetTypeface(null, Android.Graphics.TypefaceStyle.Bold);
            list.AddView(view);
        }
    }
}
