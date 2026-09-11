param(
    [Parameter(Mandatory = $true)][string]$AssemblyPath,
    [string]$InputDirectory,
    [Parameter(Mandatory = $true)][string]$OutputDirectory,
    [double[]]$Ratios = @(0, 0.5, 1),
    [string]$BaselineResults,
    [switch]$Worker,
    [string]$SourcePath,
    [double]$Ratio,
    [string]$SourceHash
)

$ErrorActionPreference = 'Stop'
$AssemblyPath = (Resolve-Path -LiteralPath $AssemblyPath).Path
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)

if ($Worker) {
    Import-Module "$PSHOME/Modules/Microsoft.PowerShell.Utility/Microsoft.PowerShell.Utility.psd1"
    # A separate .NET Framework process per case matches the plugin runtime and
    # releases the mesh working set without loading SolidWorks or modifying CAD.
    [void][Reflection.Assembly]::LoadFrom((Join-Path (Split-Path $AssemblyPath) 'geometry3Sharp.dll'))
    $assembly = [Reflection.Assembly]::LoadFrom($AssemblyPath)
    $method = $assembly.GetType('SW2URDF.URDFExport.StlMeshReducer').GetMethod(
        'ReduceFile', [Reflection.BindingFlags]'Static,NonPublic')
    $copy = Join-Path $OutputDirectory 'mesh.stl'
    Copy-Item -LiteralPath $SourcePath -Destination $copy
    $watch = [Diagnostics.Stopwatch]::StartNew()
    $result = $method.Invoke($null, @([string]$copy, [double]$Ratio))
    $watch.Stop()
    $outputHash = (Get-FileHash -LiteralPath $copy -Algorithm SHA256).Hash
    $length = (Get-Item -LiteralPath $copy).Length
    if ($result.FinalBytes -ne $length) { throw 'Reported STL size differs from file size.' }
    if ($result.FinalTriangles -lt 1 -or $result.FinalTriangles -gt $result.OriginalTriangles) {
        throw 'Invalid output triangle count.'
    }
    if ($Ratio -eq 0 -or $result.Status -ne 'reduced') {
        if ($outputHash -ne $SourceHash) { throw 'No-op or fallback changed the original bytes.' }
    } else {
        if ($length -ge $result.OriginalBytes) { throw 'Reduced mesh is not smaller.' }
        $stream = [IO.File]::OpenRead($copy)
        $reader = [IO.BinaryReader]::new($stream)
        try {
            $stream.Position = 80
            $triangles = $reader.ReadUInt32()
            if ($triangles -ne $result.FinalTriangles -or $length -ne 84L + 50L * $triangles) {
                throw 'Serialized binary STL triangle count/length mismatch.'
            }
        } finally { $reader.Dispose() }
    }
    [pscustomobject]@{
        Source = $SourcePath; SourceHash = $SourceHash; Ratio = $Ratio
        OriginalTriangles = $result.OriginalTriangles; FinalTriangles = $result.FinalTriangles
        OriginalBytes = $result.OriginalBytes; FinalBytes = $result.FinalBytes
        ActualRemoval = 1.0 - $result.FinalTriangles / [double]$result.OriginalTriangles
        Seconds = [Math]::Round($watch.Elapsed.TotalSeconds, 3)
        Status = $result.Status; Warning = $result.Warning; Integrity = 'PASS'
        Output = $copy; OutputHash = $outputHash
    } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $OutputDirectory 'result.json') -Encoding UTF8
    exit 0
}

