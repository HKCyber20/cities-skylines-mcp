param(
    [ValidateSet('Build', 'Install', 'Uninstall')]
    [string]$Action = 'Build',
    [string]$GamePath = $env:CS1_GAME_PATH,
    [string]$ManagedPath = $env:CS1_MANAGED,
    [string]$ModsPath = (Join-Path $env:LOCALAPPDATA 'Colossal Order\Cities_Skylines\Addons\Mods')
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$assembly = Join-Path $repo 'mod\bin\Release\CS1McpBridge.dll'

function Find-Game {
    param([string]$ExplicitPath)
    if ($ExplicitPath) {
        $candidate = if (Test-Path -LiteralPath $ExplicitPath -PathType Leaf) { Split-Path -Parent $ExplicitPath } else { $ExplicitPath }
        if (Test-Path -LiteralPath (Join-Path $candidate 'Cities_Data\Managed\ICities.dll')) { return (Resolve-Path -LiteralPath $candidate).Path }
        throw "No Cities: Skylines 1 installation at: $ExplicitPath"
    }

    $roots = New-Object System.Collections.Generic.List[string]
    foreach ($key in @('HKCU:\Software\Valve\Steam', 'HKLM:\SOFTWARE\WOW6432Node\Valve\Steam', 'HKLM:\SOFTWARE\Valve\Steam')) {
        try {
            if (Test-Path $key) {
                $entry = Get-ItemProperty -Path $key
                foreach ($value in @($entry.SteamPath, $entry.InstallPath)) { if ($value) { $roots.Add($value) } }
            }
        } catch [System.Security.SecurityException] { continue }
        catch [System.UnauthorizedAccessException] { continue }
    }
    foreach ($value in @((Join-Path ${env:ProgramFiles(x86)} 'Steam'), (Join-Path $env:ProgramFiles 'Steam'))) {
        if ($value) { $roots.Add($value) }
    }
    foreach ($root in @($roots)) {
        $vdf = Join-Path $root 'steamapps\libraryfolders.vdf'
        if (Test-Path -LiteralPath $vdf) {
            foreach ($match in [regex]::Matches((Get-Content -LiteralPath $vdf -Raw), '"path"\s*"([^"]+)"')) {
                $roots.Add(($match.Groups[1].Value -replace '\\\\', '\'))
            }
        }
    }
    foreach ($root in ($roots | Select-Object -Unique)) {
        $candidate = Join-Path $root 'steamapps\common\Cities_Skylines'
        if (Test-Path -LiteralPath (Join-Path $candidate 'Cities_Data\Managed\ICities.dll')) {
            return (Resolve-Path -LiteralPath $candidate).Path
        }
    }
    throw 'Cities: Skylines 1 was not found. Pass -GamePath or set CS1_GAME_PATH.'
}

if ($Action -ne 'Uninstall') {
    if (-not $ManagedPath) {
        $GamePath = Find-Game $GamePath
        $ManagedPath = Join-Path $GamePath 'Cities_Data\Managed'
    }
    $ManagedPath = (Resolve-Path -LiteralPath $ManagedPath).Path
    foreach ($dll in @('mscorlib.dll', 'System.dll', 'System.Core.dll', 'ICities.dll', 'ColossalManaged.dll', 'Assembly-CSharp.dll', 'UnityEngine.dll')) {
        if (-not (Test-Path -LiteralPath (Join-Path $ManagedPath $dll))) { throw "Missing game assembly: $dll in $ManagedPath" }
    }
    $names = @('DOTNET_CLI_HOME', 'APPDATA', 'DOTNET_SKIP_FIRST_TIME_EXPERIENCE', 'DOTNET_CLI_TELEMETRY_OPTOUT', 'NUGET_PACKAGES')
    $previous = @{}
    foreach ($name in $names) { $previous[$name] = [Environment]::GetEnvironmentVariable($name, 'Process') }
    try {
        $env:DOTNET_CLI_HOME = Join-Path $repo '.dotnet-home'
        $env:APPDATA = Join-Path $repo '.appdata'
        $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
        $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
        $env:NUGET_PACKAGES = Join-Path $repo '.nuget-packages'
        dotnet restore (Join-Path $repo 'mod\CS1McpBridge.csproj') --configfile (Join-Path $repo 'NuGet.Config') "-p:ManagedPath=$($ManagedPath -replace '\\', '/')"
        if ($LASTEXITCODE -ne 0) { throw 'Mod restore failed.' }
        dotnet build (Join-Path $repo 'mod\CS1McpBridge.csproj') -c Release --no-restore "-p:ManagedPath=$($ManagedPath -replace '\\', '/')"
        if ($LASTEXITCODE -ne 0) { throw 'Mod build failed.' }
    } finally {
        foreach ($name in $names) {
            if ($null -eq $previous[$name]) { Remove-Item -Path "Env:$name" -ErrorAction SilentlyContinue }
            else { [Environment]::SetEnvironmentVariable($name, $previous[$name], 'Process') }
        }
    }
    if (-not (Test-Path -LiteralPath $assembly)) { throw "Build output missing: $assembly" }
    Write-Host "Built against $ManagedPath"
}

if ($Action -eq 'Build') { return }

$target = Join-Path $ModsPath 'CS1McpBridge'
$targetDll = Join-Path $target 'CS1McpBridge.dll'
$statePath = Join-Path $target '.cs1mcp-install.json'
if ($Action -eq 'Install') {
    if (Get-Process -Name Cities -ErrorAction SilentlyContinue) { throw 'Close Cities: Skylines before installing the mod.' }
    New-Item -ItemType Directory -Path $target -Force | Out-Null
    if (-not (Test-Path -LiteralPath $statePath)) {
        $backup = $null
        if (Test-Path -LiteralPath $targetDll) {
            $backupDir = Join-Path (Split-Path -Parent $ModsPath) '.cs1mcp-backups'
            New-Item -ItemType Directory -Path $backupDir -Force | Out-Null
            $backup = Join-Path $backupDir ("CS1McpBridge-{0}.dll.bak" -f [guid]::NewGuid().ToString('N'))
            Copy-Item -LiteralPath $targetDll -Destination $backup
        }
        @{ backup = $backup } | ConvertTo-Json | Set-Content -LiteralPath $statePath -Encoding UTF8
    }
    Copy-Item -LiteralPath $assembly -Destination $targetDll -Force
    Write-Host "Installed $targetDll"
    return
}

if (Get-Process -Name Cities -ErrorAction SilentlyContinue) { throw 'Close Cities: Skylines before uninstalling the mod.' }
if (-not (Test-Path -LiteralPath $statePath)) { throw "No installation marker at $statePath; refusing to modify the mod directory." }
$state = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json
if ($state.backup) {
    $backupDir = [IO.Path]::GetFullPath((Join-Path (Split-Path -Parent $ModsPath) '.cs1mcp-backups'))
    $legacyBackupDir = [IO.Path]::GetFullPath((Join-Path $ModsPath '.cs1mcp-backups'))
    $backupPath = [IO.Path]::GetFullPath([string]$state.backup)
    if (-not ($backupPath.StartsWith(($backupDir + [IO.Path]::DirectorySeparatorChar), [StringComparison]::OrdinalIgnoreCase) -or
              $backupPath.StartsWith(($legacyBackupDir + [IO.Path]::DirectorySeparatorChar), [StringComparison]::OrdinalIgnoreCase))) {
        throw 'Install marker points outside the mod backup directory.'
    }
    if (-not (Test-Path -LiteralPath $backupPath)) { throw "Original DLL backup missing: $backupPath" }
    Copy-Item -LiteralPath $backupPath -Destination $targetDll -Force
    Remove-Item -LiteralPath $backupPath
} elseif (Test-Path -LiteralPath $targetDll) {
    Remove-Item -LiteralPath $targetDll
}
Remove-Item -LiteralPath $statePath
Write-Host "Uninstalled $targetDll"
