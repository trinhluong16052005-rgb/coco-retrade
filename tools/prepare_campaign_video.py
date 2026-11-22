"""Recreate the licensed, real storefront video from its recorded public source.

Requires Python, Pillow, curl and FFmpeg on PATH. Does not generate video or use AI.
"""
import json
from pathlib import Path
import shutil
import subprocess
import tempfile

from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
WEB = ROOT / "src" / "CoCoReTrade.Web"
MANIFEST = WEB / "campaign-media.json"


def run(*args):
    subprocess.run([str(arg) for arg in args], check=True)


def main():
    manifest = json.loads(MANIFEST.read_text(encoding="utf-8"))
    item = manifest["video"]
    if item["providerMetadata"]["isAiGenerated"] or item["providerMetadata"]["isPremium"]:
        raise RuntimeError("Only the recorded non-AI, nonpremium source is authorized.")
    for executable in ("curl", "ffmpeg", "ffprobe"):
        if not shutil.which(executable):
            raise RuntimeError(f"Install {executable} or add it to PATH.")
    output = WEB / "wwwroot" / "media" / "editorial"
    output.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix="coco-campaign-") as temp:
        original = Path(temp) / "original.mp4"
        poster = Path(temp) / "poster.png"
        run("curl", "--fail", "--location", "--max-time", "90", "--silent", "--show-error",
            item["sourceDownload"], "--output", original)
        for width, crf, filename in ((1600, 25, "fashion-market.mp4"),
                                     (960, 26, "fashion-market-mobile.mp4")):
            target = output / filename
            run("ffmpeg", "-hide_banner", "-loglevel", "error", "-y", "-i", original,
                "-an", "-vf", f"scale={width}:-2", "-c:v", "libx264", "-crf", str(crf),
                "-preset", "medium", "-movflags", "+faststart", "-pix_fmt", "yuv420p", target)
            run("ffmpeg", "-hide_banner", "-loglevel", "error", "-i", target, "-f", "null", "-")
        run("ffmpeg", "-hide_banner", "-loglevel", "error", "-y", "-ss", "2", "-i",
            output / "fashion-market.mp4", "-frames:v", "1", poster)
        with Image.open(poster) as image:
            image.save(output / "fashion-market-poster.webp", "WEBP", quality=85)
    for file in item["files"]:
        path = WEB / file["path"]
        file["bytes"] = path.stat().st_size
    MANIFEST.write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print("Licensed real video and poster recreated; metadata and sizes updated.")


if __name__ == "__main__":
    main()
