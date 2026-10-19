using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace GeurtsPerformancePrefect;

internal static class RecycleBinPolicyVerification
{
    internal static void Run(string folder, Action<bool, string> check)
    {
        var fixture = Path.Combine(folder, "recycle-policy-" + Guid.NewGuid().ToString("N"));
        var temp = Path.Combine(fixture, "Temp"); Directory.CreateDirectory(temp);
        var store = new SettingsStore(Path.Combine(fixture, "settings.json")); File.WriteAllText(store.Path, "{}");
        var settings = store.Load(); var now = DateTime.UtcNow;
        var bin = new BinFixture();
        RecycleBinItem Add(string id, int age = 60, bool isFolder = false) => bin.Add(new(id, @"D:\Original files\" + id,
            now.AddDays(-age), isFolder ? null : 2048, isFolder));
        var old = Add("old 日本語.txt"); var directory = Add("old folder", isFolder: true); var recent = Add("recent.txt", 1);
        using var controller = new StorageSenseController(settings, store, new StorageSenseEngine(temp, Array.Empty<string>()), () => now, bin);
        check(!settings.StorageSenseIncludeRecycleBin && !new OverlaySettings().StorageSenseIncludeRecycleBin,
            "Recycle Bin automatic inclusion defaults off for new and existing settings");
        bool approve = false; var prompts = new List<string>();
        using var panel = new StorageSensePanel(controller, settings, store, message => { prompts.Add(message); return approve; });
        panel.IncludeRecycleBin.IsChecked = true;
        check(!settings.StorageSenseIncludeRecycleBin && panel.IncludeRecycleBin.IsChecked == false && prompts.Count == 1 && bin.CleanCalls == 0,
            "Cancelling Recycle Bin opt-in restores the toggle and does not clean anything");
        approve = true; panel.IncludeRecycleBin.IsChecked = true;
        check(store.Load().StorageSenseIncludeRecycleBin && !settings.StorageSenseEnabled && bin.ScanCalls == 0,
            "Recycle Bin inclusion persists independently without enabling or immediately running automatic cleanup");
        panel.Enabled.IsChecked = true;
        check(settings.StorageSenseEnabled && prompts.Last().Contains("Recycle Bin") && prompts.Last().Contains("folders and contents"),
            "Enabling automatic cleanup explicitly explains included Recycle Bin folder deletion");
        panel.IncludeRecycleBin.IsChecked = false;
        check(!store.Load().StorageSenseIncludeRecycleBin && settings.StorageSenseEnabled,
            "Turning Recycle Bin inclusion off preserves the master automatic cleanup preference");

        panel.PreviewRecycleButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); PumpUntil(() => !controller.IsBusy);
        check(controller.RecyclePreview?.Items.Count == 2 && panel.RecycleFiles.Items.Count == 2 && bin.CleanCalls == 0 && bin.Items.Count == 3,
            "Manual Recycle Bin preview is available with automatic inclusion off and never deletes items");
        check(controller.RecyclePreview!.Items.Any(i => i == directory) && !controller.RecyclePreview.Items.Contains(recent) &&
            panel.RecycleFiles.Items.Cast<RecycleBinItem>().Any(i => i.ToString().Contains("includes contents") && i.ToString().Contains("size unavailable")),
            "Recycle Bin preview filters by recycling age and labels folder contents and unknown sizes");
        approve = false; panel.CleanRecycleButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(bin.CleanCalls == 0 && prompts.Last().Contains("2 previewed Recycle Bin") && prompts.Last().Contains("1 folder(s)") && prompts.Last().Contains("cannot be restored"),
            "Manual Recycle Bin cancellation preserves the preview and explains permanent folder deletion");
        var later = Add("added after preview.txt"); bin.Items.Remove(old.Id);
        approve = true; panel.CleanRecycleButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); PumpUntil(() => !controller.IsBusy);
        check(bin.CleanCalls == 1 && bin.LastApproved!.Items.Count == 2 && bin.Items.ContainsKey(later.Id) && bin.Items.ContainsKey(recent.Id) && !bin.Items.ContainsKey(directory.Id),
            "Manual cleanup routes only the approved snapshot; restored and newly added items stay outside deletion");
        check(controller.RecyclePreview == null && !panel.CleanRecycleButton.IsEnabled && panel.RecycleStatus.Text.Contains("Removed 1") && store.Load().StorageSenseLastResult.Contains("Recycle Bin"),
            "Recycle Bin cleanup consumes its preview and records the actual partial result");

        Await(controller.PreviewRecycleAsync()); panel.Age.SelectedItem = 7;
        check(controller.RecyclePreview == null && !panel.CleanRecycleButton.IsEnabled,
            "Changing the retention age invalidates a Recycle Bin preview");
        settings.StorageSenseLastAttemptUtc = now.AddDays(-8); settings.StorageSenseIncludeRecycleBin = false;
        var beforeScans = bin.ScanCalls; Await(controller.RunScheduledAsync());
        check(bin.ScanCalls == beforeScans && bin.Items.ContainsKey(later.Id),
            "Automatic temporary-file cleanup never scans or cleans the bin while inclusion is off");
        settings.StorageSenseLastAttemptUtc = now.AddDays(-8); settings.StorageSenseIncludeRecycleBin = true;
        Await(controller.RunScheduledAsync());
        check(bin.ScanCalls == beforeScans + 1 && !bin.Items.ContainsKey(later.Id) && bin.Items.ContainsKey(recent.Id) &&
            store.Load().StorageSenseLastResult.Contains("Temporary files:") && store.Load().StorageSenseLastResult.Contains("Recycle Bin:"),
            "Opted-in scheduled cleanup filters the bin by age and saves results for both categories");
        var calls = bin.ScanCalls; Await(controller.RunScheduledAsync());
        check(bin.ScanCalls == calls, "Recycle Bin inclusion shares the saved schedule and does not immediately repeat");

        bin.FailScan = true; Await(controller.PreviewRecycleAsync());
        check(!controller.IsBusy && controller.RecyclePreview == null && controller.RecycleStatus.Contains("preview failed"),
            "Recycle Bin access failures are displayed and release the shared operation for retry");
        bin.FailScan = false; var retry = Add("retry.txt"); Await(controller.PreviewRecycleAsync());
        bin.RefuseDelete = true; Await(controller.CleanRecycleAsync());
        check(bin.Items.ContainsKey(retry.Id) && controller.RecycleStatus.Contains("fixture access denied") && !controller.IsBusy,
            "Recycle Bin deletion failures preserve items and display an actionable result");
        bin.RefuseDelete = false;
        using (var gate = new ManualResetEventSlim(false))
        {
            bin.ScanGate = gate; bin.Started.Reset();
            var preview = controller.PreviewRecycleAsync(); PumpUntil(() => bin.Started.IsSet);
            var heldCalls = bin.ScanCalls; Await(controller.PreviewAsync()); Await(controller.RunScheduledAsync());
            check(controller.IsBusy && controller.IsRecycleBusy && bin.ScanCalls == heldCalls && !panel.PreviewButton.IsEnabled && !panel.PreviewRecycleButton.IsEnabled && panel.StopRecycleButton.IsEnabled,
                "Temporary previews and scheduled work cannot overlap an active Recycle Bin operation");
            panel.StopRecycleButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Await(preview);
            check(!controller.IsBusy && controller.RecyclePreview == null && controller.RecycleStatus.Contains("stopped") && bin.Items.ContainsKey(retry.Id),
                "Recycle Bin Stop cancels a blocked preview and preserves all items");
            bin.ScanGate = null;
        }

        panel.IncludeRecycleBin.IsChecked = true;
        var temporaryPreview = controller.PreviewAsync();
        check(controller.IsBusy && !controller.IsRecycleBusy, "Temporary-file work is identified separately from Recycle Bin work");
        panel.IncludeRecycleBin.IsChecked = false; Await(temporaryPreview);
        check(controller.Preview != null && controller.TemporaryStatus.Contains("Preview:"),
            "Turning Recycle Bin inclusion off preserves an active temporary-file preview");

        // Cancelling the inclusion toggle during scheduled work must stop before bin deletion.
        panel.IncludeRecycleBin.IsChecked = true;
        settings.StorageSenseLastAttemptUtc = now.AddDays(-8);
        using (var gate = new ManualResetEventSlim(false))
        {
            bin.ScanGate = gate; bin.Started.Reset();
            var run = controller.RunScheduledAsync(); PumpUntil(() => bin.Started.IsSet);
            var cleanCalls = bin.CleanCalls; panel.IncludeRecycleBin.IsChecked = false; Await(run);
            check(!settings.StorageSenseIncludeRecycleBin && settings.StorageSenseEnabled && bin.CleanCalls == cleanCalls && bin.Items.ContainsKey(retry.Id),
                "Disabling Recycle Bin inclusion cancels scheduled bin work while keeping temporary-file scheduling enabled");
            bin.ScanGate = null;
        }
        var blocker = Path.Combine(fixture, "blocking-file"); File.WriteAllText(blocker, "not a directory");
        var blockedStore = new SettingsStore(Path.Combine(blocker, "settings.json"));
        using var blockedController = new StorageSenseController(settings, blockedStore, new StorageSenseEngine(temp, Array.Empty<string>()), () => now, bin);
        using var blockedPanel = new StorageSensePanel(blockedController, settings, blockedStore, _ => true);
        var checkpoint = settings.StorageSenseLastAttemptUtc; blockedPanel.IncludeRecycleBin.IsChecked = true;
        check(!settings.StorageSenseIncludeRecycleBin && blockedPanel.IncludeRecycleBin.IsChecked == false && settings.StorageSenseLastAttemptUtc == checkpoint && blockedPanel.Status.Text.Contains("not changed"),
            "Failed Recycle Bin preference saves restore inclusion and the schedule checkpoint");
        settings.StorageSenseIncludeRecycleBin = true; settings.StorageSenseLastAttemptUtc = now.AddDays(-8);
        calls = bin.ScanCalls; Await(blockedController.RunScheduledAsync());
        check(bin.ScanCalls == calls && blockedController.Status.Contains("did not run"),
            "Recycle Bin automatic cleanup is refused when its attempt checkpoint cannot be saved");
        Await(blockedController.PreviewRecycleAsync()); Await(blockedController.CleanRecycleAsync());
        check(blockedController.RecycleStatus.Contains("Result could not be saved"),
            "Recycle Bin result persistence failures remain visible next to the cleanup controls");
        settings.StorageSenseEnabled = false; settings.StorageSenseIncludeRecycleBin = false;
        panel.Enabled.IsChecked = false; panel.IncludeRecycleBin.IsChecked = false;
        Add("preview 日本語 file.txt"); Add("preview folder", isFolder: true);
        Await(controller.PreviewRecycleAsync());
        var mode = Application.Current.ShutdownMode; Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var scroll = new ScrollViewer { Content = panel, Style = (Style)Application.Current.FindResource("SettingsScrollViewer"), VerticalScrollBarVisibility = ScrollBarVisibility.Visible, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        var surface = new Border { Background = Palette.Background, Child = scroll, Padding = new Thickness(12) };
        var window = new Window { Content = surface, Width = 1000, Height = 920, ShowInTaskbar = false };
        try
        {
            window.Show();
            foreach (var width in new[] { 1000d, 600d })
            {
                window.Width = width; window.Height = width == 600 ? 700 : 920; window.UpdateLayout();
                scroll.ScrollToVerticalOffset(panel.PreviewRecycleButton.TransformToAncestor(panel).Transform(new Point()).Y - 150); window.UpdateLayout();
                var bounds = panel.CleanRecycleButton.TransformToAncestor(scroll).TransformBounds(new Rect(panel.CleanRecycleButton.RenderSize));
                check(bounds.Left >= 0 && bounds.Right <= scroll.ViewportWidth && bounds.Top >= 0 && bounds.Bottom <= scroll.ViewportHeight,
                    "Recycle Bin preview and cleanup controls fit the scrolled layout at width " + width);
                Capture(surface, Path.Combine(folder, "storage-sense-recycle-" + width + ".png"));
            }
        }
        finally { window.Content = null; scroll.Content = null; window.Close(); Application.Current.ShutdownMode = mode; }
    }
    sealed class BinFixture : IRecycleBinCleanup
    {
        internal readonly Dictionary<string, RecycleBinItem> Items = new();
        internal readonly ManualResetEventSlim Started = new(false);
        internal ManualResetEventSlim? ScanGate;
        internal int ScanCalls, CleanCalls;
        internal bool FailScan, RefuseDelete;
        internal RecycleBinPreview? LastApproved;
        internal RecycleBinItem Add(RecycleBinItem item) { Items[item.Id] = item; return item; }
        public RecycleBinPreview Scan(DateTime cutoff, CancellationToken cancellation)
        {
            Interlocked.Increment(ref ScanCalls); Started.Set(); ScanGate?.Wait(cancellation); cancellation.ThrowIfCancellationRequested();
            if (FailScan) throw new IOException("fixture unavailable");
            return new(cutoff, Items.Values.Where(i => i.DeletedUtc <= cutoff).ToArray(), 0, false, null);
        }
        public RecycleBinResult Clean(RecycleBinPreview preview, CancellationToken cancellation)
        {
            Interlocked.Increment(ref CleanCalls); LastApproved = preview;
            int deleted = 0, skipped = 0; long bytes = 0;
            foreach (var item in preview.Items)
            {
                if (cancellation.IsCancellationRequested) break;
                if (RefuseDelete || !Items.TryGetValue(item.Id, out var current) || current != item || current.DeletedUtc > preview.CutoffUtc) { skipped++; continue; }
                Items.Remove(item.Id); deleted++; bytes += item.Bytes ?? 0;
            }
            return new(deleted, bytes, skipped, cancellation.IsCancellationRequested, RefuseDelete ? "fixture access denied" : null);
        }
    }
    static void Await(Task task) { PumpUntil(() => task.IsCompleted); task.GetAwaiter().GetResult(); }
    static void PumpUntil(Func<bool> complete)
    {
        var timeout = Stopwatch.StartNew();
        while (!complete())
        {
            if (timeout.Elapsed > TimeSpan.FromSeconds(30)) throw new TimeoutException("Recycle Bin policy fixture timed out.");
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle); Thread.Sleep(5);
        }
    }
    static void Capture(FrameworkElement content, string path)
    {
        var drawing = new DrawingVisual(); using (var context = drawing.RenderOpen()) context.DrawRectangle(new VisualBrush(content), null, new Rect(content.RenderSize));
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth), (int)Math.Ceiling(content.ActualHeight), 96, 96, PixelFormats.Pbgra32); bitmap.Render(drawing);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var stream = File.Create(path); encoder.Save(stream);
    }
}
