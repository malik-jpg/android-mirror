# Render video promo jadi MP4 di PC ini.
#   Klik kanan -> Run with PowerShell, atau:  powershell -ExecutionPolicy Bypass -File .\render.ps1
#   Butuh: Node.js, ffmpeg, dan Chrome/Edge (sudah ada di Windows).  Hasil: out\android-mirror-promo.mp4
param([int]$Fps = 60)
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location "$here\tools"

if (-not (Get-Command node -ErrorAction SilentlyContinue))   { throw "Node.js belum ada. Instal: winget install OpenJS.NodeJS.LTS" }
if (-not (Get-Command ffmpeg -ErrorAction SilentlyContinue)) { throw "ffmpeg belum ada. Instal: winget install Gyan.FFmpeg" }
if (-not (Test-Path "node_modules\puppeteer-core")) {
    Write-Host "Menyiapkan puppeteer-core (sekali saja)..."
    npm install --no-audit --no-fund --silent
}
$env:FPS = "$Fps"
node render.mjs
Write-Host ""
Write-Host "Video ada di: $here\out\android-mirror-promo.mp4"
Read-Host "Tekan Enter untuk menutup"
