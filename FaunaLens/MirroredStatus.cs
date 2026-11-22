namespace FaunaLens;

// Uma mensagem de status mostrada em mais de um painel ao mesmo tempo.
public sealed class MirroredStatus
{
    readonly Android.Widget.TextView[] views;

    public MirroredStatus(params Android.Widget.TextView[] views)
    {
        this.views = views;
    }

    public string Text
    {
        set
        {
            foreach (var view in views)
                view.Text = value;
        }
    }
}
