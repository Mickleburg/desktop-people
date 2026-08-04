using Godot;

namespace DesktopPeople.GodotHost;

/// <summary>
/// The launch window, rebuilt on Godot's own UI nodes. The WinForms one stays where it is; this
/// is the version that ships once the application is exported from Godot.
/// <para>
/// It is a real OS window, not an embedded sub-window — see the note beside
/// <c>embed_subwindows</c> in project.godot. Closing it hides it rather than quitting: the
/// character keeps living on the desktop and the tray reopens this.
/// </para>
/// </summary>
public sealed partial class LauncherWindow : Window
{
    private static readonly Color Ink = Color.Color8(37, 39, 53);
    private static readonly Color Muted = Color.Color8(102, 105, 122);
    private static readonly Color Faint = Color.Color8(125, 128, 143);
    private static readonly Color Accent = Color.Color8(111, 92, 255);
    private static readonly Color Page = Color.Color8(247, 247, 251);
    private static readonly Color CardBorder = Color.Color8(222, 222, 232);
    private static readonly Color ButtonBorder = Color.Color8(210, 210, 224);

    private static readonly (string Key, string Label)[] Intensities =
    [
        ("calm", "Спокойный"),
        ("normal", "Обычный"),
        ("active", "Активный"),
    ];

    private readonly Dictionary<string, Button> _intensityButtons = [];
    private Label _scaleLabel = null!;
    private HSlider _scaleSlider = null!;
    private string _intensity = "normal";
    private bool _suppressScaleSignal;

    /// <summary>Raised when the user presses "release" — the character is not put on the desktop
    /// before that, exactly as in the WinForms host.</summary>
    public event Action? ReleaseRequested;

    public event Action<string>? IntensityChanged;

    public event Action<int>? ScaleChanged;

    public override void _Ready()
    {
        Title = "DesktopPeople";
        Size = new Vector2I(520, 548);

        // An ordinary application window: resizable, minimisable, in the taskbar and Alt+Tab,
        // not pinned above anything. None of the overlay's special window rules belong here —
        // this one should obey exactly the same rules as any other window on the desktop.
        Unresizable = false;
        MinSize = new Vector2I(460, 520);
        Transparent = false;
        AlwaysOnTop = false;

        // Closing must not take the character down with it.
        CloseRequested += Hide;

        var background = new ColorRect { Color = Page };
        background.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(background);

        var margin = new MarginContainer();
        margin.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        margin.AddThemeConstantOverride("margin_left", 34);
        margin.AddThemeConstantOverride("margin_right", 34);
        margin.AddThemeConstantOverride("margin_top", 26);
        margin.AddThemeConstantOverride("margin_bottom", 22);
        AddChild(margin);

        var page = new VBoxContainer();
        page.AddThemeConstantOverride("separation", 10);
        margin.AddChild(page);

        page.AddChild(Text("DesktopPeople", 24, Ink, bold: true));
        page.AddChild(Text("Маленький персонаж, который живёт на вашем рабочем столе.", 12, Muted));
        page.AddChild(BuildPrototypeCard());
        page.AddChild(Spacer(6));

        page.AddChild(Text("Активность персонажа", 12, Ink, bold: true));
        var intensityRow = new HBoxContainer();
        intensityRow.AddThemeConstantOverride("separation", 6);
        foreach ((string key, string label) in Intensities)
        {
            var button = new Button { Text = label, CustomMinimumSize = new Vector2(0, 34) };
            button.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            button.Pressed += () =>
            {
                _intensity = key;
                RestyleIntensityButtons();
                IntensityChanged?.Invoke(key);
            };

            _intensityButtons[key] = button;
            intensityRow.AddChild(button);
        }

        page.AddChild(intensityRow);
        page.AddChild(Spacer(6));

        _scaleLabel = Text("Масштаб персонажа: 100%", 12, Ink, bold: true);
        page.AddChild(_scaleLabel);

        _scaleSlider = new HSlider
        {
            MinValue = 70,
            MaxValue = 160,
            Step = 1,
            Value = 100,
            CustomMinimumSize = new Vector2(0, 28),
        };

        _scaleSlider.ValueChanged += value =>
        {
            _scaleLabel.Text = $"Масштаб персонажа: {(int)value}%";
            if (!_suppressScaleSignal)
            {
                ScaleChanged?.Invoke((int)value);
            }
        };

        page.AddChild(_scaleSlider);
        page.AddChild(Spacer(10));

        var release = new Button
        {
            Text = "Выпустить на рабочий стол",
            CustomMinimumSize = new Vector2(0, 48),
        };

        StyleButton(release, Accent, Colors.White, Accent);
        release.Pressed += () => ReleaseRequested?.Invoke();
        page.AddChild(release);

        var privacy = Text("Работает локально • без телеметрии", 11, Faint);
        privacy.HorizontalAlignment = HorizontalAlignment.Center;
        page.AddChild(privacy);

        RestyleIntensityButtons();
    }

