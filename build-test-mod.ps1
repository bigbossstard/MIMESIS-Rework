$ErrorActionPreference = 'Stop'
$env:DOTNET_ROLL_FORWARD = 'LatestPatch'
$gameRoot = $env:MIMESIS_DIR
if ([string]::IsNullOrWhiteSpace($gameRoot)) { $gameRoot = 'E:\SteamLibrary\steamapps\common\MIMESIS' }
$projects = @(
    (Join-Path $PSScriptRoot 'MimesisRework.Core\MimesisRework.Core.csproj'),
    (Join-Path $PSScriptRoot 'MimesisRework.Loot\MimesisRework.Loot.csproj'),
    (Join-Path $PSScriptRoot 'MimesisRework.Market\MimesisRework.Market.csproj'),
    (Join-Path $PSScriptRoot 'MimesisRework.Progression\MimesisRework.Progression.csproj'),
    (Join-Path $PSScriptRoot 'MimesisRework.Medical\MimesisRework.Medical.csproj')
)
$gameMods = Join-Path $gameRoot 'Mods'

foreach ($project in $projects) {
    dotnet build $project --configuration Release
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    $dll = Join-Path (Split-Path $project -Parent) "bin\Release\netstandard2.1\$([IO.Path]::GetFileNameWithoutExtension($project)).dll"
    Copy-Item -LiteralPath $dll -Destination $gameMods -Force
    Write-Host "Installed $dll to $gameMods"
}

# Retire the previous monolithic assembly only after both replacements built and copied.
$legacyDll = Join-Path $gameMods 'MimesisTestMod.dll'
if (Test-Path -LiteralPath $legacyDll) {
    Move-Item -LiteralPath $legacyDll -Destination ($legacyDll + '.disabled') -Force
    Write-Host "Disabled legacy monolithic module: $legacyDll.disabled"
}
