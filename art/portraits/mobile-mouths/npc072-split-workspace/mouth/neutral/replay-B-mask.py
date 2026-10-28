from pathlib import Path
from PIL import Image
import argparse, hashlib
ROWS={38:(26,38),39:(25,39),40:(24,40),41:(24,40),42:(25,40),43:(26,39),44:(28,37)}
def sha(p): return hashlib.sha256(p.read_bytes()).hexdigest()
def main():
 p=argparse.ArgumentParser()
 p.add_argument("--original",type=Path,required=True)
 p.add_argument("--reviewed-fullface",type=Path,required=True)
 p.add_argument("--fullface-out",type=Path,required=True)
 p.add_argument("--native-out",type=Path,required=True)
 p.add_argument("--expected-original-sha256",required=True)
 p.add_argument("--expected-fullface-sha256",required=True)
 a=p.parse_args()
 if sha(a.original)!=a.expected_original_sha256 or sha(a.reviewed_fullface)!=a.expected_fullface_sha256: raise SystemExit("Source pin mismatch")
 with Image.open(a.original) as im: base=im.convert("RGBA")
 with Image.open(a.reviewed_fullface) as im: src=im.convert("RGBA")
 if base.size!=(64,64) or src.size!=(64,64): raise SystemExit("Expected 64x64 source images")
 out=base.copy()
 for y,(x0,x1) in ROWS.items():
  for x in range(x0,x1): out.putpixel((x,y),src.getpixel((x,y)))
 allowed={(x,y) for y,(x0,x1) in ROWS.items() for x in range(x0,x1)}
 changed={(x,y) for y in range(64) for x in range(64) if out.getpixel((x,y))!=base.getpixel((x,y))}
 if not changed<=allowed: raise SystemExit("Pixels changed outside B mask")
 a.fullface_out.parent.mkdir(parents=True,exist_ok=True);a.native_out.parent.mkdir(parents=True,exist_ok=True)
 out.save(a.fullface_out);out.crop((0,35,64,64)).save(a.native_out)
 print("B replay complete",len(changed),"changed pixels; no second feather or palette pass")
if __name__=="__main__": main()
