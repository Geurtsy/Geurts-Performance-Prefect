using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace GeurtsPerformancePrefect;

internal sealed class AutomaticCompressionPanel : StackPanel, IDisposable
{
    internal CheckBox Enabled { get; } = new() { Content = "Enable automatic compression / decompression" };
    internal ComboBox Mode { get; } = new();
    internal Slider High { get; } = Slider(6, 99);
    internal Slider Low { get; } = Slider(1, 94);
    internal Button ApplyButton { get; } = new() { Content = "Apply automatic settings…", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0,8,0,0) };
    internal Button DisableButton { get; } = new() { Content = "Stop and disable automatic compression", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0,8,0,0) };
    internal TextBlock Status { get; } = Note("");
    readonly TextBlock highLabel = Note(""), lowLabel = Note(""), driveLabel = Note("");
    readonly AutomaticCompressionController controller;
    readonly Func<string?> selectedDrive;
    readonly Func<string, bool> confirm;
    bool loading, saving, adjusting;
    internal AutomaticCompressionPanel(AutomaticCompressionController controller, Func<string?> selectedDrive, Func<string, bool>? confirm = null)
    {
        this.controller = controller; this.selectedDrive = selectedDrive;
        this.confirm = confirm ?? (message => MessageBox.Show(Window.GetWindow(this), message,
            "Geurts Performance Prefect · Automatic compression", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes);
        Children.Add(new TextBlock { Text = "Automatic compression", Foreground = Palette.Accent, FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0,20,0,4) });
        Children.Add(Note("Off by default. Choose entire drive above, select the thresholds and new-file mode, then enable or Apply. One drive policy is saved. It runs while Prefect is open, including in the tray; it starts checking one minute after enabling or launch."));
        Children.Add(driveLabel); Children.Add(Enabled);
        Children.Add(Note("New-file mode")); Mode.ItemsSource = new[] { "Compress new files only above the high threshold", "Always compress new files; thresholds manage older files" };
        Children.Add(Mode); Children.Add(highLabel); Children.Add(High); Children.Add(lowLabel); Children.Add(Low);
        Children.Add(Note("The sliders measure drive space used, not space saved. Defaults: compress at 85% full, decompress at 70% full. Keep at least five percentage points between them. Between thresholds, existing file states are kept. New means a file created after this policy was enabled; moving an older file keeps it in the older-file policy."));
        Children.Add(Note("Always mode keeps those new files compressed even below the low threshold; only older files are automatically decompressed. Decompression checks free space and skips a file that would bring the drive near the compression threshold. Windows, application/settings folders and protected files remain excluded."));
        Children.Add(Note("Each pass checks one file, compresses or decompresses it immediately when the policy allows, then moves to the next file. It starts changing files before the drive scan finishes. Live status shows the current file, completed changes and disk space saved."));
        Children.Add(ApplyButton); Children.Add(DisableButton); Children.Add(Status);
        Children.Add(Note("Changes to sliders or mode take effect after Apply. New files settle for at least ten seconds and are checked on the next one-minute pass. Locked files are retried. Missed Windows notifications can trigger a recovery scan, at most once per hour; crossing a threshold can also scan the drive. Folder defaults stay unchanged. Manual work waits while automatic work is active. Turning this off stops after the current file. Closing Settings keeps monitoring active; exit, restart and updates wait for active work to stop."));
        System.Windows.Automation.AutomationProperties.SetName(High, "Compress when drive is this percentage full");
        System.Windows.Automation.AutomationProperties.SetName(Low, "Decompress when drive is this percentage full");
        System.Windows.Automation.AutomationProperties.SetName(Mode, "Automatic new-file compression mode");
        LoadPolicy();
        High.ValueChanged += (_, _) => Adjust(true); Low.ValueChanged += (_, _) => Adjust(false);
        Mode.SelectionChanged += (_, _) => Labels();
        Enabled.Checked += async (_, _) => { if (!loading) await ApplyAsync(); };
        Enabled.Unchecked += (_, _) => { if (!loading) { controller.Disable(); LoadPolicy(); } };
        ApplyButton.Click += async (_, _) => await ApplyAsync();
        DisableButton.Click += (_, _) => { controller.Disable(); LoadPolicy(); };
        controller.Changed += Refresh; Refresh();
    }
    internal async Task ApplyAsync()
    {
        if (saving) return;
        if (Enabled.IsChecked != true)
        {
            controller.Configure(controller.Policy with { Enabled = false, CompressAt = (int)High.Value, DecompressAt = (int)Low.Value,
                NewFiles = Mode.SelectedIndex == 1 ? NewFileCompressionMode.Always : NewFileCompressionMode.AboveThreshold });
            LoadPolicy(); return;
        }
        var root = selectedDrive() ?? controller.Policy.Root;
        if (string.IsNullOrWhiteSpace(root)) { LoadPolicy(); Status.Text = "Choose an entire drive before enabling automatic compression."; return; }
        if (!confirm($"Enable automatic compression on {root}?\n\nCompress eligible files at {(int)High.Value}% full or higher; decompress at {(int)Low.Value}% full or lower. " +
            (Mode.SelectedIndex == 1 ? "Always compress files created after enabling this policy; these new files are excluded from automatic decompression. " : "New files are compressed only at the high threshold. ") +
            "Windows and protected files are skipped. Eligible files compressed by other tools may also be decompressed below the low threshold. " +
            "Each file is checked and changed immediately before checking the next file; there is no full-drive preview first. " +
            "Runs while Prefect is open, including in the tray, and resumes after launch. The first check is in one minute. Stop/disable keeps completed changes."))
        { LoadPolicy(); return; }
        saving = true; Refresh();
        try
        {
            var volume = await Task.Run(() => controller.ReadDrive(root));
            var old = controller.Policy;
            var since = old.Enabled && string.Equals(old.Root, root, StringComparison.OrdinalIgnoreCase) ? old.EnabledSinceUtc : DateTime.UtcNow;
            controller.Configure(new(true, root, volume.Volume, (int)High.Value, (int)Low.Value,
                Mode.SelectedIndex == 1 ? NewFileCompressionMode.Always : NewFileCompressionMode.AboveThreshold, since));
            LoadPolicy();
        }
        catch (Exception ex) { LoadPolicy(); Status.Text = "Could not enable automatic compression: " + ex.Message; }
        finally { saving = false; RefreshControls(); }
    }
    void Adjust(bool highChanged)
    {
        if (adjusting || loading) return;
        adjusting = true;
        if (High.Value < Low.Value + 5)
        {
            if (highChanged) Low.Value = High.Value - 5;
            else High.Value = Low.Value + 5;
        }
        Labels(); adjusting = false;
    }
    void Labels() { highLabel.Text = $"Compress eligible files at {(int)High.Value}% full or higher"; lowLabel.Text = $"Decompress eligible {(Mode.SelectedIndex == 1 ? "older " : "")}files at {(int)Low.Value}% full or lower"; }
    void LoadPolicy()
    {
        loading = true; var policy = controller.Policy;
        Enabled.IsChecked = policy.Enabled; High.Value = policy.CompressAt; Low.Value = policy.DecompressAt; Mode.SelectedIndex = (int)policy.NewFiles;
        loading = false; Labels(); Refresh();
    }
    internal void Refresh()
    {
        if (!saving && Enabled.IsChecked != controller.Policy.Enabled)
        {
            bool previousLoading = loading; loading = true; Enabled.IsChecked = controller.Policy.Enabled; loading = previousLoading;
        }
        driveLabel.Text = "Configured drive: " + (controller.Policy.Root.Length == 0 ? "none" : controller.Policy.Root) +
            " · Apply target: " + (selectedDrive() ?? controller.Policy.Root);
        Status.Text = controller.Status + (controller.LastResult.Length == 0 ? "" : "\nLast result: " + controller.LastResult);
        RefreshControls();
    }
    void RefreshControls()
    {
        Enabled.IsEnabled = ApplyButton.IsEnabled = Mode.IsEnabled = High.IsEnabled = Low.IsEnabled = !saving;
        DisableButton.IsEnabled = controller.Policy.Enabled || controller.IsBusy;
    }
    public void Dispose() => controller.Changed -= Refresh;
    static Slider Slider(double min, double max) => new() { Minimum = min, Maximum = max, SmallChange = 1, LargeChange = 5, TickFrequency = 1, IsSnapToTickEnabled = true };
    static TextBlock Note(string text) => new() { Text = text, Foreground = Palette.Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,6,0,8) };
}
