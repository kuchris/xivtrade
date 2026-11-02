$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    dotnet restore xivtrade.sln --locked-mode
    if ($LASTEXITCODE -ne 0) { throw 'Locked restore failed.' }
    dotnet build xivtrade.sln --configuration Release --no-restore --verbosity minimal
    if ($LASTEXITCODE -ne 0) { throw 'Release build failed.' }

    $output = Join-Path $PSScriptRoot 'xivtrade/bin/x64/Release'
    $manifest = Get-Content -LiteralPath (Join-Path $output 'xivtrade.json') -Raw | ConvertFrom-Json
    if ($manifest.InternalName -ne 'xivtrade' -or $manifest.DalamudApiLevel -ne 15) {
        throw 'Unexpected plugin manifest.'
    }
    $files = @('xivtrade.dll', 'xivtrade.deps.json', 'xivtrade.json') |
        ForEach-Object { Join-Path $output $_ }
    foreach ($file in $files) {
        if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { throw "Missing package file: $file" }
    }
    $artifacts = Join-Path $PSScriptRoot 'artifacts'
    New-Item -ItemType Directory -Path $artifacts -Force | Out-Null
    $files += Join-Path $PSScriptRoot 'LICENSE'
    $files += Join-Path $PSScriptRoot 'NOTICE.md'
    $zipPath = Join-Path $artifacts "xivtrade-$($manifest.AssemblyVersion).zip"
    Compress-Archive -LiteralPath $files -DestinationPath $zipPath -Force
    Write-Host "Package: $zipPath"
    Get-FileHash -LiteralPath $zipPath -Algorithm SHA256
} finally { Pop-Location }
