param([string]$BuildDirectory = 'bin')
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$exe=Join-Path (Join-Path $root $BuildDirectory) 'TaskbarMonitorStartup.exe'
if (-not ('StartupBuildResources' -as [type])) {
    Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class StartupBuildResources {
    [DllImport("shell32.dll", CharSet=CharSet.Unicode)] public static extern uint ExtractIconEx(string path, int index, IntPtr[] large, IntPtr[] small, uint count);
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr LoadLibraryEx(string path, IntPtr file, uint flags);
    [DllImport("kernel32.dll")] public static extern IntPtr FindResource(IntPtr module, IntPtr name, IntPtr type);
    [DllImport("kernel32.dll")] public static extern uint SizeofResource(IntPtr module, IntPtr resource);
    [DllImport("kernel32.dll")] public static extern IntPtr LoadResource(IntPtr module, IntPtr resource);
    [DllImport("kernel32.dll")] public static extern IntPtr LockResource(IntPtr resource);
    [DllImport("kernel32.dll")] public static extern bool FreeLibrary(IntPtr module);
}
'@
}
if ([StartupBuildResources]::ExtractIconEx($exe,-1,$null,$null,0) -lt 1) { throw 'Launcher has no native icon; Task Manager would display a generic icon.' }
$module=[StartupBuildResources]::LoadLibraryEx($exe,[IntPtr]::Zero,2)
if ($module -eq [IntPtr]::Zero) { throw 'Cannot read launcher resources.' }
try {
    $resource=[StartupBuildResources]::FindResource($module,[IntPtr]1,[IntPtr]24)
    if ($resource -eq [IntPtr]::Zero) { throw 'Missing launcher manifest.' }
    $size=[StartupBuildResources]::SizeofResource($module,$resource)
    $pointer=[StartupBuildResources]::LockResource([StartupBuildResources]::LoadResource($module,$resource))
    $bytes=New-Object byte[] $size
    [Runtime.InteropServices.Marshal]::Copy($pointer,$bytes,0,$bytes.Length)
    $manifest=[Text.Encoding]::UTF8.GetString($bytes)
    if ($manifest -notmatch 'level="asInvoker"') { throw 'Login launcher must not request UAC elevation.' }
} finally { [StartupBuildResources]::FreeLibrary($module) | Out-Null }
if ((Get-Item $exe).VersionInfo.FileDescription -ne 'WinTaskbarMonitor') { throw 'Unexpected Startup apps display name.' }
'PASS: launcher native icon, display name and asInvoker manifest'
