using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace GeurtsPerformancePrefect;

public sealed class SecurityPanel : StackPanel
{
    internal Button DisableUacButton { get; } = new() { Content = "Run all apps as administrator without prompts…", HorizontalAlignment = HorizontalAlignment.Left };
    internal Button RestoreUacButton { get; } = new() { Content = "Restore / enable UAC protection", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0,8,0,0) };
    internal Button RefreshButton { get; } = new() { Content = "Refresh security status", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0,8,0,0) };
    internal TextBlock Status { get; } = Note("");
    internal TextBlock Result { get; } = Note("");
    internal Task Operation { get; private set; } = Task.CompletedTask;
    readonly WindowsSecurity security;
    readonly Func<string, bool> confirm;
    bool busy;
    internal const string DisableWarning = "Disable User Account Control (UAC) for this entire computer?\n\n" +
        "After you restart Windows, apps launched by administrator accounts can run with full administrator permissions without approval prompts. " +
        "Malicious or unwanted software can also make system changes without asking. Standard accounts do not become administrators. " +
        "Some apps and Windows features may behave differently.\n\n" +
        "Prefect saves the previous UAC setting so you can restore it here. Windows may ask for approval to apply this change. " +
        "You must restart Windows yourself; Prefect will not restart it.\n\nDisable UAC?";

    public SecurityPanel() : this(new WindowsSecurity()) { }
    internal SecurityPanel(WindowsSecurity security, Func<string, bool>? confirm = null)
    {
        this.security = security;
        this.confirm = confirm ?? (message => MessageBox.Show(Window.GetWindow(this), message,
            "Geurts Performance Prefect · Windows security", MessageBoxButton.YesNo, MessageBoxImage.Warning,
            MessageBoxResult.No) == MessageBoxResult.Yes);
        Children.Add(new TextBlock { Text = "Windows-wide administrator mode", FontSize = 15, Foreground = Palette.Text,
            FontWeight = FontWeights.SemiBold, Margin = new Thickness(0,14,0,0) });
        Children.Add(Note("Applies to all apps launched by administrator accounts across Windows. Disables User Account Control (UAC), reducing protection against unwanted system changes. Standard accounts stay limited; applications can still choose to run with restricted permissions."));
        Children.Add(Note("Off unless Windows is already configured this way. Applying or restoring requires administrator access and a Windows restart. Opening this tab makes no changes."));
        Children.Add(DisableUacButton); Children.Add(RestoreUacButton); Children.Add(RefreshButton);
        Children.Add(Status); Children.Add(Result);
        Children.Add(Note("Restore uses the previous UAC setting saved by Prefect. If no backup exists, it enables UAC. Other UAC prompt policies are preserved, so their existing configuration determines which prompts return. Organization policies may override this setting."));
        DisableUacButton.Click += (_, _) => Operation = Run(true);
        RestoreUacButton.Click += (_, _) => Operation = Run(false);
        RefreshButton.Click += (_, _) => Refresh();
        Refresh();
    }
    internal void Refresh()
    {
        if (busy) return;
        try
        {
            var state = security.Read();
            Status.Text = state.Disabled ? "Configured policy: UAC disabled (Windows-wide administrator mode)." : "Configured policy: UAC enabled.";
            Status.Text += " Restart Windows after changing this setting; this status shows the saved policy, not the current session's permissions.";
            Status.Text += state.HasBackup ? " Previous UAC setting is available to restore." : " No Prefect UAC backup is saved.";
            DisableUacButton.IsEnabled = !state.Disabled;
            RestoreUacButton.IsEnabled = state.Disabled || state.HasBackup;
        }
        catch (Exception ex)
        {
            Status.Text = "Could not read Windows security: " + ex.Message;
            DisableUacButton.IsEnabled = RestoreUacButton.IsEnabled = false;
        }
    }
    async Task Run(bool disable)
    {
        if (busy || !(disable ? DisableUacButton.IsEnabled : RestoreUacButton.IsEnabled)) return;
        if (!confirm(disable ? DisableWarning : "Restore the previous UAC setting, or enable UAC if no backup is saved?\n\nThis applies across Windows. Restart Windows yourself afterward. Prefect will not restart it."))
        { Result.Text = "Cancelled. No security setting was changed."; return; }
        busy = true;
        DisableUacButton.IsEnabled = RestoreUacButton.IsEnabled = RefreshButton.IsEnabled = false;
        Result.Text = "Waiting for Windows to apply the security setting…";
        try
        {
            await security.ChangeAsync(disable);
            var state = security.Read();
            if (state.Disabled != disable) throw new InvalidOperationException("Windows did not confirm the requested policy. It may be managed by your organization.");
            Result.Text = disable ? "UAC disabled in the saved policy. Restart Windows yourself to apply administrator mode without prompts." :
                "UAC protection restored / enabled in the saved policy. Restart Windows yourself to apply it.";
        }
        catch (Exception ex) { Result.Text = ex.Message; }
        finally { busy = false; RefreshButton.IsEnabled = true; Refresh(); }
    }
    static TextBlock Note(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap,
        Foreground = Palette.Muted, FontSize = 12, Margin = new Thickness(0,7,0,7) };
}
