#!/usr/bin/env python3
"""Mede a identificação do BioDex com fotos que NÃO estão no pack.

Conjunto de teste: uma pasta por espécie, com o nome científico (espaços podem virar "_"),
cheia de fotos. As fotos devem ser diferentes das referências do pack.

Para cada foto, calcula a similaridade com cada espécie pelos dois modos do app:
- centroide: cosseno com o embedding médio da espécie (modo padrão);
- vizinhos: média das 3 referências mais parecidas (UseNearestReferences).

Mostra top-1, top-3 e, para calibrar o limiar de confiança, quantas fotos são reconhecidas
em cada limiar e com que precisão.

Uso:
  python evaluate_identification.py --model bioclip_image.onnx --pack regiao.faunapack --test-dir teste/
"""

import argparse
import json
import struct
import sys
import zipfile
from collections import Counter
from pathlib import Path

import numpy as np
import onnxruntime as ort
from PIL import Image

IMAGE_SUFFIXES = {".jpg", ".jpeg", ".png", ".webp"}
SIZE = 224
NEAREST_K = 3  # igual a MatchingPolicy.NearestK no app

# Mesmos valores de normalização usados em BioClipOnnxEngine.Preprocess.
MEAN = np.array([0.48145466, 0.4578275, 0.40821073], dtype=np.float32)
STD = np.array([0.26862954, 0.26130258, 0.27577711], dtype=np.float32)


def normalize(vector):
    norm = np.linalg.norm(vector)
    return vector / norm if norm > 0 else vector


def normalize_name(name):
    return name.replace("_", " ").strip().lower()


def preprocess(path):
    """Redimensiona pelo menor lado, recorta o centro 224x224 e normaliza, como o app."""
    image = Image.open(path).convert("RGB")
    width, height = image.size
    scale = max(SIZE / width, SIZE / height)
    new_width = max(SIZE, round(width * scale))
    new_height = max(SIZE, round(height * scale))
    image = image.resize((new_width, new_height), Image.BILINEAR)
    left = max(0, (new_width - SIZE) // 2)
    top = max(0, (new_height - SIZE) // 2)
    image = image.crop((left, top, left + SIZE, top + SIZE))
    array = np.asarray(image, dtype=np.float32) / 255.0
    array = (array - MEAN) / STD
    return array.transpose(2, 0, 1)[None].astype(np.float32)


def read_pack(pack_path):
    """Lê o pack: nome de cada espécie, embedding médio e embeddings de cada referência."""
    with zipfile.ZipFile(pack_path) as archive:
        species_list = json.loads(archive.read("species.json"))
        blob = archive.read("embeddings.bin")

    if blob[:4] != b"FLPK":
        raise ValueError("embeddings.bin não é um FaunaPack válido.")
    _version, _count, _refs, dim = struct.unpack("<iiii", blob[4:20])
    floats = np.frombuffer(blob, dtype="<f4", offset=20)

    names, centroids, references = [], [], []
    position = 0
    for entry in species_list:
        count = len(entry["References"])
        vectors = floats[position:position + count * dim].reshape(count, dim)
        position += count * dim
        vectors = np.array([normalize(v) for v in vectors], dtype=np.float32)

        species = entry["Species"]
        names.append(species.get("ScientificName") or species.get("Id"))
        centroids.append(normalize(vectors.mean(axis=0)))
        references.append(vectors)

    return names, np.array(centroids, dtype=np.float32), references


def nearest_score(query, vectors):
    similarities = vectors @ query
    top = np.sort(similarities)[::-1][:NEAREST_K]
    return float(top.mean())


def embed(session, input_name, path):
    output = session.run(None, {input_name: preprocess(path)})[0][0]
    return normalize(output.astype(np.float32))


def collect(args, names, centroids, references, session, input_name):
    index = {normalize_name(name): i for i, name in enumerate(names)}
    samples = []  # (verdadeiro, scores_centroide, scores_vizinhos)
    for folder in sorted(Path(args.test_dir).iterdir()):
        if not folder.is_dir():
            continue
        key = normalize_name(folder.name)
        if key not in index:
            print(f"aviso: a pasta '{folder.name}' não está no pack; ignorada")
            continue
        truth = index[key]
        for photo in sorted(folder.iterdir()):
            if photo.suffix.lower() not in IMAGE_SUFFIXES:
                continue
            query = embed(session, input_name, photo)
            centroid_scores = centroids @ query
            nearest_scores = np.array([nearest_score(query, vectors) for vectors in references])
            samples.append((truth, centroid_scores, nearest_scores))
    return samples


def report(samples, names, label, score_index):
    truths = np.array([s[0] for s in samples])
    scores = np.array([s[score_index] for s in samples])
    order = np.argsort(-scores, axis=1)
    predicted = order[:, 0]
    top1 = predicted == truths
    top3 = np.any(order[:, :3] == truths[:, None], axis=1)
    best = scores[np.arange(len(samples)), predicted]

    print(f"\n== {label} ==")
    print(f"fotos: {len(samples)}  top-1: {top1.mean():.1%}  top-3: {top3.mean():.1%}")

    print("limiar  reconhecidas  precisão ao reconhecer")
    for threshold in np.quantile(best, np.linspace(0.1, 0.9, 9)):
        recognized = best >= threshold
        if recognized.sum() == 0:
            continue
        precision = top1[recognized].mean()
        print(f"{threshold:8.4f}  {recognized.mean():11.1%}  {precision:21.1%}")

    confusions = Counter(
        (names[t], names[p]) for t, p, ok in zip(truths, predicted, top1) if not ok
    )
    if confusions:
        print("confusões mais frequentes (verdadeira -> prevista):")
        for (truth, guess), count in confusions.most_common(5):
            print(f"  {count}x  {truth} -> {guess}")


def main():
    parser = argparse.ArgumentParser(description="Mede a identificação do BioDex.")
    parser.add_argument("--model", required=True, help="bioclip_image.onnx")
    parser.add_argument("--pack", required=True, help="pack .faunapack com as referências")
    parser.add_argument("--test-dir", required=True, help="pasta com uma subpasta por espécie")
    args = parser.parse_args()

    names, centroids, references = read_pack(args.pack)
    session = ort.InferenceSession(args.model, providers=["CPUExecutionProvider"])
    input_name = session.get_inputs()[0].name

    samples = collect(args, names, centroids, references, session, input_name)
    if not samples:
        print("nenhuma foto de teste encontrada para as espécies do pack", file=sys.stderr)
        return 1

    report(samples, names, "embedding médio (padrão do app)", 1)
    report(samples, names, f"vizinhos (média das {NEAREST_K} referências mais parecidas)", 2)
    return 0


if __name__ == "__main__":
    sys.exit(main())
