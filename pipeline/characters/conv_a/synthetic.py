"""An original three-eyed kite bird, drawn with Pillow, for a third-shape test."""
import argparse
import os
from pathlib import Path

from PIL import Image, ImageDraw

from convert import canonical


def create(folder):
    folder = Path(folder)
    os.makedirs(folder,exist_ok=True)
    outline = [[302,109],[426,267],[489,357],[393,338],[361,459],
               [296,504],[255,441],[202,338],[108,358],[180,255]]
    image = Image.new("RGB",(600,720),"#F8F5EC")
    draw = ImageDraw.Draw(image)
    draw.line(outline+[outline[0]],fill="#242933",width=5,joint="curve")
    for x,y,r in ((247,289,21),(300,262,26),(350,292,18)):
        draw.ellipse((x-r,y-r,x+r,y+r),outline="#242933",width=4)
        draw.ellipse((x-5,y-4,x+7,y+9),fill="#242933")
    draw.line([(274,348),(302,364),(326,348)],fill="#242933",width=4)
    draw.line([(298,483),(294,534),(328,565),(294,591),(312,621)],fill="#242933",width=10)
    draw.ellipse((277,608,351,640),outline="#242933",width=5)
    draw.line([(382,401),(449,442),(489,508),(538,486),(542,450)],fill="#242933",width=5)
    # Distractors: handwriting and notebook binding are deliberately absent
    # from the interpretation, just as a model would ignore them in a photo.
    draw.text((210,665),"KITE BIRD",fill="#242933")
    for x in range(30,580,40):
        draw.arc((x,2,x+20,36),0,300,fill="#65615A",width=3)
    image.save(folder/"drawing.png")
    description = "Name: Kite Bird\nAbout: a three-eyed kite creature hopping on one springy leg, with a curly tail.\nColours: teal, gold and plum.\n"
    (folder/"description.txt").write_bytes(description.encode("utf-8"))
    def part(name,role,parent,pivot,shapes):
        return dict(name=name,role=role,parent=parent,pivot=pivot,shapes=shapes)
    spec = dict(version=1,name="Kite Bird",image_size=[600,720],origin=[312,640,0],
                observations=["Pointed kite body with uneven side wings, three eyes, one bent leg and a curled tail."],
                uncertainties=["Back depth and colour placement are inferred; the drawing is a flat line sketch."],
                colour_reasoning="Teal body, gold leg and eye whites, plum pupils, mouth and tail.",
                motion=dict(kind="hop",reason="One spring-like zigzag leg supports the body."),
                palette=dict(body="#42A4A3",gold="#F0C665",ink="#63375F"),parts=[])
    p = spec["parts"]
    p.append(part("body","body",None,[300,420,0],[dict(kind="volume",contour=outline,center=[300,330,0],thickness=130,colour="body",smooth=False)]))
    for i,(x,y,r) in enumerate(((247,289,21),(300,262,26),(350,292,18))):
        name = "eye_"+str(i)
        p.append(part(name,"eye","body",[x,y,60],[dict(kind="ellipsoid",center=[x,y,3],radii=[r,r,5],colour="gold",surface="body")]))
        p.append(part("pupil_"+str(i),"pupil",name,[x,y,67],[dict(kind="ellipsoid",center=[x+2,y+2,8],radii=[6,7,3],colour="ink",surface="body")]))
    p.append(part("mouth","mouth","body",[300,360,60],[dict(kind="tube",points=[[274,348,3],[302,364,3],[326,348,3]],radii=[3,3,3],colour="ink",surface="body")]))
    p.append(part("spring_leg","leg","body",[298,483,0],[dict(kind="tube",points=[[298,483,0],[294,534,0],[328,565,0],[294,591,0],[312,621,3]],radii=[9,8,7,8,9],colour="gold")]))
    p.append(part("foot","foot","spring_leg",[312,621,3],[dict(kind="ellipsoid",center=[314,624,8],radii=[37,16,30],colour="gold")]))
    p.append(part("tail","prop","body",[382,401,-10],[dict(kind="tube",points=[[382,401,-10],[449,442,-15],[489,508,-5],[538,486,4],[542,450,10]],radii=[9,8,6,4,1],colour="ink")]))
    (folder/"interpretation.json").write_bytes(canonical(spec))
    return folder


if __name__=="__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--out",required=True,type=Path)
    create(parser.parse_args().out)
    print("SYNTHETIC INPUT PASS: original Pillow drawing, description, interpretation")
