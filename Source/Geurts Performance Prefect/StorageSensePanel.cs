using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace GeurtsPerformancePrefect;

public sealed class StorageSensePanel : StackPanel, IDisposable
{
    internal CheckBox Enabled { get; } = new() { Content = "Automatically clean old temporary files" };
    internal ComboBox Schedule { get; } = new();
    internal ComboBox Age { get; } = new();
    internal Button PreviewButton { get; } = new() { Content = "Preview files", HorizontalAlignment = HorizontalAlignment.Left };
    internal Button CleanButton { get; } = new() { Content = "Clean previewed files…", HorizontalAlignment = HorizontalAlignment.Left };
    internal Button StopButton { get; } = new() { Content = "Stop", HorizontalAlignment = HorizontalAlignment.Left };
    internal TextBlock Status { get; } = Note("");
    internal TextBlock Timing { get; } = Note("");
    internal ListBox Files { get; } = new() { Height = 200, Background = Palette.Background, Foreground = Palette.Text };
    readonly StorageSenseController controller;
    readonly OverlaySettings settings;
    readonly SettingsStore store;
    readonly Func<string, bool> confirm;
    bool updating;

    internal StorageSensePanel(StorageSenseController controller, OverlaySettings settings, SettingsStore store, Func<string, bool>? confirm = null)
    {
        this.controller = controller; this.settings = settings; this.store = store;
        this.confirm = confirm ?? (message => MessageBox.Show(Window.GetWindow(this), message,
            "Geurts Performance Prefect · Storage Sense", MessageBoxButton.YesNo, MessageBoxImage.Warning,
            MessageBoxResult.No) == MessageBoxResult.Yes);
        Children.Add(new TextBlock { Text = "Storage Sense", Foreground = Palette.Accent, FontSize = 18, FontWeight = FontWeights.SemiBold });
        Children.Add(Note("Keep space free by removing old files from your user temporary folder. Previewing never deletes anything. Cleanup permanently deletes eligible files and leaves folders in place."));
        Children.Add(Note("Temporary folder: " + controller.Root));
        Children.Add(Note("Only files whose creation and last modification are both older than the age limit qualify. Open, read-only, system and linked files are kept. Downloads, Recycle Bin, personal folders, cloud files and Windows system temporary files are outside this feature."));
        Children.Add(Label("Keep temporary files for at least")); Children.Add(Age);
        Age.ItemsSource = new[] { 7, 14, 30, 60, 90, 180, 365, settings.StorageSenseAgeDays }.Distinct().Order().ToArray();
        var ageTemplate = new DataTemplate(); var ageText = new FrameworkElementFactory(typeof(TextBlock));
        ageText.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding { StringFormat = "{0} days" });
        ageTemplate.VisualTree = ageText; Age.ItemTemplate = ageTemplate;
        Children.Add(Label("Automatic cleanup")); Children.Add(Enabled);
        Children.Add(Label("Run every")); Children.Add(Schedule);
        Schedule.ItemsSource = new[] { "Day", "Week", "30 days" };
        Children.Add(Note("Off by default. Scheduling works while Prefect is running, including in the tray. The first run is one interval after enabling; an overdue run starts when Prefect next opens. Turning this off stops an active run after the current file."));
        Children.Add(Timing);
        PreviewButton.Margin = new Thickness(0,8,0,0); CleanButton.Margin = new Thickness(0,8,0,0); StopButton.Margin = new Thickness(0,8,0,0);
        Children.Add(PreviewButton); Children.Add(CleanButton); Children.Add(StopButton); Children.Add(Status);
        Children.Add(Label("Previewed files")); Children.Add(Files);
        Files.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
        Files.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Visible);
        Files.Resources.Add(typeof(ScrollViewer), Application.Current.FindResource("SettingsScrollViewer"));
        var fileTemplate = new DataTemplate(); var fileText = new FrameworkElementFactory(typeof(TextBlock));
        fileText.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding());
        fileText.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
        fileText.SetValue(FrameworkElement.MarginProperty, new Thickness(3,5,3,5)); fileTemplate.VisualTree = fileText;
        Files.ItemTemplate = fileTemplate;
        Files.ItemContainerStyle = new Style(typeof(ListBoxItem));
        Files.ItemContainerStyle.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        Children.Add(Note("The list includes up to 10,000 eligible files per preview. A scan visits at most 100,000 entries. File sizes are estimates of file data; recovered free space can differ. Files added or changed after the preview are kept."));
        System.Windows.Automation.AutomationProperties.SetName(Age, "Temporary file minimum age");
        System.Windows.Automation.AutomationProperties.SetName(Schedule, "Storage Sense schedule");
        System.Windows.Automation.AutomationProperties.SetName(Files, "Temporary files in cleanup preview");
        Sync();
        Enabled.Checked += (_, _) => SavePolicy(); Enabled.Unchecked += (_, _) => SavePolicy();
        Age.SelectionChanged += (_, _) => SavePolicy(); Schedule.SelectionChanged += (_, _) => SavePolicy();
        PreviewButton.Click += async (_, _) => await controller.PreviewAsync();
        CleanButton.Click += async (_, _) =>
        {
            if (controller.IsBusy || controller.Preview is not { Files.Count: > 0 } preview) return;
            // Only this exact preview can be approved; a scheduled run cannot substitute its own list.
            if (this.confirm($"Permanently delete {preview.Files.Count} previewed temporary file(s), {StorageSenseEngine.Size(preview.Bytes)} of file data?\n\n{preview.Root}\n\nFiles go past the Recycle Bin. Changed, open and protected files will be kept.") &&
                ReferenceEquals(preview, controller.Preview)) await controller.CleanAsync();
        };
        StopButton.Click += (_, _) => controller.Cancel();
        controller.Changed += Refresh; Refresh();
    }
    void Sync()
    {
        updating = true;
        Enabled.IsChecked = settings.StorageSenseEnabled; Age.SelectedItem = settings.StorageSenseAgeDays;
        Schedule.SelectedIndex = (int)settings.StorageSenseSchedule;
        updating = false;
    }
    void SavePolicy()
    {
        if (updating || Age.SelectedItem is not int age || Schedule.SelectedIndex < 0) return;
        var enable = Enabled.IsChecked == true;
        if (enable && !settings.StorageSenseEnabled && !confirm($"Enable automatic permanent deletion of temporary files older than {age} days from:\n\n{controller.Root}\n\nPrefect will clean every {Schedule.SelectedItem.ToString()!.ToLowerInvariant()} while running. Files bypass the Recycle Bin. The first run is after one interval. You can turn this off at any time."))
        { Sync(); return; }
        var previous = (settings.StorageSenseEnabled, settings.StorageSenseAgeDays, settings.StorageSenseSchedule, settings.StorageSenseLastAttemptUtc);
        settings.StorageSenseEnabled = enable; settings.StorageSenseAgeDays = age;
        settings.StorageSenseSchedule = (StorageSenseSchedule)Schedule.SelectedIndex;
        if (enable && (!previous.Item1 || previous.Item3 != settings.StorageSenseSchedule || previous.Item2 != age))
            settings.StorageSenseLastAttemptUtc = DateTime.UtcNow;
        if (!store.Save(settings))
        {
            (settings.StorageSenseEnabled, settings.StorageSenseAgeDays, settings.StorageSenseSchedule, settings.StorageSenseLastAttemptUtc) = previous;
            Sync(); Status.Text = "Storage Sense preferences were not changed. " + store.LastError; return;
        }
        if (!enable) controller.Cancel();
        controller.Invalidate(); Refresh();
    }
    void Refresh()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(Refresh); return; }
        Age.IsEnabled = Schedule.IsEnabled = !controller.IsBusy;
        PreviewButton.IsEnabled = !controller.IsBusy;
        CleanButton.IsEnabled = !controller.IsBusy && controller.Preview?.Files.Count > 0;
        StopButton.IsEnabled = controller.IsBusy;
        Status.Text = controller.Status;
        Timing.Text = (settings.StorageSenseEnabled
            ? "Next automatic cleanup: " + (controller.NextUtc?.ToLocalTime().ToString("g") ?? "when Prefect is ready") + "."
            : "Automatic cleanup is off.") +
            (settings.StorageSenseLastRunUtc is DateTime last ? "\nLast cleanup: " + last.ToLocalTime().ToString("g") + ". " + settings.StorageSenseLastResult : "\nNo completed cleanup recorded.");
        Files.ItemsSource = controller.Preview?.Files;
    }
    public void Dispose() => controller.Changed -= Refresh;
    static TextBlock Note(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, Foreground = Palette.Muted, Margin = new Thickness(0,7,0,7), FontSize = 12 };
    static TextBlock Label(string text) => new() { Text = text, Foreground = Palette.Text, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0,10,0,6) };
}
