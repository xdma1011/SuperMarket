param(
    [Parameter(Mandatory=$true)][string]$AppSettingsPath,
    [Parameter(Mandatory=$true)][string]$FactoryPath,
    [Parameter(Mandatory=$true)][string]$ValueFile
)

# Called from update-database.bat. Replaces the connection string in both
# places it exists in this repo, so they never drift apart:
#   1. appsettings.json  -> ConnectionStrings:DefaultConnection (used at
#      runtime by the API, and by `dotnet ef database update`)
#   2. AppDbContextFactory.cs -> the hardcoded design-time-only string used
#      by `dotnet ef migrations add` (never opens a real connection, only
#      needs the right provider - see the comment in that file)
#
# The new value is read from a file rather than passed as a normal
# argument, because connection strings routinely contain backslashes and
# semicolons that are painful to pass safely across the cmd.exe -> PowerShell
# argument boundary. The file is written by the caller right before this
# script runs and is expected to be deleted by the caller afterward.

$ErrorActionPreference = "Stop"

if (-not (Test-Path -LiteralPath $ValueFile)) {
    Write-Error "Value file not found: $ValueFile"
    exit 1
}
$newValue = (Get-Content -LiteralPath $ValueFile -Raw).TrimEnd("`r", "`n")

if ([string]::IsNullOrWhiteSpace($newValue)) {
    Write-Error "New connection string is empty - refusing to write an empty value."
    exit 1
}

if (-not (Test-Path -LiteralPath $AppSettingsPath)) {
    Write-Error "appsettings.json not found: $AppSettingsPath"
    exit 1
}
if (-not (Test-Path -LiteralPath $FactoryPath)) {
    Write-Error "AppDbContextFactory.cs not found: $FactoryPath"
    exit 1
}

# --- 1. appsettings.json (JSON-aware edit, not text substitution) ---
$json = Get-Content -LiteralPath $AppSettingsPath -Raw | ConvertFrom-Json
if (-not $json.ConnectionStrings) {
    Write-Error "appsettings.json has no ConnectionStrings section - refusing to guess where to put it."
    exit 1
}
$json.ConnectionStrings.DefaultConnection = $newValue
($json | ConvertTo-Json -Depth 20) | Set-Content -LiteralPath $AppSettingsPath -Encoding utf8

# --- 2. AppDbContextFactory.cs (the one quoted string literal passed to UseSqlServer) ---
$factoryContent = Get-Content -LiteralPath $FactoryPath -Raw
$escapedForCSharpLiteral = $newValue -replace '\\', '\\\\' -replace '"', '\"'
$pattern = 'optionsBuilder\.UseSqlServer\(\s*"[^"]*"'
$replacement = 'optionsBuilder.UseSqlServer(' + [Environment]::NewLine + '            "' + $escapedForCSharpLiteral + '"'
$updatedFactoryContent = [System.Text.RegularExpressions.Regex]::Replace($factoryContent, $pattern, { param($m) $replacement })

if ($updatedFactoryContent -eq $factoryContent) {
    Write-Error "Could not find the UseSqlServer(...) connection string in AppDbContextFactory.cs - file structure may have changed, refusing to guess."
    exit 1
}
Set-Content -LiteralPath $FactoryPath -Value $updatedFactoryContent -NoNewline -Encoding utf8

Write-Output "Connection string replaced in both files."
