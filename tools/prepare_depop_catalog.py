"""Prepare local, sourced product galleries for CatalogImport; never writes SQL.

Only downloads metadata.image URLs already captured from public listing pages.
Requires Python 3 + Pillow. Stops on access restrictions; no login or bypass.
Vietnamese product copy, size and owner are provided in an override JSON file.
"""
from __future__ import annotations

import argparse
from concurrent.futures import ThreadPoolExecutor
from decimal import Decimal, ROUND_HALF_UP
from hashlib import sha256
from html import unescape
from io import BytesIO
import json
from pathlib import Path
import re
import sys
from urllib.parse import urlparse
from urllib.request import Request, urlopen

from PIL import Image, ImageOps


ROOT = Path(__file__).resolve().parents[1]
CONDITIONS = {"Brand new": 99, "Like new": 99, "Excellent condition": 95,
              "Good condition": 90, "Fair condition": 80}


def source_url(record):
    return record.get("metadata", {}).get("offers", {}).get("url") or record["url"]


def record_id(url):
    slug = urlparse(url).path.rstrip("/").split("/")[-1]
    slug = re.sub(r"[^a-z0-9._-]", "-", slug.lower()).strip("-")
    if not slug:
        raise ValueError("Missing product slug in observed URL")
    value = "depop-" + slug
    return value if len(value) <= 100 else value[:86] + "-" + sha256(url.encode()).hexdigest()[:12]


def public_image(url):
    parsed = urlparse(url)
    if parsed.scheme != "https" or parsed.hostname != "media-photos.depop.com" or parsed.username or parsed.password:
        raise ValueError("Only observed public media-photos.depop.com HTTPS images are accepted")
    return url


def load_source(url, cache):
    cached = cache / (sha256(url.encode()).hexdigest() + ".bin")
    if cached.exists():
        return cached.read_bytes()
    request = Request(url, headers={"User-Agent": "CoCoReTrade-CatalogPreparation/1.0"})
    with urlopen(request, timeout=45) as response:
        data = response.read(25_000_001)
    if len(data) > 25_000_000:
        raise ValueError("Source image exceeds 25 MB")
    # Validate before saving any cache entry, so an HTML error is never reused as a photo.
    with Image.open(BytesIO(data)) as check:
        check.verify()
    cache.mkdir(parents=True, exist_ok=True)
    cached.write_bytes(data)
    return data


def photo(task):
    index, url, destination, cache, max_side, quality = task
    original = load_source(public_image(url), cache)
    with Image.open(BytesIO(original)) as source:
        if getattr(source, "n_frames", 1) != 1 or source.width * source.height > 40_000_000:
            raise ValueError("Animated or oversized source photo")
        source.load()
        rgba = ImageOps.exif_transpose(source).convert("RGBA")
        background = Image.new("RGBA", rgba.size, "#FFFFFF")
        background.alpha_composite(rgba)
        image = background.convert("RGB")
        original_pixel_hash = sha256(f"{image.width}x{image.height}:".encode() + image.tobytes()).hexdigest()
        image.thumbnail((max_side, max_side), Image.Resampling.LANCZOS)
        destination.parent.mkdir(parents=True, exist_ok=True)
        image.save(destination, "WEBP", quality=quality, method=6)
    with Image.open(destination) as decoded:
        pixels = decoded.convert("RGBA")
        pixel_hash = sha256(f"{pixels.width}x{pixels.height}:".encode() + pixels.tobytes()).hexdigest()
        width, height = pixels.size
    encoded = destination.read_bytes()
    if len(encoded) > 5_000_000:
        raise ValueError("Prepared WebP exceeds importer limit of 5 MB")
    return {"index": index, "path": destination, "sourceUrl": url, "width": width, "height": height,
            "bytes": len(encoded), "sha256": sha256(encoded).hexdigest(),
            "pixelSha256": pixel_hash, "originalPixelSha256": original_pixel_hash}


