using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace GeurtsPerformancePrefect;

internal static class VisualEffectsVerification
{
    sealed class Fixture : IVisualEffectsBackend
    {
        internal Dictionary<string, int?> Values = WindowsVisualEffectsBackend.Effects.ToDictionary(e => e.Name,
            e => e.Key == null ? (int?)1 : null);
        internal int Writes;
        internal string? FailRead, FailWrite, IgnoreWrite;
        internal bool FailOnce;
        public int? Read(VisualEffect effect)
        {
            if (effect.Name == FailRead) throw new IOException("fixture read denied");
            return Values[effect.Name];
        }
        public void Write(VisualEffect effect, int? value)
        {
            Writes++;
            if (effect.Name != IgnoreWrite) Values[effect.Name] = value;
            if (effect.Name == FailWrite)
            {
                if (FailOnce) FailWrite = null;
                throw new IOException("fixture write denied");
            }
        }
    }
    static string Error(Action action)
    {
        try { action(); return ""; } catch (Exception ex) { return ex.Message; }
    }
    static void Click(VisualEffectsPanel panel, Button button)
    {
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!panel.Operation.IsCompleted && DateTime.UtcNow < deadline)
            panel.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        if (!panel.Operation.IsCompleted) throw new TimeoutException("Visual effects fixture timed out.");
        panel.Operation.GetAwaiter().GetResult();
    }
    internal static void Run(string folder, Action<bool, string> check)
    {
        var root = Path.Combine(folder, "visual-effects-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var backup = Path.Combine(root, "previous.json");
        var fixture = new Fixture();
        var original = new Dictionary<string, int?>(fixture.Values);
        bool Original() => fixture.Values.All(v => original[v.Key] == v.Value);
        var service = new VisualEffectsService(fixture, backup);
        var panel = new VisualEffectsPanel(service) { Width = 330, Background = Palette.Background };
        check(fixture.Writes == 0 && !File.Exists(backup) && !panel.RestoreButton.IsEnabled,
            "Opening Windows aesthetics performs no writes or startup actions and disables Restore without a backup");
        Click(panel, panel.ApplyButton);
        check(fixture.Values.All(v => v.Value == WindowsVisualEffectsBackend.Effects.Single(e => e.Name == v.Key).Performance) &&
            panel.Status.Text.Contains("Best performance settings saved") && panel.RestoreButton.IsEnabled,
            "Explicit best performance click disables the full effect set and exposes Restore after verification");
        var saved = File.ReadAllText(backup);
        Click(panel, panel.ApplyButton);
        check(File.ReadAllText(backup) == saved, "Repeated best performance clicks preserve the original appearance backup");
        var reopened = new VisualEffectsPanel(new VisualEffectsService(fixture, backup));
        check(reopened.RestoreButton.IsEnabled, "Previous appearance remains restorable after reopening Prefect");
        Verification.Render(panel, Path.Combine(folder, "windows-aesthetics-panel.png"), 330, 570);
        Click(reopened, reopened.RestoreButton);
        check(Original() && !File.Exists(backup) && !reopened.RestoreButton.IsEnabled,
            "Restore recovers the exact original values including absent shell preferences and clears the backup only after verification");
        fixture.FailRead = "Menu animations"; var writes = fixture.Writes;
        check(Error(() => service.Apply()).Contains("read denied") && fixture.Writes == writes && !File.Exists(backup),
            "Unreadable current appearance stops before saving or changing Windows settings");
        fixture.FailRead = null;
        fixture.FailWrite = "Menu animations"; fixture.FailOnce = true;
        check(Error(() => service.Apply()).Contains("Previous settings restored") && Original() && !File.Exists(backup),
            "A setter that changes state then throws is included in the complete rollback");
        fixture.IgnoreWrite = "Font smoothing";
        check(Error(() => service.Apply()).Contains("did not confirm Font smoothing") && Original() && !File.Exists(backup),
            "A silently ignored Windows write fails read-back verification and rolls back the preset");
        fixture.IgnoreWrite = null; fixture.FailWrite = "Menu animations"; fixture.FailOnce = false;
        check(Error(() => service.Apply()).Contains("Some settings could not be restored") && File.Exists(backup) && Original(),
            "Rollback errors retain the durable original backup and report recovery is needed");
        fixture.FailWrite = null; service.Restore();
        service.Apply(); fixture.FailWrite = "Menu animations"; fixture.FailOnce = true;
        check(Error(() => service.Restore()).Contains("write denied") && File.Exists(backup),
            "A failed Restore keeps its backup available for retry");
        service.Restore();
        check(Original() && !File.Exists(backup), "Retrying Restore recovers every preference after a partial restore");
        service.Apply(); fixture.FailWrite = "Menu animations"; fixture.FailOnce = true;
        Error(() => service.Apply()); service.Restore();
        check(Original(), "A failed reapply rolls back to current values while retaining the first original appearance for Restore");
        File.WriteAllText(backup, "{}"); writes = fixture.Writes;
        check(Error(() => service.Apply()).Contains("invalid") && Error(() => service.Restore()).Contains("invalid") && fixture.Writes == writes,
            "Malformed appearance backups block Apply and Restore without any Windows writes");
        File.Delete(backup);
        var blocked = Path.Combine(root, "blocked"); File.WriteAllText(blocked, "fixture");
        check(Error(() => new VisualEffectsService(fixture, Path.Combine(blocked, "previous.json")).Apply()) != "" && fixture.Writes == writes,
            "Failure to save a durable recovery snapshot prevents all Windows writes");
        fixture.FailRead = "Menu animations";
        Click(panel, panel.ApplyButton);
        check(panel.ApplyButton.IsEnabled && !panel.RestoreButton.IsEnabled && panel.Status.Text.Contains("read denied"),
            "Windows aesthetics errors remain visible and re-enable the action for retry");
        // Native coverage is strictly read-only. Setters are exercised only against the fixture.
        var native = new WindowsVisualEffectsBackend();
        check(WindowsVisualEffectsBackend.Effects.Where(e => e.Key == null).All(e => native.Read(e) is 0 or 1),
            "Native Windows visual effect getters and animation structure return valid booleans without changing the desktop");
        Directory.Delete(root, true);
    }
}
