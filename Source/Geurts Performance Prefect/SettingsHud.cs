using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace GeurtsPerformancePrefect;

internal sealed record HudSection(string Kicker, string Title, string Description, string Code);

internal sealed class SettingsHud
{
    internal static ResourceDictionary CreateResources() => new() { Source = new Uri("pack://application:,,,/GeurtsPerformancePrefect;component/SettingsHud.xaml") };
    internal static Brush Brush(string value) => (Brush)new BrushConverter().ConvertFromString(value)!;
    internal static readonly Brush Background = Brush("#09130E"), Panel = Brush("#12231A"), Text = Brush("#E4EDDF"),
        Muted = Brush("#A2B39B"), Accent = Brush("#91C97C"), Line = Brush("#3C5942");
    readonly SettingsWindow window;
    readonly OverlayWindow overlay;
    readonly Dictionary<UIElement, string> overlayControls = new();
    readonly Dictionary<string, FrameworkElement> overlayPages = new();
    readonly Dictionary<string, ToggleButton> pageButtons = new();
    readonly SettingsHudPreview preview;
    readonly ScrollViewer overlayScroll;
    internal ComboBox[] Choices { get; }
    internal Slider[] Sliders { get; }
    internal string OverlayPage { get; private set; } = "Readings";
    internal SettingsHudPreview Preview => preview;

