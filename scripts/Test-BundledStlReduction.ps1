param(
    [Parameter(Mandatory=$true)][string]$AssemblyPath,
    [Parameter(Mandatory=$true)][string]$SourceDirectory,
    [Parameter(Mandatory=$true)][string]$OutputDirectory,
    [double]$Ratio = 0.67,
    [string]$Filter = '*.stl',
    [int]$TimeoutSeconds = 0,
    [switch]$RepeatForCache
)

$ErrorActionPreference = 'Stop'
function Get-SourceHash([string]$Path) {
    $stream = [IO.File]::OpenRead($Path)
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-','') }
    finally { $sha.Dispose(); $stream.Dispose() }
}
$sourceRoot = (Resolve-Path -LiteralPath $SourceDirectory).Path
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
if ($outputRoot.TrimEnd('\','/') -eq $sourceRoot.TrimEnd('\','/') -or
    (Test-Path -LiteralPath $outputRoot)) { throw 'Use a new output directory distinct from the original corpus.' }
New-Item -ItemType Directory -Path $outputRoot | Out-Null
$assembly = [Reflection.Assembly]::LoadFrom((Resolve-Path -LiteralPath $AssemblyPath).Path)
$type = $assembly.GetType('SW2URDF.URDFExport.BundledStlMeshReducer', $true)
$method = @($type.GetMethods([Reflection.BindingFlags]'Static,NonPublic') | Where-Object {
    $_.Name -eq 'ReduceFile' -and $_.GetParameters().Count -eq $(if ($TimeoutSeconds -gt 0) { 7 } else { 3 })
})[0]
if ($null -eq $method) { throw 'Bundled reducer entry point missing.' }
$files = @(Get-ChildItem -LiteralPath $sourceRoot -File -Filter $Filter)
if (!$files.Count) { throw 'No STL inputs found.' }
$rows = @()
foreach ($file in $files) {
    $before = Get-SourceHash $file.FullName
    foreach ($pass in 1..$(if ($RepeatForCache) { 2 } else { 1 })) {
        $destination = Join-Path $outputRoot ($file.BaseName + '-pass' + $pass + '.stl')
        Copy-Item -LiteralPath $file.FullName -Destination $destination
        $timer = [Diagnostics.Stopwatch]::StartNew()
        $arguments = [object[]]@([string]$destination, [double]$Ratio, $null)
        if ($TimeoutSeconds -gt 0) {
            $bundle = Split-Path -Parent (Resolve-Path -LiteralPath $AssemblyPath).Path
            $arguments = [object[]]@([string]$destination, [double]$Ratio,
                [string](Join-Path $bundle 'tools/openusd_runtime/python.exe'),
                [string](Join-Path $bundle 'tools/mesh_reduction/reduce_stl.py'),
                [TimeSpan]::FromSeconds($TimeoutSeconds), $null, [Threading.CancellationToken]::None)
        }
        $result = $method.Invoke($null, $arguments)
        $timer.Stop()
        if ((Get-SourceHash $file.FullName) -ne $before) {
            throw "Original input changed: $($file.FullName)"
        }
        $row = [ordered]@{ input=$file.FullName; output=$destination; pass=$pass; ratio=$Ratio;
            originalSha256=$before; originalTriangles=$result.OriginalTriangles;
            finalTriangles=$result.FinalTriangles; originalBytes=$result.OriginalBytes;
            finalBytes=$result.FinalBytes; status=$result.Status; warning=$result.Warning;
            seconds=[Math]::Round($timer.Elapsed.TotalSeconds,3) }
        $rows += [pscustomobject]$row
        $row | ConvertTo-Json -Compress | Write-Output
        [IO.File]::WriteAllText((Join-Path $outputRoot 'results.json'),
            (ConvertTo-Json -InputObject $rows -Depth 5), [Text.UTF8Encoding]::new($false))
        if ($result.FinalBytes -gt $result.OriginalBytes -or $result.FinalTriangles -eq 0) {
            throw 'Invalid reduction result.'
        }
    }
}
