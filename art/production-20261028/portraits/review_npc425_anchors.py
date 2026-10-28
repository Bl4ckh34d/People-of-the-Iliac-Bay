from pathlib import Path
from PIL import Image,ImageDraw
ROOT=Path(__file__).resolve().parent/'npc425-mouths-v1';c=Image.new('RGB',(4*300,5*200),'#222222');d=ImageDraw.Draw(c)
for row,m in enumerate(['happy','angry','sad','afraid','guarded-v2']):
 for col,s in enumerate('BDEG'):
  im=Image.open(ROOT/m/f'candidate/{s}.png');c.paste(im.crop((20,4,45,18)).resize((300,168),Image.Resampling.NEAREST),(col*300,row*200+24));d.text((col*300+5,row*200+4),m+' '+s,fill='white')
  for y in range(4,18):d.text((col*300,row*200+24+(y-4)*12),str(y),fill='white')
c.save(ROOT/'five-mood-anchor-review.png')
