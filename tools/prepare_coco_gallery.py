"""Prepare user-supplied CoCo photographs. Originals and existing assets stay intact."""
from pathlib import Path
from PIL import Image, ImageOps
import hashlib
import json
import shutil
import sys

sys.stdout.reconfigure(encoding="utf-8")
ROOT = Path(__file__).resolve().parents[1]
SOURCE = Path(r"D:\DỰ ÁN\ẢNH COCO")
WEB = ROOT / "src/CoCoReTrade.Web"
ART = ROOT / ".artifacts/coco-content-20261118"
OUT = WEB / "wwwroot/images/coco"

# Back up the starting state, including earlier uncommitted work, only once.
for directory in (WEB / "Views", WEB / "Areas/Admin/Views", WEB / "wwwroot/css"):
    for path in directory.rglob("*"):
        if path.is_file():
            backup = ART / "before" / path.relative_to(ROOT)
            if not backup.exists():
                backup.parent.mkdir(parents=True, exist_ok=True)
                shutil.copy2(path, backup)
for name in ("coco-store-media.json", "homepage-media-selection.json", "editorial-public-sources.json"):
    backup = ART / "before" / name
    if not backup.exists():
        shutil.copy2(WEB / name, backup)

specs = [
    ("coco-white-rail", "555058192_1203903841757235_5988157220821645583_n.jpg", "Lựa chọn trang phục bên dãy áo trắng tại CoCo", None),
    ("coco-tops-flatlay", "72a51a9ca67626287f672.jpg", "Áo được trải phẳng để nhìn rõ kiểu dáng và tem nhãn", None),
    ("coco-shop-exterior", "ANH COCO  Ở NGOÀI.jpg", "Mặt tiền CoCo Kho Ký Gửi trên đường Núi Thành", None),
    ("coco-shoes-selection", "GIẦY.jpg", "Các đôi giày với nhiều kiểu dáng trong bộ ảnh CoCo", None),
    ("coco-visitors-together", "KHÁCH 1.jpg", "Hai khách ghé CoCo với túi mua hàng của cửa hàng", None),
    ("coco-visitor-bag", "KHÁCH 2.jpg", "Khách cầm túi mua hàng CoCo tại khu phụ kiện", None),
    ("coco-bag-shelves", "PHỤ KIỆN 2.jpg", "Túi xách được bày trên kệ tại CoCo", (0, 390, 520, 910)),
    ("coco-accessory-room", "PHỤ KIỆN 2.jpg", "Kệ túi xách và bàn phụ kiện trong cửa hàng CoCo", (0, 280, 1080, 1090)),
    ("coco-accessories-selection", "PHỤ KIỆN.jpg", "Góc trưng bày nhiều loại phụ kiện tại CoCo", None),
    ("coco-folded-trousers", "QUẦN 2.jpg", "Quần jeans và quần vải được gấp gọn theo từng chồng", None),
    ("coco-trouser-rail", "QUẦN.jpg", "Quần với nhiều màu sắc được treo trên giá tại CoCo", None),
    ("coco-dress-rail", "VẤY.jpg", "Váy và đầm sáng màu trên giá treo tại CoCo", None),
    ("coco-top-rail", "ÁO.jpg", "Áo khoác và áo kiểu được sắp xếp trên giá tại CoCo", None),
    ("coco-fitting-area", "ẢNH BÊN TRONG COCO.jpg", "Khu thử đồ và ghế ngồi trong không gian CoCo", None),
    ("coco-busy-clothing", "ẢNH KHÁCH MUA HÀNG (2).jpg", "Khách xem trang phục trong không gian mua sắm CoCo", None),
    ("coco-shoppers-rail", "ẢNH KHÁCH MUA HÀNG (4).jpg", "Khách lựa chọn quần áo giữa các dãy trưng bày", None),
    ("coco-shoppers-accessories", "ẢNH KHÁCH MUA HÀNG (5).jpg", "Khách lựa chọn tại khu phụ kiện của CoCo", None),
    ("coco-clothing-selection", "ẢNH KHÁCH MUA HÀNG (6).jpg", "Một góc khách xem đồ giữa các dãy trang phục CoCo", None),
    ("coco-shop-browsing", "ẢNH KHÁCH MUA HÀNG.jpg", "Không gian mua sắm và những dãy trang phục tại CoCo", None),
    ("coco-shop-interior", "ẢNH SHOP.jpg", "Không gian cửa hàng CoCo với những dãy quần áo sáng thoáng", None),
]
activities = json.loads((ART / "activity-photo-results.json").read_text(encoding="utf-8"))
activity_alts = [
    "Tiết mục của các em nhỏ trong chương trình Quốc tế Thiếu nhi được CoCo chia sẻ",
    "Quà Trung thu và đồ chơi trong chương trình Trung thu cho em 2026",
    "Ảnh tập thể trong chương trình Đông ấm áp 2025 tại Bình Minh, Quảng Ngãi",
    "Buổi lễ ra mắt Ban Điều hành và sinh nhật bốn tuổi của Đội tình nguyện DAU",
]
activity_keys = ["coco-childrens-day", "coco-mid-autumn", "coco-warm-winter", "coco-dau-gathering"]

