-- Billu's Bucket: authored pixel shapes, drawn directly in Aseprite.
-- Run with aseprite -b --script-param root=<Assets> --script tools/draw_assets.lua
local root=app.params.root
local C={
 ink=0x232438, hair=0x303044, hairlight=0x4e4c62, skin=0xd68a50,
 skinlight=0xde965b, skinshade=0xb46b43, teal=0x3d9993, teallight=0x71afa4,
 tealshade=0x28626d, gold=0xe3b662, goldlight=0xf0d398, goldshade=0xb87732,
 pants=0x35465e, pantslight=0x506784, shoe=0x694638, blue=0x3c86a7,
 bluelight=0x77b7c8, blueshade=0x34536f, rim=0x9bc8cf,
 soot=0x353548, ash=0x6c667f, white=0xf1e3bf, green=0x62ad65,
 lime=0xb9dc83, darkgreen=0x315e4e, orange=0xf6a345, red=0xd75543,
}
local function col(c) if type(c)=='string' then c=C[c] end
 return app.pixelColor.rgba((c>>16)&255,(c>>8)&255,c&255,255) end
local function px(im,x,y,c) if x>=0 and y>=0 and x<im.width and y<im.height then im:drawPixel(x,y,c and col(c) or 0) end end
local function rect(im,x,y,w,h,c) for yy=y,y+h-1 do for xx=x,x+w-1 do px(im,xx,yy,c) end end end
local function line(im,x0,y0,x1,y1,c)
 local dx,dy=math.abs(x1-x0),-math.abs(y1-y0);local sx=x0<x1 and 1 or -1;local sy=y0<y1 and 1 or -1;local e=dx+dy
 while true do px(im,x0,y0,c);if x0==x1 and y0==y1 then break end;local e2=2*e;if e2>=dy then e=e+dy;x0=x0+sx end;if e2<=dx then e=e+dx;y0=y0+sy end end
