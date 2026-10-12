[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ClientPath,
    [string]$IconPath = "$PSScriptRoot/../assets/source/branding/forgeline/application/forgeline.ico"
)
$ErrorActionPreference = 'Stop'
if (-not $IsWindows) { throw 'Windows PE icon qualification requires Windows.' }
if (-not ('ApplicationIconResources' -as [type])) {
    Add-Type -TypeDefinition @"
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
public static class ApplicationIconResources
{
    private delegate bool EnumerateName(IntPtr module, IntPtr type, IntPtr name, IntPtr parameter);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibraryEx(string path, IntPtr file, uint flags);
    [DllImport("kernel32.dll")]
    private static extern bool FreeLibrary(IntPtr module);
    [DllImport("kernel32.dll", EntryPoint = "EnumResourceNamesW", SetLastError = true)]
    private static extern bool EnumResourceNames(IntPtr module, IntPtr type, EnumerateName callback, IntPtr parameter);
    [DllImport("kernel32.dll", EntryPoint = "FindResourceW", SetLastError = true)]
    private static extern IntPtr FindResource(IntPtr module, IntPtr name, IntPtr type);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint SizeofResource(IntPtr module, IntPtr resource);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LoadResource(IntPtr module, IntPtr resource);
    [DllImport("kernel32.dll")]
    private static extern IntPtr LockResource(IntPtr resource);
    private static byte[] Read(IntPtr module, IntPtr name, int type)
    {
        IntPtr resource = FindResource(module, name, (IntPtr)type);
        if (resource == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
        uint size = SizeofResource(module, resource);
        IntPtr pointer = LockResource(LoadResource(module, resource));
        if (size == 0 || pointer == IntPtr.Zero) throw new InvalidOperationException("Empty icon resource.");
        byte[] bytes = new byte[checked((int)size)];
        Marshal.Copy(pointer, bytes, 0, bytes.Length);
        return bytes;
    }
    public static byte[][] ReadFirstIconGroup(string path)
    {
        IntPtr module = LoadLibraryEx(path, IntPtr.Zero, 0x22); // Data/image resources only; never execute the binary.
        if (module == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            byte[] group = null;
            Exception failure = null;
            EnumerateName callback = (m, t, n, p) => {
                try { group = Read(m, n, 14); } catch (Exception error) { failure = error; }
                return false;
            };
            EnumResourceNames(module, (IntPtr)14, callback, IntPtr.Zero);
            GC.KeepAlive(callback);
            if (failure != null) throw failure;
            if (group == null || group.Length < 6) throw new InvalidOperationException("Application icon group missing.");
            int count = BitConverter.ToUInt16(group, 4);
            if (count == 0 || group.Length != 6 + count * 14) throw new InvalidOperationException("Invalid icon group.");
            var result = new List<byte[]> { group };
            for (int i = 0; i < count; i++)
                result.Add(Read(module, (IntPtr)BitConverter.ToUInt16(group, 6 + i * 14 + 12), 3));
            return result.ToArray();
        }
        finally { FreeLibrary(module); }
    }
}
"@
}
$client = (Resolve-Path -LiteralPath $ClientPath).Path
$source = [IO.File]::ReadAllBytes((Resolve-Path -LiteralPath $IconPath).Path)
if ($source.Length -lt 6 -or [BitConverter]::ToUInt16($source, 0) -ne 0 -or
    [BitConverter]::ToUInt16($source, 2) -ne 1) { throw 'Invalid canonical ICO header.' }
$count = [BitConverter]::ToUInt16($source, 4)
if ($count -eq 0 -or $source.Length -lt 6 + 16 * $count) { throw 'Invalid canonical ICO directory.' }
$resources = [ApplicationIconResources]::ReadFirstIconGroup($client)
$group = $resources[0]
if ([BitConverter]::ToUInt16($group, 0) -ne 0 -or [BitConverter]::ToUInt16($group, 2) -ne 1 -or
    [BitConverter]::ToUInt16($group, 4) -ne $count) { throw 'Published icon group differs from the canonical ICO.' }
$sizes = @()
for ($i = 0; $i -lt $count; $i++) {
    $entry = 6 + 16 * $i
    $resourceEntry = 6 + 14 * $i
    $length = [BitConverter]::ToUInt32($source, $entry + 8)
    $offset = [BitConverter]::ToUInt32($source, $entry + 12)
    if ($length -eq 0 -or [uint64]$offset + $length -gt $source.Length -or $offset -lt 6 + 16 * $count) {
        throw 'Canonical icon payload is truncated or overlaps its directory.'
    }
    if ($source[$entry] -ne $group[$resourceEntry] -or $source[$entry + 1] -ne $group[$resourceEntry + 1] -or
        [BitConverter]::ToUInt32($group, $resourceEntry + 8) -ne $length) { throw 'Published icon frame metadata differs.' }
    $payload = [byte[]]::new($length)
    [Array]::Copy($source, $offset, $payload, 0, $length)
    $expected = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($payload))
    $actual = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([byte[]]$resources[$i + 1]))
    if ($expected -ne $actual) { throw 'Published icon pixels differ from the canonical artwork.' }
    $sizes += if ($source[$entry] -eq 0) { 256 } else { [int]$source[$entry] }
}
Write-Output "Application icon verified in $([IO.Path]::GetFileName($client)): $($sizes -join ', ') px; all payload hashes match."