param(
    [string]$Configuration = "Release",
    [string]$Platform = "x64",
    [string]$SolidWorksInstallDir = "E:\Solidworks 2023\SOLIDWORKS Crop\SOLIDWORKS"
)

$ErrorActionPreference = "Stop"

function Get-Sha256([string]$Path) {
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
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

if ($Configuration -ne "Release" -or $Platform -ne "x64") {
    throw "The installer supports only Configuration=Release and Platform=x64."
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
    $MSBuild = & $VsWhere -latest -requires Microsoft.Component.MSBuild `
        -find "MSBuild\**\Bin\MSBuild.exe" | Select-Object -First 1
}
if (-not $MSBuild -and (Test-Path -LiteralPath $FrameworkMSBuild)) {
    $MSBuild = $FrameworkMSBuild
}
if (-not $MSBuild) {
    throw "MSBuild was not found. Install Visual Studio Build Tools or the .NET Framework MSBuild tools."
}
$MSBuild = [System.IO.Path]::GetFullPath($MSBuild)

$ISCC = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles}\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $ISCC) {
    throw "Inno Setup compiler was not found. Install Inno Setup 6.3 or newer."
}
$ISCC = [System.IO.Path]::GetFullPath($ISCC)
$InnoWhatsNew = Join-Path (Split-Path -Parent $ISCC) "whatsnew.htm"
$InnoVersionMatch = if (Test-Path -LiteralPath $InnoWhatsNew) {
    [regex]::Match(
        [System.IO.File]::ReadAllText($InnoWhatsNew),
        '<summary>Inno Setup (?<version>[0-9]+\.[0-9]+)')
} else {
    $null
}
if ($null -eq $InnoVersionMatch -or -not $InnoVersionMatch.Success -or
    ([version]$InnoVersionMatch.Groups['version'].Value) -lt [version]'6.3') {
    throw "Inno Setup 6.3 or newer is required by the x64compatible installer target."
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
        "/p:BaseIntermediateOutputPath=$BaseIntermediateOutputPath" `
        "/p:IntermediateOutputPath=$IntermediateOutputPath"
    if ($LASTEXITCODE -ne 0) {
        throw "MSBuild failed with exit code $LASTEXITCODE."
    }

    $PayloadCandidates = @(
        Get-ChildItem -LiteralPath $BuildOutputDirectory -File -Filter "*.dll"
        Get-Item -LiteralPath (Join-Path $BuildOutputDirectory "SW2URDF.png")
        Get-ChildItem -LiteralPath (Join-Path $BuildOutputDirectory "images") `
            -File -Filter "*.png"
    )
    if (-not ($PayloadCandidates | Where-Object { $_.Name -eq "SW2URDF.dll" })) {
        throw "Release build did not produce SW2URDF.dll."
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

    & $ISCC "/DInstallerDate=$InstallerDate" "/DInstallerCommit=$InstallerCommit" `
        "/DBuildConfiguration=$Configuration" "/DBuildPlatform=$Platform" $InstallerScript
    if ($LASTEXITCODE -ne 0) {
        throw "Inno Setup compiler failed with exit code $LASTEXITCODE."
    }

    $BuiltInstallerPath = Join-Path $BuildRoot "INSTALL\OUTPUT\$InstallerFileName"
    if (-not (Test-Path -LiteralPath $BuiltInstallerPath)) {
        throw "Inno Setup did not create the expected installer: $BuiltInstallerPath"
    }

    $BuildChanges = & git -C $BuildRoot status --porcelain --untracked-files=no 2>$null
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
        }
        nugetSource = "https://api.nuget.org/v3/index.json"
        packageInputs = $PackageInputs
        solidWorksInputs = $SolidWorksInputs
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
