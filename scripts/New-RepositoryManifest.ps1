[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$PackagePath,
    [Parameter(Mandatory = $true)][string]$OutputPath
)

$ErrorActionPreference = 'Stop'
# The packaged manifest is the single source for release metadata.
$manifest = & (Join-Path $PSScriptRoot 'Test-Package.ps1') -PackagePath $PackagePath
$repository = ([string]$manifest.RepoUrl).TrimEnd('/')
if ($repository -cnotmatch '^https://github\.com/[A-Za-z0-9-]+/[A-Za-z0-9_.-]+$') {
    throw 'The release workflow requires a GitHub source URL.'
}
$version = [string]$manifest.AssemblyVersion
$download = "$repository/releases/download/v$version/Sidekick-$version.zip"
$manifest | Add-Member -NotePropertyName DownloadLinkInstall -NotePropertyValue $download -Force
$manifest | Add-Member -NotePropertyName DownloadLinkUpdate -NotePropertyValue $download -Force
$manifest | Add-Member -NotePropertyName LastUpdate -NotePropertyValue ([DateTimeOffset]::UtcNow.ToUnixTimeSeconds()) -Force
$manifest | Add-Member -NotePropertyName IsTestingExclusive -NotePropertyValue $false -Force
$json = ConvertTo-Json -InputObject @($manifest) -Depth 20
[IO.File]::WriteAllText([IO.Path]::GetFullPath($OutputPath), $json + "`n", (New-Object Text.UTF8Encoding($false)))
Write-Output "Prepared repository manifest: $OutputPath"
