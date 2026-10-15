using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32;

namespace GeurtsPerformancePrefect;

internal sealed record VisualEffect(string Name, uint Get = 0, uint Set = 0, bool UiParameter = false,
    string? Key = null, string? Value = null, int Performance = 0);

internal interface IVisualEffectsBackend
{
    int? Read(VisualEffect effect);
    void Write(VisualEffect effect, int? value);
}

internal sealed class WindowsVisualEffectsBackend : IVisualEffectsBackend
{
    const string Explorer = @"Software\Microsoft\Windows\CurrentVersion\Explorer";
    const string Dwm = @"Software\Microsoft\Windows\DWM";
    internal static readonly VisualEffect[] Effects =
    {
        new("Window animations", 0x0048, 0x0049),
        new("Client area animations", 0x1042, 0x1043),
        new("Menu animations", 0x1002, 0x1003),
        new("Combo box animations", 0x1004, 0x1005),
        new("Smooth scrolling", 0x1006, 0x1007),
        new("Menu fading", 0x1012, 0x1013),
        new("Selection fading", 0x1014, 0x1015),
        new("Tooltip animations", 0x1016, 0x1017),
        new("Tooltip fading", 0x1018, 0x1019),
        new("Pointer shadows", 0x101A, 0x101B),
        new("Window shadows", 0x1024, 0x1025),
        new("Font smoothing", 0x004A, 0x004B, true),
        new("Window contents while dragging", 0x0026, 0x0025, true),
        // Shell preferences have no equivalent SystemParametersInfo setters. Windows may
        // defer these until the next sign-in; do not restart Explorer to force them live.
        new("Taskbar animations", Key: Explorer + @"\Advanced", Value: "TaskbarAnimations"),
        new("Thumbnail previews", Key: Explorer + @"\Advanced", Value: "IconsOnly", Performance: 1),
        new("Translucent selection rectangle", Key: Explorer + @"\Advanced", Value: "ListviewAlphaSelect"),
        new("Desktop icon label shadows", Key: Explorer + @"\Advanced", Value: "ListviewShadow"),
        new("Peek", Key: Dwm, Value: "EnableAeroPeek"),
        new("Saved taskbar thumbnail previews", Key: Dwm, Value: "AlwaysHibernateThumbnails"),
        new("Visual effects", 0x103E, 0x103F),
        new("Performance selection", Key: Explorer + @"\VisualEffects", Value: "VisualFXSetting", Performance: 2)
    };

