param([string]$GamePath = $env:CS1_GAME_PATH)

$ErrorActionPreference = 'Stop'
if (-not $GamePath) { throw 'Pass -GamePath or set CS1_GAME_PATH to the Cities: Skylines 1 game directory.' }
$repo = Split-Path -Parent $PSScriptRoot
$source = Get-Content -LiteralPath (Join-Path $repo 'mod\Roads.cs') -Raw
if ($source -notmatch 'SampleRawHeightSmooth\(new Vector3\(x, 0f, z\)\)') {
    throw 'Roads.Terrain must call the world-space Vector3 height overload.'
}
if ($source -match 'SampleRawHeightSmooth\(x\s*,\s*z\)') {
    throw 'Roads.Terrain passed world coordinates to the raw-grid float overload.'
}

$cecil = Join-Path $GamePath 'Mono\lib\mono\2.0\Mono.Cecil.dll'
$assemblyPath = Join-Path $GamePath 'Cities_Data\Managed\Assembly-CSharp.dll'
if (-not (Test-Path -LiteralPath $cecil) -or -not (Test-Path -LiteralPath $assemblyPath)) {
    throw "Game Mono.Cecil.dll or Assembly-CSharp.dll not found under $GamePath"
}
Add-Type -Path $cecil
$assembly = [Mono.Cecil.AssemblyFactory]::GetAssembly($assemblyPath)
$terrain = $assembly.MainModule.Types | Where-Object FullName -eq 'TerrainManager'
$worldOverload = $terrain.Methods | Where-Object {
    $_.Name -eq 'SampleRawHeightSmooth' -and $_.Parameters.Count -eq 1 -and
    $_.Parameters[0].ParameterType.FullName -eq 'UnityEngine.Vector3'
}
$instructions = @($worldOverload.Body.Instructions)
$operands = @($instructions | ForEach-Object { "$($_.Operand)" })
foreach ($constant in @('16', '540', '0.015625')) {
    if ($constant -notin $operands) { throw "Expected world-space conversion factor $constant missing from game IL." }
}
if (-not ($operands | Where-Object { $_ -match 'TerrainManager::SampleRawHeightSmooth\(System.Single,System.Single\)' })) {
    throw 'World-space overload no longer delegates to the raw-grid overload.'
}
'Roads uses the world-space terrain overload; game IL conversion verified.'
