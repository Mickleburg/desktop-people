namespace DesktopPeople.App;

internal sealed class LauncherForm : Form
{
    private readonly Button _releaseButton;
    private bool _allowClose;

    public LauncherForm()
    {
        Text = "DesktopPeople";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(520, 410);
        MinimumSize = new Size(520, 410);
        MaximizeBox = false;
        BackColor = Color.FromArgb(247, 247, 251);
        Font = new Font("Segoe UI", 10);

        var title = new Label
        {
            AutoSize = true,
            Text = "DesktopPeople",
            Font = new Font("Segoe UI Semibold", 22),
            ForeColor = Color.FromArgb(37, 39, 53),
            Location = new Point(34, 28),
        };

        var subtitle = new Label
        {
            AutoSize = false,
            Text = "Маленький персонаж, который живёт на вашем рабочем столе.",
            ForeColor = Color.FromArgb(102, 105, 122),
            Location = new Point(38, 76),
            Size = new Size(440, 44),
        };

        var prototypeCard = new Panel
        {
            BackColor = Color.White,
            Location = new Point(36, 128),
            Size = new Size(448, 156),
        };
        prototypeCard.Paint += (_, args) =>
        {
            using var border = new Pen(Color.FromArgb(222, 222, 232), 1);
            args.Graphics.DrawRectangle(border, 0, 0, prototypeCard.Width - 1, prototypeCard.Height - 1);
        };

        var cardIcon = new Label
        {
            Text = "☺",
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI", 30),
            ForeColor = Color.FromArgb(111, 92, 255),
            Location = new Point(24, 20),
            Size = new Size(62, 62),
        };
        var cardTitle = new Label
        {
            AutoSize = true,
            Text = "Тестовый персонаж готов",
            Font = new Font("Segoe UI Semibold", 12),
            ForeColor = Color.FromArgb(37, 39, 53),
            Location = new Point(102, 27),
        };
        var cardText = new Label
        {
            AutoSize = false,
            Text = "Это технический прототип overlay-runtime.\nОбработка фотографий появится на следующем этапе.",
            ForeColor = Color.FromArgb(102, 105, 122),
            Location = new Point(103, 58),
            Size = new Size(310, 56),
        };
        prototypeCard.Controls.AddRange([cardIcon, cardTitle, cardText]);

        _releaseButton = new Button
        {
            Text = "Выпустить на рабочий стол",
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(111, 92, 255),
            ForeColor = Color.White,
            Cursor = Cursors.Hand,
            Location = new Point(36, 306),
            Size = new Size(448, 48),
            Font = new Font("Segoe UI Semibold", 10),
        };
        _releaseButton.FlatAppearance.BorderSize = 0;

        var privacy = new Label
        {
            AutoSize = false,
            Text = "Работает локально • без телеметрии",
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Color.FromArgb(125, 128, 143),
            Location = new Point(36, 367),
            Size = new Size(448, 22),
        };

        Controls.AddRange([title, subtitle, prototypeCard, _releaseButton, privacy]);
        FormClosing += OnLauncherClosing;
    }

    public event EventHandler? ReleaseRequested
    {
        add => _releaseButton.Click += value;
        remove => _releaseButton.Click -= value;
    }

    public void CloseForExit()
    {
        _allowClose = true;
        Close();
    }

    private void OnLauncherClosing(object? sender, FormClosingEventArgs e)
    {
        if (_allowClose)
        {
            return;
        }

        e.Cancel = true;
        Hide();
    }
}

