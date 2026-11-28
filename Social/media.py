"""Fotos enviadas para a rede social.

Toda foto é reescrita pelo servidor: a rotação EXIF é aplicada e os metadados são descartados.
Fotos da câmera trazem GPS no EXIF; sem essa etapa, quem vê o post descobre onde o animal foi visto.
"""

import io

from PIL import Image, ImageOps

MAX_IMAGE_BYTES = 5 * 1024 * 1024
MAX_IMAGES_PER_POST = 4
MAX_SIDE = 1600
JPEG_QUALITY = 85
ALLOWED_FORMATS = {"JPEG", "PNG"}


def sanitize_image(data: bytes) -> bytes:
    """Confere o formato real do arquivo (não a extensão), aplica a rotação e devolve um JPEG sem metadados."""
    if len(data) > MAX_IMAGE_BYTES:
        raise ValueError("A foto passa de 5 MB.")

    try:
        with Image.open(io.BytesIO(data)) as probe:
            if probe.format not in ALLOWED_FORMATS:
                raise ValueError("Formato não aceito: envie JPEG ou PNG.")
    except (OSError, Image.DecompressionBombError) as error:
        raise ValueError("Arquivo não é uma imagem válida.") from error

    with Image.open(io.BytesIO(data)) as source:
        image = ImageOps.exif_transpose(source).convert("RGB")
        image.thumbnail((MAX_SIDE, MAX_SIDE))

        output = io.BytesIO()
        # Sem exif=, o Pillow não grava metadados: GPS, modelo do celular e data original saem da foto.
        image.save(output, format="JPEG", quality=JPEG_QUALITY, optimize=True)
        return output.getvalue()
