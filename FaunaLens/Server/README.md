# Servidor online do Fauna Lens

O servidor usa **BioCLIP 2 (ViT-L/14)** para a segunda opinião online. O aplicativo Android não precisa carregar o checkpoint de 1,71 GB: ele usa o BioCLIP original localmente e envia a foto para este servidor somente quando o usuário configurar um endpoint.

O checkpoint oficial do BioCLIP 2 é `imageomics/bioclip-2`.

## 1. Ambiente

Windows PowerShell:

```powershell
cd Server
py -3 -m venv .venv
.\.venv\Scripts\Activate.ps1
python -m pip install --upgrade pip
pip install -r requirements.txt
```

## 2. Catálogo online

Prepare um catálogo usando BioCLIP 2:

```powershell
python ..\tools\create_pack.py `
  --model bioclip2 `
  --references ..\references\pampa `
  --catalog ..\Data\pampa_species.json `
  --output .\data\pampa_species_online.json
```

O catálogo online deve ter `embeddingModel: "bioclip2"` em cada espécie.

## 3. Executar

```powershell
uvicorn main:app --host 0.0.0.0 --port 8000
```

Teste:

```powershell
Invoke-RestMethod http://127.0.0.1:8000/health
```

O endpoint de identificação é:

```text
POST /identify
multipart/form-data
campo: image
```

## 4. Produção

Não use HTTP sem TLS na internet. Coloque o servidor atrás de HTTPS, autenticação, limite de tamanho de upload e rate limiting. No aplicativo, deixe `usesCleartextTraffic` desativado em uma configuração de produção.
