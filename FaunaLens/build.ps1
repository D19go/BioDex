$ErrorActionPreference = 'Stop'

Write-Host 'Restaurando...' -ForegroundColor Cyan
dotnet restore .\FaunaLens.csproj

Write-Host 'Compilando Release...' -ForegroundColor Cyan
dotnet build .\FaunaLens.csproj -c Release --no-restore

Write-Host 'Arquivos APK:' -ForegroundColor Green
Get-ChildItem .\bin\Release\net10.0-android\*.apk -ErrorAction SilentlyContinue | Select-Object FullName, Length
