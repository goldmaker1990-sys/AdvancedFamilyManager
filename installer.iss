#define MyAppName "Advanced Family Manager"
#define MyAppVersion "1.0"
#define MyAppPublisher "Dr. BELABED Youssouf"
#define MyAppURL ""
#define MyAppExeName "AdvancedFamilyManager.dll"

[Setup]
AppId={{8F4E7A1B-3C2D-4B5A-9E8F-1A2B3C4D5E6F}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}

; Install to %LocalAppData%\Programs (Fixed path)
DefaultDirName={localappdata}\Programs\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes

; Requires Admin to write to C:\ProgramData
PrivilegesRequired=admin
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64

; Compression and Output
OutputDir=Output
OutputBaseFilename=AdvancedFamilyManager_Installer
Compression=lzma
SolidCompression=yes
WizardStyle=modern

[Files]
; 1. Revit 2019-2024 (net48) Files
Source: "F:\Projets\AdvancedFamilyManager\bin\Release\net48\*"; DestDir: "{app}\net48"; Flags: ignoreversion recursesubdirs createallsubdirs

; 2. Revit 2025-2030 (net8.0-windows) Files
Source: "F:\Projets\AdvancedFamilyManager\bin\Release\net8.0-windows\*"; DestDir: "{app}\net8.0-windows"; Flags: ignoreversion recursesubdirs createallsubdirs

[Code]
var
  Year: Integer;
  AddinDir: String;
  DllPath: String;
  AddinContent: String;
  AddinFileName: String;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  // This block runs immediately after all files are copied to the AppData folder
  if CurStep = ssPostInstall then
  begin
    // Loop through Revit versions 2019 to 2030
    for Year := 2019 to 2030 do
    begin
      // Define the Revit Addins path for the current year
      AddinDir := ExpandConstant('{commonappdata}\Autodesk\Revit\Addins\') + IntToStr(Year);
      
      // Create the folder if it doesn't exist
      if not DirExists(AddinDir) then
        ForceDirectories(AddinDir);

      // Determine which DLL to use based on the year
      if Year <= 2024 then
      begin
        DllPath := ExpandConstant('{app}\net48\AdvancedFamilyManager.dll');
        AddinFileName := 'AdvancedFamilyManager2024.addin';
      end
      else
      begin
        DllPath := ExpandConstant('{app}\net8.0-windows\AdvancedFamilyManager.dll');
        AddinFileName := 'AdvancedFamilyManager2025+.addin';
      end;

      // Build the .addin XML content
      AddinContent := 
        '<?xml version="1.0" encoding="utf-8"?>' + #13#10 +
        '<RevitAddIns>' + #13#10 +
        '  <AddIn Type="Application">' + #13#10 +
        '    <Name>Advanced Family Manager</Name>' + #13#10 +
        '    <Assembly>' + DllPath + '</Assembly>' + #13#10 +
        '    <FullClassName>AdvancedFamilyManager.App</FullClassName>' + #13#10 +
        '    <ClientId>8F4E7A1B-3C2D-4B5A-9E8F-1A2B3C4D5E6F</ClientId>' + #13#10 +
        '    <VendorId>BELABED</VendorId>' + #13#10 +
        '    <VendorDescription>Advanced Family Manager Plugin</VendorDescription>' + #13#10 +
        '  </AddIn>' + #13#10 +
        '</RevitAddIns>';

      // Save the .addin file to the Revit folder
      SaveStringToFile(AddinDir + '\' + AddinFileName, AddinContent, False);
    end;
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  // This block runs when the user uninstalls the plugin, cleaning up the .addin files
  if CurUninstallStep = usPostUninstall then
  begin
    for Year := 2019 to 2030 do
    begin
      AddinDir := ExpandConstant('{commonappdata}\Autodesk\Revit\Addins\') + IntToStr(Year);
      
      if Year <= 2024 then
        AddinFileName := 'AdvancedFamilyManager2024.addin'
      else
        AddinFileName := 'AdvancedFamilyManager2025+.addin';
        
      // Delete the .addin file if it exists
      if FileExists(AddinDir + '\' + AddinFileName) then
        DeleteFile(AddinDir + '\' + AddinFileName);
    end;
  end;
end;