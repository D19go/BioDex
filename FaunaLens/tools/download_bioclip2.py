r"""Baixa o checkpoint oficial do BioCLIP 2.

Uso:
  python download_bioclip2.py --output ..\Models\bioclip2
"""
from pathlib import Path
import argparse
from huggingface_hub import snapshot_download


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--output", default="../Models/bioclip2")
    args = parser.parse_args()

    destination = Path(args.output).resolve()
    destination.mkdir(parents=True, exist_ok=True)

    snapshot_download(
        repo_id="imageomics/bioclip-2",
        local_dir=str(destination),
        local_dir_use_symlinks=False,
    )

    print(f"BioCLIP 2 baixado em: {destination}")


if __name__ == "__main__":
    main()
