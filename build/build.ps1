param(
    [switch]$SkipSign
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root "orhex.exe"

$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"

& $csc /nologo /target:winexe /optimize+ /platform:anycpu `
    "/win32icon:$(Join-Path $PSScriptRoot 'exe_icon.ico')" `
    "/win32manifest:$(Join-Path $PSScriptRoot 'app.manifest')" `
    "/resource:$(Join-Path $PSScriptRoot 'icon.ico'),Orhex.icon.ico" `
    "/out:$out" `
    /reference:System.Windows.Forms.dll `
    /reference:System.Drawing.dll `
    /reference:System.Core.dll `
    (Join-Path $root "Orhex.cs") `
    (Join-Path $root "GameResolve.cs") `
    (Join-Path $root "DiscordRpc.cs") `
    (Join-Path $PSScriptRoot "AssemblyInfo.cs")
if (-not $?) { throw "Compile failed" }

if (-not $SkipSign) {
    & (Join-Path $PSScriptRoot "sign.ps1")
}

Write-Host "Built: $out"
