param([string]$Dotnet = 'dotnet')
$ErrorActionPreference='Stop'
Push-Location $PSScriptRoot
try {
    & $Dotnet publish src/App/App.csproj -c Release -r win-x64 --self-contained true -o artifacts/app
    if($LASTEXITCODE){throw 'App publish failed'}
    Copy-Item LICENSE,THIRD-PARTY.md,README.md artifacts/app
    Compress-Archive -Path artifacts/app/* -DestinationPath artifacts/app.zip -Force
    & $Dotnet publish src/Installer/Installer.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o artifacts/setup
    if($LASTEXITCODE){throw 'Installer publish failed'}
    Get-FileHash artifacts/setup/NvidiaClipManagerSetup.exe -Algorithm SHA256
} finally { Pop-Location }
