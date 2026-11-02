# Ciclo de vida e operações em segundo plano — Fauna Lens 0.6.0

## Download do BioCLIP

O download não pertence mais à `MainActivity`. Ao tocar em `Baixar BioCLIP offline`, a Activity inicia `BioClipDownloadForegroundService` e pode ser destruída sem cancelar a operação.

O serviço:

1. cria uma notificação persistente;
2. grava o progresso em `models/bioclip_download.json`;
3. mantém o arquivo parcial em `models/bioclip_image.onnx.download`;
4. usa HTTP Range para continuar a partir do tamanho já salvo quando o servidor aceitar `206 Partial Content`;
5. se o servidor responder sem Range, recria o arquivo parcial de forma segura;
6. valida o arquivo completo com ONNX Runtime;
7. só depois move o arquivo para `models/bioclip_image.onnx`;
8. mantém o estado para que a próxima abertura do app possa recuperar a operação.

## Limitações reais do Android

Foreground Service é apropriado para uma operação iniciada pelo usuário que precisa continuar quando a Activity deixa a tela. Ainda assim, o sistema operacional ou o fabricante pode interromper processos em situações extremas. Se isso ocorrer, o arquivo parcial permanece e o aplicativo tenta continuar na próxima abertura.

Um usuário que use **Forçar parada** nas configurações do Android impede que o sistema reinicie o serviço até que o aplicativo seja aberto novamente.

## Identificação

A identificação continua ligada à tela porque é uma operação iniciada diretamente pelo usuário. O modelo e os FaunaPacks permanecem no armazenamento privado do aplicativo e não dependem da Activity continuar viva para existir.

## 0.6.1 — correção do tipo de Foreground Service

O serviço de download usa `Android.Content.PM.ForegroundService.TypeDataSync`, que é o enum atual do .NET for Android. O tipo `ForegroundServiceType` não existe nessa API.

O serviço continua declarado como `dataSync`, apropriado para download/transferência de dados, com as permissões `FOREGROUND_SERVICE` e `FOREGROUND_SERVICE_DATA_SYNC` no manifesto.
