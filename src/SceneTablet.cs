using System.Globalization;
using System.Windows;
using System.Windows.Media;
namespace LittleShelter;

public sealed partial class Scene
{
    private RegisteredPoseFrames tabletFrames=null!;
    private readonly Dictionary<string,Rect> tabletButtons=new();
    private int tabletHitGeneration=-1;
    public Rect TabletPanelRect { get; private set; }=Rect.Empty;
    public Rect TabletScreenRect { get; private set; }=Rect.Empty;
    public Rect TabletDeviceRect { get; private set; }=Rect.Empty;
    public Point[] TabletGrips { get; private set; }=Array.Empty<Point>();
    public IReadOnlyDictionary<string,Rect> TabletButtons => tabletButtons;
    private void LoadTabletAssets(string root)
    {
        var atlas=new Atlas(Path.Combine(root,manifest.TabletAtlas),manifest.TabletRegions);
        if (atlas.Sprites.Length != 12 || manifest.TabletFeet.Length != 12 || manifest.TabletHeights.Length != 12 || manifest.TabletHands.Length != 12)
            throw new InvalidDataException("平板姿势/脚部/握持锚点不完整");
        tabletFrames=new RegisteredPoseFrames(atlas,manifest.TabletFeet,manifest.TabletHeights);
    }
    public void ClearTabletHits()
    {
        tabletButtons.Clear(); tabletHitGeneration=-1; TabletPanelRect=TabletScreenRect=Rect.Empty;
    }
    // Input invalidates the old page's buttons immediately, while the screen
    // still visible until the next render must continue to intercept clicks.
    public void ClearTabletButtons() => tabletButtons.Clear();
    private string? TabletHit(Point point)
    {
        var session=behavior.Tablet;
        if (!session.Active || !session.Matches(save) || !session.ScreenVisible || tabletHitGeneration != session.Generation || TabletPanelRect.IsEmpty) return null;
        if (!new RectangleGeometry(TabletPanelRect,9,9).FillContains(point)) return null;
        if (session.Interactive) foreach(var button in tabletButtons) if (button.Value.Contains(point)) return "tablet:"+button.Key;
        return "tablet:surface";
    }
    private Point TabletPoint(double x,double y,int frame)
    {
        double scale=150.0/manifest.TabletHeights[frame];
        return new Point(behavior.X+(x-manifest.TabletFeet[frame][0])*scale,behavior.Y+(y-manifest.TabletFeet[frame][1])*scale);
    }
    private void DrawHeldTablet(DrawingContext dc)
    {
        var session=behavior.Tablet; TabletDeviceRect=Rect.Empty; TabletGrips=Array.Empty<Point>();
        if (!session.DedicatedPose || !session.Matches(save)) return;
        int frame=session.PoseFrame; var hands=manifest.TabletHands[frame];
        var l=TabletPoint(hands[0],hands[1],frame); var r=TabletPoint(hands[2],hands[3],frame); TabletGrips=new[]{l,r};
        var device=new Rect(Math.Min(l.X,r.X)+1,(l.Y+r.Y)/2-10,Math.Max(18,Math.Abs(r.X-l.X)-2),21);
        TabletDeviceRect=device;
        dc.PushOpacity(session.DeviceVisibility);
        dc.DrawRoundedRectangle(Brush(255,114,131,143),new Pen(Brush(255,88,107,124),.7),device,2,2);
        var screen=device; screen.Inflate(-2,-2);
        dc.DrawRoundedRectangle(Brush(255,202,227,235),null,screen,1,1);
        dc.DrawLine(new Pen(Brush(170,252,254,255),.7),new Point(screen.Left+2,screen.Top+2),new Point(screen.Right-2,screen.Top+2));
        dc.Pop();
        // Redraw just the authored grip areas over the bezel. Hands remain
        // attached to the same complete pose; no detached animated limbs.
        var gripClip=new GeometryGroup(); foreach(var p in TabletGrips) gripClip.Children.Add(new RectangleGeometry(new Rect(p.X-4,p.Y-5,8,10)));
        dc.PushClip(gripClip); var sprite=tabletFrames.Sample(new ClipSample(frame,frame,0,0));
        dc.DrawImage(sprite.Image,new Rect(behavior.X+tabletFrames.Bounds.X,behavior.Y+tabletFrames.Bounds.Y,tabletFrames.Bounds.Width,tabletFrames.Bounds.Height)); dc.Pop();
    }
    private void DrawTablet(DrawingContext dc,string theme)
    {
        ClearTabletHits(); var session=behavior.Tablet;
        if (!session.Active || !session.Matches(save)) return;
        if (session.Phase == "tap") DrawTabletCreation(dc);
        if (!session.ScreenVisible || TabletDeviceRect.IsEmpty) return;
        var expanded=new Rect(behavior.X>=240 ? 42 : 158,Math.Clamp(behavior.Y-174,14,118),280,200);
        double p=session.Expansion;
        var d=TabletDeviceRect;
        var panel=new Rect(d.X+(expanded.X-d.X)*p,d.Y+(expanded.Y-d.Y)*p,d.Width+(expanded.Width-d.Width)*p,d.Height+(expanded.Height-d.Height)*p);
        TabletPanelRect=panel; tabletHitGeneration=session.Generation;
        // One physical tablet projects a continuous screen. The connecting
        // light is decoration and never expands the interactive hit region.
        if (p>.01) {
            var pen=new Pen(Brush((byte)(95*p),144,214,232),.7);
            dc.DrawLine(pen,new Point(d.Left,d.Top),new Point(panel.Left,panel.Bottom));
            dc.DrawLine(pen,new Point(d.Right,d.Top),new Point(panel.Right,panel.Bottom));
        }
        dc.DrawRoundedRectangle(Brush(255,118,144,160),new Pen(Brush(255,95,119,137),.8),panel,9,9);
        var screen=panel; screen.Inflate(-8,-8); TabletScreenRect=screen;
        bool dark=theme is "夜晚" or "深夜";
        dc.DrawRoundedRectangle(dark ? Brush(255,223,233,235) : Brush(255,244,250,249),null,screen,5,5);
        if (p<.999) return;
        dc.PushClip(new RectangleGeometry(screen,5,5));
        bool input=session.Interactive;
        void Button(string id,string label,Rect rect,bool enabled=true)
        {
            if (input && enabled) tabletButtons[id]=rect;
            dc.DrawRoundedRectangle(enabled ? Brush(255,221,237,239) : Brush(255,231,234,234),new Pen(Brush(255,184,209,213),.6),rect,5,5);
            Label(label,rect,13,enabled ? Ink : Brush(255,124,142,149));
        }
        if (session.Reading && session.Page == "messages") {
            Button("back","‹",new Rect(screen.Left+2,screen.Top+2,36,36));
            Label(FatherLetter.Title,new Rect(screen.Left+40,screen.Top+2,180,36),14,Ink);
            Button("close","×",new Rect(screen.Right-38,screen.Top+2,36,36));
            Text(dc,FatherLetter.Credit,screen.Left+12,screen.Top+44,12,Brush(255,104,131,145));
            var body=new FormattedText(FatherLetter.Pages[session.LetterPage],CultureInfo.GetCultureInfo("zh-CN"),FlowDirection.LeftToRight,new Typeface("Microsoft YaHei UI"),14,Ink,VisualTreeHelper.GetDpi(this).PixelsPerDip) { MaxTextWidth=screen.Width-24,LineHeight=21 };
            dc.DrawText(body,new Point(screen.Left+12,screen.Top+66));
            Button("prev","‹",new Rect(screen.Left+12,screen.Bottom-36,42,36),session.LetterPage>0);
            Label((session.LetterPage+1)+" / "+FatherLetter.Pages.Length,new Rect(screen.Left+58,screen.Bottom-36,screen.Width-116,36),12,Ink);
            Button("next","›",new Rect(screen.Right-54,screen.Bottom-36,42,36),session.LetterPage<FatherLetter.Pages.Length-1);
            dc.Pop(); if (input) session.BodyPresented(save,session.Generation); return;
        }
        Button("tab:create","创造",new Rect(screen.Left+2,screen.Top+2,78,36));
        Button("tab:messages","消息",new Rect(screen.Left+84,screen.Top+2,78,36));
        if (Story.LetterAvailable(save) && !Story.Current(save).FatherLetterRead) dc.DrawEllipse(Brush(255,106,153,172),null,new Point(screen.Left+150,screen.Top+10),2.5,2.5);
        Button("close","×",new Rect(screen.Right-38,screen.Top+2,36,36));
        dc.DrawLine(new Pen(Brush(255,175,205,213),1),new Point(screen.Left+(session.Page == "create" ? 15 : 97),screen.Top+39),new Point(screen.Left+(session.Page == "create" ? 69 : 151),screen.Top+39));
        if (session.Page == "messages") {
            if (!Story.LetterAvailable(save)) {
                Label(FatherLetter.Empty,new Rect(screen.Left+4,screen.Top+72,screen.Width-8,26),14,Ink);
                Label("2593天，没有消息。",new Rect(screen.Left+4,screen.Top+103,screen.Width-8,24),12,Brush(255,112,136,147));
            } else {
                var card=new Rect(screen.Left+8,screen.Top+58,screen.Width-16,58);
                if (input) tabletButtons["letter"]=card;
                dc.DrawRoundedRectangle(Brush(255,231,242,241),new Pen(Brush(255,177,203,210),.7),card,6,6);
                Text(dc,FatherLetter.Title,card.Left+12,card.Top+10,14,Ink);
                Text(dc,Story.Current(save).FatherLetterRead ? "已读 · 可以再次阅读" : "未读 · 点击阅读",card.Left+12,card.Top+34,12,Brush(255,104,131,145));
                if (!Story.Current(save).FatherLetterRead) dc.DrawEllipse(Brush(255,106,153,172),null,new Point(card.Right-13,card.Top+17),3,3);
            }
        } else {
            var options=session.Candidates.Skip(session.CandidatePage*4).Take(4).ToArray();
            for(int i=0;i<options.Length;i++) {
                var id=options[i]; var def=ObjectDef.Get(id);
                var rect=new Rect(screen.Left+4+i%2*128,screen.Top+44+i/2*40,124,36);
                if (input) tabletButtons["select:"+id]=rect;
                bool selected=session.Selected == id;
                dc.DrawRoundedRectangle(selected ? Brush(255,208,231,234) : Brush(255,232,242,242),new Pen(selected ? Brush(255,105,158,178) : Brush(255,184,208,214),selected ? 1.3 : .6),rect,5,5);
                var sprite=ObjectSprite(def); double k=Math.Min(25.0/sprite.Width,28.0/sprite.Height);
                dc.DrawImage(sprite.Image,new Rect(rect.Left+3+(25-sprite.Width*k)/2,rect.Top+4+(28-sprite.Height*k)/2,sprite.Width*k,sprite.Height*k));
                Text(dc,def.Name,rect.Left+32,rect.Top+9,12,Ink);
            }
            if (options.Length == 0) {
                Label("这里暂时没有可创造的景物。",new Rect(screen.Left+4,screen.Top+62,screen.Width-8,30),13,Ink);
                Label("也可以看看消息，或稍后再来。",new Rect(screen.Left+4,screen.Top+97,screen.Width-8,24),12,Brush(255,112,136,147));
            } else {
                string preview=session.Selected == null ? "选择一件景物，再确认创造。" : save.Layout.Any(o=>o.Id == session.Selected && !o.Hidden) ? "已在世界中 · 看看它。" : save.Layout.Any(o=>o.Id == session.Selected) ? "重新展示 · 保留原来的位置。" : "让这里多一点"+ObjectDef.Get(session.Selected).Name+"。";
                Text(dc,preview,screen.Left+8,screen.Top+125,12,Ink);
                Button("prev","‹",new Rect(screen.Left+4,screen.Bottom-36,36,36),session.CandidatePage>0);
                Button("next","›",new Rect(screen.Left+44,screen.Bottom-36,36,36),(session.CandidatePage+1)*4<session.Candidates.Length);
                Label((session.CandidatePage+1)+"/"+Math.Max(1,(session.Candidates.Length+3)/4),new Rect(screen.Left+82,screen.Bottom-36,55,36),12,Ink);
                Button("confirm",session.Selected != null && save.Layout.Any(o=>o.Id == session.Selected && !o.Hidden) ? "观察" : "创造",new Rect(screen.Right-110,screen.Bottom-36,106,36),session.Selected != null);
            }
        }
        dc.Pop();
        void Label(string label,Rect rect,double size,Brush ink)
        {
            var text=new FormattedText(label,CultureInfo.GetCultureInfo("zh-CN"),FlowDirection.LeftToRight,new Typeface("Microsoft YaHei UI"),size,ink,VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawText(text,new Point(rect.X+(rect.Width-text.Width)/2,rect.Y+(rect.Height-text.Height)/2));
        }
    }
    private void DrawTabletCreation(DrawingContext dc)
    {
        double t=behavior.Tablet.Elapsed;
        double amount=Math.Clamp(t/(1.4*.58),0,1);
        var center=new Point(behavior.X,behavior.Y-80);
        for(int i=0;i<6;i++) {
            double a=Time*1.2+i*Math.PI/3, r=12+amount*25;
            var p=new Point(center.X+Math.Cos(a)*r,center.Y+Math.Sin(a)*r*.6);
            dc.PushTransform(new RotateTransform(45,p.X,p.Y));
            dc.DrawRectangle(Brush((byte)(60+amount*95),166,229,238),new Pen(Brush(180,203,241,242),.7),new Rect(p.X-2,p.Y-2,4,4)); dc.Pop();
        }
    }
}
