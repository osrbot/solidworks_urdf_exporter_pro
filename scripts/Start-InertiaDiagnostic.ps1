# ASCII source so Windows PowerShell 5.1 can read it without a BOM.
[CmdletBinding()]
param([string]$SolidWorksPath = '', [switch]$ValidateOnly)
$ErrorActionPreference = 'Stop'
try {
    $pluginDirectory = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
    $plugin = Join-Path $pluginDirectory 'SW2URDF.dll'
    if (-not (Test-Path -LiteralPath $plugin -PathType Leaf)) {
        throw 'Run the installed SW2URDF SW2-3 Diagnostic shortcut after installing the diagnostic package.'
    }
    if ([string]::IsNullOrWhiteSpace($SolidWorksPath)) {
        $appPath = 'Registry::HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\SLDWORKS.exe'
        if (Test-Path -LiteralPath $appPath) { $SolidWorksPath = (Get-Item -LiteralPath $appPath).GetValue('') }
    }
    if ([string]::IsNullOrWhiteSpace($SolidWorksPath) -or -not (Test-Path -LiteralPath $SolidWorksPath -PathType Leaf)) {
        if ($ValidateOnly) { throw 'Pass -SolidWorksPath with an existing SLDWORKS.exe.' }
        Add-Type -AssemblyName System.Windows.Forms
        $picker = New-Object System.Windows.Forms.OpenFileDialog
        $picker.Title = 'SW2-3 Diagnostic: select your SLDWORKS.exe'
        $picker.Filter = 'SolidWorks|SLDWORKS.exe'
        try {
            if ($picker.ShowDialog() -ne [Windows.Forms.DialogResult]::OK) { return }
            $SolidWorksPath = $picker.FileName
        } finally { $picker.Dispose() }
    }
    $SolidWorksPath = (Resolve-Path -LiteralPath $SolidWorksPath).Path
    if ([IO.Path]::GetFileName($SolidWorksPath) -ine 'SLDWORKS.exe') { throw 'Select SLDWORKS.exe.' }
    $version = [Diagnostics.FileVersionInfo]::GetVersionInfo($plugin).ProductVersion
    if ($ValidateOnly) {
        Write-Output ("SW2-3 DIAGNOSTIC launcher validated; plugin=" + $version + "; executable=" + $SolidWorksPath)
        return
    }
    if (Get-Process -Name SLDWORKS -ErrorAction SilentlyContinue) {
        throw 'Save your work and close all SolidWorks windows normally before starting a diagnostic session.'
    }
    $sessionDirectory = Join-Path $env:LOCALAPPDATA ('SW2URDF/Diagnostics/SW2-3-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0,6))
    New-Item -ItemType Directory -Path $sessionDirectory | Out-Null
    $logFile = Join-Path $sessionDirectory 'sw2urdf.log'
    $info = "SW2-3 DIAGNOSTIC`r`nPlugin=$version`r`nStarted=$([DateTimeOffset]::Now.ToString('o'))`r`n"
    [IO.File]::WriteAllText((Join-Path $sessionDirectory 'session.txt'), $info, (New-Object Text.UTF8Encoding($false)))
    $start = New-Object Diagnostics.ProcessStartInfo
    $start.FileName = $SolidWorksPath
    $start.UseShellExecute = $false
    $start.WorkingDirectory = [IO.Path]::GetDirectoryName($SolidWorksPath)
    $start.EnvironmentVariables['SW2URDF_INERTIA_DIAGNOSTICS'] = '1'
    $start.EnvironmentVariables['SW2URDF_LOG_FILE'] = $logFile
    # Interactive SolidWorks window; this does not register, install or select a plugin DLL.
    $process = [Diagnostics.Process]::Start($start)
    $process.Dispose()
    Add-Type -AssemblyName System.Windows.Forms
    [Windows.Forms.MessageBox]::Show(
        "SW2-3 diagnostic session started.`r`nReproduce the inertia issue once, then keep sw2urdf.log and its rolling backups.`r`n`r`nLogs: " + $sessionDirectory +
        "`r`n`r`nNo CAD files or crash dumps are collected or uploaded.",
        'SW2URDF SW2-3 DIAGNOSTIC') | Out-Null
    Invoke-Item -LiteralPath $sessionDirectory
} catch {
    if ($ValidateOnly) { throw }
    Add-Type -AssemblyName System.Windows.Forms
    [Windows.Forms.MessageBox]::Show($_.Exception.Message, 'SW2-3 Diagnostic could not start') | Out-Null
    exit 1
}
