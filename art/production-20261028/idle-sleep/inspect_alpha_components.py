"""Read-only sprite separation evidence; does not modify image pixels."""
from collections import deque
from pathlib import Path
from PIL import Image
import json,sys
p=Path(sys.argv[1])
im=Image.open(p).convert('RGBA')
w,h=im.size
mask=bytearray(1 if x>=128 else 0 for x in im.getchannel('A').tobytes())
parts=[]
for seed in range(w*h):
    if not mask[seed]:continue
    mask[seed]=0
    q=deque([seed]);count=0;x0=w;y0=h;x1=y1=0
    while q:
        i=q.popleft();x=i%w;y=i//w;count+=1
        x0=min(x0,x);y0=min(y0,y);x1=max(x1,x);y1=max(y1,y)
        for nx,ny in [(x-1,y),(x+1,y),(x,y-1),(x,y+1)]:
            if 0<=nx<w and 0<=ny<h:
                j=ny*w+nx
                if mask[j]:mask[j]=0;q.append(j)
    if count>100:parts.append({'pixels':count,'bounds':[x0,y0,x1+1,y1+1]})
print(json.dumps({'path':str(p),'size':[w,h],'components':sorted(parts,key=lambda x:x['bounds'][1])}))
