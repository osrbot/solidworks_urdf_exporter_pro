param(
    [Parameter(Mandatory = $true)][string]$Installer,
    [Parameter(Mandatory = $true)][ValidatePattern('^[0-9a-fA-F]{64}$')][string]$InstallerSha256,
    [Parameter(Mandatory = $true)][string]$RuntimeEvidenceDirectory
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ($env:GITHUB_ACTIONS -ne 'true' -or $env:SW2URDF_RUNNER_ENVIRONMENT -ne 'github-hosted') {
    throw 'Installation lifecycle is restricted to a disposable GitHub-hosted runner.'
}
if (-not [Environment]::Is64BitProcess) { throw 'A 64-bit process is required.' }
$evidence = [IO.Path]::GetFullPath($RuntimeEvidenceDirectory).TrimEnd('\', '/')
$runnerTemp = [IO.Path]::GetFullPath($env:RUNNER_TEMP).TrimEnd('\', '/')
if (-not $evidence.StartsWith($runnerTemp + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Evidence must be under RUNNER_TEMP.' }
$bundle = Join-Path $evidence 'installed'
$output = Join-Path $evidence 'lifecycle'
New-Item -ItemType Directory -Path $output | Out-Null
$Installer = (Resolve-Path -LiteralPath $Installer).Path
$utf8 = New-Object Text.UTF8Encoding($false)
$expected = @{}
$state = [ordered]@{
    passed = $false; installerSha256 = $InstallerSha256.ToLowerInvariant()
    scope = 'Exact candidate install/overwrite/uninstall/reinstall on disposable Windows Server VM. No local desktop UAC interaction or SolidWorks activation is tested.'
    stages = @(); recoveryAttempted = $false; recovered = $false
}
$clsid = '{65c9fc17-6a74-45a3-8f84-55185900275d}'
$comKey = 'SOFTWARE\Classes\CLSID\' + $clsid
$addinKey = 'SOFTWARE\SolidWorks\Addins\' + $clsid
$startupKey = 'Software\SolidWorks\AddInsStartup\' + $clsid
$arpKey = 'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{E43E85A9-071D-430A-91B2-84B7AB923170}_is1'
$mutated = $false
$baselineRegistration = $null

function Hash([string]$Path) { (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() }
function Save-Json($Value, [string]$Name) { [IO.File]::WriteAllText((Join-Path $output $Name), ($Value | ConvertTo-Json -Depth 20), $utf8) }
function Assert-NoSolidWorks {
    if (@(Get-Process -Name SLDWORKS -ErrorAction SilentlyContinue).Count) { throw 'SolidWorks process exists; lifecycle will not close it.' }
}
function Read-Key([Microsoft.Win32.RegistryHive]$Hive, [string]$Path) {
    $base = [Microsoft.Win32.RegistryKey]::OpenBaseKey($Hive, [Microsoft.Win32.RegistryView]::Registry64)
    try {
        $key = $base.OpenSubKey($Path, $false)
        if ($null -eq $key) { return $null }
        try {
            $values = [ordered]@{}
            foreach ($name in $key.GetValueNames()) { $values[$name] = $key.GetValue($name) }
            return $values
        } finally { $key.Dispose() }
    } finally { $base.Dispose() }
}
function Registration {
    [ordered]@{
        clsid = (Read-Key LocalMachine $comKey)
        inproc = (Read-Key LocalMachine ($comKey + '\InprocServer32'))
        addin = (Read-Key LocalMachine $addinKey)
        startup = (Read-Key CurrentUser $startupKey)
        arp = (Read-Key LocalMachine $arpKey)
    }
}
function Files {
    if (-not (Test-Path -LiteralPath $bundle)) { return }
    $queue = New-Object 'Collections.Generic.Queue[string]'
    $queue.Enqueue($bundle)
    while ($queue.Count) {
        $directory = $queue.Dequeue()
        if ((Get-Item -LiteralPath $directory -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Reparse point in install root.' }
        foreach ($item in Get-ChildItem -LiteralPath $directory -Force) {
            if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Reparse point in payload.' }
            if ($item.PSIsContainer) { $queue.Enqueue($item.FullName); continue }
            [pscustomobject]@{ path = $item.FullName.Substring($bundle.Length + 1); sha256 = (Hash $item.FullName); bytes = $item.Length }
        }
    }
}
function Assert-SafeMutation {
    Assert-NoSolidWorks
    foreach ($file in @(Files)) {
        if (-not $expected.ContainsKey($file.path) -and $file.path -notin @('unins000.exe', 'unins000.dat')) { throw "Unexpected file would be exposed to uninstall: $($file.path)" }
    }
    $r = Registration
    if ($null -ne $r.inproc -and $r.inproc['CodeBase']) {
        if (([uri]$r.inproc['CodeBase']).LocalPath -ine (Join-Path $bundle 'SW2URDF.dll')) { throw 'Another installation owns the COM CodeBase.' }
    }
}
function Assert-Installed([string]$Label) {
    Assert-SafeMutation
    $files = @(Files)
    $actual = @{}
    foreach ($file in $files) { $actual[$file.path] = $file.sha256 }
    foreach ($path in $expected.Keys) {
        if (-not $actual.ContainsKey($path) -or $actual[$path] -ne $expected[$path]) { throw "Payload differs from first verified installation: $path" }
    }
    $r = Registration
    foreach ($key in @('clsid', 'inproc', 'addin', 'startup', 'arp')) {
        if ($null -eq $r[$key]) { throw "Missing registration: $key" }
    }
    if (([uri]$r.inproc['CodeBase']).LocalPath -ine (Join-Path $bundle 'SW2URDF.dll')) { throw 'Wrong COM CodeBase.' }
    if (([string]$r.arp['InstallLocation']).TrimEnd('\') -ine $bundle -or
        ([string]$r.arp['UninstallString']).Trim('"') -ine (Join-Path $bundle 'unins000.exe')) { throw 'Wrong ARP installation/uninstaller path.' }
    if ($null -ne $baselineRegistration) {
        foreach ($key in @('clsid', 'inproc', 'addin', 'startup')) {
            foreach ($name in $baselineRegistration[$key].Keys) {
                if ($r[$key][$name] -cne $baselineRegistration[$key][$name]) { throw "Registration changed in $key / $name" }
            }
        }
    }
    Save-Json ([ordered]@{ passed = $true; matchedPayloadFiles = $expected.Count; files = $files; registration = $r }) ($Label + '-verified.json')
    return $r
}
function Run-Stage([string]$Label, [bool]$Uninstall) {
    Assert-SafeMutation
    if ((Hash $Installer) -ne $InstallerSha256.ToLowerInvariant()) { throw 'Installer SHA256 changed.' }
    $log = Join-Path $output ($Label + '.log')
    $exe = if ($Uninstall) { Join-Path $bundle 'unins000.exe' } else { $Installer }
    $arguments = @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', "/LOG=`"$log`"")
    if (-not $Uninstall) { $arguments += @('/SP-', '/NOCLOSEAPPLICATIONS', '/NORESTARTAPPLICATIONS', "/DIR=`"$bundle`"") }
    $script:mutated = $true
    $process = Start-Process -FilePath $exe -ArgumentList $arguments -Wait -PassThru -WindowStyle Hidden
    $text = if (Test-Path -LiteralPath $log) { [IO.File]::ReadAllText($log) } else { '' }
    $childCodes = @([regex]::Matches($text, 'Process exit code:\s*(-?\d+)') | ForEach-Object { [int]$_.Groups[1].Value })
    $row = [ordered]@{ stage = $Label; exitCode = $process.ExitCode; regAsmPresent = ($text -match 'RegAsm\.exe'); childExitCodes = $childCodes }
    $state.stages += $row
    Save-Json $row ($Label + '-exit.json')
    Save-Json $state 'lifecycle-result.json'
    if ($process.ExitCode -ne 0 -or -not $row.regAsmPresent -or -not $childCodes.Count -or @($childCodes | Where-Object { $_ -ne 0 }).Count) { throw "$Label installer/RegAsm exit validation failed." }
    if ($Uninstall) {
        Assert-NoSolidWorks
        $remaining = @(Files)
        $r = Registration
        Save-Json ([ordered]@{ remainingFiles = $remaining; registration = $r }) 'uninstall-observed.json'
        if ($remaining.Count) { throw 'Uninstall left files in the otherwise empty candidate directory.' }
        foreach ($key in @('clsid', 'inproc', 'addin', 'startup', 'arp')) { if ($null -ne $r[$key]) { throw "Uninstall retained $key registration." } }
        Save-Json ([ordered]@{ passed = $true; payloadRemoved = $true; registrationRemoved = $true }) 'uninstall-verified.json'
    } else { $null = Assert-Installed $Label }
}

try {
    Assert-NoSolidWorks
    if ((Hash $Installer) -ne $InstallerSha256.ToLowerInvariant()) { throw 'Candidate SHA256 mismatch.' }
    $runtimeResult = Get-Content -LiteralPath (Join-Path $evidence 'qualification.json') -Raw | ConvertFrom-Json
    if (-not $runtimeResult.passed -or $runtimeResult.installerSha256 -ne $InstallerSha256.ToLowerInvariant()) { throw 'Initial runtime qualification identity is invalid.' }
    $initialResult = Get-Content -LiteralPath (Join-Path $evidence 'installer-result.json') -Raw | ConvertFrom-Json
    $initialLog = [IO.File]::ReadAllText((Join-Path $evidence 'installer.log'))
    $initialCodes = @([regex]::Matches($initialLog, 'Process exit code:\s*(-?\d+)') | ForEach-Object { [int]$_.Groups[1].Value })
    $initialExit = [ordered]@{ stage = 'initial-install'; exitCode = $initialResult.exitCode; regAsmPresent = ($initialLog -match 'RegAsm\.exe'); childExitCodes = $initialCodes }
    $state.stages += $initialExit
    Save-Json $initialExit 'initial-install-exit.json'
    if ($initialResult.exitCode -ne 0 -or -not $initialExit.regAsmPresent -or -not $initialCodes.Count -or @($initialCodes | Where-Object { $_ -ne 0 }).Count) { throw 'Initial installer/RegAsm exit validation failed.' }
    $initial = Get-Content -LiteralPath (Join-Path $evidence 'installed-payload.json') -Raw | ConvertFrom-Json
    foreach ($row in $initial) {
        $path = ([string]$row.path).Replace('/', '\')
        if ($path -in @('unins000.exe', 'unins000.dat')) { continue }
        if ([IO.Path]::IsPathRooted($path) -or $path -match '(^|\\)\.\.(\\|$)|:' -or $expected.ContainsKey($path)) { throw 'Invalid baseline payload path.' }
        $expected[$path] = [string]$row.sha256
    }
    if ($expected.Count -ne 1466) { throw 'Expected exact 1,466-file candidate baseline.' }
    $baselineRegistration = Assert-Installed 'initial-install'
    Run-Stage 'same-version-upgrade' $false
    Run-Stage 'uninstall' $true
    Run-Stage 'reinstall' $false
    $state.passed = $true
} catch {
    $state['failure'] = $_.Exception.Message
    if ($mutated) {
        $state.recoveryAttempted = $true
        try { Run-Stage 'failure-recovery' $false; $state.recovered = $true }
        catch { $state['recoveryFailure'] = $_.Exception.Message }
    }
} finally {
    Save-Json $state 'lifecycle-result.json'
}
if (-not $state.passed) { throw 'Exact-candidate install lifecycle failed; see retained lifecycle evidence.' }
Write-Output 'PASS: exact candidate initial-install/overwrite/uninstall/reinstall and registration/payload checks on the disposable VM.'
