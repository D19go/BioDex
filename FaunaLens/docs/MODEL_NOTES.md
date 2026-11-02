# Modelos do Fauna Lens

## Arquitetura

- **BioCLIP original (ViT-B/16):** modelo local/offline usado no Android.
- **BioCLIP 2 (ViT-L/14):** modelo online usado pelo servidor para uma segunda opinião.

O repositório oficial do BioCLIP original fornece os pesos OpenCLIP/PyTorch. O Android usa ONNX Runtime, então o aplicativo instala um **export do encoder visual para ONNX** derivado do BioCLIP original.

## Download no aplicativo

O botão **Baixar BioCLIP offline** não abre mais o gerenciador de arquivos. Ele baixa diretamente o encoder ONNX, grava primeiro em um arquivo temporário, valida o modelo com ONNX Runtime e só então substitui o modelo instalado.

A fonte configurada atualmente é:

`m3r1al/naturalist-bioclip-v1/image_encoder.onnx`

Esse artefato declara no próprio Model Card que é um encoder de imagem BioCLIP v1, OpenCLIP ViT-B/16, derivado de `imageomics/bioclip`, com entrada `[1,3,224,224]` e saída de embedding de 512 dimensões.

## Importação manual

O botão **Importar BioCLIP ONNX** continua disponível para testes ou para trocar o arquivo por outro export compatível.

## Gerar seu próprio ONNX

Se quiser usar um export produzido pelo projeto a partir dos pesos oficiais, execute os scripts em `tools/` e coloque o resultado em:

`Models/exported/bioclip_image.onnx`

O arquivo usado pelo aplicativo em tempo de execução fica no armazenamento privado:

`files/models/bioclip_image.onnx`
