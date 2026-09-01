param(
    [string]$Configuration = "Release",
    [string]$Platform = "x64",
    [string]$SolidWorksInstallDir = "",
    [string]$InnoCompilerPath = "",
    [string]$DotNetPath = ""
)

$ErrorActionPreference = "Stop"

function Get-Sha256([string]$Path) {
    $Stream = [System.IO.File]::OpenRead($Path)
    try {
        $Sha256 = [System.Security.Cryptography.SHA256]::Create()
        try {
            return ([System.BitConverter]::ToString(
                $Sha256.ComputeHash($Stream))).Replace("-", "").ToLowerInvariant()
        }
        finally {
            $Sha256.Dispose()
        }
    }
    finally {
        $Stream.Dispose()
    }
}

function Get-NormalizedTextSha256([string]$Path) {
    $Text = [System.IO.File]::ReadAllText($Path).Replace("`r`n", "`n").Replace("`r", "`n")
    $Bytes = (New-Object System.Text.UTF8Encoding($false)).GetBytes($Text)
    $Sha256 = [System.Security.Cryptography.SHA256]::Create()
    try {
        return ([System.BitConverter]::ToString(
            $Sha256.ComputeHash($Bytes))).Replace("-", "").ToLowerInvariant()
    }
    finally {
        $Sha256.Dispose()
    }
}

function Get-PinnedDownload(
    [string]$Url,
    [string]$ExpectedSha256,
    [string]$Destination,
    [string]$Label) {
    $Expected = $ExpectedSha256.ToLowerInvariant()
    if (Test-Path -LiteralPath $Destination) {
        if ((Get-Sha256 $Destination) -ne $Expected) {
            Remove-Item -LiteralPath $Destination -Force
        }
    }
    if (-not (Test-Path -LiteralPath $Destination)) {
        [System.Net.ServicePointManager]::SecurityProtocol =
            [System.Net.ServicePointManager]::SecurityProtocol -bor
            [System.Net.SecurityProtocolType]::Tls12
        Invoke-WebRequest -Uri $Url -OutFile $Destination -UseBasicParsing
    }
    $Actual = Get-Sha256 $Destination
    if ($Actual -ne $Expected) {
        Remove-Item -LiteralPath $Destination -Force -ErrorAction SilentlyContinue
        throw "$Label does not match its pinned SHA256."
    }
    return $Actual
}

