"""RGB PNG, metadata removal, deterministic contact sheets; stdlib only."""
import struct
import zlib
from pathlib import Path

SIGNATURE=b'\x89PNG\r\n\x1a\n'


def chunk(kind,data):
    return struct.pack('>I',len(data))+kind+data+struct.pack('>I',zlib.crc32(kind+data)&0xffffffff)


def encode(width,height,pixels):
    if len(pixels)!=width*height*3: raise ValueError('pixel count')
    rows=bytearray()
    for y in range(height):
        row=pixels[y*width*3:(y+1)*width*3]
        rows.append(1)
        rows.extend((v-(row[i-3] if i>=3 else 0))%256 for i,v in enumerate(row))
    return SIGNATURE+chunk(b'IHDR',struct.pack('>IIBBBBB',width,height,8,2,0,0,0))+chunk(b'IDAT',zlib.compress(rows,9))+chunk(b'IEND',b'')


def decode(data):
    if data[:8]!=SIGNATURE: raise ValueError('PNG signature')
    offset=8; packed=bytearray()
    while offset<len(data):
        count=struct.unpack_from('>I',data,offset)[0]; kind=data[offset+4:offset+8]
        value=data[offset+8:offset+8+count]; crc=struct.unpack_from('>I',data,offset+8+count)[0]
        if zlib.crc32(kind+value)&0xffffffff!=crc: raise ValueError('PNG CRC')
        if kind==b'IHDR': width,height,depth,colour,comp,filtering,interlace=struct.unpack('>IIBBBBB',value)
        if kind==b'IDAT': packed.extend(value)
        offset+=12+count
    if depth!=8 or colour not in (2,6) or interlace: raise ValueError('PNG requires RGB/RGBA 8-bit noninterlaced')
    n=3 if colour==2 else 4; stride=width*n; raw=zlib.decompress(packed)
    output=bytearray(); previous=bytearray(stride)
    for y in range(height):
        mode=raw[y*(stride+1)]; row=bytearray(raw[y*(stride+1)+1:(y+1)*(stride+1)])
        for i in range(stride):
            a=row[i-n] if i>=n else 0; b=previous[i]; c=previous[i-n] if i>=n else 0
            if mode==0: v=0
            elif mode==1: v=a
            elif mode==2: v=b
            elif mode==3: v=(a+b)//2
            elif mode==4:
                p=a+b-c; pa,pb,pc=abs(p-a),abs(p-b),abs(p-c)
                v=a if pa<=pb and pa<=pc else b if pb<=pc else c
            else: raise ValueError('PNG filter')
            row[i]=(row[i]+v)%256
        output.extend(row if n==3 else bytes(v for i,v in enumerate(row) if i%4!=3))
        previous=row
    return width,height,output


def strip(path):
    path=Path(path); w,h,p=decode(path.read_bytes()); path.write_bytes(encode(w,h,p))


