#ifndef MyAppVersion
  #define MyAppVersion "0.1.0"
#endif

[Setup]
AppId=InvestmentStory.Project_InvestmentStory
AppName=InvestmentStory
AppVersion={#MyAppVersion}
DefaultDirName={localappdata}\Programs\InvestmentStory
DefaultGroupName=InvestmentStory
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
DisableProgramGroupPage=yes
UsePreviousAppDir=yes
OutputDir=..\artifacts\release
OutputBaseFilename=InvestmentStory-Setup-x64-{#MyAppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
UninstallDisplayIcon={app}\InvestmentStory.App.exe

[Languages]
Name: "japanese"; MessagesFile: "compiler:Languages\Japanese.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "..\artifacts\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; Flags: unchecked

[Icons]
Name: "{group}\InvestmentStory"; Filename: "{app}\InvestmentStory.App.exe"
Name: "{userdesktop}\InvestmentStory"; Filename: "{app}\InvestmentStory.App.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\InvestmentStory.App.exe"; Description: "Launch InvestmentStory"; Flags: nowait postinstall skipifsilent
