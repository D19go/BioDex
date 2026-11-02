# Modelos locais

Esta pasta é gerada pelos scripts em `tools/` e não deve ser enviada com os checkpoints grandes para o Git.

Arquivos esperados:

```text
models/
├── bioclip/
├── bioclip2/
└── exported/
    ├── bioclip_image.onnx
    └── bioclip2_image.onnx
```

Para o Android, o arquivo importante é `exported/bioclip_image.onnx`.
