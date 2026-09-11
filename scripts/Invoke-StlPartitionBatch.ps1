param(
    [Parameter(Mandatory = $true)][string]$AssemblyPath,
    [Parameter(Mandatory = $true)][string]$Manifest,
    [Parameter(Mandatory = $true)][string]$Results,
    [Parameter(Mandatory = $true)][double]$Ratio,
    [int]$BudgetSeconds = 600
)

$ErrorActionPreference = 'Stop'
Import-Module "$PSHOME/Modules/Microsoft.PowerShell.Utility/Microsoft.PowerShell.Utility.psd1"
[void][Reflection.Assembly]::LoadFrom((Join-Path (Split-Path $AssemblyPath) 'geometry3Sharp.dll'))
$assembly = [Reflection.Assembly]::LoadFrom($AssemblyPath)
$reduce = $assembly.GetType('SW2URDF.URDFExport.StlMeshReducer').GetMethod(
    'ReduceFile', [Reflection.BindingFlags]'Static,NonPublic')
$jobs = @((Get-Content -LiteralPath $Manifest -Raw -Encoding UTF8 | ConvertFrom-Json).jobs)
$clock = [Diagnostics.Stopwatch]::StartNew()
$writer = [IO.StreamWriter]::new($Results, $false, [Text.UTF8Encoding]::new($false))
try {
    $index = 0
    foreach ($job in $jobs) {
        $index++
        $row = [ordered]@{ id = $job.id; status = 'budget_retained'; warning = ''; seconds = 0 }
        if ($clock.Elapsed.TotalSeconds -lt $BudgetSeconds) {
            $timer = [Diagnostics.Stopwatch]::StartNew()
            try {
                Copy-Item -LiteralPath $job.input -Destination $job.output
                $result = $reduce.Invoke($null, @([string]$job.output, [double]$Ratio))
                $row.status = $result.Status
                $row.warning = $result.Warning
                $row.original_triangles = $result.OriginalTriangles
                $row.final_triangles = $result.FinalTriangles
            } catch {
                $row.status = 'exception_retained'
                $row.warning = $_.Exception.ToString()
            }
            $row.seconds = [Math]::Round($timer.Elapsed.TotalSeconds, 4)
        }
        $writer.WriteLine(($row | ConvertTo-Json -Compress))
        $writer.Flush()
        if ($index % 25 -eq 0 -or $index -eq $jobs.Count) {
            Write-Output ("Partition batch: {0}/{1}, {2:F1}s" -f $index, $jobs.Count, $clock.Elapsed.TotalSeconds)
            [GC]::Collect()
        }
    }
} finally { $writer.Dispose() }
