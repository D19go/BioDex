r"""Exporta o encoder visual do BioCLIP ou BioCLIP 2 para ONNX.

Exemplos:
  python export_model.py --model bioclip --output ..\Models\exported\bioclip_image.onnx
  python export_model.py --model bioclip2 --output ..\Models\exported\bioclip2_image.onnx

O modelo original continua em formato OpenCLIP/PyTorch. O ONNX é uma cópia
somente do encoder visual usada por inferência.
"""
import argparse
from pathlib import Path

import numpy as np
import onnxruntime as ort
import open_clip
import torch

MODEL_IDS = {
    "bioclip": "hf-hub:imageomics/bioclip",
    "bioclip2": "hf-hub:imageomics/bioclip-2",
}


class ImageEncoder(torch.nn.Module):
    def __init__(self, model):
        super().__init__()
        self.model = model

    def forward(self, image):
        return self.model.encode_image(image, normalize=False)


def export(model_name: str, output: Path):
    output.parent.mkdir(parents=True, exist_ok=True)
    model, _, _ = open_clip.create_model_and_transforms(MODEL_IDS[model_name])
    model.eval()

    encoder = ImageEncoder(model).eval()
    dummy = torch.randn(1, 3, 224, 224, dtype=torch.float32)

    print(f"Exportando {model_name}...")
    with torch.no_grad():
        torch.onnx.export(
            encoder,
            dummy,
            str(output),
            input_names=["image"],
            output_names=["embedding"],
            opset_version=18,
            do_constant_folding=True,
            dynamo=False,
        )

    print(f"ONNX criado: {output}")
    session = ort.InferenceSession(str(output), providers=["CPUExecutionProvider"])
    input_name = session.get_inputs()[0].name
    test_input = np.random.randn(1, 3, 224, 224).astype(np.float32)
    result = session.run(None, {input_name: test_input})[0]
    print(f"Validação ONNX: saída={result.shape}, dtype={result.dtype}")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--model", choices=MODEL_IDS.keys(), required=True)
    parser.add_argument("--output", required=True)
    args = parser.parse_args()
    export(args.model, Path(args.output).resolve())


if __name__ == "__main__":
    main()
