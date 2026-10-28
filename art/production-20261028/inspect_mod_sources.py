"""Extract bounded original references and make source-inspection contacts."""
import hashlib
import json
import sys
from pathlib import Path

import UnityPy
from PIL import Image, ImageDraw

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
SOURCE_DIR = HERE / "sources"


def coordinates(name):
    if not name.lower().endswith(".png"):
        return None
    parts = Path(name).stem.split("_")
    if len(parts) != 2 or not parts[0].isdigit():
        return None
    tail = parts[1].split("-")
    if len(tail) != 2 or not all(part.isdigit() for part in tail):
        return None
    return int(parts[0]), int(tail[0]), int(tail[1])


def main():
    remaining = "--remaining" in sys.argv[1:]
    source_index = json.loads((HERE / "mod-container-index.json").read_text())
    enabled = {m["FileName"] for m in json.loads((ROOT / "Player/PortableAppdata/Mods/GameData/Mods.json").read_text()) if m["Enabled"]}
    rows = []
    for bundle in source_index["bundles"]:
        provider = Path(bundle["bundle"]).stem
        if provider not in enabled:
            continue
        selected = []
        for asset in bundle["assets"]:
            if asset["type"] != "Texture2D":
                continue
            coord = coordinates(Path(asset["path"]).name)
            if coord is None or coord[2] != 0:
                continue
            archive, record, frame = coord
            is_det = provider == "daggerfall expanded textures" and 10011 <= archive <= 10020
            is_rmb = provider == "rmb resource pack" and "/npcs/" in asset["path"]
            selected_scope = provider != "daggerfall expanded textures" and not is_rmb if remaining else is_det or is_rmb
            if selected_scope:
                selected.append((asset, coord))
        if not selected:
            continue
        env = UnityPy.load(bundle["bundle"])
        for asset, (archive, record, frame) in selected:
            existing = HERE / "idle-sleep/sources" / Path(asset["path"]).name
            if provider == "daggerfall expanded textures" and archive == 10015 and existing.exists():
                dest = existing
                image = Image.open(dest).convert("RGBA")
            else:
                dest = SOURCE_DIR / provider / f"{archive}_{record}-{frame}.png"
                dest.parent.mkdir(parents=True, exist_ok=True)
                image = env.container[asset["path"]].read().image.convert("RGBA")
                if not dest.exists():
                    image.save(dest)
                elif hashlib.sha256(Image.open(dest).convert("RGBA").tobytes()).digest() != hashlib.sha256(image.tobytes()).digest():
                    raise ValueError(f"Conflicting original source: {dest}")
            rows.append({"provider": provider, "archive": archive, "record": record,
                         "frame": frame, "container": asset["path"],
                         "bundleSha256": bundle["sha256"], "source": str(dest),
                         "dimensions": list(image.size),
                         "pixelSha256": hashlib.sha256(image.tobytes()).hexdigest(),
                         "applicability": "visual-classification-pending"})
    rows.sort(key=lambda r: (r["provider"], r["archive"], r["record"]))
    contacts = []
    for start in range(0, len(rows), 30):
        batch = rows[start:start+30]
        contact = Image.new("RGB", (1200, 1100), "#303238")
        draw = ImageDraw.Draw(contact)
        for i, row in enumerate(batch):
            x, y = (i % 6) * 200, (i // 6) * 220
            image = Image.open(row["source"]).convert("RGBA")
            scale = min(1.0, 188 / image.width, 186 / image.height)
            if scale < 1:
                image = image.resize((round(image.width*scale), round(image.height*scale)), Image.Resampling.NEAREST)
            contact.paste(image, (x + (200-image.width)//2, y+30), image)
            draw.text((x+5, y+4), f"{row['archive']}/{row['record']} {row['provider'][:3]}", fill="white")
            row["inspectionContact"] = f"{'remaining-' if remaining else ''}source-contact-{start//30:02d}.png"
        dest = HERE / f"{'remaining-' if remaining else ''}source-contact-{start//30:02d}.png"
        contact.save(dest)
        contacts.append(str(dest))
    output = "remaining-mod-source-scan.json" if remaining else "additional-mod-sources.json"
    (HERE / output).write_text(json.dumps({"status": "classification-pending", "sources": rows, "contacts": contacts}, indent=2)+"\n")
    print(json.dumps({"sources": len(rows), "contacts": len(contacts)}))


if __name__ == "__main__":
    main()
