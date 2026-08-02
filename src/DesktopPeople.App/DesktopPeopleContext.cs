using DesktopPeople.Core;
using DesktopPeople.Core.Platforms;
using DesktopPeople.Windows;

namespace DesktopPeople.App;

internal sealed class DesktopPeopleContext : ApplicationContext
{
    private readonly SettingsStore _settingsStore;
    private readonly JsonLineLogger _logger;
    private readonly LauncherForm _launcher;
    private readonly OverlayForm _overlay;
    private readonly WindowsWindowPlatformProvider _windowPlatforms;
    private readonly NotifyIcon _tray;
    private readonly ToolStripMenuItem _visibilityItem;
    private readonly ToolStripMenuItem _pauseItem;
    private readonly Dictionary<string, ToolStripMenuItem> _intensityItems = [];
    private AppSettings _settings;
    private bool _exiting;

    public DesktopPeopleContext(SettingsStore settingsStore, JsonLineLogger logger)
    {
        _settingsStore = settingsStore;
        _logger = logger;
        _settings = settingsStore.Load();
        _launcher = new LauncherForm();
        var registry = new PlatformRegistry();
        _windowPlatforms = new WindowsWindowPlatformProvider(
            new Win32WindowApi(),
            new Win32WindowEventSource(),
            registry,
            Environment.ProcessId);
        _windowPlatforms.MetricsUpdated += metrics =>
        {
            if (metrics.WasReconciliation)
            {
                _logger.Write("platform_snapshot_updated", new
                {
                    enumerated_windows = metrics.EnumeratedWindowCount,
                    platforms = metrics.PlatformCount,
                    reconciliation_interval_ms = metrics.ReconciliationInterval.TotalMilliseconds,
                    update_duration_ms = metrics.UpdateDuration.TotalMilliseconds,
                    average_update_duration_ms = metrics.AverageUpdateDuration.TotalMilliseconds,
                });
            }
        };
        _overlay = new OverlayForm(logger, _settings.TargetFps, _windowPlatforms)
        {
            IsPaused = _settings.IsPaused,
            ShowPlatformDebug = _settings.ShowPlatformDebug,
            BehaviorIntensity = _settings.BehaviorIntensity,
            CharacterScale = _settings.CharacterScale,
        };
        _launcher.SelectedIntensity = _settings.BehaviorIntensity;
        _launcher.SelectedScalePercent = (int)Math.Round(_settings.CharacterScale * 100);
        _launcher.ReleaseRequested += (_, _) =>
        {
            ShowCharacters();
            _launcher.Hide();
        };
        _launcher.IntensityChanged += SetIntensity;
        _launcher.ScaleChanged += SetScale;

        var menu = new ContextMenuStrip();
        menu.Items.Add("Открыть DesktopPeople", null, (_, _) => OpenLauncher());
        _visibilityItem = new ToolStripMenuItem("Показать персонажа")
        {
            Checked = _settings.CharactersVisible,
            CheckOnClick = true,
        };
        _visibilityItem.Click += (_, _) => ToggleCharacters();
        menu.Items.Add(_visibilityItem);

        _pauseItem = new ToolStripMenuItem("Пауза")
        {
            Checked = _settings.IsPaused,
            CheckOnClick = true,
        };
        _pauseItem.Click += (_, _) => TogglePause();
        menu.Items.Add(_pauseItem);
        menu.Items.Add(BuildIntensityMenu());
#if DEBUG
        var debugItem = new ToolStripMenuItem("Developer: платформы")
        {
            Checked = _settings.ShowPlatformDebug,
            CheckOnClick = true,
        };
        debugItem.Click += (_, _) =>
        {
            _overlay.ShowPlatformDebug = debugItem.Checked;
            SaveSettings(_settings with { ShowPlatformDebug = debugItem.Checked });
        };
        menu.Items.Add(debugItem);
#endif
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Завершить", null, (_, _) => ExitApplication());

        _tray = new NotifyIcon
        {
            Text = "DesktopPeople",
            Icon = SystemIcons.Application,
            ContextMenuStrip = menu,
            Visible = true,
        };
        _tray.DoubleClick += (_, _) => OpenLauncher();

        // The character only appears after the user presses "Release" (or the tray
        // toggle) this session — never automatically on launch, even if it was
        // visible when the app last closed.
        _launcher.Show();

        _windowPlatforms.SetExplicitlyExcludedHandles(
        [
            _launcher.Handle.ToInt64(),
            _overlay.IsHandleCreated ? _overlay.Handle.ToInt64() : 0,
        ]);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _tray.Visible = false;
            _tray.Dispose();
            _windowPlatforms.Dispose();
            _overlay.Dispose();
            _launcher.Dispose();
        }

        base.Dispose(disposing);
    }

    private void OpenLauncher()
    {
        _launcher.Show();
        _launcher.WindowState = FormWindowState.Normal;
        _launcher.Activate();
    }

    private void ToggleCharacters()
    {
        if (_visibilityItem.Checked)
        {
            ShowCharacters();
        }
        else
        {
            _overlay.HideOverlay();
            SaveSettings(_settings with { CharactersVisible = false });
        }
    }

    private void ShowCharacters()
    {
        _visibilityItem.Checked = true;
        _overlay.ShowOverlay();
        SaveSettings(_settings with { CharactersVisible = true });
    }

    private void TogglePause()
    {
        _overlay.IsPaused = _pauseItem.Checked;
        SaveSettings(_settings with { IsPaused = _pauseItem.Checked });
        _logger.Write("pause_changed", new { paused = _pauseItem.Checked });
    }

    private ToolStripMenuItem BuildIntensityMenu()
    {
        var root = new ToolStripMenuItem("Активность");
        AddIntensityOption(root, "calm", "Спокойно");
        AddIntensityOption(root, "normal", "Обычно");
        AddIntensityOption(root, "active", "Активно");
        return root;
    }

    private void AddIntensityOption(ToolStripMenuItem root, string intensity, string label)
    {
        var item = new ToolStripMenuItem(label) { Checked = _settings.BehaviorIntensity == intensity };
        item.Click += (_, _) => SetIntensity(intensity);
        _intensityItems[intensity] = item;
        root.DropDownItems.Add(item);
    }

    private void SetIntensity(string intensity)
    {
        foreach ((string key, ToolStripMenuItem item) in _intensityItems)
        {
            item.Checked = key == intensity;
        }

        _launcher.SelectedIntensity = intensity;
        _overlay.BehaviorIntensity = intensity;
        SaveSettings(_settings with { BehaviorIntensity = intensity });
        _logger.Write("behavior_intensity_changed", new { intensity });
    }

    private void SetScale(int percent)
    {
        double scale = percent / 100.0;
        _overlay.CharacterScale = scale;
        SaveSettings(_settings with { CharacterScale = scale });
        _logger.Write("character_scale_changed", new { percent });
    }

    private void SaveSettings(AppSettings settings)
    {
        _settings = settings;
        try
        {
            _settingsStore.Save(settings);
        }
        catch (IOException exception)
        {
            _logger.Write("settings_save_failed", new { error = exception.Message });
        }
    }

    private void ExitApplication()
    {
        if (_exiting)
        {
            return;
        }

        _exiting = true;
        _logger.Write("application_exiting");
        _tray.Visible = false;
        _overlay.Close();
        _launcher.CloseForExit();
        ExitThread();
    }
}
