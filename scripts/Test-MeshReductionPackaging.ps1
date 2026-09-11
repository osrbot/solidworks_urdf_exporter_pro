param([string]$WheelCache = "")

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
$Tokens = $null
$Errors = $null
$Ast = [System.Management.Automation.Language.Parser]::ParseFile(
    (Join-Path $PSScriptRoot "BuildInstaller.ps1"), [ref]$Tokens, [ref]$Errors)
if ($Errors.Count) { throw ($Errors | Out-String) }
# Load only helpers, never the installer entry point or its build/COM operations.
$Helpers = @("Get-Sha256", "Get-PinnedDownload", "Assert-ChildPath",
    "Assert-NoPythonBytecode", "Install-MeshReductionRuntime", "Test-MeshReductionRuntime",
    "Test-MeshReductionWorker")
foreach ($Name in $Helpers) {
    $Definition = $Ast.Find({ param($Node)
        $Node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
        $Node.Name -eq $Name
    }, $false)
    if ($null -eq $Definition) { throw "Missing packaging helper: $Name" }
    . ([scriptblock]::Create($Definition.Extent.Text))
}
function Assert([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}
$LockPath = Join-Path $Root "tools/mesh_reduction_runtime.lock.json"
$Lock = Get-Content -LiteralPath $LockPath -Raw | ConvertFrom-Json
$OpenUsdLock = Get-Content (Join-Path $Root "tools/openusd_runtime.lock.json") -Raw | ConvertFrom-Json
Assert ($Lock.pythonVersion -eq $OpenUsdLock.python.version) "Shared interpreter mismatch"
Assert (($Lock.packages.id -join ",") -eq "numpy,pymeshlab") "Unexpected dependency set"
foreach ($Package in $Lock.packages) {
    Assert ($Package.filename -eq "$($Package.id)-$($Package.version)-cp311-cp311-win_amd64.whl") "Wrong wheel ABI"
    Assert ($Package.sha256 -cmatch '^[a-f0-9]{64}$') "Invalid wheel SHA256"
    Assert ($Package.url.EndsWith("/" + $Package.filename)) "Noncanonical filename"
}
$Installer = Get-Content (Join-Path $Root "INSTALL/Install.iss") -Raw
Assert ($Installer.Contains('\tools\mesh_reduction\reduce_stl.py')) "Worker not installed"
Assert ($Installer.Contains('\tools\mesh_reduction_runtime.lock.json')) "Lock not installed"
Assert ($Ast.Extent.Text.Contains('$AssetRuntimeInputs += $MeshReductionInputs')) "Missing wheel provenance"
Write-Output "PASS: parser, lock, installer payload and provenance contracts"
if ([string]::IsNullOrWhiteSpace($WheelCache)) {
    Write-Output "SKIP: native smoke (pass -WheelCache with the mesh wheels, usd-core wheel and python-3.11.9-embed-amd64.zip)"
    exit 0
}
$WheelCache = (Resolve-Path -LiteralPath $WheelCache).Path
$PythonArchive = Join-Path $WheelCache "python-3.11.9-embed-amd64.zip"
Assert ((Test-Path -LiteralPath $PythonArchive -PathType Leaf)) "Python archive missing; test is offline"
Assert ((Get-Sha256 $PythonArchive) -eq $OpenUsdLock.python.sha256) "Python hash mismatch"
$UsdWheel = Join-Path $WheelCache $OpenUsdLock.usdCore.filename
Assert ((Test-Path -LiteralPath $UsdWheel -PathType Leaf)) "USD wheel missing; test is offline"
Assert ((Get-Sha256 $UsdWheel) -eq $OpenUsdLock.usdCore.sha256) "USD wheel hash mismatch"
foreach ($Package in $Lock.packages) {
    $Wheel = Join-Path $WheelCache $Package.filename
    Assert ((Test-Path -LiteralPath $Wheel -PathType Leaf)) "Wheel missing; test is offline"
    Assert ((Get-Sha256 $Wheel) -eq $Package.sha256) "Wheel hash mismatch; test will not download"
}
Add-Type -AssemblyName System.IO.Compression.FileSystem
$StagingParent = Join-Path $Root ".codex-build"
$Stage = Assert-ChildPath $StagingParent (Join-Path $StagingParent `
    ("mesh-packaging-test-" + [guid]::NewGuid().ToString("N"))) "Packaging test staging"
New-Item -ItemType Directory -Path $Stage -Force | Out-Null
try {
    $Runtime = Join-Path $Stage "openusd_runtime"
    [IO.Compression.ZipFile]::ExtractToDirectory($PythonArchive, $Runtime)
    $SitePackages = Join-Path $Runtime "Lib/site-packages"
    New-Item -ItemType Directory -Path $SitePackages -Force | Out-Null
    [IO.Compression.ZipFile]::ExtractToDirectory($UsdWheel, $SitePackages)
    $Licenses = Join-Path $Stage "THIRD_PARTY_LICENSES"
    $Inputs = @(Install-MeshReductionRuntime $LockPath $OpenUsdLock.python.version `
        $SitePackages $WheelCache $Licenses)
    Assert ($Inputs.Count -eq 2) "Wheel provenance count mismatch"
    foreach ($Package in $Lock.packages) {
        $Copy = Join-Path $Licenses ("{0}-{1}-LICENSE.txt" -f $Package.id, $Package.version)
        Assert ((Get-Sha256 $Copy) -eq (Get-Sha256 (Join-Path $SitePackages $Package.licenseFile))) "License changed"
        $DistInfo = Join-Path $SitePackages ("{0}-{1}.dist-info" -f $Package.id, $Package.version)
        foreach ($Metadata in @("METADATA", "WHEEL", "RECORD")) {
            Assert ((Test-Path (Join-Path $DistInfo $Metadata))) "Wheel metadata stripped"
        }
    }
    [IO.File]::WriteAllLines((Join-Path $Runtime "python311._pth"),
        @("python311.zip", ".", "Lib\site-packages", "import site"),
        (New-Object Text.UTF8Encoding($false)))
    Test-MeshReductionRuntime (Join-Path $Runtime "python.exe")
    & (Join-Path $Runtime "python.exe") -B -c "import numpy, pymeshlab; from pxr import Usd, UsdGeom, UsdPhysics; assert Usd.GetVersion() == (0, 26, 8); print('Shared USD and mesh runtime passed')"
    Assert ($LASTEXITCODE -eq 0) "USD and mesh packages do not coexist"
    $WorkerDirectory = Join-Path $Stage "mesh_reduction"
    New-Item -ItemType Directory -Path $WorkerDirectory -Force | Out-Null
    $Worker = Join-Path $WorkerDirectory "reduce_stl.py"
    Copy-Item -LiteralPath (Join-Path $Root "tools/mesh_reduction/reduce_stl.py") -Destination $Worker
    Test-MeshReductionWorker (Join-Path $Runtime "python.exe") $Worker $Stage
    Assert-NoPythonBytecode $Stage
    $Rejected = $false
    try {
        Install-MeshReductionRuntime $LockPath "3.12.0" $SitePackages $WheelCache $Licenses
    } catch {
        if ($_.Exception.Message -notmatch "incompatible with embedded Python") { throw }
        $Rejected = $true
    }
    Assert $Rejected "cp312 interpreter was accepted"
    Write-Output "PASS: pinned cp311 runtime, native reduction, worker protocol, licenses, metadata, no bytecode, ABI rejection"
} finally {
    $SafeStage = Assert-ChildPath $StagingParent $Stage "Packaging test cleanup"
    Remove-Item -LiteralPath $SafeStage -Recurse -Force
}
