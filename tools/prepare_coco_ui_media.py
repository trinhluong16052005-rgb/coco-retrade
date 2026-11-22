"""Prepare square editorial crops without changing supplied originals."""
from pathlib import Path
from PIL import Image, ImageOps
import hashlib
import json
import shutil
import sys

sys.stdout.reconfigure(encoding="utf-8")
ROOT = Path(__file__).resolve().parents[1]
WEB = ROOT / "src/CoCoReTrade.Web"
ART = ROOT / ".artifacts/coco-ui-balance-20261118"
OUT = WEB / "wwwroot/images/coco"
SOURCE = Path(r"D:\DỰ ÁN\ẢNH COCO")

for filename in ("Views/Home/Index.cshtml", "Views/Home/Clearout.cshtml", "wwwroot/css/home.css", "wwwroot/css/clearout.css", "coco-content-media.json", "coco-content-usage.json"):
    path = WEB / filename
    backup = ART / "before" / filename
    if not backup.exists():
        backup.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(path, backup)

specs = [
    ("coco-category-bags-display", Path(r"D:\AI-Cache\temp\codex-clipboard-71ad2df5-a9c3-4ed9-b514-f0ec4bfd23dc.png"), "Túi xách với nhiều kiểu dáng được bày cùng giày và phụ kiện", None, 1440),
    ("coco-category-all-outfits", Path(r"D:\AI-Cache\temp\codex-clipboard-75365035-3efe-42a7-a7dc-a8986e831f8f.png"), "Áo thun và áo sơ mi với nhiều kiểu dáng, màu sắc", None, 1080),
    ("coco-consign-step-photos", SOURCE / "ALL SẢN PHẨM.jpg", "Áo được trải phẳng để nhìn rõ kiểu dáng và tem nhãn", None, 800),
    ("coco-consign-step-check", SOURCE / "555058192_1203903841757235_5988157220821645583_n.jpg", "Lựa chọn trang phục bên dãy áo trắng tại CoCo", None, 800),
    ("coco-consign-step-sale", SOURCE / "ẢNH KHÁCH MUA HÀNG (4).jpg", "Khách lựa chọn phụ kiện tại cửa hàng CoCo", None, 800),
]

manifest_path = WEB / "coco-content-media.json"
manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
old = {p["key"]: p for p in manifest["photos"]}
new_photos = []
for key, source, alt, crop, side in specs:
    original = ImageOps.exif_transpose(Image.open(source)).convert("RGB")
    size = min(original.size)
    # Square crop, centered, with enough headroom for the people in the photos.
    left, top = (original.width - size) // 2, (original.height - size) // 2
    crop = (left, top, left + size, top + size)
    image = original.crop(crop)
    side = min(side, size)
    image = image.resize((side, side), Image.Resampling.LANCZOS)
    dest = OUT / f"{key}.webp"
    image.save(dest, "WEBP", quality=83, method=6)
    record = {
        "key": key, "url": f"/images/coco/{dest.name}",
        "width": side, "height": side, "bytes": dest.stat().st_size,
        "sha256": hashlib.sha256(dest.read_bytes()).hexdigest(), "altVi": alt,
        "sourceType": "user-attachment" if "AI-Cache" in str(source) else "user-folder",
        "sourcePath": str(source), "sourceUrl": None, "sourceImageUrl": None,
        "originalSha256": hashlib.sha256(source.read_bytes()).hexdigest(),
        "cropBox": crop, "variants": [],
    }
    for width in (480, 800):
        if width < side:
            variant = image.resize((width, width), Image.Resampling.LANCZOS)
            target = OUT / f"{key}-{width}.webp"
            variant.save(target, "WEBP", quality=83, method=6)
            record["variants"].append({"url": f"/images/coco/{target.name}", "width": width, "height": width, "bytes": target.stat().st_size})
    if key == "coco-category-all-outfits":
        record["samePhotographAs"] = "coco-tops-flatlay"
        record["reuseReason"] = "User explicitly selected this attachment for the all-products category. Existing benefit image is preserved as requested."
    if key.startswith("coco-consign-step-"):
        record["derivedFrom"] = {"coco-consign-step-photos": "coco-tops-flatlay", "coco-consign-step-check": "coco-white-rail", "coco-consign-step-sale": "coco-shoppers-rail"}[key]
    new_photos.append(record)
    old[key] = record

manifest["photos"] = list(old.values())
manifest["allocationRule"] = "Preserve existing placement except explicit user changes. Category all-products intentionally uses the supplied flat-lay photograph, also shown in an existing benefit and on the separate consignment page. See coco-content-usage.json for actual source reuse."
manifest_path.write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
(ART / "prepared-media.json").write_text(json.dumps(new_photos, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
print(f"Prepared {len(new_photos)} square photo assets; originals unchanged. Consignment crops: 800 x 800 each.")
