param (
    [string]$filename
 )

$RepoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$SafeRepoRoot = $RepoRoot.Path.Replace("\", "/")
$CommitVersion = & git -c "safe.directory=$SafeRepoRoot" -C $RepoRoot.Path describe --tags --long --always 2>$null
if ([string]::IsNullOrWhiteSpace($CommitVersion)) {
    $CommitVersion = "unknown"
}

$DirtyFiles = & git -c "safe.directory=$SafeRepoRoot" -C $RepoRoot.Path status --porcelain --untracked-files=no -- . ':!INSTALL/OUTPUT/sw2urdfSetup.exe' 2>$null
if ($DirtyFiles) {
    $CommitVersion = "$CommitVersion-dirty"
}

$FileContent = 'using System.Reflection;

[assembly: AssemblyInformationalVersion("{0}")]' -f $CommitVersion
$FileContent | Out-File $filename
