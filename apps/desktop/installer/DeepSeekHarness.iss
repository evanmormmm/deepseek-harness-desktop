#ifndef MyAppVersion
  #define MyAppVersion "0.1.0"
#endif
#ifndef SourceDirectory
  #error SourceDirectory must identify the verified desktop distribution
#endif
#ifndef OutputDirectory
  #error OutputDirectory must identify the release asset directory
#endif
#ifndef PayloadZip
  #error PayloadZip must identify the verified desktop payload
#endif

#define MyAppName "DeepSeek Harness Desktop"
#define MyAppExeName "DeepSeek Harness.exe"
#define MyRepositoryUrl "https://github.com/evanmormmm/deepseek-harness-desktop"

[Setup]
AppId={{88B2967E-80C7-46DF-B3AB-8DD1B34B95FD}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
VersionInfoVersion={#MyAppVersion}
VersionInfoDescription={#MyAppName} installer
AppPublisher=DeepSeek Harness contributors
AppPublisherURL={#MyRepositoryUrl}
AppSupportURL={#MyRepositoryUrl}/issues
AppUpdatesURL={#MyRepositoryUrl}/releases/latest
DefaultDirName={localappdata}\Programs\DeepSeek Harness
DefaultGroupName=DeepSeek Harness
DisableProgramGroupPage=yes
LicenseFile={#SourceDirectory}\LICENSE.txt
OutputDir={#OutputDirectory}
OutputBaseFilename=DeepSeek-Harness-Desktop-{#MyAppVersion}-win-x64-Setup
SetupIconFile=..\assets\app.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
PrivilegesRequired=lowest
Compression=lzma2/ultra64
SolidCompression=no
WizardStyle=modern dynamic
CloseApplications=force
CloseApplicationsFilter={#MyAppExeName}
RestartApplications=no
SetupLogging=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "chinesesimp"; MessagesFile: "languages\ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PayloadZip}"; DestName: "desktop-payload.zip"; Flags: dontcopy nocompression

[Icons]
Name: "{autoprograms}\DeepSeek Harness"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{%USERPROFILE}"
Name: "{autodesktop}\DeepSeek Harness"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{%USERPROFILE}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; WorkingDir: "{%USERPROFILE}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{app}\{#MyAppExeName}"; Parameters: "--cleanup-runtime"; Flags: runhidden waituntilterminated skipifdoesntexist; RunOnceId: "CleanupDesktopRuntime"

[UninstallDelete]
Type: files; Name: "{app}\{#MyAppExeName}"
Type: files; Name: "{app}\desktop-manifest.json"
Type: files; Name: "{app}\LICENSE.txt"
Type: files; Name: "{app}\Microsoft.Web.WebView2.Core.xml"
Type: files; Name: "{app}\Microsoft.Web.WebView2.WinForms.xml"
Type: files; Name: "{app}\NODE_LICENSE.txt"
Type: files; Name: "{app}\README.md"
Type: files; Name: "{app}\README.zh.md"
Type: files; Name: "{app}\THIRD_PARTY_NOTICES.md"
Type: files; Name: "{app}\WEBVIEW2_LICENSE.txt"
Type: files; Name: "{app}\WEBVIEW2_NOTICE.txt"
Type: files; Name: "{app}\WebView2Loader.dll"
Type: dirifempty; Name: "{app}"

[Code]
procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
  PayloadPath: String;
  AppPath: String;
begin
  if CurStep <> ssInstall then
    Exit;

  ExtractTemporaryFile('desktop-payload.zip');
  PayloadPath := ExpandConstant('{tmp}\desktop-payload.zip');
  AppPath := ExpandConstant('{app}');
  ForceDirectories(AppPath);
  if not Exec(
    ExpandConstant('{sys}\tar.exe'),
    '-xf "' + PayloadPath + '" -C "' + AppPath + '" --strip-components 1',
    '',
    SW_HIDE,
    ewWaitUntilTerminated,
    ResultCode) then
  begin
    RaiseException('Windows tar.exe could not start.');
  end;
  if ResultCode <> 0 then
    RaiseException(Format('Desktop payload extraction failed with exit code %d.', [ResultCode]));
end;
