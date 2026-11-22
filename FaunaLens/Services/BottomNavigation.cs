using Android.App;
using Android.Content;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.OS;
using Android.Views;
using Android.Widget;
using FaunaLens;

namespace FaunaLens.Services;

// Rodapé de navegação das telas principais: só uma borda verde em cima, sem laterais.
// Ícones: casinha (início), + (nova publicação), lupa (identificação) e avião (chats, versão futura).
// A tela de publicação e as visualizações em tela cheia não usam este rodapé.
public static class BottomNavigation
{
    static readonly Color Green = Color.Rgb(32, 230, 0);

    // Envolve o conteúdo da tela e acrescenta o rodapé embaixo, com o fundo do app por trás dos dois.
    public static View Wrap(Activity activity, View content)
    {
        var wrapper = new LinearLayout(activity) { Orientation = Orientation.Vertical };
        HudBackground.Apply(wrapper, activity);
        wrapper.AddView(content, new LinearLayout.LayoutParams(-1, 0, 1f));
        wrapper.AddView(Create(activity), new LinearLayout.LayoutParams(-1, -2));
        return wrapper;
    }

    // Rodapé em si. Na tela inicial, os botões usam onHome (fecha painéis) e onIdentify (abre a lupa).
    // Nas demais telas, o botão de início volta para a tela inicial.
    public static LinearLayout Create(Activity activity, Action? onIdentify = null, Action? onHome = null)
    {
        var density = activity.Resources!.DisplayMetrics!.Density;
        int Dp(int value) => (int)(value * density);

        var footer = new LinearLayout(activity) { Orientation = Orientation.Vertical };

        var border = new View(activity);
        border.SetBackgroundColor(Green);
        footer.AddView(border, new LinearLayout.LayoutParams(-1, Dp(2)));

        var row = new LinearLayout(activity) { Orientation = Orientation.Horizontal };
        row.SetGravity(GravityFlags.CenterVertical);
        row.SetPadding(Dp(8), Dp(8), Dp(8), Dp(8));
        // Empurra os botões para cima da barra de navegação do Android.
        row.SetOnApplyWindowInsetsListener(new BottomInsetsListener(Dp(8)));
        footer.AddView(row, new LinearLayout.LayoutParams(-1, -2));

        void Add(string icon, string description, Action onClick)
        {
            var slot = new LinearLayout.LayoutParams(0, Dp(48), 1f) { Gravity = GravityFlags.Center };
            row.AddView(Item(activity, icon, description, onClick), slot);
        }

        Add("🏠", "Início", () =>
        {
            if (onHome is not null)
                onHome();
            else
                GoHome(activity);
        });
        Add("+", "Nova publicação", () =>
            activity.StartActivity(new Intent(activity, typeof(PostComposerActivity))));
        Add("🔍", "Identificação", () =>
        {
            if (onIdentify is not null)
            {
                onIdentify();
                return;
            }

            // Fora da tela inicial: a tela inicial abre o painel de identificação ao voltar.
            MainActivity.OpenIdentifyOnResume = true;
            GoHome(activity);
        });
        Add("➤", "Chats", () =>
            Android.Widget.Toast.MakeText(activity, "Chats entram em uma versão futura.", ToastLength.Short)?.Show());

        return footer;
    }

    // Volta para a tela inicial que já está aberta, sem criar outra.
    static void GoHome(Activity activity)
    {
        if (activity is MainActivity)
            return;

        var intent = new Intent(activity, typeof(MainActivity));
        intent.AddFlags(ActivityFlags.ClearTop | ActivityFlags.SingleTop);
        activity.StartActivity(intent);
    }

    static Button Item(Activity activity, string icon, string description, Action onClick)
    {
        var button = new Button(activity) { Text = icon, TextSize = 18 };
        button.SetAllCaps(false);
        button.SetMinWidth(0);
        button.SetMinHeight(0);
        button.SetPadding(0, 0, 0, 0);
        button.ContentDescription = description;
        button.SetTextColor(Green);

        // Sem fundo nem contorno: todos os ícones iguais, só com a borda de cima.
        var shape = new GradientDrawable();
        shape.SetShape(Android.Graphics.Drawables.ShapeType.Oval);
        shape.SetColor(Color.Transparent);
        button.Background = shape;

        button.Click += (_, _) => onClick();
        return button;
    }

    // Soma a altura da barra de navegação ao espaço embaixo do rodapé.
    sealed class BottomInsetsListener : Java.Lang.Object, View.IOnApplyWindowInsetsListener
    {
        readonly int basePadding;

        public BottomInsetsListener(int basePadding)
        {
            this.basePadding = basePadding;
        }

        public WindowInsets? OnApplyWindowInsets(View? v, WindowInsets? insets)
        {
            if (v is null || insets is null)
                return insets;

            int bottom;
            if (Build.VERSION.SdkInt >= BuildVersionCodes.R)
            {
                bottom = insets.GetInsets(WindowInsets.Type.SystemBars()).Bottom;
            }
            else
            {
#pragma warning disable CA1416, CS0618
                bottom = insets.SystemWindowInsetBottom;
#pragma warning restore CA1416, CS0618
            }

            v.SetPadding(v.PaddingLeft, v.PaddingTop, v.PaddingRight, basePadding + bottom);
            return insets;
        }
    }
}
