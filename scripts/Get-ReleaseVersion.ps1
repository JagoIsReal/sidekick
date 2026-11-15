[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$ProjectVersion,
    [string[]]$ExistingTags = @(),
    [string]$RequestedVersion = ''
)

$ErrorActionPreference = 'Stop'

function Read-Version([string]$Value) {
    if ($Value -cnotmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$') {
        throw 'Use a four-part version, such as 0.10.2.0.'
    }
    $parsed = [version]::Parse($Value)
    if (@($parsed.Major, $parsed.Minor, $parsed.Build, $parsed.Revision | Where-Object { $_ -gt 65534 }).Count -gt 0) {
        throw 'Assembly version components must be between 0 and 65534.'
    }
    $parsed
}

$baseline = Read-Version $ProjectVersion
$latest = $null
foreach ($tag in $ExistingTags) {
    if ($tag -cmatch '^v([0-9]+\.[0-9]+\.[0-9]+\.[0-9]+)$') {
        $tagVersion = Read-Version $Matches[1]
        if ($null -eq $latest -or $tagVersion -gt $latest) { $latest = $tagVersion }
    }
}

$candidate = $baseline
if (-not [string]::IsNullOrWhiteSpace($RequestedVersion)) {
    $candidate = Read-Version $RequestedVersion
}
elseif ($null -ne $latest -and $candidate -le $latest) {
    $candidate = Read-Version "$($latest.Major).$($latest.Minor).$($latest.Build + 1).0"
}
if ($candidate -lt $baseline -or ($null -ne $latest -and $candidate -le $latest)) {
    throw 'The release version must be newer than existing version tags and at least the project version.'
}
$candidate.ToString(4)
