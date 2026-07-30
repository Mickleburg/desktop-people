using DesktopPeople.Core;

namespace DesktopPeople.App;

internal sealed class DesktopPeopleContext : ApplicationContext
{
    private readonly SettingsStore _settingsStore;
    private readonly JsonLineLogger _logger;
    private readonly LauncherForm _launcher;
    private readonly OverlayForm _overlay;
    private readonly NotifyIcon _tray;
    private readonly ToolStripMenuItem _visibilityItem;
    private readonly ToolStripMenuItem _pauseItem;
    private AppSettings _settings;
    private bool _exiting;

    public DesktopPeopleContext(SettingsStore settingsStore, JsonLineLogger logger)
    {
        _settingsStore = settingsStore;
        _logger = logger;
        _settings = settingsStore.Load();
        _launcher = new LauncherForm();
        _overlay = new OverlayForm(logger, _settings.TargetFps)
        {
            IsPaused = _settings.IsPaused,
        };
        _launcher.ReleaseRequested += (_, _) =>
        {
            ShowCharacters();
            _launcher.Hide();
        };

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

        _launcher.Show();
        if (_settings.CharactersVisible)
        {
            _overlay.ShowOverlay();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _tray.Visible = false;
            _tray.Dispose();
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

