using System.ComponentModel;

namespace DesktopPeople.App;

internal sealed class LauncherForm : Form
{
    private static readonly (string Key, string Label)[] IntensityOptions =
    [
        ("calm", "Спокойный"),
        ("normal", "Обычный"),
        ("active", "Активный"),
    ];

    private readonly Button _releaseButton;
    private readonly Dictionary<string, Button> _intensityButtons = [];
    private readonly TrackBar _scaleTrackBar;
    private readonly Label _scaleLabel;
    private string _selectedIntensity = "normal";
    private int _selectedScalePercent = 100;
    private bool _suppressScaleEvent;
    private bool _allowClose;

    public LauncherForm()
    {
        Text = "DesktopPeople";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(520, 548);
        MinimumSize = new Size(520, 548);
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

        var intensityLabel = new Label
        {
            AutoSize = true,
            Text = "Активность персонажа",
            Font = new Font("Segoe UI Semibold", 10),
            ForeColor = Color.FromArgb(37, 39, 53),
            Location = new Point(38, 296),
        };

        const int buttonWidth = 145;
        for (int index = 0; index < IntensityOptions.Length; index++)
        {
            (string key, string label) = IntensityOptions[index];
            var button = new Button
            {
                Text = label,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand,
                Location = new Point(36 + (index * (buttonWidth + 6)), 324),
                Size = new Size(buttonWidth, 34),
                Font = new Font("Segoe UI", 9),
            };
            button.FlatAppearance.BorderColor = Color.FromArgb(210, 210, 224);
            button.Click += (_, _) => SetIntensity(key);
            _intensityButtons[key] = button;
            Controls.Add(button);
        }

        _scaleLabel = new Label
        {
            AutoSize = true,
            Text = "Масштаб персонажа: 100%",
            Font = new Font("Segoe UI Semibold", 10),
            ForeColor = Color.FromArgb(37, 39, 53),
            Location = new Point(38, 372),
        };

        _scaleTrackBar = new TrackBar
        {
            Minimum = 70,
            Maximum = 160,
            TickFrequency = 10,
            Value = 100,
            Location = new Point(34, 396),
            Size = new Size(452, 45),
        };
        _scaleTrackBar.ValueChanged += (_, _) => SetScalePercent(_scaleTrackBar.Value);

        _releaseButton = new Button
        {
            Text = "Выпустить на рабочий стол",
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(111, 92, 255),
            ForeColor = Color.White,
            Cursor = Cursors.Hand,
            Location = new Point(36, 453),
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
            Location = new Point(36, 509),
            Size = new Size(448, 22),
        };

        Controls.AddRange(
            [title, subtitle, prototypeCard, intensityLabel, _scaleLabel, _scaleTrackBar, _releaseButton, privacy]);
        FormClosing += OnLauncherClosing;
        UpdateIntensityButtonStyles();
    }

    public event EventHandler? ReleaseRequested
    {
        add => _releaseButton.Click += value;
        remove => _releaseButton.Click -= value;
    }

    public event Action<string>? IntensityChanged;

    public event Action<int>? ScaleChanged;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string SelectedIntensity
    {
        get => _selectedIntensity;
        set
        {
            _selectedIntensity = value;
            UpdateIntensityButtonStyles();
        }
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int SelectedScalePercent
    {
        get => _selectedScalePercent;
        set
        {
            _selectedScalePercent = Math.Clamp(value, _scaleTrackBar.Minimum, _scaleTrackBar.Maximum);
            _suppressScaleEvent = true;
            _scaleTrackBar.Value = _selectedScalePercent;
            _suppressScaleEvent = false;
            _scaleLabel.Text = $"Масштаб персонажа: {_selectedScalePercent}%";
        }
    }

    private void SetIntensity(string intensity)
    {
        SelectedIntensity = intensity;
        IntensityChanged?.Invoke(intensity);
    }

    private void SetScalePercent(int percent)
    {
        _selectedScalePercent = percent;
        _scaleLabel.Text = $"Масштаб персонажа: {percent}%";
        if (!_suppressScaleEvent)
        {
            ScaleChanged?.Invoke(percent);
        }
    }

    private void UpdateIntensityButtonStyles()
    {
        foreach ((string key, Button button) in _intensityButtons)
        {
            bool selected = key == _selectedIntensity;
            button.BackColor = selected ? Color.FromArgb(111, 92, 255) : Color.White;
            button.ForeColor = selected ? Color.White : Color.FromArgb(37, 39, 53);
        }
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

