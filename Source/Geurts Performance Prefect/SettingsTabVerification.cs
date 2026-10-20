using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GeurtsPerformancePrefect;

public static class SettingsTabVerification
{
    internal static ScrollViewer Select(SettingsWindow panel, FrameworkElement control)
    {
        DependencyObject parent = control;
        while (parent is not TabItem && parent != null) parent = LogicalTreeHelper.GetParent(parent);
        var tab = (TabItem)(parent ?? throw new InvalidOperationException("Control is not in a settings tab."));
        panel.Sections.SelectedItem = tab;
        panel.Hud.RevealControl(control);
        panel.UpdateLayout();
        panel.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        return (ScrollViewer)tab.Content;
    }

    internal static void Run(SettingsWindow panel, string folder, Action<bool, string> check)
    {
        panel.Show();
        var tabs = panel.Sections.Items.Cast<TabItem>().ToArray();
        check(tabs.Length == 11 && tabs.Select(t => t.Header).Distinct().Count() == 11 && tabs.All(t => (string)t.Header != "Downloads cleanup"),
            "All eleven Settings sections have distinct tabs and Downloads cleanup has no separate tab");
        var compressionScroll = Select(panel, panel.FileCompressionPanel.PreviewButton);
        check((string)((TabItem)panel.Sections.SelectedItem).Header == "File compression" && !panel.FileCompressionPanel.IsBusy && !panel.FileCompressionPanel.CompressButton.IsEnabled,
            "File compression opens idle in its own tab and requires a preview before changes");
        var compressionFixture = Path.Combine(folder, "compression-lifetime.txt"); File.WriteAllText(compressionFixture, "disposable lifetime fixture");
        panel.FileCompressionPanel.SelectPaths(new[] { compressionFixture });
        var compressionWork = panel.FileCompressionPanel.PreviewAsync();
        check(!panel.AllowRestart() && !panel.RequestApplicationExit(), "An active file preview blocks application restart, update and exit");
        panel.Close();
        check(panel.IsVisible, "Closing Settings waits for file compression work to finish or stop");
        panel.FileCompressionPanel.Stop();
        var timeout = System.Diagnostics.Stopwatch.StartNew();
        while (!compressionWork.IsCompleted)
        {
            if (timeout.Elapsed > TimeSpan.FromSeconds(30)) throw new TimeoutException("Compression lifetime fixture timed out.");
            panel.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            System.Threading.Thread.Sleep(5);
        }
        compressionWork.GetAwaiter().GetResult();
        check(!panel.FileCompressionPanel.IsBusy && File.ReadAllText(compressionFixture) == "disposable lifetime fixture",
            "Stopping the preview releases lifetime guards without altering fixture contents");
        var storageScroll = Select(panel, panel.StorageSensePanel.PreviewButton);
        check((string)((TabItem)panel.Sections.SelectedItem).Header == "Storage Sense" && !panel.StorageSensePanel.Enabled.IsChecked!.Value,
            "Storage Sense has its own idle tab and automatic cleanup defaults off");
        check(Select(panel, panel.WipeDownloadsButton) == storageScroll && Select(panel, panel.AutoWipeDownloadsToggle) == storageScroll &&
            Select(panel, panel.DownloadsPath) == storageScroll && Select(panel, panel.DownloadsStatus) == storageScroll,
            "Downloads path, manual cleanup, startup toggle and results are grouped in Storage Sense");
        Select(panel, panel.DiskAnalyserPanel.AnalyseButton);
        check((string)((TabItem)panel.Sections.SelectedItem).Header == "Disk analyser" && !panel.DiskAnalyserPanel.IsActive,
            "Disk analyser opens idle in its own tab without starting WinDirStat");
        Select(panel, panel.StartAsAdministratorToggle);
        check((string)((TabItem)panel.Sections.SelectedItem).Header == "Security",
            "Administrator startup is in the Security tab");
        Select(panel, panel.SecurityPanel.DisableUacButton);
        check((string)((TabItem)panel.Sections.SelectedItem).Header == "Security",
            "Windows-wide UAC mode and restore controls are in the Security tab");
        var securityScroll = Select(panel, panel.FirewallPanel.DisableButton);
        check((string)((TabItem)panel.Sections.SelectedItem).Header == "Security",
            "Windows Firewall disable and restore actions are in the Security tab");
        Select(panel, panel.BitLockerPanel.DecryptButton);
        check((string)((TabItem)panel.Sections.SelectedItem).Header == "Security",
            "BitLocker drive selection and decryption are in the Security tab");
        check(panel.WindowState == WindowState.Maximized && panel.ResizeMode == ResizeMode.CanMinimize,
            "Settings opens full-screen with native minimize and close controls");
        Select(panel, panel.Console.CheckType);
        check((string)((TabItem)panel.Sections.SelectedItem).Header == "Console" && panel.Console.CheckType.Items.Count == 8,
            "Console tab embeds disk checks, fragmentation analysis, optimization, system integrity and Windows image modes");
        Select(panel, panel.DiskCleanupPanel.OpenButton);
        check((string)((TabItem)panel.Sections.SelectedItem).Header == "Performance",
            "Disk Cleanup drive selection and launch are in Performance settings");
        Select(panel, panel.VisualEffectsPanel.ApplyButton);
        check((string)((TabItem)panel.Sections.SelectedItem).Header == "Performance",
            "Windows aesthetics and its one-click best performance action are in Performance settings");
        var readings = Select(panel, panel.MetricToggles.Values.First());
        check((string)((TabItem)panel.Sections.SelectedItem).Header == "Overlay" &&
            Select(panel, panel.ApplicationAverageSlider) == readings &&
            Select(panel, panel.OverlayMonitorSelector) == readings &&
            Select(panel, panel.AutoAvoidToggle) == readings &&
            Select(panel, panel.MinimiseToTrayToggle) == readings &&
            Select(panel, panel.TopmostToggle) == readings &&
            panel.DriveToggles.Values.All(toggle => Select(panel, toggle) == readings),
            "Overlay contains the metric, hard drive, top application and window controls");
        foreach (var width in new[] { SystemParameters.WorkArea.Width })
        {
            foreach (var tab in tabs)
            {
                var peer = new TabItemAutomationPeer(tab, new TabControlAutomationPeer(panel.Sections));
                ((ISelectionItemProvider)peer.GetPattern(PatternInterface.SelectionItem)).Select();
                Capture(panel, Path.Combine(folder, "settings-tab-" + tabs.ToList().IndexOf(tab) + "-" + width + ".png"), width, 920);
                var selected = (ScrollViewer)tab.Content;
                check(tab.IsSelected && selected.IsVisible && tabs.Where(t => t != tab).All(t => !((ScrollViewer)t.Content).IsVisible),
                    "Selecting " + tab.Header + " exposes only its own controls at width " + width);
                check(tabs.All(t => t.ActualWidth > 0 && t.ActualHeight >= 40) && selected.ViewportHeight > 100,
                    "Tab labels and scrollable content have usable space: " + tab.Header + " / " + width);
                if (selected == readings)
                {
                    selected.ScrollToVerticalOffset(panel.OverlayMonitorSelector.TransformToAncestor((Visual)selected.Content).Transform(new Point()).Y - 50);
                    selected.UpdateLayout();
                    Capture(panel, Path.Combine(folder, "settings-overlay-window-" + width + ".png"), width, 920);
                    selected.ScrollToBottom(); selected.UpdateLayout();
                    Capture(panel, Path.Combine(folder, "settings-overlay-details-" + width + ".png"), width, 920);
                    selected.ScrollToTop(); selected.UpdateLayout();
                }
                if (selected == securityScroll)
                {
                    selected.ScrollToVerticalOffset(panel.FirewallPanel.TransformToAncestor((Visual)selected.Content).Transform(new Point()).Y - 12); selected.UpdateLayout();
                    Capture(panel, Path.Combine(folder, "settings-firewall-" + width + ".png"), width, 920);
                    var buttonBounds = panel.FirewallPanel.DisableButton.TransformToAncestor(selected).TransformBounds(new Rect(panel.FirewallPanel.DisableButton.RenderSize));
                    check(buttonBounds.Top >= 0 && buttonBounds.Bottom <= selected.ViewportHeight && buttonBounds.Right <= selected.ViewportWidth,
                        "Firewall action remains reachable and fits the scrolled Security tab at width " + width);
                    selected.ScrollToBottom(); selected.UpdateLayout();
                    Capture(panel, Path.Combine(folder, "settings-bitlocker-" + width + ".png"), width, 920);
                    buttonBounds = panel.BitLockerPanel.DecryptButton.TransformToAncestor(selected).TransformBounds(new Rect(panel.BitLockerPanel.DecryptButton.RenderSize));
                    check(buttonBounds.Top >= 0 && buttonBounds.Bottom <= selected.ViewportHeight && buttonBounds.Right <= selected.ViewportWidth,
                        "BitLocker action remains reachable and fits the scrolled Security tab at width " + width);
                    selected.ScrollToTop(); selected.UpdateLayout();
                }
                if (selected == storageScroll)
                {
                    selected.ScrollToBottom(); selected.UpdateLayout();
                    Capture(panel, Path.Combine(folder, "settings-storage-downloads-" + width + ".png"), width, SystemParameters.WorkArea.Height);
                    var downloadsBounds = panel.WipeDownloadsButton.TransformToAncestor(selected).TransformBounds(new Rect(panel.WipeDownloadsButton.RenderSize));
                    var toggleBounds = panel.AutoWipeDownloadsToggle.TransformToAncestor(selected).TransformBounds(new Rect(panel.AutoWipeDownloadsToggle.RenderSize));
                    check(downloadsBounds.Top >= 0 && downloadsBounds.Bottom <= selected.ViewportHeight && downloadsBounds.Right <= selected.ViewportWidth &&
                        toggleBounds.Top >= 0 && toggleBounds.Bottom <= selected.ViewportHeight && toggleBounds.Right <= selected.ViewportWidth,
                        "Downloads cleanup and startup controls fit the scrolled Storage Sense tab at width " + width);
                    selected.ScrollToTop(); selected.UpdateLayout();
                }
                if (selected == compressionScroll)
                {
                    panel.FileCompressionPanel.SelectDrive(Path.GetPathRoot(folder)!);
                    panel.Height = Math.Min(SystemParameters.WorkArea.Height, panel.MaxHeight); panel.UpdateLayout();
                    selected.ScrollToTop(); selected.UpdateLayout();
                    panel.FileCompressionPanel.ChooseDrive.BringIntoView(); selected.UpdateLayout();
                    Capture(panel, Path.Combine(folder, "settings-drive-picker-" + width + ".png"), width, SystemParameters.WorkArea.Height);
                    var pickBounds = panel.FileCompressionPanel.ChooseDrive.TransformToAncestor(selected).TransformBounds(new Rect(panel.FileCompressionPanel.ChooseDrive.RenderSize));
                    check(pickBounds.Top >= 0 && pickBounds.Bottom <= selected.ViewportHeight && pickBounds.Right <= selected.ViewportWidth,
                        "Entire-drive selection remains reachable at width " + width);
                    panel.FileCompressionPanel.ScanCompressButton.BringIntoView(); selected.UpdateLayout();
                    Capture(panel, Path.Combine(folder, "settings-scan-compress-" + width + ".png"), width, SystemParameters.WorkArea.Height);
                    var streamBounds = panel.FileCompressionPanel.ScanCompressButton.TransformToAncestor(selected).TransformBounds(new Rect(panel.FileCompressionPanel.ScanCompressButton.RenderSize));
                    check(streamBounds.Top >= 0 && streamBounds.Bottom <= selected.ViewportHeight && streamBounds.Right <= selected.ViewportWidth && panel.FileCompressionPanel.ScanCompressButton.IsEnabled,
                        "Scan-and-compress action is reachable and enabled for an explicitly selected drive at width " + width);
                    selected.ScrollToVerticalOffset(panel.FileCompressionPanel.CompressButton.TransformToAncestor((Visual)selected.Content).Transform(new Point()).Y - 20);
                    selected.UpdateLayout();
                    Capture(panel, Path.Combine(folder, "settings-file-compression-" + width + ".png"), width, SystemParameters.WorkArea.Height);
                    var actionBounds = panel.FileCompressionPanel.CompressButton.TransformToAncestor(selected).TransformBounds(new Rect(panel.FileCompressionPanel.CompressButton.RenderSize));
                    var reverseBounds = panel.FileCompressionPanel.DecompressButton.TransformToAncestor(selected).TransformBounds(new Rect(panel.FileCompressionPanel.DecompressButton.RenderSize));
                    check(actionBounds.Top >= 0 && actionBounds.Bottom <= selected.ViewportHeight && actionBounds.Right <= selected.ViewportWidth &&
                        reverseBounds.Right <= selected.ViewportWidth,
                        "Compression actions wrap and remain reachable at width " + width);
                    panel.FileCompressionPanel.SelectPaths(new[] { compressionFixture });
                    panel.UpdateLayout(); selected.UpdateLayout();
                    var automatic = panel.FileCompressionPanel.AutomaticPanel!;
                    selected.ScrollToVerticalOffset(automatic.High.TransformToAncestor((Visual)selected.Content).Transform(new Point()).Y - 40);
                    selected.UpdateLayout();
                    Capture(panel, Path.Combine(folder, "settings-automatic-compression-" + width + ".png"), width, SystemParameters.WorkArea.Height);
                    var highBounds = automatic.High.TransformToAncestor(selected).TransformBounds(new Rect(automatic.High.RenderSize));
                    var lowBounds = automatic.Low.TransformToAncestor(selected).TransformBounds(new Rect(automatic.Low.RenderSize));
                    check(highBounds.Top >= 0 && lowBounds.Bottom <= selected.ViewportHeight && highBounds.Right <= selected.ViewportWidth && lowBounds.Right <= selected.ViewportWidth,
                        "Automatic fullness sliders fit and remain reachable at width " + width);
                    check(!automatic.Enabled.IsChecked!.Value && automatic.Mode.Items.Count == 2,
                        "Both automatic modes are available and default off in Settings at width " + width);
                    selected.ScrollToBottom(); selected.UpdateLayout();
                    Capture(panel, Path.Combine(folder, "settings-automatic-actions-" + width + ".png"), width, SystemParameters.WorkArea.Height);
                    var applyBounds = automatic.ApplyButton.TransformToAncestor(selected).TransformBounds(new Rect(automatic.ApplyButton.RenderSize));
                    check(applyBounds.Right <= selected.ViewportWidth && automatic.High.Template.FindName("PART_Track", automatic.High) != null,
                        "Automatic controls use the themed slider and fit the scrolled Settings page at width " + width);
                    selected.ScrollToTop(); selected.UpdateLayout();
                }
            }
        }
        panel.Sections.SelectedIndex = 0;
        Capture(panel, Path.Combine(folder, "settings-tabs-full-screen.png"), SystemParameters.WorkArea.Width, SystemParameters.WorkArea.Height);
        check(((ScrollViewer)tabs[0].Content).ViewportHeight > 30,
            "Full-screen Settings retains scrollable section content");
        var root = (DockPanel)panel.Content;
        var footer = root.Children.OfType<StackPanel>().Single();
        var bounds = footer.TransformToAncestor(root).TransformBounds(new Rect(footer.RenderSize));
        check(bounds.Bottom <= root.ActualHeight + 1 && bounds.Top >= panel.Sections.ActualHeight,
            "Done and Exit remain fully visible below the tabs in full-screen Settings");
        panel.Width = Math.Min(1000, panel.MaxWidth); panel.Height = Math.Min(920, panel.MaxHeight); panel.UpdateLayout();
    }

    internal static void Capture(SettingsWindow panel, string path, double width, double height)
    {
        // Always capture the actual full-screen surface, including callers that previously rendered off screen.
        if (!panel.IsVisible) panel.Show();
        panel.WindowState = WindowState.Maximized;
        panel.UpdateLayout();
        panel.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        var content = (FrameworkElement)panel.Content;
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth), (int)Math.Ceiling(content.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        var drawing = new DrawingVisual();
        using (var context = drawing.RenderOpen())
            context.DrawRectangle(new VisualBrush(content), null, new Rect(content.RenderSize));
        bitmap.Render(drawing);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); encoder.Save(stream);
    }
}