    public int? Read(VisualEffect effect)
    {
        if (effect.Key != null)
        {
            using var key = Registry.CurrentUser.OpenSubKey(effect.Key);
            var value = key?.GetValue(effect.Value!, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            if (value == null) return null;
            if (value is not int number) throw new InvalidOperationException("Unexpected Windows value for " + effect.Name + ".");
            return number;
        }
        if (effect.Get == 0x0048)
        {
            var animation = new AnimationInfo { Size = (uint)Marshal.SizeOf<AnimationInfo>() };
            Confirm(Animation(effect.Get, animation.Size, ref animation, 0), effect);
            return animation.Enabled == 0 ? 0 : 1;
        }
        Confirm(Get(effect.Get, 0, out var enabled, 0), effect);
        return enabled == 0 ? 0 : 1;
    }

    public void Write(VisualEffect effect, int? value)
    {
        if (effect.Key != null)
        {
            if (value == null)
            {
                using var existing = Registry.CurrentUser.OpenSubKey(effect.Key, true);
                existing?.DeleteValue(effect.Value!, false);
            }
            else
            {
                using var key = Registry.CurrentUser.CreateSubKey(effect.Key, true);
                key.SetValue(effect.Value!, value.Value, RegistryValueKind.DWord);
            }
            return;
        }
        if (value is not (0 or 1)) throw new InvalidOperationException("Invalid value for " + effect.Name + ".");
        // Persist through the supported API and notify applications. Never overwrite UserPreferencesMask.
        if (effect.Set == 0x0049)
        {
            var animation = new AnimationInfo { Size = (uint)Marshal.SizeOf<AnimationInfo>(), Enabled = value.Value };
            Confirm(Animation(effect.Set, animation.Size, ref animation, 3), effect);
        }
        else Confirm(Set(effect.Set, effect.UiParameter ? (uint)value.Value : 0,
            effect.UiParameter ? IntPtr.Zero : new IntPtr(value.Value), 3), effect);
    }

    static void Confirm(bool success, VisualEffect effect)
    {
        if (!success) throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not access " + effect.Name + ".");
    }
    [StructLayout(LayoutKind.Sequential)]
    struct AnimationInfo { public uint Size; public int Enabled; }
    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool Get(uint action, uint parameter, out int value, uint flags);
    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool Set(uint action, uint parameter, IntPtr value, uint flags);
    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool Animation(uint action, uint parameter, ref AnimationInfo value, uint flags);
}

internal sealed class VisualEffectsService(IVisualEffectsBackend backend, string backupPath)
{
    internal static VisualEffectsService Create() => new(new WindowsVisualEffectsBackend(),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GeurtsPerformancePrefect", "visual-effects-backup.json"));
    internal bool HasBackup => File.Exists(backupPath);
    Dictionary<string, int?> Read() => WindowsVisualEffectsBackend.Effects.ToDictionary(e => e.Name, e => backend.Read(e));
    Dictionary<string, int?> LoadBackup()
    {
        var values = JsonSerializer.Deserialize<Dictionary<string, int?>>(File.ReadAllText(backupPath))
            ?? throw new InvalidOperationException("The previous appearance backup is empty.");
        if (values.Count != WindowsVisualEffectsBackend.Effects.Length || WindowsVisualEffectsBackend.Effects.Any(e =>
            !values.ContainsKey(e.Name) || (e.Key == null && values[e.Name] is not (0 or 1))))
            throw new InvalidOperationException("The previous appearance backup is invalid. No settings were changed.");
        return values;
    }
    void Verify(Dictionary<string, int?> values)
    {
        foreach (var effect in WindowsVisualEffectsBackend.Effects)
            if (backend.Read(effect) != values[effect.Name])
                throw new InvalidOperationException("Windows did not confirm " + effect.Name + ".");
    }
    void Write(Dictionary<string, int?> values)
    {
        foreach (var effect in WindowsVisualEffectsBackend.Effects) backend.Write(effect, values[effect.Name]);
        Verify(values);
    }
    internal string Apply()
    {
        bool existing = HasBackup;
        if (existing) LoadBackup(); // Never replace or ignore an unreadable recovery snapshot.
        var before = Read();
        if (!existing)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(backupPath))!);
            // A complete recovery snapshot must be durable before the first Windows write.
            var temporary = backupPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    JsonSerializer.Serialize(stream, before);
                    stream.Flush(true);
                }
                File.Move(temporary, backupPath);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        try
        {
            Write(WindowsVisualEffectsBackend.Effects.ToDictionary(e => e.Name, e => (int?)e.Performance));
            return "Best performance settings saved for your Windows account. Some desktop and taskbar effects may require signing out and back in. Previous appearance is saved for Restore.";
        }
        catch (Exception error)
        {
            var recoveryErrors = new List<string>();
            // A failed API can already have changed a preference. Attempt every rollback,
            // including that preference, and keep the backup if recovery is incomplete.
            foreach (var effect in WindowsVisualEffectsBackend.Effects)
                try { backend.Write(effect, before[effect.Name]); } catch (Exception ex) { recoveryErrors.Add(ex.Message); }
            try { Verify(before); } catch (Exception ex) { recoveryErrors.Add(ex.Message); }
            if (recoveryErrors.Count == 0)
            {
                if (!existing) File.Delete(backupPath);
                throw new InvalidOperationException("Could not apply best performance: " + error.Message + " Previous settings restored.", error);
            }
            throw new InvalidOperationException("Could not apply best performance: " + error.Message +
                " Some settings could not be restored. Use Restore previous appearance to retry. " + string.Join(" ", recoveryErrors), error);
        }
    }
    internal string Restore()
    {
        var previous = LoadBackup();
        Write(previous); // Keep the backup on any failure so Restore can be retried.
        File.Delete(backupPath);
        return "Previous appearance restored. Some desktop and taskbar effects may require signing out and back in.";
    }
}
