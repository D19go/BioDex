"""Baixa os checkpoints oficiais do BioCLIP e do BioCLIP 2.

Uso:
  python download_models.py --model bioclip
  python download_models.py --model bioclip2
  python download_models.py --model all

Os pesos ficam fora do projeto Android e não são embutidos no APK.
"""
import argparse
from pathlib import Path

from huggingface_hub import snapshot_download

MODELS = {
    "bioclip": "imageomics/bioclip",
    "bioclip2": "imageomics/bioclip-2",
}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--model", choices=["bioclip", "bioclip2", "all"], default="all")
    parser.add_argument("--output", default="../models")
    args = parser.parse_args()

    root = Path(args.output).resolve()
    root.mkdir(parents=True, exist_ok=True)
    names = list(MODELS) if args.model == "all" else [args.model]

    for name in names:
        destination = root / name
        destination.mkdir(parents=True, exist_ok=True)
        print(f"Baixando {MODELS[name]} para {destination} ...")
        snapshot_download(
            repo_id=MODELS[name],
            local_dir=str(destination),
            local_dir_use_symlinks=False,
        )
        print(f"Concluído: {destination}")


if __name__ == "__main__":
    main()
