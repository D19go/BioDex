r"""Gera embeddings de referência para um pack de espécies.

O mesmo script pode preparar:
- pack offline: BioCLIP original (recomendado para o Android);
- pack online: BioCLIP 2 (recomendado para o servidor).

Estrutura:
  references/pampa/capybara/*.jpg
  references/pampa/maned_wolf/*.jpg
  ...

Uso:
  python create_pack.py --model bioclip --references ..\references\pampa \
      --catalog ..\Data\pampa_species.json \
      --output ..\Data\pampa_species_offline.json

  python create_pack.py --model bioclip2 --references ..\references\pampa \
      --catalog ..\Data\pampa_species.json \
      --output ..\Server\data\pampa_species_online.json
"""
import argparse
import json
from pathlib import Path

import numpy as np
import open_clip
import torch
from PIL import Image

MODEL_IDS = {
    "bioclip": "hf-hub:imageomics/bioclip",
    "bioclip2": "hf-hub:imageomics/bioclip-2",
}


def normalize(vector):
    norm = np.linalg.norm(vector)
    return vector if norm == 0 else vector / norm


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--model", choices=MODEL_IDS.keys(), required=True)
    parser.add_argument("--references", required=True)
    parser.add_argument("--catalog", required=True)
    parser.add_argument("--output", required=True)
    args = parser.parse_args()

    model, _, preprocess = open_clip.create_model_and_transforms(MODEL_IDS[args.model])
    model.eval()

    species = json.loads(Path(args.catalog).read_text(encoding="utf-8"))
    root = Path(args.references)

    for item in species:
        folder = root / item["id"]
        images = [
            path for path in folder.rglob("*")
            if path.suffix.lower() in {".jpg", ".jpeg", ".png", ".webp"}
        ]
        if not images:
            raise SystemExit(f"Sem imagens para {item['id']}: {folder}")

        vectors = []
        for path in images:
            with Image.open(path).convert("RGB") as image:
                tensor = preprocess(image).unsqueeze(0)
            with torch.no_grad():
                vector = model.encode_image(tensor, normalize=False).cpu().numpy()[0]
            vectors.append(normalize(vector))

        mean = normalize(np.mean(np.stack(vectors), axis=0))
        item["embedding"] = mean.astype(np.float32).tolist()
        item["referenceCount"] = len(images)
        item["embeddingModel"] = args.model

    output = Path(args.output)
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(
        json.dumps(species, ensure_ascii=False, separators=(",", ":")),
        encoding="utf-8",
    )
    print(f"Pack criado: {output}")
    print(f"Modelo: {args.model}")


if __name__ == "__main__":
    main()