manifest = {
    "schemaVersion": 2,
    "updatedOn": "2026-11-18",
    "purpose": "Actual shop and activity illustrations; not product inventory or customer endorsements.",
    "authorization": "User supplied the local folder and explicitly requested these four public Facebook posts for CoCo's website.",
    "transformation": "EXIF orientation corrected, optional crop recorded, WebP quality 82 with responsive variants. No synthetic images, upscaling or color replacement.",
    "researchLimit": "Facebook public Open Graph descriptions are truncated. Only visible facts are used; source links accompany all four activities.",
    "allocationRule": "No original photo repeated within a page; at most two placements across the seven editorial pages. Video poster and its fallback count as one placement.",
    "excluded": [
        {"source": str(SOURCE / "ẢNH KHÁCH MUA HÀNG (3).jpg"), "reason": "Visually duplicates ẢNH KHÁCH MUA HÀNG (2).jpg."},
        {"source": str(SOURCE / "ẢNH NÀY ĐỂ ĐẦU TRANG KÝ GỬI GIÚP TÔI.jpg"), "reason": "Text-heavy old promotion with extra contact numbers; use actual shop photography and current verified contacts."},
    ],
    "photos": [],
}

def encode(key, path, alt, crop=None, source_url=None, image_url=None):
    original = ImageOps.exif_transpose(Image.open(path)).convert("RGB")
    photo = original.crop(crop) if crop else original
    photo.thumbnail((1440, 1440), Image.Resampling.LANCZOS)
    target = OUT / f"{key}.webp"
    photo.save(target, "WEBP", quality=82, method=6)
    entry = {
        "key": key, "url": f"/images/coco/{target.name}",
        "width": photo.width, "height": photo.height, "bytes": target.stat().st_size,
        "sha256": hashlib.sha256(target.read_bytes()).hexdigest(), "altVi": alt,
        "sourceType": "public-facebook-post" if source_url else "user-folder",
        "sourcePath": str(path), "sourceUrl": source_url, "sourceImageUrl": image_url,
        "originalSha256": hashlib.sha256(path.read_bytes()).hexdigest(),
        "cropBox": crop, "variants": [],
    }
    for width in (480, 800):
        if width < photo.width:
            variant = photo.resize((width, round(photo.height * width / photo.width)), Image.Resampling.LANCZOS)
            dest = OUT / f"{key}-{width}.webp"
            variant.save(dest, "WEBP", quality=82, method=6)
            entry["variants"].append({"url": f"/images/coco/{dest.name}", "width": variant.width, "height": variant.height, "bytes": dest.stat().st_size})
    manifest["photos"].append(entry)

OUT.mkdir(parents=True, exist_ok=True)
for key, filename, alt, crop in specs:
    encode(key, SOURCE / filename, alt, crop)
for n, (key, alt, activity) in enumerate(zip(activity_keys, activity_alts, activities), 1):
    encode(key, ART / f"activity-{n}.jpg", alt, source_url=activity["sourceUrl"], image_url=activity["imageUrl"])

(WEB / "coco-content-media.json").write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
print(f"Prepared {len(manifest['photos'])} photo assets from 19 unique local photos and 4 public activity photos.")
print(f"WebP total, including responsive sizes: {sum(p['bytes'] + sum(v['bytes'] for v in p['variants']) for p in manifest['photos']):,} bytes.")
