$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
Push-Location $projectRoot
try {
    dotnet build plugin/Jellyfin.Plugin.Danmaku.csproj -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Plugin build failed' }
    New-Item -ItemType Directory -Path artifacts -Force | Out-Null
    $pluginVersion = (Get-Content manifest.json -Raw | ConvertFrom-Json).version
    $packagePath = "artifacts/jellyfin-danmaku-$pluginVersion.zip"
    Compress-Archive -Path plugin/bin/Release/net10.0/Jellyfin.Plugin.Danmaku.dll,manifest.json,LICENSE,README.md,docs -DestinationPath $packagePath -Force
    Get-FileHash $packagePath -Algorithm SHA256
} finally { Pop-Location }
