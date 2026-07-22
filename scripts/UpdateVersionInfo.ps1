param (
    [string]$filename,
    [switch]$Strict
)

$ErrorActionPreference = "Stop"

function Escape-CSharpString([string]$value) {
    if ($null -eq $value) {
        return ""
    }

    return $value.Replace('\', '\\').Replace('"', '\"')
}

$RepoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$SafeRepoRoot = $RepoRoot.Path.Replace("\", "/")
$CommitHash = & git -c "safe.directory=$SafeRepoRoot" -C $RepoRoot.Path rev-parse --short=12 HEAD 2>$null
$CommitHashExitCode = $LASTEXITCODE
if ($CommitHashExitCode -ne 0 -or [string]::IsNullOrWhiteSpace($CommitHash)) {
    if ($Strict) {
        throw "Unable to resolve the Git commit for Release version metadata."
    }
    $CommitHash = "unknown"
}
$CommitVersion = $CommitHash

$DirtyFiles = & git -c "safe.directory=$SafeRepoRoot" -C $RepoRoot.Path status --porcelain --untracked-files=no -- . ':!INSTALL/OUTPUT/**' 2>$null
$DirtyExitCode = $LASTEXITCODE
if ($DirtyExitCode -ne 0 -and $Strict) {
    throw "Unable to inspect the Git worktree for Release version metadata."
}
$Dirty = $DirtyExitCode -ne 0 -or -not [string]::IsNullOrWhiteSpace($DirtyFiles)
if ($Dirty) {
    $CommitVersion = "$CommitVersion-dirty"
}

$CommitTime = & git -c "safe.directory=$SafeRepoRoot" -C $RepoRoot.Path show -s --format=%cI HEAD 2>$null
$CommitTimeExitCode = $LASTEXITCODE
$ParsedCommitTime = [DateTimeOffset]::MinValue
if ($CommitTimeExitCode -ne 0 -or -not [DateTimeOffset]::TryParse(
    $CommitTime,
    [System.Globalization.CultureInfo]::InvariantCulture,
    [System.Globalization.DateTimeStyles]::RoundtripKind,
    [ref]$ParsedCommitTime)) {
    if ($Strict) {
        throw "Unable to resolve the Git commit time for Release version metadata."
    }
    $ParsedCommitTime = [DateTimeOffset]::UtcNow
}
$BuildTimeUtc = $ParsedCommitTime.UtcDateTime.ToString(
    "yyyy-MM-ddTHH:mm:ssZ",
    [System.Globalization.CultureInfo]::InvariantCulture)
$FileContent = @"
using System.Reflection;

[assembly: AssemblyInformationalVersion("$(Escape-CSharpString $CommitVersion)")]
[assembly: AssemblyMetadata("SW2URDF.CommitVersion", "$(Escape-CSharpString $CommitVersion)")]
[assembly: AssemblyMetadata("SW2URDF.CommitHash", "$(Escape-CSharpString $CommitHash)")]
[assembly: AssemblyMetadata("SW2URDF.BuildTimeUtc", "$(Escape-CSharpString $BuildTimeUtc)")]
[assembly: AssemblyMetadata("SW2URDF.Dirty", "$($Dirty.ToString().ToLowerInvariant())")]
"@

$DirectoryName = Split-Path -Parent $filename
if (-not [string]::IsNullOrWhiteSpace($DirectoryName)) {
    New-Item -ItemType Directory -Force -Path $DirectoryName | Out-Null
}

$Encoding = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText($filename, $FileContent, $Encoding)
