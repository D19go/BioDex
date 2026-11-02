r"""Exporta o encoder visual oficial do BioCLIP 2 para ONNX.

Uso:
  python export_bioclip2.py --output ..\Models\exported\bioclip2_image.onnx

O servidor normal não precisa deste ONNX; ele usa OpenCLIP diretamente.
Este arquivo existe para testes, benchmark e futuras integrações.
"""
from pathlib import Path
import argparse

from export_model import export


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--output", default="../Models/exported/bioclip2_image.onnx")
    args = parser.parse_args()
    export("bioclip2", Path(args.output).resolve())


if __name__ == "__main__":
    main()
