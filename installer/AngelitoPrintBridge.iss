; Instalador de Angelito Print Bridge (Inno Setup 6). Compilar con installer/publish.ps1.
; Por defecto instala por usuario sin privilegios de administrador. /ALLUSERS instala para todo el equipo.
; Instalación silenciosa empresarial:  AngelitoPrintBridge-Setup.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /TASKS="autostart"

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\artifacts\win-x64\app"
#endif
#ifndef Arch
  #define Arch "x64compatible"
#endif
#ifndef Runtime
  #define Runtime "win-x64"
#endif

[Setup]
AppId={{8C5B2E0A-3F4D-4C3B-9E7A-41A9D2B7E730}
AppName=Angelito Print Bridge
AppVersion={#AppVersion}
AppVerName=Angelito Print Bridge {#AppVersion}
AppPublisher=Angelito Systems S.A.C.
AppPublisherURL=https://angelitosystems.net
AppSupportURL=https://angelitosystems.net
AppCopyright=Copyright © 2026 Angelito Systems S.A.C. — Apache-2.0
DefaultDirName={autopf}\Angelito Print Bridge
DefaultGroupName=Angelito Print Bridge
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog commandline
ArchitecturesAllowed={#Arch}
ArchitecturesInstallIn64BitMode={#Arch}
MinVersion=10.0.19041
OutputDir=..\artifacts
OutputBaseFilename=AngelitoPrintBridge-{#AppVersion}-{#Runtime}-Setup
SetupIconFile=..\src\Angelito.PrintBridge.Desktop\Assets\icon.ico
UninstallDisplayIcon={app}\AngelitoPrintBridge.Desktop.exe
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
SetupLogging=yes
LicenseFile=..\LICENSE
CloseApplications=no
VersionInfoVersion={#AppVersion}
VersionInfoCompany=Angelito Systems S.A.C.

[Languages]
Name: "es"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "en"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "autostart"; Description: "Iniciar Angelito Print Bridge al iniciar sesión en Windows"; GroupDescription: "Opciones:"
Name: "desktopicon"; Description: "Crear acceso directo en el escritorio"; GroupDescription: "Opciones:"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Angelito Print Bridge"; Filename: "{app}\AngelitoPrintBridge.Desktop.exe"
Name: "{autodesktop}\Angelito Print Bridge"; Filename: "{app}\AngelitoPrintBridge.Desktop.exe"; Tasks: desktopicon

[Registry]
; HKA = HKCU en instalación por usuario, HKLM en instalación para todo el equipo.
Root: HKA; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "AngelitoPrintBridge"; \
  ValueData: """{app}\AngelitoPrintBridge.Desktop.exe"" --minimized"; Flags: uninsdeletevalue; Tasks: autostart

[Run]
Filename: "{app}\AngelitoPrintBridge.Desktop.exe"; Description: "Abrir Angelito Print Bridge"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{sys}\taskkill.exe"; Parameters: "/F /IM AngelitoPrintBridge.Desktop.exe"; Flags: runhidden waituntilterminated; RunOnceId: "StopDesktop"
Filename: "{app}\AngelitoPrintBridge.exe"; Parameters: "shutdown"; Flags: runhidden waituntilterminated; RunOnceId: "StopAgent"

[Code]
function ProcessRunning(ExeName: String): Boolean;
var
  Code: Integer;
begin
  // find.exe returns 0 when tasklist lists the image name.
  Result := Exec(ExpandConstant('{cmd}'), '/C tasklist /NH /FI "IMAGENAME eq ' + ExeName + '" | find /I "' + ExeName + '"',
    '', SW_HIDE, ewWaitUntilTerminated, Code) and (Code = 0);
end;

procedure WaitForExit(ExeName: String);
var
  I: Integer;
begin
  for I := 1 to 20 do
  begin
    if not ProcessRunning(ExeName) then exit;
    Sleep(500);
  end;
end;

// Antes de actualizar hay que liberar los archivos de {app}. Orden: primero la bandeja (si no, su vigilante reinicia el
// agente) y luego el agente, solo si no hay trabajos en proceso (código 4 = trabajos activos).
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Code: Integer;
  Agent: String;
begin
  Result := '';
  // /F: sin él, la bandeja interpreta el cierre como "ocultar" y sigue bloqueando sus DLL. No tiene trabajos propios.
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM AngelitoPrintBridge.Desktop.exe', '', SW_HIDE, ewWaitUntilTerminated, Code);
  WaitForExit('AngelitoPrintBridge.Desktop.exe');
  Agent := ExpandConstant('{app}\AngelitoPrintBridge.exe');
  if FileExists(Agent) and ProcessRunning('AngelitoPrintBridge.exe') then
  begin
    Exec(Agent, 'shutdown --if-idle', '', SW_HIDE, ewWaitUntilTerminated, Code);
    if Code = 4 then
    begin
      Result := 'Angelito Print Bridge está imprimiendo. Espere a que terminen los trabajos y vuelva a intentarlo.';
      exit;
    end;
    WaitForExit('AngelitoPrintBridge.exe');
    if ProcessRunning('AngelitoPrintBridge.exe') then
      Result := 'No se pudo detener el agente de Angelito Print Bridge (puede pertenecer a otro usuario de Windows). Ciérrelo y vuelva a intentarlo.';
  end;
end;

// Los datos (autorizaciones, auditoría, registros) se conservan al desinstalar salvo que el usuario elija borrarlos.
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataDir: String;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    DataDir := ExpandConstant('{localappdata}\AngelitoSystems\PrintBridge');
    if DirExists(DataDir) and (not UninstallSilent) then
      if MsgBox('¿Eliminar también las autorizaciones, el historial y los registros de Angelito Print Bridge?', mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
        DelTree(DataDir, True, True, True);
  end;
end;