$InputDirectory = (Resolve-Path -LiteralPath $InputDirectory).Path.TrimEnd('\', '/')
if ($OutputDirectory.Equals($InputDirectory, [StringComparison]::OrdinalIgnoreCase) -or
    $OutputDirectory.StartsWith($InputDirectory + [IO.Path]::DirectorySeparatorChar,
        [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Output must be outside the read-only input corpus.'
}
if (Test-Path -LiteralPath $OutputDirectory) { throw 'Choose a fresh output directory.' }
foreach ($value in $Ratios) {
    if ([double]::IsNaN($value) -or $value -lt 0 -or $value -gt 1) { throw 'Invalid ratio.' }
}
[void][IO.Directory]::CreateDirectory($OutputDirectory)
$files = @(Get-ChildItem -LiteralPath $InputDirectory -Recurse -File |
    Where-Object Extension -ieq '.stl' | Sort-Object FullName)
if ($files.Count -eq 0) { throw 'No STL files found.' }
$inventory = @($files | ForEach-Object {
    [pscustomobject]@{ Source = $_.FullName; Bytes = $_.Length
        Hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
})
$inventory | Export-Csv -LiteralPath (Join-Path $OutputDirectory 'inventory.csv') -NoTypeInformation -Encoding UTF8
$unique = @($inventory | Group-Object Hash)
$rows = [Collections.Generic.List[object]]::new()
$failures = 0
$baseline = @{}
if ($BaselineResults) {
    foreach ($row in (Import-Csv -LiteralPath $BaselineResults)) {
        $key = $row.SourceHash + '|' + ([double]$row.Ratio).ToString('R', [Globalization.CultureInfo]::InvariantCulture)
        $baseline[$key] = $row
    }
}
$regressions = 0
$index = 0
$hostExe = Join-Path $env:SystemRoot 'System32/WindowsPowerShell/v1.0/powershell.exe'
foreach ($group in $unique) {
    $index++
    $source = $group.Group[0]
    foreach ($value in $Ratios) {
        $token = $value.ToString('0.####', [Globalization.CultureInfo]::InvariantCulture)
        $case = Join-Path $OutputDirectory ('{0:D3}-{1}-{2}' -f $index, $group.Name.Substring(0, 12), $token)
        [void][IO.Directory]::CreateDirectory($case)
        $arguments = @('-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-File',
            ('"{0}"' -f $PSCommandPath), '-Worker', '-AssemblyPath', ('"{0}"' -f $AssemblyPath),
            '-OutputDirectory', ('"{0}"' -f $case), '-SourcePath', ('"{0}"' -f $source.Source),
            '-Ratio', $token, '-SourceHash', $group.Name)
        $process = Start-Process -FilePath $hostExe -ArgumentList $arguments -WindowStyle Hidden -PassThru `
            -RedirectStandardOutput (Join-Path $case 'stdout.log') -RedirectStandardError (Join-Path $case 'stderr.log')
        if (-not $process.WaitForExit(180000)) {
            $process.Kill()
            $process.WaitForExit()
            $failures++
            Write-Warning "TIMEOUT: $($source.Source), ratio=$token"
        } elseif ($process.ExitCode -ne 0) {
            $failures++
            Write-Warning "FAILED: $($source.Source), ratio=$token; see $case/stderr.log"
        } else {
            $row = Get-Content -LiteralPath (Join-Path $case 'result.json') -Raw -Encoding UTF8 | ConvertFrom-Json
            if ($BaselineResults) {
                $key = $row.SourceHash + '|' + ([double]$row.Ratio).ToString('R', [Globalization.CultureInfo]::InvariantCulture)
                if (-not $baseline.ContainsKey($key)) { throw "Missing baseline case: $key" }
                $oldCount = [long]$baseline[$key].FinalTriangles
                $row | Add-Member -NotePropertyName BaselineFinalTriangles -NotePropertyValue $oldCount
                if ($row.FinalTriangles -gt $oldCount) {
                    $regressions++
                    Write-Warning "Reduction regressed: $($source.Source), ratio=$token, $oldCount->$($row.FinalTriangles)"
                }
            }
            $rows.Add($row)
            Write-Host ('[{0}/{1}] {2} ratio={3}: {4}->{5}, {6}s, {7}' -f $index, $unique.Count,
                [IO.Path]::GetFileName($source.Source), $token, $row.OriginalTriangles,
                $row.FinalTriangles, $row.Seconds, $row.Status)
            $rows | Export-Csv -LiteralPath (Join-Path $OutputDirectory 'results.csv') -NoTypeInformation -Encoding UTF8
        }
        $process.Dispose()
    }
}
foreach ($file in $inventory) {
    if ((Get-FileHash -LiteralPath $file.Source -Algorithm SHA256).Hash -ne $file.Hash) {
        throw "Input corpus changed: $($file.Source)"
    }
}
[pscustomobject]@{
    AssemblyPath = $AssemblyPath; AssemblyHash = (Get-FileHash -LiteralPath $AssemblyPath).Hash
    InputFiles = $inventory.Count; UniqueMeshes = $unique.Count; Ratios = $Ratios
    Completed = $rows.Count; Failures = $failures; SourcesUnchanged = $true
    BaselineResults = $BaselineResults; ReductionRegressions = $regressions
    Reduced = @($rows | Where-Object Status -eq 'reduced').Count
    Fallback = @($rows | Where-Object { $_.Ratio -gt 0 -and $_.Status -ne 'reduced' }).Count
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $OutputDirectory 'summary.json') -Encoding UTF8
if ($failures -gt 0) { throw "$failures corpus cases failed; see per-case logs." }
if ($regressions -gt 0) { throw "$regressions corpus cases reduced less than the baseline." }
Write-Host "Corpus integrity PASS. Reduction effectiveness is recorded separately: $OutputDirectory/results.csv"
