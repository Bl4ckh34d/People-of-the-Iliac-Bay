"""Pack generated art by point sampling; preserve original RGBA and source files."""
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont
from collections import Counter
import json, hashlib, statistics

ROOT=Path(__file__).resolve().parent
SOURCE=ROOT.parents[1]/'standing/source/native-idle/381-0.png'
GEN=ROOT/'381-jump-front-apex-generated-v1.png'
source=Image.open(SOURCE).convert('RGBA')
generated=Image.open(GEN).convert('RGBA')
# Native head cross-section y5..15 is19px, generated y340..400 is141px.
# This matches head/body scale instead of stretching a tucked pose to standing height.
scale=19/141
size=(round(generated.width*scale),round(generated.height*scale))
reduced=generated.resize(size,Image.Resampling.NEAREST)
canvas=(128,192)
frame=Image.new('RGBA',canvas)
offset=(round(64-498.5*scale),round(16-287*scale))
frame.alpha_composite(reduced,offset)
frame.save(ROOT/'381-jump-front-apex-native-v1.png')
base=Image.new('RGBA',canvas)
base.alpha_composite(source,(24,16))
font=ImageFont.truetype('C:/Windows/Fonts/arial.ttf',16)
review=Image.new('RGB',(128*3*4,192*4+62),(42,45,49))
draw=ImageDraw.Draw(review)
for n,(im,label,cutout) in enumerate([(base,'Native reference — unchanged',True),(frame,'Generated apex — native scale',False),(frame,'Same apex — cutout alpha>=128',True)]):
    im=im.copy()
    if cutout:
        im.putalpha(im.getchannel('A').point(lambda v:255 if v>=128 else 0))
    enlarged=im.resize((512,768),Image.Resampling.NEAREST)
    review.paste(enlarged,(512*n,44),enlarged)
    draw.text((512*n+10,12),label,font=font,fill='white')
    draw.line((512*n+10,44+181*4,512*(n+1)-10,44+181*4),fill=(90,90,90))
review.save(ROOT/'381-jump-front-apex-review.png')
alpha=Counter(frame.getchannel('A').getdata())
bbox=frame.getchannel('A').point(lambda v:255 if v>=128 else 0).getbbox()
assert bbox and bbox[0]>0 and bbox[1]>0 and bbox[2]<128 and bbox[3]<192
data={'status':'single-keyframe-candidate-requires-visual-review','appearance':381,'state':'jumping','bearing':0,'phase':'apex','generatedSource':GEN.name,'generatedSourceSha256':hashlib.sha256(GEN.read_bytes()).hexdigest(),'nativeIdentitySource':str(SOURCE),'nativeIdentitySha256':hashlib.sha256(SOURCE.read_bytes()).hexdigest(),'nativeIdentitySize':list(source.size),'output':'381-jump-front-apex-native-v1.png','outputSha256':hashlib.sha256((ROOT/'381-jump-front-apex-native-v1.png').read_bytes()).hexdigest(),'canvas':list(canvas),'scale':scale,'scaleBasis':'native head width19 vs generated head width141; pose not stretched to standing height','placement':list(offset),'alpha128Bounds':list(bbox),'alphaHistogram':dict(sorted(alpha.items())),'medianCutoutAlpha':statistics.median([v for v in frame.getchannel('A').getdata() if v>=128]),'sourceAlphaPreservedAtSampledPixels':True,'paletteQuantization':False,'anatomyPixelEditing':False,'runtimeIntegrated':False,'userApproved':False,'knownLimits':['One frontal apex keyframe; no complete animation or other bearings.','Point sampling only; generated colors not guaranteed exact native palette.','Visual acceptance remains separate from technical packing.']}
(ROOT/'381-jump-front-apex-native-v1.json').write_text(json.dumps(data,indent=2)+'\n',encoding='utf-8')
print(json.dumps({k:v for k,v in data.items() if k!='alphaHistogram'},indent=2))
