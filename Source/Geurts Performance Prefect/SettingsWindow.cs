using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace GeurtsPerformancePrefect;

public sealed class SettingsWindow : Window
{
    readonly OverlayWindow overlay;
    readonly ComboBox graphics = new(), board = new();
    readonly TextBlock sensorStatus;
    string choiceSignature = "";
    bool updatingChoices;
    readonly CoreParking parking;
    readonly TextBlock parkingStatus = Note("Reading the active power plan…");
    readonly Button parkingRefresh = new() { Content = "Refresh power plan", HorizontalAlignment = HorizontalAlignment.Left };
    bool updatingParking, parkingBusy;
    ParkingState? displayedParking;
    public CheckBox CoreParkingToggle { get; } = new() { Content = "Remove Core Parking", IsEnabled = false };
    public Dictionary<Metric, CheckBox> MetricToggles { get; } = new();
    public Dictionary<string, CheckBox> DriveToggles { get; } = new();
    readonly StackPanel driveToggles = new();
    readonly TextBlock driveStatus = Note("Detecting drives…");
    string driveSignature = "";
    public CheckBox TopmostToggle { get; }
    public CheckBox MinimiseToTrayToggle { get; }
    public CheckBox AutoAvoidToggle { get; }
    public ComboBox OverlayMonitorSelector { get; } = new();
    bool updatingMonitors;
    public Dictionary<ScreenCorner, CheckBox> AutoAvoidCornerToggles { get; } = new();
    bool updatingAutoAvoidCorners;
    public Slider ApplicationAverageSlider { get; }
    public Button CheckUpdatesButton { get; } = new() { Content = "Check for updates", HorizontalAlignment = HorizontalAlignment.Left };
    public Button InstallUpdateButton { get; } = new() { Content = "Install update and restart", HorizontalAlignment = HorizontalAlignment.Left, Visibility = Visibility.Collapsed, Margin = new Thickness(0,8,0,0) };
    public TextBlock UpdateStatus { get; } = Note("Check GitHub for a newer version.");
    readonly CancellationTokenSource updateStop = new();
    AppRelease? availableRelease;
    public CheckBox AutoWipeDownloadsToggle { get; } = new() { Content = "Auto-wipe Downloads on app startup" };
    public Button WipeDownloadsButton { get; } = new() { Content = "Wipe Downloads now…", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0,8,0,0) };
    public TextBlock DownloadsStatus { get; } = Note("");
    public TextBlock DownloadsPath { get; } = Note("");
    readonly Func<string, bool> confirmDownloads;
    bool updatingDownloads;
    readonly WindowsStartup startup;
    bool updatingStartup;
    public CheckBox StartWithWindowsToggle { get; } = new() { Content = "Start with Windows" };
    public TextBlock StartupStatus { get; } = Note("");
    public SettingsWindow(OverlayWindow overlay, CoreParking? parking = null, Func<string, bool>? confirmDownloads = null, WindowsStartup? startup = null)
    {
        this.overlay = overlay;
        this.parking = parking ?? new CoreParking();
        this.startup = startup ?? new WindowsStartup();
        this.confirmDownloads = confirmDownloads ?? (message => MessageBox.Show(this, message,
            "Geurts Performance Prefect · Downloads", MessageBoxButton.YesNo, MessageBoxImage.Warning,
            MessageBoxResult.No) == MessageBoxResult.Yes);
        Foreground = Palette.Text; Background = Palette.Background; FontFamily = new FontFamily("Segoe UI"); FontSize = 13;
        Title = "Geurts Performance Prefect · Settings"; Width = 500; Height = 820; MinWidth = 430; MinHeight = 440;
        Icon = Branding.WindowIcon;
        MaxHeight = SystemParameters.WorkArea.Height - 32; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Topmost = overlay.Settings.AlwaysOnTop;
        var dock = new DockPanel { Margin = new Thickness(24), Background = Palette.Background }; Content = dock;
        var footer = new StackPanel { Margin = new Thickness(0,16,0,0) };
        var footerButtons = new Grid(); footerButtons.ColumnDefinitions.Add(new()); footerButtons.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var quit = new Button { Content = "Exit application", HorizontalAlignment = HorizontalAlignment.Left }; quit.Click += (_, _) => overlay.Close();
        footerButtons.Children.Add(quit);
        var done = new Button { Content = "Done", MinWidth = 90 }; done.Click += (_, _) => Close(); Grid.SetColumn(done, 1); footerButtons.Children.Add(done);
        footer.Children.Add(new TextBlock { Text = "Overlay changes save automatically. Performance changes may require administrator approval.", TextWrapping = TextWrapping.Wrap, Foreground = Palette.Muted, FontSize = 11, Margin = new Thickness(0,0,0,10) });
        footer.Children.Add(footerButtons); DockPanel.SetDock(footer, Dock.Bottom); dock.Children.Add(footer);
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        var stack = new StackPanel { Margin = new Thickness(0,0,12,0) }; scroll.Content = stack; dock.Children.Add(scroll);
        stack.Children.Add(new TextBlock { Text = "Make it yours", FontSize = 26, FontWeight = FontWeights.SemiBold });
        stack.Children.Add(Note("Choose the readings you want at a glance."));
        stack.Children.Add(Heading("STARTUP"));
        stack.Children.Add(StartWithWindowsToggle);
        stack.Children.Add(Note("Off by default. Open the overlay automatically when you sign in to Windows. Applies to your Windows account and saves immediately; administrator approval is not needed."));
        stack.Children.Add(StartupStatus);
        stack.Children.Add(Note("Keep the application in the same folder. If you move it, turn this off and on from the new location. Windows Settings → Apps → Startup can also disable it."));
        StartWithWindowsToggle.Checked += (_, _) => ChangeStartup(true);
        StartWithWindowsToggle.Unchecked += (_, _) => ChangeStartup(false);
        RefreshStartup();
        Activated += (_, _) => RefreshStartup();
        stack.Children.Add(Heading("APPLICATION UPDATES"));
        stack.Children.Add(Note("Installed version: " + AppUpdates.CurrentVersion));
        var installationFolder = new Button { Content = "Open Installation Folder", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0,0,0,8) };
        installationFolder.Click += (_, _) => overlay.OpenInstallationFolder();
        stack.Children.Add(installationFolder);
        stack.Children.Add(CheckUpdatesButton);
        stack.Children.Add(InstallUpdateButton);
        stack.Children.Add(UpdateStatus);
        stack.Children.Add(Note("Updates come from the public Geurts Performance Prefect GitHub repository. Installing saves your preferences and restarts the app. Internet is needed only to check and download."));
        var repository = new Button { Content = "Open GitHub repository", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0,8,0,0) };
        repository.Click += (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo(AppUpdates.Repository) { UseShellExecute = true }); }
            catch (Exception ex) { UpdateStatus.Text = "Could not open the repository: " + ex.Message; }
        };
        stack.Children.Add(repository);
        var updateResult = AppUpdates.ReadResult();
        if (updateResult != null) UpdateStatus.Text = updateResult.Message;
        CheckUpdatesButton.Click += (_, _) => CheckUpdates();
        InstallUpdateButton.Click += (_, _) => InstallUpdate();
        Closed += (_, _) => updateStop.Cancel();
        stack.Children.Add(Heading("DOWNLOADS CLEANUP"));
        stack.Children.Add(Note("Permanently deletes files and subfolders from your Windows Downloads folder. Deleted items do not go to the Recycle Bin. Locked, protected and linked items are skipped."));
        stack.Children.Add(DownloadsPath);
        AutoWipeDownloadsToggle.IsChecked = overlay.Settings.AutoWipeDownloadsOnStartup;
        AutoWipeDownloadsToggle.Checked += (_, _) => ChangeAutoWipeDownloads(true);
        AutoWipeDownloadsToggle.Unchecked += (_, _) => ChangeAutoWipeDownloads(false);
        stack.Children.Add(AutoWipeDownloadsToggle);
        stack.Children.Add(Note("Off by default. When enabled, wipes once each time this app starts, including after a restart or update. Enabling takes effect on the next startup."));
        WipeDownloadsButton.Click += (_, _) => WipeDownloads();
        stack.Children.Add(WipeDownloadsButton);
        stack.Children.Add(DownloadsStatus);
        overlay.Downloads.Changed += UpdateDownloads;
        Closed += (_, _) => overlay.Downloads.Changed -= UpdateDownloads;
        UpdateDownloads();
        stack.Children.Add(Heading("PERFORMANCE"));
        stack.Children.Add(CoreParkingToggle);
        stack.Children.Add(Note("Keep cores unparked while plugged in by setting the active power plan to 100%. Turning this off restores the value saved by this app. This may increase power use and heat."));
        stack.Children.Add(parkingStatus);
        parkingRefresh.Margin = new Thickness(0,8,0,0); stack.Children.Add(parkingRefresh);
        parkingRefresh.Click += (_, _) => RefreshParking();
        CoreParkingToggle.Checked += (_, _) => ChangeParking(true);
        CoreParkingToggle.Unchecked += (_, _) => ChangeParking(false);
        RefreshParking();
        Activated += (_, _) => { if (!parkingBusy) RefreshParking(); };
        stack.Children.Add(Heading("OVERLAY READINGS"));
        var toggles = Panel();
        foreach (var metric in MetricInfo.All)
        {
            var toggle = new CheckBox { Content = MetricInfo.Label(metric), IsChecked = overlay.Settings.Visible[metric] };
            toggle.Checked += (_, _) => { overlay.Settings.Visible[metric] = true; overlay.Changed(); };
            toggle.Unchecked += (_, _) => { overlay.Settings.Visible[metric] = false; overlay.Changed(); };
            MetricToggles.Add(metric, toggle); ((StackPanel)toggles.Child).Children.Add(toggle);
        }
        stack.Children.Add(toggles);
        stack.Children.Add(Note("Foreground FPS follows the process owning the active window. Shows presented frames per second on its busiest swap chain over the last 2 seconds; displayed and generated frames can differ. Hiding this reading stops capture."));
        stack.Children.Add(Note("FPS uses bundled PresentMon for DirectX, Vulkan and OpenGL. Restart as administrator if frame tracing is unavailable. Static windows and unsupported or protected applications may have no recent frames."));
        stack.Children.Add(Heading("HARD DRIVE USAGE"));
        stack.Children.Add(Note("Show activity (%) for each physical drive, including SSDs. Choices save automatically."));
        var drivesPanel = Panel(); drivesPanel.Child = driveToggles; stack.Children.Add(drivesPanel);
        stack.Children.Add(driveStatus);
        stack.Children.Add(Heading("TOP APPLICATIONS"));
        stack.Children.Add(Note("Each usage reading shows the application with the highest average over the selected window. Processes with the same executable name are combined."));
        var averageCaption = Label($"Average over · {overlay.Settings.ApplicationAverageSeconds} seconds");
        stack.Children.Add(averageCaption);
        ApplicationAverageSlider = new Slider { Minimum = 5, Maximum = 600, Value = overlay.Settings.ApplicationAverageSeconds,
            SmallChange = 5, LargeChange = 30, TickFrequency = 5, IsSnapToTickEnabled = true };
        System.Windows.Automation.AutomationProperties.SetName(ApplicationAverageSlider, "Application averaging window in seconds");
        ApplicationAverageSlider.ValueChanged += (_, _) =>
        {
            overlay.Settings.ApplicationAverageSeconds = (int)Math.Round(ApplicationAverageSlider.Value);
            averageCaption.Text = $"Average over · {overlay.Settings.ApplicationAverageSeconds} seconds";
            overlay.Changed();
        };
        stack.Children.Add(ApplicationAverageSlider);
        stack.Children.Add(Note("Default: 60 seconds. Range: 5 seconds to 10 minutes. While starting, averages use the samples collected so far. CPU and GPU show average usage; RAM shows average physical working set; drives show average read/write throughput."));
        stack.Children.Add(Note("Per-drive application tracking needs administrator access. Use Restart as administrator below if it is unavailable. GPU attribution follows your graphics card choice and requires supported Windows counters."));
        stack.Children.Add(Heading("WINDOW"));
        stack.Children.Add(Label("Overlay monitor"));
        System.Windows.Automation.AutomationProperties.SetName(OverlayMonitorSelector, "Overlay monitor");
        stack.Children.Add(OverlayMonitorSelector);
        stack.Children.Add(Note("Choose the display for the overlay, Reset position and Auto-avoid. Current monitor lets you drag between displays. If a chosen display is disconnected, the primary display is used until it returns."));
        OverlayMonitorSelector.SelectionChanged += (_, _) =>
        {
            if (!updatingMonitors && OverlayMonitorSelector.SelectedItem is OverlayMonitorChoice choice)
                overlay.SetOverlayMonitor(choice.Id);
        };
        RefreshMonitors();
        Activated += (_, _) => RefreshMonitors();
        overlay.DisplaysChanged += RefreshMonitors;
        Closed += (_, _) => overlay.DisplaysChanged -= RefreshMonitors;
        AutoAvoidToggle = new CheckBox { Content = "Auto-avoid", IsChecked = overlay.Settings.AutoAvoid };
        AutoAvoidToggle.Checked += (_, _) => overlay.SetAutoAvoid(true);
        AutoAvoidToggle.Unchecked += (_, _) => overlay.SetAutoAvoid(false);
        stack.Children.Add(AutoAvoidToggle);
        stack.Children.Add(Note("Lock the overlay to a screen corner and move it to another corner when hovered. The header is hidden in this mode. Use the tray menu to open Settings or turn Auto-avoid off; dragging is available while it is off."));
        stack.Children.Add(Label("Available corners for Auto-avoid"));
        var cornerGrid = new Grid();
        cornerGrid.ColumnDefinitions.Add(new()); cornerGrid.ColumnDefinitions.Add(new());
        cornerGrid.RowDefinitions.Add(new()); cornerGrid.RowDefinitions.Add(new());
        foreach (var corner in Enum.GetValues<ScreenCorner>())
        {
            var name = corner switch
            {
                ScreenCorner.TopLeft => "Top left", ScreenCorner.TopRight => "Top right",
                ScreenCorner.BottomLeft => "Bottom left", _ => "Bottom right"
            };
            var toggle = new CheckBox { Content = name, IsChecked = overlay.Settings.AutoAvoidAvailableCorners.Contains(corner) };
            toggle.Checked += (_, _) => { if (!updatingAutoAvoidCorners) overlay.SetAutoAvoidCornerAvailable(corner, true); };
            toggle.Unchecked += (_, _) => { if (!updatingAutoAvoidCorners) overlay.SetAutoAvoidCornerAvailable(corner, false); };
            Grid.SetColumn(toggle, corner is ScreenCorner.TopRight or ScreenCorner.BottomRight ? 1 : 0);
            Grid.SetRow(toggle, corner is ScreenCorner.BottomLeft or ScreenCorner.BottomRight ? 1 : 0);
            AutoAvoidCornerToggles.Add(corner, toggle); cornerGrid.Children.Add(toggle);
        }
        var cornersPanel = Panel(); cornersPanel.Child = cornerGrid; stack.Children.Add(cornersPanel);
        stack.Children.Add(Note("Choose the corners Auto-avoid can use. Keep at least one selected. With one selected, the overlay stays in that corner. Choices save automatically and can be set before enabling Auto-avoid."));
        UpdateAutoAvoid(overlay.Settings.AutoAvoid);
        MinimiseToTrayToggle = new CheckBox { Content = "Minimise to tray", IsChecked = overlay.Settings.MinimiseToTray };
        MinimiseToTrayToggle.Checked += (_, _) => { overlay.Settings.MinimiseToTray = true; overlay.Changed(); };
        MinimiseToTrayToggle.Unchecked += (_, _) => { overlay.Settings.MinimiseToTray = false; overlay.Changed(); };
        stack.Children.Add(MinimiseToTrayToggle);
        stack.Children.Add(Note("Hide the application in the notification area when minimised. Double-click its tray icon or choose Restore to reopen it. Turn this off to minimise to the taskbar."));
        TopmostToggle = new CheckBox { Content = "Always on top", IsChecked = overlay.Settings.AlwaysOnTop };
        TopmostToggle.Checked += (_, _) => { overlay.Settings.AlwaysOnTop = true; overlay.Changed(); };
        TopmostToggle.Unchecked += (_, _) => { overlay.Settings.AlwaysOnTop = false; overlay.Changed(); };
        stack.Children.Add(TopmostToggle);
        stack.Children.Add(Note("Keep the overlay above other windows. For games, use windowed or borderless mode."));
        AddSlider(stack, "Opacity", .35, 1, overlay.Settings.Opacity, value => { overlay.Settings.Opacity = value; overlay.Changed(); }, value => $"{value:P0}");
        AddSlider(stack, "Size", .8, 1.6, overlay.Settings.Scale, value => { overlay.Settings.Scale = value; overlay.Changed(); }, value => $"{value:P0}");
        stack.Children.Add(Label("Update every"));
        var refresh = new ComboBox { ItemsSource = new[] { new RefreshChoice(500, "Half a second"), new RefreshChoice(1000, "1 second"), new RefreshChoice(2000, "2 seconds"), new RefreshChoice(5000, "5 seconds") }, DisplayMemberPath = "Name", SelectedValuePath = "Milliseconds", SelectedValue = overlay.Settings.RefreshMilliseconds };
        refresh.SelectionChanged += (_, _) => { if (refresh.SelectedItem is RefreshChoice choice) { overlay.Settings.RefreshMilliseconds = choice.Milliseconds; overlay.Changed(); } };
        stack.Children.Add(refresh);
        var reset = new Button { Content = "Reset overlay position", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0,12,0,0) };
        reset.Click += (_, _) => overlay.ResetPosition(); stack.Children.Add(reset);
        stack.Children.Add(Note("Drag the application header to move the overlay. Right-click the overlay or use the tray icon for Settings and Exit."));
        stack.Children.Add(Heading("SENSOR SOURCES"));
        stack.Children.Add(Label("Graphics card"));
        graphics.DisplayMemberPath = "Name"; graphics.SelectedValuePath = "Id";
        graphics.SelectionChanged += (_, _) => { if (!updatingChoices && graphics.SelectedItem is DeviceChoice c) { overlay.Settings.GraphicsId = c.Id; overlay.Changed(); } };
        stack.Children.Add(graphics);
        stack.Children.Add(Label("Motherboard temperature sensor"));
        board.DisplayMemberPath = "Name"; board.SelectedValuePath = "Id";
        board.SelectionChanged += (_, _) => { if (!updatingChoices && board.SelectedItem is DeviceChoice c) { overlay.Settings.MotherboardSensorId = c.Id; overlay.Changed(); } };
        stack.Children.Add(board);
        stack.Children.Add(Note("Automatic prefers Motherboard or System, then a single ACPI thermal zone. If several zones exist, select one here. ACPI reports a firmware/system temperature; its physical sensor location is not identified."));
        sensorStatus = Note("Detecting hardware…"); sensorStatus.Margin = new Thickness(0,12,0,12); stack.Children.Add(sensorStatus);
        var admin = new Button { Content = HardwareSampler.IsAdministrator ? "Running as administrator" : "Restart as administrator", IsEnabled = !HardwareSampler.IsAdministrator, HorizontalAlignment = HorizontalAlignment.Left };
        admin.Click += (_, _) => overlay.RestartElevated(); stack.Children.Add(admin);
        stack.Children.Add(Note("CPU and motherboard temperatures can require administrator access and the PawnIO driver. Unsupported sensors remain unavailable."));
        var driver = new Button { Content = "Open official PawnIO download", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0,8,0,0) };
        driver.Click += (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo("https://pawnio.eu/") { UseShellExecute = true }); }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Could not open browser"); }
        };
        stack.Children.Add(driver);
        stack.Children.Add(Note("PawnIO is installed on first launch when missing. Windows asks for administrator approval. If you decline, the app tries again next launch. Reopen the overlay as administrator after installation."));
        if (overlay.Store.LastError != null) stack.Children.Add(Note(overlay.Store.LastError));
        overlay.SnapshotReceived += UpdateSensors;
        overlay.AutoAvoidChanged += UpdateAutoAvoid;
        if (overlay.Latest != null) UpdateSensors(overlay.Latest);
        Closed += (_, _) => { overlay.SnapshotReceived -= UpdateSensors; overlay.AutoAvoidChanged -= UpdateAutoAvoid; overlay.SaveSettings(); };
        StateChanged += (_, _) => { if (WindowState == WindowState.Minimized) overlay.Minimise(); };
    }
    void UpdateDownloads()
    {
        DownloadsStatus.Text = overlay.Downloads.Status;
        WipeDownloadsButton.IsEnabled = !overlay.Downloads.IsBusy;
        try { DownloadsPath.Text = "Folder: " + overlay.Downloads.GetPath(); }
        catch (Exception ex) { DownloadsPath.Text = ex.Message; WipeDownloadsButton.IsEnabled = false; }
    }
    void ChangeAutoWipeDownloads(bool enabled)
    {
        if (updatingDownloads) return;
        var previous = overlay.Settings.AutoWipeDownloadsOnStartup;
        try
        {
            if (enabled && !confirmDownloads("Enable automatic cleanup of:\n\n" + overlay.Downloads.GetPath() +
                "\n\nEvery app startup will permanently delete its files and subfolders without another prompt, including after a restart or update. Items do not go to the Recycle Bin. Cleanup starts on the next launch.")) return;
            overlay.Settings.AutoWipeDownloadsOnStartup = enabled;
            overlay.SaveSettings();
            if (overlay.Store.LastError != null)
            {
                overlay.Settings.AutoWipeDownloadsOnStartup = previous;
                DownloadsStatus.Text = overlay.Store.LastError;
            }
        }
        catch (Exception ex) { DownloadsStatus.Text = ex.Message; }
        finally
        {
            updatingDownloads = true;
            AutoWipeDownloadsToggle.IsChecked = overlay.Settings.AutoWipeDownloadsOnStartup;
            updatingDownloads = false;
        }
    }
    async void WipeDownloads()
    {
        if (overlay.Downloads.IsBusy) return;
        try
        {
            var path = overlay.Downloads.GetPath();
            if (!confirmDownloads("Permanently delete all files and subfolders from:\n\n" + path +
                "\n\nItems do not go to the Recycle Bin. The Downloads folder itself is kept.")) return;
            await overlay.Downloads.RunAsync(path);
        }
        catch (Exception ex) { DownloadsStatus.Text = "Could not wipe Downloads: " + ex.Message; }
    }
    void UpdateAutoAvoid(bool enabled)
    {
        if (AutoAvoidToggle.IsChecked != enabled) AutoAvoidToggle.IsChecked = enabled;
        updatingAutoAvoidCorners = true;
        try
        {
            foreach (var pair in AutoAvoidCornerToggles)
            {
                var selected = overlay.Settings.AutoAvoidAvailableCorners.Contains(pair.Key);
                pair.Value.IsChecked = selected;
                pair.Value.IsEnabled = !selected || overlay.Settings.AutoAvoidAvailableCorners.Count > 1;
                pair.Value.ToolTip = pair.Value.IsEnabled ? null : "Keep at least one corner selected.";
            }
        }
        finally { updatingAutoAvoidCorners = false; }
    }
    void RefreshMonitors()
    {
        updatingMonitors = true;
        try
        {
            var choices = overlay.AvailableDisplays.Select(display => new OverlayMonitorChoice(display.Id, display.Label)).ToList();
            choices.Insert(0, new("", "Current monitor (drag to move)"));
            var id = overlay.Settings.OverlayMonitorId;
            if (!choices.Any(choice => choice.Id == id)) choices.Add(new(id, id + " · Disconnected (using primary)"));
            OverlayMonitorSelector.ItemsSource = choices;
            OverlayMonitorSelector.SelectedItem = choices.First(choice => choice.Id == id);
        }
        finally { updatingMonitors = false; }
    }
    void RefreshStartup(string? message = null)
    {
        updatingStartup = true;
        try
        {
            var command = startup.Read();
            StartWithWindowsToggle.IsChecked = !string.IsNullOrWhiteSpace(command);
            StartWithWindowsToggle.IsEnabled = true;
            StartupStatus.Text = string.IsNullOrWhiteSpace(command) ? "Automatic startup is off." :
                string.Equals(command, startup.Command, StringComparison.OrdinalIgnoreCase) ?
                "Registered to start when you sign in. Windows startup controls must also allow it." :
                "Startup is registered for another application location. Turn this off and on to use this copy.";
        }
        catch (Exception ex)
        {
            StartWithWindowsToggle.IsChecked = null;
            StartWithWindowsToggle.IsEnabled = false;
            StartupStatus.Text = "Could not read the Windows startup setting: " + ex.Message;
        }
        finally { updatingStartup = false; }
        if (message != null) StartupStatus.Text += "\n" + message;
    }
    void ChangeStartup(bool enabled)
    {
        if (updatingStartup) return;
        string? message = null;
        try { startup.SetEnabled(enabled); }
        catch (Exception ex) { message = "Could not change automatic startup: " + ex.Message; }
        RefreshStartup(message);
    }
    async void CheckUpdates()
    {
        CheckUpdatesButton.IsEnabled = false; InstallUpdateButton.Visibility = Visibility.Collapsed;
        availableRelease = null; UpdateStatus.Text = "Checking GitHub releases…";
        try
        {
            var release = await AppUpdates.CheckAsync(updateStop.Token);
            if (release == null) UpdateStatus.Text = "No published Windows release is available yet.";
            else if (release.Version <= AppUpdates.ParseVersion(AppUpdates.CurrentVersion)) UpdateStatus.Text = "You are up to date (" + AppUpdates.CurrentVersion + ").";
            else
            {
                availableRelease = release; InstallUpdateButton.Visibility = Visibility.Visible;
                UpdateStatus.Text = $"Version {release.Version} is available ({release.Size / 1048576d:0.0} MB).";
            }
        }
        catch (OperationCanceledException) { if (!updateStop.IsCancellationRequested) UpdateStatus.Text = "The update check timed out. Try again when connected."; }
        catch (Exception ex) { UpdateStatus.Text = "Could not check for updates. You can keep using this version. " + ex.Message; }
        finally { CheckUpdatesButton.IsEnabled = true; }
    }
    async void InstallUpdate()
    {
        if (availableRelease == null) return;
        if (MessageBox.Show(this, $"Download and install version {availableRelease.Version}? The application will save your preferences, close and reopen. Windows may request administrator approval if this folder is protected.",
            "Geurts Performance Prefect · Update", MessageBoxButton.OKCancel, MessageBoxImage.Information) != MessageBoxResult.OK) return;
        CheckUpdatesButton.IsEnabled = false; InstallUpdateButton.IsEnabled = false;
        try
        {
            var plan = await AppUpdates.PrepareAsync(availableRelease, new Progress<string>(message => UpdateStatus.Text = message), updateStop.Token);
            overlay.SaveSettings();
            if (overlay.Store.LastError != null) throw new InvalidOperationException("Save your preferences before installing. " + overlay.Store.LastError);
            UpdateStatus.Text = "Starting the installer…";
            await AppUpdates.LaunchInstallerAsync(plan, updateStop.Token);
            overlay.Close();
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) { UpdateStatus.Text = "Administrator approval cancelled. Your application was not changed."; }
        catch (OperationCanceledException) { }
        catch (Exception ex) { UpdateStatus.Text = "Could not install the update. " + ex.Message; }
        finally { CheckUpdatesButton.IsEnabled = true; InstallUpdateButton.IsEnabled = true; }
    }
    void RefreshParking(string? message = null)
    {
        updatingParking = true;
        try
        {
            var state = parking.Read();
            displayedParking = state;
            CoreParkingToggle.IsChecked = state.Minimum == 100;
            var canRestore = overlay.Settings.CoreParkingPreviousValues.TryGetValue(state.Scheme, out var previous);
            CoreParkingToggle.IsEnabled = !parkingBusy && (state.Minimum != 100 || canRestore);
            parkingStatus.Text = $"Plugged-in minimum: {state.Minimum}%. " +
                (state.Minimum == 100 ? "Core parking is removed for this setting." : "Core parking is permitted.") +
                (state.Minimum == 100 && !canRestore ? " Already enabled outside this app; no previous value is available to restore." : canRestore ? $" Saved previous value: {previous}%." : "");
            if (message != null) parkingStatus.Text += "\n" + message;
        }
        catch (Exception ex) { displayedParking = null; CoreParkingToggle.IsChecked = false; CoreParkingToggle.IsEnabled = false; parkingStatus.Text = "Core parking setting unavailable: " + ex.Message; }
        finally { updatingParking = false; }
    }
    async void ChangeParking(bool enabled)
    {
        if (updatingParking || parkingBusy) return;
        parkingBusy = true; CoreParkingToggle.IsEnabled = false; parkingRefresh.IsEnabled = false;
        string? message = null;
        try
        {
            var state = parking.Read();
            if (state != displayedParking) throw new InvalidOperationException("The power plan or core parking value changed. The setting has been refreshed; try again.");
            uint desired;
            if (enabled)
            {
                if (state.Minimum == 100) return;
                // Save before changing Windows, so restore survives a crash or restart.
                overlay.Settings.CoreParkingPreviousValues[state.Scheme] = state.Minimum;
                if (!overlay.Store.Save(overlay.Settings)) throw new InvalidOperationException(overlay.Store.LastError);
                desired = 100;
            }
            else
            {
                if (!overlay.Settings.CoreParkingPreviousValues.TryGetValue(state.Scheme, out desired))
                    throw new InvalidOperationException("No previous value is available for this power plan.");
            }
            parkingStatus.Text = "Applying core parking setting… Approve the Windows prompt if shown.";
            await CoreParking.ChangeWithElevation(state, desired);
            var after = parking.Read();
            if (after.Scheme != state.Scheme || after.Minimum != desired)
                throw new InvalidOperationException("The active plan changed or Windows did not confirm the requested value. Refresh and try again.");
            if (!enabled) { overlay.Settings.CoreParkingPreviousValues.Remove(state.Scheme); overlay.SaveSettings(); }
            message = enabled ? "Applied successfully." : "Previous value restored.";
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) { message = "Administrator approval cancelled. No change was applied."; }
        catch (Exception ex) { message = ex.Message; }
        finally { parkingBusy = false; parkingRefresh.IsEnabled = true; RefreshParking(message); }
    }
    void UpdateSensors(Snapshot snapshot)
    {
        UpdateDrives(snapshot);
        var gpuChoices = SensorSelection.Graphics(snapshot);
        var boardChoices = SensorSelection.BoardSensors(snapshot);
        var signature = string.Join("|", gpuChoices.Concat(boardChoices).Select(c => c.Id + c.Name));
        if (signature != choiceSignature || graphics.ItemsSource == null)
        {
            choiceSignature = signature; updatingChoices = true;
            var gpuItems = new List<DeviceChoice> { new("", "Automatic (prefer a dedicated graphics card)") }; gpuItems.AddRange(gpuChoices);
            var boardItems = new List<DeviceChoice> { new("", "Automatic (Motherboard / System / ACPI)") }; boardItems.AddRange(boardChoices);
            if (overlay.Settings.GraphicsId != "" && !gpuItems.Any(c => c.Id == overlay.Settings.GraphicsId)) gpuItems.Add(new(overlay.Settings.GraphicsId, "Saved graphics card (unavailable)"));
            if (overlay.Settings.MotherboardSensorId != "" && !boardItems.Any(c => c.Id == overlay.Settings.MotherboardSensorId)) boardItems.Add(new(overlay.Settings.MotherboardSensorId, "Saved sensor (unavailable)"));
            graphics.ItemsSource = gpuItems; graphics.SelectedValue = overlay.Settings.GraphicsId;
            board.ItemsSource = boardItems; board.SelectedValue = overlay.Settings.MotherboardSensorId;
            updatingChoices = false;
        }
        var readings = SensorSelection.Select(snapshot, overlay.Settings);
        var missing = readings.Where(p => !p.Value.Value.HasValue).Select(p => MetricInfo.Label(p.Key)).ToArray();
        sensorStatus.Text = missing.Length == 0 ? "All readings are available." : "Unavailable: " + string.Join(", ", missing) + ".";
        if (!readings[Metric.ForegroundFps].Value.HasValue) sensorStatus.Text += "\n" + readings[Metric.ForegroundFps].Detail;
        if (snapshot.Error != null) sensorStatus.Text += "\n" + snapshot.Error;
        if (!readings[Metric.MotherboardTemperature].Value.HasValue && snapshot.BoardTemperatureError != null)
            sensorStatus.Text += "\n" + snapshot.BoardTemperatureError;
        sensorStatus.ToolTip = string.Join("\n", readings.Select(p => MetricInfo.Label(p.Key) + ": " + p.Value.Source));
    }
    void UpdateDrives(Snapshot snapshot)
    {
        var signature = string.Join("|", snapshot.Drives.Select(d => d.Id + d.Label + d.Model));
        if (signature != driveSignature)
        {
            driveSignature = signature; driveToggles.Children.Clear(); DriveToggles.Clear();
            foreach (var drive in snapshot.Drives.OrderBy(d => d.Index))
            {
                var toggle = new CheckBox { Content = new TextBlock { Text = drive.Label + " · " + drive.Model, TextWrapping = TextWrapping.Wrap }, IsChecked = overlay.Settings.IsDriveVisible(drive.Id) };
                toggle.Checked += (_, _) => { overlay.Settings.DriveVisible[drive.Id] = true; overlay.Changed(); };
                toggle.Unchecked += (_, _) => { overlay.Settings.DriveVisible[drive.Id] = false; overlay.Changed(); };
                DriveToggles.Add(drive.Id, toggle); driveToggles.Children.Add(toggle);
            }
        }
        driveStatus.Text = snapshot.DriveError ?? (snapshot.Drives.Count == 0 ? "No physical drives detected." :
            snapshot.Drives.Any(d => !d.Activity.HasValue) ? "Waiting for drive activity readings. Disconnected or unsupported drives show unavailable." : "Drive activity readings are available.");
    }
    static TextBlock Heading(string text) => new() { Text = text, FontSize = 11, FontWeight = FontWeights.Bold, Foreground = Palette.Accent, Margin = new Thickness(0,24,0,9) };
    static TextBlock Label(string text) => new() { Text = text, Margin = new Thickness(0,12,0,7) };
    static TextBlock Note(string text) => new() { Text = text, Foreground = Palette.Muted, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,7,0,0), LineHeight = 18 };
    static Border Panel() => new() { Background = Palette.Panel, CornerRadius = new CornerRadius(10), Padding = new Thickness(14,8,10,8), Child = new StackPanel() };
    static void AddSlider(StackPanel parent, string label, double min, double max, double current, Action<double> changed, Func<double,string> format)
    {
        var caption = Label(label + " · " + format(current)); parent.Children.Add(caption);
        var slider = new Slider { Minimum = min, Maximum = max, Value = current, SmallChange = .05, LargeChange = .1, TickFrequency = .05, IsSnapToTickEnabled = true };
        slider.ValueChanged += (_, _) => { caption.Text = label + " · " + format(slider.Value); changed(slider.Value); }; parent.Children.Add(slider);
    }
    sealed record RefreshChoice(int Milliseconds, string Name);
}
