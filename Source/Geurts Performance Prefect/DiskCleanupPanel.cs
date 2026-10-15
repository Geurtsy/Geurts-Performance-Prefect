using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace GeurtsPerformancePrefect;

public sealed class DiskCleanupPanel : StackPanel
{
    internal ComboBox Drive { get; } = new();
    internal Button OpenButton { get; } = new() { Content = "Open Disk Cleanup…", HorizontalAlignment = HorizontalAlignment.Left };
    internal Button RefreshButton { get; } = new() { Content = "Refresh drives", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0,8,0,0) };
    internal TextBlock Status { get; } = Note("");
    readonly Func<IReadOnlyList<DiskCheckVolume>> discover;
    readonly Func<string, DiskCleanupLaunch> open;

    public DiskCleanupPanel(Func<IReadOnlyList<DiskCheckVolume>>? discover = null, Func<string, DiskCleanupLaunch>? open = null)
    {
        this.discover = discover ?? DiskCheck.Volumes;
        this.open = open ?? (root => DiskCleanup.Open(root));
        TextBlock.SetForeground(this, Palette.Text);
        Children.Add(new TextBlock { Text = "Disk Cleanup", FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0,14,0,0) });
        Children.Add(Note("Free space using Windows Disk Cleanup. Choose a drive, then review the file categories and space estimates in its separate window. Delete files only after reviewing the selection."));
        Children.Add(new TextBlock { Text = "Drive to clean", Foreground = Palette.Text, Margin = new Thickness(0,0,0,6) });
        Children.Add(Drive);
        Children.Add(RefreshButton);
        OpenButton.Margin = new Thickness(0,8,0,0); Children.Add(OpenButton);
        Children.Add(Status);
        Children.Add(Note("Nothing runs automatically. Windows may ask for administrator approval for system files. Deletion can be permanent; check categories such as Recycle Bin and previous Windows installations carefully."));
        System.Windows.Automation.AutomationProperties.SetName(Drive, "Drive for Disk Cleanup");
        RefreshButton.Click += (_, _) => Refresh();
        Drive.SelectionChanged += (_, _) =>
        {
            OpenButton.IsEnabled = Drive.SelectedItem is DiskCheckVolume;
            if (Drive.SelectedItem is DiskCheckVolume volume) Status.Text = "Ready to open Disk Cleanup for " + volume.Root;
        };
        OpenButton.Click += (_, _) =>
        {
            if (Drive.SelectedItem is not DiskCheckVolume volume) return;
            try { Status.Text = this.open(volume.Root).Message; }
            catch (Exception ex) { Status.Text = "Could not open Disk Cleanup: " + ex.Message; }
        };
        Refresh();
    }

    void Refresh()
    {
        var previous = (Drive.SelectedItem as DiskCheckVolume)?.Root ?? Path.GetPathRoot(Environment.SystemDirectory);
        try
        {
            var volumes = discover();
            Drive.ItemsSource = volumes;
            Drive.SelectedItem = volumes.FirstOrDefault(v => string.Equals(v.Root, previous, StringComparison.OrdinalIgnoreCase)) ?? volumes.FirstOrDefault();
            OpenButton.IsEnabled = Drive.SelectedItem != null;
            Status.Text = Drive.SelectedItem is DiskCheckVolume volume ? "Ready to open Disk Cleanup for " + volume.Root
                : "No ready local drives are available. Connect or unlock a drive, then refresh.";
        }
        catch (Exception ex)
        {
            Drive.ItemsSource = Array.Empty<DiskCheckVolume>(); OpenButton.IsEnabled = false;
            Status.Text = "Could not load drives: " + ex.Message + " Try Refresh drives.";
        }
    }

    static TextBlock Note(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap,
        Foreground = Palette.Muted, FontSize = 12, Margin = new Thickness(0,7,0,7) };
}
