#ifndef StageDir
  #define StageDir "..\build-release"
#endif
#ifndef OutputDirPath
  #define OutputDirPath "..\dist"
#endif
[Setup]
AppId={{57F571DC-767B-4A3A-BCCE-073197E4B8F5}
AppName=WinTaskbarMonitor
AppVersion=1.0.2
AppPublisher=ZoriChen
AppPublisherURL=https://github.com/zorichen/WinTaskbarMonitor
AppSupportURL=https://github.com/zorichen/WinTaskbarMonitor/issues
AppUpdatesURL=https://github.com/zorichen/WinTaskbarMonitor/releases
DefaultDirName={autopf}\WinTaskbarMonitor
DefaultGroupName=WinTaskbarMonitor
UsePreviousGroup=no
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.22000
WizardStyle=modern
SetupIconFile=..\assets\taskbar-monitor.ico
UninstallDisplayIcon={app}\TaskbarMonitor.exe
OutputDir={#OutputDirPath}
OutputBaseFilename=WinTaskbarMonitor-1.0.2-Setup-x64
Compression=lzma2
SolidCompression=yes
LicenseFile=..\LICENSE
CloseApplications=yes
RestartApplications=no
UninstallDisplayName=WinTaskbarMonitor

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "chinesesimp"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut / 创建桌面快捷方式"; Flags: unchecked
Name: "startup"; Description: "Start after Windows sign-in (Task Manager startup entry) / 登录后自动启动（可在任务管理器中开关）"; Flags: unchecked
Name: "pawnio"; Description: "Install signed PawnIO 2.2.0 driver for CPU temperature / 安装官方签名温度驱动"; Flags: unchecked; Check: NeedsPawnIO

[Files]
Source: "{#StageDir}\*"; DestDir: "{app}"; Excludes: "startup-registration.json"; Flags: ignoreversion recursesubdirs createallsubdirs

[InstallDelete]
Type: files; Name: "{autoprograms}\TaskbarMonitor\TaskbarMonitor.lnk"
Type: files; Name: "{autoprograms}\TaskbarMonitor\Uninstall TaskbarMonitor.lnk"
Type: dirifempty; Name: "{autoprograms}\TaskbarMonitor"
Type: files; Name: "{autodesktop}\TaskbarMonitor.lnk"

[Icons]
Name: "{group}\WinTaskbarMonitor"; Filename: "{app}\TaskbarMonitor.exe"
Name: "{autodesktop}\WinTaskbarMonitor"; Filename: "{app}\TaskbarMonitor.exe"; Tasks: desktopicon
Name: "{group}\Uninstall WinTaskbarMonitor"; Filename: "{uninstallexe}"

[Run]
Filename: "{app}\TaskbarMonitor.exe"; Description: "Launch WinTaskbarMonitor / 启动任务栏监控"; Flags: postinstall nowait skipifsilent

[UninstallRun]
Filename: "{app}\TaskbarMonitor.exe"; Parameters: "--quit"; Flags: runhidden waituntilterminated; RunOnceId: "StopTaskbarMonitor"

[Code]
var
  DriverNeedsRestart: Boolean;

procedure ConfigureStartup(Enable: Boolean);
var
  Parameters: String;
  ResultCode: Integer;
begin
  if Enable then Parameters := '--install-startup'
  else Parameters := '--remove-startup';
  if not Exec(ExpandConstant('{app}\TaskbarMonitorStartup.exe'), Parameters, '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    RaiseException('Could not run the startup configuration tool. See %LOCALAPPDATA%\TaskbarMonitor\startup-launcher.log.')
  else if ResultCode <> 0 then
    RaiseException('Could not configure startup. See %LOCALAPPDATA%\TaskbarMonitor\startup-launcher.log.');
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  { Wait until uninstall is confirmed; cancelling the dialog must preserve startup. }
  if (CurUninstallStep = usUninstall) and FileExists(ExpandConstant('{app}\TaskbarMonitorStartup.exe')) then
    ConfigureStartup(False);
end;

function NeedsPawnIO: Boolean;
begin
  Result := not RegKeyExists(HKLM64, 'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO');
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
begin
  if CurStep = ssPostInstall then
    ConfigureStartup(WizardIsTaskSelected('startup'));
  if (CurStep = ssPostInstall) and WizardIsTaskSelected('pawnio') and NeedsPawnIO then
  begin
    if not Exec(ExpandConstant('{app}\drivers\PawnIO_setup.exe'), '-install -silent', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
      MsgBox('Could not start PawnIO installer. WinTaskbarMonitor is installed; temperature may be unavailable.', mbError, MB_OK)
    else if ResultCode = 3010 then
      DriverNeedsRestart := True
    else if ResultCode <> 0 then
      MsgBox('PawnIO installation failed (code ' + IntToStr(ResultCode) + '). CPU temperature may be unavailable.', mbError, MB_OK);
  end;
end;

function NeedRestart: Boolean;
begin
  Result := DriverNeedsRestart;
end;
