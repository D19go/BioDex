# Arquitetura do Fauna Lens

## Decisão atual

O projeto usa dois modelos com funções diferentes:

```text
                         FOTO
                          |
             +------------+------------+
             |                         |
             v                         v
      BIOCLIP ORIGINAL             BIOCLIP 2
        ViT-B/16                    ViT-L/14
             |                         |
       Android/offline              servidor
             |                         |
       pack do bioma              pack online
             |                         |
             +------------+------------+
                          |
                       Top-N
```

### Offline — BioCLIP original

O BioCLIP original é ViT-B/16 e o checkpoint oficial é muito menor que o BioCLIP 2. Ele é a opção escolhida para o dispositivo porque o aplicativo precisa continuar útil sem internet.

O Android recebe apenas o encoder visual convertido para:

```text
Models/exported/bioclip_image.onnx
```

O APK não embute esse arquivo. O usuário instala o ONNX pelo botão do aplicativo, e o arquivo é copiado para o armazenamento privado em:

```text
/data/user/0/com.dsc.faunalens/files/models/bioclip_image.onnx
```

### Online — BioCLIP 2

O BioCLIP 2 é ViT-L/14 e fica no servidor. O servidor carrega:

```text
hf-hub:imageomics/bioclip-2
```

ou um cache local equivalente. O aplicativo só envia a fotografia quando o usuário configura um endpoint online.

## Packs

A mesma base de espécies é transformada em dois catálogos porque os embeddings precisam ser produzidos pelo mesmo modelo usado para consultar.

```text
Data/pampa_species.json
             |
       +-----+-----+
       |           |
       v           v
  BioCLIP       BioCLIP 2
       |           |
       v           v
pampa_species_  pampa_species_
offline.json    online.json
```

O APK usa somente o catálogo offline.

O servidor usa somente o catálogo online.

## Exportação

`tools/export_model.py` exporta somente o encoder visual. Isso evita levar o text encoder para o Android, já que a classificação local é feita por similaridade entre o embedding da fotografia e embeddings pré-calculados das espécies.

## Limitações atuais

- O recorte do animal ainda não existe; a foto inteira é enviada ao encoder.
- A classificação é Top-N por similaridade, não uma certeza absoluta.
- A qualidade do pack depende diretamente da quantidade e diversidade das imagens de referência.
- A exportação ONNX precisa ser validada no computador antes de ser copiada ao telefone.
