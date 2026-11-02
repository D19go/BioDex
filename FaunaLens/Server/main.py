import io
import json
import os
from pathlib import Path

import numpy as np
import open_clip
import torch
from fastapi import FastAPI, File, UploadFile
from PIL import Image

MODEL_ID = os.environ.get("FAUNA_MODEL", "hf-hub:imageomics/bioclip-2")
CATALOG = Path(os.environ.get("FAUNA_CATALOG", "data/pampa_species_online.json"))

app = FastAPI(title="Fauna Lens API", version="0.2.0")

model, _, preprocess = open_clip.create_model_and_transforms(MODEL_ID)
model.eval()

catalog = json.loads(CATALOG.read_text(encoding="utf-8"))


def normalize(vector):
    norm = np.linalg.norm(vector)
    return vector if norm == 0 else vector / norm


def image_embedding(data: bytes):
    image = Image.open(io.BytesIO(data)).convert("RGB")
    tensor = preprocess(image).unsqueeze(0)
    with torch.no_grad():
        vector = model.encode_image(tensor, normalize=False).cpu().numpy()[0]
    return normalize(vector)


@app.get("/health")
def health():
    return {
        "ok": True,
        "model": MODEL_ID,
        "catalog": str(CATALOG),
        "species": len(catalog),
    }


@app.post("/identify")
async def identify(image: UploadFile = File(...)):
    query = image_embedding(await image.read())
    ranked = []

    for item in catalog:
        embedding = np.asarray(item.get("embedding", []), dtype=np.float32)
        if embedding.size != query.size:
            continue
        ranked.append((float(np.dot(query, embedding)), item))

    ranked.sort(key=lambda value: value[0], reverse=True)

    return [
        {
            "commonName": item["commonName"],
            "scientificName": item["scientificName"],
            "score": score,
        }
        for score, item in ranked[:5]
    ]
