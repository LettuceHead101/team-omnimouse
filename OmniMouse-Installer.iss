; OmniMouse Installer Script
; Requires Inno Setup 6.0 or later

#define MyAppName "OmniMouse"
#define MyAppVersion "1.0"
#define MyAppPublisher "OmniMouse Team"
#define MyAppExeName "OmniMouse.exe"
#define MyAppURL "https://github.com/LettuceHead101/team-omnimouse"

[Setup]
; NOTE: The value of AppId uniquely identifies this application.
; Do not use the same AppId value in installers for other applications.
AppId={{8A7B9C3D-4E5F-6A7B-8C9D-0E1F2A3B4C5D}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
LicenseFile=LICENSE.txt
OutputDir=installer_output
OutputBaseFilename=OmniMouse-Setup-v{#MyAppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
UninstallDisplayIcon={app}\{#MyAppExeName}
SetupIconFile=OmniMouse\Resources\logo.ico
DisableProgramGroupPage=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "OmniMouse\bin\Release\net8.0-windows\win-x64\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
; NOTE: Don't use "Flags: ignoreversion" on any shared system files

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent shellexec

[Code]
const
  DotNetRuntimeURL = 'https://dotnet.microsoft.com/download/dotnet/8.0';

function IsDotNet8Installed(): Boolean;
var
  ResultCode: Integer;
  TempFile: String;
  FileContent: AnsiString;
begin
  Result := False;
  
  // Create a temporary file to capture the output
  TempFile := ExpandConstant('{tmp}\dotnet_version.txt');
  
  // Run dotnet --list-runtimes and save output to temp file
  if Exec('cmd.exe', '/C dotnet --list-runtimes > "' + TempFile + '" 2>&1', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
  begin
    // Check if the command executed successfully (dotnet is installed)
    if ResultCode = 0 then
    begin
      // Load the file content and check for Desktop Runtime 8.0
      if LoadStringFromFile(TempFile, FileContent) then
      begin
        Result := (Pos('Microsoft.WindowsDesktop.App 8.', FileContent) > 0);
      end;
    end;
  end;
  
  // Clean up temp file
  if FileExists(TempFile) then
    DeleteFile(TempFile);
end;

function InitializeSetup(): Boolean;
var
  ErrorCode: Integer;
  Response: Integer;
begin
  Result := True;
  
  // Check if .NET 8 Desktop Runtime is installed
  if not IsDotNet8Installed() then
  begin
    Response := MsgBox(
      '.NET Desktop Runtime 8.0 is required to run OmniMouse but was not detected on your system.' + #13#10 + #13#10 +
      'Would you like to download and install it now?' + #13#10 + #13#10 +
      'Click YES to open the download page in your browser.' + #13#10 +
      'Click NO to cancel the installation.',
      mbConfirmation,
      MB_YESNO
    );
    
    if Response = IDYES then
    begin
      // Open the .NET download page
      ShellExec('open', DotNetRuntimeURL, '', '', SW_SHOWNORMAL, ewNoWait, ErrorCode);
      
      MsgBox(
        'Please download and install the ".NET Desktop Runtime 8.0 (x64)" from the opened webpage.' + #13#10 + #13#10 +
        'After installation, run this installer again.',
        mbInformation,
        MB_OK
      );
    end;
    
    // Abort installation
    Result := False;
  end;
end;