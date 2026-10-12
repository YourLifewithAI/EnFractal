using Godot;
using System;
using EnFractal.Native.Kernel;

namespace EnFractal.Native.Ui;

/// <summary>Reversible cover opening/closing; input stays with the book until it reaches its dock.</summary>
public partial class JournalPresentation : Control
{
    public Func<CommandHost?> FindHost { get; set; } = () => null;
    public Action ActivityChanged { get; set; } = () => { };
    public JournalPanel Panel { get; private set; } = null!;
    public JournalBookIcon Dock { get; private set; } = null!;
    public bool WantsOpen { get; private set; }
    public float Progress { get; private set; }
    public bool IsActive => WantsOpen || Progress > 0;
    public bool IsAnimating => Progress > 0 && Progress < 1;
    private ColorRect _shade = null!;
    private JournalFlight _flight = null!;

    public override void _Ready()
    {
        Name="JournalPresentation"; MouseFilter=MouseFilterEnum.Ignore; SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        Dock=new JournalBookIcon(); AddChild(Dock);
        Dock.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomRight);
        Dock.OffsetLeft=-112; Dock.OffsetRight=-18; Dock.OffsetTop=-118; Dock.OffsetBottom=-18;
        Dock.Pressed+=Toggle;
        _shade=new ColorRect {Color=new Color(0.06f,0.04f,0.025f,0),MouseFilter=MouseFilterEnum.Stop,Visible=false};
        AddChild(_shade); _shade.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        Panel=new JournalPanel {Visible=false,FindHost=FindHost,CloseRequested=Toggle}; AddChild(Panel);
        _flight=new JournalFlight {Visible=false,Presentation=this,MouseFilter=MouseFilterEnum.Stop};
        AddChild(_flight); _flight.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
    }
    public void Toggle()
    {
        WantsOpen=!WantsOpen;
        if(WantsOpen) { Panel.OpenPage("Index"); Dock.ReleaseFocus(); }
        ActivityChanged(); UpdateVisuals();
    }
    public override void _Process(double delta)
    {
        var wasActive=IsActive;
        Progress=Mathf.MoveToward(Progress,WantsOpen ? 1 : 0,(float)delta/(WantsOpen ? 0.95f : 0.8f));
        UpdateVisuals();
        if(wasActive!=IsActive) { ActivityChanged(); if(!IsActive) Dock.GrabFocus(); }
    }
    private void UpdateVisuals()
    {
        Dock.Visible=!IsActive;
        _shade.Visible=IsActive; _shade.Color=new Color(0.055f,0.035f,0.02f,0.42f*Progress);
        var ink=Mathf.SmoothStep(0,1,Mathf.Clamp((Progress-0.82f)/0.18f,0,1));
        Panel.Visible=Progress>0.82f; Panel.Modulate=new Color(1,1,1,ink);
        _flight.Visible=IsActive&&Progress<1;
        _flight.Modulate=new Color(1,1,1,1-ink); _flight.QueueRedraw();
    }
}

public partial class JournalFlight : Control
{
    public JournalPresentation Presentation { get; set; } = null!;
    public override void _Draw()
    {
        var p=Presentation.Progress;
        var spread=Presentation.Panel.GetRect();
        var spine=spread.Position.X+spread.Size.X*0.465f;
        var right=new Rect2(spine,spread.Position.Y,spread.End.X-spine,spread.Size.Y);
        var origin=Presentation.Dock.CoverRect;
        if(p<0.42f)
        {
            var travel=Mathf.SmoothStep(0,1,p/0.42f);
            var position=origin.Position.Lerp(right.Position,travel);
            position.Y-=Mathf.Sin(travel*Mathf.Pi)*Size.Y*0.07f;
            JournalBookIcon.DrawCover(this,new Rect2(position,origin.Size.Lerp(right.Size,travel)),1);
            return;
        }
        var opening=Mathf.SmoothStep(0,1,(p-0.42f)/0.58f);
        var leftWidth=(spine-spread.Position.X)*opening;
        var visibleSpread=new Rect2(spine-leftWidth,spread.Position.Y,leftWidth+right.Size.X,spread.Size.Y);
        DrawStyleBox(new StyleBoxFlat {BgColor=new Color("503325"),BorderColor=new Color("2b1d18"),BorderWidthLeft=3,BorderWidthTop=3,BorderWidthRight=3,BorderWidthBottom=3,CornerRadiusTopLeft=12,CornerRadiusTopRight=12,CornerRadiusBottomLeft=12,CornerRadiusBottomRight=12},visibleSpread);
        var paper=visibleSpread.Grow(-18); DrawRect(paper,new Color("e7d2a6"));
        for(var layer=0;layer<5;layer++) DrawLine(new Vector2(paper.Position.X,paper.End.Y-layer*2),new Vector2(paper.End.X,paper.End.Y-layer*2),new Color("bba275"));
        for(var i=0;i<18;i++) DrawLine(new Vector2(spine+i,paper.Position.Y),new Vector2(spine+i,paper.End.Y),new Color(0.28f,0.15f,0.06f,(18-i)*0.014f));
        var turn=Mathf.Cos(opening*Mathf.Pi);
        if(turn>0)
            JournalBookIcon.DrawCover(this,new Rect2(spine,spread.Position.Y,right.Size.X*turn,spread.Size.Y),0.6f+0.4f*turn);
        else if(opening<0.91f)
        {
            var width=leftWidth*(-turn);
            DrawRect(new Rect2(spine-width,paper.Position.Y,width,paper.Size.Y),new Color("d3b986"));
            DrawLine(new Vector2(spine-width,paper.Position.Y),new Vector2(spine-width,paper.End.Y),new Color("987546"),2);
        }
    }
}
