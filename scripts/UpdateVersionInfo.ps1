param (
    [string]$filename
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
$CommitVersion = & git -c "safe.directory=$SafeRepoRoot" -C $RepoRoot.Path describe --tags --long --always 2>$null
if ([string]::IsNullOrWhiteSpace($CommitVersion)) {
    $CommitVersion = "unknown"
}

$CommitHash = & git -c "safe.directory=$SafeRepoRoot" -C $RepoRoot.Path rev-parse --short=12 HEAD 2>$null
if ([string]::IsNullOrWhiteSpace($CommitHash)) {
    $CommitHash = "unknown"
}

$DirtyFiles = & git -c "safe.directory=$SafeRepoRoot" -C $RepoRoot.Path status --porcelain --untracked-files=no -- . ':!INSTALL/OUTPUT/*.exe' 2>$null
$Dirty = -not [string]::IsNullOrWhiteSpace($DirtyFiles)
if ($Dirty) {
    $CommitVersion = "$CommitVersion-dirty"
}

$BuildTimeUtc = [DateTime]::UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", [System.Globalization.CultureInfo]::InvariantCulture)
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
