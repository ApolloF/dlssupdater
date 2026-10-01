; Inno Setup script for the installable DLSS Updater. Build after `dotnet publish`:
;   iscc /DAppVersion=1.5.1 installer\DLSSUpdater.iss
; The portable build is the same single-file publish\DLSSUpdater.exe, released as is.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#define AppName "DLSS Updater"
#define AppExe "DLSSUpdater.exe"

[Setup]
AppId={{6F0B8E53-1C2D-4B7A-9E4F-3A5D2C8B1E07}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=ApolloF
AppPublisherURL=https://github.com/ApolloF/dlssupdater
AppSupportURL=https://github.com/ApolloF/dlssupdater/issues
AppUpdatesURL=https://github.com/ApolloF/dlssupdater/releases
; Per-user by default (no admin prompt); the dialog offers an all-users install.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
SetupIconFile=..\src\DLSSUpdater\Assets\icon.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
OutputDir=..\publish
OutputBaseFilename=DLSSUpdater-Setup-{#AppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "..\publish\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; Drop the Seaglass add-on link if it points at this copy. Settings and caches in %LocalAppData%\DLSSUpdater are kept.
Filename: "{app}\{#AppExe}"; Parameters: "--unregister-addon"; Flags: runhidden waituntilterminated; RunOnceId: "UnregisterAddon"
