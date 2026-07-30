using System.Text.Json;

namespace DesktopPeople.WindowHost;

internal sealed class PlatformTestForm : Form
{
    private readonly string _logPath;
    private readonly System.Windows.Forms.Timer? _automationTimer;
    private int _automationStep;

    public PlatformTestForm(bool automated)
    {
        Text = "DesktopPeople Platform Test Host";
        StartPosition = FormStartPosition.Manual;
        Location = new Point(220, 320);
        ClientSize = new Size(680, 420);
        MinimumSize = new Size(320, 220);
        BackColor = Color.FromArgb(245, 247, 252);
        Font = new Font("Segoe UI", 10);

        string logDirectory = Path.Combine(Path.GetTempPath(), "DesktopPeople.WindowHost");
        Directory.CreateDirectory(logDirectory);
        _logPath = Path.Combine(logDirectory, "last-run.jsonl");
        File.WriteAllText(_logPath, string.Empty);

        var title = new Label
        {
            Text = "Обычное тестовое окно-платформа",
            Font = new Font("Segoe UI Semibold", 18),
            AutoSize = true,
            Location = new Point(28, 26),
        };
        var instructions = new Label
        {
            Text = "Поставьте персонажа на верхнюю рамку. Затем перемещайте,\n" +
                "изменяйте размер, скрывайте или закрывайте это окно.",
            AutoSize = true,
            ForeColor = Color.FromArgb(85, 89, 105),
            Location = new Point(31, 78),
        };
        var move = Button("Переместить", 32, (_, _) =>
        {
            Location = new Point(Location.X + 120, Location.Y - 60);
            Log("moved");
        });
        var resize = Button("Изменить размер", 184, (_, _) =>
        {
            Size = new Size(Math.Max(360, Width - 180), Height + 60);
            Log("resized");
        });
        var hide = Button("Скрыть на 2 сек", 336, async (_, _) =>
        {
            Log("hidden");
            Hide();
            await Task.Delay(2_000);
            Show();
            Log("shown");
        });
        var close = Button("Закрыть", 488, (_, _) => Close());
        Controls.AddRange([title, instructions, move, resize, hide, close]);

        Shown += (_, _) => Log("shown");
        Move += (_, _) => Log("location_changed");
        Resize += (_, _) => Log("size_changed");
        FormClosed += (_, _) => Log("closed");

        if (automated)
        {
            _automationTimer = new System.Windows.Forms.Timer { Interval = 1_000 };
            _automationTimer.Tick += (_, _) => RunAutomationStep();
            _automationTimer.Start();
        }
    }

    private Button Button(string text, int x, EventHandler click)
    {
        var button = new Button
        {
            Text = text,
            Location = new Point(x, 150),
            Size = new Size(136, 42),
            FlatStyle = FlatStyle.System,
        };
        button.Click += click;
        return button;
    }

    private void RunAutomationStep()
    {
        _automationStep++;
        switch (_automationStep)
        {
            case 1:
                Location = new Point(Location.X + 160, Location.Y - 100);
                Log("automation_move");
                break;
            case 2:
                Size = new Size(480, 520);
                Log("automation_resize");
                break;
            case 3:
                WindowState = FormWindowState.Minimized;
                Log("automation_minimize");
                break;
            case 4:
                WindowState = FormWindowState.Normal;
                Log("automation_restore");
                break;
            default:
                _automationTimer!.Stop();
                Close();
                break;
        }
    }

    private void Log(string eventName)
    {
        var entry = new
        {
            timestamp = DateTimeOffset.UtcNow,
            event_name = eventName,
            hwnd = IsHandleCreated ? $"0x{Handle.ToInt64():X}" : null,
            bounds = Bounds.ToString(),
        };
        File.AppendAllText(_logPath, JsonSerializer.Serialize(entry) + Environment.NewLine);
    }
}
