; Inno Setup script for Uproot. Build it through build.ps1, which publishes the app first and passes the version in.
; Modelled on Sysoptimizer's installer. Installs to Program Files\ProtagonistLabs\Uproot, the studio's common folder.

#define AppName "Uproot"
#define AppExe "Uproot.exe"
#define AppPublisher "Protagonist Labs"

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif

[Setup]
; Never change AppId: it is what ties an update to the installation it replaces.
AppId={{5B7C1E92-4D3A-4F86-9C21-8E0A7D6B3F14}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL=https://protagonistlabs.app/uproot/
AppCopyright=Copyright (c) 2026 Protagonist Labs
VersionInfoVersion={#AppVersion}

; The app requires admin at every launch (uninstallers, Program Files, HKLM), so installing per-machine costs nothing extra.
PrivilegesRequired=admin
DefaultDirName={autopf}\ProtagonistLabs\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
DisableDirPage=auto
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}

ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

CloseApplications=force
RestartApplications=no

OutputDir=..\dist
OutputBaseFilename=Uproot-{#AppVersion}-setup
SetupIconFile=..\Uproot\app.ico
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked

[Files]
Source: "..\dist\app\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
; Started with the installer's own (admin) rights: a postinstall entry otherwise runs as the signed-in user, and Windows
; refuses an exe that requires admin from there (CreateProcess error 740).
Filename: "{app}\{#AppExe}"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent runascurrentuser

[UninstallDelete]
; Its card in the shared folder, so the sister apps stop offering it. Its log and the registry backups stay.
Type: files; Name: "{userappdata}\ProtagonistLabs\apps\uproot.json"

[Code]
procedure StopUproot();
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM {#AppExe}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Sleep(300);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  StopUproot();
  Result := '';
end;

function InitializeUninstall(): Boolean;
begin
  StopUproot();
  Result := True;
end;
