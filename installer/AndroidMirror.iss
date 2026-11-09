; Inno Setup 6 script - Android Mirror
; Dibangun lewat: .\build.ps1 -Installer

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef ScrcpyVersion
  #define ScrcpyVersion "5.0.1"
#endif
#define AppName "Android Mirror"
#define AppExe "Android Mirror.exe"
#define Root ".."

[Setup]
AppId={{6F1E2C1A-7B4D-4E8A-9B2F-5A1D3C7E9F10}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=MalixNet
AppComments=Mirror, kontrol & transfer file HP Android dari PC (scrcpy {#ScrcpyVersion})
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
OutputDir={#Root}\dist
OutputBaseFilename=AndroidMirror-Setup-{#AppVersion}
SetupIconFile={#Root}\assets\app.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
LicenseFile={#Root}\LICENSE
WizardStyle=modern
Compression=lzma2/ultra64
SolidCompression=yes
CloseApplications=yes
VersionInfoVersion={#AppVersion}.0
VersionInfoProductName={#AppName}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
english.DesktopIcon=Buat shortcut di Desktop
english.FilesIcon=Buat shortcut "Android Mirror - File" (langsung buka transfer file)
english.Shortcuts=Shortcut:
english.LaunchNow=Jalankan Android Mirror sekarang

[Tasks]
Name: "desktopicon"; Description: "{cm:DesktopIcon}"; GroupDescription: "{cm:Shortcuts}"
Name: "filesicon"; Description: "{cm:FilesIcon}"; GroupDescription: "{cm:Shortcuts}"; Flags: unchecked

[Files]
Source: "{#Root}\dist\app\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#Root}\dist\app\scrcpy\*"; DestDir: "{app}\scrcpy"; Flags: ignoreversion recursesubdirs
Source: "{#Root}\LICENSE"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{group}\{#AppName} - File"; Filename: "{app}\{#AppExe}"; Parameters: "--files"
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon
Name: "{autodesktop}\{#AppName} - File"; Filename: "{app}\{#AppExe}"; Parameters: "--files"; Tasks: filesicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchNow}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{app}\scrcpy\adb.exe"; Parameters: "kill-server"; Flags: runhidden skipifdoesntexist; RunOnceId: "KillAdb"

[UninstallDelete]
Type: dirifempty; Name: "{app}\scrcpy"
Type: dirifempty; Name: "{app}"

[Code]
// Hentikan adb lama dari folder instalasi supaya file bisa ditimpa saat update
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Adb: String;
  Code: Integer;
begin
  Adb := ExpandConstant('{app}\scrcpy\adb.exe');
  if FileExists(Adb) then
    Exec(Adb, 'kill-server', '', SW_HIDE, ewWaitUntilTerminated, Code);
  Result := '';
end;
