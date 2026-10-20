using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace GeurtsPerformancePrefect;

public sealed class FileCompressionPanel : StackPanel, IDisposable
{
    internal Button ChooseFiles { get; } = Button("Choose files…");
    internal Button ChooseFolder { get; } = Button("Choose folder…");
    internal Button ChooseDrive { get; } = Button("Choose entire drive…");
    internal Button PreviewButton { get; } = Button("Preview files");
    internal Button ScanCompressButton { get; } = Button("Scan and compress drive…");
    internal Button CompressButton { get; } = Button("Compress previewed files…");
    internal Button DecompressButton { get; } = Button("Decompress previewed files…");
    internal Button StopButton { get; } = Button("Stop");
    internal TextBlock Selection { get; } = Note("Choose files or a data folder to begin.");
    internal TextBlock Status { get; } = Note("Nothing runs automatically.");
    internal ListBox Files { get; } = new() { Height = 240, Background = Palette.Background, Foreground = Palette.Text };
    readonly FileCompression engine;
    readonly Func<string, bool> confirm;
    string[] selections = Array.Empty<string>();
    string? selectedDrive;
    readonly Func<string, CancellationToken, Action<string>?, CompressionPreview> previewDrive;
    readonly Func<string, CancellationToken, Action<string>?, ScanCompressionResult> scanAndCompressDrive;
    CompressionPreview? preview;
    CancellationTokenSource? cancellation;
    internal bool IsBusy { get; private set; }
    internal CompressionPreview? Preview => preview;
    readonly AutomaticCompressionController? automatic;
    internal AutomaticCompressionPanel? AutomaticPanel { get; }

