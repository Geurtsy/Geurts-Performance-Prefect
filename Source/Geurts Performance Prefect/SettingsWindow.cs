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
    internal void ShowInForeground()
    {
        WindowState = WindowState.Normal;
        Show();
        // Raise Settings above the overlay and other windows for this explicit open action.
        // Restore the saved preference immediately so Settings does not stay pinned.
        Topmost = true;
        Topmost = overlay.Settings.AlwaysOnTop;
        Activate();
        Focus();
        // Tray/context menus can restore their previous focus as they close.
        Dispatcher.InvokeAsync(() =>
        {
            if (IsVisible && WindowState == WindowState.Normal) { Activate(); Focus(); }
        }, System.Windows.Threading.DispatcherPriority.Input);
    }

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
    public TabControl Sections { get; } = new();
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
    public CheckBox StartAsAdministratorToggle { get; } = new() { Content = "Start as administrator" };
    public TextBlock AdministratorStartupStatus { get; } = Note("");
    bool updatingAdministratorStartup;
    public CheckBox ApplyPowerOptionsToggle { get; } = new() { Content = "Apply selected power preset on app startup" };
    public ComboBox PowerPresetSelector { get; } = new();
    public Slider BatteryCpuLimitSlider { get; } = new() { Minimum = 50, Maximum = 100, SmallChange = 1, LargeChange = 5, TickFrequency = 1, IsSnapToTickEnabled = true };
    public Button ApplyPowerOptionsButton { get; } = new() { Content = "Apply preset now", HorizontalAlignment = HorizontalAlignment.Left };
    public Button RestorePowerOptionsButton { get; } = new() { Content = "Restore previous power plan", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0,8,0,0) };
    public TextBlock PowerOptionsStatus { get; } = Note("");
    public Button CheckDiskButton { get; } = new() { Content = "Check disk for errors…", HorizontalAlignment = HorizontalAlignment.Left };
    public Button FixDiskButton { get; } = new() { Content = "Fix disk errors…", HorizontalAlignment = HorizontalAlignment.Left };
    public DiskCleanupPanel DiskCleanupPanel { get; } = new();
    public VisualEffectsPanel VisualEffectsPanel { get; } = new();
    readonly TextBlock batteryCpuCaption = Label("");
    bool updatingPowerOptions;
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
        System.Windows.Automation.AutomationProperties.SetName(Sections, "Settings sections");
        dock.Children.Add(Sections);
        var stack = AddTab("Startup");
        stack.Children.Add(StartWithWindowsToggle);
        stack.Children.Add(Note("Off by default. Open the overlay automatically when you sign in to Windows. Applies to your Windows account and saves immediately; administrator approval is not needed."));
        stack.Children.Add(StartupStatus);
        stack.Children.Add(Note("Keep the application in the same folder. If you move it, turn this off and on from the new location. Windows Settings → Apps → Startup can also disable it."));
        StartWithWindowsToggle.Checked += (_, _) => ChangeStartup(true);
        StartWithWindowsToggle.Unchecked += (_, _) => ChangeStartup(false);
        RefreshStartup();
        Activated += (_, _) => RefreshStartup();
        stack.Children.Add(StartAsAdministratorToggle);
        stack.Children.Add(Note("Off by default. Applies on the next launch, including Start with Windows. Windows may ask for administrator approval. Cancelling approval keeps the app running with normal permissions."));
        stack.Children.Add(AdministratorStartupStatus);
        StartAsAdministratorToggle.IsChecked = overlay.Settings.StartAsAdministrator;
        RefreshAdministratorStartup();
        StartAsAdministratorToggle.Checked += (_, _) => ChangeAdministratorStartup(true);
        StartAsAdministratorToggle.Unchecked += (_, _) => ChangeAdministratorStartup(false);
        stack = AddTab("Application updates");
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
        stack = AddTab("Downloads cleanup");
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
        stack = AddTab("Performance");
        stack.Children.Add(VisualEffectsPanel);
        stack.Children.Add(DiskCleanupPanel);
        stack.Children.Add(new BoringApplicationsPanel(overlay.Settings, () => overlay.Store.Save(overlay.Settings)));
        AddPowerOptions(stack);
        stack.Children.Add(CoreParkingToggle);
        stack.Children.Add(Note("Keep cores unparked while plugged in by setting the active power plan to 100%. Turning this off restores the value saved by this app. This may increase power use and heat."));
        stack.Children.Add(parkingStatus);
        parkingRefresh.Margin = new Thickness(0,8,0,0); stack.Children.Add(parkingRefresh);
        parkingRefresh.Click += (_, _) => RefreshParking();
        CoreParkingToggle.Checked += (_, _) => ChangeParking(true);
        CoreParkingToggle.Unchecked += (_, _) => ChangeParking(false);
        RefreshParking();
        Activated += (_, _) => { if (!parkingBusy) RefreshParking(); };
        stack = AddTab("Repair tools");
        stack.Children.Add(Label("Disk check"));
        stack.Children.Add(CheckDiskButton);
        stack.Children.Add(Note("Open a console inside a Prefect window to check a local drive for file-system errors. Windows may ask for administrator approval. Choose a drive and start a read-only check; repairs are not applied."));
        var diskCheckStatus = Note(""); stack.Children.Add(diskCheckStatus);
        CheckDiskButton.Click += (_, _) => diskCheckStatus.Text = DiskCheck.OpenWindow();
        stack.Children.Add(FixDiskButton);
        stack.Children.Add(Note("Open the console in repair mode. Select a drive and confirm before CHKDSK repairs its file system. Windows may ask to dismount the drive or schedule repair at restart; you answer those prompts. Back up important files first."));
        FixDiskButton.Click += (_, _) => diskCheckStatus.Text = DiskCheck.OpenWindow(repair: true);
        stack.Children.Add(Label("System integrity"));
        var integrityButton = new Button { Content = "Scan Windows system integrity", HorizontalAlignment = HorizontalAlignment.Left };
        stack.Children.Add(integrityButton);
        stack.Children.Add(Note("Open the diagnostic console to scan protected Windows system files. Requires administrator approval. The scan reports issues without applying repairs."));
        var integrityStatus = Note(""); stack.Children.Add(integrityStatus);
        integrityButton.Click += (_, _) => integrityStatus.Text = DiskCheck.OpenWindow(integrity: true);
        var imageButton = new Button { Content = "Fix Windows image (DISM)…", HorizontalAlignment = HorizontalAlignment.Left };
        stack.Children.Add(imageButton);
        stack.Children.Add(Note("Check, scan or repair the Windows component store in the built-in diagnostic console. Repair requires confirmation and may use Windows Update."));
        var imageStatus = Note(""); stack.Children.Add(imageStatus);
        imageButton.Click += (_, _) => imageStatus.Text = DiskCheck.OpenWindow(windowsImage: true);
        stack = AddTab("Overlay");
        var toggles = Panel();
        foreach (var metric in MetricInfo.All)
        {
            var toggle = new CheckBox { Content = MetricInfo.Label(metric), IsChecked = overlay.Settings.Visible[metric] };
            toggle.Checked += (_, _) => { overlay.Settings.Visible[metric] = true; overlay.Changed(); };
            toggle.Unchecked += (_, _) => { overlay.Settings.Visible[metric] = false; overlay.Changed(); };
            MetricToggles.Add(metric, toggle); ((StackPanel)toggles.Child).Children.Add(toggle);
        }
        stack.Children.Add(toggles);
        stack.Children.Add(Note("FPS works with desktop apps as well as games. Foreground FPS shows the active process's presented frames when available. Otherwise the row says Desktop FPS and shows Windows' desktop composition cadence, often the display refresh rate. This fallback is desktop-wide, not that app's own render rate or its monitor's measured FPS."));
        stack.Children.Add(Note("App frames use PresentMon for DirectX, Vulkan and OpenGL and may need administrator access. Desktop FPS needs no administrator access and covers static windows, traditional desktop apps and apps that render in separate worker processes. Hiding the FPS reading stops capture."));
        stack.Children.Add(Label("Hard drive usage"));
        stack.Children.Add(Note("Show activity (%) for each physical drive, including SSDs. Choices save automatically."));
        var drivesPanel = Panel(); drivesPanel.Child = driveToggles; stack.Children.Add(drivesPanel);
        stack.Children.Add(driveStatus);
        stack.Children.Add(Label("Top applications"));
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
        stack.Children.Add(Note("Per-drive application tracking needs administrator access. Use Restart as administrator in Sensor sources if it is unavailable. GPU attribution follows your graphics card choice and requires supported Windows counters."));
        stack.Children.Add(Label("Window"));
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
        stack = AddTab("Sensor sources");
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
    void RefreshAdministratorStartup(string? message = null)
    {
        AdministratorStartupStatus.Text = "This session: " + (HardwareSampler.IsAdministrator ? "administrator" : "normal permissions") + ". " +
            (overlay.Settings.StartAsAdministrator ? "Administrator startup is on for the next launch." : "Administrator startup is off.");
        if (message != null) AdministratorStartupStatus.Text += "\n" + message;
    }
    void ChangeAdministratorStartup(bool enabled)
    {
        if (updatingAdministratorStartup) return;
        var previous = overlay.Settings.StartAsAdministrator;
        overlay.Settings.StartAsAdministrator = enabled;
        string? message = null;
        if (!overlay.Store.Save(overlay.Settings))
        {
            overlay.Settings.StartAsAdministrator = previous;
            updatingAdministratorStartup = true;
            try { StartAsAdministratorToggle.IsChecked = previous; }
            finally { updatingAdministratorStartup = false; }
            message = overlay.Store.LastError;
        }
        RefreshAdministratorStartup(message);
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
    void AddPowerOptions(StackPanel stack)
    {
        stack.Children.Add(Label("Power preset"));
        PowerPresetSelector.ItemsSource = Enum.GetValues<PowerPreset>().Select(preset => new PowerPresetChoice(preset, PowerPolicy.Name(preset))).ToArray();
        PowerPresetSelector.SelectedValuePath = "Preset";
        var template = new DataTemplate();
        var text = new FrameworkElementFactory(typeof(TextBlock));
        text.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("Name"));
        text.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
        text.SetValue(FrameworkElement.MaxWidthProperty, 345d);
        template.VisualTree = text; PowerPresetSelector.ItemTemplate = template;
        System.Windows.Automation.AutomationProperties.SetName(PowerPresetSelector, "Power preset");
        stack.Children.Add(PowerPresetSelector);
        stack.Children.Add(Note("Performance Mode Only: maximum CPU performance, boost, all cores unparked, CPU idle saving disabled, full brightness, and no automatic sleep, hibernate, dimming, screen-off or disk-off on battery or plugged in. Lid close and the sleep button do nothing."));
        stack.Children.Add(Note("Plugged performance / unplugged saving: the same performance settings while plugged in. On battery: CPU performance limit, core parking, no boost, strongest supported device power saving, 10% brightness, dim after 30 seconds, screen off after 1 minute, sleep after 2 minutes, hibernate after 5 minutes. Lid close and the sleep button sleep."));
        stack.Children.Add(batteryCpuCaption); stack.Children.Add(BatteryCpuLimitSlider);
        System.Windows.Automation.AutomationProperties.SetName(BatteryCpuLimitSlider, "Battery CPU performance limit in percent");
        stack.Children.Add(Note("Default: 50%. Increase to 100%. Limits maximum processor performance and the percentage of unparked cores where supported; Task Manager CPU usage can still reach 100%. Applies only to the battery-saving preset."));
        stack.Children.Add(ApplyPowerOptionsToggle);
        stack.Children.Add(Note("Off by default. Saves for the next app launch, including Start with Windows. Changing the preset or slider saves for the next application; use Apply preset now to apply it now. Turning startup off leaves the current Windows plan active."));
        stack.Children.Add(ApplyPowerOptionsButton); stack.Children.Add(RestorePowerOptionsButton);
        stack.Children.Add(PowerOptionsStatus);
        stack.Children.Add(Note("Uses a separate Windows power plan and leaves your previous plan available to restore. Windows switches between its AC and battery values automatically, even when Prefect is closed. Restore also turns startup application off. Administrator approval may be required. Unsupported options are listed after application; hardware, firmware, vendor controls and Windows policies can limit the effect. Critical battery protection is retained."));
        UpdatePowerOptions();
        ApplyPowerOptionsToggle.Checked += (_, _) => SavePowerOptions();
        ApplyPowerOptionsToggle.Unchecked += (_, _) => SavePowerOptions();
        PowerPresetSelector.SelectionChanged += (_, _) => SavePowerOptions();
        BatteryCpuLimitSlider.ValueChanged += (_, _) => SavePowerOptions();
        ApplyPowerOptionsButton.Click += async (_, _) => { if (!parkingBusy) await overlay.PowerOptions.ApplyAsync(); };
        RestorePowerOptionsButton.Click += async (_, _) => { if (!parkingBusy) await overlay.PowerOptions.RestoreAsync(); };
        overlay.PowerOptions.Changed += PowerOptionsChanged;
        Closed += (_, _) => overlay.PowerOptions.Changed -= PowerOptionsChanged;
    }
    void PowerOptionsChanged() { UpdatePowerOptions(); RefreshParking(); }
    void UpdatePowerOptions()
    {
        updatingPowerOptions = true;
        ApplyPowerOptionsToggle.IsChecked = overlay.Settings.ApplyPowerOptionsOnStartup;
        PowerPresetSelector.SelectedValue = overlay.Settings.PowerPreset;
        BatteryCpuLimitSlider.Value = overlay.Settings.BatteryCpuLimit;
        batteryCpuCaption.Text = $"Battery CPU performance limit · {overlay.Settings.BatteryCpuLimit}%";
        var busy = overlay.PowerOptions.Busy || parkingBusy;
        ApplyPowerOptionsToggle.IsEnabled = !busy; PowerPresetSelector.IsEnabled = !busy;
        BatteryCpuLimitSlider.IsEnabled = !busy && overlay.Settings.PowerPreset == PowerPreset.PerformancePluggedBatterySaving;
        ApplyPowerOptionsButton.IsEnabled = !busy;
        RestorePowerOptionsButton.IsEnabled = !busy && overlay.Settings.ManagedPowerScheme != Guid.Empty && overlay.Settings.PreviousPowerScheme != Guid.Empty;
        PowerOptionsStatus.Text = overlay.PowerOptions.Status;
        updatingPowerOptions = false;
    }
    void SavePowerOptions()
    {
        if (updatingPowerOptions || overlay.PowerOptions.Busy) return;
        var previousToggle = overlay.Settings.ApplyPowerOptionsOnStartup;
        var previousPreset = overlay.Settings.PowerPreset; var previousLimit = overlay.Settings.BatteryCpuLimit;
        overlay.Settings.ApplyPowerOptionsOnStartup = ApplyPowerOptionsToggle.IsChecked == true;
        if (PowerPresetSelector.SelectedValue is PowerPreset preset) overlay.Settings.PowerPreset = preset;
        overlay.Settings.BatteryCpuLimit = (int)Math.Round(BatteryCpuLimitSlider.Value);
        var saved = overlay.Store.Save(overlay.Settings);
        if (!saved)
        {
            overlay.Settings.ApplyPowerOptionsOnStartup = previousToggle;
            overlay.Settings.PowerPreset = previousPreset; overlay.Settings.BatteryCpuLimit = previousLimit;
        }
        UpdatePowerOptions();
        PowerOptionsStatus.Text = saved ? "Saved. " + (overlay.Settings.ApplyPowerOptionsOnStartup ? "Selected preset will apply on the next app launch." : "Startup application is off.") + " Use Apply preset now for an immediate change." : overlay.Store.LastError;
    }
    sealed record PowerPresetChoice(PowerPreset Preset, string Name);
    void RefreshParking(string? message = null)
    {
        updatingParking = true;
        try
        {
            var state = parking.Read();
            displayedParking = state;
            CoreParkingToggle.IsChecked = state.Minimum == 100;
            var canRestore = overlay.Settings.CoreParkingPreviousValues.TryGetValue(state.Scheme, out var previous);
            var managed = state.Scheme == overlay.Settings.ManagedPowerScheme;
            CoreParkingToggle.IsEnabled = !parkingBusy && !overlay.PowerOptions.Busy && !managed && (state.Minimum != 100 || canRestore);
            parkingStatus.Text = $"Plugged-in minimum: {state.Minimum}%. " +
                (state.Minimum == 100 ? "Core parking is removed for this setting." : "Core parking is permitted.") +
                (state.Minimum == 100 && !canRestore ? " Already enabled outside this app; no previous value is available to restore." : canRestore ? $" Saved previous value: {previous}%." : "");
            if (message != null) parkingStatus.Text += "\n" + message;
            if (managed) parkingStatus.Text += "\nCore parking is controlled by the active Prefect preset. Restore the previous power plan to use this separate control.";
        }
        catch (Exception ex) { displayedParking = null; CoreParkingToggle.IsChecked = false; CoreParkingToggle.IsEnabled = false; parkingStatus.Text = "Core parking setting unavailable: " + ex.Message; }
        finally { updatingParking = false; }
    }
    async void ChangeParking(bool enabled)
    {
        if (updatingParking || parkingBusy || overlay.PowerOptions.Busy) return;
        parkingBusy = true; UpdatePowerOptions(); CoreParkingToggle.IsEnabled = false; parkingRefresh.IsEnabled = false;
        string? message = null;
        try
        {
            var state = parking.Read();
            if (state.Scheme == overlay.Settings.ManagedPowerScheme) throw new InvalidOperationException("Core parking is controlled by the Prefect power preset.");
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
        finally { parkingBusy = false; UpdatePowerOptions(); parkingRefresh.IsEnabled = true; RefreshParking(message); }
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
    StackPanel AddTab(string title)
    {
        var stack = new StackPanel { Margin = new Thickness(0,0,12,0) };
        var scroll = new ScrollViewer { Content = stack, VerticalScrollBarVisibility = ScrollBarVisibility.Visible,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        scroll.Style = (Style)FindResource("SettingsScrollViewer");
        var tab = new TabItem { Header = title, Content = scroll };
        System.Windows.Automation.AutomationProperties.SetName(tab, title);
        Sections.Items.Add(tab);
        if (Sections.Items.Count == 1) Sections.SelectedIndex = 0;
        return stack;
    }
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
