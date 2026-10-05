using System;
using System.IO;
using Microsoft.Win32;

namespace GeurtsPerformancePrefect;

public interface IStartupEntry
{
    string? Read();
    void Write(string? command);
}

public sealed class RegistryStartupEntry : IStartupEntry
{
    internal const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    internal const string ValueName = "GeurtsPerformancePrefect";
    readonly string keyPath;
    public RegistryStartupEntry() : this(RunKey) { }
    internal RegistryStartupEntry(string keyPath) => this.keyPath = keyPath;

    public string? Read()
    {
        using var key = Registry.CurrentUser.OpenSubKey(keyPath);
        return key?.GetValue(ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
    }

    public void Write(string? command)
    {
        if (command == null)
        {
            using var key = Registry.CurrentUser.OpenSubKey(keyPath, writable: true);
            key?.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        else
        {
            using var key = Registry.CurrentUser.CreateSubKey(keyPath, writable: true);
            key.SetValue(ValueName, command, RegistryValueKind.String);
        }
    }
}

public sealed class WindowsStartup
{
    readonly IStartupEntry entry;
    readonly string executable;
    public WindowsStartup(IStartupEntry? entry = null, string? executable = null)
    {
        this.entry = entry ?? new RegistryStartupEntry();
        this.executable = executable ?? Path.Combine(AppContext.BaseDirectory, "GeurtsPerformancePrefect.exe");
    }
    public string Command => "\"" + executable + "\" --windows-startup";
    public string? Read() => entry.Read();
    public void SetEnabled(bool enabled)
    {
        if (enabled && (!Path.IsPathFullyQualified(executable) || executable.Contains('"') ||
            !File.Exists(executable) || !executable.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("The application executable could not be found. Extract the release ZIP before enabling startup.");
        if (enabled && Command.Length > 260)
            throw new InvalidOperationException("The application folder path is too long for Windows startup. Move the application to a shorter path and try again.");
        entry.Write(enabled ? Command : null);
        if (!string.Equals(entry.Read(), enabled ? Command : null, StringComparison.Ordinal))
            throw new IOException("Windows did not confirm the startup change. Check the setting and try again.");
    }
}
