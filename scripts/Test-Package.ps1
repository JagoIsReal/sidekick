[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$PackagePath
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$projectPath = Join-Path $PSScriptRoot '../src/Sidekick/Sidekick.csproj'
[xml]$project = Get-Content -LiteralPath $projectPath -Raw
$expectedVersion = [string]$project.Project.PropertyGroup.Version
$expectedApi = [int]$project.Project.PropertyGroup.DalamudApiLevel
$archive = [IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $PackagePath).Path)
try {
    # This plugin has no private runtime dependencies. Reject accidentally bundled Dalamud/test assemblies.
    $allowed = @('Sidekick.dll', 'Sidekick.json', 'Sidekick.deps.json', 'images/icon.png')
    if ($archive.Entries.Count -lt $allowed.Count) { throw 'Unexpected archive entry count.' }
    $seen = @{}
    foreach ($entry in $archive.Entries) {
        $normalizedName = $entry.FullName.Replace('\', '/')
        $isTranslation = $normalizedName -match '^([a-z]{2,3}(?:-[A-Za-z0-9]{2,8})*)/Sidekick\.resources\.dll$'
        if ($isTranslation) { $null = [Globalization.CultureInfo]::GetCultureInfo($Matches[1]) }
        if (($normalizedName -notin $allowed -and -not $isTranslation) -or $entry.Length -eq 0 -or $seen.ContainsKey($normalizedName)) {
            throw "Unexpected or empty archive entry: $($entry.FullName)"
        }
        $seen[$normalizedName] = $true
    }
    foreach ($name in $allowed) {
        if (-not $seen.ContainsKey($name)) { throw "Missing archive entry: $name" }
    }
    if (-not $seen.ContainsKey('fr/Sidekick.resources.dll')) {
        throw 'The French translation is missing from the package.'
    }

    $reader = New-Object IO.StreamReader($archive.GetEntry('Sidekick.json').Open())
    try { $manifest = $reader.ReadToEnd() | ConvertFrom-Json } finally { $reader.Dispose() }
    if ($manifest.InternalName -ne 'Sidekick' -or $manifest.Name -ne 'Sidekick') {
        throw 'Plugin identity is inconsistent.'
    }
    if ($manifest.AssemblyVersion -ne $expectedVersion -or $manifest.DalamudApiLevel -ne $expectedApi) {
        throw 'Packaged version/API does not match the project.'
    }
    if ($manifest.LoadSync -ne $true -or $manifest.CanUnloadAsync -ne $false) {
        throw 'Native integrations require synchronous framework-thread loading and unloading.'
    }
    if ($manifest.IconUrl -ne [string]$project.Project.PropertyGroup.IconUrl) { throw 'Packaged icon URL does not match the project.' }
    if ($manifest.Description -ne [string]$project.Project.PropertyGroup.Description) { throw 'Packaged description does not match the project.' }

    $iconEntry = $archive.Entries | Where-Object { $_.FullName.Replace('\', '/') -eq 'images/icon.png' }
    $iconStream = New-Object IO.MemoryStream
    $iconSource = $iconEntry.Open()
    try { $iconSource.CopyTo($iconStream); $iconBytes = $iconStream.ToArray() }
    finally { $iconSource.Dispose(); $iconStream.Dispose() }
    if ($iconBytes.Length -lt 24 -or [BitConverter]::ToString($iconBytes, 0, 8) -ne '89-50-4E-47-0D-0A-1A-0A') {
        throw 'Packaged icon is not a PNG.'
    }
    $iconWidth = [Net.IPAddress]::NetworkToHostOrder([BitConverter]::ToInt32($iconBytes, 16))
    $iconHeight = [Net.IPAddress]::NetworkToHostOrder([BitConverter]::ToInt32($iconBytes, 20))
    if ($iconWidth -ne $iconHeight -or $iconWidth -lt 64 -or $iconWidth -gt 512) { throw 'Invalid packaged icon dimensions.' }
    $iconPath = Join-Path $PSScriptRoot '../icon.png'
    if ([Convert]::ToBase64String($iconBytes) -ne [Convert]::ToBase64String([IO.File]::ReadAllBytes($iconPath))) {
        throw 'Packaged icon differs from the source icon.'
    }
    if ($manifest.Author -ne [string]$project.Project.PropertyGroup.Author) { throw 'Packaged author does not match the project.' }
    if ($manifest.RepoUrl -ne [string]$project.Project.PropertyGroup.RepoUrl) { throw 'Packaged source URL does not match the project.' }
    $manifest
}
finally {
    $archive.Dispose()
}