    public string SelectedIntensity
    {
        get => _intensity;
        set
        {
            _intensity = value;
            RestyleIntensityButtons();
        }
    }

    public int SelectedScalePercent
    {
        get => (int)_scaleSlider.Value;
        set
        {
            // Set without echoing back out as a user edit, or restoring saved settings would
            // look identical to the user dragging the slider.
            _suppressScaleSignal = true;
            _scaleSlider.Value = Math.Clamp(value, (int)_scaleSlider.MinValue, (int)_scaleSlider.MaxValue);
            _suppressScaleSignal = false;
            _scaleLabel.Text = $"Масштаб персонажа: {(int)_scaleSlider.Value}%";
        }
    }

    private Control BuildPrototypeCard()
    {
        var card = new PanelContainer { CustomMinimumSize = new Vector2(0, 140) };
        var style = new StyleBoxFlat
        {
            BgColor = Colors.White,
            BorderColor = CardBorder,
            ContentMarginLeft = 22,
            ContentMarginRight = 22,
            ContentMarginTop = 20,
            ContentMarginBottom = 20,
        };

        style.SetBorderWidthAll(1);
        card.AddThemeStyleboxOverride("panel", style);

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 16);
        card.AddChild(row);

        Label icon = Text("☺", 34, Accent);
        icon.CustomMinimumSize = new Vector2(62, 0);
        icon.VerticalAlignment = VerticalAlignment.Center;
        row.AddChild(icon);

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 8);
        column.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        column.AddChild(Text("Тестовый персонаж готов", 13, Ink, bold: true));
        column.AddChild(Text(
            "Это технический прототип overlay-runtime.\nОбработка фотографий появится на следующем этапе.",
            11,
            Muted));
        row.AddChild(column);
        return card;
    }

    private void RestyleIntensityButtons()
    {
        foreach ((string key, Button button) in _intensityButtons)
        {
            bool selected = key == _intensity;
            StyleButton(
                button,
                selected ? Accent : Colors.White,
                selected ? Colors.White : Ink,
                selected ? Accent : ButtonBorder);
        }
    }

    /// <summary>Godot's default button skin is a dark game-UI look; every state is overridden so
    /// the window reads as the same application as the WinForms launcher.</summary>
    private static void StyleButton(Button button, Color background, Color foreground, Color border)
    {
        foreach (string state in new[] { "normal", "hover", "pressed", "focus", "disabled" })
        {
            var style = new StyleBoxFlat
            {
                BgColor = state == "hover" ? background.Lightened(0.06f) : background,
                BorderColor = border,
                CornerRadiusTopLeft = 4,
                CornerRadiusTopRight = 4,
                CornerRadiusBottomLeft = 4,
                CornerRadiusBottomRight = 4,
            };

            style.SetBorderWidthAll(1);
            button.AddThemeStyleboxOverride(state, style);
        }

        button.AddThemeColorOverride("font_color", foreground);
        button.AddThemeColorOverride("font_hover_color", foreground);
        button.AddThemeColorOverride("font_pressed_color", foreground);
        button.AddThemeColorOverride("font_focus_color", foreground);
        button.AddThemeFontSizeOverride("font_size", 13);
    }

    private static Label Text(string text, int size, Color color, bool bold = false)
    {
        var label = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        label.AddThemeColorOverride("font_color", color);
        label.AddThemeFontSizeOverride("font_size", bold ? size + 1 : size);
        return label;
    }

    private static Control Spacer(int height) => new Control { CustomMinimumSize = new Vector2(0, height) };
}
