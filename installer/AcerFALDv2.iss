; ---------------------------------------------------------------------------
;  AcerFALDv2 - Windows installer
;
;  Installs the self-contained portable build to C:\AcerFALDv2 by default and
;  offers three optional entries, all chosen inside the wizard:
;
;    * Desktop shortcut
;    * Start Menu shortcut
;    * Start automatically at sign-in (Startup folder)
;
;  Upgrade behaviour: AcerFALDv2.exe and README.txt are always replaced,
;  faldsync.json is only written when it does not exist yet, so brightness,
;  input and theme settings survive a reinstall.
;
;  Build with installer\build-installer.ps1 (preferred) or directly with
;  ISCC.exe /DMyAppVersion=2.2.0 installer\AcerFALDv2.iss
; ---------------------------------------------------------------------------

#ifndef MyAppVersion
  #define MyAppVersion "2.2.0"
#endif

#define MyAppName "AcerFALDv2"
#define MyAppPublisher "rocky29b4u"
#define MyAppURL "https://github.com/rocky29b4u/AcerFALDGui"
#define MyAppExeName "AcerFALDv2.exe"
#define MyPayloadDir "..\AcerFALDv2"

[Setup]
; A stable, never-changing identifier: it is the key Add/Remove Programs and
; upgrades use to recognise an existing installation.
AppId={{E109B4D6-6B77-459B-9BAA-5DE19BEC50C9}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}/issues
AppUpdatesURL={#MyAppURL}/releases
AppComments=HDR and local dimming control for the Acer Nitro XV275K P5

; The app writes faldsync.json and faldsync.log next to the exe, so it lives
; outside Program Files where that is allowed without a virtualisation layer.
; DefaultDirName is only a default: the directory page is shown and the user
; may pick another folder (a drive root is rejected in [Code] below).
DefaultDirName=C:\AcerFALDv2
DefaultGroupName={#MyAppName}
DisableDirPage=no
DisableProgramGroupPage=yes
AllowNoIcons=yes

; Writing to C:\ and adding a machine-wide uninstall entry needs elevation.
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763

; AcerFALDv2 holds Local\AcerFALDv2.SingleInstance while it runs. Setup and
; the uninstaller notice this and offer to close the app before touching files.
AppMutex=Local\AcerFALDv2.SingleInstance
CloseApplications=yes
RestartApplications=no

OutputDir=..\dist
OutputBaseFilename=AcerFALDv2-Setup-{#MyAppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
SetupLogging=yes
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription={#MyAppName} Setup
VersionInfoProductName={#MyAppName}
VersionInfoVersion={#MyAppVersion}

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"
Name: "startmenuicon"; Description: "Create a &Start Menu shortcut"; GroupDescription: "Additional shortcuts:"
Name: "startupicon"; Description: "Start {#MyAppName} automatically when I &sign in"; GroupDescription: "Startup:"

[InstallDelete]
; Shortcuts are not removed automatically when a task is unticked during an
; upgrade, so drop the ones the previous install created.
Type: files; Name: "{userdesktop}\{#MyAppName}.lnk"; Check: not WizardIsTaskSelected('desktopicon')
Type: files; Name: "{userstartup}\{#MyAppName}.lnk"; Check: not WizardIsTaskSelected('startupicon')
Type: files; Name: "{group}\{#MyAppName}.lnk"; Check: not WizardIsTaskSelected('startmenuicon')
Type: files; Name: "{group}\{cm:UninstallProgram,{#MyAppName}}.lnk"; Check: not WizardIsTaskSelected('startmenuicon')
Type: dirifempty; Name: "{group}"; Check: not WizardIsTaskSelected('startmenuicon')

[Files]
Source: "{#MyPayloadDir}\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion restartreplace
Source: "{#MyPayloadDir}\README.txt"; DestDir: "{app}"; Flags: ignoreversion
; Never overwrite saved settings, and never let the uninstaller delete them
; silently. Uninstall asks first (see [Code]).
Source: "{#MyPayloadDir}\faldsync.json"; DestDir: "{app}"; Flags: onlyifdoesntexist uninsneveruninstall

[Dirs]
; The app stores faldsync.json and faldsync.log in its own folder, so a
; standard user must be able to write there. Normally C:\ already grants this
; through inheritance; the explicit entry keeps it working if C:\ is hardened.
Name: "{app}"; Permissions: users-modify

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Comment: "HDR and local dimming control"; Tasks: startmenuicon
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"; Tasks: startmenuicon
Name: "{userdesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Comment: "HDR and local dimming control"; Tasks: desktopicon
Name: "{userstartup}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: startupicon

[Registry]
; Lets Win+R -> AcerFALDv2 work.
Root: HKLM; Subkey: "SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\{#MyAppExeName}"; ValueType: string; ValueName: ""; ValueData: "{app}\{#MyAppExeName}"; Flags: uninsdeletekey

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; WorkingDir: "{app}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: files; Name: "{app}\faldsync.log"
Type: dirifempty; Name: "{app}"

[Code]
function IsDriveRoot(const Dir: String): Boolean;
var
  Trimmed: String;
begin
  Trimmed := Dir;
  while (Length(Trimmed) > 0)
        and ((Trimmed[Length(Trimmed)] = '\') or (Trimmed[Length(Trimmed)] = '/')) do
    SetLength(Trimmed, Length(Trimmed) - 1);

  Result := (Length(Trimmed) = 2) and (Trimmed[2] = ':');
end;

function ValidateInstallDir(const Dir: String): String;
begin
  Result := '';

  if IsDriveRoot(Dir) then
    Result := 'AcerFALDv2 cannot be installed directly into a drive root such as C:\.'
              + #13#10#13#10
              + 'Choose a folder inside the drive instead, for example C:\AcerFALDv2.'
  else if CompareText(RemoveBackslashUnlessRoot(Dir),
                      RemoveBackslashUnlessRoot(ExpandConstant('{win}'))) = 0 then
    Result := 'Please choose a folder other than the Windows folder.'
              + #13#10#13#10
              + 'For example C:\AcerFALDv2.';
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  ErrorText: String;
begin
  Result := True;

  if CurPageID <> wpSelectDir then
    Exit;

  ErrorText := ValidateInstallDir(WizardDirValue);
  if ErrorText <> '' then
  begin
    MsgBox(ErrorText, mbError, MB_OK);
    Result := False;
  end;
end;

// Also guards silent installs, where the directory page is never shown
// (for example setup.exe /VERYSILENT /DIR=C:\).
function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := ValidateInstallDir(ExpandConstant('{app}'));
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ConfigFile: String;
begin
  if CurUninstallStep <> usPostUninstall then
    Exit;

  ConfigFile := ExpandConstant('{app}\faldsync.json');

  // Only ask while the user is watching. A message box raised by a silent
  // uninstall (/SILENT or /VERYSILENT) leaves the uninstaller's second phase
  // waiting for an answer that never arrives, so the process never exits.
  // Silent uninstalls keep the settings, which is the safe default.
  if (not UninstallSilent) and FileExists(ConfigFile) then
    if MsgBox('Also delete your saved settings (faldsync.json)?'
              + #13#10#13#10
              + 'Choose No to keep brightness, input and theme settings so a later '
              + 'reinstall starts where you left off.',
              mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
      DeleteFile(ConfigFile);

  RemoveDir(ExpandConstant('{app}'));
end;
