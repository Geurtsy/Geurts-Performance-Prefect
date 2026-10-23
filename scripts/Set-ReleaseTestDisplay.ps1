$ErrorActionPreference = 'Stop'
if ($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted') {
    throw 'Display preparation is for the disposable GitHub-hosted CI runner only.'
}
# Native HUD expansion tests need the same full-size desktop as local validation.
# This script cannot run against the user's development or installed-app display.
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class ReleaseTestDisplay
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct Mode
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Device;
        public ushort SpecVersion, DriverVersion, Size, DriverExtra;
        public uint Fields;
        public int PositionX, PositionY;
        public uint Orientation, FixedOutput;
        public short Color, Duplex, Resolution, TrueType, Collate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Form;
        public ushort LogPixels;
        public uint BitsPerPixel, Width, Height, Flags, Frequency, IcmMethod, IcmIntent, MediaType, DitherType, Reserved1, Reserved2, PanningWidth, PanningHeight;
    }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern bool EnumDisplaySettings(string device, int index, ref Mode mode);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int ChangeDisplaySettings(ref Mode mode, uint flags);
    [DllImport("user32.dll")] public static extern int GetSystemMetrics(int index);
    public static void Prepare()
    {
        int width = GetSystemMetrics(0), height = GetSystemMetrics(1);
        Console.WriteLine("Runner desktop before tests: " + width + " x " + height);
        if (width >= 1920 && height >= 1080) return;
        var mode = new Mode { Size = (ushort)Marshal.SizeOf<Mode>() };
        if (!EnumDisplaySettings(null, -1, ref mode)) throw new InvalidOperationException("Cannot read the CI display mode.");
        mode.Width = 1920; mode.Height = 1080; mode.Fields = 0x180000;
        int result = ChangeDisplaySettings(ref mode, 0);
        if (result != 0) throw new InvalidOperationException("Cannot prepare the CI test display: " + result);
    }
}
'@
[ReleaseTestDisplay]::Prepare()
Start-Sleep -Seconds 1
$width = [ReleaseTestDisplay]::GetSystemMetrics(0); $height = [ReleaseTestDisplay]::GetSystemMetrics(1)
if ($width -lt 1920 -or $height -lt 1080) { throw "CI desktop is too small for native HUD expansion tests: $width x $height" }
Write-Output "Runner desktop ready for native UI tests: $width x $height"
