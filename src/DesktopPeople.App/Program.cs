using DesktopPeople.Core;

namespace DesktopPeople.App;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        string dataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DesktopPeople");

        var logger = new JsonLineLogger(Path.Combine(dataDirectory, "logs"));
        logger.Write("application_started", new { version = Application.ProductVersion });

        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, args) =>
            logger.Write("unhandled_ui_exception", new { error = args.Exception.ToString() });
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            logger.Write("unhandled_exception", new { error = args.ExceptionObject?.ToString() });

        var settingsStore = new SettingsStore(Path.Combine(dataDirectory, "settings.json"));

        Application.Run(new DesktopPeopleContext(settingsStore, logger));
    }
}

