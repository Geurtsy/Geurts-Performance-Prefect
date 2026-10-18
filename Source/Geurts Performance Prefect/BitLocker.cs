using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;

namespace GeurtsPerformancePrefect;

internal enum BitLockerConversion { FullyDecrypted, FullyEncrypted, Encrypting, Decrypting, EncryptionPaused, DecryptionPaused }
internal sealed record BitLockerTarget(string Id, string Drive);
internal sealed record BitLockerVolume(string Id, string Drive, BitLockerConversion? Conversion, uint? EncryptedPercent,
    bool Locked, uint? Protection, string? Error = null)
{
    internal BitLockerTarget Target => new(Id, Drive);
    internal bool CanDecrypt => Error == null && !Locked && Conversion is BitLockerConversion.FullyEncrypted or
        BitLockerConversion.Encrypting or BitLockerConversion.EncryptionPaused or BitLockerConversion.DecryptionPaused;
    public override string ToString() => Drive + " · " + State;
    internal string State => Error != null ? "Status unavailable" : Locked ? "Locked" : Conversion switch
    {
        BitLockerConversion.FullyDecrypted => "Fully decrypted · BitLocker off",
        BitLockerConversion.FullyEncrypted => "Fully encrypted",
        BitLockerConversion.Encrypting => "Encryption in progress",
        BitLockerConversion.Decrypting => "Decryption in progress",
        BitLockerConversion.EncryptionPaused => "Encryption paused",
        BitLockerConversion.DecryptionPaused => "Decryption paused",
        _ => "Unknown conversion status"
    };
}
internal interface IBitLockerBackend
{
    IReadOnlyList<BitLockerVolume> Read();
    BitLockerVolume Read(BitLockerTarget target);
    void Decrypt(BitLockerTarget target);
}

