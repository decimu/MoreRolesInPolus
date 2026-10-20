<#
.SYNOPSIS
  NoSDevKit がまとめた DevKit.log を色つきで表示し続ける。
.PARAMETER MinLevel
  表示する最低レベル（DBG / INF / MSG / WRN / ERR）。既定は INF。
.PARAMETER Index
  複数起動したときに何番目のゲームのログを見るか（既定は 1）。
.PARAMETER Tail
  開始時に表示する直近の行数。
#>
param(
    [ValidateSet('DBG', 'INF', 'MSG', 'WRN', 'ERR')]
    [string]$MinLevel = 'INF',
    [int]$Tail = 40,
    [int]$Index = 1
)

if (-not $env:AmongUs) { Write-Host '環境変数 AmongUs が設定されていません' -ForegroundColor Red; exit 1 }
$path = Join-Path $env:AmongUs $(if ($Index -le 1) { 'DevKit.log' } else { "DevKit.$Index.log" })

$order = @{ DBG = 0; INF = 1; MSG = 2; WRN = 3; ERR = 4 }
$colors = @{ DBG = 'DarkGray'; INF = 'Gray'; MSG = 'Cyan'; WRN = 'Yellow'; ERR = 'Red' }
$min = $order[$MinLevel]
$currentLevel = 'INF'

function Write-LogLine([string]$line) {
    # 字下げされた行は直前のログの続き
    if ($line -match '^\d\d:\d\d:\d\d\.\d{3} (DBG|INF|MSG|WRN|ERR) ') { $script:currentLevel = $Matches[1] }
    if ($order[$script:currentLevel] -lt $min) { return }
    Write-Host $line -ForegroundColor $colors[$script:currentLevel]
}

Write-Host "DevKit.log を表示します: $path （Ctrl+C で終了）" -ForegroundColor DarkCyan
while (-not (Test-Path -LiteralPath $path)) { Start-Sleep -Milliseconds 500 }

$position = 0L
$first = $true
$encoding = New-Object System.Text.UTF8Encoding($false)

while ($true) {
    try {
        $stream = [System.IO.File]::Open($path, 'Open', 'Read', 'ReadWrite, Delete')
        try {
            if ($stream.Length -lt $position) {
                Write-Host '──────── ゲームが再起動しました ────────' -ForegroundColor DarkCyan
                $position = 0L
            }
            if ($stream.Length -gt $position) {
                $stream.Seek($position, 'Begin') | Out-Null
                $reader = New-Object System.IO.StreamReader($stream, $encoding, $false, 65536, $true)
                $text = $reader.ReadToEnd()
                # 書き込み途中の最終行は次回に回す
                $end = $text.LastIndexOf("`n")
                if ($end -ge 0) {
                    $lines = $text.Substring(0, $end).Split("`n") | ForEach-Object { $_.TrimEnd("`r") }
                    if ($first -and $lines.Count -gt $Tail) { $lines = $lines[($lines.Count - $Tail)..($lines.Count - 1)] }
                    foreach ($l in $lines) { Write-LogLine $l }
                    $position += $encoding.GetByteCount($text.Substring(0, $end + 1))
                }
            }
            $first = $false
        }
        finally { $stream.Dispose() }
    }
    catch [System.IO.FileNotFoundException] { }
    catch [System.IO.IOException] { }
    Start-Sleep -Milliseconds 250
}