end
local function poly(im,p,c)
 local ymin,ymax=999,-999;for _,v in ipairs(p) do ymin=math.min(ymin,v[2]);ymax=math.max(ymax,v[2]) end
 for y=ymin,ymax do local xs={};for i,a in ipairs(p) do local b=p[i%#p+1];if (a[2]<=y and b[2]>y) or (b[2]<=y and a[2]>y) then xs[#xs+1]=a[1]+(y-a[2])*(b[1]-a[1])/(b[2]-a[2]) end end;table.sort(xs);for i=1,#xs-1,2 do for x=math.ceil(xs[i]),math.floor(xs[i+1]) do px(im,x,y,c) end end end
 for i,a in ipairs(p) do local b=p[i%#p+1];line(im,a[1],a[2],b[1],b[2],c) end
end
local function oval(im,x,y,w,h,c)
 for yy=y,y+h-1 do for xx=x,x+w-1 do if ((xx-x-(w-1)/2)/(w/2))^2+((yy-y-(h-1)/2)/(h/2))^2<=1 then px(im,xx,yy,c) end end end
end
local function img(w,h) return Image(w,h,ColorMode.RGB) end
local function paste(a,b,x,y) a:drawImage(b,Point(x,y)) end
local function contour(im,points,c)
 for i,p in ipairs(points) do local q=points[i%#points+1];line(im,p[1],p[2],q[1],q[2],c) end
end
local function saveFrames(name,frames,w,h,tags)
 local s=Sprite(w,h,ColorMode.RGB);s.layers[1].name=name
 for i,im in ipairs(frames) do if i>1 then s:newEmptyFrame() end;s:newCel(s.layers[1],i,im,Point(0,0));s.frames[i].duration=0.11 end
 if tags then for _,t in ipairs(tags) do local tag=s:newTag(t[2],t[3]);tag.name=t[1] end end
 s:saveAs(root..'/Sources/'..name..'.aseprite')
 local sheet=img(w*#frames,h);for i,im in ipairs(frames) do paste(sheet,im,(i-1)*w,0) end
 -- Intermediate construction drawings stay editable without shipping unused PNGs.
 if name~='billu-body' and name~='cash-sheet' and name~='ui-font' then
  sheet:saveAs(root..'/'..name..'.png')
 end
 s:close();return sheet
end

-- Bucket, 28x16. The lower sides sit inside Billu's closed hands.
local bucket=img(28,16)
poly(bucket,{{3,4},{24,4},{22,13},{20,15},{7,15},{5,13}},'ink')
poly(bucket,{{4,5},{23,5},{21,13},{19,14},{8,14},{6,12}},'blueshade')
poly(bucket,{{5,5},{22,5},{20,12},{19,13},{8,13},{6,11}},'blue')
-- Broad metal shading: the light stays upper-left in every facing.
line(bucket,6,7,7,10,'bluelight');line(bucket,7,11,9,12,'bluelight')
line(bucket,21,7,20,11,'blueshade')
-- A broad reflected patch and a shaded bottom seam, like the rooftop tank.
rect(bucket,8,7,3,3,0x5299b1)
line(bucket,10,13,18,13,'blueshade')
line(bucket,15,10,17,10,0x5299b1)
-- An open oval mouth, with a bright front lip distinct from the body.
oval(bucket,0,0,28,7,'ink');oval(bucket,2,1,24,5,'bluelight')
oval(bucket,4,2,20,3,'ink')
rect(bucket,4,5,20,1,'bluelight');rect(bucket,7,6,14,1,'blue')
line(bucket,5,1,11,1,'rim')
saveFrames('bucket',{bucket},28,16)
-- Square browser icon: preserve the exact game's 28x16 bucket proportions.
local favicon=img(32,32);paste(favicon,bucket,2,8)
saveFrames('favicon',{favicon},32,32)

-- Inverted wreck: compressed base, crumpled side, split and bent ground-facing rim.
local wreck=img(28,16)
local wreckEdge={{7,1},{12,1},{14,3},{18,2},{21,3},{23,7},{22,9},{25,12},{27,13},{25,15},{3,15},{0,13},{3,11},{4,7},{6,6}}
poly(wreck,wreckEdge,0x49403e)
poly(wreck,{{7,4},{11,3},{14,5},{18,4},{20,5},{21,8},{19,9},{22,12},{6,12},{5,9}},0x665b53)
-- Burned paint is fully gone: warm charcoal, soot and exposed ash.
poly(wreck,{{6,5},{10,4},{11,7},{9,9},{10,12},{5,12},{4,9}},0x332e30)
poly(wreck,{{17,3},{21,4},{22,8},{20,10},{17,8},{18,6}},0x332e30)
line(wreck,8,2,11,2,0x998a77);line(wreck,12,3,14,4,0x998a77)
-- A short angular split and exposed steel on the folded panel.
line(wreck,14,7,13,9,'ink');line(wreck,13,9,15,11,'ink')
line(wreck,15,7,14,9,0x998a77)
poly(wreck,{{3,11},{10,12},{14,11},{19,12},{24,11},{26,13},{24,14},{16,13},{11,14},{3,13}},'ink')
line(wreck,3,12,8,13,0x998a77);line(wreck,17,12,22,13,0x998a77)
contour(wreck,wreckEdge,'ink')
saveFrames('bucket-burned',{wreck},28,16)

-- Character: 24x32. Body, arms, and expression are authored at native size.
local function simplifyBillu(a)
 -- Larger flat color groups match the props; keep expressive eyes and silhouette.
 local merge={{'skinlight','skin'},{'goldlight','gold'}}
 for y=0,a.height-1 do for x=0,a.width-1 do
  local c=a:getPixel(x,y)
  for _,pair in ipairs(merge) do if c==col(pair[1]) then px(a,x,y,pair[2]);break end end
 end end
 return a
end
-- Close the silhouette after all face, sleeve and scarf pixels have been painted.
-- Collect edges first so the result does not depend on scan direction.
local function outlineBillu(a)
 local edges={}
 for y=0,a.height-1 do for x=0,a.width-1 do
  if a:getPixel(x,y)~=0 and a:getPixel(x,y)~=col('ink') then
   local exposed=x==0 or x==a.width-1 or y==0 or y==a.height-1
   if not exposed then
    exposed=a:getPixel(x-1,y)==0 or a:getPixel(x+1,y)==0 or
            a:getPixel(x,y-1)==0 or a:getPixel(x,y+1)==0
   end
   if exposed then edges[#edges+1]={x,y} end
  end
 end end
 for _,point in ipairs(edges) do px(a,point[1],point[2],'ink') end
 return a
end
local function sideWalk(step,left)
 local a=img(24,32)
 -- Profile gait: far leg first, near leg in front, alternating contacts.
 local poses={{7,17,30,30},{10,14,29,30},{17,7,30,30},{14,11,30,29}}
 local q=poses[step+1];local back,front,by,fy=table.unpack(q)
 poly(a,{{10,23},{14,23},{back+3,by-1},{back,by-1}},'ink')
 line(a,12,25,back+1,by-2,'pants')
 local backSandalY=math.min(by,29)
 rect(a,back,backSandalY,6,3,'ink');rect(a,back+1,backSandalY+1,4,1,'shoe')
 poly(a,{{12,23},{16,23},{front+3,fy-1},{front,fy-1}},'ink')
 line(a,14,26,front+1,fy-2,'pants');line(a,15,26,front+2,fy-2,'pants')
 local frontSandalY=math.min(fy,29)
 rect(a,front,frontSandalY,6,3,'ink');rect(a,front+1,frontSandalY+1,4,1,'shoe')
 -- Sleeves reach the bucket; only the small hands at its rim are exposed.
 poly(a,{{1,0},{4,0},{4,5},{8,9},{10,13},{7,15},{3,10},{1,6}},'ink')
 poly(a,{{2,4},{3,4},{3,6},{6,9},{9,13},{7,14},{3,10},{2,7}},'teal')
 line(a,3,7,5,10,'tealshade')
 rect(a,2,1,2,3,'skin');px(a,2,4,'ink');px(a,3,4,'ink')
 -- Three-quarter torso, shifted toward the direction of travel.
 poly(a,{{9,13},{16,13},{18,18},{17,25},{8,25},{7,19}},'ink')
 poly(a,{{10,14},{15,14},{17,18},{16,23},{9,23},{8,19}},'teal')
 rect(a,9,18,2,5,'tealshade');line(a,14,18,14,22,'teallight')
 rect(a,9,24,8,3,'pants');rect(a,13,24,3,2,'pantslight')
 -- Near arm is behind the head, never painted across the eye or cheek.
 poly(a,{{19,0},{22,0},{22,6},{20,10},{17,15},{14,14},{17,9},{19,5}},'ink')
 poly(a,{{20,4},{21,4},{21,6},{19,9},{16,14},{15,14},{17,9},{20,5}},'teal')
 line(a,20,7,18,10,'tealshade')
 rect(a,20,1,2,3,'skin');px(a,20,4,'ink');px(a,21,4,'ink')
 -- A readable profile: one eye, a forward nose, and hair behind the ear.
 poly(a,{{6,5},{8,3},{14,2},{17,4},{18,7},{18,9},{20,10},{20,12},{18,12},{18,14},{15,16},{10,16},{6,12},{5,8}},'ink')
 poly(a,{{9,6},{16,6},{17,8},{17,10},{19,10},{19,11},{17,11},{17,14},{14,15},{10,14},{8,11}},'skin')
 poly(a,{{6,5},{8,3},{13,3},{16,4},{17,6},{14,6},{12,7},{10,7},{9,10},{7,10},{7,12},{5,9}},'hair')
 line(a,8,4,12,3,'hairlight')
 rect(a,13,8,4,1,'skinshade')
 rect(a,13,9,4,2,'white');rect(a,15,9,2,2,'ink')
 px(a,9,11,'skinshade');rect(a,14,13,3,1,'ink');px(a,15,13,'white')
 -- Scarf belongs to the torso: turn its neck band, tail and outline together.
 line(a,9,16,17,16,'ink');line(a,10,17,16,17,'gold')
 line(a,9,18,14,18,'ink')
 rect(a,15,17,4,6,'ink');rect(a,16,18,2,4,'goldshade')
 line(a,16,18,16,20,'gold')
 -- Close the scarf silhouette before turning, including the shirt edge behind it.
 rect(a,18,18,1,5,'ink')
 px(a,9,17,'ink');px(a,8,18,'ink')
 if left then
  local turned=img(24,32)
  for y=0,31 do for x=0,23 do turned:drawPixel(23-x,y,a:getPixel(x,y)) end end
  a=turned
 end
 -- A short shirt hem and trouser fold use the same restrained prop shading.
 for x=10,14 do if a:getPixel(x,22)==col('teal') then px(a,x,22,'tealshade') end end
 for y=25,27 do for x=8,16 do
  if a:getPixel(x,y)==col('pants') and a:getPixel(x-1,y)==col('ink') then px(a,x,y,'pantslight') end
 end end
 -- Keep highlights on the world-facing upper-left surfaces.
 for y=18,22 do
  for x=7,17 do
   if a:getPixel(x,y)==col('teallight') then px(a,x,y,'teal') end
  end
 end
 for y=19,21 do
  local hx=left and 10 or 11
  if a:getPixel(hx,y)==col('teal') or a:getPixel(hx,y)==col('tealshade') then px(a,hx,y,'teallight') end
 end
 return outlineBillu(simplifyBillu(a))
end
local function billu(side,step,burn)
 if side and not burn then return sideWalk(step) end
 local a=img(24,32)
 -- sandals and legs (four distinct walk contacts, fixed root)
 local lx,rx=7,14;local ly,ry=30,30
 if side then local offsets={{4,16,30,29},{8,12,30,29},{8,16,29,30},{7,13,29,30}};local q=offsets[step+1];lx,rx,ly,ry=table.unpack(q) end
 rect(a,lx,25,4,ly-24,'ink');rect(a,rx,25,4,ry-24,'ink')
 rect(a,lx+1,27,2,ly-26,'pants');rect(a,rx+1,27,2,ry-26,'pants')
 local leftSandalY=math.min(ly,29);local rightSandalY=math.min(ry,29)
 rect(a,lx-1,leftSandalY,6,3,'ink');rect(a,rx,rightSandalY,6,3,'ink')
 rect(a,lx,leftSandalY+1,4,1,'shoe');rect(a,rx+1,rightSandalY+1,4,1,'shoe')
 rect(a,6,23,13,5,'ink');rect(a,7,23,5,4,'pants');rect(a,13,23,5,4,'pants')
 rect(a,7,24,4,1,'pantslight');rect(a,14,24,3,1,'pantslight')
 if side then
  line(a,9,26,lx+1,28,'pants');line(a,15,26,rx+1,28,'pants')
  line(a,10,26,lx+2,28,'pantslight');line(a,16,26,rx+2,28,'pantslight')
 end
 -- shirt silhouette
 poly(a,{{7,13},{16,13},{19,18},{18,24},{6,24},{5,18}},'ink')
 poly(a,{{8,14},{15,14},{17,18},{17,23},{7,23},{6,18}},'teal')
 rect(a,8,17,2,4,'teallight');line(a,8,22,15,22,'tealshade');px(a,16,18,'tealshade')
 -- arms: hands overlap the combined bucket at its bottom corners
 if not burn then
  poly(a,{{1,0},{4,0},{4,5},{8,12},{7,16},{4,12},{1,6}},'ink')
  poly(a,{{19,0},{22,0},{22,6},{19,12},{16,16},{15,12},{19,5}},'ink')
  poly(a,{{2,4},{3,4},{3,6},{6,10},{7,13},{5,14},{2,7}},'teal')
  poly(a,{{20,4},{21,4},{21,6},{19,10},{17,13},{16,13},{20,5}},'teal')
  line(a,3,7,5,10,'tealshade');line(a,20,7,18,10,'tealshade')
  rect(a,2,1,2,3,'skinlight');rect(a,20,1,2,3,'skinlight')
  px(a,3,3,'skinshade');px(a,20,3,'skinshade')
  line(a,2,4,3,4,'ink');line(a,20,4,21,4,'ink')
  poly(a,{{4,9},{7,11},{9,14},{7,16},{5,12}},'tealshade')
  poly(a,{{17,10},{19,9},{18,13},{16,16},{14,14}},'tealshade')
  line(a,5,10,8,13,'teal');line(a,18,10,15,13,'teal')
 else
  poly(a,{{5,15},{7,16},{5,22},{3,24},{1,23},{2,19}},'ink')
  poly(a,{{18,15},{20,16},{22,22},{21,24},{19,23},{17,19}},'ink')
  -- Full sleeves bend down to the wrists; only the hands are soot-black.
  poly(a,{{5,16},{7,17},{5,21},{3,22},{2,20}},'teal')
  poly(a,{{18,16},{19,17},{21,20},{20,22},{18,20}},'teal')
  line(a,4,19,3,20,'tealshade');line(a,19,19,20,20,'tealshade')
  contour(a,{{5,16},{7,17},{5,21},{3,22},{2,20}},'ink')
  contour(a,{{18,16},{19,17},{21,20},{20,22},{18,20}},'ink')
  rect(a,2,21,3,2,'soot');rect(a,19,21,3,2,'soot')
  px(a,2,21,'ash');px(a,20,21,'ash')
 end
 -- face and hair (black tousled hair, warm face, big eyebrows)
 local skin=burn and 'soot' or 'skin';local light=burn and 'ash' or 'skinlight'
 oval(a,4,3,16,14,'ink');rect(a,6,6,12,8,skin);rect(a,8,13,8,3,skin)
 rect(a,4,9,2,3,skin);rect(a,18,9,2,3,skin);rect(a,7,6,9,7,light)
 poly(a,{{4,6},{5,3},{8,3},{10,2},{14,3},{17,3},{19,5},{19,8},{17,8},{16,5},{13,6},{12,5},{9,6},{7,5},{6,8},{4,8}},'ink')
 line(a,7,4,10,3,'hairlight');line(a,12,4,16,4,'hair');px(a,17,5,'hairlight')
 -- The ears previously painted over the inner arm contour, merging both shapes.
 if not burn and not side then
  line(a,4,9,4,11,'ink');line(a,19,9,19,11,'ink')
  px(a,5,12,'ink');px(a,18,12,'ink')
  line(a,3,9,3,11,'tealshade');line(a,20,9,20,11,'tealshade')
 end
 if side then
  rect(a,13,7,3,1,'skinshade');rect(a,12,9,4,2,'white');rect(a,15,9,1,2,'ink')
  px(a,18,11,skin);px(a,19,11,light);px(a,11,10,'skinshade')
  rect(a,12,13,5,2,'ink');rect(a,13,13,3,1,'white')
 else
  rect(a,7,7,3,1,burn and 'ash' or 'skinshade');rect(a,14,7,3,1,burn and 'ash' or 'skinshade')
  rect(a,7,9,4,2,'white');rect(a,13,9,4,2,'white');rect(a,9,9,2,2,'ink');rect(a,15,9,2,2,'ink')
  px(a,12,11,burn and 'ash' or 'skinshade')
  if burn then
   line(a,9,11,8,13,'bluelight');line(a,15,11,16,13,'bluelight')
   px(a,11,13,'white');px(a,12,12,'white');px(a,13,13,'white')
  else rect(a,10,13,5,2,'ink');rect(a,11,13,3,1,'white') end
 end
 -- A dark border separates the scarf from the face, shirt and sleeves.
 line(a,7,16,18,16,'ink')
 line(a,8,17,16,17,'goldshade');line(a,10,17,15,17,'gold')
 line(a,7,17,7,18,'ink');line(a,8,18,14,18,'ink')
 rect(a,15,17,4,6,'ink');rect(a,16,18,2,4,'goldshade')
 line(a,16,18,16,20,'gold');px(a,17,18,'goldlight')
 return outlineBillu(simplifyBillu(a))
end
local carry={}
for row=0,2 do for f=0,3 do
 local a=img(32,48)
 local body=row==0 and billu(false,f,false) or sideWalk(f,row==2)
 paste(a,body,4,12);paste(a,bucket,2,0)
 carry[#carry+1]=a
end end
saveFrames('billu-carry',carry,32,48,{{'straight',1,4},{'walk-right',5,8},{'walk-left',9,12}})
local burned=billu(false,0,true);local blink=burned:clone();rect(blink,7,9,4,2,'soot');rect(blink,13,9,4,2,'soot');rect(blink,7,10,4,1,'ash');rect(blink,13,10,4,1,'ash')
saveFrames('billu-burned',{burned,blink},24,32,{{'cry',1,2}})
saveFrames('billu-body',{billu(false,0,false)},24,32)

-- Four 12x8 paper poses: clean border, pale inset, single printed medallion.
local money={}
local bends={{1,1,1,1,1,1,1,1,1,1,1,1},
 {1,1,1,1,0,0,0,0,1,1,1,1},
 {0,0,0,0,1,1,1,1,1,1,1,1},
 {1,1,1,1,1,1,1,1,0,0,0,0}}
for f=1,4 do local a=img(12,8)
 for x=0,11 do
  local y=bends[f][x+1]
  rect(a,x,y,1,6,'ink')
  if x>0 and x<11 then rect(a,x,y+1,1,4,'lime') end
  
 end
 -- Small corner stamps and a dark central seal remain readable in rotation.
 for _,x in ipairs({2,9}) do px(a,x,bends[f][x+1]+2,'green');px(a,x,bends[f][x+1]+3,'green') end
 for x=5,6 do rect(a,x,bends[f][x+1]+1,1,4,'green');rect(a,x,bends[f][x+1]+2,1,2,'darkgreen') end
 money[#money+1]=a
end
saveFrames('cash-sheet',money,12,8,{{'flutter',1,4}})
-- Pakistani-inspired game notes: Rs10,20,50,100,500,1000,5000.
-- SBP main colors: green, orange-green, purple, red, deep green, dark blue, mustard.
-- Orange is dominant on Rs20; its green detail is deliberately just one pixel.
-- Rs10 uses warm olive paper; Rs500 uses a cooler, richer green field.
-- These are readable pixel-art interpretations of SBP's green/deep-green notes.
local notePalettes={{0x71804d,0xc4cc96},{0xb66937,0xe7a65c},{0x79558e,0xbda4cb},{0xa84f5b,0xd99398},{0x254f3a,0x63a277},{0x365880,0x93afc8},{0x8e723d,0xc8af70}}
local variants={}
for denomination,palette in ipairs(notePalettes) do
 for _,source in ipairs(money) do local note=source:clone()
  for y=0,7 do for x=0,11 do local c=source:getPixel(x,y)
   if c==col('green') or c==col('darkgreen') then px(note,x,y,palette[1])
   elseif c==col('lime') then px(note,x,y,palette[2]) end
  end end
  if denomination==2 then px(note,9,bends[#variants%4+1][10]+2,0x718150) end
  variants[#variants+1]=note
 end
end
saveFrames('rupee-notes',variants,12,8)


local bombs={};for f=0,3 do local a=img(16,16)
 -- Upright fuse; one-pixel squash/stretch keeps the silhouette crisp.
 local x,y,w,h=2,5,12,11
 if f==1 then x,y,w,h=1,6,14,9 elseif f==3 then x,y,w,h=3,4,10,12 end
 oval(a,x,y,w,h,'ink');oval(a,x+1,y+1,w-2,h-2,'soot')
 oval(a,x+2,y+2,math.floor(w/2),3,'hairlight');rect(a,x+3,y+2,2,1,'ash');px(a,x+2,y+3,'ash')
 rect(a,6,y-1,4,2,'ink');rect(a,7,y-1,2,1,'goldshade')
 line(a,8,y-2,8,2,'gold')
 rect(a,7,0,3,3,'red');rect(a,7,1,3,2,'orange');px(a,8,1,'white')
 if f%2==0 then px(a,6,1,'orange');px(a,10,2,'gold') else px(a,9,0,'gold');px(a,6,3,'red') end
 bombs[#bombs+1]=a end;saveFrames('bomb-sheet',bombs,16,16,{{'fuse',1,4}})

-- Uneven, hand-shaped blast: flash, torn flame, rolling smoke, loose embers.
-- No concentric discs or evenly spaced rays (which read as a little sun).
local fx={}
local blast={{6,20},{13,18},{10,9},{19,14},{22,3},{26,13},{35,7},{33,17},{43,15},{37,24},{45,30},{34,31},{36,40},{27,35},{21,44},{18,34},{8,38},{12,28},{3,26}}
local core={{13,23},{20,21},{18,16},{23,19},{26,10},{28,23},{35,20},{31,27},{36,30},{26,29},{23,36},{20,28},{13,30}}
local function scaled(points,s,dy)
 local p={};for _,q in ipairs(points) do p[#p+1]={math.floor(24+(q[1]-24)*s+.5),math.floor(24+(q[2]-24)*s+dy+.5)} end;return p
end
local function puff(a,x,y,r,light)
 -- Scale every corner with the radius; fixed offsets crossed over at r=2/3.
 local corner=math.max(1,math.floor(r*.5))
 local p={{x-r,y-corner},{x-corner,y-r},{x+corner,y-r},
  {x+r,y-corner},{x+r,y+corner},{x+corner,y+r},
  {x-corner,y+r},{x-r,y+corner}}
 poly(a,p,'hairlight');contour(a,p,'soot')
 if light then line(a,x-r+3,y-r+3,x-1,y-r+2,'ash');rect(a,x-2,y-r+2,4,2,'ash') end
end
for f=0,7 do local a=img(48,48)
 if f>=2 then
  local k=f-2
  puff(a,14-math.floor(k*.7),19-k,math.max(3,8-math.max(0,k-2)),f<6)
  puff(a,30+math.floor(k*.8),15-k,math.max(3,9-math.max(0,k-2)),f<6)
  puff(a,31+k,31-math.floor(k*.8),math.max(2,7-math.max(0,k-1)),f<5)
  if f<7 then puff(a,15-k,32-k,math.max(3,7-k),f<5) end
 end
 if f<4 then
  local s=({.36,.77,1,.82})[f+1]
  local p=scaled(blast,s,0);poly(a,p,'red');contour(a,p,'goldshade')
  poly(a,scaled(core,s,0),'orange')
  poly(a,scaled({{19,23},{23,22},{24,16},{27,24},{30,26},{25,27},{22,32},{22,26},{18,26}},s,0),'goldlight')
  if f<2 then rect(a,23,22,2,4,'white') end
 elseif f<6 then
  poly(a,{{17,27-f},{20,18-f},{22,25-f},{26,22-f},{25,30-f},{20,32-f}},'red')
  line(a,20,28-f,21,23-f,'orange')
 end
 if f>=1 and f<=5 then
  local k=f-1
  line(a,10-k,12-k,12-k,14-k,'gold');px(a,11-k,13-k,'goldlight')
  line(a,36+k,9-k,35+k,11-k,'orange')
  rect(a,39+k,33+k,2,2,'red');px(a,7-k,32+k,'orange')
 end
 fx[#fx+1]=a
end
saveFrames('explosion-sheet',fx,48,48,{{'burst',1,4},{'smoke',5,8}})

-- Rooftop: separate editable layers, 320x180. All geometry on a 1px grid.
local sky=img(320,180)
for y=0,179 do
 local t=math.floor(math.min(1,y/160)*31)/31
 local r=math.floor(54+55*t);local g=math.floor(57+23*t);local b=math.floor(96+14*t)
 rect(sky,0,y,320,1,(r<<16)|(g<<8)|b)
end
-- Very broad bands are broken by sparse clouds, not dithering noise.
local clouds=img(320,180)
local function cloud(x,y,points)
 -- Rounded step clusters with an atmospheric edge, lighter than building ink.
 local shape={}
 for _,p in ipairs(points) do
  shape[#shape+1]={x+p[1],y+p[2]}
 end
 poly(clouds,shape,0x6a5a7b)
 contour(clouds,shape,0x5e5271)
end
-- Three individually drawn silhouettes, rather than scaled copies.
-- Upper cloud: taller left puff with a long trailing edge.
cloud(157,58,{{0,0},{0,-1},{3,-1},{3,-3},{8,-3},{8,-5},{13,-5},{13,-8},{19,-8},{19,-6},{24,-6},{24,-4},{29,-4},{29,-2},{36,-2},{36,-1},{43,-1},{43,0}})
-- Lower left: low, broad double puff, with its taller lobe on the right.
cloud(68,95,{{0,0},{0,-1},{4,-1},{4,-3},{8,-3},{8,-5},{14,-5},{14,-3},{20,-3},{20,-4},{23,-4},{23,-6},{29,-6},{29,-4},{33,-4},{33,-2},{37,-2},{37,-1},{40,-1},{40,0}})
-- Small right cloud: a compact puff and a short left wisp.
cloud(224,85,{{0,0},{0,-1},{7,-1},{7,-2},{11,-2},{11,-4},{14,-4},{14,-5},{18,-5},{18,-3},{21,-3},{21,-1},{24,-1},{24,0}})
paste(sky,clouds,0,0)
-- Muted dusk: no sun or stars.
local far=img(320,180);local city=img(320,180)
local buildings={{0,128,19},{19,116,16},{35,125,21},{56,112,16},{72,130,22},{94,121,19},{113,110,17},{130,128,21},{151,118,24},{175,127,15},{190,108,18},{208,124,23},{231,114,16},{247,123,24},{271,108,18},{289,119,18},{307,125,13}}
for _,b in ipairs(buildings) do local x,y,w=table.unpack(b)
 local shape={{x,y},{x+3,y},{x+3,y-3},{x+w-4,y-3},{x+w-4,y},{x+w-1,y},{x+w-1,166},{x,166}}
 poly(far,shape,0x78677f);contour(far,shape,0x685d75)
 line(far,x+w-6,y-8,x+w-6,y-3,0x685d75)
end
local blocks={{-4,138,23},{23,131,24},{48,142,20},{69,126,23},{94,137,21},{118,130,24},{146,139,19},{168,123,22},{193,138,21},{217,131,24},{245,140,18},{267,124,22},{293,137,25}}
for i,b in ipairs(blocks) do local x,y,w=table.unpack(b);local shape={{x,y},{x+2,y},{x+2,y-2},{x+w-3,y-2},{x+w-3,y},{x+w-1,y},{x+w-1,166},{x,166}}
 poly(city,shape,0x675d78);contour(city,shape,0x58516b)
 for wy=y+7,156,12 do if i%3~=0 then rect(city,x+6,wy,2,5,0x59536c);rect(city,x+9,wy,2,5,0x59536c) end end
end
-- One connected utility line with varied spans and pole heights.
local poles={{48,126},{116,120},{203,128},{276,122}}
for _,p in ipairs(poles) do
 rect(city,p[1],p[2]-4,1,167-p[2]+4,0x484158)
 line(city,p[1]-3,p[2],p[1]+3,p[2],0x484158)
end
-- Continue past the end poles to off-screen anchors; no dangling crossarms.
local cableAnchors={{-12,119},{48,126},{116,120},{203,128},{276,122},{332,118}}
for i=1,#cableAnchors-1 do
 local a,b=cableAnchors[i],cableAnchors[i+1];local span=b[1]-a[1]
 local sag=({5,7,9,6,5})[i];local lastX,lastY=a[1],a[2]
 for x=a[1]+1,b[1] do
  local t=(x-a[1])/span
  local y=math.floor(a[2]+(b[2]-a[2])*t+4*sag*t*(1-t)+0.5)
  line(city,lastX,lastY,x,y,0x484158);lastX,lastY=x,y
 end
end
local roof=img(320,180)
rect(roof,0,157,320,9,0x886879);rect(roof,0,157,320,1,0xb58a82);rect(roof,0,165,320,15,0x51465e);rect(roof,0,165,320,2,0xaa807b)
-- Restrained plaster wear matches the rooftop room: broad shade, small chipped patches.
rect(roof,0,162,320,2,0x795c6e)
for _,q in ipairs({{57,160,4},{91,158,3},{126,161,5},{177,159,3},{217,161,4},{249,158,3}}) do
 rect(roof,q[1],q[2],q[3],1,0x795c6e)
 px(roof,q[1]+1,q[2]-1,0x9a7885)
end
for x=0,319,29 do rect(roof,x,168,1,12,0x3e3852);rect(roof,x+1,169,1,8,0x715a70);line(roof,x+2,178,x+27,178,0x3e3852);px(roof,x+5,167,0x886879) end
for _,q in ipairs({{14,171},{42,174},{80,169},{119,174},{157,171},{201,175},{252,170},{287,173}}) do rect(roof,q[1],q[2],3,1,0x665269) end
local props=img(320,180)
-- Rooftop room and tank at left.
rect(props,0,128,38,37,0x513e56);rect(props,1,130,34,35,0x8e5b59);rect(props,2,133,29,31,0xa36959)
rect(props,0,126,41,4,0x6e4650);rect(props,0,126,40,1,0xb58a82);rect(props,36,130,3,35,0x674653)
rect(props,5,141,17,24,0x533747);rect(props,7,143,13,22,0x322a40);rect(props,7,143,2,22,0x68454b);px(props,18,155,0xc49365)
rect(props,28,134,1,8,'ink');oval(props,24,139,9,10,'ink');oval(props,25,140,7,8,0xc47c47);oval(props,27,142,3,5,'gold');px(props,28,143,'white')
for _,q in ipairs({{3,135},{24,132},{31,151},{23,160},{33,138},{1,158}}) do rect(props,q[1],q[2],3,1,0x8e5b59);px(props,q[1]+2,q[2]+1,0xb77c64) end
rect(props,7,118,2,8,'ink');rect(props,28,118,2,8,'ink');line(props,9,120,28,125,'ink');line(props,28,120,9,125,'ink')
poly(props,{{7,103},{11,100},{26,100},{30,104},{30,118},{7,118}},'ink');rect(props,9,104,19,13,'blueshade');rect(props,10,104,5,12,0x3d6687)
line(props,8,108,29,108,'ink');line(props,8,114,29,114,'ink');rect(props,13,98,11,3,'ink');rect(props,15,99,7,1,0x59738b)
line(props,11,102,24,102,0x59738b);rect(props,25,105,2,2,0x406583);rect(props,11,110,2,3,0x4a7090)
-- Plants, clothesline, and right parapet.
local function plant(x,y)
 -- Draw the green blades first, then their shared one-pixel silhouette.
 -- This avoids overlapping black triangles and detached leaf-tip pixels.
 local leaves=img(21,15);local edged=img(21,15)
 for _,q in ipairs({{4,5},{7,1},{12,1},{16,5},{17,10},{4,10}}) do
  line(leaves,10,13,q[1],q[2],0x748969)
 end
 rect(leaves,9,11,3,3,0x748969)
 -- A few connected highlights and shaded stems, using the existing muted greens.
 line(leaves,10,12,7,3,0x607554);line(leaves,10,12,12,3,0x607554)
 line(leaves,5,6,7,8,0x96a477);line(leaves,14,7,16,5,0x96a477)
 px(leaves,7,1,0x96a477);px(leaves,12,1,0x96a477)
 for yy=0,14 do for xx=0,20 do
  if app.pixelColor.rgbaA(leaves:getPixel(xx,yy))>0 then
   for _,d in ipairs({{-1,0},{1,0},{0,-1},{0,1}}) do px(edged,xx+d[1],yy+d[2],'ink') end
  end
 end end
 paste(edged,leaves,0,0);paste(props,edged,x-10,y-14)
 -- Explicit pixel rows keep the rim level and the tapered sides symmetric.
 rect(props,x-5,y,12,3,'ink')
 rect(props,x-4,y+1,10,1,0xb77c59)
 line(props,x-3,y+1,x,y+1,0xc49365)
 rect(props,x-4,y+3,10,1,'ink');rect(props,x-3,y+3,8,1,0x743e43)
 rect(props,x-3,y+4,8,1,'ink');rect(props,x-2,y+4,6,1,0x743e43)
 rect(props,x-3,y+5,8,1,'ink')
 px(props,x-2,y+3,0x95584b);px(props,x-1,y+3,0x95584b)
 px(props,x+3,y+3,0x603941);px(props,x+2,y+4,0x603941)
end
plant(45,159);rect(props,290,153,30,12,0x7f5260);rect(props,287,152,33,3,0xb58a82);plant(307,146)
rect(props,291,154,29,1,0x694653);rect(props,291,155,2,8,0x956575)
rect(props,298,158,3,1,0x956575);rect(props,313,161,4,1,0x694653)
rect(props,268,120,2,45,0x232438);rect(props,269,121,1,43,0x6d4b5c);rect(props,316,121,2,31,0x232438)
line(props,267,123,290,127,'ink');line(props,290,127,319,124,'ink')
poly(props,{{274,125},{284,127},{284,141},{277,142},{275,139}},0x853f58);line(props,277,128,278,139,0xb75a68);line(props,282,128,282,140,0x6d3854)
poly(props,{{289,127},{301,126},{300,146},{292,145}},0xac7a7b);line(props,291,129,293,143,0xb99594);line(props,298,129,297,144,0x835e72)
for _,shape in ipairs({{{274,125},{284,127},{284,141},{277,142},{275,139}},{{289,127},{301,126},{300,146},{292,145}}}) do
 for i,p in ipairs(shape) do local q=shape[i%#shape+1];line(props,p[1],p[2],q[1],q[2],'ink') end
end
-- Foreground architectural edges use the same ink as the portable props.
line(props,0,126,40,126,'ink');line(props,40,126,40,129,'ink');line(props,0,129,40,129,'ink');line(props,38,130,38,164,'ink')
line(props,5,141,21,141,'ink');line(props,5,141,5,164,'ink');line(props,21,141,21,164,'ink')
line(props,287,152,319,152,'ink');line(props,287,152,287,154,'ink');line(props,287,154,319,154,'ink');line(props,290,155,290,164,'ink')
rect(props,276,124,1,3,'goldshade');rect(props,283,126,1,3,'goldshade');rect(props,290,126,1,3,'goldshade');rect(props,300,125,1,3,'goldshade')
-- Every scenery silhouette has a visible edge; distance uses muted plum.
-- Per-object contours above preserve single-pixel wires and narrow leaf gaps.
line(roof,0,157,319,157,'ink')
-- Enclose the warm front ledge highlight with a one-pixel edge on both sides.
line(roof,0,164,319,164,'ink')
-- The front ledge also passes in front of the side buildings and plant bases.
line(props,0,164,319,164,'ink')
line(roof,0,166,319,166,'ink')
local scene=Sprite(320,180,ColorMode.RGB)
local layers={{'Sky',sky},{'Distant town',far},{'Windows and wires',city},{'Rooftop',roof},{'Tank laundry and plants',props}}
local combined=img(320,180)
for i,t in ipairs(layers) do local layer=i==1 and scene.layers[1] or scene:newLayer();layer.name=t[1];scene:newCel(layer,1,t[2],Point(0,0));paste(combined,t[2],0,0) end
scene:saveAs(root..'/Sources/rooftop.aseprite');combined:saveAs(root..'/rooftop.png');scene:close()
-- Rooftop UI: muted painted timber and clean, unadorned bevels.
local sign=img(144,78)

local signEdge={{3,13},{138,13},{143,18},{141,66},{136,70},{7,70},{1,66},{0,18}}
poly(sign,signEdge,0x725661);contour(sign,signEdge,'ink')
poly(sign,{{4,16},{137,16},{140,19},{138,65},{135,67},{8,67},{4,64}},0x725661)
rect(sign,6,18,132,2,0xad8080);rect(sign,6,21,132,42,0x514352)
line(sign,9,65,132,65,0x9a7376)
-- Unadorned panel face; no corner studs.
line(sign,12,19,24,19,0xc19487);line(sign,118,64,130,64,0x725661)
-- Keep the outer contour above all painted panels and trim.
contour(sign,signEdge,'ink')
saveFrames('ui-sign',{sign},144,78)
local buttons={}
for f=0,1 do local b=img(68,18)
local buttonEdge={{3,0},{64,0},{67,3},{67,14},{64,17},{3,17},{0,14},{0,3}}
poly(b,buttonEdge,0x294957);contour(b,buttonEdge,'ink')
poly(b,{{4,1},{63,1},{66,4},{66,13},{63,16},{4,16},{1,13},{1,4}},f==0 and 0x396e78 or 0x4d8b91)
line(b,5,2,61,2,f==0 and 0x73a6a2 or 0xa2c8b6);line(b,5,14,61,14,0x294957)
-- Keep the button face free of decorative dots.
contour(b,buttonEdge,'ink')
buttons[#buttons+1]=b end
saveFrames('ui-button',buttons,68,18)
-- Editable atlas of the exact hand-authored runtime glyphs.
local fontSource=io.open(root..'/../Presentation/PixelFont.cs','r')
local fontText=fontSource:read('*a');fontSource:close()
local atlas=img(128,32);local index=0
for glyph,rows in fontText:gmatch("%['(.)'%]%s*=%s*new%[%]%s*{(.-)}") do
 local gx=(index%16)*8;local gy=math.floor(index/16)*8;local y=0
 for row in rows:gmatch('"([01]+)"') do
  for x=1,#row do if row:sub(x,x)=='1' then px(atlas,gx+x-1,gy+y,'white') end end
  y=y+1
 end
 index=index+1
end
saveFrames('ui-font',{atlas},128,32)
-- Browser loading logo uses the same authored glyphs and palette as the game.
local glyphs={}
for glyph,rows in fontText:gmatch("%['(.)'%]%s*=%s*new%[%]%s*{(.-)}") do
 local pattern={};for row in rows:gmatch('"([01]+)"') do pattern[#pattern+1]=row end
 glyphs[glyph]=pattern
end
glyphs["'"]={"1","1"}
local logo=img(128,12);local title="BILLU'S BUCKET";local width=0
for i=1,#title do local g=glyphs[title:sub(i,i)];width=width+(g and #g[1]+1 or 4) end
local x=math.floor((128-width+1)/2)
for i=1,#title do
 local g=glyphs[title:sub(i,i)]
 if g then
  for y,row in ipairs(g) do for xx=1,#row do if row:sub(xx,xx)=='1' then px(logo,x+xx,3+y,'ink') end end end
  for y,row in ipairs(g) do for xx=1,#row do if row:sub(xx,xx)=='1' then px(logo,x+xx-1,2+y,'goldlight') end end end
  x=x+#g[1]+1
 else x=x+4 end
end
saveFrames('loading-logo',{logo},128,12)
local audioIcons={}
for f=0,5 do local a=img(20,16)
 local edge={{2,0},{17,0},{19,2},{19,13},{17,15},{2,15},{0,13},{0,2}}
 poly(a,edge,0x34536f);contour(a,edge,'ink');line(a,3,1,16,1,0x77b7c8)
 if f<2 then
  rect(a,8,4,2,7,'white');rect(a,9,3,5,2,'white');rect(a,12,4,2,3,'white');oval(a,5,9,5,4,'white')
 elseif f<4 then
  rect(a,4,6,3,5,'white');poly(a,{{7,6},{10,4},{10,12},{7,10}},'white')
  line(a,13,5,15,7,'white');line(a,15,7,15,9,'white');line(a,15,9,13,11,'white')
 elseif f==4 then
  rect(a,6,4,3,8,'white');rect(a,11,4,3,8,'white')
 else
  poly(a,{{7,4},{13,8},{7,12}},'white')
 end
 if f<4 and f%2==1 then
  -- Muted state is visibly neutral even at native size.
  for y=0,15 do for x=0,19 do
   local c=a:getPixel(x,y)
   if app.pixelColor.rgbaA(c)>0 and c~=col('ink') then
    local l=math.floor((app.pixelColor.rgbaR(c)+app.pixelColor.rgbaG(c)+app.pixelColor.rgbaB(c))/3)
    a:drawPixel(x,y,col(l>150 and 0x94909d or 0x504c60))
   end
  end end
  line(a,4,12,15,3,'ink');line(a,4,13,15,4,0xc5bfca)
 end
 audioIcons[#audioIcons+1]=a
end
saveFrames('ui-audio',audioIcons,20,16)
print("Hand-drawn Billu's Bucket assets exported at native dimensions.")



