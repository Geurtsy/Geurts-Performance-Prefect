using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace GeurtsPerformancePrefect;

public sealed class BitLockerPanel : StackPanel
{
    internal ComboBox Drives { get; } = new();
    internal Button RefreshButton { get; } = new() { Content = "Load / refresh BitLocker drives", HorizontalAlignment = HorizontalAlignment.Left };
    internal Button DecryptButton { get; } = new() { Content = "Turn off BitLocker and decrypt drive…", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0,8,0,0), IsEnabled = false };
    internal Button AdministratorButton { get; } = new() { Content = "Restart as administrator for BitLocker", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0,8,0,0) };
    internal TextBlock Status { get; } = Note("");
    internal TextBlock Result { get; } = Note("");
    internal Task Operation { get; private set; } = Task.CompletedTask;
    internal bool IsChanging { get; private set; }
    readonly BitLockerService service;
    readonly Func<string, bool> confirm;
    readonly bool administrator;
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(15) };
    bool busy;
    public BitLockerPanel(Action restartAdministrator) : this(new BitLockerService(new WindowsBitLockerBackend()),
        HardwareSampler.IsAdministrator, restartAdministrator) { }
    internal BitLockerPanel(BitLockerService service, bool administrator, Action? restartAdministrator = null, Func<string, bool>? confirm = null)
    {
        this.service = service; this.administrator = administrator;
        this.confirm = confirm ?? (message => MessageBox.Show(Window.GetWindow(this), message,
            "Geurts Performance Prefect · BitLocker", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes);
        Children.Add(new TextBlock { Text = "BitLocker drive decryption", FontSize = 15, Foreground = Palette.Text,
            FontWeight = FontWeights.SemiBold, Margin = new Thickness(0,14,0,0) });
        Children.Add(Note("Turn off BitLocker on one selected drive and decrypt its contents. Files stay on the drive, but lose BitLocker's protection against offline access. This does not remove separate EFS file encryption."));
        Children.Add(RefreshButton);
        Children.Add(Note("Choose a drive explicitly. Locked drives must first be unlocked in Windows with an authorized password or recovery key."));
        System.Windows.Automation.AutomationProperties.SetName(Drives, "BitLocker drive to decrypt");
        Children.Add(Drives); Children.Add(DecryptButton); Children.Add(AdministratorButton); Children.Add(Status); Children.Add(Result);
        Children.Add(Note("Windows handles decryption in the background; closing Prefect after Windows accepts the request does not cancel it. Keep the drive connected and power available. Refresh to check progress. While this tab is visible, an actively decrypting selected drive refreshes every 15 seconds. Re-enabling BitLocker requires a separate encryption setup in Windows."));
        var manage = new Button { Content = "Open Windows BitLocker management", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0,8,0,0) };
        manage.Click += (_, _) =>
        {
            try
            {
                var info = new ProcessStartInfo(System.IO.Path.Combine(Environment.SystemDirectory, "control.exe")) { UseShellExecute = true };
                info.ArgumentList.Add("/name"); info.ArgumentList.Add("Microsoft.BitLockerDriveEncryption"); Process.Start(info)?.Dispose();
            }
            catch (Exception ex) { Result.Text = "Could not open BitLocker management: " + ex.Message; }
        };
        Children.Add(manage);
        Children.Add(Note("Nothing is decrypted on startup or merely by opening this tab. Administrator access and Windows BitLocker support are required. Organization policies or auto-unlock dependencies may prevent decryption; Prefect does not clear saved keys or bypass those restrictions."));
        RefreshButton.IsEnabled = administrator;
        Drives.IsEnabled = administrator;
        AdministratorButton.Visibility = administrator ? Visibility.Collapsed : Visibility.Visible;
        AdministratorButton.IsEnabled = restartAdministrator != null;
        AdministratorButton.Click += (_, _) => restartAdministrator?.Invoke();
        Status.Text = administrator ? "Load the BitLocker drives, then choose the drive you want to decrypt." : "Restart Prefect as administrator to load BitLocker status. No encryption has been changed.";
        RefreshButton.Click += (_, _) => Operation = RefreshAsync();
        DecryptButton.Click += (_, _) => Operation = DecryptAsync();
        Drives.SelectionChanged += (_, _) => { if (!busy) UpdateSelected(); };
        timer.Tick += (_, _) =>
        {
            if (IsVisible && !busy && Drives.SelectedItem is BitLockerVolume { Conversion: BitLockerConversion.Decrypting }) Operation = RefreshAsync();
        };
        Loaded += (_, _) => timer.Start(); Unloaded += (_, _) => timer.Stop();
    }
    internal static string Warning(BitLockerVolume volume) => $"Turn off BitLocker and decrypt {volume.Drive}?\n\nVolume: {volume.Id}\n\n" +
        "This decrypts the selected drive and its contents, removing BitLocker protection against offline access. Files are kept. " +
        "If encryption is in progress, this reverses it; if decryption is paused, it resumes it.\n\n" +
        "Keep a current backup and your recovery key available until the operation finishes. Windows removes the drive's key protectors when decryption completes. " +
        "Re-enabling BitLocker later requires a separate encryption setup. Keep the drive connected and power available.\n\n" +
        "Decryption can take a long time and continues in Windows after Prefect closes. This does not decrypt separate EFS-encrypted files.\n\n" +
        "Start decryption of " + volume.Drive + "?";
    void Controls(bool enabled)
    {
        RefreshButton.IsEnabled = Drives.IsEnabled = enabled && administrator;
        DecryptButton.IsEnabled = enabled && administrator && Drives.SelectedItem is BitLockerVolume { CanDecrypt: true };
    }
    void UpdateSelected()
    {
        DecryptButton.IsEnabled = administrator && !busy && Drives.SelectedItem is BitLockerVolume { CanDecrypt: true };
        if (Drives.SelectedItem is not BitLockerVolume volume)
        { Status.Text = "Choose a drive. No drive is selected for decryption."; return; }
        Status.Text = volume.Drive + " · " + volume.State + ".";
        if (volume.EncryptedPercent != null) Status.Text += " Still encrypted: " + volume.EncryptedPercent + "%.";
        Status.Text += volume.Protection switch { 0 => " Protection: Off (this alone does not mean fully decrypted).", 1 => " Protection: On.", _ => "" };
        if (volume.Locked) Status.Text += " Unlock this drive in Windows first.";
        if (volume.Error != null) Status.Text += " " + volume.Error;
        DecryptButton.Content = volume.Conversion == BitLockerConversion.DecryptionPaused ? "Resume BitLocker decryption…" : "Turn off BitLocker and decrypt drive…";
    }
    async Task RefreshAsync()
    {
        if (busy || !administrator) return;
        var target = (Drives.SelectedItem as BitLockerVolume)?.Target;
        busy = true; Controls(false); Status.Text = "Reading BitLocker drives…";
        try
        {
            var volumes = await Task.Run(service.Read);
            Result.Text = "";
            Drives.ItemsSource = volumes;
            Drives.SelectedItem = volumes.FirstOrDefault(v => v.Target == target);
            busy = false; UpdateSelected();
            if (volumes.Count == 0) Status.Text = "Windows returned no supported BitLocker volumes with drive letters.";
        }
        catch (Exception ex)
        {
            Drives.ItemsSource = null;
            Status.Text = "Could not read BitLocker drives: " + ex.Message + " BitLocker may be unavailable or managed by your organization.";
        }
        finally { busy = false; Controls(true); }
    }
    async Task DecryptAsync()
    {
        if (busy || !administrator || Drives.SelectedItem is not BitLockerVolume { CanDecrypt: true } selected) return;
        if (!confirm(Warning(selected))) { Result.Text = "Cancelled. No BitLocker setting was changed."; return; }
        busy = IsChanging = true; Controls(false); Result.Text = "Asking Windows to decrypt " + selected.Drive + "…";
        try
        {
            var current = await Task.Run(() => service.Decrypt(selected.Target));
            var volumes = Drives.Items.Cast<BitLockerVolume>().Select(v => v.Target == selected.Target ? current : v).ToArray();
            Drives.ItemsSource = volumes; Drives.SelectedItem = current;
            Result.Text = current.Error ?? (current.Conversion switch
            {
                BitLockerConversion.FullyDecrypted => selected.Drive + " is fully decrypted. BitLocker is off.",
                BitLockerConversion.Decrypting => "Windows is decrypting " + selected.Drive + ". Refresh to check progress; closing Prefect does not cancel it.",
                _ => "Windows accepted the decryption request for " + selected.Drive + ". Refresh to confirm its current conversion status."
            });
        }
        catch (Exception ex)
        {
            Result.Text = "Could not confirm decryption: " + ex.Message;
            // Discard stale drive information so a retry always starts from a fresh inventory.
            Drives.ItemsSource = null;
        }
        finally { busy = IsChanging = false; UpdateSelected(); Controls(true); }
    }
    static TextBlock Note(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap,
        Foreground = Palette.Muted, FontSize = 12, Margin = new Thickness(0,7,0,7) };
}
