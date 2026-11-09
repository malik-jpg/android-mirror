# Publikasikan Android Mirror ke GitHub: buat repo publik, push, dan buat Release berisi installer.
#   powershell -ExecutionPolicy Bypass -File .\publish.ps1
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path -Parent $MyInvocation.MyCommand.Path)
$gh = 'C:\Program Files\GitHub CLI\gh.exe'
$version = '1.0.0'
$setup = "dist\AndroidMirror-Setup-$version.exe"
if (-not (Test-Path $setup)) { & .\build.ps1 -Installer }

& $gh auth setup-git
& $gh repo create android-mirror --public `
    --description "Mirror, kontrol & transfer file HP Android dari PC Windows (USB / Wi-Fi). Installer 10 MB, gratis." `
    --source . --remote origin --push
if ($LASTEXITCODE -ne 0) { throw "Gagal membuat / push repo" }

$notes = @"
## Android Mirror v$version

Tampilkan, kontrol, dan transfer file HP Android dari PC Windows lewat USB atau Wi-Fi.

**Download:** ``AndroidMirror-Setup-$version.exe`` di bawah (Windows 10/11 64-bit, ±10 MB, tanpa hak admin).

- Mirror real-time sampai 120 fps, kontrol mouse & keyboard, audio ke PC (Android 11+)
- USB atau Wi-Fi (1 klik dari USB)
- Transfer file dua arah: jelajahi HP, ambil ke PC, kirim ke HP (drag & drop), buat folder, ganti nama, hapus
- Layar HP mati saat mirror, selalu di atas, layar penuh, drag & drop APK

Cara pakai lengkap ada di README. Kalau muncul "Windows protected your PC", klik **More info → Run anyway**.

Dibangun di atas [scrcpy](https://github.com/Genymobile/scrcpy) (Apache-2.0), sudah termasuk di installer.
"@
& $gh release create "v$version" $setup --title "Android Mirror v$version" --notes $notes
if ($LASTEXITCODE -ne 0) { throw "Gagal membuat release" }

& $gh repo edit --add-topic android --add-topic scrcpy --add-topic screen-mirroring --add-topic windows --add-topic file-transfer
Write-Host ""
Write-Host "SELESAI:"
& $gh repo view --json url --jq .url
Write-Host "Download langsung: https://github.com/malik-jpg/android-mirror/releases/latest"
Read-Host "Tekan Enter untuk menutup"
