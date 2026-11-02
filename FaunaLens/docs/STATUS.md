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
