param(
    [Parameter(Mandatory = $true)][string]$Installer,
    [Parameter(Mandatory = $true)][ValidatePattern('^[0-9a-fA-F]{64}$')][string]$InstallerSha256,
    [Parameter(Mandatory = $true)][string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
# This script installs software. Never run its installer steps on a developer host.
if ($env:GITHUB_ACTIONS -ne 'true' -or $env:SW2URDF_RUNNER_ENVIRONMENT -ne 'github-hosted') {
    throw 'Installation is restricted to a fresh GitHub-hosted runner.'
}
$Installer = (Resolve-Path -LiteralPath $Installer).Path
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$utf8 = New-Object Text.UTF8Encoding($false)
function Save-Json($Value, [string]$Name) {
    [IO.File]::WriteAllText((Join-Path $OutputDirectory $Name), ($Value | ConvertTo-Json -Depth 12), $utf8)
}
function Hash([string]$Path) { (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() }
function Get-VcState {
    $rows = foreach ($name in @('MSVCP140.dll', 'MSVCP140_1.dll', 'VCOMP140.dll', 'VCRUNTIME140.dll', 'VCRUNTIME140_1.dll')) {
        $path = Join-Path "$env:SystemRoot\System32" $name
        if (Test-Path -LiteralPath $path) {
            $item = Get-Item -LiteralPath $path
            [ordered]@{ name = $name; path = $path; version = $item.VersionInfo.FileVersion; sha256 = (Hash $path) }
        } else { [ordered]@{ name = $name; missing = $true } }
    }
    return @($rows)
}
$actual = Hash $Installer
if ($actual -ne $InstallerSha256.ToLowerInvariant()) { throw 'Installer SHA256 mismatch.' }
Save-Json ([ordered]@{
    scope = 'Fresh GitHub-hosted Windows Server VM; preinstalled development tools exist. This is not a bare Windows desktop qualification.'
    os = (Get-CimInstance Win32_OperatingSystem | Select-Object Caption, Version, BuildNumber, OSArchitecture)
    imageOS = $env:ImageOS; imageVersion = $env:ImageVersion
    runnerOS = $env:RUNNER_OS; runnerArch = $env:RUNNER_ARCH
    workflowCommit = $env:GITHUB_SHA; runId = $env:GITHUB_RUN_ID
    installerName = [IO.Path]::GetFileName($Installer); installerSha256 = $actual
    vcBefore = @(Get-VcState)
}) 'environment.json'

$redist = Join-Path $OutputDirectory 'vc_redist.x64.exe'
$redistHash = '843068991daaa1f73ad9f6239bce4d0f6a07a51f18c37ea2a867e9beca71295c'
$redistVersion = [version]'14.51.36247.0'
# Official latest-supported permalink resolved on 2026-09-13; pin immutable URL,
# digest, file version and Authenticode together for reproducible qualification.
$redistUrl = 'https://download.visualstudio.microsoft.com/download/pr/ebdab8e5-1d7b-4d9f-a11b-cbb1720c3b12/843068991DAAA1F73AD9F6239BCE4D0F6A07A51F18C37EA2A867E9BECA71295C/VC_redist.x64.exe'
Invoke-WebRequest -Uri $redistUrl -OutFile $redist
if ((Hash $redist) -ne $redistHash) { throw 'Microsoft redist changed; review the new supported version before updating its pin.' }
$signature = Get-AuthenticodeSignature -LiteralPath $redist
if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch '(^|, )O=Microsoft Corporation(,|$)') { throw 'Microsoft redist signature validation failed.' }
if ([version](Get-Item $redist).VersionInfo.FileVersion -ne $redistVersion) { throw 'Microsoft redist version mismatch.' }
$redistLog = Join-Path $OutputDirectory 'vc-redist.log'
$vc = Start-Process -FilePath $redist -ArgumentList @('/install', '/quiet', '/norestart', '/log', "`"$redistLog`"") -Wait -PassThru -WindowStyle Hidden
Save-Json ([ordered]@{ version = $redistVersion.ToString(); url = $redistUrl; sha256 = $redistHash; signature = $signature.Status.ToString(); exitCode = $vc.ExitCode; vcAfter = @(Get-VcState) }) 'vc-install.json'
# 3010 means a reboot is required; do not silently call that a completed qualification.
if ($vc.ExitCode -ne 0) { throw "Pinned VC++ prerequisite did not complete without reboot: $($vc.ExitCode)" }

$bundle = Join-Path $OutputDirectory 'installed'
$installLog = Join-Path $OutputDirectory 'installer.log'
$setup = Start-Process -FilePath $Installer -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-', "/DIR=`"$bundle`"", "/LOG=`"$installLog`"") -Wait -PassThru -WindowStyle Hidden
$registrationLines = @(Get-Content -LiteralPath $installLog | Where-Object { $_ -match 'RegAsm|Process exit code|Registering' })
Save-Json ([ordered]@{ exitCode = $setup.ExitCode; registrationLog = $registrationLines; scope = 'Runtime payload qualification only; SolidWorks COM activation is not asserted.' }) 'installer-result.json'
if ($setup.ExitCode -ne 0) { throw "Release installer failed: $($setup.ExitCode)" }
$python = Join-Path $bundle 'tools/openusd_runtime/python.exe'
if (-not (Test-Path -LiteralPath $python)) { throw 'Installed embedded Python missing.' }
Save-Json (@(Get-ChildItem -LiteralPath $bundle -File -Recurse | ForEach-Object {
    [ordered]@{ path = $_.FullName.Substring($bundle.Length + 1); bytes = $_.Length; sha256 = (Hash $_.FullName) }
})) 'installed-payload.json'

# Only Windows system locations are searchable; -I and _pth isolate Python imports.
$env:PATH = "$env:SystemRoot\System32;$env:SystemRoot"
foreach ($entry in @(Get-ChildItem Env: | Where-Object { $_.Name -match '^(PYTHON|QT_|QML|PYSIDE|CONDA|VIRTUAL_ENV)' })) {
    Remove-Item -LiteralPath ("Env:" + $entry.Name)
}
$probe = Join-Path $PSScriptRoot 'release_runtime_probe.py'
$probeOutput = Join-Path $OutputDirectory 'probe'
& $python -I -B $probe probe --bundle $bundle --output $probeOutput 1> (Join-Path $OutputDirectory 'probe.stdout.txt') 2> (Join-Path $OutputDirectory 'probe.stderr.txt')
if ($LASTEXITCODE -ne 0) { throw 'Packaged runtime probe failed. Inspect retained probe logs.' }
$vcModules = foreach ($file in Get-ChildItem -LiteralPath $probeOutput -Filter '*.modules.json') {
    foreach ($module in @(Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json)) {
        if ($module.name -match '^(MSVCP140(_1)?|VCOMP140|VCRUNTIME140(_1)?)\.dll$') {
            [ordered]@{ probe = $file.Name; name = $module.name; path = $module.path; origin = $module.origin; version = (Get-Item -LiteralPath $module.path).VersionInfo.FileVersion; sha256 = $module.sha256 }
        }
    }
}
Save-Json @($vcModules) 'loaded-vc-modules.json'
Save-Json ([ordered]@{ passed = $true; installerSha256 = $actual; prerequisite = "Microsoft Visual C++ x64 $redistVersion"; scope = 'Fresh Windows Server runner with explicit supported VC prerequisite and sanitized DLL/import environment. Not a bare Windows desktop or SolidWorks integration test.' }) 'qualification.json'
Write-Output 'PASS: fresh Windows Server release runtime qualification. Bare Windows desktop and SolidWorks qualification are separate.'
