#define MyAppName "BAYAT Smart Network Optimizer"
#define MyAppVersion "1.1.0"
#define MyAppPublisher "BAYAT"
#define MyAppExeName "SmartNetworkOptimizer.exe"

[Setup]
AppId={{A84D6E93-DBA8-4B20-98FA-01C88D24C8F1}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\Programs\SmartNetworkOptimizer
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=..\artifacts\installer
OutputBaseFilename=SmartNetworkOptimizer-Setup-{#MyAppVersion}-win-x64
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
UninstallDisplayIcon={app}\{#MyAppExeName}

[Tasks]
Name: "desktopicon"; Description: "Masaüstüne kısayol oluştur"; GroupDescription: "Ek kısayollar:"; Flags: unchecked

[Files]
Source: "..\artifacts\win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{#MyAppName} uygulamasını başlat"; Flags: postinstall nowait skipifsilent

[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ExistingCommand: string;
  InstalledExe: string;
begin
  if CurUninstallStep = usUninstall then
  begin
    InstalledExe := '"' + ExpandConstant('{app}\SmartNetworkOptimizer.exe') + '"';
    if RegQueryStringValue(HKEY_CURRENT_USER,
      'Software\Microsoft\Windows\CurrentVersion\Run',
      'SmartNetworkOptimizer', ExistingCommand) then
    begin
      if CompareText(Trim(ExistingCommand), InstalledExe) = 0 then
        RegDeleteValue(HKEY_CURRENT_USER,
          'Software\Microsoft\Windows\CurrentVersion\Run',
          'SmartNetworkOptimizer');
    end;
  end;
end;
