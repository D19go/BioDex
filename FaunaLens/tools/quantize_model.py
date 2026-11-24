#!/usr/bin/env python3
"""Gera uma versão quantizada (int8) do encoder de imagem do BioCLIP, menor e mais rápida no celular.

A quantização reduz o tamanho do arquivo e costuma acelerar a inferência em CPU, mas pode piorar
a precisão. Só troque o modelo do app depois de medir com evaluate_identification.py.

Uso:
  python quantize_model.py --input bioclip_image.onnx --output bioclip_image_int8.onnx
"""

import argparse
import sys

from onnxruntime.quantization import QuantType, quantize_dynamic


def main():
    parser = argparse.ArgumentParser(description="Quantiza o modelo BioCLIP para int8.")
    parser.add_argument("--input", required=True, help="modelo ONNX original (float32)")
    parser.add_argument("--output", required=True, help="caminho do modelo int8")
    args = parser.parse_args()

    quantize_dynamic(args.input, args.output, weight_type=QuantType.QInt8)
    print(f"modelo int8 gerado em {args.output}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
