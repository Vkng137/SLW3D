#ifndef MyAppVersion
  #define MyAppVersion "1.0.0"
#endif

[Setup]
AppId={{A7E4C2D1-9F3B-4B8E-8C1A-5E6D7F8A9B0C}
AppName=Streamlined Desktop Overlay for Windows (SLW3D)
AppVersion={#MyAppVersion}
AppVerName=SLW3D {#MyAppVersion}
AppPublisher=Vkng137
DefaultDirName={localappdata}\Programs\SLW3D
DefaultGroupName=SLW3D
AllowNoIcons=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
SetupIconFile=..\assets\desktop3d.ico
UninstallDisplayIcon={app}\bin\controller\Desktop3D.Controller.exe
UninstallDisplayName=Streamlined Desktop Overlay for Windows (SLW3D)
OutputDir=..\dist
OutputBaseFilename=SLW3D-Setup-{#MyAppVersion}-win-x64
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "..\dist\stage\PROJECT.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\dist\stage\bin\controller\*"; DestDir: "{app}\bin\controller"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\dist\stage\bin\shell\*"; DestDir: "{app}\bin\shell"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\dist\stage\shell\web\*"; DestDir: "{app}\shell\web"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\dist\stage\assets\desktop3d.ico"; DestDir: "{app}\assets"; Flags: ignoreversion
Source: "..\dist\stage\assets\desktop3d.png"; DestDir: "{app}\assets"; Flags: ignoreversion
Source: "..\dist\stage\assets\backgrounds\.gitkeep"; DestDir: "{app}\assets\backgrounds"; Flags: ignoreversion
Source: "..\dist\stage\config\controller.example.json"; DestDir: "{app}\config"; Flags: ignoreversion
Source: "..\dist\stage\config\apps.json"; DestDir: "{app}\config"; Flags: onlyifdoesntexist
Source: "..\dist\stage\config\shell.json"; DestDir: "{app}\config"; Flags: onlyifdoesntexist
Source: "..\dist\stage\config\themes.json"; DestDir: "{app}\config"; Flags: onlyifdoesntexist

[Icons]
Name: "{group}\SLW3D"; Filename: "{app}\bin\controller\Desktop3D.Controller.exe"; WorkingDir: "{app}\bin\controller"; IconFilename: "{app}\assets\desktop3d.ico"
Name: "{group}\Uninstall SLW3D"; Filename: "{uninstallexe}"
Name: "{autodesktop}\SLW3D"; Filename: "{app}\bin\controller\Desktop3D.Controller.exe"; WorkingDir: "{app}\bin\controller"; IconFilename: "{app}\assets\desktop3d.ico"; Tasks: desktopicon

[Run]
Filename: "{app}\bin\controller\Desktop3D.Controller.exe"; WorkingDir: "{app}\bin\controller"; Description: "{cm:LaunchProgram,SLW3D}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "schtasks.exe"; Parameters: "/Delete /TN ""Windows 3D Desktop"" /F"; Flags: runhidden; RunOnceId: "DelAutostartTask"

[Code]
const
  EVENT_MODIFY_STATE = $0002;

function OpenEventW(dwDesiredAccess: DWORD; bInheritHandle: BOOL; lpName: string): THandle;
  external 'OpenEventW@kernel32.dll stdcall';
function SetEvent(hEvent: THandle): BOOL;
  external 'SetEvent@kernel32.dll stdcall';
function CloseHandle(hObject: THandle): BOOL;
  external 'CloseHandle@kernel32.dll stdcall';

procedure StopRunningDesktop3D();
var
  hEvt: THandle;
  ResultCode: Integer;
begin
  hEvt := OpenEventW(EVENT_MODIFY_STATE, False, 'Desktop3D.Controller.Quit');
  if hEvt <> 0 then
  begin
    SetEvent(hEvt);
    CloseHandle(hEvt);
    Sleep(1200);
  end;
  Exec('taskkill.exe', '/F /IM Desktop3D.Controller.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec('taskkill.exe', '/F /IM Desktop3D.Shell.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  StopRunningDesktop3D();
  Result := '';
end;

function InitializeUninstall(): Boolean;
begin
  StopRunningDesktop3D();
  Result := True;
end;
