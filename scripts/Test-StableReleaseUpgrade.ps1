param(
    [Parameter(Mandatory = $true)][string]$DownloadDirectory,
    [Parameter(Mandatory = $true)][string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ($env:GITHUB_ACTIONS -ne 'true' -or $env:SW2URDF_RUNNER_ENVIRONMENT -ne 'github-hosted') { throw 'Stable upgrade runs only on a disposable GitHub-hosted VM.' }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory).TrimEnd('\', '/')
$temp = [IO.Path]::GetFullPath($env:RUNNER_TEMP).TrimEnd('\', '/')
if (-not $OutputDirectory.StartsWith($temp + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Upgrade destination must be inside RUNNER_TEMP.' }
$bundle = Join-Path $OutputDirectory 'installed'
if (Test-Path -LiteralPath $bundle) { throw 'Stable upgrade requires a new VM destination; no preexisting installation may be replaced.' }
$upgradeEvidence = Join-Path $OutputDirectory 'stable-upgrade'
New-Item -ItemType Directory -Path $upgradeEvidence -Force | Out-Null
$stableName = 'sw2urdfSetup_20260906_fc88a14.exe'
$candidateName = 'sw2urdfSetup_20260911_a0af2c5.exe'
$stableHash = 'da18423ec7087fa775c2b65d2d48b72f557005aa88377e95f4c31b04f5ed3334'
$candidateHash = 'bf7c25db6779b9eb1d6c4b1bfa1d369f4155e1c9a1799320283a9b2bc245eb0f'
$stable = Join-Path $DownloadDirectory $stableName
$candidate = Join-Path $DownloadDirectory $candidateName
$state = [ordered]@{ passed = $false; fromRelease = 'v20260906'; fromSource = 'fc88a146747ec5de444add4b57c6d2b4705ef82d'; fromInstallerSha256 = $stableHash; toSource = 'a0af2c5a1ea934fe027fb935df94516880c7a33f'; toInstallerSha256 = $candidateHash; scope = 'Published stable to exact candidate upgrade in place on a separate Windows Server VM; no local UAC or SolidWorks activation qualification.' }
function Upgrade-Hash([string]$Path) { (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() }
function Save-Upgrade($Value, [string]$Name) { [IO.File]::WriteAllText((Join-Path $upgradeEvidence $Name), ($Value | ConvertTo-Json -Depth 20), (New-Object Text.UTF8Encoding($false))) }
function No-SolidWorks {
    if (@(Get-Process -Name SLDWORKS -ErrorAction SilentlyContinue).Count) { throw 'SolidWorks is running; no process will be closed automatically.' }
}
function Upgrade-Key([Microsoft.Win32.RegistryHive]$Hive, [string]$Path) {
    $base = [Microsoft.Win32.RegistryKey]::OpenBaseKey($Hive, [Microsoft.Win32.RegistryView]::Registry64)
    try {
        $key = $base.OpenSubKey($Path, $false)
        if ($null -eq $key) { return $null }
        try { $r = [ordered]@{}; foreach ($name in $key.GetValueNames()) { $r[$name] = $key.GetValue($name) }; return $r }
        finally { $key.Dispose() }
    } finally { $base.Dispose() }
}
function Upgrade-Registration {
    $id = '{65c9fc17-6a74-45a3-8f84-55185900275d}'
    [ordered]@{
        clsid = (Upgrade-Key LocalMachine ('SOFTWARE\Classes\CLSID\' + $id))
        inproc = (Upgrade-Key LocalMachine ('SOFTWARE\Classes\CLSID\' + $id + '\InprocServer32'))
        addin = (Upgrade-Key LocalMachine ('SOFTWARE\SolidWorks\Addins\' + $id))
        startup = (Upgrade-Key CurrentUser ('Software\SolidWorks\AddInsStartup\' + $id))
        arp = (Upgrade-Key LocalMachine 'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{E43E85A9-071D-430A-91B2-84B7AB923170}_is1')
    }
}
function Assert-UpgradeExit([int]$ExitCode, [string]$Log, [string]$Label) {
    $text = [IO.File]::ReadAllText($Log)
    $codes = @([regex]::Matches($text, 'Process exit code:\s*(-?\d+)') | ForEach-Object { [int]$_.Groups[1].Value })
    $result = [ordered]@{ exitCode = $ExitCode; childExitCodes = $codes; regAsmPresent = ($text -match 'RegAsm\.exe') }
    Save-Upgrade $result ($Label + '-exit.json')
    if ($ExitCode -ne 0 -or -not $result.regAsmPresent -or -not $codes.Count -or @($codes | Where-Object { $_ -ne 0 }).Count) { throw "$Label installer/RegAsm exit failure." }
}
function Read-UpgradeManifest([string]$Path, [string]$Hash, [string]$Source, [string]$InstallerDigest, [int]$Count) {
    if ((Upgrade-Hash $Path) -ne $Hash) { throw 'Pinned provenance SHA256 mismatch.' }
    $m = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
    if ($m.sourceCommit -ne $Source -or $m.installerSha256 -ne $InstallerDigest -or $m.payloadInputs.Count -ne $Count) { throw 'Provenance identity or payload count mismatch.' }
    $files = @{}
    foreach ($row in $m.payloadInputs) {
        $path = ([string]$row.path).Replace('/', '\')
        if ([IO.Path]::IsPathRooted($path) -or $path -match '(^|\\)\.\.(\\|$)|:' -or $files.ContainsKey($path)) { throw 'Unsafe or duplicate manifest path.' }
        $files[$path] = [string]$row.sha256
    }
    return $files
}
function Assert-UpgradePayload($Expected, [string]$Label) {
    No-SolidWorks
    $files = @()
    foreach ($entry in Get-ChildItem -LiteralPath $bundle -Recurse -Force) {
        if ($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Reparse point in VM installation.' }
        if (-not $entry.PSIsContainer) { $files += [ordered]@{ path = $entry.FullName.Substring($bundle.Length + 1); sha256 = (Upgrade-Hash $entry.FullName); bytes = $entry.Length } }
    }
    $actual = @{}
    foreach ($file in $files) {
        if (-not $Expected.ContainsKey($file.path) -and $file.path -notin @('unins000.exe', 'unins000.dat')) { throw "Unexpected residual payload: $($file.path)" }
        $actual[$file.path] = $file.sha256
    }
    foreach ($path in $Expected.Keys) { if (-not $actual.ContainsKey($path) -or $actual[$path] -ne $Expected[$path]) { throw "Manifest payload mismatch: $path" } }
    $r = Upgrade-Registration
    foreach ($name in @('clsid', 'inproc', 'addin', 'startup', 'arp')) { if ($null -eq $r[$name]) { throw "Missing $name registration." } }
    if (([uri]$r.inproc['CodeBase']).LocalPath -ine (Join-Path $bundle 'SW2URDF.dll') -or
        ([string]$r.arp['InstallLocation']).TrimEnd('\') -ine $bundle -or
        ([string]$r.arp['UninstallString']).Trim('"') -ine (Join-Path $bundle 'unins000.exe')) { throw 'Registration does not own the upgrade destination.' }
    Save-Upgrade ([ordered]@{ passed = $true; matchedPayloadFiles = $Expected.Count; files = $files; registration = $r }) ($Label + '-verified.json')
}
try {
    No-SolidWorks
    if ((Upgrade-Hash $stable) -ne $stableHash -or (Upgrade-Hash $candidate) -ne $candidateHash) { throw 'Stable or candidate installer digest mismatch.' }
    $oldFiles = Read-UpgradeManifest ($stable + '.provenance.json') 'bc2a47f09e3aed764127a916dce66cc5edeb9a35c150b46d4f082fb9ba64cd72' $state.fromSource $stableHash 249
    $newFiles = Read-UpgradeManifest ($candidate + '.provenance.json') 'afddc87bd6e32d6d4d779c279643d5cd227f9039ea0467ee01859bf3645c58e3' $state.toSource $candidateHash 1466
    $before = Upgrade-Registration
    foreach ($key in $before.Keys) { if ($null -ne $before[$key]) { throw 'Fresh VM already has SW2URDF registration; stable baseline is not clean.' } }
    Save-Upgrade $before 'before-install-registration.json'
    $oldLog = Join-Path $upgradeEvidence 'stable-install.log'
    $p = Start-Process -FilePath $stable -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/NOCLOSEAPPLICATIONS', '/NORESTARTAPPLICATIONS', '/SP-', "/DIR=`"$bundle`"", "/LOG=`"$oldLog`"") -Wait -PassThru -WindowStyle Hidden
    Assert-UpgradeExit $p.ExitCode $oldLog 'stable-install'
    Assert-UpgradePayload $oldFiles 'stable-install'
    # The tested candidate is installed directly over the verified stable payload.
    # No uninstall, payload extraction, manual cleanup, or rebuild occurs here.
    & (Join-Path $PSScriptRoot 'Test-CleanReleaseRuntime.ps1') -Installer $candidate -InstallerSha256 $candidateHash -OutputDirectory $OutputDirectory
    $candidateResult = Get-Content -LiteralPath (Join-Path $OutputDirectory 'installer-result.json') -Raw | ConvertFrom-Json
    Assert-UpgradeExit $candidateResult.exitCode (Join-Path $OutputDirectory 'installer.log') 'stable-to-candidate'
    Assert-UpgradePayload $newFiles 'candidate-after-upgrade'
    $obsolete = @($oldFiles.Keys | Where-Object { -not $newFiles.ContainsKey($_) })
    foreach ($path in $obsolete) { if (Test-Path -LiteralPath (Join-Path $bundle $path)) { throw "Obsolete stable file survived upgrade: $path" } }
    $runtime = Get-Content -LiteralPath (Join-Path $OutputDirectory 'qualification.json') -Raw | ConvertFrom-Json
    if (-not $runtime.passed -or $runtime.installerSha256 -ne $candidateHash) { throw 'Upgraded runtime qualification did not pass.' }
    Save-Upgrade $obsolete 'obsolete-stable-paths.json'
    $state['stablePayloadFiles'] = $oldFiles.Count
    $state['candidatePayloadFiles'] = $newFiles.Count
    $state['obsoleteStablePathsRemoved'] = $obsolete.Count
    $state['upgradedRuntimePassed'] = $true
    $state.passed = $true
} catch { $state['failure'] = $_.Exception.Message }
finally { Save-Upgrade $state 'stable-upgrade-result.json' }
if (-not $state.passed) { throw 'Stable-to-candidate upgrade failed. Inspect stable-upgrade evidence.' }
Write-Output 'PASS: published v20260906 installed and upgraded directly to exact a0af2c5 candidate with full provenance/payload/registration/runtime verification.'
