"""Review-only material-color repair of generated standing outlines.

Never removes pixels or changes alpha. Original native fronts are copied exactly.
Color edits are limited to dark pixels in the outer two-pixel band, with colors
sampled from the nearest original interior. This is not anatomy recognition:
every output still requires enlarged visual review before promotion.
"""
from pathlib import Path
import argparse
import hashlib
import json
import shutil
import numpy as np
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[1]
ART = ROOT / "art/standing/mobile"


def erode(mask):
    padded = np.pad(mask, 1)
    result = mask.copy()
    for y in range(3):
        for x in range(3):
            result &= padded[y:y + mask.shape[0], x:x + mask.shape[1]]
    return result


def soften(image):
    source = np.array(image.convert("RGBA"))
    result = source.copy()
    mask = source[:, :, 3] >= 128
    interior = erode(erode(mask))
    brightness = source[:, :, :3].max(axis=2)
    band = mask & ~interior
    for y, x in zip(*np.nonzero(band & (brightness < 70))):
        y0, y1 = max(0, y - 4), min(mask.shape[0], y + 5)
        x0, x1 = max(0, x - 4), min(mask.shape[1], x + 5)
        dy, dx = np.nonzero(interior[y0:y1, x0:x1])
        if not len(dy):
            continue
        dy, dx = dy + y0, dx + x0
        distance = (dy - y) ** 2 + (dx - x) ** 2
        nearest = distance.min()
        if nearest > 16:
            continue
        # Sample original colors only. Never propagate edited pixels or reach
        # across a transparent gap into a neighboring arm or garment.
        donors = []
        for yy, xx in zip(dy[distance <= nearest + 1], dx[distance <= nearest + 1]):
            steps = max(abs(int(yy) - y), abs(int(xx) - x))
            if all(mask[round(y + (yy-y)*t/steps), round(x + (xx-x)*t/steps)] for t in range(1, steps+1)):
                donors.append(source[yy, xx, :3])
        if not donors:
            continue
        color = np.median(donors, axis=0)
        # Leave already-material-colored shadows and uniformly dark details.
        if color.max() < max(48, int(brightness[y, x]) + 28):
            continue
        result[y, x, :3] = np.rint(color * 0.9).astype(np.uint8)
    changed = np.any(result[:, :, :3] != source[:, :, :3], axis=2)
    assert np.array_equal(result[:, :, 3], source[:, :, 3])
    assert not np.any(changed & ~band)
    assert np.array_equal(source[interior], result[interior])
    return Image.fromarray(result), {"alpha_byte_identical": True,
        "silhouette_byte_identical": True, "protected_interior_unchanged": True,
        "recolored_rim_pixels": int(changed.sum())}


def prepare(archive, output):
    folder = ART / ("idle-pilot-391" if archive == 391 else f"idle-{archive}")
    meta = json.loads((folder / "manifest-v1.json").read_text())
    if "edge_cleanup" in meta or "outline_color_repair" in meta:
        raise ValueError("Use original artwork, not a previously processed result")
    dest = output / str(archive)
    dest.mkdir(parents=True, exist_ok=True)
    cw, ch = meta["atlas_cell_size"]
    atlas = Image.new("RGBA", (cw*4, ch*2))
    checks = []
    for frame in meta["frames"]:
        v = frame["view"]
        name = frame.get("file", f"{archive}-idle-view-{v}-{frame['bearing']}.png")
        frame["file"] = name
        path = folder / name
        original = Image.open(path).convert("RGBA")
        if v == 0:
            candidate = original.copy()
            verification = {"native_front_byte_identical": True}
            shutil.copyfile(path, dest / name)
        else:
            candidate, verification = soften(original)
            candidate.save(dest / name)
        atlas.paste(candidate, (v%4*cw+(cw-candidate.width)//2,v//4*ch))
        checks.append({"view":v,"source_sha256":hashlib.sha256(path.read_bytes()).hexdigest(),**verification})
        sheet = Image.new("RGB",(max(240,cw*8+32),ch*4+36),"#858779")
        draw = ImageDraw.Draw(sheet)
        draw.text((8,8),f"{archive}/{v} original | contour preserved",fill="black")
        for j,im in enumerate((original,candidate)):
            im = im.resize((im.width*4,im.height*4),Image.Resampling.NEAREST)
            sheet.paste(im,(j*sheet.width//2+(sheet.width//2-im.width)//2,30),im)
        sheet.save(dest/f"compare-{v}.png")
    atlas.save(dest/"atlas.png")
    meta["outline_color_repair"]={"status":"review candidate", "method":"nearest interior material color; alpha and silhouette unchanged", "frames":checks}
    (dest/"manifest.json").write_text(json.dumps(meta,indent=2))
    print(json.dumps({"archive":archive,"recolored":sum(c.get('recolored_rim_pixels',0) for c in checks)}))


if __name__ == "__main__":
    parser=argparse.ArgumentParser()
    parser.add_argument("--archive",type=int,required=True)
    parser.add_argument("--output",type=Path,default=ROOT/"work/standing-outline-color-review")
    args=parser.parse_args()
    prepare(args.archive,args.output)
