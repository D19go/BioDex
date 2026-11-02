$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $projectRoot

$publishDir = Join-Path $projectRoot 'publish'
$packageVersion = '1.30.0'

Write-Host '=== FaunaPack Builder 1.1.4 ==='
Write-Host 'Limpando publicação anterior...'
if (Test-Path $publishDir) {
    Remove-Item $publishDir -Recurse -Force
}

Write-Host 'Restaurando pacotes...'
dotnet restore .\FaunaPackBuilder.csproj

Write-Host 'Publicando x64 self-contained...'
dotnet publish .\FaunaPackBuilder.csproj -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=false `
    -p:PublishTrimmed=false `
    -o $publishDir

# O Microsoft.ML.OnnxRuntime possui componentes gerenciados e nativos.
# Em alguns ambientes/.NET SDKs, os assets nativos podem não ser copiados
# automaticamente para o publish. O Builder copia explicitamente os DLLs
# do pacote NuGet para evitar uma publicação incompleta.
Write-Host 'Localizando pacote NuGet do ONNX Runtime...'
$nugetInfo = dotnet nuget locals global-packages --list
$globalPackages = ($nugetInfo | Select-String -Pattern '^global-packages:\s*(.+)$').Matches.Groups[1].Value.Trim()

if ([string]::IsNullOrWhiteSpace($globalPackages)) {
    throw 'Não foi possível descobrir a pasta global de pacotes NuGet.'
}

$globalPackages = $globalPackages.TrimEnd('\') + '\'
$nativePackage = Join-Path $globalPackages "microsoft.ml.onnxruntime\$packageVersion"
$managedPackage = Join-Path $globalPackages "microsoft.ml.onnxruntime.managed\$packageVersion"

if (-not (Test-Path $nativePackage)) {
    throw "Pacote Microsoft.ML.OnnxRuntime $packageVersion não encontrado em: $nativePackage"
}

$nativeCandidates = @(
    (Join-Path $nativePackage 'runtimes\win-x64\native'),
    (Join-Path $nativePackage 'runtimes\win-x64\nativeassets')
)

$nativeDir = $nativeCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $nativeDir) {
    throw "A pasta nativa win-x64 do ONNX Runtime não foi encontrada em: $nativePackage"
}

$nativeDlls = Get-ChildItem $nativeDir -Filter '*.dll' -File
if ($nativeDlls.Count -eq 0) {
    throw "Nenhuma DLL nativa do ONNX Runtime foi encontrada em: $nativeDir"
}

foreach ($dll in $nativeDlls) {
    Copy-Item $dll.FullName (Join-Path $publishDir $dll.Name) -Force
    Write-Host "  Copiado: $($dll.Name)"
}

# Garante também a DLL gerenciada correta.
$managedDll = $null
if (Test-Path $managedPackage) {
    $managedDll = Get-ChildItem $managedPackage -Recurse -Filter 'Microsoft.ML.OnnxRuntime.dll' -File |
        Where-Object { $_.FullName -match '\\lib\\' } |
        Select-Object -First 1
}

if (-not $managedDll) {
    $managedDll = Get-ChildItem $nativePackage -Recurse -Filter 'Microsoft.ML.OnnxRuntime.dll' -File |
        Select-Object -First 1
}

if ($managedDll) {
    Copy-Item $managedDll.FullName (Join-Path $publishDir 'Microsoft.ML.OnnxRuntime.dll') -Force
    Write-Host "  Copiado: Microsoft.ML.OnnxRuntime.dll"
} else {
    throw 'Microsoft.ML.OnnxRuntime.dll não foi encontrado no cache NuGet.'
}

# Verificação obrigatória antes de considerar o publish pronto.
$required = @(
    'FaunaPackBuilder.exe',
    'Microsoft.ML.OnnxRuntime.dll',
    'onnxruntime.dll',
    'onnxruntime_providers_shared.dll'
)

$missing = @($required | Where-Object { -not (Test-Path (Join-Path $publishDir $_)) })
if ($missing.Count -gt 0) {
    throw "Publicação incompleta. Arquivos ausentes: $($missing -join ', ')"
}

Write-Host ''
Write-Host '=== VERIFICAÇÃO DO ONNX RUNTIME ==='
Get-ChildItem $publishDir -Filter '*onnxruntime*.dll' -File |
    Select-Object Name, Length |
    Format-Table -AutoSize

Write-Host ''
Write-Host "Pronto: $publishDir\FaunaPackBuilder.exe"
@'
@echo off
cd /d "%~dp0"
start "" "%~dp0FaunaPackBuilder.exe"
'@ | Set-Content -Encoding ASCII (Join-Path $publishDir 'FaunaPackBuilder.cmd')

Write-Host 'IMPORTANTE: mantenha a pasta publish inteira.'
Write-Host "Launcher: $publishDir\FaunaPackBuilder.cmd"
