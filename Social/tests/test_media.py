import io

import pytest
from PIL import Image

from media import sanitize_image


def jpeg_with_gps(size=(3000, 2000), orientation=6):
    image = Image.new("RGB", size, (10, 120, 40))
    exif = Image.Exif()
    exif[0x0112] = orientation
    gps = exif.get_ifd(0x8825)
    gps[1] = "S"
    gps[2] = (22.0, 0.0, 0.0)
    gps[3] = "W"
    gps[4] = (47.0, 0.0, 0.0)
    buffer = io.BytesIO()
    image.save(buffer, format="JPEG", exif=exif)
    return buffer.getvalue()


def test_gps_and_camera_metadata_are_removed():
    source = jpeg_with_gps()
    assert Image.open(io.BytesIO(source)).getexif().get_ifd(0x8825)  # garante que o teste tem GPS

    clean = Image.open(io.BytesIO(sanitize_image(source)))
    assert clean.format == "JPEG"
    assert len(clean.getexif()) == 0


def test_orientation_is_applied_and_size_is_capped():
    clean = Image.open(io.BytesIO(sanitize_image(jpeg_with_gps(size=(3000, 2000), orientation=6))))
    assert max(clean.size) <= 1600
    assert clean.size[1] > clean.size[0]  # girada: era paisagem, virou retrato


def test_png_is_accepted():
    buffer = io.BytesIO()
    Image.new("RGBA", (50, 40), (1, 2, 3, 255)).save(buffer, format="PNG")
    assert Image.open(io.BytesIO(sanitize_image(buffer.getvalue()))).format == "JPEG"


def test_non_image_is_rejected():
    with pytest.raises(ValueError):
        sanitize_image(b"isto nao e uma imagem")


def test_gif_is_rejected_even_when_valid():
    buffer = io.BytesIO()
    Image.new("RGB", (10, 10)).save(buffer, format="GIF")
    with pytest.raises(ValueError):
        sanitize_image(buffer.getvalue())


def test_file_over_limit_is_rejected():
    with pytest.raises(ValueError):
        sanitize_image(b"0" * (5 * 1024 * 1024 + 1))
