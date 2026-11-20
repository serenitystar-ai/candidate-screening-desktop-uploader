# Installer

`CandidateScreeningLoaderSetup-<version>.exe`, built with [Inno Setup 6](https://jrsoftware.org/isinfo.php).

## Building it

Three steps, in order. The installer packages a published build that must already exist: it doesn't build it.

```powershell
pnpm -C ui build
dotnet publish src/ScreeningLoader.Shell -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
& "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe" installer\CandidateScreeningLoader.iss
```

It's written to `installer/Output/`, which isn't versioned. If the published build is missing, the compiler
aborts with a message saying so instead of producing an empty installer.

The last line is PowerShell. In `cmd`, where `&` and `$env:` don't exist:

```bat
"%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe" installer\CandidateScreeningLoader.iss
```

Inno Setup is installed with `winget install JRSoftware.InnoSetup`. It ends up in `%LOCALAPPDATA%\Programs`
when installed per user and in `Program Files (x86)` when installed for the whole machine; the commands above
assume the former.

**The version comes from the executable**, which takes it from `<Version>` in `ScreeningLoader.Shell.csproj`.
Bump it there, not here.

## Releasing

Publishing a GitHub release runs the [Release workflow](../.github/workflows/release.yml), which builds the
installer on a Windows runner and attaches it to that release a couple of minutes later. Its tag has to be
`v<Version>`, matching `<Version>` in `ScreeningLoader.Shell.csproj`; otherwise the workflow fails before
building and the release is left without an installer, so delete it, fix the tag and publish again.

From the browser: **Releases → Draft a new release**, choose a tag, type `v0.9.1` and pick **Create new tag on
publish**, target `main`, **Generate release notes**, and **Publish release**. Tick **Set as a pre-release** for
a build that is not meant for users yet.

From a terminal, the same thing:

```bash
gh release create v0.9.1 --target main --generate-notes
```

Pushing a tag alone does nothing: the workflow runs on the release, not on the tag.

## What it does

- Installs **per user**, in `%LOCALAPPDATA%\Programs\CandidateScreeningLoader`. No administrator rights and no
  IT involvement.
- Adds the shortcut to the Start menu and, if the checkbox is accepted, to the desktop.
- Registers the uninstall entry in Programs and Features.
- **Checks for the WebView2 runtime and downloads it if missing.** It comes preinstalled on Windows 11 and on
  Windows 10 with a modern Edge, so that branch almost never runs; when it does, it needs internet access.

## What it doesn't do

- **It isn't signed.** If the installer arrives by email, Teams or a download, Windows adds the Mark of the Web
  and SmartScreen shows *"Windows protected your PC"*. Through an internal network share or a USB drive it
  doesn't happen.
- **It doesn't delete local data on uninstall.** Audit logs and preferences live in
  `%LOCALAPPDATA%\CandidateScreeningLoader` and stay there: they belong to whoever uses the application, not to
  the application.
- **x64 only.** An ARM64 machine would run it emulated.
