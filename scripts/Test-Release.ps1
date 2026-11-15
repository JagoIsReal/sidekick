[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$PackagePath,
    [Parameter(Mandatory = $true)][string]$RepositoryPath
)

$ErrorActionPreference = 'Stop'
$versionScript = Join-Path $PSScriptRoot 'Get-ReleaseVersion.ps1'
$cases = @(
    @{ Base = '0.10.2.0'; Tags = @(); Requested = ''; Expected = '0.10.2.0' },
    @{ Base = '0.10.2.0'; Tags = @('v0.10.2.0'); Requested = ''; Expected = '0.10.3.0' },
    @{ Base = '0.10.2.0'; Tags = @('v0.10.9.0', 'v0.10.10.0', 'v0.10.3.0'); Requested = ''; Expected = '0.10.11.0' },
    @{ Base = '1.0.0.0'; Tags = @('v0.10.2.0'); Requested = ''; Expected = '1.0.0.0' },
    @{ Base = '0.10.2.0'; Tags = @('unrelated', 'v0.10.2.0-preview'); Requested = ''; Expected = '0.10.2.0' },
    @{ Base = '0.10.2.0'; Tags = @('v0.10.2.0'); Requested = '0.11.0.0'; Expected = '0.11.0.0' }
)
foreach ($case in $cases) {
    $actual = & $versionScript -ProjectVersion $case.Base -ExistingTags $case.Tags -RequestedVersion $case.Requested
    if ($actual -cne $case.Expected) { throw "Unexpected release version: $actual; expected $($case.Expected)." }
}
foreach ($invalid in @('0.10.2.0', '0.10.1.0', '1.0', 'v1.0.0.0', '01.0.0.0', '1.0.65535.0', '1.0.0.0;exit')) {
    $rejected = $false
    try { $null = & $versionScript -ProjectVersion '0.10.2.0' -ExistingTags @('v0.10.2.0') -RequestedVersion $invalid }
    catch { $rejected = $true }
    if (-not $rejected) { throw "Invalid or reused version accepted: $invalid" }
}
$overflowRejected = $false
try { $null = & $versionScript -ProjectVersion '0.10.2.0' -ExistingTags @('v1.0.65534.0') }
catch { $overflowRejected = $true }
if (-not $overflowRejected) { throw 'Automatic version overflow was not rejected.' }

$manifest = & (Join-Path $PSScriptRoot 'Test-Package.ps1') -PackagePath $PackagePath
$raw = Get-Content -LiteralPath $RepositoryPath -Raw -Encoding UTF8
if (-not $raw.TrimStart().StartsWith('[')) { throw 'The repository manifest must be a JSON array.' }
$entries = @($raw | ConvertFrom-Json)
if ($entries.Count -ne 1) { throw 'The repository must contain exactly one plugin.' }
$entry = $entries[0]
foreach ($property in $manifest.PSObject.Properties) {
    if (($entry.($property.Name) | ConvertTo-Json -Compress) -cne ($property.Value | ConvertTo-Json -Compress)) {
        throw "Repository metadata differs from the package: $($property.Name)"
    }
}
$expected = "$($manifest.RepoUrl)/releases/download/v$($manifest.AssemblyVersion)/Sidekick-$($manifest.AssemblyVersion).zip"
if ($entry.DownloadLinkInstall -cne $expected -or $entry.DownloadLinkUpdate -cne $expected -or
    $entry.IsTestingExclusive -ne $false -or $entry.LastUpdate -le 0) {
    throw 'Invalid repository download links or release settings.'
}
Write-Output 'Release tooling checks passed: version selection, immutable versions, metadata and repository links.'
