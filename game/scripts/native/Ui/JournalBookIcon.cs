using Godot;

namespace EnFractal.Native.Ui;

/// <summary>The closed journal uses the same leather motif in the dock and during its flight.</summary>
public partial class JournalBookIcon : Button
{
    public override void _Ready()
    {
        Name = "JournalBookIcon"; TooltipText = "Open the journal · J";
        MouseDefaultCursorShape = CursorShape.PointingHand;
        foreach (var state in new[] { "normal", "hover", "pressed", "focus", "disabled" })
            AddThemeStyleboxOverride(state,new StyleBoxEmpty());
        MouseEntered += QueueRedraw; MouseExited += QueueRedraw; FocusEntered += QueueRedraw; FocusExited += QueueRedraw;
    }
    public Rect2 CoverRect => new(GetGlobalRect().Position+new Vector2(13,3),new Vector2(Size.X-26,Size.Y-24));
    public override void _Draw()
    {
        if (IsHovered() || HasFocus()) DrawCircle(Size*0.5f,Size.X*0.47f,new Color(0.9f,0.72f,0.4f,0.16f));
        DrawCover(this,new Rect2(new Vector2(13,3),new Vector2(Size.X-26,Size.Y-24)),1);
        var font=ThemeDB.FallbackFont;
        DrawString(font,new Vector2(9,Size.Y-2),"Journal · J",HorizontalAlignment.Center,Size.X-18,13,new Color("f0e6cf"));
    }
    public static void DrawCover(Control canvas,Rect2 rect,float light,bool reverse=false)
    {
        var origin=rect.Position; var size=rect.Size;
        if(size.X<0.5f||size.Y<1) return;
        canvas.DrawSetTransform(origin,0,size/new Vector2(100,140));
        canvas.DrawStyleBox(new StyleBoxFlat {BgColor=new Color(0,0,0,0.36f),CornerRadiusTopLeft=7,CornerRadiusTopRight=9,CornerRadiusBottomLeft=7,CornerRadiusBottomRight=9},new Rect2(4,5,99,140));
        canvas.DrawStyleBox(new StyleBoxFlat {BgColor=new Color("ac9165"),BorderColor=new Color("402a20"),BorderWidthLeft=2,BorderWidthBottom=3,CornerRadiusTopRight=6,CornerRadiusBottomRight=6},new Rect2(5,5,94,133));
        for(var y=12;y<135;y+=3) canvas.DrawLine(new Vector2(93,y),new Vector2(99,y),new Color("d7bf92"),0.7f);
        var leather=new Color(reverse ? "654734" : "563525"); leather.R*=light; leather.G*=light; leather.B*=light;
        canvas.DrawStyleBox(new StyleBoxFlat {BgColor=leather,BorderColor=new Color("2c1c17"),BorderWidthLeft=2,BorderWidthTop=2,BorderWidthRight=2,BorderWidthBottom=2,CornerRadiusTopLeft=5,CornerRadiusBottomLeft=5,CornerRadiusTopRight=9,CornerRadiusBottomRight=9},new Rect2(0,0,95,135));
        canvas.DrawRect(new Rect2(4,2,9,130),new Color("352219"));
        for(var y=20;y<128;y+=28) canvas.DrawRect(new Rect2(3,y,12,4),new Color("926341"));
        var rng=new RandomNumberGenerator {Seed=1847};
        for(var i=0;i<170;i++)
        {
            var p=new Vector2(rng.RandfRange(17,90),rng.RandfRange(5,129));
            canvas.DrawLine(p,p+new Vector2(rng.RandfRange(1,5),rng.RandfRange(-2,2)),new Color(0.85f,0.64f,0.37f,0.09f),0.6f);
        }
        canvas.DrawRect(new Rect2(18,9,68,116),new Color("ad8756"),false,0.8f);
        for(var y=13;y<123;y+=6) { canvas.DrawLine(new Vector2(16,y),new Vector2(16,y+2),new Color("c0a16e"),0.7f); canvas.DrawLine(new Vector2(89,y),new Vector2(89,y+2),new Color("c0a16e"),0.7f); }
        canvas.DrawLine(new Vector2(19,10),new Vector2(35,10),new Color("d2ac70"),2);
        canvas.DrawLine(new Vector2(70,124),new Vector2(86,124),new Color("d2ac70"),2);
        if(!reverse)
        {
            var gold=new Color("c6a36c");
            canvas.DrawArc(new Vector2(52,66),18,0,Mathf.Tau,40,gold,1,true);
            canvas.DrawLine(new Vector2(51,81),new Vector2(51,51),gold,1.1f,true);
            canvas.DrawPolyline(new[]{new Vector2(51,65),new Vector2(39,59),new Vector2(43,69),new Vector2(51,72),new Vector2(65,59),new Vector2(55,61),new Vector2(51,68)},gold,1,true);
            canvas.DrawString(ThemeDB.FallbackFont,new Vector2(22,38),"JOURNAL",HorizontalAlignment.Center,60,9,gold);
            canvas.DrawRect(new Rect2(75,133,8,10),new Color("854635"));
        }
        canvas.DrawSetTransform(Vector2.Zero);
    }
}
