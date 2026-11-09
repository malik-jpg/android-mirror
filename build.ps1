# Build Android Mirror
#   .\build.ps1             -> dist\app\Android Mirror.exe (+ scrcpy bawaan)
#   .\build.ps1 -Installer  -> juga dist\AndroidMirror-Setup-<versi>.exe (butuh Inno Setup 6)
param([switch]$Installer)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$version = (Select-String -Path "$root\src\AndroidMirror.cs" -Pattern 'AssemblyVersion\("([\d\.]+)"\)').Matches[0].Groups[1].Value
$version = ($version -split '\.')[0..2] -join '.'
$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"

& "$root\make-icon.ps1" | Out-Null

$app = "$root\dist\app"
New-Item -ItemType Directory -Force $app | Out-Null
$out = "$app\Android Mirror.exe"
$cscArgs = @('/nologo', '/target:winexe', '/optimize+', "/out:$out",
             '/r:System.Windows.Forms.dll', '/r:System.Drawing.dll', '/r:System.Core.dll',
             "/win32icon:$root\assets\app.ico", "/resource:$root\assets\logo.png,logo.png")
$cscArgs += (Get-ChildItem "$root\src" -Filter *.cs | ForEach-Object FullName)
& $csc @cscArgs
if ($LASTEXITCODE -ne 0) { throw "Compile gagal" }

# Salin scrcpy (Apache-2.0) ke dist\app\scrcpy supaya aplikasi berdiri sendiri
$scrcpy = Get-ChildItem "$env:LOCALAPPDATA\Microsoft\WinGet\Packages\Genymobile.scrcpy_*\scrcpy-win64-*" -Directory -ErrorAction SilentlyContinue |
          Sort-Object Name -Descending | Select-Object -First 1
if (-not $scrcpy) { throw "scrcpy tidak ditemukan. Jalankan: winget install Genymobile.scrcpy" }
$dst = "$app\scrcpy"
if (Test-Path $dst) { Remove-Item $dst -Recurse -Force }
New-Item -ItemType Directory $dst | Out-Null
Get-ChildItem $scrcpy.FullName -File | Where-Object { $_.Extension -notin '.bat', '.vbs' } | Copy-Item -Destination $dst
Copy-Item "$root\LICENSE" $app -ErrorAction SilentlyContinue
Write-Host "App    : $out ($version)"

if ($Installer) {
    $iscc = @("$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe", "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe") |
            Where-Object { Test-Path $_ } | Select-Object -First 1
    if (-not $iscc) { throw "Inno Setup 6 tidak ditemukan. Jalankan: winget install JRSoftware.InnoSetup" }
    & $iscc /Q "/DAppVersion=$version" "/DScrcpyVersion=$($scrcpy.Name -replace 'scrcpy-win64-v','')" "$root\installer\AndroidMirror.iss"
    if ($LASTEXITCODE -ne 0) { throw "Installer gagal" }
    Get-ChildItem "$root\dist\*.exe" | ForEach-Object { Write-Host ("Setup  : {0} ({1:N1} MB)" -f $_.FullName, ($_.Length / 1MB)) }
}
