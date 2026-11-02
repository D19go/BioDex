using Android.OS;
using Android.Views;

namespace FaunaLens;

// A partir do Android 15 o app é desenhado por baixo da barra de status e da barra de
// navegação. Este listener soma a altura delas ao espaçamento original da tela.
sealed class SystemBarInsetsListener : Java.Lang.Object, View.IOnApplyWindowInsetsListener
{
    readonly int horizontal;
    readonly int vertical;

    public SystemBarInsetsListener(int horizontal, int vertical)
    {
        this.horizontal = horizontal;
        this.vertical = vertical;
    }

    public WindowInsets? OnApplyWindowInsets(View? v, WindowInsets? insets)
    {
        if (v is null || insets is null)
            return insets;

        int left, top, right, bottom;
        if (Build.VERSION.SdkInt >= BuildVersionCodes.R)
        {
            var bars = insets.GetInsets(WindowInsets.Type.SystemBars() | WindowInsets.Type.DisplayCutout());
            left = bars.Left;
            top = bars.Top;
            right = bars.Right;
            bottom = bars.Bottom;
        }
        else
        {
#pragma warning disable CA1416, CS0618
            left = insets.SystemWindowInsetLeft;
            top = insets.SystemWindowInsetTop;
            right = insets.SystemWindowInsetRight;
            bottom = insets.SystemWindowInsetBottom;
#pragma warning restore CA1416, CS0618
        }

        v.SetPadding(horizontal + left, vertical + top, horizontal + right, vertical + bottom);
        return insets;
    }
}
