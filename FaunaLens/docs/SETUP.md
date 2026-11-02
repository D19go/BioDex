# Setup completo — Fauna Lens

## 1. Android

O projeto usa .NET 10 for Android.

Verifique:

```powershell
dotnet --info
dotnet workload list
adb version
```

## 2. Preparar os modelos

Os modelos oficiais são:

- `imageomics/bioclip` — BioCLIP original, ViT-B/16.
- `imageomics/bioclip-2` — BioCLIP 2, ViT-L/14.

Os autores documentam o uso de ambos com OpenCLIP. citeturn0search4turn0search1

Entre em `tools`:

```powershell
cd tools
py -3 -m venv .venv
.\.venv\Scripts\Activate.ps1
python -m pip install --upgrade pip
pip install -r requirements.txt
```

Baixe os checkpoints:

```powershell
python .\download_models.py --model all --output ..\Models
```

Exportação do modelo local:

```powershell
python .\export_model.py `
  --model bioclip `
  --output ..\Models\exported\bioclip_image.onnx
```

Exportação opcional do BioCLIP 2:

```powershell
python .\export_model.py `
  --model bioclip2 `
  --output ..\Models\exported\bioclip2_image.onnx
```

O segundo arquivo é apenas para testes/experimentos. O servidor usa o BioCLIP 2 diretamente com OpenCLIP.

### Atalho

```powershell
.\prepare_models.ps1
```

## 3. Referências de espécies

Crie uma pasta:

```text
references/
└── pampa/
    ├── capybara/
    ├── maned_wolf/
    ├── marsh_deer/
    ├── pampas_cat/
    └── southern_crested_caracara/
```

Coloque várias imagens reais em cada pasta.

Quanto mais variadas forem as imagens — ângulo, distância, sexo, idade, iluminação, ambiente e postura — melhor será o protótipo de similaridade.

## 4. Pack offline

Use BioCLIP original:

```powershell
python .\create_pack.py `
  --model bioclip `
  --references ..\references\pampa `
  --catalog ..\Data\pampa_species.json `
  --output ..\Data\pampa_species_offline.json
```

Esse arquivo é incluído como Asset no APK.

## 5. Pack online

Use BioCLIP 2:

```powershell
python .\create_pack.py `
  --model bioclip2 `
  --references ..\references\pampa `
  --catalog ..\Data\pampa_species.json `
  --output ..\Server\data\pampa_species_online.json
```

## 6. Servidor

```powershell
cd ..\Server
py -3 -m venv .venv
.\.venv\Scripts\Activate.ps1
pip install -r requirements.txt
uvicorn main:app --host 0.0.0.0 --port 8000
```

Verifique:

```powershell
Invoke-RestMethod http://127.0.0.1:8000/health
```

## 7. Android

Gere o APK:

```powershell
cd ..
dotnet clean .\FaunaLens.csproj
dotnet restore .\FaunaLens.csproj
dotnet build .\FaunaLens.csproj -c Release
```

Instale o APK diretamente da pasta `bin\Release\net10.0-android\`.

## 8. Instalar o BioCLIP no telefone

Abra o aplicativo e toque em:

**Baixar BioCLIP offline**

O aplicativo baixa o encoder ONNX, valida o arquivo com ONNX Runtime e instala o modelo no armazenamento privado. Não é necessário abrir o gerenciador de arquivos.

Para testes com um ONNX próprio, use o botão secundário:

**Importar BioCLIP ONNX**

## 9. Configurar online

Informe o endereço do servidor no campo:

```text
Servidor BioCLIP 2 opcional
```

Sem endpoint, o aplicativo continua somente offline.

Com endpoint, o fluxo é híbrido:

```text
                 Foto
                  │
         ┌────────┴────────┐
         │                 │
      Android           Internet
         │                 │
      BioCLIP          BioCLIP 2
      original          servidor
         │                 │
      Top-5 local      Top-5 online
```

## 10. Regra de compatibilidade dos packs

Não use o catálogo BioCLIP 2 no Android.

Não use o catálogo BioCLIP no servidor BioCLIP 2.

Os embeddings são produzidos pelo encoder específico de cada modelo e devem permanecer separados.
