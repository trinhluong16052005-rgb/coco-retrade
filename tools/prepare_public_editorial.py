"""Rebuild licensed editorial and category WebP assets from source URLs.

Requires Python 3 and Pillow. Keeps each photo's provenance in
src/CoCoReTrade.Web/editorial-public-sources.json.
"""

from concurrent.futures import ThreadPoolExecutor
from io import BytesIO
import json
from pathlib import Path
from urllib.request import Request, urlopen

from PIL import Image, ImageOps


ROOT = Path(__file__).resolve().parents[1]
WEB = ROOT / "src" / "CoCoReTrade.Web"
MANIFEST = WEB / "editorial-public-sources.json"


def prepare(photo):
    request = Request(
        photo["downloadUrl"],
        headers={"User-Agent": "CoCoReTrade-EditorialAssets/1.0"},
    )
    with urlopen(request, timeout=60) as response:
        original = response.read()
    with Image.open(BytesIO(original)) as source:
        image = ImageOps.exif_transpose(source).convert("RGB")
        image.thumbnail((1400, 1400), Image.Resampling.LANCZOS)
        destination = WEB / photo["path"]
        destination.parent.mkdir(parents=True, exist_ok=True)
        image.save(destination, "WEBP", quality=85, method=6)
        return f"{destination.name}: {image.width}x{image.height}, {destination.stat().st_size:,} bytes"


def main():
    manifest = json.loads(MANIFEST.read_text(encoding="utf-8"))
    with ThreadPoolExecutor(max_workers=3) as executor:
        for result in executor.map(prepare, manifest["photos"]):
            print(result)


if __name__ == "__main__":
    main()
