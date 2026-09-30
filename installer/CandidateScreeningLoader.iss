; Instalador por usuario, sin permisos de administrador.
; Se compila con ISCC contra un publicado que ya exista; ver installer/README.md.

#define Publicado SourcePath + "..\src\ScreeningLoader.Shell\bin\Release\net10.0-windows\win-x64\publish\ScreeningLoader.Shell.exe"

#if !FileExists(Publicado)
  #error Falta el ejecutable publicado. Correr antes: pnpm -C ui build && dotnet publish ...
#endif

#define Nombre "Candidate Screening Loader"
#define Version GetVersionNumbersString(Publicado)
#define Editor "Serenity Star"

[Setup]
; No cambiar nunca: es lo que identifica a la aplicación instalada entre versiones.
AppId={{8A6F2C41-3D5E-4B7A-9E10-6C2B4F8D1A93}
AppName={#Nombre}
AppVersion={#Version}
AppVerName={#Nombre} {#Version}
AppPublisher={#Editor}
VersionInfoVersion={#Version}

; Por usuario: en %LOCALAPPDATA% no hace falta administrador ni que IT intervenga.
PrivilegesRequired=lowest
DefaultDirName={autopf}\CandidateScreeningLoader
DefaultGroupName={#Nombre}
DisableProgramGroupPage=yes
DisableDirPage=yes

; El ejecutable se lleva el runtime de .NET adentro, así que el comprimido pesa igual que él.
Compression=lzma2/max
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

OutputDir=Output
OutputBaseFilename=CandidateScreeningLoaderSetup
SetupIconFile=..\src\ScreeningLoader.Shell\app.ico
WizardStyle=modern
UninstallDisplayName={#Nombre}
UninstallDisplayIcon={app}\ScreeningLoader.Shell.exe

[Languages]
Name: "es"; MessagesFile: "compiler:Languages\Spanish.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "{#Publicado}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#Nombre}"; Filename: "{app}\ScreeningLoader.Shell.exe"
Name: "{autodesktop}\{#Nombre}"; Filename: "{app}\ScreeningLoader.Shell.exe"; Tasks: desktopicon

[Run]
Filename: "{tmp}\MicrosoftEdgeWebview2Setup.exe"; Parameters: "/silent /install"; \
  StatusMsg: "Instalando el runtime de WebView2…"; Check: FaltaWebView2; Flags: waituntilterminated
Filename: "{app}\ScreeningLoader.Shell.exe"; Description: "{cm:LaunchProgram,{#Nombre}}"; \
  Flags: nowait postinstall skipifsilent

[Code]
var
  Descarga: TDownloadWizardPage;

{ El runtime evergreen se registra bajo WOW6432Node en las máquinas de 64 bits, y bajo HKCU
  cuando alguien lo instaló sólo para su usuario. }
function Registrado(Raiz: Integer; Clave: String): Boolean;
var
  Version: String;
begin
  Result := RegQueryStringValue(Raiz, Clave, 'pv', Version) and (Version <> '') and (Version <> '0.0.0.0');
end;

function FaltaWebView2: Boolean;
var
  Cliente: String;
begin
  Cliente := 'Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}';

  Result := not (
    Registrado(HKLM, 'SOFTWARE\WOW6432Node\' + Cliente)
    or Registrado(HKLM, 'SOFTWARE\' + Cliente)
    or Registrado(HKCU, 'SOFTWARE\' + Cliente));
end;

procedure InitializeWizard;
begin
  Descarga := CreateDownloadPage(SetupMessage(msgWizardPreparing), SetupMessage(msgPreparingDesc), nil);
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;

  if (CurPageID <> wpReady) or not FaltaWebView2 then
    Exit;

  { Viene preinstalado en Windows 11 y en Windows 10 con Edge moderno; esto es la red de seguridad,
    y necesita salida a internet en el momento de instalar. }
  Descarga.Clear;
  Descarga.Add('https://go.microsoft.com/fwlink/p/?LinkId=2124703', 'MicrosoftEdgeWebview2Setup.exe', '');
  Descarga.Show;

  try
    try
      Descarga.Download;
    except
      SuppressibleMsgBox(
        'No se ha podido descargar el runtime de WebView2, que la aplicación necesita para abrirse.' + #13#10#13#10
        + AddPeriod(GetExceptionMessage),
        mbCriticalError,
        MB_OK,
        IDOK);
      Result := False;
    end;
  finally
    Descarga.Hide;
  end;
end;
