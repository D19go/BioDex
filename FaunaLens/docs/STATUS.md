# Status — Fauna Lens 0.5.0

- [x] BioCLIP v1 ONNX offline
- [x] Catálogo offline
- [x] FaunaPack Reader
- [x] Importação de `.faunapack`
- [x] Validação do pacote
- [x] Carregamento de embeddings
- [x] Seleção de FaunaPack ativo
- [x] Lista de FaunaPacks instalados
- [x] Identificação offline usando o pack ativo
- [x] BioCLIP 2 online opcional
- [ ] Teste final em dispositivo com um `.faunapack` real

## 0.6.0 — ciclo de vida e download resiliente
- [x] Download do BioCLIP executado em Foreground Service.
- [x] Download parcial mantido em `models/bioclip_image.onnx.download`.
- [x] Retomada por HTTP Range quando o arquivo parcial existe.
- [x] Estado persistente em `models/bioclip_download.json`.
- [x] Recuperação automática ao abrir o aplicativo novamente.
- [x] Modelo final instalado somente após validação ONNX.
- [x] Fechamento da Activity não cancela o download.
- [ ] Teste final em aparelho físico com app fechado durante o download.

## 0.6.2 — correções da primeira revisão (não compiladas nem testadas em aparelho)
- [x] O modelo é carregado automaticamente quando o download termina (sem reiniciar o app) e o progresso aparece ao vivo.
- [x] O APK não traz mais pacote de demonstração: o app começa sem espécies e o usuário cria ou importa um FaunaPack.
- [x] A criação de FaunaPack roda em serviço em primeiro plano, com notificação de progresso; a tela pode ser fechada sem cancelar.
- [x] Packs guardam as opções de criação e podem ser atualizados (mais imagens por espécie, até 50) pelo gerenciador.
- [x] Fotos são abertas reduzidas e com a rotação EXIF corrigida (menos risco de falta de memória).
- [x] A galeria copia o arquivo original em vez de recomprimir em tamanho cheio.
- [x] O catálogo/pacote é carregado fora da thread da tela.
- [x] Resultado de baixa confiança vira "não reconheci" (limiar inicial 0.60, a calibrar).
- [x] Campos vazios da ficha deixam de aparecer.
- [x] Permissão POST_NOTIFICATIONS; `allowBackup` e tráfego sem TLS desativados.
- [x] Atualizações do download limitadas a uma por segundo.
- [x] Servidor não cai sem catálogo; docs apontam para `pampa_species.json`.
- [ ] Compilar e testar em aparelho físico.
- [ ] Calibrar o limiar de confiança com fotos reais.
- [ ] Comparar o embedding do Android com o do Python (paridade do pré-processamento).
