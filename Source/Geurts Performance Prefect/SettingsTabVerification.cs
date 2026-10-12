using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;

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
        return (ScrollViewer)tab.Content;
    }

    internal static void Run(SettingsWindow panel, string folder, Action<bool, string> check)
    {
        panel.Show();
        var tabs = panel.Sections.Items.Cast<TabItem>().ToArray();
        check(tabs.Length == 9 && tabs.Select(t => t.Header).Distinct().Count() == 9,
            "All nine Settings sections have distinct tabs");
        foreach (var width in new[] { 490d, 420d })
        {
            foreach (var tab in tabs)
            {
                var peer = new TabItemAutomationPeer(tab, new TabControlAutomationPeer(panel.Sections));
                ((ISelectionItemProvider)peer.GetPattern(PatternInterface.SelectionItem)).Select();
                Verification.Render((FrameworkElement)panel.Content,
                    Path.Combine(folder, "settings-tab-" + tabs.ToList().IndexOf(tab) + "-" + width + ".png"), width, 790);
                var selected = (ScrollViewer)tab.Content;
                check(tab.IsSelected && selected.IsVisible && tabs.Where(t => t != tab).All(t => !((ScrollViewer)t.Content).IsVisible),
                    "Selecting " + tab.Header + " exposes only its own controls at width " + width);
                check(tabs.All(t => t.ActualWidth > 0 && t.ActualHeight >= 40) && selected.ViewportHeight > 100,
                    "Tab labels and scrollable content have usable space: " + tab.Header + " / " + width);
            }
        }
        panel.Sections.SelectedIndex = 0;
        Verification.Render((FrameworkElement)panel.Content, Path.Combine(folder, "settings-tabs-minimum.png"), 420, 410);
        check(((ScrollViewer)tabs[0].Content).ViewportHeight > 60,
            "Minimum Settings height retains scrollable content below every tab");
    }
}
