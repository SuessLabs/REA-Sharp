# Build Windows GitHub release assets

Install the .NET 10 SDK selected by global.json, and use a Git checkout on Windows.
From the repository root, run:

```bat
build.bat
```

The batch launcher calls build.ps1 using PowerShell 7 when available and Windows
PowerShell 5.1 otherwise. Build orchestration is the explicitly requested
PowerShell/batch exception; installer and application logic remain C#. No Node/npm,
installer-authoring toolchain, administrative installation or external engine is
required. Restore may download the matching .NET deployment packs.

## Output

The ignored publish folder contains versioned Windows x64 assets by default:

- REA-Sharp-VERSION-win-x64-Setup.exe: standalone C# installer with embedded
  payload and runtime. Run this file directly after downloading; no checkout,
  .NET SDK or separately installed .NET runtime is needed.
- REA-Sharp-VERSION-win-x64.zip: portable self-contained CLI. Extract the entire
  archive and run rea.exe; keep its DLLs and other files together.
- REA-Sharp-VERSION-win-x64-manifest.json: source revision, dirty-source flag,
  full-suite status and each payload file's length and SHA-256.
- REA-Sharp-VERSION-win-x64-build.json: build and installer smoke-check status.
- REA-Sharp-VERSION-win-x64-SHA256SUMS.txt: checksums of the other four assets.

Upload these files manually to a GitHub release. The build does not upload,
publish, create a Git tag, sign binaries or claim a qualified clean-commit release.
Hashes detect changed bytes; they do not authenticate a publisher.

## Build and install

```bat
build.bat -Install
build.bat -Install -InstallDir "C:\Tools\REA-Sharp"
build.bat -Version 0.2.0
build.bat -Runtime win-arm64
build.bat -Test
build.bat -Force
```

- -Install builds first, then launches the resulting installer and requests
  confirmation. -InstallDir selects its destination.
- -Version overrides the project/package and CLI/MCP version for this build
  without editing source project files. The default is the CLI project version.
- -Runtime supports win-x64 and win-arm64. Installer smoke tests run on the
  matching build-process architecture; cross-built smoke coverage is explicitly
  recorded as not run.
- -Test runs the repository and complete test/cleanup gates before publishing.
  Any failure stops the build; no existing policy failure is silently exempted.
- -Force backs up existing same-version release assets under publish/backups
  before replacement. Without it, existing assets are preserved and the build stops.

Default builds compile the solution and run isolated real installer/CLI smoke
checks on matching architecture. The full test suite is opt-in via -Test and is
recorded as not-run otherwise. The existing native-source policy failure remains
a real failure if it is still present; smoke tests are not a full-suite substitute.

## Installer behavior

Setup.exe shows its plan and requires yes before writing. Its default destination
is %LOCALAPPDATA%\Programs\REA-Sharp\VERSION\RUNTIME. It does not modify PATH,
install providers, register clients automatically or download software.

```text
REA-Sharp-VERSION-win-x64-Setup.exe --dry-run
REA-Sharp-VERSION-win-x64-Setup.exe --install-dir C:\Tools\REA-Sharp --yes
```

An exact matching installation is left unchanged. Other files, modified installs
and different releases are preserved; select a new directory or build a new version
rather than overwriting them. The installer validates its manifest, paths,
dependencies and digests before extracting into staging. Failed extraction is
retained for inspection. The 256 MiB archive and 512 MiB expanded-payload limits
bound installation work.

After installation, invoke the printed rea.exe path. Its setup command uses the
native executable for MCP registration, so a self-contained installation does
not accidentally depend on a global dotnet command. Configuration changes remain
a separate explicit setup --config PATH --yes action.

Self-contained deployments include the selected .NET runtime:
[deployment reference](https://learn.microsoft.com/en-us/dotnet/core/deploying/).
