; Inno Setup script for AcmeDeck
; Build with setup\build.ps1, which publishes the app and then calls ISCC.

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif

#define AppId "{5B3A7C61-2E4F-4C8B-9D17-AC0E3D5F8B21}"

#ifexist "win-acme\wacs.exe"
  #define WithWinAcme
  #define WacsRaw GetStringFileInfo("win-acme\wacs.exe", "ProductVersion")
  #define WacsVersion Pos("+", WacsRaw) > 0 ? Copy(WacsRaw, 1, Pos("+", WacsRaw) - 1) : WacsRaw
#endif

[Setup]
AppId={{#AppId}
AppName=AcmeDeck
AppVersion={#AppVersion}
AppPublisher=AcmeDeck contributors
AppComments=Graphical front end for win-acme to manage Let's Encrypt certificates in IIS
DefaultDirName={autopf}\AcmeDeck
DefaultGroupName=AcmeDeck
DisableProgramGroupPage=yes
LicenseFile=..\LICENSE
OutputDir=output
OutputBaseFilename=AcmeDeck-Setup-{#AppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\AcmeDeck.exe
SetupIconFile=..\src\AcmeDeck\Assets\AcmeDeck.ico
MinVersion=10.0

#ifdef WithWinAcme
[Types]
Name: "full"; Description: "AcmeDeck with win-acme {#WacsVersion}"
Name: "apponly"; Description: "AcmeDeck only (win-acme is already installed)"
Name: "custom"; Description: "Custom"; Flags: iscustom

[Components]
Name: "app"; Description: "AcmeDeck {#AppVersion}"; Types: full apponly custom; Flags: fixed
Name: "winacme"; Description: "win-acme {#WacsVersion} (Apache 2.0), installed into the AcmeDeck folder. Stays on uninstall, because the renewal task depends on it."; Types: full
#endif

[Files]
Source: "publish\AcmeDeck.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion

#ifdef WithWinAcme
; Never removed on uninstall: the scheduled task points to this wacs.exe, and
; removing it would silently stop every renewal. win-acme creates settings.json
; from settings_default.json on first start; an existing settings.json (with the
; notification settings) is never overwritten.
Source: "win-acme\*"; DestDir: "{app}\win-acme"; Excludes: "settings.json"; Components: winacme; Flags: ignoreversion recursesubdirs createallsubdirs uninsneveruninstall
#endif

[Icons]
Name: "{autoprograms}\AcmeDeck"; Filename: "{app}\AcmeDeck.exe"
Name: "{autodesktop}\AcmeDeck"; Filename: "{app}\AcmeDeck.exe"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; Flags: unchecked

[Run]
; shellexec respects the requireAdministrator manifest of the application
Filename: "{app}\AcmeDeck.exe"; Description: "Start AcmeDeck"; Flags: nowait postinstall skipifsilent shellexec

[UninstallDelete]
; Settings of AcmeDeck itself. win-acme's configuration in ProgramData stays untouched.
Type: filesandordirs; Name: "{commonappdata}\AcmeDeck"

#ifdef WithWinAcme
[Code]
function IsUpgrade(): Boolean;
begin
  Result := RegKeyExists(HKLM, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{#AppId}_is1');
end;

function ExistingWinAcme(): Boolean;
begin
  Result := FileExists(ExpandConstant('{commonpf64}\win-acme\wacs.exe'));
end;

procedure InitializeWizard();
begin
  // A server that already runs win-acme from the usual folder should keep using it:
  // a second copy would not be the one the scheduled task starts.
  // On upgrades the previous selection is kept.
  if (not IsUpgrade()) and ExistingWinAcme() then
    WizardSelectComponents('!winacme');
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if (CurPageID = wpSelectComponents) and (not IsUpgrade()) and ExistingWinAcme() then
    WizardForm.ComponentsDiskSpaceLabel.Caption :=
      'win-acme is already installed in ' + ExpandConstant('{commonpf64}\win-acme') +
      ' and will be used by AcmeDeck. Select the component only if you want a separate copy.';
end;
#endif

// Debug aid: ISCC /DDumpPreprocessed /O- AcmeDeck.iss writes the resolved script
#ifdef DumpPreprocessed
  #expr SaveToFile(AddBackslash(SourcePath) + "AcmeDeck.preprocessed.iss")
#endif
