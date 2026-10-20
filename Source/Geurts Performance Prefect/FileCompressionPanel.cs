using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace GeurtsPerformancePrefect;

public sealed class FileCompressionPanel : StackPanel
{
    internal Button ChooseFiles { get; } = Button("Choose files…");
    internal Button ChooseFolder { get; } = Button("Choose folder…");
    internal Button PreviewButton { get; } = Button("Preview files");
    internal Button CompressButton { get; } = Button("Compress previewed files…");
    internal Button DecompressButton { get; } = Button("Decompress previewed files…");
    internal Button StopButton { get; } = Button("Stop");
    internal TextBlock Selection { get; } = Note("Choose files or a data folder to begin.");
    internal TextBlock Status { get; } = Note("Nothing runs automatically.");
    internal ListBox Files { get; } = new() { Height = 240, Background = Palette.Background, Foreground = Palette.Text };
    readonly FileCompression engine;
    readonly Func<string, bool> confirm;
    string[] selections = Array.Empty<string>();
    CompressionPreview? preview;
    CancellationTokenSource? cancellation;
    internal bool IsBusy { get; private set; }
    internal CompressionPreview? Preview => preview;

    public FileCompressionPanel() : this(new FileCompression(), null) { }
    internal FileCompressionPanel(FileCompression engine, Func<string, bool>? confirm)
    {
        this.engine = engine;
        this.confirm = confirm ?? (message => MessageBox.Show(Window.GetWindow(this), message,
            "Geurts Performance Prefect · File compression", MessageBoxButton.YesNo, MessageBoxImage.Question,
            MessageBoxResult.No) == MessageBoxResult.Yes);
        Children.Add(new TextBlock { Text = "File compression", Foreground = Palette.Accent, FontSize = 18, FontWeight = FontWeights.SemiBold });
        Children.Add(Note("Save disk space with Windows' lossless NTFS compression. Files keep their names and open, read and edit normally in their usual apps. No extraction or running Prefect is needed to read them."));
        Children.Add(Note("Choose files or a data folder. Folder previews include subfolders. Only existing previewed files change; new files do not inherit compression from this action. Whole drives and Windows, application and settings folders are protected."));
        var pick = new WrapPanel(); pick.Children.Add(ChooseFiles); pick.Children.Add(ChooseFolder); Children.Add(pick);
        Children.Add(Selection); Children.Add(PreviewButton);
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
        Children.Add(Note("Preview first, then confirm either action. Locked, read-only, encrypted, sparse, system, cloud placeholder and linked files are skipped, as are files over 30 GB. Files changed since preview are skipped. Decompression needs free disk space. Stop waits for the current file; completed changes remain. Preview again to reverse them."));
        Children.Add(Note("A preview contains up to 10,000 eligible files and visits up to 100,000 entries. Limits and skipped items are reported; details show the first 100 issues. Files and folder permissions stay under Windows' control."));
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
        PreviewButton.Click += async (_, _) => await PreviewAsync();
        CompressButton.Click += async (_, _) => await ApplyAsync(true);
        DecompressButton.Click += async (_, _) => await ApplyAsync(false);
        StopButton.Click += (_, _) => Stop();
        Sync();
    }

    internal void SelectPaths(IEnumerable<string> paths)
    {
        if (IsBusy) return;
        selections = paths.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        preview = null; Files.ItemsSource = null; Selection.Text = string.Join("\n", selections);
        Status.Text = "Ready to preview. No files have changed."; Sync();
    }

    internal async Task PreviewAsync()
    {
        if (IsBusy || selections.Length == 0) return;
        Begin(); preview = null; Files.ItemsSource = null; Status.Text = "Previewing files…";
        try
        {
            var result = await Task.Run(() => engine.Preview(selections, cancellation!.Token, Report));
            cancellation!.Token.ThrowIfCancellationRequested();
            preview = result; Files.ItemsSource = result.Files.Select(f => f.ToString()).Concat(result.Issues.Select(i => "Skipped: " + i)).ToArray();
            Status.Text = $"Preview: {result.Files.Length} eligible file(s), {result.Skipped} skipped. " +
                $"Original size {FileCompression.Size(result.Files.Sum(f => f.Length))}; on disk {FileCompression.Size(result.Files.Sum(f => f.DiskBytes))}." +
                (result.Limited ? " Preview limit reached; only listed eligible files can change." : "");
        }
        catch (OperationCanceledException) { Status.Text = "Preview stopped. No files changed."; }
        catch (Exception ex) { Status.Text = "Preview failed: " + ex.Message; }
        finally { End(); }
    }

    internal async Task ApplyAsync(bool compress)
    {
        if (IsBusy || preview == null) return;
        var approved = preview;
        int count = approved.Files.Count(f => f.Compressed != compress);
        if (count == 0) return;
        if (!confirm($"{(compress ? "Compress" : "Decompress")} {count} previewed file(s)?\n\n" +
            "Only the files shown in this preview are eligible. Files remain readable in their usual apps. " +
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
        finally { End(); }
    }

    internal void Stop() { cancellation?.Cancel(); Status.Text = "Stopping after the current file…"; }
    internal bool CanLeave()
    {
        if (!IsBusy) return true;
        Status.Text = "Let file compression finish, or click Stop and wait for the current file before closing, restarting or updating.";
        return false;
    }
    void Begin() { IsBusy = true; cancellation = new(); Sync(); }
    void End() { IsBusy = false; cancellation?.Dispose(); cancellation = null; Sync(); }
    void Report(string message) => Dispatcher.Invoke(() => { if (cancellation?.IsCancellationRequested != true) Status.Text = message; });
    void Sync()
    {
        ChooseFiles.IsEnabled = ChooseFolder.IsEnabled = !IsBusy;
        PreviewButton.IsEnabled = !IsBusy && selections.Length != 0;
        CompressButton.IsEnabled = !IsBusy && preview?.Files.Any(f => !f.Compressed) == true;
        DecompressButton.IsEnabled = !IsBusy && preview?.Files.Any(f => f.Compressed) == true;
        StopButton.IsEnabled = IsBusy;
    }
    static Button Button(string caption) => new() { Content = caption, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0,8,8,0) };
    static TextBlock Note(string text) => new() { Text = text, Foreground = Palette.Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,6,0,8) };
}
