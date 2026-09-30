# Instalador

`CandidateScreeningLoaderSetup.exe`, construido con [Inno Setup 6](https://jrsoftware.org/isinfo.php).

## Construirlo

Tres pasos, en orden. El instalador empaqueta un publicado que ya tiene que existir: no lo construye él.

```powershell
pnpm -C ui build
dotnet publish src/ScreeningLoader.Shell -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
& "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe" installer\CandidateScreeningLoader.iss
```

Sale en `installer/Output/`, que no se versiona. Si falta el publicado, el compilador aborta con un mensaje
que lo dice en vez de generar un instalador vacío.

La última línea es de PowerShell. En `cmd`, donde `&` y `$env:` no existen:

```bat
"%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe" installer\CandidateScreeningLoader.iss
```

Inno Setup se instala con `winget install JRSoftware.InnoSetup`. Queda en `%LOCALAPPDATA%\Programs` cuando se
instala por usuario y en `Archivos de programa (x86)` cuando se instala para toda la máquina; los comandos de
arriba asumen lo primero.

**La versión sale del ejecutable**, que la toma de `<Version>` en `ScreeningLoader.Shell.csproj`. Se sube ahí
y no acá.

## Qué hace

- Instala **por usuario**, en `%LOCALAPPDATA%\Programs\CandidateScreeningLoader`. Sin permisos de
  administrador y sin que IT intervenga.
- Deja el acceso directo en el menú Inicio y, si se acepta la casilla, en el escritorio.
- Registra la entrada de desinstalación en Programas y características.
- **Verifica el runtime de WebView2 y lo descarga si falta.** Viene preinstalado en Windows 11 y en Windows 10
  con Edge moderno, así que esa rama casi nunca se dispara; cuando lo hace necesita salida a internet.

## Qué no hace

- **No está firmado.** Si el instalador llega por correo, Teams o una descarga, Windows le pone la Mark of the
  Web y SmartScreen muestra *"Windows protegió su PC"*. Por un share de red interno o un pendrive no pasa.
- **No borra los datos locales al desinstalar.** Los registros de auditoría y las preferencias viven en
  `%LOCALAPPDATA%\CandidateScreeningLoader` y se quedan ahí: son de quien usa la aplicación, no de la
  aplicación.
- **Sólo x64.** Una máquina ARM64 lo correría emulado.
