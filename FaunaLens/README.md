# Fauna Lens 0.6.0

Aplicativo Android em C#/.NET 10 para identificação de fauna com BioCLIP v1 offline e BioCLIP 2 online opcional.

## Novidade principal: FaunaPack

O aplicativo agora possui fluxo completo para instalar e utilizar arquivos `.faunapack` gerados pelo FaunaPack Builder.

Fluxo:

1. Toque em **Importar FaunaPack**.
2. Selecione o arquivo `.faunapack` no armazenamento do Android.
3. O aplicativo copia o arquivo para o armazenamento privado.
4. O `FaunaPackReader` extrai e valida o pacote.
5. `manifest.json`, `species.json`, `embeddings.bin` e imagens são conferidos.
6. Os embeddings BioCLIP v1 são carregados e associados às espécies.
7. O pacote passa a ser o catálogo offline ativo.
8. Ao identificar uma foto, o embedding da foto é comparado com o catálogo do pacote.

## Gerenciamento de pacotes

**Gerenciar FaunaPacks** mostra os pacotes instalados e permite selecionar qual deles será usado pelo reconhecimento offline.

O `.faunapack` não precisa ser incorporado ao APK. Novos biomas podem ser instalados posteriormente sem recompilar o aplicativo.

## Compatibilidade atual

O leitor espera o formato FaunaPack versão 1:

- BioCLIP v1 image encoder
- embeddings de 512 dimensões
- `manifest.json`
- `species.json`
- `embeddings.bin`
- pasta `images/`
- quantidade variável de referências por espécie, conforme o manifesto

O pacote é validado antes de ser ativado. Arquivos com caminhos inválidos, estrutura incompatível ou embeddings inconsistentes são rejeitados.

## Build

```powershell
cd FaunaLens
Remove-Item -Recurse -Force bin,obj -ErrorAction SilentlyContinue
dotnet restore
dotnet build -c Release
```

O projeto mantém `AndroidFastDeployment=false` para evitar o problema de assemblies ausentes durante a execução em dispositivo.


## Operações em segundo plano

O download do BioCLIP offline agora é executado por um Foreground Service do Android, separado da `MainActivity`. Fechar a tela do aplicativo não cancela o download.

O arquivo parcial é preservado e pode ser retomado por HTTP Range. O estado é salvo em `models/bioclip_download.json`, e a abertura seguinte do aplicativo verifica automaticamente uma operação interrompida.

O modelo somente é promovido para `models/bioclip_image.onnx` depois de ser validado pelo ONNX Runtime.

Consulte `docs/LIFECYCLE.md` para os detalhes e limitações do ciclo de vida do Android.