    internal static Border Header()
    {
        var grid = new Grid(); grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new()); grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var emblem = new Border { Width = 45, Height = 49, Background = Brush("#233E29"), BorderBrush = Brush("#78936A"), BorderThickness = new Thickness(2),
            Child = new TextBlock { Text = "G+", FontFamily = new FontFamily("Bahnschrift"), FontSize = 24, FontWeight = FontWeights.SemiBold, Foreground = Accent, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
        grid.Children.Add(emblem);
        var brand = new StackPanel { Margin = new Thickness(14,0,16,0), VerticalAlignment = VerticalAlignment.Center };
        brand.Children.Add(new TextBlock { Text = "G E U R T S", FontFamily = new FontFamily("Consolas"), FontSize = 11, Foreground = Accent });
        brand.Children.Add(new TextBlock { Text = "PERFORMANCE PREFECT", FontFamily = new FontFamily("Bahnschrift"), FontWeight = FontWeights.SemiBold, FontSize = 17, Foreground = Text, TextWrapping = TextWrapping.Wrap });
        Grid.SetColumn(brand,1); grid.Children.Add(brand);
        var release = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
        release.Children.Add(new TextBlock { Text = "SETTINGS / COMMAND DECK", FontFamily = new FontFamily("Consolas"), FontSize = 11, Foreground = Muted, TextWrapping = TextWrapping.Wrap, MaxWidth = 200 });
        release.Children.Add(new TextBlock { Text = "v" + AppUpdates.CurrentVersion, FontFamily = new FontFamily("Consolas"), FontSize = 11, Foreground = Accent, Margin = new Thickness(0,5,0,0) });
        Grid.SetColumn(release,2); grid.Children.Add(release);
        return new Border { Child = grid, Background = new LinearGradientBrush(Color.FromRgb(35,55,37),Color.FromRgb(21,37,26),90), BorderBrush = Brush("#5F795B"), BorderThickness = new Thickness(0,0,0,2), Padding = new Thickness(24,19,24,19) };
    }

    internal SettingsHud(SettingsWindow window, OverlayWindow overlay)
    {
        this.window = window; this.overlay = overlay;
        var oldTabs = window.Sections.Items.Cast<TabItem>().ToArray();
        var panels = oldTabs.Select(tab => ((ScrollViewer)tab.Content).Content).OfType<System.Windows.Controls.Panel>();
        Choices = panels.SelectMany(panel => panel.Children.OfType<ComboBox>()).ToArray();
        Sliders = panels.SelectMany(panel => panel.Children.OfType<Slider>()).ToArray();
        var order = new[] { "Overlay", "Performance", "Startup", "Security", "Storage Sense", "File compression", "Console", "Disk analyser", "Programs and Features", "Sensor sources", "Application updates" };
        var tabs = oldTabs.OrderBy(tab => Array.IndexOf(order,(string)tab.Header)).ToArray();
        window.Sections.Items.Clear();
        for (var index = 0; index < tabs.Length; index++)
        {
            var title = (string)tabs[index].Header;
            var description = title switch
            {
                "Overlay" => "Choose what appears on your overlay and how it behaves.",
                "Performance" => "Choose power behavior and optional performance controls.",
                "Startup" => "Control how Prefect opens when you sign in.",
                "Security" => "Review administrator access and Windows security controls.",
                "Storage Sense" => "Review automatic cleanup and Downloads settings.",
                "File compression" => "Preview and manage compression before applying changes.",
                "Console" => "Run diagnostic checks and review their live output.",
                "Disk analyser" => "Inspect disk space using the embedded analyser.",
                "Programs and Features" => "Review installed applications and their maintenance actions.",
                "Sensor sources" => "Choose the hardware sources used by your readings.",
                _ => "Review release information and application update controls."
            };
            tabs[index].Tag = new HudSection(title == "Overlay" ? "DISPLAY CONFIGURATION" : "SYSTEM CONFIGURATION", title.ToUpperInvariant(), description, (index + 1).ToString("00"));
            window.Sections.Items.Add(tabs[index]);
            if (title != "Overlay" && title != "Console" && ((ScrollViewer)tabs[index].Content).Content is StackPanel stack)
            {
                var children = stack.Children.Cast<UIElement>().ToArray(); stack.Children.Clear();
                var group = new StackPanel(); foreach (var child in children) group.Children.Add(child);
                stack.Children.Add(new GroupBox { Header = title.ToUpperInvariant() + " CONTROLS", Content = group });
            }
        }
        var overlayTab = tabs.Single(tab => (string)tab.Header == "Overlay");
        overlayScroll = (ScrollViewer)overlayTab.Content;
        var original = (StackPanel)overlayScroll.Content;
        var nodes = original.Children.Cast<UIElement>().ToArray(); original.Children.Clear();
        var placementStart = Array.FindIndex(nodes,node => node is TextBlock label && label.Text == "Window");
        var appearanceStart = Array.FindIndex(nodes,node => node is TextBlock label && (label.Text == "HUD layout" || label.Text.StartsWith("Opacity ·",StringComparison.Ordinal)));
        if (placementStart < 0 || appearanceStart <= placementStart) throw new InvalidOperationException("Overlay control groups could not be identified.");
        var readings = new StackPanel();
        // Keep the existing controls and their event handlers; only their containers change.
        var metricNodes = nodes.Take(3).ToArray();
        var fpsDetails = new StackPanel(); fpsDetails.Children.Add(metricNodes[1]); fpsDetails.Children.Add(metricNodes[2]);
        if (metricNodes[0] is Border metricBorder) { metricBorder.Padding = new Thickness(0); metricBorder.Background = Brushes.Transparent; }
        var metricBody = new StackPanel(); metricBody.Children.Add(metricNodes[0]);
        metricBody.Children.Add(new Expander { Header = "FPS source details", Content = fpsDetails, Margin = new Thickness(0,15,0,0), Foreground = Muted });
        readings.Children.Add(new GroupBox { Header = "VISIBLE READINGS", Content = metricBody });
        var driveBody = new StackPanel(); foreach (var node in nodes.Skip(3).Take(placementStart - 3)) driveBody.Children.Add(node);
        readings.Children.Add(new GroupBox { Header = "DRIVES & APPLICATIONS", Content = driveBody });
        AddOverlayPage("Readings",readings,nodes.Take(placementStart));
        var placementBody = new StackPanel(); foreach (var node in nodes.Skip(placementStart + 1).Take(appearanceStart - placementStart - 1)) placementBody.Children.Add(node);
        AddOverlayPage("Placement",new GroupBox { Header = "DISPLAY & POSITIONING", Content = placementBody },nodes.Skip(placementStart).Take(appearanceStart - placementStart));
        var appearanceBody = new StackPanel(); foreach (var node in nodes.Skip(appearanceStart)) appearanceBody.Children.Add(node);
        AddOverlayPage("Appearance",new GroupBox { Header = "OVERLAY APPEARANCE", Content = appearanceBody },nodes.Skip(appearanceStart));
        var navigation = new WrapPanel { Margin = new Thickness(0,0,0,22) };
        foreach (var name in overlayPages.Keys)
        {
            var button = new ToggleButton { Content = name, Style = (Style)window.FindResource("HudSegment"), Margin = new Thickness(0,0,8,8) };
            System.Windows.Automation.AutomationProperties.SetName(button,"Overlay " + name.ToLowerInvariant());
            button.Click += (_,_) => SelectOverlayPage(name); pageButtons[name] = button; navigation.Children.Add(button);
        }
        original.Children.Add(navigation);
        var columns = new Grid(); columns.ColumnDefinitions.Add(new()); columns.ColumnDefinitions.Add(new() { Width = new GridLength(238) });
        var pages = new StackPanel(); foreach (var page in overlayPages.Values) pages.Children.Add(page);
        columns.Children.Add(pages);
        preview = new SettingsHudPreview(overlay) { Margin = new Thickness(20,0,0,0), VerticalAlignment = VerticalAlignment.Top };
        Grid.SetColumn(preview,1); columns.Children.Add(preview); original.Children.Add(columns);
        void Responsive()
        {
            window.Resources["HudRailWidth"] = new GridLength(213);
            window.Resources["HudDescriptionVisibility"] = window.ActualHeight < 800 ? Visibility.Collapsed : Visibility.Visible;
            var show = window.ActualWidth >= 1000;
            columns.ColumnDefinitions[1].Width = new GridLength(show ? 238 : 0);
            preview.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        }
        window.SizeChanged += (_,_) => Responsive(); Responsive();
        overlay.SnapshotReceived += UpdateSnapshot; overlay.SettingsChanged += RefreshPreview;
        window.Closed += (_,_) => { overlay.SnapshotReceived -= UpdateSnapshot; overlay.SettingsChanged -= RefreshPreview; };
        SelectOverlayPage("Readings"); window.Sections.SelectedIndex = 0; RefreshPreview(); RemapLegacyBrushes(window);
    }
    void AddOverlayPage(string name, FrameworkElement page, IEnumerable<UIElement> controls)
    {
        overlayPages[name] = page; foreach (var control in controls) overlayControls[control] = name;
    }
    internal void SelectOverlayPage(string name)
    {
        if (!overlayPages.ContainsKey(name)) return;
        OverlayPage = name;
        foreach (var item in overlayPages) item.Value.Visibility = item.Key == name ? Visibility.Visible : Visibility.Collapsed;
        foreach (var button in pageButtons) button.Value.IsChecked = button.Key == name;
        overlayScroll.ScrollToTop();
    }
    internal void RevealControl(FrameworkElement control)
    {
        for (DependencyObject? current = control; current != null; current = LogicalTreeHelper.GetParent(current))
            if (current is UIElement element && overlayControls.TryGetValue(element,out var page)) { SelectOverlayPage(page); return; }
    }
    void UpdateSnapshot(Snapshot _) => RefreshPreview();
    void RefreshPreview() => preview.Refresh();
    static void RemapLegacyBrushes(DependencyObject element)
    {
        Brush Map(Brush brush)
        {
            if (brush == Palette.Text) return Text; if (brush == Palette.Muted) return Muted; if (brush == Palette.Accent) return Accent;
            if (brush == Palette.Background) return Background; if (brush == Palette.Panel) return Panel; if (brush == Palette.Track) return Line;
            return brush;
        }
        if (element is Control control) { control.Foreground = Map(control.Foreground); control.Background = Map(control.Background); control.BorderBrush = Map(control.BorderBrush); }
        if (element is TextBlock label) label.Foreground = Map(label.Foreground);
        if (element is Border border) { border.Background = Map(border.Background); border.BorderBrush = Map(border.BorderBrush); }
        if (element is System.Windows.Controls.Panel panel) panel.Background = Map(panel.Background);
        foreach (var child in LogicalTreeHelper.GetChildren(element).OfType<DependencyObject>()) RemapLegacyBrushes(child);
    }
}

internal sealed class SettingsHudPreview : StackPanel
{
    readonly OverlayWindow overlay;
    readonly Dictionary<Metric,MetricRow> rows = new();
    readonly Border frame;
    readonly StackPanel driveRows = new();
    readonly TextBlock caption = new() { Foreground = SettingsHud.Muted, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,18,0,0) };
    readonly StackPanel readings = new();
    readonly TextBlock empty = new() { Text = "Enable a reading to see it here.", Foreground = SettingsHud.Muted, FontSize = 12, TextWrapping = TextWrapping.Wrap };
    internal string DisplayedValue(Metric metric) => rows[metric].ValueText;
    internal bool IsDisplayed(Metric metric) => rows[metric].Visibility == Visibility.Visible;
    internal HudLayout DisplayedLayout { get; private set; }
    internal SettingsHudPreview(OverlayWindow overlay)
    {
        this.overlay = overlay;
        Children.Add(new TextBlock { Text = "OVERLAY PREVIEW / LIVE", FontFamily = new FontFamily("Consolas"), FontSize = 11, Foreground = SettingsHud.Muted, Margin = new Thickness(0,0,0,13) });
        readings.Children.Add(new TextBlock { Text = "G+ PREFECT", Foreground = SettingsHud.Accent, FontFamily = new FontFamily("Bahnschrift"), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0,0,0,12) });
        foreach (var metric in MetricInfo.All)
        {
            var row = new MetricRow(metric);
            rows[metric] = row; readings.Children.Add(row);
        }
        readings.Children.Add(driveRows); readings.Children.Add(empty);
        frame = new Border { Child = readings, BorderThickness = new Thickness(1) };
        Children.Add(new Viewbox { Child = frame, Width = 218, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, HorizontalAlignment = HorizontalAlignment.Left });
        Children.Add(caption);
    }
    internal void Refresh()
    {
        DisplayedLayout = overlay.Settings.HudLayout;
        frame.Width = CompactHud.Width(DisplayedLayout);
        frame.Padding = new Thickness(DisplayedLayout == HudLayout.Standard ? 14 : 9);
        frame.Background = DisplayedLayout == HudLayout.Standard ? Palette.Background : CompactHud.Background;
        frame.BorderBrush = DisplayedLayout == HudLayout.Standard ? Palette.Track : CompactHud.Line;
        System.Windows.Documents.TextElement.SetFontFamily(readings,new FontFamily(DisplayedLayout == HudLayout.Standard ? "Segoe UI" : "Bahnschrift"));
        var count = 0;
        foreach (var metric in MetricInfo.All)
        {
            var row = rows[metric]; var live = overlay.MetricRows[metric];
            row.ApplyLayout(DisplayedLayout); row.CopyFrom(live);
            row.Visibility = overlay.Settings.Visible[metric] ? Visibility.Visible : Visibility.Collapsed;
            if (overlay.Settings.Visible[metric]) count++;
        }
        driveRows.Children.Clear();
        foreach (var drive in overlay.DriveRows.Where(pair => overlay.Settings.IsDriveVisible(pair.Key)))
        {
            count++;
            var row = new MetricRow(drive.Value.LabelText); row.ApplyLayout(DisplayedLayout); row.CopyFrom(drive.Value);
            driveRows.Children.Add(row);
        }
        empty.Visibility = count == 0 ? Visibility.Visible : Visibility.Collapsed;
        readings.Opacity = overlay.Settings.Opacity;
        caption.Text = (overlay.Latest == null ? "Waiting for live readings." : "Live values from the overlay.") +
            $"\n{CompactHud.Name(DisplayedLayout)}\nSize {overlay.Settings.Scale:P0} · Opacity {overlay.Settings.Opacity:P0}\nPosition: {overlay.Settings.AutoAvoidCorner}";
    }
}
