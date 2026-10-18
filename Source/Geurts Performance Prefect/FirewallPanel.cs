using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace GeurtsPerformancePrefect;

public sealed class FirewallPanel : StackPanel
{
    internal Button DisableButton { get; } = new() { Content = "Disable Windows Firewall…", HorizontalAlignment = HorizontalAlignment.Left };
    internal Button RestoreButton { get; } = new() { Content = "Restore / enable Windows Firewall", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0,8,0,0) };
    internal Button RefreshButton { get; } = new() { Content = "Refresh firewall status", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0,8,0,0) };
    internal TextBlock Status { get; } = Note("");
    internal TextBlock Result { get; } = Note("");
    internal Task Operation { get; private set; } = Task.CompletedTask;
    readonly WindowsFirewall firewall;
    readonly Func<string, bool> confirm;
    bool busy;
    internal const string DisableWarning = "Disable Windows Firewall for this entire computer?\n\n" +
        "This turns off the Domain, Private and Public firewall profiles, including public Wi-Fi networks. " +
        "It reduces protection against unwanted network access. The change persists until you restore or change it.\n\n" +
        "Prefect saves each profile's current enabled/disabled setting first. Rules are preserved and the firewall service stays running. " +
        "Administrator approval may be required. No Windows restart is normally needed.\n\nDisable all three firewall profiles?";
    public FirewallPanel() : this(new WindowsFirewall()) { }
    internal FirewallPanel(WindowsFirewall firewall, Func<string, bool>? confirm = null)
    {
        this.firewall = firewall;
        this.confirm = confirm ?? (message => MessageBox.Show(Window.GetWindow(this), message,
            "Geurts Performance Prefect · Windows Firewall", MessageBoxButton.YesNo, MessageBoxImage.Warning,
            MessageBoxResult.No) == MessageBoxResult.Yes);
        Children.Add(new TextBlock { Text = "Windows Firewall", FontSize = 15, Foreground = Palette.Text,
            FontWeight = FontWeights.SemiBold, Margin = new Thickness(0,14,0,0) });
        Children.Add(Note("Disable Windows Firewall for Domain, Private and Public networks across this computer. This reduces protection against unwanted network access, including on public Wi-Fi."));
        Children.Add(Note("Applies only when you confirm the action. Administrator access is required; no Windows restart is normally needed. Nothing is applied automatically when Prefect starts."));
        Children.Add(DisableButton); Children.Add(RestoreButton); Children.Add(RefreshButton);
        Children.Add(Status); Children.Add(Result);
        Children.Add(Note("Restore recovers each profile's saved enabled/disabled setting. Without a backup, it enables all three profiles. Firewall rules, the firewall service, antivirus and UAC settings are preserved. Organization policies or other security products can override local settings."));
        DisableButton.Click += (_, _) => Operation = Run(true);
        RestoreButton.Click += (_, _) => Operation = Run(false);
        RefreshButton.Click += (_, _) => Refresh();
        Refresh();
    }
    internal void Refresh()
    {
        if (busy) return;
        try
        {
            var state = firewall.Read();
            static string On(bool value) => value ? "On" : "Off";
            Status.Text = $"Local profile settings · Domain: {On(state.Profiles.Domain)} · Private: {On(state.Profiles.Private)} · Public: {On(state.Profiles.Public)}.";
            Status.Text += state.HasBackup ? " Previous profile settings are available to restore." : " No Prefect firewall backup is saved.";
            Status.Text += " Effective protection may differ on managed computers.";
            DisableButton.IsEnabled = state.Profiles != FirewallProfiles.AllOff;
            RestoreButton.IsEnabled = state.HasBackup || state.Profiles != FirewallProfiles.AllOn;
        }
        catch (Exception ex)
        {
            Status.Text = "Could not read Windows Firewall: " + ex.Message;
            DisableButton.IsEnabled = RestoreButton.IsEnabled = false;
        }
    }
    async Task Run(bool disable)
    {
        if (busy || !(disable ? DisableButton.IsEnabled : RestoreButton.IsEnabled)) return;
        if (!confirm(disable ? DisableWarning : "Restore the saved Windows Firewall profile settings? Without a backup, all three profiles will be enabled.\n\nApplies across this computer. Administrator approval may be required."))
        { Result.Text = "Cancelled. No firewall setting was changed."; return; }
        busy = true;
        DisableButton.IsEnabled = RestoreButton.IsEnabled = RefreshButton.IsEnabled = false;
        Result.Text = "Waiting for Windows to apply the firewall settings…";
        try
        {
            var before = firewall.Read();
            await firewall.ChangeAsync(disable);
            var after = firewall.Read();
            if ((disable && after.Profiles != FirewallProfiles.AllOff) || (!disable && (after.HasBackup || (!before.HasBackup && after.Profiles != FirewallProfiles.AllOn))))
                throw new InvalidOperationException("Windows did not confirm the requested local firewall settings. Refresh the status and check your organization policy.");
            Result.Text = disable ? "Windows Firewall disabled in all three local profiles. Use Restore / enable Windows Firewall to recover protection." :
                "Windows Firewall profile settings " + (before.HasBackup ? "restored from the saved backup." : "enabled for all three profiles.");
        }
        catch (Exception ex) { Result.Text = ex.Message; }
        finally { busy = false; RefreshButton.IsEnabled = true; Refresh(); }
    }
    static TextBlock Note(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap,
        Foreground = Palette.Muted, FontSize = 12, Margin = new Thickness(0,7,0,7) };
}
