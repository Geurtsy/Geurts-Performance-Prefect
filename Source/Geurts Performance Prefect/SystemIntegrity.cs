using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GeurtsPerformancePrefect;

public static class SystemIntegrity
{
    internal static ProcessStartInfo ScanInfo()
    {
        // SFC writes UTF-16 output to redirected handles. Never run through a shell.
        var info = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "sfc.exe"))
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true,
            RedirectStandardError = true, RedirectStandardInput = true,
            StandardOutputEncoding = Encoding.Unicode, StandardErrorEncoding = Encoding.Unicode,
            WorkingDirectory = Environment.SystemDirectory
        };
        info.ArgumentList.Add("/verifyonly");
        return info;
    }

    public static Task<int> RunAsync(Action<string> output, CancellationToken stop)
        => DiskCheck.RunProcessAsync(ScanInfo(), output, stop);

    // SFC's exit status alone does not establish that Windows is healthy, and output is localized.
    internal static string Result(int code) => $"System integrity scan exited with code {code}. Review Windows Resource Protection's findings in the console. No repairs were requested.";
}