# Original 5x7 bitmap alphabet, no font downloads or installed-font dependency.
_GLYPHS={
'A':['01110','10001','10001','11111','10001','10001','10001'],
'B':['11110','10001','10001','11110','10001','10001','11110'],
'C':['01111','10000','10000','10000','10000','10000','01111'],
'D':['11110','10001','10001','10001','10001','10001','11110'],
'E':['11111','10000','10000','11110','10000','10000','11111'],
'F':['11111','10000','10000','11110','10000','10000','10000'],
'G':['01111','10000','10000','10111','10001','10001','01111'],
'H':['10001','10001','10001','11111','10001','10001','10001'],
'I':['11111','00100','00100','00100','00100','00100','11111'],
'J':['00111','00010','00010','00010','10010','10010','01100'],
'K':['10001','10010','10100','11000','10100','10010','10001'],
'L':['10000','10000','10000','10000','10000','10000','11111'],
'M':['10001','11011','10101','10101','10001','10001','10001'],
'N':['10001','11001','10101','10011','10001','10001','10001'],
'O':['01110','10001','10001','10001','10001','10001','01110'],
'P':['11110','10001','10001','11110','10000','10000','10000'],
'Q':['01110','10001','10001','10001','10101','10010','01101'],
'R':['11110','10001','10001','11110','10100','10010','10001'],
'S':['01111','10000','10000','01110','00001','00001','11110'],
'T':['11111','00100','00100','00100','00100','00100','00100'],
'U':['10001','10001','10001','10001','10001','10001','01110'],
'V':['10001','10001','10001','10001','10001','01010','00100'],
'W':['10001','10001','10001','10101','10101','10101','01010'],
'X':['10001','10001','01010','00100','01010','10001','10001'],
'Y':['10001','10001','01010','00100','00100','00100','00100'],
'Z':['11111','00001','00010','00100','01000','10000','11111'],
'0':['01110','10001','10011','10101','11001','10001','01110'],
'1':['00100','01100','00100','00100','00100','00100','01110'],
'2':['01110','10001','00001','00010','00100','01000','11111'],
'3':['11110','00001','00001','01110','00001','00001','11110'],
'4':['00010','00110','01010','10010','11111','00010','00010'],
'5':['11111','10000','10000','11110','00001','00001','11110'],
'6':['01110','10000','10000','11110','10001','10001','01110'],
'7':['11111','00001','00010','00100','01000','01000','01000'],
'8':['01110','10001','10001','01110','10001','10001','01110'],
'9':['01110','10001','10001','01111','00001','00001','01110'],
' ':['00000']*7, '_':['00000']*6+['11111'], '-':['00000']*3+['11111']+['00000']*3,
'.':['00000']*6+['00100'], ':':['00000','00100','00100','00000','00100','00100','00000'],
'/':['00001','00010','00010','00100','01000','01000','10000'],
}


def text(pixels,width,height,x,y,value,scale=2):
    for ch in value.upper():
        for j,row in enumerate(_GLYPHS.get(ch,_GLYPHS[' '])):
            for i,v in enumerate(row):
                if v=='1':
                    for dy in range(scale):
                        for dx in range(scale):
                            xx,yy=x+i*scale+dx,y+j*scale+dy
                            if 0<=xx<width and 0<=yy<height:
                                pos=(yy*width+xx)*3; pixels[pos:pos+3]=b'\x32\x3a\x3e'
        x+=6*scale


def sheet(folder,views,label,setup):
    folder=Path(folder); tile_w,tile_h=480,270; width=tile_w*3; height=tile_h*2+110
    pixels=bytearray(b'\xee\xeb\xe0'*(width*height))
    text(pixels,width,height,16,12,label)
    answer=(f"{setup['gameplay_mode']}  WATER {setup['water']}  LAT {setup['latitude_deg']}  "
            f"BEARING {setup['neg_z_bearing_deg']}  {setup['season']}  DAY {setup['day_of_year']}  "
            f"SOLAR {int(setup['solar_time_h']):02}:{round(setup['solar_time_h']%1*60):02}")
    text(pixels,width,height,16,34,answer)
    for index,view in enumerate(views):
        w,h,p=decode((folder/(view['name']+'.png')).read_bytes())
        x0=index%3*tile_w; y0=60+index//3*(tile_h+24)
        for y in range(tile_h):
            for x in range(tile_w):
                source=(min(h-1,y*h//tile_h)*w+min(w-1,x*w//tile_w))*3
                target=((y0+y)*width+x0+x)*3
                pixels[target:target+3]=p[source:source+3]
        flags=' - GROUND MISSING' if view.get('ground_missing') else ' - EYE BLOCKED' if view.get('inside_geometry') else ''
        text(pixels,width,height,x0+8,y0+tile_h+4,view['name']+flags,1)
    data=encode(width,height,pixels)
    if len(data)>=2_000_000: raise ValueError('sheet exceeds 2 MB')
    (folder/'sheet.png').write_bytes(data)
