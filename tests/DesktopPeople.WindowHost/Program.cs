namespace DesktopPeople.WindowHost;

internal static class Program
{
    [STAThread]
    private static void Main(string[] arguments)
    {
        ApplicationConfiguration.Initialize();
        bool automated = arguments.Contains("--automated", StringComparer.OrdinalIgnoreCase);
        Application.Run(new PlatformTestForm(automated));
    }
}
