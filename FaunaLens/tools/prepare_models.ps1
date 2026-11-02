$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$venv = Join-Path $PSScriptRoot ".venv"
$python = Join-Path $venv "Scripts\python.exe"

if (-not (Test-Path $python)) {
    Write-Host "Criando ambiente virtual..."
    py -3 -m venv $venv
}

& $python -m pip install --upgrade pip
& $python -m pip install -r (Join-Path $PSScriptRoot "requirements.txt")

$models = Join-Path $root "Models"
$exported = Join-Path $models "exported"
New-Item -ItemType Directory -Force -Path $exported | Out-Null

Write-Host "Baixando BioCLIP e BioCLIP 2..."
& $python (Join-Path $PSScriptRoot "download_models.py") --model all --output $models

Write-Host "Exportando BioCLIP original para uso offline..."
& $python (Join-Path $PSScriptRoot "export_model.py") --model bioclip --output (Join-Path $exported "bioclip_image.onnx")

Write-Host "Exportando BioCLIP 2 para testes/servidor..."
& $python (Join-Path $PSScriptRoot "export_model.py") --model bioclip2 --output (Join-Path $exported "bioclip2_image.onnx")

Write-Host ""
Write-Host "Concluido. Arquivos ONNX:"
Write-Host (Join-Path $exported "bioclip_image.onnx")
Write-Host (Join-Path $exported "bioclip2_image.onnx")
