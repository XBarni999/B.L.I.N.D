param([string]$GameDir, [switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
$project = Split-Path $PSScriptRoot -Parent
$versionMatch = [regex]::Match((Get-Content (Join-Path $project 'BlindPlugin.cs') -Raw),'PluginVersion = "([^"]+)"')
if (!$versionMatch.Success) { throw 'PluginVersion not found' }
$version = $versionMatch.Groups[1].Value
if (!$SkipBuild) {
    $buildArgs = @('build',(Join-Path $project 'BLIND.csproj'),'-c','Release','--no-restore')
    if ($GameDir) { $buildArgs += "-p:GameDir=$GameDir" }
    & dotnet @buildArgs
    if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
}
$dist = Join-Path $project 'dist'
$stage = Join-Path $dist ("BLIND-v$version-" + (Get-Date -Format 'yyyyMMdd-HHmmss'))
$plugin = Join-Path $stage 'BepInEx\plugins\BLIND'
New-Item -ItemType Directory -Force $plugin | Out-Null
$assembly = Join-Path $project 'bin\Release\BLIND.dll'
$bundle = Join-Path $project 'bin\Release\blind-thermal.bundle'
if (!(Test-Path $bundle)) { throw 'Shader bundle missing from build output' }
# Refuse to distribute a diagnostic build accidentally copied into Release.
$dllText = [Text.Encoding]::UTF8.GetString([IO.File]::ReadAllBytes($assembly))
if ($dllText.Contains('ThermalRuntimeProbe')) { throw 'Diagnostic assembly cannot be packaged' }
Copy-Item -LiteralPath $assembly -Destination $plugin
Copy-Item -LiteralPath $bundle -Destination $plugin
Copy-Item -LiteralPath (Join-Path $project 'README.md') -Destination (Join-Path $stage 'README.md')
Copy-Item -LiteralPath (Join-Path $project 'CHANGELOG.md') -Destination $stage
Copy-Item -LiteralPath (Join-Path $project 'VALIDATION.md') -Destination $stage
$checks = foreach ($file in Get-ChildItem -LiteralPath $plugin -File) {
    $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  BepInEx/plugins/BLIND/$($file.Name)"
}
$checks | Set-Content -LiteralPath (Join-Path $stage 'SHA256SUMS.txt') -Encoding ascii
$archive = Join-Path $dist "BLIND-v$version.zip"
if (Test-Path $archive) { throw "Archive already exists: $archive. Preserve it or choose a new version before packaging." }
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $archive
$archiveHash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
"$archiveHash  BLIND-v$version.zip" | Set-Content -LiteralPath "$archive.sha256.txt" -Encoding ascii
Write-Output $archive
Write-Output $archiveHash