    public FileCompressionPanel() : this(new FileCompression(), null) { }
    internal FileCompressionPanel(FileCompression engine, Func<string, bool>? confirm,
        Func<string, CancellationToken, Action<string>?, CompressionPreview>? previewDrive = null,
        Func<string, CancellationToken, Action<string>?, ScanCompressionResult>? scanAndCompressDrive = null,
        AutomaticCompressionController? automatic = null)
    {
        this.engine = engine;
        this.automatic = automatic;
        this.previewDrive = previewDrive ?? engine.PreviewDrive;
        this.scanAndCompressDrive = scanAndCompressDrive ?? engine.ScanAndCompressDrive;
        this.confirm = confirm ?? (message => MessageBox.Show(Window.GetWindow(this), message,
            "Geurts Performance Prefect · File compression", MessageBoxButton.YesNo, MessageBoxImage.Question,
            MessageBoxResult.No) == MessageBoxResult.Yes);
        Children.Add(new TextBlock { Text = "File compression", Foreground = Palette.Accent, FontSize = 18, FontWeight = FontWeights.SemiBold });
        Children.Add(Note("Save disk space with Windows' lossless NTFS compression. Files keep their names and open, read and edit normally in their usual apps. No extraction or running Prefect is needed to read them."));
        Children.Add(Note("Choose files, a data folder or an entire local NTFS drive. Windows, standard application folders, boot/recovery files, AppData and other protected paths are skipped. Preview actions change only their previewed files. Folder and drive compression defaults stay unchanged."));
        var pick = new WrapPanel(); pick.Children.Add(ChooseFiles); pick.Children.Add(ChooseFolder); pick.Children.Add(ChooseDrive); Children.Add(pick);
        Children.Add(Selection);
        var scanActions = new WrapPanel(); scanActions.Children.Add(PreviewButton); scanActions.Children.Add(ScanCompressButton); Children.Add(scanActions);
        Children.Add(Note("For a selected drive, Scan and compress drive checks each file, compresses it immediately if eligible, then moves on. Confirm once before starting; no full preview is required. Stop finishes the current file and keeps completed changes. Preview entire drive remains available for reviewing files before any changes."));
        Children.Add(Note("Review original size, size on disk and compression state below. Savings depend on the data; videos, photos and archives often save little. Reading and editing compressed files uses extra CPU and performance can vary."));
        var template = new DataTemplate(); var text = new FrameworkElementFactory(typeof(TextBlock));
        text.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding());
        text.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap); text.SetValue(MarginProperty, new Thickness(3,5,3,5));
        template.VisualTree = text; Files.ItemTemplate = template;
        Files.ItemContainerStyle = new Style(typeof(ListBoxItem));
        Files.ItemContainerStyle.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        Files.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
        Files.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Visible);
        Files.Resources.Add(typeof(ScrollViewer), Application.Current.FindResource("SettingsScrollViewer"));
        System.Windows.Automation.AutomationProperties.SetName(Files, "Files in compression preview and operation results");
        Children.Add(Files);
        var actions = new WrapPanel(); actions.Children.Add(CompressButton); actions.Children.Add(DecompressButton); actions.Children.Add(StopButton); Children.Add(actions);
        Children.Add(Status);
        Children.Add(Note("For the preview actions, preview first and then confirm Compress or Decompress. All actions skip locked, read-only, encrypted, sparse, system, cloud placeholder and linked files, as well as files over 30 GB. Files changed since their eligibility check are skipped. Decompression needs free disk space. Stop waits for the current file; completed changes remain. Preview again to reverse them."));
        Children.Add(Note("Drive previews scan the complete accessible tree and show the first 500 eligible files here; totals and confirmation include every eligible file in the completed preview. A temporary snapshot is removed when replaced, used or closed. File/folder previews contain up to 10,000 eligible files and visit up to 100,000 entries. Skipped counts include excluded folders without scanning their contents; details show the first 100 issues. Windows permissions are respected."));
        ChooseFiles.Click += (_, _) =>
        {
            var dialog = new OpenFileDialog { Multiselect = true, Title = "Choose files to preview for compression" };
            if (dialog.ShowDialog(Window.GetWindow(this)) == true) SelectPaths(dialog.FileNames);
        };
        ChooseFolder.Click += (_, _) =>
        {
            var dialog = new OpenFolderDialog { Title = "Choose a data folder to preview for compression" };
            if (dialog.ShowDialog(Window.GetWindow(this)) == true) SelectPaths(new[] { dialog.FolderName });
        };
        ChooseDrive.Click += (_, _) =>
        {
            var dialog = new OpenFolderDialog { Title = "Choose a drive root, such as C:\\", InitialDirectory = System.IO.Path.GetPathRoot(Environment.SystemDirectory) };
            if (dialog.ShowDialog(Window.GetWindow(this)) == true) SelectDrive(dialog.FolderName);
        };
        PreviewButton.Click += async (_, _) => await PreviewAsync();
        ScanCompressButton.Click += async (_, _) => await ScanAndCompressAsync();
        CompressButton.Click += async (_, _) => await ApplyAsync(true);
        DecompressButton.Click += async (_, _) => await ApplyAsync(false);
        StopButton.Click += (_, _) => Stop();
        if (automatic != null)
        {
            AutomaticPanel = new AutomaticCompressionPanel(automatic, () => selectedDrive);
            Children.Add(AutomaticPanel); automatic.Changed += Sync;
        }
        Sync();
    }

    internal void SelectPaths(IEnumerable<string> paths)
    {
        if (IsBusy || automatic?.IsBusy == true) return;
        selectedDrive = null; preview?.Dispose();
        selections = paths.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        preview = null; Files.ItemsSource = null; Selection.Text = string.Join("\n", selections);
        Status.Text = "Ready to preview. No files have changed."; Sync();
    }

    internal bool SelectDrive(string root)
    {
        if (IsBusy || automatic?.IsBusy == true) return false;
        try
        {
            var normalized = System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(root));
            if (!System.IO.Path.IsPathFullyQualified(root) || !string.Equals(normalized,
                System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetPathRoot(normalized)!), StringComparison.OrdinalIgnoreCase) || normalized.StartsWith(@"\\", StringComparison.Ordinal))
                throw new ArgumentException("Select the root of a local drive, such as C:\\.");
            SelectPaths(new[] { normalized }); selectedDrive = normalized;
            Selection.Text = "Entire drive: " + normalized;
            Status.Text = "Ready to preview the entire drive. Windows and protected paths will be skipped. No files have changed.";
            Sync(); return true;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or System.IO.IOException)
        { Status.Text = ex.Message; return false; }
    }

    internal async Task PreviewAsync()
    {
        if (IsBusy || automatic?.IsBusy == true || selections.Length == 0) return;
        Begin(); preview?.Dispose(); preview = null; Files.ItemsSource = null; Status.Text = "Previewing files…";
        try
        {
            var result = await Task.Run(() => selectedDrive == null ? engine.Preview(selections, cancellation!.Token, Report) : previewDrive(selectedDrive, cancellation!.Token, Report));
            preview = result;
            cancellation!.Token.ThrowIfCancellationRequested();
            Files.ItemsSource = result.Files.Select(f => f.ToString()).Concat(result.Issues.Select(i => "Skipped: " + i)).ToArray();
            Status.Text = $"{(selectedDrive == null ? "Preview" : "Completed drive preview")}: {result.Count} eligible file(s), {result.Skipped} skipped file(s)/folder(s). " +
                $"Original size {FileCompression.Size(result.Length)}; on disk {FileCompression.Size(result.DiskBytes)}." +
                (result.Snapshot != null ? $" Showing the first {result.Files.Length} eligible file(s); actions include all {result.Count}." : "") +
                (result.Limited ? " Preview limit reached; only listed eligible files can change." : "");
        }
        catch (OperationCanceledException) { preview?.Dispose(); preview = null; Status.Text = "Preview stopped. No files changed."; }
        catch (Exception ex) { preview?.Dispose(); preview = null; Status.Text = "Preview failed: " + ex.Message; }
        finally { End(); }
    }

    internal async Task ApplyAsync(bool compress)
    {
        if (IsBusy || automatic?.IsBusy == true || preview == null) return;
        var approved = preview;
        long count = compress ? approved.Count - approved.CompressedCount : approved.CompressedCount;
        if (count == 0) return;
        if (!confirm($"{(compress ? "Compress" : "Decompress")} {count} previewed file(s){(selectedDrive == null ? "" : " on entire drive " + selectedDrive)}?\n\n" +
            (selectedDrive == null ? "Only the files in this preview are eligible. " : $"All {count} eligible files in the completed drive preview are included, including files beyond the first {CompressionSnapshot.DisplayLimit} displayed. Windows and protected files/folders are skipped. ") +
            "Files remain readable in their usual apps. " +
            (compress ? "Savings vary and compressed access uses extra CPU." : "Decompression uses more disk space.") +
            "\n\nStop waits for the current file and keeps completed changes. You can preview again to reverse them.")) return;
        Begin(); preview = null;
        try
        {
            var result = await Task.Run(() => engine.Apply(approved, compress, cancellation!.Token, Report));
            Status.Text = result.Message;
            // Remove stale pre-operation sizes and states; a fresh preview is required for either next action.
            Files.ItemsSource = new[] { result.Message }.Concat(result.Issues.Select(i => "Skipped: " + i)).ToArray();
        }
        catch (Exception ex) { Status.Text = "Operation failed: " + ex.Message + " Preview again to check file states."; }
        finally { approved.Dispose(); End(); }
    }

    internal async Task ScanAndCompressAsync()
    {
        if (IsBusy || automatic?.IsBusy == true || selectedDrive == null) return;
        var root = selectedDrive;
        if (!confirm("Scan and compress eligible files across entire drive " + root + "?\n\n" +
            "Each file is checked, compressed immediately if eligible, then scanning moves to the next file. " +
            "There is no full preview or final file count before compression starts. Windows and protected files/folders are skipped. " +
            "Files found while scanning, including newly added files, may be compressed. Files remain normally readable and editable. " +
            "Savings vary and compressed access uses extra CPU.\n\nStop waits for the current file and keeps completed changes. " +
            "Use Preview entire drive first if you want to review the files before changing them.")) return;
        Begin(); preview?.Dispose(); preview = null; Files.ItemsSource = null;
        Status.Text = "Scanning and compressing eligible drive files…";
        try
        {
            var result = await Task.Run(() => scanAndCompressDrive(root, cancellation!.Token, Report));
            Status.Text = result.Message;
            Files.ItemsSource = new[] { result.Message }.Concat(result.Result.Issues.Select(i => "Skipped: " + i)).ToArray();
        }
        catch (Exception ex) { Status.Text = "Scan and compress failed: " + ex.Message + " Completed changes remain. Preview again to review file states."; }
        finally { End(); }
    }

    internal void Stop() { cancellation?.Cancel(); Status.Text = "Stopping after the current file…"; }
    internal bool CanLeave()
    {
        if (!IsBusy) return true;
        Status.Text = "Let file compression finish, or click Stop and wait for the current file before closing, restarting or updating.";
        return false;
    }
    void Begin() { IsBusy = true; if (automatic != null) automatic.ManualBusy = true; cancellation = new(); Sync(); }
    void End() { IsBusy = false; if (automatic != null) automatic.ManualBusy = false; cancellation?.Dispose(); cancellation = null; Sync(); }
    void Report(string message) => Dispatcher.Invoke(() => { if (cancellation?.IsCancellationRequested != true) Status.Text = message; });
    void Sync()
    {
        if (automatic?.IsBusy == true && preview != null)
        {
            preview.Dispose(); preview = null; Files.ItemsSource = null;
            Status.Text = "Automatic work is active. Preview again after it finishes to review current file states.";
        }
        bool idle = !IsBusy && automatic?.IsBusy != true;
        ChooseFiles.IsEnabled = ChooseFolder.IsEnabled = ChooseDrive.IsEnabled = idle;
        PreviewButton.IsEnabled = idle && selections.Length != 0;
        ScanCompressButton.IsEnabled = idle && selectedDrive != null;
        PreviewButton.Content = selectedDrive == null ? "Preview files" : "Preview entire drive";
        CompressButton.Content = selectedDrive == null ? "Compress previewed files…" : "Compress eligible drive files…";
        DecompressButton.Content = selectedDrive == null ? "Decompress previewed files…" : "Decompress eligible drive files…";
        CompressButton.IsEnabled = idle && preview != null && preview.Count > preview.CompressedCount;
        DecompressButton.IsEnabled = idle && preview?.CompressedCount > 0;
        StopButton.IsEnabled = IsBusy;
        AutomaticPanel?.Refresh();
    }
    public void Dispose() { if (automatic != null) automatic.Changed -= Sync; AutomaticPanel?.Dispose(); preview?.Dispose(); preview = null; }
    static Button Button(string caption) => new() { Content = caption, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0,8,8,0) };
    static TextBlock Note(string text) => new() { Text = text, Foreground = Palette.Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,6,0,8) };
}
