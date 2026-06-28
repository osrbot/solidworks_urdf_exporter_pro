param(
    [string]$Configuration = "Release",
    [string]$Platform = "x64",
    [string]$SolidWorksInstallDir = "E:\Solidworks 2023\SOLIDWORKS Crop\SOLIDWORKS"
)

$ErrorActionPreference = "Stop"

$RepoRoot = Split-Path -Parent $PSScriptRoot
$SafeRepoRoot = $RepoRoot -replace "\\", "/"
$Solution = Join-Path $RepoRoot "SW2URDF.sln"
$PackagesConfig = Join-Path $RepoRoot "SW2URDF\packages.config"
$PackagesDirectory = Join-Path $RepoRoot "packages"
$InstallerScript = Join-Path $RepoRoot "INSTALL\Install.iss"
$BaseIntermediateOutputPath = Join-Path $RepoRoot ".codex-build\SW2URDF\obj"
$IntermediateOutputPath = Join-Path $BaseIntermediateOutputPath "$Platform\$Configuration"
$BaseIntermediateOutputPath = $BaseIntermediateOutputPath.TrimEnd("\") + "\"
$IntermediateOutputPath = $IntermediateOutputPath.TrimEnd("\") + "\"
$VsWhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$WindowsDir = $env:Windir
if ([string]::IsNullOrWhiteSpace($WindowsDir)) {
    $WindowsDir = $env:SystemRoot
}
if ([string]::IsNullOrWhiteSpace($WindowsDir)) {
    $WindowsDir = "C:\Windows"
}
$FrameworkMSBuild = Join-Path $WindowsDir "Microsoft.NET\Framework64\v4.0.30319\MSBuild.exe"
$InstallerDate = Get-Date -Format "yyyyMMdd"
$InstallerCommit = & git -c "safe.directory=$SafeRepoRoot" -C $RepoRoot rev-parse --short=7 HEAD 2>$null
if ([string]::IsNullOrWhiteSpace($InstallerCommit)) {
    $InstallerCommit = "unknown"
}

if (Test-Path $VsWhere) {
    $MSBuild = & $VsWhere -latest -requires Microsoft.Component.MSBuild -find "MSBuild\**\Bin\MSBuild.exe" | Select-Object -First 1
}

if (-not $MSBuild -and (Test-Path $FrameworkMSBuild)) {
    $MSBuild = $FrameworkMSBuild
}

if (-not $MSBuild) {
    throw "MSBuild was not found. Install Visual Studio Build Tools or the .NET Framework MSBuild tools."
}

$ISCC = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 5\ISCC.exe",
    "${env:ProgramFiles}\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles}\Inno Setup 5\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $ISCC) {
    throw "Inno Setup compiler was not found. Install Inno Setup 6."
}

$NuGet = Get-Command nuget.exe -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $NuGet -and (Test-Path "C:\tmp\sw2urdf-tools\nuget.exe")) {
    $NuGet = Get-Item "C:\tmp\sw2urdf-tools\nuget.exe"
}

if ($NuGet) {
    & $NuGet.FullName restore $PackagesConfig -PackagesDirectory $PackagesDirectory
    if ($LASTEXITCODE -ne 0) {
        throw "NuGet restore failed with exit code $LASTEXITCODE."
    }
}

& $MSBuild $Solution /p:Configuration=$Configuration /p:Platform=$Platform /p:RegisterForComInterop=false /p:PostBuildEvent= /p:SolidWorksInstallDir="$SolidWorksInstallDir" "/p:BaseIntermediateOutputPath=$BaseIntermediateOutputPath" "/p:IntermediateOutputPath=$IntermediateOutputPath"
if ($LASTEXITCODE -ne 0) {
    throw "MSBuild failed with exit code $LASTEXITCODE."
}

& $ISCC "/DInstallerDate=$InstallerDate" "/DInstallerCommit=$InstallerCommit" $InstallerScript
if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup compiler failed with exit code $LASTEXITCODE."
}