function Restore-EnvironmentVariable([string]$Name, [string]$Value) {
    if ($null -eq $Value) {
        Remove-Item ("Env:\" + $Name) -ErrorAction SilentlyContinue
    }
    else {
        [System.Environment]::SetEnvironmentVariable($Name, $Value, "Process")
    }
}

function Assert-ChildPath([string]$Parent, [string]$Child, [string]$Description) {
    $ResolvedParent = [System.IO.Path]::GetFullPath($Parent).TrimEnd('\') + '\'
    $ResolvedChild = [System.IO.Path]::GetFullPath($Child)
    if (-not $ResolvedChild.StartsWith(
        $ResolvedParent,
        [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "$Description is outside its expected parent: $ResolvedChild"
    }
    return $ResolvedChild
}

function Assert-NoPythonBytecode([string]$Root) {
    $Unexpected = @(Get-ChildItem -LiteralPath $Root -Force -Recurse | Where-Object {
        ($_.PSIsContainer -and $_.Name -eq "__pycache__") -or
        (-not $_.PSIsContainer -and $_.Extension -eq ".pyc")
    })
    if ($Unexpected.Count -gt 0) {
        $Paths = ($Unexpected | ForEach-Object { $_.FullName }) -join "; "
        throw "Refusing to package Python bytecode/cache artifacts: $Paths"
    }
}

if ($Configuration -ne "Release" -or $Platform -ne "x64") {
    throw "The installer supports only Configuration=Release and Platform=x64."
}

if ([string]::IsNullOrWhiteSpace($SolidWorksInstallDir)) {
    throw "Pass -SolidWorksInstallDir with the directory containing the four SolidWorks API assemblies."
}
$SolidWorksInstallDir = [System.IO.Path]::GetFullPath($SolidWorksInstallDir)
if (-not (Test-Path -LiteralPath $SolidWorksInstallDir -PathType Container)) {
    throw "The requested SolidWorks API directory does not exist: $SolidWorksInstallDir"
}

$SourceRepoRoot = Split-Path -Parent $PSScriptRoot
$SafeRepoRoot = $SourceRepoRoot -replace "\\", "/"
$InstallerDate = Get-Date -Format "yyyyMMdd"
$OutputDirectory = Join-Path $SourceRepoRoot "INSTALL\OUTPUT"
$Utf8NoBom = New-Object System.Text.UTF8Encoding($false)

$SourceChanges = & git -c "safe.directory=$SafeRepoRoot" -C $SourceRepoRoot status `
    --porcelain --untracked-files=normal -- . ":(exclude)INSTALL/OUTPUT" 2>$null
if ($LASTEXITCODE -ne 0) {
    throw "Unable to inspect the Git working tree before packaging."
}
if (-not [string]::IsNullOrWhiteSpace(($SourceChanges -join "`n"))) {
    throw "Refusing to package uncommitted source changes. Commit the source first."
}

$InstallerCommit = & git -c "safe.directory=$SafeRepoRoot" -C $SourceRepoRoot `
    rev-parse --short=7 HEAD 2>$null
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($InstallerCommit)) {
    throw "Unable to resolve the source commit for the installer name."
}
$InstallerCommit = $InstallerCommit.Trim()
$SourceCommit = & git -c "safe.directory=$SafeRepoRoot" -C $SourceRepoRoot `
    rev-parse HEAD 2>$null
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($SourceCommit)) {
    throw "Unable to resolve the full source commit for installer provenance."
}
$SourceCommit = $SourceCommit.Trim()
$SourceTree = & git -c "safe.directory=$SafeRepoRoot" -C $SourceRepoRoot `
    rev-parse "$SourceCommit^{tree}" 2>$null
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($SourceTree)) {
    throw "Unable to resolve the source tree for installer provenance."
}
$SourceTree = $SourceTree.Trim()

$VsWhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$WindowsDir = $env:Windir
if ([string]::IsNullOrWhiteSpace($WindowsDir)) {
    $WindowsDir = $env:SystemRoot
}
if ([string]::IsNullOrWhiteSpace($WindowsDir)) {
    $WindowsDir = "C:\Windows"
}
$FrameworkMSBuild = Join-Path $WindowsDir "Microsoft.NET\Framework64\v4.0.30319\MSBuild.exe"
$MSBuild = $null
if (Test-Path -LiteralPath $VsWhere) {
    $MSBuild = & $VsWhere -latest -products * -requires Microsoft.Component.MSBuild `
        -find "MSBuild\**\Bin\MSBuild.exe" | Select-Object -First 1
    if (-not $MSBuild) {
        # Some Build Tools installations contain MSBuild even when setup metadata
        # does not advertise the Microsoft.Component.MSBuild component.
        $MSBuild = & $VsWhere -latest -products * -find `
            "MSBuild\**\Bin\MSBuild.exe" | Select-Object -First 1
    }
}
if (-not $MSBuild -and (Test-Path -LiteralPath $FrameworkMSBuild)) {
    $MSBuild = $FrameworkMSBuild
}
if (-not $MSBuild) {
    throw "MSBuild was not found. Install Visual Studio Build Tools or the .NET Framework MSBuild tools."
}
$MSBuild = [System.IO.Path]::GetFullPath($MSBuild)
$DotNet = $null
if (-not [string]::IsNullOrWhiteSpace($DotNetPath)) {
    if (-not (Test-Path -LiteralPath $DotNetPath -PathType Leaf)) {
        throw "The requested dotnet executable does not exist: $DotNetPath"
    }
    $DotNet = [System.IO.Path]::GetFullPath($DotNetPath)
}
else {
    $DotNetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($DotNetCommand) {
        $DotNet = $DotNetCommand.Source
    }
}
if (-not $DotNet) {
    throw ".NET SDK was not found. Install .NET 8 SDK or pass -DotNetPath."
}
$DotNetSdks = @(& $DotNet --list-sdks)
if ($LASTEXITCODE -ne 0) {
    throw "Unable to enumerate installed .NET SDKs with $DotNet."
}
$GlobalJsonPath = Join-Path $SourceRepoRoot "global.json"
if (-not (Test-Path -LiteralPath $GlobalJsonPath -PathType Leaf)) {
    throw "The pinned .NET SDK contract is missing: $GlobalJsonPath"
}
$GlobalJson = Get-Content -LiteralPath $GlobalJsonPath -Raw -Encoding UTF8 | ConvertFrom-Json
$RequiredDotNetVersion = [string]$GlobalJson.sdk.version
if ($RequiredDotNetVersion -ne "8.0.424" -or
    [string]$GlobalJson.sdk.rollForward -ne "disable") {
    throw "global.json must pin .NET SDK 8.0.424 with rollForward=disable."
}
$DotNet8Sdk = @($DotNetSdks | ForEach-Object {
    $Match = [regex]::Match(
        [string]$_,
        '^(?<version>8\.[0-9]+\.[0-9]+)\s+\[(?<sdkRoot>.+)\]$')
    if ($Match.Success) {
        [pscustomobject]@{
            Version = [version]$Match.Groups['version'].Value
            VersionText = $Match.Groups['version'].Value
            SdkRoot = $Match.Groups['sdkRoot'].Value
        }
    }
} | Where-Object { $_.VersionText -eq $RequiredDotNetVersion })
if ($DotNet8Sdk.Count -ne 1) {
    throw "Installer verification requires the exact .NET SDK $RequiredDotNetVersion. Pass its dotnet executable with -DotNetPath."
}
$DotNetVersion = [string]$DotNet8Sdk[0].VersionText
$DotNetRoot = Split-Path -Parent ([string]$DotNet8Sdk[0].SdkRoot)
$DotNetSdkPath = Join-Path ([string]$DotNet8Sdk[0].SdkRoot) `
    ($DotNetVersion + "\Sdks")
if (-not (Test-Path -LiteralPath $DotNetSdkPath -PathType Container)) {
    throw "The selected .NET 8 SDK directory is incomplete: $DotNetSdkPath"
}

$ISCC = $null
if (-not [string]::IsNullOrWhiteSpace($InnoCompilerPath)) {
    if (-not (Test-Path -LiteralPath $InnoCompilerPath -PathType Leaf)) {
        throw "The requested Inno Setup compiler does not exist: $InnoCompilerPath"
    }
    $ISCC = $InnoCompilerPath
} else {
    $ISCC = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles}\Inno Setup 6\ISCC.exe"
    ) | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}
if (-not $ISCC) {
    throw "Inno Setup compiler was not found. Install Inno Setup 6.3.3 or pass -InnoCompilerPath."
}
$ISCC = [System.IO.Path]::GetFullPath($ISCC)
$InnoWhatsNew = Join-Path (Split-Path -Parent $ISCC) "whatsnew.htm"
$InnoVersionMatch = if (Test-Path -LiteralPath $InnoWhatsNew) {
    [regex]::Match(
        [System.IO.File]::ReadAllText($InnoWhatsNew),
        '(?:<summary>Inno Setup |class="ver">)(?<version>[0-9]+\.[0-9]+(?:\.[0-9]+)?)')
} else {
    $null
}
if ($null -eq $InnoVersionMatch -or -not $InnoVersionMatch.Success) {
    throw "Unable to determine the Inno Setup compiler version from $InnoWhatsNew."
}
$InnoVersion = [version]$InnoVersionMatch.Groups['version'].Value
if ($InnoVersion -lt [version]'6.3' -or $InnoVersion -gt [version]'6.3.3') {
    throw "Release packaging requires Inno Setup 6.3.0 through 6.3.3 so CI can inspect the installer payload. Pass a compatible compiler with -InnoCompilerPath."
}

$LockPath = Join-Path $SourceRepoRoot "SW2URDF\packages.release.lock.json"
if (-not (Test-Path -LiteralPath $LockPath)) {
    throw "Release package lock file was not found: $LockPath"
}
$ReleaseLock = Get-Content -LiteralPath $LockPath -Raw -Encoding UTF8 | ConvertFrom-Json
if ($ReleaseLock.schemaVersion -ne 1 -or $null -eq $ReleaseLock.nuget -or
    $null -eq $ReleaseLock.packages) {
    throw "Release package lock file is invalid."
}

$LegacyTestLockPath = Join-Path $SourceRepoRoot "scripts\legacy-test-packages.lock.json"
if (-not (Test-Path -LiteralPath $LegacyTestLockPath -PathType Leaf)) {
    throw "Legacy test package content lock was not found: $LegacyTestLockPath"
}
$LegacyTestLock = Get-Content -LiteralPath $LegacyTestLockPath -Raw -Encoding UTF8 |
    ConvertFrom-Json
if ($LegacyTestLock.schemaVersion -ne 1 -or
    [string]$LegacyTestLock.packagesConfig.path -cne "SW2URDF/packages.config" -or
    $null -eq $LegacyTestLock.packages -or $LegacyTestLock.packages.Count -lt 1) {
    throw "Legacy test package content lock is invalid."
}
$LegacyPackagesConfigPath = Join-Path $SourceRepoRoot `
    ([string]$LegacyTestLock.packagesConfig.path).Replace("/", "\")
if ((Get-NormalizedTextSha256 $LegacyPackagesConfigPath) -cne
    ([string]$LegacyTestLock.packagesConfig.sha256).ToLowerInvariant()) {
    throw "Legacy packages.config does not match its content lock."
}
[xml]$LegacyPackagesConfig = Get-Content -LiteralPath $LegacyPackagesConfigPath -Encoding UTF8
$LegacyConfigCoordinates = @($LegacyPackagesConfig.packages.package | ForEach-Object {
    ([string]$_.id) + "`t" + ([string]$_.version)
} | Sort-Object)
$LegacyLockCoordinates = @($LegacyTestLock.packages | ForEach-Object {
    if ([string]::IsNullOrWhiteSpace([string]$_.id) -or
        [string]::IsNullOrWhiteSpace([string]$_.version) -or
        [string]$_.sha256 -notmatch '^[0-9a-fA-F]{64}$') {
        throw "Legacy test package content lock contains an invalid package record."
    }
    ([string]$_.id) + "`t" + ([string]$_.version)
} | Sort-Object)
if ($LegacyConfigCoordinates.Count -ne $LegacyLockCoordinates.Count -or
    ($LegacyConfigCoordinates -join "`n") -cne ($LegacyLockCoordinates -join "`n")) {
    throw "Legacy packages.config coordinates do not match the content lock."
}

