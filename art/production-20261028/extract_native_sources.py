"""Recover original frame-zero references using DFU TextureFile semantics.

This is an archive decoder, not image synthesis. Reference implementation:
daggerfall-unity/Assets/Scripts/API/TextureFile.cs, ReadImage and ReadRle.
"""
import hashlib
import json
import struct
from pathlib import Path

from PIL import Image, ImageDraw

HERE = Path(__file__).resolve().parent
REPO = HERE.parents[1]
ARENA = REPO.parent / "GameFiles/arena2"


def decode(archive, record, frame=0):
    path = ARENA / f"TEXTURE.{archive}"
    data = path.read_bytes()
    count = struct.unpack_from("<h", data)[0]
    if not 0 <= record < count:
        raise ValueError(f"Invalid record {archive}/{record}, count={count}")
    base = struct.unpack_from("<i", data, 28 + record * 20)[0]
    width, height, compression = struct.unpack_from("<hhH", data, base + 4)
    offset = struct.unpack_from("<I", data, base + 14)[0]
    frames = struct.unpack_from("<H", data, base + 20)[0]
    scale_x, scale_y = struct.unpack_from("<hh", data, base + 24)
    if not 0 <= frame < frames or width <= 0 or height <= 0:
        raise ValueError("Invalid dimensions/frame")
    start = base + offset
    pixels = bytearray()
    if compression in (0x1108, 0x0108):
        for row in range(height):
            row_offset, encoding = struct.unpack_from("<hH", data, start + (height * frame + row) * 4)
            cursor = base + row_offset
            if encoding == 0x8000:
                row_width = struct.unpack_from("<H", data, cursor)[0]
                cursor += 2
                if row_width != width:
                    raise ValueError("Unexpected RLE row width")
                row_pixels = bytearray()
                while len(row_pixels) < row_width:
                    run = struct.unpack_from("<h", data, cursor)[0]
                    cursor += 2
                    if run < 0:
                        row_pixels.extend([data[cursor]] * -run)
                        cursor += 1
                    elif run > 0:
                        row_pixels.extend(data[cursor:cursor+run])
                        cursor += run
                    else:
                        raise ValueError("Zero-length RLE run")
                if len(row_pixels) != width:
                    raise ValueError("RLE row overrun")
                pixels.extend(row_pixels)
            else:
                pixels.extend(data[cursor:cursor+width])
    elif frames == 1:
        for row in range(height):
            pixels.extend(data[start + row*256:start + row*256 + width])
    else:
        cursor = start + struct.unpack_from("<i", data, start + frame*4)[0]
        frame_width, frame_height = struct.unpack_from("<hh", data, cursor)
        cursor += 4
        if (frame_width, frame_height) != (width, height):
            raise ValueError("Unexpected frame extent")
        for row in range(height):
            row_pixels = bytearray()
            while len(row_pixels) < width:
                transparent = data[cursor]
                cursor += 1
                row_pixels.extend([0] * transparent)
                opaque = data[cursor]
                cursor += 1
                row_pixels.extend(data[cursor:cursor+opaque])
                cursor += opaque
                if transparent == opaque == 0:
                    raise ValueError("Zero-length transparent/image run")
            if len(row_pixels) != width:
                raise ValueError("Image row overrun")
            pixels.extend(row_pixels)
    if len(pixels) != width * height:
        raise ValueError("Decoded size mismatch")
    palette = (ARENA / "ART_PAL.COL").read_bytes()[8:776]
    if len(palette) != 768:
        raise ValueError("Invalid native palette")
    rgba = bytearray()
    for value in pixels:
        rgba.extend(palette[value*3:value*3+3] if value else b"\0\0\0")
        rgba.append(255 if value else 0)
    return Image.frombytes("RGBA", (width, height), bytes(rgba)), {
        "archive": archive, "record": record, "frame": frame,
        "recordFrameCount": frames, "compression": compression,
        "sourceArchiveSha256": hashlib.sha256(data).hexdigest(),
        "sourcePaletteSha256": hashlib.sha256((ARENA / "ART_PAL.COL").read_bytes()).hexdigest(),
        "pixelSha256": hashlib.sha256(rgba).hexdigest(), "dimensions": [width, height],
        "nativeRecordScaleXY": [scale_x, scale_y],
        "scaledSizeBeforeGlobalScaleAndOverrides": [width + int(width*scale_x/256), height + int(height*scale_y/256)],
        "scaleFormulaReference": "MobilePersonBillboard.cs:321-346; BlocksFile.ScaleDivisor=256f",
    }


def compare_native_reference(image, reference):
    if image.size != reference.size:
        return False
    return all(a[3] == b[3] and (a[3] == 0 or a[:3] == b[:3])
               for a, b in zip(image.getdata(), reference.convert("RGBA").getdata()))


def main():
    keeper_ref = REPO / "art/standing/keepers/prototypes/shopkeeper-182-0-native-reference-nn8x.png"
    keeper = Image.open(keeper_ref).convert("RGBA")
    keeper = keeper.resize((keeper.width//8, keeper.height//8), Image.Resampling.NEAREST)
    keeper_decoded, _ = decode(182, 0)
    if not compare_native_reference(keeper_decoded, keeper):
        raise ValueError("182/0 decoded image differs from retained native reference")
    mobile_ref = REPO / "art/run/pilot-385/source/385-2-0.png"
    mobile_decoded, _ = decode(385, 2)
    if not compare_native_reference(mobile_decoded, Image.open(mobile_ref)):
        raise ValueError("385/2 decoded image differs from retained native reference")
    inventory = json.loads((HERE / "source-inventory.json").read_text())
    rows = []
    folder = HERE / "sources/native"
    folder.mkdir(parents=True, exist_ok=True)
    for entry in inventory["identities"]:
        if entry["provider"] != "vanilla" or entry["kind"] != "stationary":
            continue
        image, row = decode(entry["archive"], entry["record"])
        dest = folder / f"{row['archive']}_{row['record']}-0.png"
        if dest.exists() and not compare_native_reference(image, Image.open(dest)):
            raise ValueError(f"Refusing to overwrite inconsistent original {dest}")
        if not dest.exists():
            image.save(dest)
        row["source"] = str(dest)
        row["id"] = entry["id"]
        rows.append(row)
    rows.sort(key=lambda r: (r["archive"], r["record"]))
    for start in range(0, len(rows), 30):
        contact = Image.new("RGB", (1200, 1100), "#303238")
        draw = ImageDraw.Draw(contact)
        for i, row in enumerate(rows[start:start+30]):
            image = Image.open(row["source"]).convert("RGBA")
            factor = min(1.0, 188/image.width, 186/image.height)
            if factor < 1:
                image = image.resize((round(image.width*factor), round(image.height*factor)), Image.Resampling.NEAREST)
            x, y = (i % 6)*200, (i // 6)*220
            contact.paste(image, (x+(200-image.width)//2, y+30), image)
            draw.text((x+4, y+4), f"{row['archive']}/{row['record']} ({row['recordFrameCount']} frames)", fill="white")
        contact.save(HERE / f"native-source-contact-{start//30:02d}.png")
    result = {"status": "original-extraction-reference-parity-checked",
              "verification": {"keeper182Frame0PixelParity": True, "mobile385Record2Frame0PixelParity": True},
              "sources": rows}
    (HERE / "native-sources.json").write_text(json.dumps(result, indent=2)+"\n")
    print(json.dumps({"nativeSources": len(rows), "independentRetainedReferenceChecks": 2}))


if __name__ == "__main__":
    main()
