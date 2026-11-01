from PIL import Image
from pathlib import Path
root=Path('BillusBucket/Assets')
for p in root.glob('*.png'):
 im=Image.open(p).convert('RGBA'); assert {value for count,value in im.getchannel('A').getcolors()} <= {0,255},p.name
 assert (root/'Sources'/(p.stem+'.aseprite')).exists(),p.name
sheet=Image.open(root/'billu-carry.png').convert('RGBA');assert sheet.size==(384,48)
frames=[sheet.crop((i*32,0,(i+1)*32,48)) for i in range(12)]
bucket=Image.open(root/'bucket.png').convert('RGBA')
for i,f in enumerate(frames):
 assert f.crop((2,0,30,12)).tobytes()==bucket.crop((0,0,28,12)).tobytes(),('bucket drift',i)
 assert f.getbbox()[3]==44,('ground alignment',i)
for start in [4,8]:assert len({f.tobytes() for f in frames[start:start+4]})==4
assert frames[4].transpose(Image.Transpose.FLIP_LEFT_RIGHT).tobytes()!=frames[8].tobytes()
icon=Image.open(root/'favicon.png').convert('RGBA');assert icon.crop((2,8,30,24)).tobytes()==bucket.tobytes()
print('All PNGs have binary alpha and editable sources; 12 character frames, unique gaits, fixed bucket/ground alignment, exact favicon verified.')
