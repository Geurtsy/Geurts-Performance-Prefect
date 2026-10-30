using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace GeurtsPerformancePrefect;

public enum DiskOptimizationAction { Analyze, Optimize }

public static class DiskOptimization
{
    internal static ProcessStartInfo Info(string root, DiskOptimizationAction action)
    {
        if (!Enum.IsDefined(action)) throw new ArgumentOutOfRangeException(nameof(action));
        var info = DiskCheck.ScanInfo(root);
        info.FileName = Path.Combine(Environment.SystemDirectory, "defrag.exe");
        // /O lets Windows choose the operation for the media; never force traditional SSD defrag.
        info.ArgumentList.Add(action == DiskOptimizationAction.Analyze ? "/A" : "/O");
        info.ArgumentList.Add("/U");
        info.ArgumentList.Add("/V");
        return info;
    }

    public static Task<int> RunAsync(string root, DiskOptimizationAction action, Action<string> output, CancellationToken stop)
    {
        var info = Info(root, action);
        return action == DiskOptimizationAction.Analyze ? DiskCheck.RunProcessAsync(info, output, stop)
            : WindowsImage.RunProcessAsync(info, output, stop);
    }

    internal static string Confirmation(string root) => "Optimize " + DiskCheck.VolumeArgument(root) + " with Windows?\n\n" +
        "Windows will choose the appropriate optimization for this drive's media type, including SSDs. " +
        "This changes the selected drive and may take some time. Save your work and keep Prefect open until it finishes. " +
        "Exiting Prefect stops its command and may leave optimization incomplete. No restart or schedule change is requested.";

    internal static string Result(DiskOptimizationAction action, int code) => code == 0
        ? (action == DiskOptimizationAction.Analyze ? "Fragmentation analysis completed." : "Drive optimization completed.") + " Review Windows' full report below."
        : $"Windows drive {(action == DiskOptimizationAction.Analyze ? "analysis" : "optimization")} exited with code {code} (0x{unchecked((uint)code):X8}). " +
            "Review the console output; the operation may be incomplete or unsupported for this drive.";
}
