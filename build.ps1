[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'win-arm64')][string]$Runtime = 'win-x64',
    [ValidateSet('Release', 'Debug')][string]$Configuration = 'Release',
    [string]$Version,
    [switch]$Test,
    [switch]$Install,
    [string]$InstallDir,
    [switch]$Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$buildRoot = [IO.Path]::GetFullPath($PSScriptRoot)
$publishRoot = Join-Path $buildRoot 'publish'
$artifactRoot = Join-Path $buildRoot 'dotnet\artifacts\release-build'
$stagingRoot = Join-Path $artifactRoot ([Guid]::NewGuid().ToString('N'))
$buildSucceeded = $false

function Invoke-Dotnet {
    param([string[]]$Arguments)
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw ('dotnet failed with exit code ' + $LASTEXITCODE) }
}
function Invoke-ReleaseExe {
    param([string]$Executable, [string[]]$Arguments)
    & $Executable @Arguments
    if ($LASTEXITCODE -ne 0) { throw ($Executable + ' failed with exit code ' + $LASTEXITCODE) }
}
function Write-Utf8 {
    param([string]$Path, [string]$Text)
    [IO.File]::WriteAllText($Path, $Text, ([Text.UTF8Encoding]::new($false)))
}
function Get-RelativeReleasePath {
    param([string]$Base, [string]$Path)
    $basePath = [IO.Path]::GetFullPath($Base).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    $filePath = [IO.Path]::GetFullPath($Path)
    if (-not $filePath.StartsWith($basePath, [StringComparison]::OrdinalIgnoreCase)) { throw 'Release file escaped its staging directory' }
    return $filePath.Substring($basePath.Length).Replace('\', '/')
}

Push-Location -LiteralPath $buildRoot
try {
    if ($env:OS -ne 'Windows_NT') { throw 'This build script produces Windows installers and requires Windows.' }
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw 'Install the .NET 10 SDK selected by global.json, then rerun build.bat.' }
    if (-not (Get-Command git -ErrorAction SilentlyContinue)) { throw 'Git is required to record the source revision.' }
    if ($InstallDir -and -not $Install) { throw '-InstallDir requires -Install.' }
    if ($Install -and (($Runtime -eq 'win-arm64') -ne ([Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString() -eq 'Arm64'))) {
        throw 'The requested installer architecture cannot be launched by this build process. Build without -Install for cross-publishing.'
    }
    if (-not $Version) {
        [xml]$project = Get-Content -LiteralPath 'dotnet/src/Rea.Cli/Rea.Cli.csproj' -Raw
        $versionNode = $project.SelectSingleNode('/Project/PropertyGroup/Version')
        if ($null -eq $versionNode) { throw 'Supply -Version because the CLI project does not declare its package version.' }
        $Version = $versionNode.InnerText
    }
    if ($Version -notmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-[0-9A-Za-z]+(?:[.-][0-9A-Za-z]+)*)?$') {
        throw '-Version must be an exact semantic version without build metadata.'
    }
    $sourceRevision = (& git rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0 -or $sourceRevision -notmatch '^[0-9a-f]{40}$') { throw 'Run this build from a Git checkout.' }
    $sourceChanges = @(& git status --porcelain --untracked-files=normal)
    if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect source status.' }
    $sourceDirty = $sourceChanges.Count -ne 0
    $assetName = 'REA-Sharp-' + $Version + '-' + $Runtime
    $finalNames = @(($assetName + '-Setup.exe'), ($assetName + '.zip'), ($assetName + '-manifest.json'), ($assetName + '-build.json'), ($assetName + '-SHA256SUMS.txt'))
    foreach ($name in $finalNames) {
        if ((Test-Path -LiteralPath (Join-Path $publishRoot $name)) -and -not $Force) {
            throw ('Release assets already exist. Use -Force to back them up before replacement: ' + $name)
        }
    }
    [IO.Directory]::CreateDirectory($stagingRoot) | Out-Null
    $appRoot = Join-Path $stagingRoot 'app'
    $setupRoot = Join-Path $stagingRoot 'setup'
    $testStatus = 'not-run'
    Write-Host ('Building REA-Sharp ' + $Version + ' for ' + $Runtime)
    Invoke-Dotnet -Arguments @('build', 'Rea.slnx', '--configuration', $Configuration, '--nologo', '--disable-build-servers', '-m:1', ('-p:Version=' + $Version))
    if ($Test) {
        Invoke-Dotnet -Arguments @('exec', ('scripts/bin/' + $Configuration + '/net10.0/Rea.Validation.dll'), 'repository', '.')
        Invoke-Dotnet -Arguments @('exec', ('scripts/bin/' + $Configuration + '/net10.0/Rea.Validation.dll'), 'test-hygiene', '.', (Join-Path $stagingRoot 'test-results.json'))
        $testStatus = 'passed'
    }
    Invoke-Dotnet -Arguments @('publish', 'dotnet/src/Rea.Cli/Rea.Cli.csproj', '--configuration', $Configuration,
        '--runtime', $Runtime, '--self-contained', 'true', '--disable-build-servers', '-m:1', '--output', $appRoot,
        '-p:PackAsTool=false', '-p:PublishSingleFile=false', '-p:DebugType=none', '-p:DebugSymbols=false', ('-p:Version=' + $Version))
    foreach ($name in @('LICENSE', 'THIRD_PARTY_NOTICES.md')) { Copy-Item -LiteralPath (Join-Path $buildRoot $name) -Destination $appRoot }
    Copy-Item -LiteralPath (Join-Path $buildRoot 'dotnet/README.md') -Destination (Join-Path $appRoot 'README.md')
    $runtimeConfig = Get-Content -LiteralPath (Join-Path $appRoot 'rea.runtimeconfig.json') -Raw | ConvertFrom-Json
    $framework = @($runtimeConfig.runtimeOptions.includedFrameworks | Where-Object { $_.name -eq 'Microsoft.NETCore.App' })
    if ($framework.Count -ne 1) { throw 'The CLI publish is not a self-contained Microsoft.NETCore.App deployment.' }
    $runtimeVersion = $framework[0].version
    $assets = Get-Content -LiteralPath 'dotnet/src/Rea.Cli/obj/project.assets.json' -Raw | ConvertFrom-Json
    $runtimePack = $null
    foreach ($folder in $assets.packageFolders.PSObject.Properties.Name) {
        $candidate = Join-Path $folder ('microsoft.netcore.app.runtime.' + $Runtime + '/' + $runtimeVersion)
        if ((Test-Path -LiteralPath (Join-Path $candidate 'LICENSE.TXT')) -and (Test-Path -LiteralPath (Join-Path $candidate 'THIRD-PARTY-NOTICES.TXT'))) { $runtimePack = $candidate; break }
    }
    if (-not $runtimePack) { throw 'Cannot preserve the matching redistributed .NET runtime license/notices from the restored deployment pack.' }
    $licenseRoot = Join-Path $appRoot 'licenses/dotnet'
    [IO.Directory]::CreateDirectory($licenseRoot) | Out-Null
    foreach ($notice in @('LICENSE.TXT', 'THIRD-PARTY-NOTICES.TXT')) { Copy-Item -LiteralPath (Join-Path $runtimePack $notice) -Destination (Join-Path $licenseRoot $notice) }
    $releaseFiles = @(Get-ChildItem -LiteralPath $appRoot -File -Recurse | Sort-Object FullName | ForEach-Object {
        [ordered]@{
            path = Get-RelativeReleasePath -Base $appRoot -Path $_.FullName
            length = $_.Length
            sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        }
    })
    $manifest = [ordered]@{
        schema_version = 1
        version = $Version
        runtime_identifier = $Runtime
        entry_point = 'rea.exe'
        source_revision = $sourceRevision
        source_dirty = $sourceDirty
        test_status = $testStatus
        files = $releaseFiles
    }
    $manifestPath = Join-Path $appRoot 'release-manifest.json'
    Write-Utf8 -Path $manifestPath -Text (($manifest | ConvertTo-Json -Depth 10) + [Environment]::NewLine)
    $payload = Join-Path $stagingRoot ($assetName + '.zip')
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zipStream = [IO.File]::Open($payload, [IO.FileMode]::CreateNew)
    $archive = [IO.Compression.ZipArchive]::new($zipStream, [IO.Compression.ZipArchiveMode]::Create, $false)
    try {
        foreach ($file in Get-ChildItem -LiteralPath $appRoot -File -Recurse | Sort-Object FullName) {
            $entryName = Get-RelativeReleasePath -Base $appRoot -Path $file.FullName
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $file.FullName, $entryName, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
        }
    }
    finally { $archive.Dispose(); $zipStream.Dispose() }
    Invoke-Dotnet -Arguments @('publish', 'dotnet/tools/Rea.Installer/Rea.Installer.csproj', '--configuration', $Configuration,
        '--runtime', $Runtime, '--self-contained', 'true', '--disable-build-servers', '-m:1', '--output', $setupRoot,
        '-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true', '-p:EnableCompressionInSingleFile=true',
        '-p:PublishTrimmed=false', '-p:DebugType=none', '-p:DebugSymbols=false', ('-p:Version=' + $Version), ('-p:InstallerPayload=' + $payload))
    $setupExe = Join-Path $setupRoot 'Rea.Installer.exe'
    if (-not (Test-Path -LiteralPath $setupExe -PathType Leaf)) { throw 'The standalone installer was not produced.' }
    $sameArchitecture = (($Runtime -eq 'win-arm64') -eq ([Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString() -eq 'Arm64'))
    $smokeStatus = 'not-run-cross-architecture'
    if ($sameArchitecture) {
        $smokeRoot = Join-Path $stagingRoot 'smoke-install'
        Invoke-ReleaseExe -Executable $setupExe -Arguments @('--install-dir', $smokeRoot, '--dry-run')
        if (Test-Path -LiteralPath $smokeRoot) { throw 'Installer dry-run unexpectedly created the installation directory.' }
        Invoke-ReleaseExe -Executable $setupExe -Arguments @('--install-dir', $smokeRoot, '--yes')
        $installedExe = Join-Path $smokeRoot 'rea.exe'
        Invoke-ReleaseExe -Executable $installedExe -Arguments @('--version')
        Invoke-ReleaseExe -Executable $installedExe -Arguments @('--help')
        $fixture = Join-Path $buildRoot ('dotnet/tests/fixtures/Rea.ManagedFixture/bin/' + $Configuration + '/net10.0/Rea.ManagedFixture.dll')
        $inspection = & $installedExe inspect-managed-artifact $fixture
        if ($LASTEXITCODE -ne 0) { throw 'Installed CLI could not inspect the managed fixture.' }
        $reply = ($inspection -join [Environment]::NewLine) | ConvertFrom-Json
        if (-not $reply.ok -or $reply.value.assembly.name -ne 'Rea.ManagedFixture' -or $reply.value.artifact.sha256 -ne (Get-FileHash -LiteralPath $fixture -Algorithm SHA256).Hash.ToLowerInvariant()) { throw 'Installed CLI returned incorrect fixture identity.' }
        $configPath = Join-Path $stagingRoot 'smoke-client.json'
        Invoke-ReleaseExe -Executable $installedExe -Arguments @('setup', '--config', $configPath, '--yes')
        $client = Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json
        if ($client.mcpServers.'rea-sharp'.command -ne $installedExe) { throw 'Self-contained setup did not register its native executable.' }
        $smokeStatus = 'passed'
    }
    [IO.Directory]::CreateDirectory($publishRoot) | Out-Null
    if ($Force) {
        $existing = @($finalNames | Where-Object { Test-Path -LiteralPath (Join-Path $publishRoot $_) })
        if ($existing.Count -gt 0) {
            $backupRoot = Join-Path $publishRoot ('backups/' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N'))
            [IO.Directory]::CreateDirectory($backupRoot) | Out-Null
            foreach ($name in $existing) { Move-Item -LiteralPath (Join-Path $publishRoot $name) -Destination (Join-Path $backupRoot $name) }
        }
    }
    Copy-Item -LiteralPath $setupExe -Destination (Join-Path $publishRoot ($assetName + '-Setup.exe'))
    Copy-Item -LiteralPath $payload -Destination (Join-Path $publishRoot ($assetName + '.zip'))
    Copy-Item -LiteralPath $manifestPath -Destination (Join-Path $publishRoot ($assetName + '-manifest.json'))
    Write-Utf8 -Path (Join-Path $stagingRoot 'build-result.json') -Text (([ordered]@{ version = $Version; runtime = $Runtime; tests = $testStatus; installer_smoke = $smokeStatus; source_revision = $sourceRevision; source_dirty = $sourceDirty } | ConvertTo-Json) + [Environment]::NewLine)
    Copy-Item -LiteralPath (Join-Path $stagingRoot 'build-result.json') -Destination (Join-Path $publishRoot ($assetName + '-build.json'))
    $checksumLines = @($finalNames[0..3] | ForEach-Object {
        (Get-FileHash -LiteralPath (Join-Path $publishRoot $_) -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + $_
    })
    Write-Utf8 -Path (Join-Path $publishRoot ($assetName + '-SHA256SUMS.txt')) -Text (($checksumLines -join [Environment]::NewLine) + [Environment]::NewLine)
    $buildSucceeded = $true
    Write-Host ('Release files: ' + $publishRoot)
    Write-Host ('Full suite: ' + $testStatus + '; installer smoke: ' + $smokeStatus)
    if ($Install) {
        $installerArguments = @()
        if ($InstallDir) { $installerArguments += @('--install-dir', [IO.Path]::GetFullPath($InstallDir)) }
        Invoke-ReleaseExe -Executable (Join-Path $publishRoot ($assetName + '-Setup.exe')) -Arguments $installerArguments
    }
}
catch {
    Write-Error -Message $_.Exception.Message -ErrorAction Continue
    Write-Host ('Build/installation failed. Staging evidence, if created: ' + $stagingRoot)
    exit 1
}
finally {
    Pop-Location
    if ($buildSucceeded -and (Test-Path -LiteralPath $stagingRoot)) {
        $resolvedStaging = [IO.Path]::GetFullPath($stagingRoot)
        $ownedPrefix = [IO.Path]::GetFullPath($artifactRoot).TrimEnd('\') + '\'
        if (-not $resolvedStaging.StartsWith($ownedPrefix, [StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolvedStaging) -notmatch '^[0-9a-f]{32}$') { throw 'Refusing unexpected build-staging cleanup path.' }
        Remove-Item -LiteralPath $resolvedStaging -Recurse -Force
    }
}
