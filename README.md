# BioDex 🐾

**Português** | [English](#english)

> ⚠️ **Status: em desenvolvimento.** Nenhuma função está finalizada e há erros conhecidos. Os testes foram feitos em um número pequeno de aparelhos, então o app ainda não foi validado em uma variedade maior de celulares.

Aplicativo Android que identifica animais a partir de fotos usando inteligência artificial, pensado para quem está em trilhas e áreas com pouco ou nenhum sinal de internet.

## Baixar o APK

O APK de teste mais recente fica na aba **[Releases](https://github.com/D19go/BioDex/releases/tag/ultima-versao)** (arquivo `BioDex-debug.apk`). Ele é gerado automaticamente e só é publicado depois que os testes e as compilações passam.

- Para instalar, permita a instalação de apps de fontes desconhecidas no Android.
- O APK tem cerca de 175 MB, porque já traz o modelo de IA (BioCLIP). Não é preciso baixar nada depois de instalar.
- Na primeira abertura, o app copia o modelo para o armazenamento privado dele (leva alguns segundos).
- Para criar o catálogo da sua região, use Wi-Fi e deixe o celular carregando (ver [Criar o FaunaPack da região](#criar-o-faunapack-da-região)).

## Para quem é

Pessoas curiosas, como trilheiros e entusiastas da natureza, até estudantes e biólogos.

## Como funciona

1. Você tira uma foto (ou escolhe uma da galeria).
2. O modelo **BioCLIP** transforma a foto em um vetor numérico (*embedding*) direto no celular, via ONNX Runtime.
3. O app compara esse vetor com os de um catálogo de espécies (o *FaunaPack*) e mostra as mais parecidas.

Tudo isso roda **offline**, depois que o catálogo da sua região está criado ou importado.

## Criar o FaunaPack da região

O FaunaPack é o catálogo de espécies que o app compara com a sua foto. Ele é criado no próprio celular, a partir de registros e fotos públicas do [GBIF](https://www.gbif.org):

- Cada pack tem até **50 espécies**, com até **50 fotos de referência** por espécie.
- A criação roda em **segundo plano**, com notificação de progresso. Pode levar **várias horas**.
- Se o Android pausar a criação, o progresso fica salvo. Abra o app e toque em **Atualizar FaunaPack** para continuar. Se o sistema encerrar o app no meio, a criação é retomada ao abrir o app de novo.
- Para a criação não ser interrompida, use Wi-Fi, deixe o celular carregando e toque em **Liberar o app da otimização de bateria** na tela de criação (escolha o Biodex e marque "Sem restrições").

Você também pode importar um arquivo `.faunapack` pronto.

## Visão do projeto

A meta final é usar a **localização do usuário** para descobrir o bioma onde ele está e carregar só os animais daquela região. Com menos candidatos, a busca fica mais rápida e mais precisa.

Hoje o escopo é o **Rio Grande do Sul, Brasil** (bioma Pampa). O APK não traz catálogo de espécies: a criação do pack da região é feita no aparelho (ver acima).

## Privacidade

- **Fotos:** a identificação acontece no celular. Nenhuma foto é enviada para servidor nenhum neste momento.
- **Localização:** o app pede a localização aproximada, com explicação, para descobrir o estado e escolher o pack da região. A coordenada **não é enviada nem guardada pelo app**. A conversão de coordenada em estado usa o geocodificador do Android, que em alguns aparelhos consulta serviços do Google. Você pode recusar e criar o pack escolhendo a região manualmente, ou não criar pack.
- **Placar:** a pontuação fica no celular. Quando há mais de uma instalação do app, o placar compartilhado fica no arquivo `Download/BioDex/placar.json`, que exige acesso a arquivos.
- **Metadados das fotos:** as fotos publicadas no app ainda guardam os dados da câmera, incluindo a localização, na cópia local. O serviço da rede social (abaixo) já remove esses dados no envio, mas a limpeza local ainda não foi feita.

## Estado atual

| Item | Situação |
|---|---|
| Tela inicial com feed, painel lateral e rodapé de navegação | 🧪 Em teste |
| Tema claro e escuro (fundo no estilo HUD) | 🧪 Em teste |
| Foto pela câmera e pela galeria | 🧪 Em teste |
| Identificação offline (BioCLIP em ONNX, embutido no APK) | 🧪 Em teste |
| Armazenamento privado do app para modelo e packs (migra dados de versões antigas) | 🧪 Em teste |
| Criação do pack da região em segundo plano, com rodadas, pausa e retomada | 🧪 Em teste |
| Download paralelo de até 8 fotos na criação do pack | 🧪 Em teste |
| Publicações com até 5 fotos, com pontos da identificação | 🧪 Em teste |
| Pontuação por identificação, colecionador, repetição e novos habitats | 🧪 Em teste |
| Ranking local e placar compartilhado entre instalações | 🧪 Em teste |
| Importação de catálogos `.faunapack` | 🧪 Em teste |
| Criar packs ao mudar de estado (localização, opcional) | 🚧 Em desenvolvimento |
| Servidor da rede social (contas anônimas, amizades, posts e feed) | 🚧 Em desenvolvimento (`Social/`, ainda não conectado ao app) |
| Rede social no app (amigos, compartilhamento de encontros) | 📝 Planejado |
| Packs de região prontos para baixar | 📝 Planejado |
| Identificação pela localização / bioma | 📝 Planejado |

## Limitações

- O resultado é uma **estimativa por similaridade**, não uma certeza. Confirme com um guia de campo ou um especialista.
- O catálogo criado no celular tem poucas fotos de referência por espécie quando a região tem pouco registro no GBIF, então os erros são esperados.
- O app ainda não diz "não reconheci": mesmo uma foto de outra coisa pode retornar uma espécie.
- A foto inteira é analisada; ainda não há recorte do animal.
- Criar o pack da região leva horas e depende do aparelho, da bateria e da rede.
- O Android pode limitar a criação em segundo plano (por exemplo, até 6 horas por dia). Nesse caso a criação pausa e continua quando você voltar ao app.
- Só o Android foi testado. Não há versão para iOS.

## Desenvolvimento

- **App Android (`FaunaLens/`):** projeto .NET 10 (`net10.0-android`). Compile com `dotnet build FaunaLens/FaunaLens.csproj -f net10.0-android`.
- **Testes (`FaunaLens.Tests/`):** xUnit, rodam sem Android (`dotnet test FaunaLens.Tests`). Cobrem packs, pontuação, pesos de embedding, cálculo de tempo e caminhos.
- **Modelo embutido:** o CI baixa o BioCLIP (`image_encoder.onnx`, do Hugging Face) para `FaunaLens/Assets/bioclip_image.onnx` antes de montar o APK. O arquivo não fica no git, por causa do tamanho.
- **Criador de packs (`FaunaPackBuilder/`):** app Windows que monta packs a partir do GBIF, com as mesmas regras do app.
- **Servidor da rede social (`Social/`):** FastAPI com SQLite. Contas anônimas por token, amizades, posts com 1 a 4 fotos, feed com 10 posts de amigos para 1 público, exclusão de conta. Fotos são reescritas sem metadados. Para rodar os testes: `pip install -r Social/requirements.txt` e `python -m pytest Social/tests`.
- **CI (`.github/workflows/ci.yml`):** roda os testes xUnit, os testes do servidor social, a checagem dos scripts Python, o build do Windows e os APKs (principal e clones). Só publica o APK na aba Releases se tudo passar.
- **Scripts de avaliação (`FaunaLens/tools/`):** `evaluate_identification.py` mede acerto no topo e calibração do limiar com uma pasta de fotos rotuladas; `quantize_model.py` gera uma versão menor do modelo (ainda não usada).

## Créditos e licenças

- Modelo: [BioCLIP](https://huggingface.co/imageomics/bioclip), com os pesos de [m3r1al/naturalist-bioclip-v1](https://huggingface.co/m3r1al/naturalist-bioclip-v1). Confirme a licença do modelo na página dele antes de distribuir o app comercialmente.
- Runtime de inferência: [ONNX Runtime](https://onnxruntime.ai) (licença MIT).
- Dados de ocorrência e imagens de referência: [GBIF](https://www.gbif.org). Cada imagem tem a licença do autor (CC0, CC BY, CC BY-NC ou CC BY-SA), que exige atribuição e pode limitar o uso comercial ou exigir a mesma licença em obras derivadas. Os packs guardam essas informações.
- **Licença do código:** todos os direitos reservados. Veja o arquivo [LICENSE](LICENSE).

## Contato

**Diogo da Silva Costa** — LinkedIn: [linkedin.com/in/diogo-da-silva-costa](https://www.linkedin.com/in/diogo-da-silva-costa)

---

<a id="english"></a>

# BioDex 🐾 (English)

[Português](#biodex-) | **English**

> ⚠️ **Status: in development.** No feature is finished and there are known bugs. Testing has been done on a small number of devices, so the app has not been validated on a wide range of phones yet.

An Android app that identifies animals from photos using artificial intelligence, designed for people on trails and in areas with little or no internet signal.

## Download the APK

The latest test APK is on the **[Releases](https://github.com/D19go/BioDex/releases/tag/ultima-versao)** tab (`BioDex-debug.apk`). It is built automatically and only published after tests and builds pass.

- To install it, allow installing apps from unknown sources on Android.
- The APK is about 175 MB because it already includes the AI model (BioCLIP). You don't need to download anything after installing.
- On first launch, the app copies the model to its private storage (this takes a few seconds).
- To build the catalog for your region, use Wi-Fi and keep the phone charging (see [Build the regional FaunaPack](#build-the-regional-faunapack)).

## Who it's for

Curious people such as hikers and nature enthusiasts, up to students and biologists.

## How it works

1. Take a photo (or pick one from the gallery).
2. The **BioCLIP** model turns it into an embedding vector on the phone, using ONNX Runtime.
3. The app compares that vector with a species catalog (a *FaunaPack*) and shows the closest matches.

It runs **offline** once your regional catalog is built or imported.

## Build the regional FaunaPack

The FaunaPack is the species catalog that the app compares your photo with. It is built on the phone from public records and photos from [GBIF](https://www.gbif.org):

- Each pack has up to **50 species**, with up to **50 reference photos** per species.
- The build runs in the **background**, with a progress notification. It can take **several hours**.
- If Android pauses the build, progress is saved. Open the app and tap **Update FaunaPack** to continue. If the system closes the app in the middle of a build, it resumes when you open the app again.
- To avoid interruptions, use Wi-Fi, keep the phone charging, and tap **Exempt the app from battery optimization** on the build screen (choose BioDex and set it to "No restrictions").

You can also import a ready-made `.faunapack` file.

## Project vision

The end goal is to use the **user's location** to find their biome and load only the animals from that region. Fewer candidates make searches faster and more accurate.

Today the scope is the **state of Rio Grande do Sul, Brazil** (Pampa biome). The APK ships no species catalog: the regional pack is built on the device (see above).

## Privacy

- **Photos:** identification happens on the phone. No photo is sent to any server at this time.
- **Location:** the app asks for approximate location, with an explanation, to find the state and choose the regional pack. The coordinate **is not sent or stored by the app**. Converting a coordinate into a state uses Android's geocoder, which on some devices queries Google services. You can decline and pick the region manually, or skip building a pack.
- **Scoreboard:** scores stay on the phone. When more than one installation of the app exists, the shared scoreboard lives in `Download/BioDex/placar.json`, which requires file access.
- **Photo metadata:** photos published in the app still keep camera data, including location, in the local copy. The social network server (below) strips this data on upload, but the local copy is not cleaned yet.

## Current status

| Item | Status |
|---|---|
| Home feed, side panel and navigation footer | 🧪 Testing |
| Light and dark theme (HUD-style background) | 🧪 Testing |
| Photo from camera or gallery | 🧪 Testing |
| Offline identification (BioCLIP via ONNX, embedded in the APK) | 🧪 Testing |
| Private app storage for model and packs (migrates data from older versions) | 🧪 Testing |
| Regional pack build in the background, with rounds, pause and resume | 🧪 Testing |
| Parallel download of up to 8 photos during the pack build | 🧪 Testing |
| Posts with up to 5 photos and identification points | 🧪 Testing |
| Scoring by identification, collector, repetition and new habitats | 🧪 Testing |
| Local ranking and shared scoreboard between installations | 🧪 Testing |
| Importing `.faunapack` catalogs | 🧪 Testing |
| Create packs when the state changes (location, optional) | 🚧 In development |
| Social network server (anonymous accounts, friends, posts and feed) | 🚧 In development (`Social/`, not connected to the app yet) |
| Social network in the app (friends, sharing encounters) | 📝 Planned |
| Ready-made regional packs to download | 📝 Planned |
| Identification by location / biome | 📝 Planned |

## Limitations

- Results are a **similarity-based estimate**, not a certainty. Confirm with a field guide or an expert.
- A catalog built on the phone has few reference photos per species when the region has few GBIF records, so mistakes are expected.
- The app can't yet say "not recognized": a photo of something else may still return a species.
- The whole photo is analyzed; there is no animal cropping yet.
- Building the regional pack takes hours and depends on the device, battery and network.
- Android may limit background work (for example, up to 6 hours per day). In that case the build pauses and continues when you return to the app.
- Only Android has been tested. There is no iOS version.

## Development

- **Android app (`FaunaLens/`):** .NET 10 project (`net10.0-android`). Build with `dotnet build FaunaLens/FaunaLens.csproj -f net10.0-android`.
- **Tests (`FaunaLens.Tests/`):** xUnit, run without Android (`dotnet test FaunaLens.Tests`). They cover packs, scoring, embedding weights, time calculations and paths.
- **Embedded model:** CI downloads BioCLIP (`image_encoder.onnx`, from Hugging Face) to `FaunaLens/Assets/bioclip_image.onnx` before building the APK. The file is not kept in git because of its size.
- **Pack builder (`FaunaPackBuilder/`):** Windows app that builds packs from GBIF, following the same rules as the app.
- **Social network server (`Social/`):** FastAPI with SQLite. Anonymous token accounts, friendships, posts with 1 to 4 photos, a feed with 10 friend posts for every 1 public post, and account deletion. Photos are rewritten without metadata. To run the tests: `pip install -r Social/requirements.txt` and `python -m pytest Social/tests`.
- **CI (`.github/workflows/ci.yml`):** runs the xUnit tests, the social server tests, the Python script checks, the Windows build and the APKs (main and clones). It publishes the APK to the Releases tab only if everything passes.
- **Evaluation scripts (`FaunaLens/tools/`):** `evaluate_identification.py` measures top-match accuracy and threshold calibration with a folder of labeled photos; `quantize_model.py` produces a smaller model (not used yet).

## Credits and licenses

- Model: [BioCLIP](https://huggingface.co/imageomics/bioclip), with weights from [m3r1al/naturalist-bioclip-v1](https://huggingface.co/m3r1al/naturalist-bioclip-v1). Check the model's license on its page before distributing the app commercially.
- Inference runtime: [ONNX Runtime](https://onnxruntime.ai) (MIT license).
- Occurrence data and reference images: [GBIF](https://www.gbif.org). Each image carries its author's license (CC0, CC BY, CC BY-NC or CC BY-SA), which requires attribution and may limit commercial use or require the same license for derived works. Packs keep this information.
- **Code license:** all rights reserved. See the [LICENSE](LICENSE) file.

## Contact

**Diogo da Silva Costa** — LinkedIn: [linkedin.com/in/diogo-da-silva-costa](https://www.linkedin.com/in/diogo-da-silva-costa)
