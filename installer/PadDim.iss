#ifndef AppVersion
  #define AppVersion "0.1.8"
#endif
#ifndef PublishDir
  #define PublishDir "..\artifacts\publish\win-x64"
#endif

[Setup]
AppId={{B8E28E5C-44C1-4B90-8EF2-705F65EAF225}
AppName=PadDim
AppVersion={#AppVersion}
DefaultDirName={localappdata}\Programs\PadDim
DefaultGroupName=PadDim
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
OutputDir=..\artifacts\installer
OutputBaseFilename=PadDim-Setup-{#AppVersion}-win-x64
SetupIconFile=..\assets\PadDim.ico
UninstallDisplayIcon={app}\PadDim.exe
WizardStyle=modern
Compression=lzma2
SolidCompression=yes
AppMutex=Local\PadDim.Application
CloseApplications=no
RestartApplications=no
DisableProgramGroupPage=yes
UsePreviousTasks=yes
LicenseFile=..\LICENSE.txt

[Languages]
Name: "japanese"; MessagesFile: "compiler:Languages\Japanese.isl"

[Tasks]
Name: "desktopicon"; Description: "デスクトップにショートカットを作成する"; Flags: unchecked
Name: "startup"; Description: "Windowsへのサインイン時に自動起動する"; Flags: unchecked

[InstallDelete]
Type: files; Name: "{userstartup}\PadDim.lnk"; Tasks: not startup

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\PadDim"; Filename: "{app}\PadDim.exe"; WorkingDir: "{app}"
Name: "{userdesktop}\PadDim"; Filename: "{app}\PadDim.exe"; WorkingDir: "{app}"; Tasks: desktopicon
Name: "{userstartup}\PadDim"; Filename: "{app}\PadDim.exe"; Parameters: "--tray"; WorkingDir: "{app}"; Tasks: startup

[Run]
Filename: "{app}\PadDim.exe"; Description: "PadDim を起動する"; Flags: nowait postinstall skipifsilent