$ToolCache = Join-Path ([System.IO.Path]::GetTempPath()) "sw2urdf-tools"
New-Item -ItemType Directory -Path $ToolCache -Force | Out-Null
$NuGet = Join-Path $ToolCache ("nuget-" + $ReleaseLock.nuget.version + ".exe")
$ExpectedNuGetHash = ([string]$ReleaseLock.nuget.sha256).ToLowerInvariant()
if (Test-Path -LiteralPath $NuGet) {
    if ((Get-Sha256 $NuGet) -ne $ExpectedNuGetHash) {
        Remove-Item -LiteralPath $NuGet -Force
    }
}
if (-not (Test-Path -LiteralPath $NuGet)) {
    [System.Net.ServicePointManager]::SecurityProtocol =
        [System.Net.ServicePointManager]::SecurityProtocol -bor
        [System.Net.SecurityProtocolType]::Tls12
    Invoke-WebRequest -Uri $ReleaseLock.nuget.url -OutFile $NuGet
}
if ((Get-Sha256 $NuGet) -ne $ExpectedNuGetHash) {
    Remove-Item -LiteralPath $NuGet -Force -ErrorAction SilentlyContinue
    throw "Downloaded NuGet CLI does not match the pinned SHA256."
}
$NuGetVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($NuGet).ProductVersion
if ([string]::IsNullOrWhiteSpace($NuGetVersion) -or
    -not $NuGetVersion.StartsWith(
        [string]$ReleaseLock.nuget.version,
        [System.StringComparison]::Ordinal)) {
    throw "Pinned NuGet CLI version does not match the release lock."
}

$TempRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
$BuildRoot = Join-Path $TempRoot ("sw2urdf-build-" + [Guid]::NewGuid().ToString("N"))
$BuildRoot = Assert-ChildPath $TempRoot $BuildRoot "Temporary build worktree"
$WorktreeAdded = $false
$BuildSucceeded = $false
$DotNetEnvironmentConfigured = $false
$PreviousDotNetRoot = $null
$PreviousMSBuildSDKsPath = $null
$PreviousMSBuildEnableWorkloadResolver = $null
$InstallerFileName = "sw2urdfSetup_${InstallerDate}_${InstallerCommit}.exe"
$InstallerPath = Join-Path $OutputDirectory $InstallerFileName
$Sha256Path = "$InstallerPath.sha256"
$ProvenancePath = "$InstallerPath.provenance.json"
foreach ($ExistingArtifact in @($InstallerPath, $Sha256Path, $ProvenancePath)) {
    if (Test-Path -LiteralPath $ExistingArtifact) {
        throw "Refusing to overwrite an existing release artifact: $ExistingArtifact"
    }
}

