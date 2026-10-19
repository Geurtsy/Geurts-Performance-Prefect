using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Interop;

namespace GeurtsPerformancePrefect;

internal static class UninstallForegroundVerification
{
    internal static void Run(ProgramsFeaturesHost host, Window owner, string folder, Action<bool,string> check)
    {
        // Use real fixture processes/windows and process ancestry, but capture focus
        // requests instead of changing the user's foreground application.
        nint foreground = new WindowInteropHelper(owner).Handle;
        var currentTime = DateTime.UtcNow;
        var requests = new List<nint>();
        Process Start(string name)
        {
            var info = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden };
            info.ArgumentList.Add("--programs-features-dialog-fixture");
            info.ArgumentList.Add(Path.Combine(folder, name + ".txt"));
            return Process.Start(info) ?? throw new InvalidOperationException("Fixture did not start.");
        }
        nint WaitForWindow(Process process)
        {
            DiskAnalyserVerification.WaitFor(() => { process.Refresh(); return process.MainWindowHandle != 0; }, "background fixture window");
            return process.MainWindowHandle;
        }
        void Close(Process? process)
        {
            if (process == null) return;
            if (!process.HasExited)
            {
                process.CloseMainWindow();
                DiskAnalyserVerification.WaitFor(() => process.HasExited, "owned dialog fixture close");
            }
            process.Dispose();
        }

        Process? existing = null, launched = null;
        var handoff = new UninstallForegroundHandoff(host.Handle, () => foreground,
            window => { requests.Add(window); return true; }, () => currentTime);
        try
        {
            existing = Start("foreground-preexisting-fixture");
            var existingWindow = WaitForWindow(existing);
            currentTime = DateTime.UtcNow;
            handoff.Begin();
            check(handoff.IsArmed, "Explicit input starts the bounded dialog handoff");
            launched = Start("foreground-new-fixture");
            var newWindow = WaitForWindow(launched);
            DiskAnalyserVerification.WaitFor(() => { handoff.Poll(); return requests.Contains(newWindow); }, "new child dialog handoff");
            check(handoff.LastRaisedWindow == newWindow && !requests.Contains(existingWindow),
                "Real process ancestry routes a new child dialog to foreground while leaving existing windows alone");
            var count = requests.Count;
            handoff.Poll(); handoff.Poll();
            check(requests.Count == count, "An already handled dialog is not repeatedly raised");
            currentTime = currentTime.AddSeconds(31); handoff.Poll();
            check(!handoff.IsArmed, "The automatic handoff expires after thirty seconds");

            currentTime = DateTime.UtcNow;
            handoff.Begin();
            foreground = existingWindow;
            handoff.Poll();
            check(!handoff.IsArmed && requests.Count == count,
                "Switching to a pre-existing process cancels the handoff without requesting focus");
        }
        finally { handoff.Dispose(); Close(launched); Close(existing); }
        check(!handoff.IsArmed && !handoff.IsHooked, "Disposing a foreground handoff removes its timer and input hook");
    }
}
