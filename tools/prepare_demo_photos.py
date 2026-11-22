"""Fetch public reference photos for the local, noncommercial coursework demo.

No login, scraping protection bypass, or credentials. Source URLs remain in JSON.
Downloaded files live in ignored PrivateUploads; production never serves them.
"""
import concurrent.futures
import io
import json
from pathlib import Path
import re
import subprocess
from PIL import Image, ImageOps

ROOT = Path(__file__).resolve().parents[1]
WEB = ROOT / 'src/CoCoReTrade.Web'
CACHE = ROOT / '.artifacts'
EDITORIAL = WEB / 'PrivateUploads/editorial/demo'
PRODUCTS = WEB / 'PrivateUploads/demo'

def fetch(url):
    result = subprocess.run(['curl.exe', '-L', '-sS', '--fail', '--max-time', '45', url], capture_output=True, check=True)
    return result.stdout

def photo(url, dest, width=1200, refresh=False):
    if dest.exists() and not refresh:
        with Image.open(dest) as check:
            check.verify()
        return
    data = fetch(url)
    with Image.open(io.BytesIO(data)) as source:
        image = ImageOps.exif_transpose(source).convert('RGBA')
        canvas = Image.new('RGBA', image.size, '#FFFFFF')
        canvas.alpha_composite(image)
        image = canvas.convert('RGB')
        image.thumbnail((width, width * 2), Image.Resampling.LANCZOS)
        image.save(dest, 'WEBP', quality=83, method=5)

def product(item):
    sources = item.get('imageSources', [])
    if len(sources) >= 3:
        # Checked-in original URLs make regeneration independent of an old
        # HTML snapshot or the current availability of the listing page.
        sources = sources[:3]
    else:
        cached = CACHE / (item['sku'].lower() + '.html')
        if not cached.exists():
            cached.write_bytes(fetch(item['sourceUrl']))
        html = cached.read_text(encoding='utf-8')
        match = re.search(r'<script id="__NEXT_DATA__"[^>]*>(.*?)</script>', html, re.S)
        if not match:
            raise ValueError('Public product JSON not present: ' + item['sku'])
        data = json.loads(match.group(1))['props']['pageProps']
        gallery = data.get('product', {}).get('images', [])
        if len(gallery) < 3:
            raise ValueError('Less than three original listing photos: ' + item['sku'])
        sources = [x['src'] for x in gallery[:3]]
    for index, url in enumerate(sources, 1):
        photo(url, PRODUCTS / f"{item['sku'].lower()}-{index}.webp", 1000)
    item['imageSources'] = sources
    item['imageCredit'] = 'Ảnh listing gốc Thrift+ · Chỉ dùng trong demo môn học trên máy.'
    item.pop('illustrationType', None)
    item.pop('illustrationColor', None)
    print('Photos ready:', item['sku'], flush=True)
    return item

def main():
    for folder in [EDITORIAL, PRODUCTS, CACHE]:
        folder.mkdir(parents=True, exist_ok=True)
    # Source manifest is checked in; ignored research snapshots are optional.
    manifest = json.loads((WEB / 'editorial-assets.json').read_text(encoding='utf-8-sig'))
    assets = manifest['assets']
    def editorial(x):
        photo(x['sourceUrl'], EDITORIAL / (x['name']+'.webp'),x['maxWidth'],refresh=True)
        print('Editorial ready:',x['name'],flush=True)
    with concurrent.futures.ThreadPoolExecutor(max_workers=5) as pool:
        list(pool.map(editorial,assets))
    import sys
    if '--editorial-only' in sys.argv:
        print('Prepared',len(assets),'editorial photos; product catalog left unchanged.')
        return
    catalog=json.loads((WEB/'demo-products.json').read_text(encoding='utf-8-sig'))
    with concurrent.futures.ThreadPoolExecutor(max_workers=5) as pool:
        completed=list(pool.map(product,catalog))
    (WEB/'demo-products.json').write_text(json.dumps(completed,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print('Prepared',len(completed),'original listing galleries and',len(assets),'editorial photos.')

if __name__=='__main__':
    main()
