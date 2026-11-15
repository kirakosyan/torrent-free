using System.Reflection;
using System.Runtime.InteropServices;

namespace TorrentFree.Services;

internal static class AppDiagnostics
{
    private static readonly Lazy<DiagnosticLog> Log = new(() => new DiagnosticLog(
        Path.Combine(GetDirectory(), "crash.log"),
        $"app={typeof(App).Assembly.GetName().Version}; maui={typeof(Application).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion}; " +
        $"runtime={RuntimeInformation.FrameworkDescription}; os={RuntimeInformation.OSDescription}; " +
        $"architecture={RuntimeInformation.ProcessArchitecture}; process={Environment.ProcessId}"));
    private static int _registered;

    private static string GetDirectory()
    {
#if DEBUG && WINDOWS
        if (Environment.GetEnvironmentVariable("TORRENTFREE_DATA_DIR") is { Length: > 0 } isolated) return isolated;
#endif
        return OperatingSystem.IsWindows()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TorrentFree")
            : FileSystem.AppDataDirectory;
    }

    public static void Initialize()
    {
        if (Interlocked.Exchange(ref _registered, 1) != 0) return;
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Record("AppDomain.UnhandledException", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Record("TaskScheduler.UnobservedTaskException", e.Exception);
            e.SetObserved();
        };
        Record("Session.Start");
    }

    public static void State(string state)
    {
        try { Log.Value.Breadcrumb(state); } catch { }
    }

    public static void Record(string source, Exception? exception = null, string? details = null)
    {
        try { Log.Value.Write(source, exception, details); } catch { }
    }
}
