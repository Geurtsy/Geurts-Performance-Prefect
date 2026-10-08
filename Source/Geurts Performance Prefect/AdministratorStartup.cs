using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace GeurtsPerformancePrefect;

public static class AdministratorStartup
{
    internal const string RelaunchArgument = "--administrator-relaunch";

    internal static bool ShouldElevate(OverlaySettings settings, bool isAdministrator, string[] arguments)
        => settings.StartAsAdministrator && !isAdministrator && !arguments.Contains(RelaunchArgument);

    internal static (bool Started, string? Error) Relaunch(string[] arguments, int parentProcess,
        Func<ProcessStartInfo, bool>? start = null, string? executable = null)
    {
        try
        {
            executable ??= Environment.ProcessPath;
            if (executable == null || !Path.IsPathFullyQualified(executable) || !File.Exists(executable) ||
                !executable.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                throw new IOException("The application executable could not be found. Extract the release ZIP before starting it.");
            var info = new ProcessStartInfo(executable) { UseShellExecute = true, Verb = "runas", WorkingDirectory = AppContext.BaseDirectory };
            // The child waits until this process releases the single-instance mutex.
            info.ArgumentList.Add("--wait-for");
            info.ArgumentList.Add(parentProcess.ToString(System.Globalization.CultureInfo.InvariantCulture));
            info.ArgumentList.Add(RelaunchArgument);
            var forwarded = arguments.Length >= 2 && arguments[0] == "--wait-for" ? arguments.Skip(2) : arguments;
            foreach (var argument in forwarded) info.ArgumentList.Add(argument);
            bool started;
            if (start != null) started = start(info);
            else { using var child = Process.Start(info); started = child != null; }
            if (!started) throw new IOException("Windows did not start the administrator session.");
            return (true, null);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return (false, "Administrator approval was cancelled. The app will continue with normal permissions. Your startup preference is still saved for the next launch.");
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return (false, "Could not start as administrator. The app will continue with normal permissions. " + ex.Message);
        }
    }
}
