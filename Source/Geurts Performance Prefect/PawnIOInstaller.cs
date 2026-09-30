using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;

namespace GeurtsPerformancePrefect;

internal static class PawnIOInstaller
{
    const string InstallerHash = "1F519A22E47187F70A1379A48CA604981C4FCF694F4E65B734AAA74A9FBA3032";

    internal static bool IsInstalled()
    {
        using var service = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\PawnIO");
        return service != null;
    }

    internal static async Task InstallIfMissingAsync()
    {
        try
        {
            if (IsInstalled()) return;
            var installer = Path.Combine(AppContext.BaseDirectory, "PawnIO_setup.exe");
            if (!File.Exists(installer)) throw new FileNotFoundException("The bundled PawnIO installer is missing.", installer);
            var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(installer)));
            if (!hash.Equals(InstallerHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The bundled PawnIO installer did not pass its integrity check.");

            using var process = Process.Start(new ProcessStartInfo(installer, "-install -silent")
            {
                UseShellExecute = true,
                Verb = "runas"
            }) ?? throw new InvalidOperationException("PawnIO setup could not be started.");
            await process.WaitForExitAsync();
            if (process.ExitCode != 0 && process.ExitCode != 3010)
                throw new InvalidOperationException($"PawnIO setup exited with code {process.ExitCode}.");
            if (!IsInstalled()) throw new InvalidOperationException("PawnIO setup finished, but the driver was not found.");
            MessageBox.Show(process.ExitCode == 3010
                ? "PawnIO was installed. Restart Windows to finish installation."
                : "PawnIO was installed. Restart Geurts Performance Prefect as administrator to use supported temperature sensors.",
                "Geurts Performance Prefect · PawnIO");
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            // Windows elevation was declined; try again on the next launch.
        }
        catch (Exception ex)
        {
            MessageBox.Show("PawnIO could not be installed: " + ex.Message,
                "Geurts Performance Prefect · PawnIO");
        }
    }
}