internal sealed class WindowsBitLockerBackend : IBitLockerBackend
{
    internal const string Namespace = @"root\CIMV2\Security\MicrosoftVolumeEncryption";
    internal static void Validate(BitLockerTarget target)
    {
        if (target.Drive.Length != 2 || target.Drive[0] is < 'A' or > 'Z' || target.Drive[1] != ':' ||
            target.Id.Length != 49 || !target.Id.StartsWith(@"\\?\Volume{", StringComparison.OrdinalIgnoreCase) ||
            !target.Id.EndsWith(@"}\", StringComparison.Ordinal) || !Guid.TryParseExact(target.Id.Substring(11, 36), "D", out _))
            throw new ArgumentException("Select a local BitLocker volume with a drive letter and a valid volume identity.");
    }
    static ManagementScope Scope()
    {
        if (!HardwareSampler.IsAdministrator)
            throw new UnauthorizedAccessException("Restart Prefect as administrator to read BitLocker drives. No encryption was changed.");
        var scope = new ManagementScope(@"\\.\" + Namespace, new ConnectionOptions
            { Authentication = AuthenticationLevel.PacketPrivacy, Impersonation = ImpersonationLevel.Impersonate, Timeout = TimeSpan.FromSeconds(20) });
        scope.Connect();
        return scope;
    }
    internal static string ErrorText(uint code) => code switch
    {
        0x80310000 => "The drive is locked. Unlock it in Windows before decrypting it.",
        0x80310029 => "Windows requires the operating-system drive's auto-unlock dependencies to be resolved in Manage BitLocker first. Prefect has not cleared any saved keys.",
        0x80070005 => "Windows denied access. Administrator permissions or an organization policy may prevent this operation.",
        _ => "Windows returned BitLocker error 0x" + code.ToString("X8", CultureInfo.InvariantCulture) + ". Review the drive in Manage BitLocker."
    };
    static uint Number(ManagementBaseObject result, string name) => result[name] is uint number ? number :
        throw new IOException("Windows returned a missing or invalid BitLocker field: " + name + ".");
    static ManagementBaseObject Call(ManagementObject volume, string method)
    {
        using var input = volume.GetMethodParameters(method);
        if (method == "GetConversionStatus") input["PrecisionFactor"] = 0u;
        var result = volume.InvokeMethod(method, input, new InvokeMethodOptions { Timeout = TimeSpan.FromSeconds(30) })
            ?? throw new IOException("Windows returned no BitLocker result for " + method + ".");
        uint code;
        try { code = Number(result, "ReturnValue"); }
        catch { result.Dispose(); throw; }
        if (code == 0) return result;
        result.Dispose();
        throw new IOException(ErrorText(code));
    }
    static BitLockerVolume ReadVolume(ManagementObject volume)
    {
        var id = Convert.ToString(volume["DeviceID"], CultureInfo.InvariantCulture) ?? "";
        var drive = (Convert.ToString(volume["DriveLetter"], CultureInfo.InvariantCulture) ?? "").ToUpperInvariant();
        try
        {
            Validate(new(id, drive));
            using var lockResult = Call(volume, "GetLockStatus");
            var locked = Number(lockResult, "LockStatus");
            if (locked > 1) throw new IOException("Windows returned an unknown BitLocker lock status.");
            if (locked == 1) return new(id, drive, null, null, true, null);
            using var conversion = Call(volume, "GetConversionStatus");
            using var protection = Call(volume, "GetProtectionStatus");
            var state = Number(conversion, "ConversionStatus");
            var percent = Number(conversion, "EncryptionPercentage");
            var protectedState = Number(protection, "ProtectionStatus");
            if (state > 5 || percent > 100 || protectedState > 2) throw new IOException("Windows returned an unsupported BitLocker status.");
            return new(id, drive, (BitLockerConversion)state, percent, false, protectedState);
        }
        catch (Exception ex) { return new(id, drive, null, null, false, null, ex.Message); }
    }
    static ManagementObjectSearcher Search() => new(Scope(), new ObjectQuery("SELECT * FROM Win32_EncryptableVolume"),
        new System.Management.EnumerationOptions { Timeout = TimeSpan.FromSeconds(30) });
    public IReadOnlyList<BitLockerVolume> Read()
    {
        using var search = Search();
        using var volumes = search.Get();
        var result = new List<BitLockerVolume>();
        foreach (ManagementObject volume in volumes)
            using (volume)
                if (!string.IsNullOrEmpty(Convert.ToString(volume["DriveLetter"], CultureInfo.InvariantCulture))) result.Add(ReadVolume(volume));
        return result.OrderBy(v => v.Drive, StringComparer.OrdinalIgnoreCase).ToArray();
    }
    static T WithVolume<T>(BitLockerTarget target, Func<ManagementObject, T> action)
    {
        Validate(target); // Identity is compared after a static query; no caller-supplied WMI query is executed.
        using var search = Search();
        using var volumes = search.Get();
        foreach (ManagementObject volume in volumes)
        {
            using (volume)
            {
                if (!string.Equals(Convert.ToString(volume["DeviceID"], CultureInfo.InvariantCulture), target.Id, StringComparison.OrdinalIgnoreCase)) continue;
                if (!string.Equals(Convert.ToString(volume["DriveLetter"], CultureInfo.InvariantCulture), target.Drive, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("The drive letter changed. Refresh and select the drive again before decrypting.");
                return action(volume);
            }
        }
        throw new IOException("The selected volume is no longer available. Refresh and select it again.");
    }
    public BitLockerVolume Read(BitLockerTarget target) => WithVolume(target, ReadVolume);
    public void Decrypt(BitLockerTarget target) => WithVolume(target, volume =>
    {
        BitLockerService.RequireDecryptable(ReadVolume(volume));
        using var result = Call(volume, "Decrypt");
        return true;
    });
}

internal sealed class BitLockerService(IBitLockerBackend backend)
{
    internal IReadOnlyList<BitLockerVolume> Read() => backend.Read();
    internal static void RequireDecryptable(BitLockerVolume volume)
    {
        if (volume.Error != null) throw new IOException(volume.Error);
        if (volume.Locked) throw new IOException("The selected drive is locked. Unlock it in Windows first.");
        if (volume.Conversion == BitLockerConversion.FullyDecrypted) throw new IOException("The selected drive is already fully decrypted.");
        if (volume.Conversion == BitLockerConversion.Decrypting) throw new IOException("Windows is already decrypting this drive. Refresh its status to check progress.");
        if (!volume.CanDecrypt) throw new IOException("The drive has an unknown or unsupported BitLocker status. No decryption was requested.");
    }
    internal BitLockerVolume Decrypt(BitLockerTarget target)
    {
        WindowsBitLockerBackend.Validate(target);
        var current = backend.Read(target);
        if (!string.Equals(current.Id, target.Id, StringComparison.OrdinalIgnoreCase) || current.Drive != target.Drive)
            throw new IOException("The drive identity changed. Refresh and select it again.");
        RequireDecryptable(current);
        backend.Decrypt(target);
        // Success starts a Windows background conversion; it does not mean the drive is decrypted yet.
        try { return backend.Read(target); }
        catch (Exception ex) { return current with { Conversion = null, EncryptedPercent = null,
            Error = "Windows accepted the decryption request, but its current status could not be read. Refresh to check progress. " + ex.Message }; }
    }
}
