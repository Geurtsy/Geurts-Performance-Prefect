using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace GeurtsPerformancePrefect;

public sealed class MetricRow : Border
{
    readonly TextBlock value, detail;
    readonly TextBlock application;
    readonly Border fill, track;
    readonly bool temperature;
    readonly bool fps;
    readonly TextBlock label;
    readonly Grid contentGrid;
    readonly System.Windows.Shapes.Path readingIcon;
    readonly Expander expander;
    readonly TextBlock expandedText;
    internal Expander DetailsExpander => expander;
    HudLayout layout;
    bool layoutApplied;
    string fullLabel;
    Reading lastReading = new(null, "Waiting for sensors");
    TopApplication? lastApplication;
    int lastAverage = 60;
    string? lastResource;
    public MetricRow(Metric metric) : this(MetricInfo.Label(metric), MetricInfo.IsTemperature(metric), metric == Metric.ForegroundFps) { }
    public MetricRow(string name, bool temperature = false, bool fps = false)
    {
        fullLabel = name;
        this.temperature = temperature;
        this.fps = fps;
        Background = Palette.Panel; CornerRadius = new CornerRadius(8); Padding = new Thickness(12,9,12,8); Margin = new Thickness(0,0,0,6);
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition()); grid.RowDefinitions.Add(new RowDefinition()); grid.RowDefinitions.Add(new RowDefinition()); grid.RowDefinitions.Add(new RowDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0) });
        grid.ColumnDefinitions.Add(new ColumnDefinition()); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        readingIcon = new System.Windows.Shapes.Path { Data = CompactHud.Icon(name), Stroke = CompactHud.Muted, StrokeThickness = 1,
            Width = 17, Height = 17, Stretch = Stretch.Uniform, Margin = new Thickness(0,0,8,0), VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
        grid.Children.Add(readingIcon);
        label = new TextBlock { Text = name, Foreground = Palette.Muted, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        Grid.SetColumn(label,1); grid.Children.Add(label);
        value = new TextBlock { Text = "—", FontSize = 22, FontWeight = FontWeights.SemiBold, Foreground = Palette.Text };
        Grid.SetColumn(value, 2); grid.Children.Add(value);
        detail = new TextBlock { FontSize = 10, Foreground = Palette.Muted, Text = "Waiting for sensors", Margin = new Thickness(0,0,0,5), TextTrimming = TextTrimming.CharacterEllipsis };
        Grid.SetRow(detail, 1); Grid.SetColumn(detail,1); Grid.SetColumnSpan(detail, 2); grid.Children.Add(detail);
        application = new TextBlock { FontSize = 10, Foreground = Palette.Accent, Margin = new Thickness(0,0,0,6), TextTrimming = TextTrimming.CharacterEllipsis, Visibility = Visibility.Collapsed };
        Grid.SetRow(application, 2); Grid.SetColumn(application,1); Grid.SetColumnSpan(application, 2); grid.Children.Add(application);
        track = new Border { Background = Palette.Track, Height = 3, CornerRadius = new CornerRadius(2), ClipToBounds = true };
        fill = new Border { Background = Palette.Accent, Width = 0, HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(2) };
        track.Child = fill; Grid.SetRow(track, 3); Grid.SetColumn(track,1); Grid.SetColumnSpan(track, 2); grid.Children.Add(track);
        if (fps) track.Visibility = Visibility.Collapsed;
        track.SizeChanged += (_, _) => UpdateFill();
        contentGrid = grid;
        expandedText = new TextBlock { FontSize = 11, Foreground = CompactHud.Muted, TextWrapping = TextWrapping.Wrap };
        var expanded = new StackPanel(); expanded.Children.Add(expandedText);
        expander = new Expander { Style = CompactHud.Style("HudReadingExpander") };
        expanded.Children.Add(CompactHud.Button("\uE711", "Close reading details", () => expander.IsExpanded = false));
        expander.Content = new Border { Background = CompactHud.Panel, BorderBrush = CompactHud.Accent, BorderThickness = new Thickness(2,0,0,0),
            Padding = new Thickness(8), Margin = new Thickness(0,4,0,4), Child = expanded };
        System.Windows.Automation.AutomationProperties.SetName(expander,name + " details");
        Child = grid;
    }
    double fraction;
    public void SetLabel(string name)
    {
        fullLabel = name; label.Text = layout == HudLayout.MinimalVertical ? CompactHud.ShortLabel(name) : name;
        readingIcon.Data = CompactHud.Icon(name);
        System.Windows.Automation.AutomationProperties.SetName(expander,name + " details");
    }
    internal HudLayout Layout => layout;
    internal bool DetailsVisible => detail.Visibility == Visibility.Visible;
    internal bool ApplicationVisible => application.Visibility == Visibility.Visible;
    internal bool MeterVisible => track.Visibility == Visibility.Visible;
    internal bool IconVisible => readingIcon.Visibility == Visibility.Visible;
    internal double ValueFontSize => value.FontSize;
    internal void CopyFrom(MetricRow source)
    {
        SetLabel(source.fullLabel); Update(source.lastReading, source.lastApplication, source.lastAverage, source.lastResource);
    }
    internal void ApplyLayout(HudLayout selected)
    {
        if (layoutApplied && layout == selected) return;
        layoutApplied = true;
        layout = selected;
        Child = null; expander.Header = null;
        var fullVertical = layout == HudLayout.StandardVertical;
        if (layout is HudLayout.CompactVertical or HudLayout.StandardVertical) { expander.Header = contentGrid; Child = expander; }
        else { expander.IsExpanded = false; Child = contentGrid; }
        var standard = layout == HudLayout.Standard;
        var minimal = layout == HudLayout.MinimalVertical;
        contentGrid.ColumnDefinitions[0].Width = new GridLength(fullVertical ? 30 : 0);
        readingIcon.Visibility = fullVertical ? Visibility.Visible : Visibility.Collapsed;
        Background = standard ? Palette.Panel : Brushes.Transparent;
        BorderBrush = standard ? Brushes.Transparent : CompactHud.Line;
        BorderThickness = standard ? new Thickness(0) : new Thickness(0,0,0,1);
        CornerRadius = new CornerRadius(standard ? 8 : 0);
        Padding = standard ? new Thickness(12,9,12,8) : fullVertical ? new Thickness(0,12,0,12) : minimal ? new Thickness(0,5,0,5) : new Thickness(0,6,0,6);
        Margin = standard ? new Thickness(0,0,0,6) : new Thickness(0);
        label.Foreground = standard ? Palette.Muted : CompactHud.Text;
        label.FontWeight = standard ? FontWeights.Normal : FontWeights.Medium;
        label.FontSize = fullVertical ? 13 : 12;
        label.TextWrapping = minimal ? TextWrapping.NoWrap : TextWrapping.Wrap;
        label.TextTrimming = minimal || standard ? TextTrimming.CharacterEllipsis : TextTrimming.None;
        value.FontSize = standard ? 22 : fullVertical ? 24 : minimal ? 18 : 20;
        detail.Foreground = standard ? Palette.Muted : CompactHud.Muted;
        detail.FontSize = standard ? 10 : 11;
        detail.LineHeight = fullVertical ? 16 : double.NaN;
        detail.Margin = standard ? new Thickness(0,0,0,5) : new Thickness(0,1,0,0);
        detail.Visibility = minimal ? Visibility.Collapsed : Visibility.Visible;
        detail.TextWrapping = standard ? TextWrapping.NoWrap : TextWrapping.Wrap;
        detail.TextTrimming = standard ? TextTrimming.CharacterEllipsis : TextTrimming.None;
        application.Foreground = standard ? Palette.Accent : CompactHud.Accent;
        application.FontSize = standard ? 10 : 11;
        application.LineHeight = fullVertical ? 16 : double.NaN;
        application.Margin = standard ? new Thickness(0,0,0,6) : new Thickness(0,1,0,2);
        application.TextWrapping = standard ? TextWrapping.NoWrap : TextWrapping.Wrap;
        application.TextTrimming = standard ? TextTrimming.CharacterEllipsis : TextTrimming.None;
        track.Height = standard || fullVertical ? 3 : 2;
        track.Background = standard ? Palette.Track : CompactHud.Track;
        track.Visibility = minimal || fps ? Visibility.Collapsed : Visibility.Visible;
        SetLabel(fullLabel);
        Update(lastReading,lastApplication,lastAverage,lastResource);
    }
    void UpdateFill() => fill.Width = Math.Max(0, ((FrameworkElement)fill.Parent).ActualWidth * fraction);
    public string ApplicationText => application.Text;
    public string ValueText => value.Text;
    public string LabelText => label.Text;
    public string DetailText => detail.Text;
    public void Update(Reading reading, TopApplication? topApplication = null, int averageSeconds = 60, string? resource = null)
    {
        lastReading = reading; lastApplication = topApplication; lastAverage = averageSeconds; lastResource = resource;
        var valid = reading.Value.HasValue && double.IsFinite(reading.Value.Value);
        value.Text = valid ? $"{reading.Value:0}{(fps ? " FPS" : temperature ? " °C" : " %")}" : "—";
        if (layout != HudLayout.Standard && valid)
        {
            value.Inlines.Clear();
            value.Inlines.Add(new Run($"{reading.Value:0}"));
            value.Inlines.Add(new Run(fps ? " FPS" : temperature ? " °C" : " %") { FontSize = 11, Foreground = CompactHud.Muted });
        }
        var separator = reading.Source.LastIndexOf(" / ", StringComparison.Ordinal);
        var sourceLabel = separator >= 0 ? reading.Source[(separator + 3)..] : reading.Source;
        detail.Text = valid || fps ? (string.IsNullOrEmpty(reading.Detail) ? sourceLabel : reading.Detail) : "Unavailable · open Settings";
        ToolTip = valid ? reading.Source + "\n" + reading.Detail : reading.Source + "\nNo reading is available. Check Settings.";
        if (layout == HudLayout.MinimalVertical) ToolTip = null;
        fraction = valid ? Math.Clamp(reading.Value!.Value / 100d, 0, 1) : 0;
        var hot = temperature && reading.Value >= 85;
        fill.Background = hot ? (layout == HudLayout.Standard ? Palette.Amber : CompactHud.Amber) : layout == HudLayout.Standard ? Palette.Accent : CompactHud.Accent;
        value.Foreground = hot ? (layout == HudLayout.Standard ? Palette.Amber : CompactHud.Amber) : layout == HudLayout.Standard ? Palette.Text : CompactHud.Text;
        application.Visibility = resource == null || layout == HudLayout.MinimalVertical ? Visibility.Collapsed : Visibility.Visible;
        if (resource != null)
        {
            topApplication ??= new(null, 0, 0, "Waiting for application samples");
            var amount = resource == UsageResource.Memory ? FormatBytes(topApplication.Average) :
                resource.StartsWith("drive:", StringComparison.Ordinal) ? FormatBytes(topApplication.Average) + "/s" : $"{topApplication.Average:0.0}%";
            application.Text = topApplication.Unavailable != null ? "Top app · " + topApplication.Unavailable :
                topApplication.Name == null ? $"Top app · no activity ({averageSeconds}s avg)" :
                $"Top app · {topApplication.Name} · {amount} ({averageSeconds}s avg)";
            application.ToolTip = topApplication.Unavailable ??
                $"{topApplication.Name ?? "No application activity"}\nAverage: {amount}\n" +
                $"Last {Math.Min(averageSeconds, topApplication.ObservedSeconds):0.#} seconds sampled of a {averageSeconds}-second window.\n" +
                (resource == UsageResource.Memory ? "Physical working sets, grouped by executable name; shared pages can appear in more than one process." :
                resource.StartsWith("drive:", StringComparison.Ordinal) ? "Read/write throughput on this physical drive, grouped by executable name. The drive percentage measures total active time." :
                resource.StartsWith("gpu:", StringComparison.Ordinal) ? "Busiest GPU engine on the selected card, grouped by executable name." :
                "Share of total CPU capacity, grouped by executable name. Protected or very short-lived processes may not be sampled.");
        }
        expandedText.Text = reading.Source + (string.IsNullOrWhiteSpace(reading.Detail) ? "" : "\n" + reading.Detail) +
            (resource == null ? "" : "\n\n" + application.Text + "\n" + application.ToolTip);
        UpdateFill();
    }
    static string FormatBytes(double bytes) => bytes >= 1073741824 ? $"{bytes / 1073741824:0.0} GB" :
        bytes >= 1048576 ? $"{bytes / 1048576:0.0} MB" : bytes >= 1024 ? $"{bytes / 1024:0.0} KB" : $"{bytes:0} B";
}

