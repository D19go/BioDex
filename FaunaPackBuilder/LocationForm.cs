namespace FaunaPackBuilder;

public sealed class LocationForm : Form
{
    readonly TextBox country = new();
    readonly TextBox state = new();

    public string CountryCode => country.Text.Trim().ToUpperInvariant();
    public string StateProvince => state.Text.Trim();

    public LocationForm(string countryCode, string stateProvince)
    {
        Text = "Selecionar local";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(500, 230);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(15),
            ColumnCount = 2,
            RowCount = 5
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 155));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        Controls.Add(root);

        root.Controls.Add(new Label { Text = "País (ISO-2)", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        country.Dock = DockStyle.Fill;
        country.MaxLength = 2;
        country.Text = countryCode;
        country.CharacterCasing = CharacterCasing.Upper;
        root.Controls.Add(country, 1, 0);

        root.Controls.Add(new Label { Text = "Estado/Província", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 1);
        state.Dock = DockStyle.Fill;
        state.Text = stateProvince;
        root.Controls.Add(state, 1, 1);

        var info = new Label
        {
            Text = "A busca usa estes valores diretamente nos filtros do GBIF.\n" +
                   "Deixe Estado/Província vazio para pesquisar o país inteiro.\n\n" +
                   "Filtros mantidos: Animalia • StillImage • present • SPECIES.",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.TopLeft
        };
        root.Controls.Add(info, 0, 2);
        root.SetColumnSpan(info, 2);

        var cancel = new Button { Text = "Cancelar", Dock = DockStyle.Fill };
        cancel.DialogResult = DialogResult.Cancel;
        root.Controls.Add(cancel, 0, 4);

        var ok = new Button { Text = "Aplicar", Dock = DockStyle.Fill };
        ok.Click += (_, _) =>
        {
            if (CountryCode.Length != 2 || !CountryCode.All(char.IsLetter))
            {
                MessageBox.Show(this, "Informe um código de país ISO-2 válido, por exemplo BR.", "Local", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                country.Focus();
                return;
            }

            DialogResult = DialogResult.OK;
            Close();
        };
        root.Controls.Add(ok, 1, 4);
        AcceptButton = ok;
        CancelButton = cancel;
    }
}
