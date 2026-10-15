using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace GeurtsPerformancePrefect;

public sealed record DiskCleanupLaunch(bool Started, string Message);

public static class DiskCleanup
{
    internal static ProcessStartInfo StartInfo(string root)
    {
        var drive = DiskCheck.VolumeArgument(root)[..1];
        var info = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cleanmgr.exe"))
        { UseShellExecute = true, WorkingDirectory = Environment.SystemDirectory };
        // Open Windows' review dialog for this drive. Never use automatic cleanup/profile switches.
        info.ArgumentList.Add("/d"); info.ArgumentList.Add(drive);
        return info;
    }

    public static DiskCleanupLaunch Open(string root, Func<ProcessStartInfo, bool>? launch = null, Func<string, bool>? exists = null)
    {
        try
        {
            var info = StartInfo(root);
            if (!(exists ?? File.Exists)(info.FileName))
                return new(false, "Windows Disk Cleanup is not available. Open Windows Settings → System → Storage to review cleanup options.");
            bool started;
            if (launch != null) started = launch(info);
            else { using var process = Process.Start(info); started = process != null; }
            return started
                ? new(true, "Windows Disk Cleanup opened for " + DiskCheck.VolumeArgument(root) + ". Review the file categories and confirm deletion there. Prefect does not track the cleanup result.")
                : new(false, "Windows did not open Disk Cleanup. Try again or use Windows Settings → System → Storage.");
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        { return new(false, "Administrator approval cancelled. Disk Cleanup was not opened."); }
        catch (Exception ex) when (ex is Win32Exception or IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        { return new(false, "Could not open Disk Cleanup: " + ex.Message); }
    }
}
