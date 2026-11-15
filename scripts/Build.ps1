[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$localDotnet = Join-Path $root '.tools/dotnet/dotnet.exe'
$dotnet = if (Test-Path -LiteralPath $localDotnet) { $localDotnet } else { (Get-Command dotnet -ErrorAction Stop).Source }
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
if (Test-Path -LiteralPath $localDotnet) {
    if (-not $env:DOTNET_CLI_HOME) { $env:DOTNET_CLI_HOME = Join-Path $root '.tools/cli' }
    if (-not $env:NUGET_PACKAGES) { $env:NUGET_PACKAGES = Join-Path $root '.tools/nuget' }
}

Push-Location $root
try {
    & $dotnet restore Sidekick.sln --locked-mode --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Dependency restore failed.' }
    & $dotnet build Sidekick.sln -c Release --no-restore --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    & $dotnet format Sidekick.sln whitespace --verify-no-changes --no-restore --verbosity minimal
    if ($LASTEXITCODE -ne 0) { throw 'Whitespace validation failed.' }
    & $dotnet test Sidekick.sln -c Release --no-build --no-restore --nologo --logger trx --results-directory artifacts/TestResults
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }

    $output = Join-Path $root 'src/Sidekick/bin/x64/Release'
    $package = Join-Path $output 'Sidekick/latest.zip'
    $manifest = & (Join-Path $PSScriptRoot 'Test-Package.ps1') -PackagePath $package
    $assemblyVersion = [Reflection.AssemblyName]::GetAssemblyName((Join-Path $output 'Sidekick.dll')).Version.ToString()
    if ($assemblyVersion -ne $manifest.AssemblyVersion) { throw 'Assembly and manifest versions differ.' }

    $artifacts = Join-Path $root 'artifacts'
    New-Item -ItemType Directory -Force -Path $artifacts | Out-Null
    $fileName = "Sidekick-$assemblyVersion.zip"
    $destination = Join-Path $artifacts $fileName
    Copy-Item -LiteralPath $package -Destination $destination -Force
    $hash = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText("$destination.sha256", "$hash  $fileName`n", (New-Object Text.UTF8Encoding($false)))

    Write-Output "Validated local package: $destination"
    Write-Output "SHA256: $hash"
}
finally {
    Pop-Location
}

