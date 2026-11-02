# BioDex 🐾

**Português** | [English](#english)

> ⚠️ **Status: em desenvolvimento.** Nenhuma função está finalizada e há erros conhecidos. O projeto ainda não foi validado em aparelhos reais.

Aplicativo Android que identifica animais a partir de fotos usando inteligência artificial, pensado para quem está em trilhas e áreas com pouco ou nenhum sinal de internet.

## Para quem é

Pessoas curiosas, como trilheiros e entusiastas da natureza, até estudantes e biólogos.

## Como funciona

1. Você tira uma foto (ou escolhe uma da galeria).
2. O modelo **BioCLIP** transforma a foto em um vetor numérico (*embedding*) direto no celular, via ONNX Runtime.
3. O app compara esse vetor com os de um catálogo de espécies (o *FaunaPack*) e mostra as mais parecidas.

Tudo isso roda **offline**, depois que o modelo e o catálogo estão instalados.

## Visão do projeto

A meta final é usar a **localização do usuário** para descobrir o bioma onde ele está e carregar só os animais daquela região. Com menos candidatos, a busca fica mais rápida e mais precisa.

Hoje o escopo é o **Rio Grande do Sul** (bioma Pampa), com um catálogo de demonstração de 5 espécies.

## Estado atual

| Item | Situação |
|---|---|
| Foto pela câmera e pela galeria | 🚧 Em desenvolvimento |
| Identificação offline (BioCLIP em ONNX) | 🚧 Em desenvolvimento |
| Importação de catálogos `.faunapack` | 🚧 Em desenvolvimento |
| Download do modelo em segundo plano | 🚧 Em desenvolvimento |
| Identificação pela localização / bioma | 📝 Planejado |
| Criar catálogos dentro do próprio app | 📝 Planejado |

## Limitações

- O resultado é uma **estimativa por similaridade**, não uma certeza. Confirme com um guia de campo ou um especialista.
- O catálogo atual tem poucas espécies e poucas fotos de referência por espécie, então os erros são esperados.
- O app ainda não diz "não reconheci": mesmo uma foto de outra coisa pode retornar uma espécie.
- A foto inteira é analisada; ainda não há recorte do animal.

## Roadmap

1. Corrigir os erros conhecidos e testar em aparelho físico.
2. Aceitar "não reconheci" quando a confiança for baixa.
3. Melhorar a precisão com mais referências por espécie.
4. Detectar o bioma pela localização e carregar só os animais da região.
5. Gerar e atualizar os catálogos dentro do app Android.
6. Histórico de animais encontrados (o "BioDex" de verdade).

## Créditos e licenças

- Modelo: [BioCLIP](https://github.com/Imageomics/bioclip) (Imageomics).
- Dados de ocorrência e imagens de referência: [GBIF](https://www.gbif.org). As imagens têm licenças próprias e exigem atribuição.
- Inferência: [ONNX Runtime](https://onnxruntime.ai).
- Licença do código: **a definir**.

## Contato

**Diogo da Silva Costa** — LinkedIn: [linkedin.com/in/diogo-da-silva-costa](https://www.linkedin.com/in/diogo-da-silva-costa)

---

<a id="english"></a>

# BioDex 🐾 (English)

> ⚠️ **Status: work in progress.** No feature is finished yet and there are known bugs. The project has not been validated on real devices.

An Android app that identifies animals from photos using AI, designed for hikers and anyone in areas with weak or no internet.

## Who it's for

Curious people such as hikers and nature enthusiasts, up to students and biologists.

## How it works

1. Take a photo (or pick one from the gallery).
2. The **BioCLIP** model turns it into an embedding vector on the phone, using ONNX Runtime.
3. The app compares that vector with a species catalog (a *FaunaPack*) and shows the closest matches.

It runs **offline** once the model and the catalog are installed.

## Vision

The end goal is to use the **user's location** to find their biome and load only the animals from that region, making searches faster and more accurate.

Today the scope is **Rio Grande do Sul, Brazil** (Pampa biome), with a demo catalog of 5 species.

## Current status

| Item | Status |
|---|---|
| Photo from camera or gallery | 🚧 In development |
| Offline identification (BioCLIP via ONNX) | 🚧 In development |
| Importing `.faunapack` catalogs | 🚧 In development |
| Background model download | 🚧 In development |
| Identification by location / biome | 📝 Planned |
| Building catalogs inside the app | 📝 Planned |

## Limitations

- Results are a **similarity-based estimate**, not a certainty. Confirm with a field guide or an expert.
- The current catalog has few species and few reference photos per species, so mistakes are expected.
- The app can't yet say "I don't recognize this": a photo of something else may still return a species.
- The whole photo is analyzed; there is no animal cropping yet.

## Roadmap

1. Fix known bugs and test on a physical device.
2. Return "not recognized" when confidence is low.
3. Improve accuracy with more references per species.
4. Detect the biome from location and load only that region's animals.
5. Build and update catalogs inside the Android app.
6. A history of animals found (the real "BioDex").

## Credits and licenses

- Model: [BioCLIP](https://github.com/Imageomics/bioclip) (Imageomics).
- Occurrence data and reference images: [GBIF](https://www.gbif.org). Images carry their own licenses and require attribution.
- Inference: [ONNX Runtime](https://onnxruntime.ai).
- Code license: **to be decided**.

## Contact

**Diogo da Silva Costa** — LinkedIn: [linkedin.com/in/diogo-da-silva-costa](https://www.linkedin.com/in/diogo-da-silva-costa)
