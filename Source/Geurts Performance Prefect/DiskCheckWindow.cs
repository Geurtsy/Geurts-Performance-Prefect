using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace GeurtsPerformancePrefect;

// Retain the explicit diagnostic command-line entry points using the same embedded control.
public sealed class DiskCheckWindow : Window
{
    internal DiagnosticConsole Console { get; }
    public DiskCheckWindow(bool integrity = false, bool repair = false, bool windowsImage = false)
        : this(new DiagnosticConsole(integrity, repair, windowsImage)) { }
    internal DiskCheckWindow(IReadOnlyList<DiskCheckVolume> volumes,
        Func<string, Action<string>, CancellationToken, Task<int>> run,
        Func<Action<string>, CancellationToken, Task<int>>? integrityRun = null, bool integrity = false,
        Func<string, Action<string>, Action<Func<string, Task>?>, Task<int>>? repairRun = null,
        Func<string, bool>? confirmRepair = null, bool repair = false,
        Func<WindowsImageAction, Action<string>, Task<int>>? imageRun = null,
        Func<bool>? confirmImageRepair = null, bool windowsImage = false)
        : this(new DiagnosticConsole(volumes, run, integrityRun, integrity, repairRun, confirmRepair, repair,
            imageRun, confirmImageRepair, windowsImage)) { }
    DiskCheckWindow(DiagnosticConsole console)
    {
        Console = console; Content = console; console.Margin = new Thickness(20);
        Title = "Geurts Performance Prefect · Diagnostic console"; Icon = Branding.WindowIcon;
        Width = 850; Height = 640; MinWidth = 560;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        void UpdateMinimum() => MinHeight = console.CheckType.SelectedIndex == 2 ? 650 : 520;
        console.CheckType.SelectionChanged += (_, _) => UpdateMinimum(); UpdateMinimum();
        Closing += (_, e) => e.Cancel = !Console.RequestClose(Close);
    }
}
