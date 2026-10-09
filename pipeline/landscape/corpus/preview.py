"""Small metadata-free RGB PNG plans, entirely standard library, no fonts installed."""

import math
import re
import struct
import zlib

# Five-bit rows of a deliberately small plan-label font, authored here.
FONT = {
    "A": [14,17,17,31,17,17,17], "B": [30,17,17,30,17,17,30],
    "C": [14,17,16,16,16,17,14], "D": [30,17,17,17,17,17,30],
    "E": [31,16,16,30,16,16,31], "F": [31,16,16,30,16,16,16],
    "G": [14,17,16,23,17,17,14], "H": [17,17,17,31,17,17,17],
    "I": [14,4,4,4,4,4,14], "J": [7,2,2,2,2,18,12],
    "K": [17,18,20,24,20,18,17], "L": [16,16,16,16,16,16,31],
    "M": [17,27,21,21,17,17,17], "N": [17,25,21,19,17,17,17],
    "O": [14,17,17,17,17,17,14], "P": [30,17,17,30,16,16,16],
    "Q": [14,17,17,17,21,18,13], "R": [30,17,17,30,20,18,17],
    "S": [15,16,16,14,1,1,30], "T": [31,4,4,4,4,4,4],
    "U": [17,17,17,17,17,17,14], "V": [17,17,17,17,17,10,4],
    "W": [17,17,17,21,21,21,10], "X": [17,17,10,4,10,17,17],
    "Y": [17,17,10,4,4,4,4], "Z": [31,1,2,4,8,16,31],
    "0": [14,17,19,21,25,17,14], "1": [4,12,4,4,4,4,14],
    "2": [14,17,1,2,4,8,31], "3": [30,1,1,14,1,1,30],
    "4": [2,6,10,18,31,2,2], "5": [31,16,16,30,1,1,30],
    "6": [14,16,16,30,17,17,14], "7": [31,1,2,4,8,8,8],
    "8": [14,17,17,14,17,17,14], "9": [14,17,17,15,1,1,14],
    "-": [0,0,0,31,0,0,0], ".": [0,0,0,0,0,12,12],
    ":": [0,12,12,0,12,12,0], "/": [1,2,2,4,8,8,16],
    "(": [2,4,8,8,8,4,2], ")": [8,4,2,2,2,4,8],
    "+": [0,4,4,31,4,4,0], " ": [0]*7,
}


def rgb(hex_colour):
    return tuple(int(hex_colour[i:i+2],16) for i in (1,3,5))


def deflate_zero_runs(data):
    """A canonical zlib stream: fixed Huffman codes and distance-1 zero runs.

    PNG's Sub filter makes flat fills mostly zero. All other bytes are literals.
    Choosing the tokens here avoids zlib/zlib-ng compressor version differences.
    Codes and length tables follow RFC 1951, sections 3.2.5 and 3.2.6.
    """
    symbols = []
    for symbol in range(288):
        if symbol < 144:
            code, width = 0x30+symbol, 8
        elif symbol < 256:
            code, width = 0x190+symbol-144, 9
        elif symbol < 280:
            code, width = symbol-256, 7
        else:
            code, width = 0xc0+symbol-280, 8
        symbols.append((int(f"{code:0{width}b}"[::-1],2),width))
    bases = (3,4,5,6,7,8,9,10,11,13,15,17,19,23,27,31,
             35,43,51,59,67,83,99,115,131,163,195,227,258)
    extras = (0,)*8+(1,)*4+(2,)*4+(3,)*4+(4,)*4+(5,)*4+(0,)
    lengths = {}
    for i,(base,extra) in enumerate(zip(bases,extras)):
        for length in range(base,min(258,base+(1<<extra)-1)+1):
            lengths[length] = (i+257,length-base,extra)
    output = bytearray(b"\x78\x01")  # zlib, 32 KiB window, no dictionary
    bits = count = 0

    def emit(value, width):
        nonlocal bits, count
        bits |= value << count
        count += width
        while count >= 8:
            output.append(bits & 255)
            bits >>= 8
            count -= 8

    emit(3,3)  # final block, fixed Huffman tree
    offset = 0
    for run in re.finditer(b"\x00{4,}",data):
        for value in data[offset:run.start()]:
            emit(*symbols[value])
        emit(*symbols[0])  # establishes the byte for distance-1 copies
        remaining = run.end()-run.start()-1
        while remaining >= 3:
            length = min(258,remaining)
            symbol,value,width = lengths[length]
            emit(*symbols[symbol])
            emit(value,width)
            emit(0,5)  # distance code 0 means distance 1, no extra bits
            remaining -= length
        for _ in range(remaining):
            emit(*symbols[0])
        offset = run.end()
    for value in data[offset:]:
        emit(*symbols[value])
    emit(*symbols[256])  # end of block
    if count:
        output.append(bits)
    output.extend(struct.pack(">I",zlib.adler32(data)&0xffffffff))
    return bytes(output)


