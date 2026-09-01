param()

$ErrorActionPreference = "Stop"

function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) {
        throw $Message
    }
}

function Assert-Contains([string]$Text, [string]$Expected, [string]$Message) {
    Assert-True ($Text.Contains($Expected)) $Message
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

function Assert-PowerShellSyntax([string]$Path) {
    $Tokens = $null
    $Errors = $null
    [void][System.Management.Automation.Language.Parser]::ParseFile(
        $Path,
        [ref]$Tokens,
        [ref]$Errors)
    Assert-True ($Errors.Count -eq 0) `
        ("PowerShell parser errors in {0}: {1}" -f $Path, ($Errors -join "; "))
}

$RepoRoot = Split-Path -Parent $PSScriptRoot
$BuildInstallerPath = Join-Path $RepoRoot "scripts/BuildInstaller.ps1"
$PublishWorkflowPath = Join-Path $RepoRoot ".github/workflows/publish-installer-release.yml"
$InstallScriptPath = Join-Path $RepoRoot "INSTALL/Install.iss"
$SchemaReadmePath = Join-Path $RepoRoot "schemas/README.md"
$LegacyLockPath = Join-Path $RepoRoot "scripts/legacy-test-packages.lock.json"
$PackagesConfigPath = Join-Path $RepoRoot "SW2URDF/packages.config"

Assert-PowerShellSyntax $BuildInstallerPath
Assert-PowerShellSyntax $PSCommandPath

$BuildInstaller = [System.IO.File]::ReadAllText($BuildInstallerPath)
Assert-Contains $BuildInstaller '$RequiredDotNetVersion -ne "8.0.424"' `
    "BuildInstaller must reject SDK versions other than 8.0.424."
Assert-Contains $BuildInstaller '$env:PYTHONDONTWRITEBYTECODE = "1"' `
    "Bundled Python must disable bytecode writes."
Assert-Contains $BuildInstaller '& $OpenUsdPython -B -c' `
    "Bundled Python imports must use -B."
Assert-Contains $BuildInstaller '& $OpenUsdPython -B $OpenUsdAdapter' `
    "Bundled Python adapter execution must use -B."
Assert-Contains $BuildInstaller 'Assert-NoPythonBytecode $BuildOutputDirectory' `
    "Installer staging must reject Python bytecode caches."
Assert-Contains $BuildInstaller 'tests\OSURDF.Core.Tests\packages.lock.json' `
    "Core test package lock must be recorded in provenance."
Assert-Contains $BuildInstaller 'traceable-build-inputs-not-bit-reproducible' `
    "Build provenance must state its traceability scope."

$LegacyLock = Get-Content -LiteralPath $LegacyLockPath -Raw -Encoding UTF8 |
    ConvertFrom-Json
Assert-True ($LegacyLock.schemaVersion -eq 1) "Legacy package lock schema is invalid."
Assert-True ($LegacyLock.packagesConfig.path -ceq "SW2URDF/packages.config") `
    "Legacy package lock points at the wrong packages.config."
Assert-True ((Get-NormalizedTextSha256 $PackagesConfigPath) -ceq
    ([string]$LegacyLock.packagesConfig.sha256).ToLowerInvariant()) `
    "Legacy packages.config normalized hash does not match its content lock."
[xml]$PackagesConfig = Get-Content -LiteralPath $PackagesConfigPath -Raw -Encoding UTF8
$ConfigCoordinates = @($PackagesConfig.packages.package | ForEach-Object {
    (([string]$_.id).ToLowerInvariant() + "|" + [string]$_.version)
} | Sort-Object)
$LockCoordinates = @($LegacyLock.packages | ForEach-Object {
    Assert-True ([string]$_.sha256 -match '^[0-9a-fA-F]{64}$') `
        "Legacy package lock contains an invalid SHA256."
    (([string]$_.id).ToLowerInvariant() + "|" + [string]$_.version)
} | Sort-Object)
Assert-True ($ConfigCoordinates.Count -eq 24) `
    "Legacy packages.config must contain the expected 24 locked packages."
Assert-True (($ConfigCoordinates -join "`n") -ceq ($LockCoordinates -join "`n")) `
    "Legacy package lock must exactly cover packages.config coordinates."

$WorkflowFiles = @(Get-ChildItem -LiteralPath (Join-Path $RepoRoot ".github/workflows") `
    -File -Filter "*.yml")
$SdkReferences = 0
foreach ($WorkflowFile in $WorkflowFiles) {
    $Workflow = [System.IO.File]::ReadAllText($WorkflowFile.FullName)
    $Matches = [regex]::Matches(
        $Workflow,
        '(?m)^\s*dotnet-version:\s*["'']?([^"''\s]+)')
    foreach ($Match in $Matches) {
        $SdkReferences++
        Assert-True ($Match.Groups[1].Value -ceq "8.0.424") `
            ("{0} uses non-exact SDK {1}." -f $WorkflowFile.Name, $Match.Groups[1].Value)
    }
}
Assert-True ($SdkReferences -gt 0) "No workflow .NET SDK references were found."

$PublishWorkflow = [System.IO.File]::ReadAllText($PublishWorkflowPath)
$PublishHeader = $PublishWorkflow.Substring(0, $PublishWorkflow.IndexOf("permissions:"))
Assert-Contains $PublishHeader "workflow_dispatch:" `
    "Release candidate workflow must be manually dispatched."
Assert-True ($PublishHeader -notmatch '(?m)^  (push|schedule|release):') `
    "Release candidate workflow must not have an automatic trigger."
Assert-Contains $PublishWorkflow '.tools.dotnet.version == "8.0.424"' `
    "Release provenance must verify SDK 8.0.424 exactly."
Assert-Contains $PublishWorkflow 'tests/OSURDF.Core.Tests/packages.lock.json' `
    "Release provenance must require the Core Tests package lock."
Assert-Contains $PublishWorkflow 'legacy-test-packages.lock.json' `
    "Release provenance must verify the legacy package content lock."
Assert-Contains $PublishWorkflow 'traceable-build-inputs-not-bit-reproducible' `
    "Release provenance must enforce the traceability evidence model."
Assert-Contains $PublishWorkflow 'fact_placeholders = (' `
    "Release Notes must enforce one shared factual placeholder set."
Assert-Contains $PublishWorkflow 'Release notes title must contain English and Simplified Chinese.' `
    "Release Notes must enforce a bilingual title."
Assert-Contains $PublishWorkflow '--draft' `
    "GitHub Release creation must remain Draft-only."
Assert-True ($PublishWorkflow -notmatch 'gh release edit|--draft[= ]false') `
    "Release workflow must never publish or convert a Draft to public."

$InstallScript = [System.IO.File]::ReadAllText($InstallScriptPath)
$SchemaSources = @($InstallScript -split '\r?\n' | Where-Object {
    $_ -match '^Source:.*\\schemas\\'
})
Assert-True ($SchemaSources.Count -gt 0) "Installer has no explicit schema payload."
Assert-True (@($SchemaSources | Where-Object {
    $_ -match 'isaac-profile|isaaclab-profile|isaaclab-actuator-profile|\\schemas\\\*'
}).Count -eq 0) "Installer must exclude internal legacy Isaac schemas."

$SchemaReadme = [System.IO.File]::ReadAllText($SchemaReadmePath)
Assert-Contains $SchemaReadme "internal legacy compatibility contracts" `
    "Schema documentation must classify Isaac contracts as internal legacy."
Assert-True ($SchemaReadme -match
    'are not installed\s+and are not loaded by the\s+current SolidWorks UI') `
    "Schema documentation must deny legacy Isaac schemas as UI entrypoints."

$ReleaseProcess = [System.IO.File]::ReadAllText(
    (Join-Path $RepoRoot "docs/wiki/Release-Process.md"))
$ReleaseProcessZh = [System.IO.File]::ReadAllText(
    (Join-Path $RepoRoot "docs/wiki/Release-Process-zh-CN.md"))
Assert-Contains $ReleaseProcess "does not claim bit-for-bit reproducibility" `
    "English release documentation must avoid reproducibility overclaims."
Assert-Contains $ReleaseProcessZh "bit-for-bit reproducibility" `
    "Chinese release documentation must avoid reproducibility overclaims."

Write-Output "Release gate static validation passed."