def main():
    # Windows terminals often default to cp1252; paths and product copy are Vietnamese.
    sys.stdout.reconfigure(encoding="utf-8")
    sys.stderr.reconfigure(encoding="utf-8")
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--raw", type=Path, default=ROOT / ".artifacts/depop-raw.json")
    parser.add_argument("--overrides", type=Path, required=True,
                        help="JSON keyed by observed source URL or record id: Vietnamese name/description/size/color/sellerEmail")
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--category", help="Only this CoCo category")
    parser.add_argument("--seller-email", help="Owner fallback; item override wins")
    parser.add_argument("--manifest-id", default="coco-depop-import")
    parser.add_argument("--usd-vnd", type=Decimal, default=Decimal("26000"), help="Fixed reference rate, never a live rate")
    parser.add_argument("--max-side", type=int, default=1400)
    parser.add_argument("--quality", type=int, default=85)
    args = parser.parse_args()
    if args.usd_vnd <= 0 or not 100 <= args.max_side <= 1400 or not 1 <= args.quality <= 100:
        raise ValueError("Invalid rate/image dimensions/quality")
    raw = json.loads(args.raw.read_text(encoding="utf-8-sig"))
    overrides = json.loads(args.overrides.read_text(encoding="utf-8-sig"))
    records = [row for row in raw if not args.category or row.get("category") == args.category]
    if not records:
        raise ValueError("No matching raw records")
    output = args.output.resolve()
    output.parent.mkdir(parents=True, exist_ok=True)
    cache = output.parent / ".source-cache"
    items, report, source_ids = [], [], set()
    for record in records:
        metadata = record["metadata"]
        url = source_url(record)
        identifier = record_id(url)
        if identifier in source_ids:
            raise ValueError("Duplicate listing in raw records: " + identifier)
        source_ids.add(identifier)
        override = overrides.get(url) or overrides.get(url.rstrip("/")) or overrides.get(identifier)
        if not override or any(not override.get(key) for key in ("name", "description", "size")):
            raise ValueError("Supply Vietnamese name/description and source-supported size for " + identifier)
        seller = override.get("sellerEmail") or args.seller_email
        if not seller:
            raise ValueError("Missing sellerEmail for " + identifier)
        urls = metadata.get("image")
        if not isinstance(urls, list) or len(urls) not in range(3, 11):
            raise ValueError("Observed metadata.image must contain 3–10 source images: " + identifier)
        if len(set(urls)) != len(urls):
            raise ValueError("metadata.image contains repeated URLs: " + identifier)
        offers = metadata["offers"]
        if offers.get("priceCurrency") != "USD":
            raise ValueError("Only USD has an explicit reference rate; do not guess other currencies")
        price = Decimal(str(offers["price"]))
        if price <= 0:
            raise ValueError("Source price must be positive")
        price_vnd = int((price * args.usd_vnd).quantize(Decimal("1"), rounding=ROUND_HALF_UP))
        attributes = record.get("attributes", "")
        condition_label = next((label for label in CONDITIONS if label in attributes), attributes.split("\n", 1)[0].strip())
        condition = override.get("conditionPercent", CONDITIONS.get(condition_label))
        if condition is None:
            raise ValueError("Need explicit source condition mapping for " + identifier)
        tasks = [(index, url, output.parent / "photos" / identifier / f"{index:02d}.webp",
                  cache, args.max_side, args.quality) for index, url in enumerate(urls, 1)]
        with ThreadPoolExecutor(max_workers=4) as pool:
            prepared = sorted(pool.map(photo, tasks), key=lambda item: item["index"])
        unique, duplicate_sources, file_hashes, pixel_hashes, original_hashes = [], [], set(), set(), set()
        for image in prepared:
            if image["sha256"] in file_hashes or image["pixelSha256"] in pixel_hashes or image["originalPixelSha256"] in original_hashes:
                duplicate_sources.append(image["sourceUrl"])
                continue
            file_hashes.add(image["sha256"])
            pixel_hashes.add(image["pixelSha256"])
            original_hashes.add(image["originalPixelSha256"])
            unique.append(image)
        if len(unique) < 3:
            raise ValueError("Fewer than 3 distinct actual photos after duplicate removal: " + identifier)
        condition_note = (f"Tình trạng nguồn: {condition_label}; quy đổi thành {condition}% để khai báo, "
                          "chưa được CoCo kiểm định hiện vật.")
        notes = (f"Giá listing {price:.2f} USD, quy đổi tham khảo cố định {args.usd_vnd:,.0f} VNĐ/USD; "
                 "không phải tỷ giá trực tiếp. Chưa biết giá bán lẻ gốc nên originalPriceVnd bằng askingPriceVnd. "
                 + condition_note + " " + override.get("notes", "")
                 + " Mô tả gốc: " + unescape(metadata.get("description", "")))
        items.append({"id": identifier, "sourceUrl": url, "sellerEmail": seller, "name": override["name"],
                      "brand": unescape(metadata.get("brand", {}).get("name", "")),
                      "category": record["category"], "size": override["size"],
                      "originalPriceVnd": price_vnd, "askingPriceVnd": price_vnd,
                      "conditionPercent": condition, "allowOffers": True,
                      "images": [{"file": image["path"].relative_to(output.parent).as_posix(), "sourceUrl": image["sourceUrl"]}
                                 for image in unique],
                      "description": override["description"], "color": override.get("color"), "notes": notes.strip()})
        report.append({"id": identifier, "sourceUrl": url, "sourceCondition": condition_label,
                       "sourcePrice": str(price), "sourceCurrency": "USD", "referenceUsdVnd": str(args.usd_vnd),
                       "duplicatesExcluded": duplicate_sources,
                       "images": [{key: str(value) if isinstance(value, Path) else value for key, value in image.items()}
                                  for image in unique]})
        print(f"Prepared {identifier}: {len(unique)} distinct photos, {price_vnd:,} VND", flush=True)
    output.write_text(json.dumps({"schemaVersion": 1, "id": args.manifest_id, "items": items},
                                 ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    report_path = output.with_suffix(".photos-report.json")
    report_path.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"Saved {len(items)} sourced records to {output}; SQL untouched", flush=True)


if __name__ == "__main__":
    main()
