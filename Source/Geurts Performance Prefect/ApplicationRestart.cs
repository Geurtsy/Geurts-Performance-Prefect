using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace GeurtsPerformancePrefect;

public static class ApplicationRestart
{
    internal static (bool Started, string? Error) Relaunch(SettingsStore store, OverlaySettings settings,
        Func<ProcessStartInfo, bool>? start = null, string? executable = null)
    {
        if (!store.Save(settings)) return (false, "Preferences could not be saved. " + store.LastError);
        try
        {
            executable ??= Environment.ProcessPath;
            if (executable == null || !Path.IsPathFullyQualified(executable) || !File.Exists(executable) ||
                !executable.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                throw new IOException("The application executable could not be found. Extract the release ZIP before starting it.");
            var info = new ProcessStartInfo(executable) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(executable)! };
            // Start a fresh session only after this process releases the single-instance mutex.
            info.ArgumentList.Add("--wait-for");
            info.ArgumentList.Add(Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
            bool started;
            if (start != null) started = start(info);
            else { using var child = Process.Start(info); started = child != null; }
            if (!started) throw new IOException("Windows did not start the replacement application.");
            return (true, null);
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return (false, ex.Message);
        }
    }
}
