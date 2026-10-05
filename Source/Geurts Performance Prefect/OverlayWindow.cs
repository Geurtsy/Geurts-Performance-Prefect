using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace GeurtsPerformancePrefect;

public sealed class MetricRow : Border
{
    readonly TextBlock value, detail;
    readonly TextBlock application;
    readonly Border fill;
    readonly bool temperature;
    readonly TextBlock label;
    public MetricRow(Metric metric) : this(MetricInfo.Label(metric), MetricInfo.IsTemperature(metric)) { }
    public MetricRow(string name, bool temperature = false)
    {
        this.temperature = temperature;
        Background = Palette.Panel; CornerRadius = new CornerRadius(8); Padding = new Thickness(12,9,12,8); Margin = new Thickness(0,0,0,6);
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition()); grid.RowDefinitions.Add(new RowDefinition()); grid.RowDefinitions.Add(new RowDefinition()); grid.RowDefinitions.Add(new RowDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition()); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        label = new TextBlock { Text = name, Foreground = Palette.Muted, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        grid.Children.Add(label);
        value = new TextBlock { Text = "—", FontSize = 22, FontWeight = FontWeights.SemiBold, Foreground = Palette.Text };
        Grid.SetColumn(value, 1); grid.Children.Add(value);
        detail = new TextBlock { FontSize = 10, Foreground = Palette.Muted, Text = "Waiting for sensors", Margin = new Thickness(0,0,0,5), TextTrimming = TextTrimming.CharacterEllipsis };
        Grid.SetRow(detail, 1); Grid.SetColumnSpan(detail, 2); grid.Children.Add(detail);
        application = new TextBlock { FontSize = 10, Foreground = Palette.Accent, Margin = new Thickness(0,0,0,6), TextTrimming = TextTrimming.CharacterEllipsis, Visibility = Visibility.Collapsed };
        Grid.SetRow(application, 2); Grid.SetColumnSpan(application, 2); grid.Children.Add(application);
        var track = new Border { Background = Palette.Track, Height = 3, CornerRadius = new CornerRadius(2), ClipToBounds = true };
        fill = new Border { Background = Palette.Accent, Width = 0, HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(2) };
        track.Child = fill; Grid.SetRow(track, 3); Grid.SetColumnSpan(track, 2); grid.Children.Add(track);
        track.SizeChanged += (_, _) => UpdateFill();
        Child = grid;
    }
    double fraction;
    public void SetLabel(string name) => label.Text = name;
    void UpdateFill() => fill.Width = Math.Max(0, ((FrameworkElement)fill.Parent).ActualWidth * fraction);
    public string ApplicationText => application.Text;
    public void Update(Reading reading, TopApplication? topApplication = null, int averageSeconds = 60, string? resource = null)
    {
        var valid = reading.Value.HasValue && double.IsFinite(reading.Value.Value);
        value.Text = valid ? $"{reading.Value:0}{(temperature ? " °C" : " %")}" : "—";
        var separator = reading.Source.LastIndexOf(" / ", StringComparison.Ordinal);
        var sourceLabel = separator >= 0 ? reading.Source[(separator + 3)..] : reading.Source;
        detail.Text = valid ? (string.IsNullOrEmpty(reading.Detail) ? sourceLabel : reading.Detail) : "Unavailable · open Settings";
        ToolTip = valid ? reading.Source + "\n" + reading.Detail : reading.Source + "\nNo reading is available. Check Settings.";
        fraction = valid ? Math.Clamp(reading.Value!.Value / 100d, 0, 1) : 0;
        var hot = temperature && reading.Value >= 85;
        fill.Background = hot ? Palette.Amber : Palette.Accent;
        value.Foreground = hot ? Palette.Amber : Palette.Text;
        application.Visibility = resource == null ? Visibility.Collapsed : Visibility.Visible;
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
    public Snapshot? Latest { get; private set; }
    public event Action<Snapshot>? SnapshotReceived;
    public event Action<bool>? AutoAvoidChanged;
    readonly Dictionary<Metric, MetricRow> rows = new();
    readonly ApplicationUsageHistory applicationHistory = new();
    public IReadOnlyDictionary<Metric, MetricRow> MetricRows => rows;
    public Dictionary<string, MetricRow> DriveRows { get; } = new();
    readonly StackPanel driveRows = new();
    readonly ScrollViewer readingsScroll;
    readonly TextBlock status, empty;
    readonly Border surface;
    readonly Grid header;
    readonly Func<Point> cursorPosition;
    readonly DispatcherTimer saveTimer, staleTimer;
    readonly CancellationTokenSource stop = new();
    readonly Forms.NotifyIcon? tray;
    readonly System.Drawing.Icon? trayIcon;
    readonly Forms.ContextMenuStrip trayMenu = new();
    readonly MenuItem autoAvoidMenuItem;
    public Forms.ToolStripMenuItem AutoAvoidTrayItem { get; }
    Task? worker;
    SettingsWindow? settingsWindow;
    int refreshMilliseconds;
    bool closing;
    bool restoreSettingsAfterMinimise;
    bool restoringOverlay;
    bool placingAutoAvoid, autoAvoidSnapPending;
    public Button MinimiseButton { get; }
    public OverlayWindow(SettingsStore store, OverlaySettings settings, bool verification = false, Func<Point>? cursorPosition = null)
    {
        Store = store; Settings = settings;
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
        header.MouseLeftButtonDown += (_, e) =>
        {
            if (e.OriginalSource is DependencyObject source && IsInsideButton(source)) return;
            if (e.ClickCount == 2) OpenSettings();
            else if (!Settings.AutoAvoid) { try { DragMove(); } catch (InvalidOperationException) { } ClampToScreen(); Changed(); }
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
            ClampToScreen();
            QueueAutoAvoidSnap();
            if (!verification && worker == null) { staleTimer.Start(); worker = Task.Factory.StartNew(SampleLoop, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default); }
        };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.F2) { OpenSettings(); e.Handled = true; } };
        Closing += OnClosing;
        MouseEnter += (_, _) => AvoidPointer(this.cursorPosition());
        SizeChanged += (_, _) => QueueAutoAvoidSnap();
        LocationChanged += (_, _) => { if (!placingAutoAvoid) QueueAutoAvoidSnap(); };
        DpiChanged += (_, _) => QueueAutoAvoidSnap();
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
        using var sampler = new HardwareSampler();
        sampler.Open();
        while (!stop.IsCancellationRequested)
        {
            Snapshot snapshot;
            try { snapshot = sampler.Sample(); }
            catch (Exception ex) { snapshot = new(DateTimeOffset.Now, Array.Empty<SensorValue>(), null, null, "", ex.Message); }
            if (stop.IsCancellationRequested) break;
            Dispatcher.BeginInvoke(() => { if (!closing) Receive(snapshot); });
            if (stop.Token.WaitHandle.WaitOne(Volatile.Read(ref refreshMilliseconds))) break;
        }
    }
    public void Receive(Snapshot snapshot)
    {
        Latest = snapshot;
        applicationHistory.Add(snapshot.Applications);
        var readings = SensorSelection.Select(snapshot, Settings);
        TopApplication Top(string resource) => snapshot.Applications == null ? new(null, 0, 0, "Waiting for application samples") :
            applicationHistory.Top(resource, Settings.ApplicationAverageSeconds);
        foreach (var pair in readings)
        {
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
        ShowInTaskbar = !Settings.MinimiseToTray;
        header.Visibility = Settings.AutoAvoid ? Visibility.Collapsed : Visibility.Visible;
        if (WindowState == WindowState.Minimized) ApplyMinimise();
        surface.LayoutTransform = new ScaleTransform(Settings.Scale, Settings.Scale);
        readingsScroll.MaxHeight = Math.Max(150, (SystemParameters.WorkArea.Height - 190) / Settings.Scale);
        foreach (var pair in rows) pair.Value.Visibility = Settings.Visible[pair.Key] ? Visibility.Visible : Visibility.Collapsed;
        foreach (var pair in DriveRows) pair.Value.Visibility = Settings.IsDriveVisible(pair.Key) ? Visibility.Visible : Visibility.Collapsed;
        UpdateEmpty();
        Volatile.Write(ref refreshMilliseconds, Settings.RefreshMilliseconds);
        if (settingsWindow != null) settingsWindow.Topmost = Settings.AlwaysOnTop;
        if (Latest != null) Receive(Latest);
        AutoAvoidTrayItem.Checked = Settings.AutoAvoid; autoAvoidMenuItem.IsChecked = Settings.AutoAvoid;
        AutoAvoidChanged?.Invoke(Settings.AutoAvoid);
        QueueAutoAvoidSnap();
    }
    public void Changed() { ApplySettings(); saveTimer.Stop(); saveTimer.Start(); }
    void UpdateEmpty() => empty.Visibility = rows.Values.Concat(DriveRows.Values).Any(r => r.Visibility == Visibility.Visible) ? Visibility.Collapsed : Visibility.Visible;
    public void SaveSettings()
    {
        var bounds = RestoreBounds;
        Settings.Left = WindowState != WindowState.Normal && !bounds.IsEmpty ? bounds.Left : Left;
        Settings.Top = WindowState != WindowState.Normal && !bounds.IsEmpty ? bounds.Top : Top;
        if (!Store.Save(Settings)) { status.Text = "Settings could not be saved"; status.ToolTip = Store.LastError; }
    }
    public void ResetPosition()
    {
        if (Settings.AutoAvoid) { Settings.AutoAvoidCorner = ScreenCorner.TopLeft; SnapAutoAvoid(); }
        else { Left = 24; Top = 24; ClampToScreen(); }
        Changed();
    }
    public void SetAutoAvoid(bool enabled)
    {
        if (Settings.AutoAvoid == enabled) return;
        if (enabled && IsVisible && WindowState == WindowState.Normal && TryGetAutoAvoidLayout(out var workArea, out var bounds, out var margin))
            Settings.AutoAvoidCorner = AutoAvoidPlacement.Nearest(workArea, bounds, margin);
        Settings.AutoAvoid = enabled; Changed();
        if (enabled) { SnapAutoAvoid(); AvoidPointer(cursorPosition()); }
        SaveSettings();
    }
    internal bool TryGetAutoAvoidLayout(out Rect workArea, out Rect bounds, out double margin)
    {
        var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        var screen = Forms.Screen.FromHandle(handle);
        workArea = new Rect(screen.WorkingArea.Left, screen.WorkingArea.Top, screen.WorkingArea.Width, screen.WorkingArea.Height);
        margin = 12 * (PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice.M11 ?? 1);
        return OverlayScreenPosition.Read(handle, out bounds);
    }
    void QueueAutoAvoidSnap()
    {
        if (!Settings.AutoAvoid || closing || placingAutoAvoid || autoAvoidSnapPending || !IsVisible || WindowState != WindowState.Normal) return;
        autoAvoidSnapPending = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => { autoAvoidSnapPending = false; SnapAutoAvoid(); }));
    }
    internal void SnapAutoAvoid()
    {
        if (!Settings.AutoAvoid || closing || placingAutoAvoid || !IsVisible || WindowState != WindowState.Normal) return;
        if (TryGetAutoAvoidLayout(out var area, out var bounds, out var margin))
        {
            var cursor = cursorPosition();
            var target = AutoAvoidPlacement.AtCorner(area, bounds.Size, Settings.AutoAvoidCorner, margin);
            // A resize can grow the selected corner back underneath a stationary cursor.
            if (target.Contains(cursor))
                Settings.AutoAvoidCorner = AutoAvoidPlacement.AwayFrom(area, bounds, cursor, margin) ?? Settings.AutoAvoidCorner;
            PlaceAutoAvoid(AutoAvoidPlacement.AtCorner(area, bounds.Size, Settings.AutoAvoidCorner, margin), bounds);
        }
    }
    internal bool AvoidPointer(Point cursor)
    {
        if (!Settings.AutoAvoid || closing || placingAutoAvoid || !IsVisible || WindowState != WindowState.Normal || ContextMenu?.IsOpen == true) return false;
        if (!TryGetAutoAvoidLayout(out var area, out var bounds, out var margin) || !bounds.Contains(cursor)) return false;
        var corner = AutoAvoidPlacement.AwayFrom(area, bounds, cursor, margin);
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
    }
    void ClampToScreen()
    {
        // Convert physical monitor coordinates to WPF device-independent units.
        var screen = Forms.Screen.FromHandle(new System.Windows.Interop.WindowInteropHelper(this).Handle);
        var transform = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        var topLeft = transform.Transform(new Point(screen.WorkingArea.Left, screen.WorkingArea.Top));
        var bottomRight = transform.Transform(new Point(screen.WorkingArea.Right, screen.WorkingArea.Bottom));
        Left = Math.Clamp(Left, topLeft.X, Math.Max(topLeft.X, bottomRight.X - ActualWidth));
        Top = Math.Clamp(Top, topLeft.Y, Math.Max(topLeft.Y, bottomRight.Y - ActualHeight));
    }
    public void OpenSettings()
    {
        RestoreOverlay();
        if (settingsWindow == null)
        {
            settingsWindow = new SettingsWindow(this);
            settingsWindow.Closed += (_, _) => settingsWindow = null;
            settingsWindow.Show();
        }
        else { settingsWindow.WindowState = WindowState.Normal; settingsWindow.Activate(); }
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
            settingsWindow.Hide(); settingsWindow.WindowState = WindowState.Normal;
        }
        if (Settings.MinimiseToTray) { ShowInTaskbar = false; Hide(); }
        else { ShowInTaskbar = true; if (!IsVisible) Show(); }
    }
    public void RestoreOverlay()
    {
        if (closing) return;
        ShowInTaskbar = !Settings.MinimiseToTray;
        restoringOverlay = true;
        try { WindowState = WindowState.Normal; Show(); ClampToScreen(); Activate(); }
        finally { restoringOverlay = false; }
        RestoreSettingsAfterMinimise();
        QueueAutoAvoidSnap();
    }
    void RestoreSettingsAfterMinimise()
    {
        if (!restoreSettingsAfterMinimise || settingsWindow == null) return;
        restoreSettingsAfterMinimise = false;
        settingsWindow.WindowState = WindowState.Normal; settingsWindow.Show(); settingsWindow.Activate();
    }
    public void RestartElevated()
    {
        if (HardwareSampler.IsAdministrator) return;
        SaveSettings();
        try
        {
            Process.Start(new ProcessStartInfo(Environment.ProcessPath!, "--wait-for " + Environment.ProcessId) { UseShellExecute = true, Verb = "runas" });
            Close();
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) { /* User cancelled; keep this instance running. */ }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Could not restart"); }
    }
    void OnClosing(object? sender, CancelEventArgs e)
    {
        if (closing) return;
        closing = true; saveTimer.Stop(); staleTimer.Stop(); stop.Cancel();
        settingsWindow?.Close(); SaveSettings(); tray?.Dispose(); trayIcon?.Dispose(); trayMenu.Dispose();
        // Sampling owns the monitor and disposes it; never race its hardware update.
        worker?.Wait(TimeSpan.FromSeconds(2));
    }
}
