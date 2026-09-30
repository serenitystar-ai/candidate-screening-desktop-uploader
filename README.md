# Candidate Screening Loader

Batch-processes the CVs of a job opening against the AI Hub's candidate screening agent, without the micro app's
limit of five files per run.

## Requirements

- **.NET 10 SDK** for the engine and the window, in `src/`.
- **Node 20+ and pnpm** for the interface, in `ui/`.
- **WebView2 Runtime** — preinstalled on Windows 11.
- **Inno Setup 6**, only to build the installer: `winget install JRSoftware.InnoSetup`.

This is a polyglot repo: both toolchains are needed to build all of it.

## Build

There are three chained steps, and each one consumes what the previous one left behind:

```
pnpm -C ui build   →  ui/dist/                          the compiled interface
dotnet publish     →  ScreeningLoader.Shell.exe         embeds ui/dist inside
ISCC.exe           →  CandidateScreeningLoaderSetup.exe packages that executable
```

None can be skipped: `publish` fails if `ui/dist` is missing, and the installer compiler aborts if the
executable is missing.

Run the commands one per line, without splitting them. The ones below are **PowerShell**; the only line that
changes in `cmd` is the installer compiler, because `&` and `$env:` don't exist there:

```bat
"%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe" installer\CandidateScreeningLoader.iss
```

### The executable

```powershell
pnpm -C ui build
dotnet publish src/ScreeningLoader.Shell -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
```

It ends up in `src\ScreeningLoader.Shell\bin\Release\net10.0-windows\win-x64\publish\`: a single file of about
100 MB that carries the .NET runtime inside and doesn't depend on the machine having it. Adding
`-o "$env:USERPROFILE\Desktop"` drops it straight onto the desktop.

### The installer

The same two steps plus one more:

```powershell
pnpm -C ui build
dotnet publish src/ScreeningLoader.Shell -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
& "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe" installer\CandidateScreeningLoader.iss
```

The output is `installer\Output\CandidateScreeningLoaderSetup.exe`, about 33 MB. It installs per user, without
administrator rights. Details are in [`installer/`](installer/README.md).

## Development

In development it's the other way around: `pnpm -C ui dev` starts the dev server and the window points to it,
with hot reload of the interface while the engine runs in the debugger. The window loads the dev server when it
finds the `SCREENINGLOADER_DEV_SERVER` variable; without it, it serves the bundle it carries embedded.

```powershell
pnpm -C ui dev                                       # in one terminal
$env:SCREENINGLOADER_DEV_SERVER = "http://localhost:5173"
dotnet run --project src/ScreeningLoader.Shell       # in another
```

The engine can also be driven without a window, from `ScreeningLoader.Console`:

```powershell
dotnet run --project src/ScreeningLoader.Console                      # login and dataset skill
dotnet run --project src/ScreeningLoader.Console -- --probe-refresh   # also renews the token
dotnet run --project src/ScreeningLoader.Console -- --history         # recorded runs, no session
```

The debugger configuration isn't versioned. When you set it up, **launch the console in a real terminal**: the
host reads the password key by key, and VS Code's internal debug console can't read keys, so startup dies right
when it asks for it (`"console": "integratedTerminal"` in `launch.json`).

## License

[MIT](LICENSE). The bundled fonts are licensed separately — see [`THIRD_PARTY_NOTICES.md`](THIRD_PARTY_NOTICES.md).
