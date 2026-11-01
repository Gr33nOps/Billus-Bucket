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

# Scarf and its outline must turn with the torso, not stay screen-right.
gold={(227,182,98,255),(184,119,50,255)}
for step in range(4):
 right,left=frames[4+step],frames[8+step]
 r={(x,y) for y in range(28,35) for x in range(32) if right.getpixel((x,y)) in gold}
 l={(x,y) for y in range(28,35) for x in range(32) if left.getpixel((x,y)) in gold}
 assert r and {(31-x,y) for x,y in r}==l,('scarf did not turn with torso',step)
print('Directional scarf placement passed.')

ink=(35,36,56,255)
for step in range(4):
 for x,y in [(9,17),(8,18)]:
  assert frames[4+step].getpixel((4+x,12+y))==ink,('shirt pixel breaks scarf edge',step)
  assert frames[8+step].getpixel((27-x,12+y))==ink,('left scarf edge',step)
print('Scarf neck outline passed in all walking frames.')