class Canvas:
    def __init__(self, width, height, colour=(241,239,230)):
        self.width, self.height = width, height
        self.pixels = bytearray(bytes(colour)*(width*height))

    def pixel(self, x, y, colour):
        x, y = int(x), int(y)
        if 0 <= x < self.width and 0 <= y < self.height:
            start = 3*(y*self.width+x)
            self.pixels[start:start+3] = bytes(colour)

    def line(self, a, b, colour):
        ax, ay = a
        bx, by = b
        n = max(1, int(math.ceil(max(abs(bx-ax),abs(by-ay)))))
        for i in range(n+1):
            self.pixel(round(ax+(bx-ax)*i/n),round(ay+(by-ay)*i/n),colour)

    def polygon(self, points, fill, edge=(47,49,49)):
        # Even/odd scanline filling works for the concave shell too.
        for y in range(max(0,int(min(p[1] for p in points))),min(self.height,int(max(p[1] for p in points))+1)):
            cross = []
            for a,b in zip(points,points[1:]+points[:1]):
                if (a[1] <= y+.5 < b[1]) or (b[1] <= y+.5 < a[1]):
                    cross.append(a[0]+(y+.5-a[1])*(b[0]-a[0])/(b[1]-a[1]))
            cross.sort()
            for left,right in zip(cross[::2],cross[1::2]):
                for x in range(max(0,int(math.ceil(left))),min(self.width,int(right)+1)):
                    self.pixel(x,y,fill)
        for a,b in zip(points,points[1:]+points[:1]):
            self.line(a,b,edge)

    def text(self, x, y, text, colour=(38,43,48), scale=1):
        for char in text.upper().replace("_"," "):
            for row,bits in enumerate(FONT.get(char,FONT[" "])):
                for col in range(5):
                    if bits & (1<<(4-col)):
                        for dy in range(scale):
                            for dx in range(scale):
                                self.pixel(x+col*scale+dx,y+row*scale+dy,colour)
            x += 6*scale

    def paste(self, other, x, y):
        for row in range(other.height):
            dest = 3*((y+row)*self.width+x)
            src = 3*row*other.width
            self.pixels[dest:dest+3*other.width] = other.pixels[src:src+3*other.width]

    def png(self):
        def chunk(name,data):
            return struct.pack(">I",len(data))+name+data+struct.pack(">I",zlib.crc32(name+data)&0xffffffff)
        rows = bytearray()
        for y in range(self.height):
            row = self.pixels[3*y*self.width:3*(y+1)*self.width]
            rows.append(1)  # PNG Sub filter, three bytes per RGB pixel
            rows.extend(row[:3])
            rows.extend((value-previous)&255 for value,previous in zip(row[3:],row))
        return (b"\x89PNG\r\n\x1a\n"+chunk(b"IHDR",struct.pack(">IIBBBBB",self.width,self.height,8,2,0,0,0))
                +chunk(b"IDAT",deflate_zero_runs(bytes(rows)))+chunk(b"IEND",b""))


def corners(position, size, yaw):
    x,_,z = position
    w,_,d = size
    c,s = math.cos(math.radians(yaw)),math.sin(math.radians(yaw))
    return [(x+c*dx+s*dz,z-s*dx+c*dz) for dx,dz in
            [(-w/2,-d/2),(w/2,-d/2),(w/2,d/2),(-w/2,d/2)]]


def plan(layout, inventory, variant):
    canvas = Canvas(640,480)
    w,h,d = layout["dimensions_m"]
    factor = min(360/w,380/d)
    def point(p):
        return (205+p[0]*factor,252+p[1]*factor)
    canvas.text(14,12,layout["id"],scale=2)
    canvas.text(14,32,f"{w:g} X {d:g} M - HEIGHT {h:g} M - {variant}")
    canvas.polygon([point(p) for p in layout["outline_xz_m"]],rgb(layout["floor_colour"]))
    # Metric grid: draw over floor only, leaving the absent L quadrant blank.
    for x in range(math.ceil(-w/2),math.floor(w/2)+1):
        for z in range(math.ceil(-d/2),math.floor(d/2)+1):
            if layout["id"] != "awkward_l" or x <= 0 or z <= 0:
                canvas.pixel(*point((x,z)),(215,210,197))
    entries = inventory["objects"]
    draw_order = sorted(enumerate(entries,1),key=lambda pair: pair[1]["placement"]["position_m"][1])
    for index,entry in draw_order:
        p,b = entry["placement"],entry["box"]
        pts = [point(v) for v in corners(p["position_m"],b["size_m"],b["yaw_deg"])]
        canvas.polygon(pts,rgb(entry["colours"][0]["hex"]),edge=(40,42,47) if p["support"]["kind"] == "floor" else (246,241,224))
        centre = point((p["position_m"][0],p["position_m"][2]))
        canvas.text(centre[0]-3,centre[1]-3,str(index),(248,247,237))
        if max(b["size_m"]) > .5:
            a = math.radians(b["yaw_deg"])
            canvas.line(centre,(centre[0]-math.sin(a)*12,centre[1]-math.cos(a)*12),(45,42,47))
    canvas.text(408,48,"ID / C4 KIND (+ SUPPORTED)")
    for index,entry in enumerate(entries,1):
        suffix = " +" if entry["placement"]["support"]["kind"] != "floor" else ""
        canvas.text(408,61+(index-1)*9,f"{index:02} {entry['kind']}{suffix}")
    canvas.text(14,453,"-Z UP   +X RIGHT   GRID DOTS 1 M   SYNTHETIC")
    canvas.line((24,440),(24+factor,440),(38,43,48))
    canvas.text(28,426,"1 M")
    return canvas
