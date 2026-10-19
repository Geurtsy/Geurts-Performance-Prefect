using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace GeurtsPerformancePrefect;

public sealed class StorageSensePanel : StackPanel, IDisposable
{
    internal CheckBox Enabled { get; } = new() { Content = "Enable automatic cleanup" };
    internal CheckBox IncludeRecycleBin { get; } = new() { Content = "Include Recycle Bin in automatic cleanup" };
    internal ComboBox Schedule { get; } = new();
    internal ComboBox Age { get; } = new();
    internal Button PreviewButton { get; } = new() { Content = "Preview files", HorizontalAlignment = HorizontalAlignment.Left };
    internal Button CleanButton { get; } = new() { Content = "Clean previewed files…", HorizontalAlignment = HorizontalAlignment.Left };
    internal Button StopButton { get; } = new() { Content = "Stop", HorizontalAlignment = HorizontalAlignment.Left };
    internal TextBlock Status { get; } = Note("");
    internal TextBlock Timing { get; } = Note("");
    internal ListBox Files { get; } = new() { Height = 200, Background = Palette.Background, Foreground = Palette.Text };
    internal Button PreviewRecycleButton { get; } = new() { Content = "Preview Recycle Bin", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0,8,0,0) };
    internal Button CleanRecycleButton { get; } = new() { Content = "Clean previewed Recycle Bin items…", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0,8,0,0) };
    internal Button StopRecycleButton { get; } = new() { Content = "Stop", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0,8,0,0) };
    internal ListBox RecycleFiles { get; } = new() { Height = 240, Background = Palette.Background, Foreground = Palette.Text };
    internal TextBlock RecycleStatus { get; } = Note("");
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
        Children.Add(Note("Manage temporary files, Recycle Bin items and Downloads cleanup here. Temporary files and Recycle Bin items have their own age-based previews and permanent cleanup confirmations. Downloads has separate full-folder wipe controls at the end of this page."));
        Children.Add(Note("Temporary folder: " + controller.Root));
        Children.Add(Note("Temporary files must have both creation and modification dates older than the age limit. Recycle Bin items must have been in the bin longer than that limit. These age and schedule controls do not apply to Downloads. Personal folders, cloud storage and Windows system temporary files are outside this cleanup."));
        Children.Add(Label("Keep items for at least")); Children.Add(Age);
        Age.ItemsSource = new[] { 7, 14, 30, 60, 90, 180, 365, settings.StorageSenseAgeDays }.Distinct().Order().ToArray();
        var ageTemplate = new DataTemplate(); var ageText = new FrameworkElementFactory(typeof(TextBlock));
        ageText.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding { StringFormat = "{0} days" });
        ageTemplate.VisualTree = ageText; Age.ItemTemplate = ageTemplate;
        Children.Add(Label("Automatic cleanup")); Children.Add(Enabled);
        Children.Add(IncludeRecycleBin);
        Children.Add(Note("Recycle Bin inclusion is off by default and takes effect only when automatic cleanup is enabled. It permanently deletes eligible files and folders, including folder contents, from your current account's bin on available drives."));
        Children.Add(Label("Run every")); Children.Add(Schedule);
        Schedule.ItemsSource = new[] { "Day", "Week", "30 days" };
        Children.Add(Note("Off by default. Scheduling works while Prefect is running, including in the tray. The first run is one interval after enabling; an overdue run starts when Prefect next opens. Turning this off stops an active run after the current item."));
        Children.Add(Timing);
        Children.Add(Label("Temporary files"));
        PreviewButton.Margin = new Thickness(0,8,0,0); CleanButton.Margin = new Thickness(0,8,0,0); StopButton.Margin = new Thickness(0,8,0,0);
        Children.Add(PreviewButton); Children.Add(CleanButton); Children.Add(StopButton); Children.Add(Status);
        Children.Add(Label("Previewed files")); Children.Add(Files);
        void StyleList(ListBox list)
        {
            list.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
            list.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Visible);
            list.Resources.Add(typeof(ScrollViewer), Application.Current.FindResource("SettingsScrollViewer"));
            var fileTemplate = new DataTemplate(); var fileText = new FrameworkElementFactory(typeof(TextBlock));
            fileText.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding());
            fileText.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
            fileText.SetValue(FrameworkElement.MarginProperty, new Thickness(3,5,3,5)); fileTemplate.VisualTree = fileText;
            list.ItemTemplate = fileTemplate;
            list.ItemContainerStyle = new Style(typeof(ListBoxItem));
            list.ItemContainerStyle.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        }
        StyleList(Files); StyleList(RecycleFiles);
        Children.Add(Note("The list includes up to 10,000 eligible files per preview. A scan visits at most 100,000 entries. File sizes are estimates of file data; recovered free space can differ. Files added or changed after the preview are kept."));
        Children.Add(Label("Recycle Bin"));
        Children.Add(Note("Review each item's original location, recycling date and known size. Folders include their contents; folder sizes may be unavailable. Cleanup cannot be undone. Items restored or removed since preview are kept or skipped. New items are never added to an approved preview. Stop can leave a folder partly cleaned."));
        Children.Add(PreviewRecycleButton); Children.Add(CleanRecycleButton); Children.Add(StopRecycleButton); Children.Add(RecycleStatus);
        Children.Add(RecycleFiles);
        Children.Add(Note("Recycle Bin previews include up to 10,000 eligible items and examine at most 100,000 entries. Only items with a reliable recycling date and original location are eligible. Unavailable items and Windows access errors are reported."));
        System.Windows.Automation.AutomationProperties.SetName(Age, "Storage Sense minimum age");
        System.Windows.Automation.AutomationProperties.SetName(Schedule, "Storage Sense schedule");
        System.Windows.Automation.AutomationProperties.SetName(Files, "Temporary files in cleanup preview");
        System.Windows.Automation.AutomationProperties.SetName(RecycleFiles, "Recycle Bin cleanup preview");
        Sync();
        Enabled.Checked += (_, _) => SavePolicy(); Enabled.Unchecked += (_, _) => SavePolicy();
        IncludeRecycleBin.Checked += (_, _) => SavePolicy(); IncludeRecycleBin.Unchecked += (_, _) => SavePolicy();
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
        StopRecycleButton.Click += (_, _) => controller.Cancel();
        PreviewRecycleButton.Click += async (_, _) => await controller.PreviewRecycleAsync();
        CleanRecycleButton.Click += async (_, _) =>
        {
            if (controller.IsBusy || controller.RecyclePreview is not { Items.Count: > 0 } preview) return;
            var folders = preview.Items.Count(i => i.IsFolder);
            if (this.confirm($"Permanently delete {preview.Items.Count} previewed Recycle Bin item(s), including {folders} folder(s) and their contents?\n\nKnown file data: {StorageSenseEngine.Size(preview.Bytes)}. Folder sizes may be unavailable.\n\nThese items cannot be restored afterward. Only the previewed items will be removed.") &&
                ReferenceEquals(preview, controller.RecyclePreview)) await controller.CleanRecycleAsync();
        };
        controller.Changed += Refresh; Refresh();
    }
    void Sync()
    {
        updating = true;
        Enabled.IsChecked = settings.StorageSenseEnabled; Age.SelectedItem = settings.StorageSenseAgeDays;
        IncludeRecycleBin.IsChecked = settings.StorageSenseIncludeRecycleBin;
        Schedule.SelectedIndex = (int)settings.StorageSenseSchedule;
        updating = false;
    }
    void SavePolicy()
    {
        if (updating || Age.SelectedItem is not int age || Schedule.SelectedIndex < 0) return;
        var enable = Enabled.IsChecked == true;
        var includeRecycle = IncludeRecycleBin.IsChecked == true;
        if (includeRecycle && !settings.StorageSenseIncludeRecycleBin && !confirm($"Include Recycle Bin items in automatic cleanup?\n\nItems kept in your current account's bin for more than {age} days will be permanently deleted, including folders and their contents. This cannot be undone.\n\nThis runs only when automatic cleanup is enabled. You can turn inclusion off at any time."))
        { Sync(); return; }
        if (enable && !settings.StorageSenseEnabled && !confirm($"Enable automatic permanent deletion of temporary files older than {age} days from:\n\n{controller.Root}\n\n" + (includeRecycle ? $"Recycle Bin items kept for more than {age} days will also be permanently deleted, including folders and contents.\n\n" : "") + $"Prefect will clean every {Schedule.SelectedItem.ToString()!.ToLowerInvariant()} while running. Deletion cannot be undone. The first run is after one interval. You can turn this off at any time."))
        { Sync(); return; }
        var previous = (settings.StorageSenseEnabled, settings.StorageSenseAgeDays, settings.StorageSenseSchedule, settings.StorageSenseLastAttemptUtc, settings.StorageSenseIncludeRecycleBin);
        settings.StorageSenseEnabled = enable; settings.StorageSenseAgeDays = age;
        settings.StorageSenseIncludeRecycleBin = includeRecycle;
        settings.StorageSenseSchedule = (StorageSenseSchedule)Schedule.SelectedIndex;
        if (enable && (!previous.Item1 || previous.Item3 != settings.StorageSenseSchedule || previous.Item2 != age || previous.Item5 != includeRecycle))
            settings.StorageSenseLastAttemptUtc = DateTime.UtcNow;
        if (!store.Save(settings))
        {
            (settings.StorageSenseEnabled, settings.StorageSenseAgeDays, settings.StorageSenseSchedule, settings.StorageSenseLastAttemptUtc, settings.StorageSenseIncludeRecycleBin) = previous;
            Sync(); Status.Text = "Storage Sense preferences were not changed. " + store.LastError; return;
        }
        if (previous.Item1 && !enable) controller.Cancel();
        else if (previous.Item5 && !includeRecycle) controller.CancelRecycle();
        controller.Invalidate(); Refresh();
    }
    void Refresh()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(Refresh); return; }
        Age.IsEnabled = Schedule.IsEnabled = !controller.IsBusy;
        PreviewButton.IsEnabled = !controller.IsBusy;
        PreviewRecycleButton.IsEnabled = !controller.IsBusy;
        CleanRecycleButton.IsEnabled = !controller.IsBusy && controller.RecyclePreview?.Items.Count > 0;
        IncludeRecycleBin.IsEnabled = !controller.IsBusy || IncludeRecycleBin.IsChecked == true;
        StopRecycleButton.IsEnabled = controller.IsBusy;
        CleanButton.IsEnabled = !controller.IsBusy && controller.Preview?.Files.Count > 0;
        StopButton.IsEnabled = controller.IsBusy;
        Status.Text = controller.IsBusy ? controller.Status : controller.TemporaryStatus;
        RecycleStatus.Text = controller.IsBusy ? controller.Status : controller.RecycleStatus;
        Timing.Text = (settings.StorageSenseEnabled
            ? "Next automatic cleanup: " + (controller.NextUtc?.ToLocalTime().ToString("g") ?? "when Prefect is ready") + "."
            : "Automatic cleanup is off.") +
            (settings.StorageSenseLastRunUtc is DateTime last ? "\nLast cleanup: " + last.ToLocalTime().ToString("g") + ". " + settings.StorageSenseLastResult : "\nNo completed cleanup recorded.");
        Files.ItemsSource = controller.Preview?.Files;
        RecycleFiles.ItemsSource = controller.RecyclePreview?.Items;
    }
    public void Dispose() => controller.Changed -= Refresh;
    static TextBlock Note(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, Foreground = Palette.Muted, Margin = new Thickness(0,7,0,7), FontSize = 12 };
    static TextBlock Label(string text) => new() { Text = text, Foreground = Palette.Text, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0,10,0,6) };
}
