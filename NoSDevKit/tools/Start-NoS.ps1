param(
    [int]$Count = 1,
    [int]$Width = 1600,
    [int]$Height = 900,
    [string]$AddonDir = "$PSScriptRoot\..\..\MoreRolesInPolus",
    [string]$AddonZipName = "[Toa]MoreRolesInPolus.zip",
    # Close the games started from $env:AmongUs first. Games from other folders (e.g. a normal NoS install) are never touched
    [switch]$Restart
)

# Pack the addon into Addons\, then start $Count windowed games (with -Restart, close the dev games first).
# When starting more than one, wait for the first game to compile the addon (Cache\Dll) before starting the rest,
# otherwise they compile at the same time and fail ("Compile Error! Scripts is ignored").
# Windows are tiled 2x2 per screen (DISPLAY1, then DISPLAY4).

if (-not $env:AmongUs) { throw "Set the AmongUs environment variable to the game folder" }
$gameDir = $env:AmongUs

# Only the games started from this game folder. Matching by process name alone would also close a normal NoS install
function Get-DevGames {
    $exe = (Join-Path $gameDir "Among Us.exe").ToLowerInvariant()
    Get-Process -Name "Among Us" -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path.ToLowerInvariant() -eq $exe }
}

if ($Restart) {
    Get-DevGames | Stop-Process -Force
    while (Get-DevGames) { Start-Sleep -Milliseconds 300 }
}
elseif (Get-DevGames) {
    "A dev game is running. It keeps the old addon until it is restarted (use -Restart to close it)."
}

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
Add-Type -AssemblyName System.Windows.Forms

$src = (Resolve-Path $AddonDir).Path
$dest = Join-Path $gameDir "Addons\$AddonZipName"
if (Test-Path -LiteralPath $dest) { Remove-Item -LiteralPath $dest }
$zipStream = [System.IO.File]::Open($dest, [System.IO.FileMode]::Create)
$zip = New-Object System.IO.Compression.ZipArchive($zipStream, [System.IO.Compression.ZipArchiveMode]::Create, $false)
Get-ChildItem -LiteralPath $src -Recurse -File | ForEach-Object {
    $relPath = $_.FullName.Substring($src.Length + 1).Replace('\', '/')
    $entry = $zip.CreateEntry($relPath, [System.IO.Compression.CompressionLevel]::Optimal)
    $es = $entry.Open(); $fs = [System.IO.File]::OpenRead($_.FullName)
    $fs.CopyTo($es); $fs.Dispose(); $es.Dispose()
}
$zip.Dispose(); $zipStream.Dispose()
"packed $dest"

Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class NoSWindowPos {
  [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int w, int hh, uint flags);
}
"@

$screens = [System.Windows.Forms.Screen]::AllScreens
$targets = @($screens | Where-Object { $_.DeviceName -eq '\\.\DISPLAY1' }) + @($screens | Where-Object { $_.DeviceName -eq '\\.\DISPLAY4' })
# When tiling and no size was given, use a quarter of the screen so that 2x2 windows fit
if ($Count -gt 1 -and $targets.Count -gt 0) {
    if (-not $PSBoundParameters.ContainsKey('Width')) { $Width = [int]($targets[0].Bounds.Width / 2) }
    if (-not $PSBoundParameters.ContainsKey('Height')) { $Height = [int]($targets[0].Bounds.Height / 2) }
}
$cells = foreach ($s in $targets) { foreach ($row in 0, 1) { foreach ($col in 0, 1) { ,@(($s.Bounds.X + $col * $Width), ($s.Bounds.Y + $row * $Height)) } } }

$launchArgs = "-screen-fullscreen 0 -screen-width $Width -screen-height $Height"
$procs = @()
for ($i = 0; $i -lt $Count; $i++) {
    $procs += Start-Process (Join-Path $gameDir "Among Us.exe") -ArgumentList $launchArgs -PassThru
    if ($i -eq 0 -and $Count -gt 1) {
        $log = Join-Path $gameDir "DevKit.log"
        $startedAt = Get-Date
        $limit = $startedAt.AddSeconds(180)
        do {
            Start-Sleep -Seconds 1
            $ready = (Test-Path $log) -and ((Get-Item $log).LastWriteTime -gt $startedAt) -and (Select-String -Path $log -Pattern 'Compile (Finished|Error)' -Quiet)
        } while (-not $ready -and (Get-Date) -lt $limit)
        "first game compiled: $ready"
    }
    elseif ($i -gt 0) { Start-Sleep -Milliseconds 800 }
}

# Tile only when several games run; a single window keeps its default position
if ($Count -gt 1) {
    $placed = @{}
    $deadline = (Get-Date).AddSeconds(120)
    while ($placed.Count -lt $procs.Count -and (Get-Date) -lt $deadline) {
        for ($i = 0; $i -lt $procs.Count; $i++) {
            if ($placed.ContainsKey($i)) { continue }
            $p = $procs[$i]; $p.Refresh()
            if ($p.MainWindowHandle -ne 0 -and $i -lt $cells.Count) {
                [NoSWindowPos]::SetWindowPos($p.MainWindowHandle, [IntPtr]::Zero, $cells[$i][0], $cells[$i][1], $Width, $Height, 0x0004) | Out-Null
                $placed[$i] = $true
            }
        }
        Start-Sleep -Milliseconds 500
    }
}
"started $($procs.Count)"
