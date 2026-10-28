from pathlib import Path
from PIL import Image,ImageDraw
import sys
ROOT=Path(__file__).resolve().parent/'npc425-mouths-v1';mood=sys.argv[1];p=ROOT/mood;raw=Image.open(p/f'mouth/{mood}/raw.png').convert('RGB');canvas=Image.new('RGB',(9*300,400),'#222222');d=ImageDraw.Draw(canvas)
for i,s in enumerate('ABCDEFGHX'):
 r,c=divmod(i,3);im=raw.transform((64,29),Image.Transform.EXTENT,(c*raw.width/3,r*raw.height/3,(c+1)*raw.width/3,(r+1)*raw.height/3),Image.Resampling.NEAREST);canvas.paste(im.crop((20,3,45,19)).resize((300,192),Image.Resampling.NEAREST),(i*300,28));d.text((i*300+3,5),s,fill='white')
 for y in range(3,19):d.text((i*300+2,28+(y-3)*12),str(y),fill='white')
canvas.save(p/'raw-mouth-coordinate-review.png')
