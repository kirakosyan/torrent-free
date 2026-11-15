namespace TorrentFree.ViewModels
{
    internal enum DevicePlatform { WinUI, Other }
    internal static class DeviceInfo { public static DevicePlatform Platform => DevicePlatform.Other; }
}

namespace TorrentFree.Services
{
    // Only the platform log sink is substituted; tests execute the production view-model code.
    internal static class AppDiagnostics
    {
        public static void Record(string source, Exception? exception = null, string? details = null) { }
    }
}