public static class Palette
{
    static SolidColorBrush Brush(string hex) { var b = (SolidColorBrush)new BrushConverter().ConvertFrom(hex)!; b.Freeze(); return b; }
    public static readonly Brush Text = Brush("#F0F7F3"), Muted = Brush("#A3B8AD"), Accent = Brush("#8BE5AF"),
        Panel = Brush("#1B2622"), Track = Brush("#34463A"), Amber = Brush("#FFC981"), Background = Brush("#111917");
}

public sealed class OverlayWindow : Window
{
    public OverlaySettings Settings { get; }
    public SettingsStore Store { get; }
    public DownloadsCleanup Downloads { get; }
    internal StorageSenseController StorageSense { get; }
    internal AutomaticCompressionController AutomaticCompression { get; }
    public PowerOptionsController PowerOptions { get; }
    public HardwareInventoryController HardwareInventory { get; }
    public Snapshot? Latest { get; private set; }
    public event Action<Snapshot>? SnapshotReceived;
    public event Action<bool>? AutoAvoidChanged;
    public event Action? DisplaysChanged;
    internal event Action? SettingsChanged;
    readonly Dictionary<Metric, MetricRow> rows = new();
    readonly ApplicationUsageHistory applicationHistory = new();
    public IReadOnlyDictionary<Metric, MetricRow> MetricRows => rows;
    public Dictionary<string, MetricRow> DriveRows { get; } = new();
    readonly StackPanel driveRows = new();
    readonly ScrollViewer readingsScroll;
    readonly TextBlock status, empty;
    readonly Border surface;
    readonly Grid header;
    readonly FrameworkElement standardTitle, standardButtons;
    readonly Grid compactHeader;
    readonly StackPanel tacticalBrand, tacticalButtons;
    readonly Border tacticalEmblem;
    readonly TextBlock tacticalName, tacticalKicker, tacticalSubhead;
    readonly Button pinButton, collapseButton;
    bool hudCollapsed;
    HudLayout appliedLayout;
    internal Button CollapseHudButton => collapseButton;
    internal Button PinHudButton => pinButton;
    internal bool HudCollapsed => hudCollapsed;
    internal double HudWidth => surface.Width;
    readonly Func<Point> cursorPosition;
    readonly Func<ProcessStartInfo, bool>? restartStart;
    readonly DispatcherTimer saveTimer, staleTimer;
    readonly CancellationTokenSource stop = new();
    readonly Forms.NotifyIcon? tray;
    readonly System.Drawing.Icon? trayIcon;
    readonly Forms.ContextMenuStrip trayMenu = new();
    readonly MenuItem autoAvoidMenuItem;
    public Forms.ToolStripMenuItem AutoAvoidTrayItem { get; }
    internal Forms.ToolStripMenuItem RestartTrayItem { get; }
    Task? worker;
    SettingsWindow? settingsWindow;
    int refreshMilliseconds;
    bool monitorFps;
    bool closing;
    bool exitRequested;
    readonly bool verification;
    internal bool IsExiting => exitRequested || closing;
    internal bool OwnsSettings(SettingsWindow window) => settingsWindow == window;
    bool restoreSettingsAfterMinimise;
    bool restoringOverlay;
    bool placingAutoAvoid, autoAvoidSnapPending, draggingOverlay;
    public Button MinimiseButton { get; }
    public OverlayWindow(SettingsStore store, OverlaySettings settings, bool verification = false, Func<Point>? cursorPosition = null, DownloadsCleanup? downloads = null, PowerOptionsController? powerOptions = null, Func<ProcessStartInfo, bool>? restartStart = null, HardwareInventoryController? hardwareInventory = null)
    {
        Store = store; Settings = settings;
        this.verification = verification;
        HardwareInventory = hardwareInventory ?? new();
        this.restartStart = restartStart;
        PowerOptions = powerOptions ?? new PowerOptionsController(settings, store);
        Downloads = downloads ?? new DownloadsCleanup();
        StorageSense = new StorageSenseController(settings, store);
        AutomaticCompression = new AutomaticCompressionController(settings, store);
        this.cursorPosition = cursorPosition ?? (() => { var cursor = Forms.Cursor.Position; return new Point(cursor.X, cursor.Y); });
        Foreground = Palette.Text; FontFamily = new FontFamily("Segoe UI"); FontSize = 13;
        Title = "Geurts Performance Prefect"; Width = 318; SizeToContent = SizeToContent.WidthAndHeight;
        Icon = Branding.WindowIcon;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; AllowsTransparency = true; Background = Brushes.Transparent;
        ShowInTaskbar = !settings.MinimiseToTray; Topmost = settings.AlwaysOnTop;
        Left = settings.Left; Top = settings.Top;
        surface = new Border { Width = 318, Background = Palette.Background, BorderBrush = Palette.Track, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(13), Padding = new Thickness(14) };
        var stack = new StackPanel(); surface.Child = stack; Content = surface;
        header = new Grid { Margin = new Thickness(0,0,0,13), Background = Brushes.Transparent, Cursor = Cursors.SizeAll };
        header.ColumnDefinitions.Add(new ColumnDefinition()); header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = new StackPanel();
        title.Children.Add(new TextBlock { Text = "Geurts Performance\nPrefect", FontWeight = FontWeights.Bold, FontSize = 14, Foreground = Palette.Accent });
        title.Children.Add(new TextBlock { Text = "Live system monitor", FontSize = 11, Foreground = Palette.Muted, Margin = new Thickness(0,3,0,0) });
        header.Children.Add(title);
        var settingsButton = new Button { Content = "Settings", Padding = new Thickness(9,6,9,6), VerticalAlignment = VerticalAlignment.Center, Cursor = Cursors.Hand };
        settingsButton.Click += (_, _) => OpenSettings();
        var headerButtons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        MinimiseButton = new Button { Content = "—", ToolTip = "Minimise", Width = 28, Padding = new Thickness(0,6,0,6), Margin = new Thickness(0,0,6,0), Cursor = Cursors.Hand };
        System.Windows.Automation.AutomationProperties.SetName(MinimiseButton, "Minimise");
        MinimiseButton.Click += (_, _) => Minimise(); headerButtons.Children.Add(MinimiseButton); headerButtons.Children.Add(settingsButton);
        Grid.SetColumn(headerButtons, 1); header.Children.Add(headerButtons);
        standardTitle = title; standardButtons = headerButtons;
        compactHeader = new Grid { Visibility = Visibility.Collapsed };
        compactHeader.ColumnDefinitions.Add(new()); compactHeader.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        compactHeader.RowDefinitions.Add(new() { Height = GridLength.Auto }); compactHeader.RowDefinitions.Add(new() { Height = GridLength.Auto });
        tacticalBrand = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        tacticalEmblem = new Border { Width = 36, Height = 40, BorderBrush = CompactHud.Accent, BorderThickness = new Thickness(1),
            Margin = new Thickness(0,0,10,0), Child = new TextBlock { Text = "G+", FontSize = 18, FontWeight = FontWeights.Bold,
                Foreground = CompactHud.Accent, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
        tacticalBrand.Children.Add(tacticalEmblem);
        var brandText = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        tacticalKicker = new TextBlock { Text = "GEURTS PERFORMANCE", FontFamily = new FontFamily("Consolas"), FontSize = 10, Foreground = CompactHud.Muted };
        tacticalName = new TextBlock { FontFamily = new FontFamily("Bahnschrift"), FontWeight = FontWeights.SemiBold, Foreground = CompactHud.Text };
        brandText.Children.Add(tacticalKicker); brandText.Children.Add(tacticalName); tacticalBrand.Children.Add(brandText);
        compactHeader.Children.Add(tacticalBrand);
        tacticalSubhead = new TextBlock { Text = "HUD / OVERLAY", FontFamily = new FontFamily("Consolas"), FontSize = 10,
            Foreground = CompactHud.Muted, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetRow(tacticalSubhead,1); compactHeader.Children.Add(tacticalSubhead);
        tacticalButtons = new StackPanel { Orientation = Orientation.Horizontal };
        pinButton = CompactHud.Button("\uE718", "Keep HUD on top", () => { Settings.AlwaysOnTop = !Settings.AlwaysOnTop; Changed(); });
        collapseButton = CompactHud.Button("\uE70E", "Collapse HUD", () => { hudCollapsed = !hudCollapsed; ApplyHudContentVisibility(); QueueAutoAvoidSnap(); });
        var compactSettings = CompactHud.Button("\uE713", "Settings", OpenSettings);
        tacticalButtons.Children.Add(pinButton); tacticalButtons.Children.Add(collapseButton); tacticalButtons.Children.Add(compactSettings);
        Grid.SetColumn(tacticalButtons,1); compactHeader.Children.Add(tacticalButtons);
        Grid.SetColumnSpan(compactHeader,2); header.Children.Add(compactHeader);
        header.MouseLeftButtonDown += (_, e) =>
        {
            if (e.OriginalSource is DependencyObject source && IsInsideButton(source)) return;
            if (e.ClickCount == 2) OpenSettings();
            else if (!Settings.AutoAvoid)
            {
                draggingOverlay = true;
                try { DragMove(); }
                catch (InvalidOperationException) { }
                finally { draggingOverlay = false; }
                UpdateOverlayHeight(); ClampToScreen(); Changed();
            }
        };
        stack.Children.Add(header);
        var readingsStack = new StackPanel();
        readingsScroll = new ScrollViewer { Content = readingsStack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        stack.Children.Add(readingsScroll);
        foreach (var metric in MetricInfo.All) { var row = new MetricRow(metric); rows.Add(metric, row); readingsStack.Children.Add(row); }
        readingsStack.Children.Add(driveRows);
        empty = new TextBlock { Text = "All readings are hidden.\nChoose what to show in Settings.", Foreground = Palette.Muted, Margin = new Thickness(0,12,0,18), TextWrapping = TextWrapping.Wrap };
        stack.Children.Add(empty);
        status = new TextBlock { Text = "Connecting to sensors…", FontSize = 10, Foreground = Palette.Muted, Margin = new Thickness(0,5,0,0), TextWrapping = TextWrapping.Wrap };
        stack.Children.Add(status);
        var menu = new ContextMenu();
        autoAvoidMenuItem = new MenuItem { Header = "Auto-avoid", IsCheckable = true, IsChecked = Settings.AutoAvoid };
        autoAvoidMenuItem.Click += (_, _) => SetAutoAvoid(autoAvoidMenuItem.IsChecked); menu.Items.Add(autoAvoidMenuItem);
        var minimiseItem = new MenuItem { Header = "Minimise" }; minimiseItem.Click += (_, _) => Minimise(); menu.Items.Add(minimiseItem);
        var settingsItem = new MenuItem { Header = "Settings" }; settingsItem.Click += (_, _) => OpenSettings(); menu.Items.Add(settingsItem);
        var installationItem = new MenuItem { Header = "Open Installation Folder" }; installationItem.Click += (_, _) => OpenInstallationFolder(); menu.Items.Add(installationItem);
        var shortcutItem = new MenuItem { Header = "Create desktop shortcut" }; shortcutItem.Click += (_, _) => CreateDesktopShortcut(); menu.Items.Add(shortcutItem);
        var exitItem = new MenuItem { Header = "Exit Geurts Performance Prefect" }; exitItem.Click += (_, _) => Close(); menu.Items.Add(exitItem); ContextMenu = menu;
        saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        saveTimer.Tick += (_, _) => { saveTimer.Stop(); SaveSettings(); };
        staleTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        staleTimer.Tick += (_, _) =>
        {
            if (Latest != null && DateTimeOffset.Now - Latest.Time > TimeSpan.FromSeconds(Math.Max(10, refreshMilliseconds / 1000 * 3)))
            {
                foreach (var row in rows.Values) row.Update(new(null, "Sensor readings have stopped updating"));
                foreach (var row in DriveRows.Values) row.Update(new(null, "Drive readings have stopped updating"));
                status.Text = "Readings stopped updating · restart the app";
            }
        };
        AutoAvoidTrayItem = new Forms.ToolStripMenuItem("Auto-avoid") { CheckOnClick = true, Checked = Settings.AutoAvoid };
        AutoAvoidTrayItem.Click += (_, _) => Dispatcher.Invoke(() => SetAutoAvoid(AutoAvoidTrayItem.Checked));
        trayMenu.Items.Add("Restore", null, (_, _) => Dispatcher.Invoke(RestoreOverlay));
        trayMenu.Items.Add("Minimise", null, (_, _) => Dispatcher.Invoke(Minimise));
        trayMenu.Items.Add(AutoAvoidTrayItem);
        trayMenu.Items.Add("Settings", null, (_, _) => Dispatcher.Invoke(OpenSettings));
        trayMenu.Items.Add("Open Installation Folder", null, (_, _) => Dispatcher.Invoke(OpenInstallationFolder));
        trayMenu.Items.Add("Create desktop shortcut", null, (_, _) => Dispatcher.Invoke(CreateDesktopShortcut));
        RestartTrayItem = new Forms.ToolStripMenuItem("Restart application");
        RestartTrayItem.Click += (_, _) => Dispatcher.Invoke(() => RestartApplication());
        trayMenu.Items.Add(RestartTrayItem);
        trayMenu.Items.Add("Exit", null, (_, _) => Dispatcher.Invoke(Close));
        if (!verification)
        {
            trayIcon = Branding.CreateTrayIcon();
            tray = new Forms.NotifyIcon { Icon = trayIcon, Text = "Geurts Performance Prefect", Visible = true };
            tray.ContextMenuStrip = trayMenu;
            tray.DoubleClick += (_, _) => Dispatcher.Invoke(RestoreOverlay);
        }
        Loaded += (_, _) =>
        {
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplaysChanged;
            UpdateOverlayHeight();
            ClampToScreen();
            QueueAutoAvoidSnap();
            if (!verification) StorageSense.Start();
            if (!verification) AutomaticCompression.Start();
            if (!verification) _ = HardwareInventory.RefreshAsync();
            if (!verification && worker == null) { staleTimer.Start(); worker = Task.Factory.StartNew(SampleLoop, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default); }
        };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.F2) { OpenSettings(); e.Handled = true; } };
        Closing += OnClosing;
        MouseEnter += (_, _) => AvoidPointer(this.cursorPosition());
        SizeChanged += (_, _) => QueueAutoAvoidSnap();
        LocationChanged += (_, _) => { if (!placingAutoAvoid) QueueAutoAvoidSnap(); };
        DpiChanged += (_, _) => { UpdateOverlayHeight(); QueueAutoAvoidSnap(); };
        StateChanged += (_, _) =>
        {
            if (closing) return;
            if (WindowState == WindowState.Minimized) ApplyMinimise();
            else if (WindowState == WindowState.Normal && !restoringOverlay) RestoreSettingsAfterMinimise();
        };
        ApplySettings();
    }
    static bool IsInsideButton(DependencyObject obj)
    {
        for (DependencyObject? current = obj; current != null; current = VisualTreeHelper.GetParent(current))
            if (current is Button) return true;
        return false;
    }
    void SampleLoop()
    {
        using var sampler = new HardwareSampler(Fans);
        sampler.Open();
        while (!stop.IsCancellationRequested)
        {
            Snapshot snapshot;
            try { snapshot = sampler.Sample(Volatile.Read(ref monitorFps)); }
            catch (Exception ex)
            {
                var fanReadings = Fans.Process(Array.Empty<FanEndpoint>(), Array.Empty<SensorValue>());
                snapshot = new(DateTimeOffset.Now, Array.Empty<SensorValue>(), null, null, "", ex.Message)
                    { FanControls = fanReadings, FanStatus = Fans.Status };
            }
            if (stop.IsCancellationRequested) break;
            Dispatcher.BeginInvoke(() => { if (!closing) Receive(snapshot); });
            var delay = Volatile.Read(ref refreshMilliseconds);
            if (Fans.HasWork) delay = Math.Min(delay, 1000);
            if (stop.Token.WaitHandle.WaitOne(delay)) break;
        }
    }
    public FanControlService Fans { get; } = new();
    public void Receive(Snapshot snapshot)
    {
        snapshot = HardwareInventoryParser.Enrich(snapshot, HardwareInventory.Current);
        Latest = snapshot;
        applicationHistory.Add(snapshot.Applications);
        var readings = SensorSelection.Select(snapshot, Settings);
        TopApplication Top(string resource) => snapshot.Applications == null ? new(null, 0, 0, "Waiting for application samples") :
            applicationHistory.Top(resource, Settings.ApplicationAverageSeconds);
        foreach (var pair in readings)
        {
            if (pair.Key == Metric.ForegroundFps)
                rows[pair.Key].SetLabel(DesktopCompositionFps.IsDesktopReading(pair.Value) ? "Desktop FPS" : MetricInfo.Label(pair.Key));
            if (pair.Key == Metric.MotherboardTemperature)
                rows[pair.Key].SetLabel(pair.Value.Detail.StartsWith("ACPI thermal zone ", StringComparison.Ordinal)
                    ? "System temperature (ACPI)" : MetricInfo.Label(pair.Key));
            var resource = pair.Key switch
            {
                Metric.CpuUsage => UsageResource.Cpu, Metric.MemoryUsage => UsageResource.Memory,
                Metric.GpuUsage => UsageResource.Gpu(SensorSelection.GraphicsId(snapshot, Settings)), _ => null
            };
            rows[pair.Key].Update(pair.Value, resource == null ? null : Top(resource), Settings.ApplicationAverageSeconds, resource);
        }
        foreach (var id in DriveRows.Keys.Where(id => !snapshot.Drives.Any(d => d.Id == id)).ToArray())
        { driveRows.Children.Remove(DriveRows[id]); DriveRows.Remove(id); }
        foreach (var drive in snapshot.Drives)
        {
            if (!DriveRows.TryGetValue(drive.Id, out var row))
            { row = new MetricRow(drive.Label + " usage"); DriveRows.Add(drive.Id, row); }
            row.ApplyLayout(Settings.HudLayout);
            row.SetLabel(drive.Label + " usage");
            var resource = UsageResource.Drive(drive.Id);
            row.Update(drive.Reading, Top(resource), Settings.ApplicationAverageSeconds, resource);
            row.Visibility = Settings.IsDriveVisible(drive.Id) ? Visibility.Visible : Visibility.Collapsed;
        }
        // Preserve disk-number order after reconnecting or discovering a new drive.
        var orderedRows = snapshot.Drives.OrderBy(d => d.Index).Select(d => DriveRows[d.Id]).ToArray();
        if (!driveRows.Children.OfType<MetricRow>().SequenceEqual(orderedRows))
        { driveRows.Children.Clear(); foreach (var row in orderedRows) driveRows.Children.Add(row); }
        UpdateEmpty();
        var missing = readings.Count(p => Settings.Visible[p.Key] && !p.Value.Value.HasValue);
        missing += snapshot.Drives.Count(d => Settings.IsDriveVisible(d.Id) && !d.Activity.HasValue);
        status.Text = missing > 0 ? $"{missing} reading{(missing == 1 ? "" : "s")} unavailable · check Settings" : $"LIVE   •   Updated {snapshot.Time:HH:mm:ss}";
        if (snapshot.Error != null) status.Text = "Some sensors could not be read · check Settings";
        if (snapshot.DriveError != null) status.Text = "Drive readings unavailable · check Settings";
        status.ToolTip = string.Join("\n", new[] { snapshot.Error, snapshot.DriveError }.Where(e => e != null));
        SnapshotReceived?.Invoke(snapshot);
    }
    public void ApplySettings()
    {
        Settings.Normalise(); Topmost = Settings.AlwaysOnTop; Opacity = Settings.Opacity;
        ApplyHudLayout();
        ShowInTaskbar = !Settings.MinimiseToTray;
        header.Visibility = Settings.AutoAvoid ? Visibility.Collapsed : Visibility.Visible;
        if (WindowState == WindowState.Minimized) ApplyMinimise();
        surface.LayoutTransform = new ScaleTransform(Settings.Scale, Settings.Scale);
        UpdateOverlayHeight();
        foreach (var pair in rows) pair.Value.Visibility = Settings.Visible[pair.Key] ? Visibility.Visible : Visibility.Collapsed;
        foreach (var pair in DriveRows) pair.Value.Visibility = Settings.IsDriveVisible(pair.Key) ? Visibility.Visible : Visibility.Collapsed;
        UpdateEmpty();
        Volatile.Write(ref refreshMilliseconds, Settings.RefreshMilliseconds);
        Volatile.Write(ref monitorFps, Settings.Visible[Metric.ForegroundFps]);
        settingsWindow?.ApplyTopmostPreference();
        if (Latest != null) Receive(Latest);
        AutoAvoidTrayItem.Checked = Settings.AutoAvoid; autoAvoidMenuItem.IsChecked = Settings.AutoAvoid;
        AutoAvoidChanged?.Invoke(Settings.AutoAvoid);
        QueueAutoAvoidSnap();
    }
    public void Changed() { ApplySettings(); saveTimer.Stop(); saveTimer.Start(); SettingsChanged?.Invoke(); }
    void UpdateEmpty() => empty.Visibility = hudCollapsed || rows.Values.Concat(DriveRows.Values).Any(r => r.Visibility == Visibility.Visible) ? Visibility.Collapsed : Visibility.Visible;
    void ApplyHudLayout()
    {
        var selected = Settings.HudLayout;
        if (appliedLayout != selected || Settings.AutoAvoid) hudCollapsed = false;
        appliedLayout = selected;
        var standard = selected == HudLayout.Standard;
        var fullVertical = selected == HudLayout.StandardVertical;
        surface.Width = CompactHud.Width(selected);
        Width = double.NaN;
        surface.Background = standard ? Palette.Background : CompactHud.Background;
        surface.BorderBrush = standard ? Palette.Track : CompactHud.Line;
        surface.BorderThickness = standard ? new Thickness(1) : new Thickness(1,3,1,1);
        surface.CornerRadius = new CornerRadius(standard ? 13 : 0);
        surface.Padding = new Thickness(standard || fullVertical ? 14 : 9);
        FontFamily = new FontFamily(standard ? "Segoe UI" : "Bahnschrift");
        header.Margin = new Thickness(0,0,0,standard ? 13 : 6);
        standardTitle.Visibility = standard ? Visibility.Visible : Visibility.Collapsed;
        standardButtons.Visibility = standard ? Visibility.Visible : Visibility.Collapsed;
        compactHeader.Visibility = standard ? Visibility.Collapsed : Visibility.Visible;
        tacticalEmblem.Visibility = tacticalKicker.Visibility = tacticalSubhead.Visibility = fullVertical ? Visibility.Visible : Visibility.Collapsed;
        tacticalName.Text = fullVertical ? "PREFECT" : "G+ PREFECT";
        tacticalName.FontSize = fullVertical ? 18 : 14;
        tacticalBrand.Margin = new Thickness(0,0,0,fullVertical ? 12 : 0);
        Grid.SetColumnSpan(tacticalBrand,fullVertical ? 2 : 1);
        Grid.SetRow(tacticalButtons,fullVertical ? 1 : 0);
        foreach (Button button in tacticalButtons.Children)
        {
            button.Width = button.Height = button.MinHeight = fullVertical ? 31 : 25;
            button.Margin = new Thickness(5,0,0,fullVertical ? 0 : 4);
        }
        pinButton.Foreground = Settings.AlwaysOnTop ? CompactHud.Accent : CompactHud.Muted;
        pinButton.ToolTip = Settings.AlwaysOnTop ? "Unpin HUD" : "Keep HUD on top";
        foreach (var row in rows.Values.Concat(DriveRows.Values)) row.ApplyLayout(selected);
        status.FontSize = standard || fullVertical ? 10 : 9;
        status.Foreground = standard ? Palette.Muted : CompactHud.Muted;
        empty.Foreground = standard ? Palette.Muted : CompactHud.Muted;
        ApplyHudContentVisibility();
    }
    void ApplyHudContentVisibility()
    {
        readingsScroll.Visibility = hudCollapsed ? Visibility.Collapsed : Visibility.Visible;
        status.Visibility = hudCollapsed ? Visibility.Collapsed : Visibility.Visible;
        UpdateEmpty();
        collapseButton.Content = hudCollapsed ? "\uE70D" : "\uE70E";
        collapseButton.ToolTip = hudCollapsed ? "Expand HUD" : "Collapse HUD";
        System.Windows.Automation.AutomationProperties.SetName(collapseButton,hudCollapsed ? "Expand HUD" : "Collapse HUD");
    }
    public void SaveSettings()
    {
        UpdateSavedPosition();
        if (!Store.Save(Settings)) { status.Text = "Settings could not be saved"; status.ToolTip = Store.LastError; }
    }
    void UpdateSavedPosition()
    {
        var bounds = RestoreBounds;
        Settings.Left = WindowState != WindowState.Normal && !bounds.IsEmpty ? bounds.Left : Left;
        Settings.Top = WindowState != WindowState.Normal && !bounds.IsEmpty ? bounds.Top : Top;
    }
    public void ResetPosition()
    {
        if (Settings.AutoAvoid) { Settings.AutoAvoidCorner = Enum.GetValues<ScreenCorner>().First(Settings.AutoAvoidAvailableCorners.Contains); SnapAutoAvoid(); }
        else if (OverlayScreenPosition.Read(new System.Windows.Interop.WindowInteropHelper(this).Handle, out var bounds))
        {
            var area = ActiveDisplay.WorkArea;
            var dpi = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice.M11 ?? 1;
            PlaceAutoAvoid(new Rect(OverlayDisplay.Clamp(area, new Rect(new Point(area.Left + 24 * dpi, area.Top + 24 * dpi), bounds.Size)), bounds.Size), bounds);
        }
        Changed();
    }
    internal Func<OverlayDisplay[]> DisplaySource { get; set; } = OverlayDisplay.Read;
    internal OverlayDisplay[] AvailableDisplays => DisplaySource();
    internal OverlayDisplay ActiveDisplay => OverlayDisplay.Resolve(AvailableDisplays, Settings.OverlayMonitorId,
        Forms.Screen.FromHandle(new System.Windows.Interop.WindowInteropHelper(this).Handle).DeviceName);
    public void SetOverlayMonitor(string id)
    {
        if (Settings.OverlayMonitorId == id) return;
        Settings.OverlayMonitorId = id;
        UpdateOverlayHeight();
        if (Settings.AutoAvoid) { Changed(); SnapAutoAvoid(); }
        else ResetPosition();
        SaveSettings();
    }
    void UpdateOverlayHeight()
    {
        var dpi = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice.M22 ?? 1;
        readingsScroll.MaxHeight = Math.Max(1, (ActiveDisplay.WorkArea.Height / dpi - 190) / Settings.Scale);
    }
    void OnDisplaysChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(new Action(RefreshDisplays));
    internal void RefreshDisplays()
    {
        if (closing) return;
        UpdateOverlayHeight();
        QueueAutoAvoidSnap();
        DisplaysChanged?.Invoke();
    }
    public void SetAutoAvoid(bool enabled)
    {
        if (Settings.AutoAvoid == enabled) return;
        if (enabled && IsVisible && WindowState == WindowState.Normal && TryGetAutoAvoidLayout(out var workArea, out var bounds, out var margin))
            Settings.AutoAvoidCorner = AutoAvoidPlacement.Nearest(workArea, bounds, margin, Settings.AutoAvoidAvailableCorners);
        Settings.AutoAvoid = enabled; Changed();
        if (enabled) { SnapAutoAvoid(); AvoidPointer(cursorPosition()); }
        SaveSettings();
    }
    public void SetAutoAvoidCornerAvailable(ScreenCorner corner, bool available)
    {
        if (!Enum.IsDefined(corner)) return;
        if (available) Settings.AutoAvoidAvailableCorners.Add(corner);
        else if (Settings.AutoAvoidAvailableCorners.Count > 1) Settings.AutoAvoidAvailableCorners.Remove(corner);
        Changed();
        SnapAutoAvoid();
        SaveSettings();
    }
    internal bool TryGetAutoAvoidLayout(out Rect workArea, out Rect bounds, out double margin)
    {
        var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        workArea = ActiveDisplay.WorkArea;
        margin = 12 * (PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice.M11 ?? 1);
        return OverlayScreenPosition.Read(handle, out bounds);
    }
    void QueueAutoAvoidSnap()
    {
        if (closing || placingAutoAvoid || draggingOverlay || autoAvoidSnapPending || !IsVisible || WindowState != WindowState.Normal) return;
        autoAvoidSnapPending = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            autoAvoidSnapPending = false;
            if (closing || draggingOverlay || !IsVisible || WindowState != WindowState.Normal) return;
            UpdateOverlayHeight();
            if (Settings.AutoAvoid) SnapAutoAvoid();
            else ClampToScreen();
        }));
    }
    internal void SnapAutoAvoid()
    {
        if (!Settings.AutoAvoid || closing || placingAutoAvoid || !IsVisible || WindowState != WindowState.Normal) return;
        if (TryGetAutoAvoidLayout(out var area, out var bounds, out var margin))
        {
            if (!Settings.AutoAvoidAvailableCorners.Contains(Settings.AutoAvoidCorner))
                Settings.AutoAvoidCorner = AutoAvoidPlacement.Nearest(area, bounds, margin, Settings.AutoAvoidAvailableCorners);
            var cursor = cursorPosition();
            var target = AutoAvoidPlacement.AtCorner(area, bounds.Size, Settings.AutoAvoidCorner, margin);
            // A resize can grow the selected corner back underneath a stationary cursor.
            if (target.Contains(cursor))
                Settings.AutoAvoidCorner = AutoAvoidPlacement.AwayFrom(area, bounds, cursor, margin, Settings.AutoAvoidAvailableCorners) ?? Settings.AutoAvoidCorner;
            PlaceAutoAvoid(AutoAvoidPlacement.AtCorner(area, bounds.Size, Settings.AutoAvoidCorner, margin), bounds);
        }
    }
    internal bool AvoidPointer(Point cursor)
    {
        if (!Settings.AutoAvoid || closing || placingAutoAvoid || !IsVisible || WindowState != WindowState.Normal || ContextMenu?.IsOpen == true) return false;
        if (!TryGetAutoAvoidLayout(out var area, out var bounds, out var margin) || !bounds.Contains(cursor)) return false;
        var corner = AutoAvoidPlacement.AwayFrom(area, bounds, cursor, margin, Settings.AutoAvoidAvailableCorners);
        if (corner == null) return false;
        Settings.AutoAvoidCorner = corner.Value;
        PlaceAutoAvoid(AutoAvoidPlacement.AtCorner(area, bounds.Size, corner.Value, margin), bounds);
        saveTimer.Stop(); saveTimer.Start(); return true;
    }
    void PlaceAutoAvoid(Rect target, Rect current)
    {
        if (Math.Abs(target.Left - current.Left) < 1 && Math.Abs(target.Top - current.Top) < 1) return;
        placingAutoAvoid = true;
        try { OverlayScreenPosition.Move(new System.Windows.Interop.WindowInteropHelper(this).Handle, target.TopLeft); saveTimer.Stop(); saveTimer.Start(); }
        catch (Win32Exception ex) { status.Text = "Could not move the overlay"; status.ToolTip = ex.Message; }
        finally { placingAutoAvoid = false; }
        QueueAutoAvoidSnap();
    }
    void ClampToScreen()
    {
        if (closing || !IsVisible || WindowState != WindowState.Normal) return;
        if (OverlayScreenPosition.Read(new System.Windows.Interop.WindowInteropHelper(this).Handle, out var bounds))
            PlaceAutoAvoid(new Rect(OverlayDisplay.Clamp(ActiveDisplay.WorkArea, bounds), bounds.Size), bounds);
    }
    public void OpenInstallationFolder()
    {
        try { Process.Start(new ProcessStartInfo(AppContext.BaseDirectory) { UseShellExecute = true }); }
        catch (Exception ex) { MessageBox.Show("Could not open the installation folder: " + ex.Message, "Geurts Performance Prefect"); }
    }
    public void CreateDesktopShortcut()
    {
        try { MessageBox.Show("Desktop shortcut ready: " + DesktopShortcut.Create(), "Geurts Performance Prefect"); }
        catch (Exception ex) { MessageBox.Show("Could not create the desktop shortcut: " + ex.Message, "Geurts Performance Prefect", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
    public void OpenSettings() => OpenSettings(window => new SettingsWindow(window));
    internal void OpenSettings(Func<OverlayWindow, SettingsWindow> create)
    {
        if (IsExiting) return;
        RestoreOverlay();
        if (settingsWindow == null)
        {
            settingsWindow = create(this);
            settingsWindow.Closed += (_, _) => settingsWindow = null;
        }
        settingsWindow.ShowInForeground();
    }
    internal void OpenConsole()
    {
        OpenSettings(); settingsWindow!.ShowConsole();
    }
    internal void OpenSecurity()
    {
        OpenSettings(); settingsWindow!.ShowSecurity();
    }
    public void Minimise()
    {
        if (closing) return;
        SaveSettings();
        if (WindowState == WindowState.Minimized) ApplyMinimise();
        else WindowState = WindowState.Minimized;
    }
    void ApplyMinimise()
    {
        if (settingsWindow?.IsVisible == true)
        {
            restoreSettingsAfterMinimise = true;
            settingsWindow.Hide(); settingsWindow.WindowState = WindowState.Maximized;
        }
        if (Settings.MinimiseToTray) { ShowInTaskbar = false; Hide(); }
        else { ShowInTaskbar = true; if (!IsVisible) Show(); }
    }
    public void RestoreOverlay()
    {
        if (closing) return;
        ShowInTaskbar = !Settings.MinimiseToTray;
        restoringOverlay = true;
        try
        {
            WindowState = WindowState.Normal; Show();
            OverlayScreenPosition.Restore(new System.Windows.Interop.WindowInteropHelper(this).Handle);
            ClampToScreen(); Activate();
        }
        finally { restoringOverlay = false; }
        RestoreSettingsAfterMinimise();
        QueueAutoAvoidSnap();
    }
    void RestoreSettingsAfterMinimise()
    {
        if (!restoreSettingsAfterMinimise || settingsWindow == null) return;
        restoreSettingsAfterMinimise = false;
        settingsWindow.ShowInForeground();
    }
    internal bool RestartApplication()
    {
        if (IsExiting) return false;
        if (!AutomaticCompression.CanLeave()) { OpenSettings(); settingsWindow!.ShowFileCompression(); return false; }
        if (settingsWindow != null && !settingsWindow.AllowRestart()) return false;
        UpdateSavedPosition();
        var result = ApplicationRestart.Relaunch(Store, Settings, restartStart);
        if (!result.Started)
        {
            MessageBox.Show(this, result.Error, "Could not restart", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }
        Close();
        return true;
    }
    public void RestartElevated(bool openConsole = false, bool openSecurity = false)
    {
        if (IsExiting) return;
        if (HardwareSampler.IsAdministrator) return;
        if (!AutomaticCompression.CanLeave()) { OpenSettings(); settingsWindow!.ShowFileCompression(); return; }
        if (settingsWindow != null && !settingsWindow.AllowRestart()) return;
        SaveSettings();
        try
        {
            Process.Start(new ProcessStartInfo(Environment.ProcessPath!, "--wait-for " + Environment.ProcessId + (openConsole ? " --console" : openSecurity ? " --security" : "")) { UseShellExecute = true, Verb = "runas" });
            Close();
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) { /* User cancelled; keep this instance running. */ }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Could not restart"); }
    }
    void OnClosing(object? sender, CancelEventArgs e)
    {
        if (closing) return;
        // Idle verification/restart sessions retain synchronous close. User exits show
        // a non-modal warning and keep pumping the dispatcher until workers finish.
        if (!exitRequested && (verification && !HasExitWork)) { CompleteExit(); return; }
        e.Cancel = true;
        if (exitRequested) return;
        exitRequested = true;
        Dispatcher.BeginInvoke(new Action(ExitAsync));
    }
    bool HasExitWork => AutomaticCompression.IsBusy || StorageSense.IsBusy || Downloads.IsBusy || HardwareInventory.Busy ||
        settingsWindow?.HasExitWork == true;
    async void ExitAsync()
    {
        var warning = new ExitWarning();
        if (!verification) warning.Show();
        try
        {
            IsEnabled = false;
            AutomaticCompression.Dispose(); StorageSense.Dispose(); Downloads.Stop();
            HardwareInventory.Dispose();
            settingsWindow?.StopForExit();
            stop.Cancel();
            // A current compression/deletion finishes before disposal. Do not block
            // the dispatcher: worker completions and console output need it.
            var visibleUntil = DateTime.UtcNow.AddMilliseconds(1200);
            while (HasExitWork || worker?.IsCompleted == false || (!verification && DateTime.UtcNow < visibleUntil))
                await Task.Delay(50);
            if (settingsWindow != null) await settingsWindow.DiskAnalyserPanel.ExitCleanup;
            CompleteExit();
            Close();
        }
        catch (Exception ex)
        {
            // Never abandon a failed cleanup or claim that the app has exited.
            warning.ReportFailure(ex.Message);
            IsEnabled = true; exitRequested = false;
            return;
        }
        warning.Close();
    }
    void CompleteExit()
    {
        closing = true; saveTimer.Stop(); staleTimer.Stop(); stop.Cancel();
        StorageSense.Dispose();
        HardwareInventory.Dispose();
        AutomaticCompression.Dispose();
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplaysChanged;
        settingsWindow?.CloseAfterExit(); SaveSettings(); tray?.Dispose(); trayIcon?.Dispose(); trayMenu.Dispose();
        // Sampling owns the monitor and disposes it; never race its hardware update.
        worker?.Wait(TimeSpan.FromSeconds(8));
    }
}