try {
    $PreviousDotNetRoot = $env:DOTNET_ROOT
    $PreviousMSBuildSDKsPath = $env:MSBuildSDKsPath
    $PreviousMSBuildEnableWorkloadResolver = $env:MSBuildEnableWorkloadResolver
    $env:DOTNET_ROOT = $DotNetRoot
    $env:MSBuildSDKsPath = $DotNetSdkPath
    $env:MSBuildEnableWorkloadResolver = "false"
    $DotNetEnvironmentConfigured = $true

    & git -c "safe.directory=$SafeRepoRoot" -C $SourceRepoRoot worktree add `
        --detach $BuildRoot $SourceCommit
    if ($LASTEXITCODE -ne 0) {
        throw "Creating the immutable source worktree failed with exit code $LASTEXITCODE."
    }
    $WorktreeAdded = $true

    $BuildHead = & git -C $BuildRoot rev-parse HEAD 2>$null
    if ($LASTEXITCODE -ne 0 -or $BuildHead.Trim() -ne $SourceCommit) {
        throw "Temporary build worktree does not match the requested source commit."
    }

    Push-Location $BuildRoot
    try {
        $ActivatedDotNetVersion = (& $DotNet --version).Trim()
        $ActivatedDotNetExitCode = $LASTEXITCODE
    }
    finally {
        Pop-Location
    }
    if ($ActivatedDotNetExitCode -ne 0 -or
        $ActivatedDotNetVersion -ne $RequiredDotNetVersion) {
        throw "global.json did not activate the exact .NET SDK $RequiredDotNetVersion in the immutable build worktree."
    }

    $StagedSolidWorksDirectory = Join-Path $BuildRoot ".solidworks-api"
    New-Item -ItemType Directory -Path $StagedSolidWorksDirectory -Force | Out-Null
    $SolidWorksInputs = @(
        "SolidWorks.Interop.sldworks.dll",
        "SolidWorks.Interop.swconst.dll",
        "SolidWorks.Interop.swpublished.dll",
        "solidworkstools.dll"
    ) | ForEach-Object {
        $SourceInput = Join-Path $SolidWorksInstallDir $_
        if (-not (Test-Path -LiteralPath $SourceInput)) {
            throw "Required SolidWorks build input was not found: $SourceInput"
        }
        $StagedInput = Join-Path $StagedSolidWorksDirectory $_
        Copy-Item -LiteralPath $SourceInput -Destination $StagedInput
        [ordered]@{
            file = $_
            sha256 = Get-Sha256 $StagedInput
            fileVersion =
                [System.Diagnostics.FileVersionInfo]::GetVersionInfo($StagedInput).FileVersion
        }
    }

    $Project = Join-Path $BuildRoot "SW2URDF\SW2URDF.csproj"
    $SolutionDir = $BuildRoot.TrimEnd("\") + "\"
    $PackagesConfig = Join-Path $BuildRoot "SW2URDF\packages.release.config"
    $NuGetConfig = Join-Path $BuildRoot "NuGet.Config"
    $PackagesDirectory = Join-Path $BuildRoot "packages"
    $SdkPackagesDirectory = Join-Path $BuildRoot ".nuget-packages"
    $InstallerScript = Join-Path $BuildRoot "INSTALL\Install.iss"
    $BaseIntermediateOutputPath = Join-Path $BuildRoot ".codex-build\SW2URDF\obj"
    $IntermediateOutputPath = Join-Path $BaseIntermediateOutputPath "$Platform\$Configuration"
    $BaseIntermediateOutputPath = $BaseIntermediateOutputPath.TrimEnd("\") + "\"
    $IntermediateOutputPath = $IntermediateOutputPath.TrimEnd("\") + "\"

    & $NuGet restore $PackagesConfig -PackagesDirectory $PackagesDirectory `
        -ConfigFile $NuGetConfig -NoHttpCache -DirectDownload -NonInteractive
    if ($LASTEXITCODE -ne 0) {
        throw "NuGet restore failed with exit code $LASTEXITCODE."
    }

    $PackageInputs = @($ReleaseLock.packages | ForEach-Object {
        $PackageFile = Join-Path $PackagesDirectory `
            ("{0}.{1}\{0}.{1}.nupkg" -f $_.id, $_.version)
        if (-not (Test-Path -LiteralPath $PackageFile)) {
            throw "Pinned NuGet package archive was not restored: $PackageFile"
        }
        $ActualPackageHash = Get-Sha256 $PackageFile
        if ($ActualPackageHash -ne ([string]$_.sha256).ToLowerInvariant()) {
            throw "NuGet package $($_.id) $($_.version) does not match the pinned SHA256."
        }
        [ordered]@{
            id = [string]$_.id
            version = [string]$_.version
            sha256 = $ActualPackageHash
        }
    })
    $RestoredPackageArchives = @(Get-ChildItem -LiteralPath $PackagesDirectory `
        -Recurse -Filter "*.nupkg")
    if ($RestoredPackageArchives.Count -ne $PackageInputs.Count) {
        throw "NuGet restore produced package archives not covered by the release lock."
    }
    $SdkPackageLocks = @(
        "src\OSURDF.Core\packages.lock.json",
        "tests\OSURDF.Core.Tests\packages.lock.json",
        "TestRunner\packages.lock.json"
    ) | ForEach-Object {
        $LockFile = Join-Path $BuildRoot $_
        if (-not (Test-Path -LiteralPath $LockFile -PathType Leaf)) {
            throw "SDK package lock file was not found: $LockFile"
        }
        [ordered]@{
            path = $_.Replace("\", "/")
            sha256 = Get-NormalizedTextSha256 $LockFile
        }
    }

    $BuildOutputDirectory = Join-Path $BuildRoot "SW2URDF\bin\$Platform\$Configuration"
    $ResolvedBuildOutput = Assert-ChildPath $BuildRoot $BuildOutputDirectory "Build output"
    if (Test-Path -LiteralPath $ResolvedBuildOutput) {
        Remove-Item -LiteralPath $ResolvedBuildOutput -Recurse -Force
    }
    $ResolvedIntermediateOutput = Assert-ChildPath $BuildRoot `
        $BaseIntermediateOutputPath.TrimEnd("\") "Intermediate output"
    if (Test-Path -LiteralPath $ResolvedIntermediateOutput) {
        Remove-Item -LiteralPath $ResolvedIntermediateOutput -Recurse -Force
    }

    & $MSBuild $Project /p:Configuration=$Configuration /p:Platform=$Platform `
        /p:RegisterForComInterop=false /p:PostBuildEvent= `
        /p:SolidWorksInstallDir="$StagedSolidWorksDirectory" `
        "/p:SolutionDir=$SolutionDir" `
        "/p:SW2URDFBaseIntermediateOutputPath=$BaseIntermediateOutputPath" `
        "/p:SW2URDFIntermediateOutputPath=$IntermediateOutputPath" `
        "/p:RestoreConfigFile=$NuGetConfig" `
        "/p:RestorePackagesPath=$SdkPackagesDirectory" `
        /p:RestoreLockedMode=true /restore
    if ($LASTEXITCODE -ne 0) {
        throw "MSBuild failed with exit code $LASTEXITCODE."
    }

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $RuntimeStaging = Join-Path $BuildRoot ".asset-runtime-staging"
    New-Item -ItemType Directory -Path $RuntimeStaging -Force | Out-Null

    $OpenUsdLockPath = Join-Path $BuildRoot "tools\openusd_runtime.lock.json"
    $OpenUsdLock = Get-Content -LiteralPath $OpenUsdLockPath -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($OpenUsdLock.schemaVersion -ne 1 -or $null -eq $OpenUsdLock.python -or
        $null -eq $OpenUsdLock.usdCore) {
        throw "The OpenUSD runtime lock is invalid: $OpenUsdLockPath"
    }
    $OpenUsdPythonArchive = Join-Path $ToolCache `
        ("python-embed-" + $OpenUsdLock.python.version + "-amd64.zip")
    $OpenUsdWheel = Join-Path $ToolCache ([string]$OpenUsdLock.usdCore.filename)
    $OpenUsdPythonHash = Get-PinnedDownload `
        ([string]$OpenUsdLock.python.url) `
        ([string]$OpenUsdLock.python.sha256) `
        $OpenUsdPythonArchive `
        "Embedded Python runtime"
    $OpenUsdWheelHash = Get-PinnedDownload `
        ([string]$OpenUsdLock.usdCore.url) `
        ([string]$OpenUsdLock.usdCore.sha256) `
        $OpenUsdWheel `
        "usd-core wheel"
    $OpenUsdRuntime = Join-Path $BuildOutputDirectory "tools\openusd_runtime"
    if (Test-Path -LiteralPath $OpenUsdRuntime) {
        Remove-Item -LiteralPath $OpenUsdRuntime -Recurse -Force
    }
    New-Item -ItemType Directory -Path $OpenUsdRuntime -Force | Out-Null
    [System.IO.Compression.ZipFile]::ExtractToDirectory(
        $OpenUsdPythonArchive,
        $OpenUsdRuntime)
    $OpenUsdSitePackages = Join-Path $OpenUsdRuntime "Lib\site-packages"
    New-Item -ItemType Directory -Path $OpenUsdSitePackages -Force | Out-Null
    [System.IO.Compression.ZipFile]::ExtractToDirectory(
        $OpenUsdWheel,
        $OpenUsdSitePackages)
    [System.IO.File]::WriteAllLines(
        (Join-Path $OpenUsdRuntime "python311._pth"),
        @("python311.zip", ".", "Lib\site-packages", "import site"),
        $Utf8NoBom)
    $OpenUsdPython = Join-Path $OpenUsdRuntime "python.exe"
    $PreviousPythonDontWriteBytecode = $env:PYTHONDONTWRITEBYTECODE
    try {
        $env:PYTHONDONTWRITEBYTECODE = "1"
        & $OpenUsdPython -B -c `
            "from pxr import Usd, UsdGeom, UsdPhysics; assert Usd.GetVersion() == (0, 26, 8); print(Usd.GetVersion())"
        if ($LASTEXITCODE -ne 0) {
            throw "The pinned OpenUSD runtime did not import successfully."
        }
        $OpenUsdAdapter = Join-Path $BuildOutputDirectory `
            "tools\usd_adapter\osurdf_usd_adapter.py"
        $OpenUsdFixtureOutput = Join-Path $RuntimeStaging "openusd-fixture"
        & $OpenUsdPython -B $OpenUsdAdapter export `
            --bundle (Join-Path $BuildRoot "tests\fixtures\usd_bundle") `
            --output $OpenUsdFixtureOutput --overwrite
        if ($LASTEXITCODE -ne 0 -or
            -not (Test-Path -LiteralPath (Join-Path $OpenUsdFixtureOutput "robot.usd"))) {
            throw "The packaged OpenUSD adapter integration test failed."
        }
    }
    finally {
        Restore-EnvironmentVariable "PYTHONDONTWRITEBYTECODE" $PreviousPythonDontWriteBytecode
    }

    $MuJoCoLockPath = Join-Path $BuildRoot "tools\mujoco_runtime.lock.json"
    $MuJoCoLock = Get-Content -LiteralPath $MuJoCoLockPath -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($MuJoCoLock.schemaVersion -ne 1 -or $null -eq $MuJoCoLock.archive -or
        [string]::IsNullOrWhiteSpace([string]$MuJoCoLock.version)) {
        throw "The MuJoCo runtime lock is invalid: $MuJoCoLockPath"
    }
    $MuJoCoArchive = Join-Path $ToolCache ([string]$MuJoCoLock.archive.filename)
    $MuJoCoArchiveHash = Get-PinnedDownload `
        ([string]$MuJoCoLock.archive.url) `
        ([string]$MuJoCoLock.archive.sha256) `
        $MuJoCoArchive `
        "Official MuJoCo Windows archive"
    $MuJoCoExtract = Join-Path $RuntimeStaging "mujoco-extract"
    New-Item -ItemType Directory -Path $MuJoCoExtract -Force | Out-Null
    [System.IO.Compression.ZipFile]::ExtractToDirectory($MuJoCoArchive, $MuJoCoExtract)
    $MuJoCoRuntime = Join-Path $BuildOutputDirectory "tools\mujoco_runtime"
    if (Test-Path -LiteralPath $MuJoCoRuntime) {
        Remove-Item -LiteralPath $MuJoCoRuntime -Recurse -Force
    }
    New-Item -ItemType Directory -Path $MuJoCoRuntime -Force | Out-Null
    foreach ($Relative in @($MuJoCoLock.payload)) {
        $Normalized = ([string]$Relative).Replace("/", "\")
        $SourceRuntimeFile = Join-Path $MuJoCoExtract $Normalized
        if (-not (Test-Path -LiteralPath $SourceRuntimeFile -PathType Leaf)) {
            throw "The pinned MuJoCo archive is missing payload file: $Relative"
        }
        Copy-Item -LiteralPath $SourceRuntimeFile `
            -Destination (Join-Path $MuJoCoRuntime (Split-Path -Leaf $SourceRuntimeFile))
    }
    $MuJoCoCompile = Join-Path $MuJoCoRuntime "compile.exe"
    $MuJoCoTestSpeed = Join-Path $MuJoCoRuntime "testspeed.exe"
    & $MuJoCoCompile
    if ($LASTEXITCODE -ne 1) {
        throw "The pinned MuJoCo compiler did not return its expected usage status."
    }
    & $MuJoCoTestSpeed --help
    if ($LASTEXITCODE -ne 0) {
        throw "The pinned MuJoCo zero-step validator did not start."
    }
    $AssetRuntimeInputs = @(
        [ordered]@{
            id = "python-embed"
            version = [string]$OpenUsdLock.python.version
            sha256 = $OpenUsdPythonHash
        },
        [ordered]@{
            id = "usd-core"
            version = [string]$OpenUsdLock.usdCore.version
            sha256 = $OpenUsdWheelHash
        },
        [ordered]@{
            id = "mujoco"
            version = [string]$MuJoCoLock.version
            sha256 = $MuJoCoArchiveHash
        }
    )

    $CoreTestsProject = Join-Path $BuildRoot `
        "tests\OSURDF.Core.Tests\OSURDF.Core.Tests.csproj"
    & $DotNet restore $CoreTestsProject --locked-mode `
        --configfile $NuGetConfig --packages $SdkPackagesDirectory
    if ($LASTEXITCODE -ne 0) {
        throw "OSURDF.Core locked test restore failed with exit code $LASTEXITCODE."
    }
    $PreviousMuJoCoBin = $env:SW2URDF_MUJOCO_BIN
    $PreviousMuJoCoVersion = $env:SW2URDF_MUJOCO_VERSION
    try {
        $env:SW2URDF_MUJOCO_BIN = $MuJoCoRuntime
        $env:SW2URDF_MUJOCO_VERSION = [string]$MuJoCoLock.version
        & $DotNet test $CoreTestsProject --configuration Release --no-restore `
            --filter "Category!=PinnedMuJoCoRuntime" `
            "-p:RestorePackagesPath=$SdkPackagesDirectory"
        if ($LASTEXITCODE -ne 0) {
            throw "OSURDF.Core regression suite failed with exit code $LASTEXITCODE."
        }

        $MuJoCoTestResults = Join-Path $RuntimeStaging "mujoco-test-results"
        New-Item -ItemType Directory -Path $MuJoCoTestResults -Force | Out-Null
        & $DotNet test $CoreTestsProject --configuration Release --no-restore `
            --filter "Category=PinnedMuJoCoRuntime" `
            --logger "trx;LogFileName=mujoco-runtime.trx" `
            --results-directory $MuJoCoTestResults `
            "-p:RestorePackagesPath=$SdkPackagesDirectory"
        if ($LASTEXITCODE -ne 0) {
            throw "Pinned MuJoCo xUnit gate failed with exit code $LASTEXITCODE."
        }

        [xml]$MuJoCoTrx = Get-Content `
            -LiteralPath (Join-Path $MuJoCoTestResults "mujoco-runtime.trx") -Raw
        $MuJoCoCounters = $MuJoCoTrx.SelectSingleNode(
            "/*[local-name()='TestRun']/*[local-name()='ResultSummary']/*[local-name()='Counters']")
        if ($null -eq $MuJoCoCounters -or
            [int]$MuJoCoCounters.GetAttribute("total") -ne 1 -or
            [int]$MuJoCoCounters.GetAttribute("executed") -ne 1 -or
            [int]$MuJoCoCounters.GetAttribute("passed") -ne 1 -or
            [int]$MuJoCoCounters.GetAttribute("failed") -ne 0) {
            throw "Pinned MuJoCo gate must execute and pass exactly one test."
        }
    }
    finally {
        if ($null -eq $PreviousMuJoCoBin) {
            Remove-Item Env:\SW2URDF_MUJOCO_BIN -ErrorAction SilentlyContinue
        }
        else {
            $env:SW2URDF_MUJOCO_BIN = $PreviousMuJoCoBin
        }
        if ($null -eq $PreviousMuJoCoVersion) {
            Remove-Item Env:\SW2URDF_MUJOCO_VERSION -ErrorAction SilentlyContinue
        }
        else {
            $env:SW2URDF_MUJOCO_VERSION = $PreviousMuJoCoVersion
        }
    }
    $CoreTestEvidence = [ordered]@{
        configuration = "Release"
        framework = "net8.0"
        result = "passed"
        bundledOpenUsdIntegration = "passed"
        bundledMuJoCoIntegration = "passed"
    }
    Remove-Item -LiteralPath $RuntimeStaging -Recurse -Force
    Assert-NoPythonBytecode $BuildOutputDirectory

    $InstallerSchemaFiles = @(
        "README.md",
        "robot.schema.v2.json",
        "robot-bundle-manifest.schema.v1.json",
        "ros2-control-profile.schema.v1.json",
        "ros2-control-profile.example.json"
    ) | ForEach-Object {
        Get-Item -LiteralPath (Join-Path $BuildOutputDirectory ("schemas\" + $_))
    }
    $PayloadCandidates = @(
        Get-ChildItem -LiteralPath $BuildOutputDirectory -File -Filter "*.dll"
        Get-Item -LiteralPath (Join-Path $BuildOutputDirectory "SW2URDF.png")
        Get-Item -LiteralPath (Join-Path $BuildOutputDirectory "LICENSE")
        Get-Item -LiteralPath (Join-Path $BuildOutputDirectory "THIRD_PARTY_NOTICES.md")
        Get-ChildItem -LiteralPath (Join-Path $BuildOutputDirectory "THIRD_PARTY_LICENSES") `
            -File
        Get-ChildItem -LiteralPath (Join-Path $BuildOutputDirectory "images") `
            -File -Filter "*.png"
        $InstallerSchemaFiles
        Get-ChildItem -LiteralPath (Join-Path $BuildOutputDirectory "tools\usd_adapter") `
            -File -Recurse
        Get-ChildItem -LiteralPath (Join-Path $BuildOutputDirectory "tools\openusd_runtime") `
            -File -Recurse
        Get-ChildItem -LiteralPath (Join-Path $BuildOutputDirectory "tools\mujoco_runtime") `
            -File -Recurse
        Get-Item -LiteralPath (Join-Path $BuildOutputDirectory "tools\openusd_runtime.lock.json")
        Get-Item -LiteralPath (Join-Path $BuildOutputDirectory "tools\mujoco_runtime.lock.json")
    )
    $RequiredPayloadFiles = @(
        "SW2URDF.dll",
        "OSURDF.Core.dll",
        "Newtonsoft.Json.dll",
        "log4net.dll",
        "APACHE-2.0.txt",
        "MIT.txt",
        "osurdf_usd_adapter.py",
        "python.exe",
        "compile.exe",
        "testspeed.exe",
        "mujoco.dll",
        "openusd_runtime.lock.json",
        "mujoco_runtime.lock.json",
        "robot.schema.v2.json",
        "solidworkstools.dll"
    )
    foreach ($RequiredPayloadFile in $RequiredPayloadFiles) {
        if (-not ($PayloadCandidates | Where-Object { $_.Name -eq $RequiredPayloadFile })) {
            throw "Release build did not produce required installer payload: $RequiredPayloadFile"
        }
    }

    # Build the test-bearing plugin configuration and run the deterministic,
    # non-Live plugin suite against that exact DLL before packaging.
    & $NuGet restore (Join-Path $BuildRoot "SW2URDF\packages.config") `
        -PackagesDirectory $PackagesDirectory -ConfigFile $NuGetConfig `
        -NoHttpCache -DirectDownload -NonInteractive
    if ($LASTEXITCODE -ne 0) {
        throw "NuGet restore for the plugin regression suite failed with exit code $LASTEXITCODE."
    }
    $LegacyTestPackageInputs = @($LegacyTestLock.packages | ForEach-Object {
        $PackageNames = @(
            (([string]$_.id) + "." + ([string]$_.version) + ".nupkg")
        )
        if (-not [string]::IsNullOrWhiteSpace([string]$_.archiveVersion)) {
            $PackageNames += (([string]$_.id) + "." +
                ([string]$_.archiveVersion) + ".nupkg")
        }
        $PackageFiles = @(Get-ChildItem -LiteralPath $PackagesDirectory -Recurse -File |
            Where-Object { $PackageNames -contains $_.Name })
        if ($PackageFiles.Count -ne 1) {
            throw "Legacy package archive was not restored exactly once: $($_.id) $($_.version)"
        }
        $ActualHash = Get-Sha256 $PackageFiles[0].FullName
        if ($ActualHash -cne ([string]$_.sha256).ToLowerInvariant()) {
            throw "Legacy test package $($_.id) $($_.version) does not match the content lock."
        }
        [ordered]@{
            id = [string]$_.id
            version = [string]$_.version
            sha256 = $ActualHash
        }
    })
    $ExpectedRestoredCoordinates = @(
        (@($ReleaseLock.packages) + @($LegacyTestLock.packages)) | ForEach-Object {
            ([string]$_.id).ToLowerInvariant() + "`t" + ([string]$_.version).ToLowerInvariant()
        } | Sort-Object -Unique
    )
    $RestoredPackageCount = @(Get-ChildItem -LiteralPath $PackagesDirectory `
        -Recurse -File -Filter "*.nupkg").Count
    if ($RestoredPackageCount -ne $ExpectedRestoredCoordinates.Count) {
        throw "Legacy test restore produced package archives not covered by a content lock."
    }
    $TestBaseIntermediateOutputPath = Join-Path $BuildRoot ".codex-build\SW2URDF-test\obj"
    $TestIntermediateOutputPath = Join-Path $TestBaseIntermediateOutputPath "$Platform\Test"
    $TestBaseIntermediateOutputPath = $TestBaseIntermediateOutputPath.TrimEnd("\") + "\"
    $TestIntermediateOutputPath = $TestIntermediateOutputPath.TrimEnd("\") + "\"
    & $MSBuild $Project /p:Configuration=Test /p:Platform=$Platform `
        /p:RegisterForComInterop=false /p:PostBuildEvent= `
        /p:SolidWorksInstallDir="$StagedSolidWorksDirectory" `
        "/p:SolutionDir=$SolutionDir" `
        "/p:SW2URDFBaseIntermediateOutputPath=$TestBaseIntermediateOutputPath" `
        "/p:SW2URDFIntermediateOutputPath=$TestIntermediateOutputPath" `
        "/p:RestoreConfigFile=$NuGetConfig" `
        "/p:RestorePackagesPath=$SdkPackagesDirectory" `
        /p:RestoreLockedMode=true /restore
    if ($LASTEXITCODE -ne 0) {
        throw "SolidWorks test assembly build failed with exit code $LASTEXITCODE."
    }

    $TestRunnerProject = Join-Path $BuildRoot "TestRunner\TestRunner.csproj"
    & $DotNet restore $TestRunnerProject --locked-mode `
        --configfile $NuGetConfig --packages $SdkPackagesDirectory
    if ($LASTEXITCODE -ne 0) {
        throw "TestRunner locked restore failed with exit code $LASTEXITCODE."
    }
    & $DotNet build $TestRunnerProject --configuration Release --no-restore `
        "-p:SolidWorksInstallDir=$StagedSolidWorksDirectory" `
        "-p:RestorePackagesPath=$SdkPackagesDirectory"
    if ($LASTEXITCODE -ne 0) {
        throw "TestRunner build failed with exit code $LASTEXITCODE."
    }

    $TestAssembly = Join-Path $BuildRoot "SW2URDF\bin\$Platform\Test\SW2URDF.dll"
    $TestRunnerExecutable = Join-Path $BuildRoot "TestRunner\bin\Release\net48\TestRunner.exe"
    if (-not (Test-Path -LiteralPath $TestAssembly -PathType Leaf) -or
        -not (Test-Path -LiteralPath $TestRunnerExecutable -PathType Leaf)) {
        throw "Plugin regression test outputs were not produced."
    }
    $PreviousTestAssembly = $env:SW2URDF_TEST_ASSEMBLY
    try {
        $env:SW2URDF_TEST_ASSEMBLY = $TestAssembly
        # Native SolidWorks COM suites are intentionally separate, explicit evidence.
        # A traceable installer build runs only deterministic tests and must not
        # launch or mutate a developer's locally installed CAD session.
        & $TestRunnerExecutable --exclude-live-solidworks
        if ($LASTEXITCODE -ne 0) {
            throw "Deterministic plugin regression suite failed with exit code $LASTEXITCODE."
        }
    }
    finally {
        if ($null -eq $PreviousTestAssembly) {
            Remove-Item Env:\SW2URDF_TEST_ASSEMBLY -ErrorAction SilentlyContinue
        }
        else {
            $env:SW2URDF_TEST_ASSEMBLY = $PreviousTestAssembly
        }
    }
    $PluginTestEvidence = [ordered]@{
        configuration = "Test"
        platform = $Platform
        testAssemblySha256 = Get-Sha256 $TestAssembly
        runner = "TestRunner/bin/Release/net48/TestRunner.exe"
        selection = "exclude-live-solidworks"
        result = "passed"
        deepReferenceLiveApi = "not_requested"
        crossVersionSolidWorks = "not_run"
    }

    $PayloadRoot = $BuildOutputDirectory.TrimEnd("\") + "\"
    $PayloadInputs = @($PayloadCandidates |
        Sort-Object -Property FullName -Unique |
        ForEach-Object {
            $ResolvedPayload = Assert-ChildPath $BuildOutputDirectory $_.FullName `
                "Installer payload"
            [ordered]@{
                path = $ResolvedPayload.Substring($PayloadRoot.Length).Replace("\", "/")
                sha256 = Get-Sha256 $ResolvedPayload
            }
        })

    Assert-NoPythonBytecode $BuildOutputDirectory
    & $ISCC "/DInstallerDate=$InstallerDate" "/DInstallerCommit=$InstallerCommit" `
        "/DBuildConfiguration=$Configuration" "/DBuildPlatform=$Platform" $InstallerScript
    if ($LASTEXITCODE -ne 0) {
        throw "Inno Setup compiler failed with exit code $LASTEXITCODE."
    }

    $BuiltInstallerPath = Join-Path $BuildRoot "INSTALL\OUTPUT\$InstallerFileName"
    if (-not (Test-Path -LiteralPath $BuiltInstallerPath)) {
        throw "Inno Setup did not create the expected installer: $BuiltInstallerPath"
    }

    $BuildChanges = & git -C $BuildRoot status --porcelain --untracked-files=normal -- . `
        ":(exclude).solidworks-api" `
        ":(exclude).nuget-packages" `
        ":(exclude).codex-build" `
        ":(exclude)packages" `
        ":(exclude)SW2URDF/bin" `
        ":(exclude)src/**/bin" `
        ":(exclude)src/**/obj" `
        ":(exclude)INSTALL/OUTPUT" 2>$null
    $BuildStatusExitCode = $LASTEXITCODE
    $PostBuildHead = & git -C $BuildRoot rev-parse HEAD 2>$null
    $BuildHeadExitCode = $LASTEXITCODE
    if ($BuildStatusExitCode -ne 0 -or $BuildHeadExitCode -ne 0 -or
        [string]::IsNullOrWhiteSpace($PostBuildHead) -or
        $PostBuildHead.Trim() -ne $SourceCommit -or
        -not [string]::IsNullOrWhiteSpace(($BuildChanges -join "`n"))) {
        throw "The immutable build worktree changed during packaging."
    }

    $PostBuildCommit = & git -c "safe.directory=$SafeRepoRoot" -C $SourceRepoRoot `
        rev-parse HEAD 2>$null
    $SourceHeadExitCode = $LASTEXITCODE
    $PostBuildChanges = & git -c "safe.directory=$SafeRepoRoot" -C $SourceRepoRoot status `
        --porcelain --untracked-files=normal -- . ":(exclude)INSTALL/OUTPUT" 2>$null
    $SourceStatusExitCode = $LASTEXITCODE
    if ($SourceHeadExitCode -ne 0 -or $SourceStatusExitCode -ne 0 -or
        [string]::IsNullOrWhiteSpace($PostBuildCommit) -or
        $PostBuildCommit.Trim() -ne $SourceCommit -or
        -not [string]::IsNullOrWhiteSpace(($PostBuildChanges -join "`n"))) {
        throw "Source changed during packaging. The installer was not promoted."
    }

    New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
    Copy-Item -LiteralPath $BuiltInstallerPath -Destination $InstallerPath -Force
    $InstallerSha256 = Get-Sha256 $InstallerPath
    $InstallerRelativePath = "INSTALL/OUTPUT/$InstallerFileName"
    [System.IO.File]::WriteAllText(
        $Sha256Path,
        "$InstallerSha256  $InstallerRelativePath`n",
        $Utf8NoBom)

    $Provenance = [ordered]@{
        schemaVersion = 2
        trustModel = "local-build-from-immutable-git-worktree"
        evidenceModel = "traceable-build-inputs-not-bit-reproducible"
        sourceCommit = $SourceCommit
        sourceTree = $SourceTree
        sourceCommitShort = $InstallerCommit
        installerDate = $InstallerDate
        installerFile = $InstallerFileName
        installerSha256 = $InstallerSha256
        configuration = $Configuration
        platform = $Platform
        tools = [ordered]@{
            nuget = [ordered]@{
                version = [string]$ReleaseLock.nuget.version
                sha256 = Get-Sha256 $NuGet
            }
            msbuild = [ordered]@{
                fileVersion =
                    [System.Diagnostics.FileVersionInfo]::GetVersionInfo($MSBuild).FileVersion
                sha256 = Get-Sha256 $MSBuild
            }
            innoSetup = [ordered]@{
                version = $InnoVersionMatch.Groups['version'].Value
                sha256 = Get-Sha256 $ISCC
            }
            dotnet = [ordered]@{
                version = $DotNetVersion
                sha256 = Get-Sha256 $DotNet
            }
        }
        nugetSource = "https://api.nuget.org/v3/index.json"
        packageInputs = $PackageInputs
        sdkPackageLocks = $SdkPackageLocks
        legacyTestPackageLock = [ordered]@{
            path = "scripts/legacy-test-packages.lock.json"
            sha256 = Get-NormalizedTextSha256 $LegacyTestLockPath
            packagesConfigPath = [string]$LegacyTestLock.packagesConfig.path
            packagesConfigSha256 =
                ([string]$LegacyTestLock.packagesConfig.sha256).ToLowerInvariant()
        }
        legacyTestPackageInputs = $LegacyTestPackageInputs
        solidWorksInputs = $SolidWorksInputs
        assetRuntimeInputs = $AssetRuntimeInputs
        coreTests = $CoreTestEvidence
        pluginTests = $PluginTestEvidence
        payloadInputs = $PayloadInputs
    }
    [System.IO.File]::WriteAllText(
        $ProvenancePath,
        (($Provenance | ConvertTo-Json -Depth 8) + "`n"),
        $Utf8NoBom)
    $BuildSucceeded = $true

    Write-Output "Installer: $InstallerPath"
    Write-Output "SHA256: $InstallerSha256"
    Write-Output "Provenance: $ProvenancePath"
}
finally {
    if ($DotNetEnvironmentConfigured) {
        Restore-EnvironmentVariable "DOTNET_ROOT" $PreviousDotNetRoot
        Restore-EnvironmentVariable "MSBuildSDKsPath" $PreviousMSBuildSDKsPath
        Restore-EnvironmentVariable `
            "MSBuildEnableWorkloadResolver" `
            $PreviousMSBuildEnableWorkloadResolver
    }
    if ($WorktreeAdded) {
        & git -c "safe.directory=$SafeRepoRoot" -C $SourceRepoRoot worktree remove `
            --force $BuildRoot 2>$null
        if ($LASTEXITCODE -ne 0) {
            Write-Warning "Git could not remove the temporary build worktree cleanly: $BuildRoot"
        }
    }
    if (Test-Path -LiteralPath $BuildRoot) {
        $SafeBuildRoot = Assert-ChildPath $TempRoot $BuildRoot "Temporary build worktree"
        Remove-Item -LiteralPath $SafeBuildRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
    if (-not $BuildSucceeded) {
        Remove-Item -LiteralPath $InstallerPath -Force -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath $Sha256Path -Force -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath $ProvenancePath -Force -ErrorAction SilentlyContinue
    }
}
