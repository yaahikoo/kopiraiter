$ErrorActionPreference = "Stop"

$outputDirectory = Join-Path $PSScriptRoot "publish\win-x64"

dotnet publish "$PSScriptRoot\Kopiraiter.csproj" `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    --output $outputDirectory `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=None `
    -p:DebugSymbols=false

Write-Host "Готово: $outputDirectory\Kopiraiter.exe"
