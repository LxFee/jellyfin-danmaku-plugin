$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
Push-Location $projectRoot
try {
    dotnet build plugin/Jellyfin.Plugin.Danmaku.csproj -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Plugin build failed' }
    New-Item -ItemType Directory -Path artifacts -Force | Out-Null
    Compress-Archive -Path plugin/bin/Release/net10.0/Jellyfin.Plugin.Danmaku.dll,manifest.json,LICENSE,README.md,docs -DestinationPath artifacts/jellyfin-danmaku-1.0.0.0.zip -Force
    Get-FileHash artifacts/jellyfin-danmaku-1.0.0.0.zip -Algorithm SHA256
} finally { Pop-Location }
