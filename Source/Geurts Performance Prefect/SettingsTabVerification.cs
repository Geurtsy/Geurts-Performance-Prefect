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
        panel.UpdateLayout();
        panel.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        return (ScrollViewer)tab.Content;
    }

    internal static void Run(SettingsWindow panel, string folder, Action<bool, string> check)
    {
        panel.Show();
        var tabs = panel.Sections.Items.Cast<TabItem>().ToArray();
        check(tabs.Length == 6 && tabs.Select(t => t.Header).Distinct().Count() == 6,
            "All six Settings sections have distinct tabs");
        Select(panel, panel.CheckDiskButton);
        check((string)((TabItem)panel.Sections.SelectedItem).Header == "Performance",
            "Check disk for errors opens from the Performance settings tab");
        var readings = Select(panel, panel.MetricToggles.Values.First());
        check((string)((TabItem)panel.Sections.SelectedItem).Header == "Overlay" &&
            Select(panel, panel.ApplicationAverageSlider) == readings &&
            Select(panel, panel.OverlayMonitorSelector) == readings &&
            Select(panel, panel.AutoAvoidToggle) == readings &&
            Select(panel, panel.MinimiseToTrayToggle) == readings &&
            Select(panel, panel.TopmostToggle) == readings &&
            panel.DriveToggles.Values.All(toggle => Select(panel, toggle) == readings),
            "Overlay contains the metric, hard drive, top application and window controls");
        foreach (var width in new[] { 500d, 430d })
        {
            foreach (var tab in tabs)
            {
                var peer = new TabItemAutomationPeer(tab, new TabControlAutomationPeer(panel.Sections));
                ((ISelectionItemProvider)peer.GetPattern(PatternInterface.SelectionItem)).Select();
                Capture(panel, Path.Combine(folder, "settings-tab-" + tabs.ToList().IndexOf(tab) + "-" + width + ".png"), width, 820);
                var selected = (ScrollViewer)tab.Content;
                check(tab.IsSelected && selected.IsVisible && tabs.Where(t => t != tab).All(t => !((ScrollViewer)t.Content).IsVisible),
                    "Selecting " + tab.Header + " exposes only its own controls at width " + width);
                check(tabs.All(t => t.ActualWidth > 0 && t.ActualHeight >= 40) && selected.ViewportHeight > 100,
                    "Tab labels and scrollable content have usable space: " + tab.Header + " / " + width);
                if (selected == readings)
                {
                    selected.ScrollToVerticalOffset(panel.OverlayMonitorSelector.TransformToAncestor((Visual)selected.Content).Transform(new Point()).Y - 50);
                    selected.UpdateLayout();
                    Capture(panel, Path.Combine(folder, "settings-overlay-window-" + width + ".png"), width, 820);
                    selected.ScrollToBottom(); selected.UpdateLayout();
                    Capture(panel, Path.Combine(folder, "settings-overlay-details-" + width + ".png"), width, 820);
                    selected.ScrollToTop(); selected.UpdateLayout();
                }
            }
        }
        panel.Sections.SelectedIndex = 0;
        Capture(panel, Path.Combine(folder, "settings-tabs-minimum.png"), 430, 440);
        check(((ScrollViewer)tabs[0].Content).ViewportHeight > 30,
            "Minimum Settings height retains scrollable content below every tab");
        var root = (DockPanel)panel.Content;
        var footer = root.Children.OfType<StackPanel>().Single();
        var bounds = footer.TransformToAncestor(root).TransformBounds(new Rect(footer.RenderSize));
        check(bounds.Bottom <= root.ActualHeight + 1 && bounds.Top >= panel.Sections.ActualHeight,
            "Done and Exit remain fully visible below the tabs at minimum window size");
        panel.Width = 500; panel.Height = 820; panel.UpdateLayout();
    }

    static void Capture(SettingsWindow panel, string path, double width, double height)
    {
        panel.Width = width; panel.Height = height;
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
