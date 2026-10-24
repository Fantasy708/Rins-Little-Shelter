using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LittleShelter;

internal static class TabletVisualTests
{
    [STAThread] private static void Main(string[] args)
    {
        string output=Path.GetFullPath(args.Length>0 ? args[0] : "output/tablet-visual-qa"); Directory.CreateDirectory(output);
        int checks=0;
        void Check(bool c,string name) { if (!c) throw new Exception("FAIL: "+name); checks++; }
        var save=new SaveData(); save.Settings.FreeMode=true; save.Settings.Theme="白天"; save.Settings.Music=false; World.Initialize(save);
        var actor=new Behavior(12); using var audio=new AudioService();
        var scene=new Scene(save,actor,audio) { Width=480,Height=340 };
        scene.Measure(new Size(480,340)); scene.Arrange(new Rect(0,0,480,340));
        var manifest=JsonSerializer.Deserialize<ResourceManifest>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"assets","manifest.json")))!;
        double clock=0;
        BitmapSource Render(string? name=null,double dpi=1,bool white=false)
        {
            scene.Time=clock; scene.InvalidateVisual(); scene.UpdateLayout();
            // The fixed logical viewbox prevents WPF stretching the visual's
            // occupied bounds differently when the tablet opens or closes.
            var brush=new VisualBrush(scene) { ViewboxUnits=BrushMappingMode.Absolute,Viewbox=new Rect(0,0,480,340),ViewportUnits=BrushMappingMode.Absolute,Viewport=new Rect(0,0,480,340),Stretch=Stretch.Fill };
            var visual=new DrawingVisual(); using(var dc=visual.RenderOpen()) {
                if (white) dc.DrawRectangle(Brushes.White,null,new Rect(0,0,480,340));
                dc.DrawRectangle(brush,null,new Rect(0,0,480,340));
            }
            var bitmap=new RenderTargetBitmap((int)(480*dpi),(int)(340*dpi),96*dpi,96*dpi,PixelFormats.Pbgra32); bitmap.Render(visual); bitmap.Freeze();
            if (name!=null) SavePng(bitmap,Path.Combine(output,name+".png")); return bitmap;
        }
        void Step(double seconds) { while(seconds>1e-7) { double dt=Math.Min(.01,seconds); actor.Tick(dt,save,false); clock+=dt; seconds-=dt; } }
        void Until(string phase) { for(int i=0;i<20000 && actor.Tablet.Phase!=phase;i++) Step(.01); Check(actor.Tablet.Phase==phase,"到达平板阶段 "+phase); }
        void Reset(string id,double x=365,bool free=true)
        {
            actor.Tablet.Cancel(save); actor.Life.Cancel(); save.Settings.FreeMode=free;
            if (!Scenes.Switch(save,id)) throw new Exception("fixture scene unavailable");
            actor.X=x; actor.Y=Scenes.Home(save).Y; actor.Start("stand"); scene.ResetLifeVisuals(); Render(); Step(.3); Render();
        }
        Point Center(Rect r) => new(r.X+r.Width/2,r.Y+r.Height/2);
        byte Alpha(BitmapSource bitmap,Point p,double dpi) {
            byte[] pixel=new byte[4]; bitmap.CopyPixels(new Int32Rect((int)Math.Clamp(Math.Round(p.X*dpi),0,bitmap.PixelWidth-1),(int)Math.Clamp(Math.Round(p.Y*dpi),0,bitmap.PixelHeight-1),1,1),pixel,4,0); return pixel[3];
        }
        IEnumerable<GlyphRunDrawing> Glyphs(Drawing drawing)
        {
            if (drawing is GlyphRunDrawing glyph) yield return glyph;
            if (drawing is DrawingGroup group) foreach(var child in group.Children) foreach(var nested in Glyphs(child)) yield return nested;
        }
        void PanelChecks(BitmapSource bitmap,double dpi)
        {
            var panel=scene.TabletPanelRect; var screen=scene.TabletScreenRect;
            Check(!panel.IsEmpty && !screen.IsEmpty && panel.Contains(screen),"屏幕在平板边框内");
            Check(new Rect(0,0,480,340).Contains(panel),"平板在480×340场景内");
            var head=new Rect(actor.X-23,actor.Y-146,46,45); Check(!panel.IntersectsWith(head),"展开屏幕不盖Rin的脸");
            Check(Alpha(bitmap,Center(screen),dpi)>=240,"屏幕保持不透明");
            foreach(var button in scene.TabletButtons) {
                Check(screen.Contains(button.Value),"按钮完全位于屏幕内 "+button.Key);
                Check(button.Value.Width>=36 && button.Value.Height>=36,"按钮至少36×36 "+button.Key);
                Check(scene.Hit(Center(button.Value))=="tablet:"+button.Key,"按钮图形与命中对应 "+button.Key);
            }
            Check(scene.Hit(new Point(0,0))==null && Alpha(bitmap,new Point(0,0),dpi)==0,"平板外透明角落继续穿透");
            Check(scene.TabletGrips.Length==2 && !scene.TabletDeviceRect.IsEmpty,"专用完整姿势登记两手和设备");
            var gripArea=scene.TabletDeviceRect; gripArea.Inflate(4,3);
            foreach(var grip in scene.TabletGrips) Check(gripArea.Contains(grip),"手与物理平板握持对应");
        }
        foreach(string id in Scenes.All.Select(d=>d.Id)) foreach(double x in new[]{115.0,365.0}) {
            Reset(id,x); Check(actor.Tablet.Open(save),"各场景左右位置都能打开 "+id+"/"+x); Until("ready");
            foreach(double dpi in new[]{1.0,1.5,2.0}) {
                var bitmap=Render(id+"-create-"+x+"-"+dpi.ToString("0.0",CultureInfo.InvariantCulture),dpi); PanelChecks(bitmap,dpi);
            }
        }
        Reset("outdoor"); actor.Tablet.Open(save,preselect:"flower"); Until("ready"); Render("tablet-create",1,true);
        var previousOptions=scene.TabletButtons.Where(p=>p.Key.StartsWith("select:")).ToArray();
        Check(actor.Tablet.ChangePage(save,"messages"),"切换消息页"); scene.ClearTabletHits();
        foreach(var option in previousOptions) Check(scene.Hit(Center(option.Value))!="tablet:"+option.Key,"清理后过期选择命中失效");
        foreach(double dpi in new[]{1.0,1.5,2.0}) {
            var bitmap=Render("messages-unread-"+dpi.ToString("0.0",CultureInfo.InvariantCulture),dpi); PanelChecks(bitmap,dpi);
            Check(scene.TabletButtons.ContainsKey("letter") && !save.FreeStory.FatherLetterRead,"消息列表有信且保持未读");
            Check(!scene.TabletButtons.Keys.Any(k=>k.StartsWith("select:") || k=="confirm"),"消息页无旧创造选项");
        }
        Render("tablet-messages",1,true); Check(actor.Tablet.OpenLetter(save),"主动打开信件"); scene.ClearTabletHits();
        for(int page=0;page<FatherLetter.Pages.Length;page++) {
            foreach(double dpi in new[]{1.0,1.5,2.0}) {
                var bitmap=Render("letter-page-"+(page+1)+"-"+dpi.ToString("0.0",CultureInfo.InvariantCulture),dpi); PanelChecks(bitmap,dpi);
                var screen=scene.TabletScreenRect;
                var bodyArea=new Rect(screen.Left+12,screen.Top+65,screen.Width-24,screen.Height-102);
                var bodyGlyphs=Glyphs(VisualTreeHelper.GetDrawing(scene)).Where(g=>g.Bounds.Top>=bodyArea.Top-1 && g.Bounds.Top<bodyArea.Bottom).ToArray();
                Check(bodyGlyphs.Length>0,"正文实际绘制字形而非占位 "+page);
                Check(bodyGlyphs.All(g=>Math.Abs(g.GlyphRun.FontRenderingEmSize-14)<.01),"正文保持14逻辑像素 "+page);
                Check(bodyGlyphs.All(g=>g.Bounds.Left>=bodyArea.Left-1 && g.Bounds.Right<=bodyArea.Right+1 && g.Bounds.Bottom<=bodyArea.Bottom+1),"正文文字不越界或盖分页按钮 "+page);
                Check(scene.TabletButtons.ContainsKey("back") && scene.TabletButtons.ContainsKey("close"),"阅读页可返回与关闭");
                Check(scene.TabletButtons.ContainsKey("prev")== (page>0) && scene.TabletButtons.ContainsKey("next")== (page<FatherLetter.Pages.Length-1),"分页末端没有过期箭头命中");
            }
            Render(page==0 ? "tablet-letter" : "tablet-letter-page-"+(page+1),1,true);
            if (page<FatherLetter.Pages.Length-1) actor.Tablet.TurnPage(save,1);
        }
        Check(save.FreeStory.FatherLetterRead && !save.GrowthStory.FatherLetterRead,"正文呈现标记自由已读且不污染成长");
        actor.Tablet.BackToList(save); scene.ClearTabletHits(); Render("messages-read"); Check(!scene.TabletButtons.ContainsKey("back") && scene.TabletButtons.ContainsKey("letter"),"返回后正文操作命中移除");
        save.CompanionDates=Enumerable.Range(0,20).Select(i=>new DateTime(2026,1,1).AddDays(i).ToString("yyyy-MM-dd")).ToList();
        Reset("indoor",365,false); actor.Tablet.Open(save,page:"messages"); Until("ready");
        foreach(double dpi in new[]{1.0,1.5,2.0}) {
            var bitmap=Render("messages-empty-"+dpi.ToString("0.0",CultureInfo.InvariantCulture),dpi); PanelChecks(bitmap,dpi);
            Check(!scene.TabletButtons.ContainsKey("letter") && !actor.Tablet.OpenLetter(save),"成长空消息不提供占位来信");
            var empty=new FormattedText(FatherLetter.Empty,CultureInfo.GetCultureInfo("zh-CN"),FlowDirection.LeftToRight,new Typeface("Microsoft YaHei UI"),14,Brushes.Black,1);
            Check(empty.Width<=scene.TabletScreenRect.Width-8,"固定英文空消息完整容纳");
        }
        Render("tablet-empty",1,true);
        Reset("cloud"); foreach(var p in save.Layout) p.Hidden=true;
        actor.Tablet.Open(save); var strip=new List<(string Caption,BitmapSource Image)>(); var gif=new GifBitmapEncoder();
        for(int i=0;i<24;i++) {
            Step(.075); var bitmap=Render(); var white=Render(null,1,true);
            var meta=new BitmapMetadata("gif"); meta.SetQuery("/grctlext/Delay",(ushort)8); gif.Frames.Add(BitmapFrame.Create(white,null,meta,null));
            if (i is 0 or 4 or 9 or 13 or 17 or 23) strip.Add((actor.Tablet.Phase+" "+actor.Tablet.Elapsed.ToString("0.00",CultureInfo.InvariantCulture),white));
            Check(scene.Hit(new Point(0,0))==null,"展开过程透明空白不拦截");
            if (actor.Tablet.ScreenVisible && actor.Tablet.Expansion<.999) Check(scene.TabletButtons.Count==0,"展开完成前不注册操作按钮");
            Check(actor.X==365 && actor.Y==252,"专用持板过程脚部不整人平移");
            int frame=actor.Tablet.PoseFrame; double k=150.0/manifest.TabletHeights[frame];
            // Verify a central torso patch in the isolated scene, away from the
            // tablet panel, instead of claiming every edge pixel is opaque.
            var torso=new Point(actor.X,actor.Y-55); Check(Alpha(bitmap,torso,1)>=240,"完整身体核心保持不透明");
            byte[] data=new byte[bitmap.PixelWidth*bitmap.PixelHeight*4]; bitmap.CopyPixels(data,bitmap.PixelWidth*4,0);
            int ground=-1;
            for(int y=235;y<262;y++) for(int x=340;x<390;x++) if (data[(y*bitmap.PixelWidth+x)*4+3]>=240) ground=Math.Max(ground,y);
            if(ground<250 || ground>253)SavePng(bitmap,Path.Combine(output,"tablet-sole-failure-"+i.ToString("D2")+".png"));
            Check(ground>=250 && ground<=253,"专用姿势凉鞋落点保持平台高度，ground="+ground+" phase="+actor.Tablet.Phase+" elapsed="+actor.Tablet.Elapsed.ToString("F3",CultureInfo.InvariantCulture)+" transition="+scene.ActorTransitionActive+" progress="+scene.ActorTransitionProgress.ToString("F3",CultureInfo.InvariantCulture));
        }
        Until("ready"); Render(); var expired=scene.TabletButtons.ToArray(); actor.Tablet.Close(save);
        foreach(var button in expired) Check(scene.Hit(Center(button.Value))!="tablet:"+button.Key,"关闭立即使旧按钮命中失效");
        for(int i=0;i<19;i++) {
            Step(.075); var frame=Render(null,1,true); var meta=new BitmapMetadata("gif"); meta.SetQuery("/grctlext/Delay",(ushort)8); gif.Frames.Add(BitmapFrame.Create(frame,null,meta,null));
            if (i is 2 or 6 or 12 or 18) strip.Add((actor.Tablet.Phase+" "+actor.Tablet.Elapsed.ToString("0.00",CultureInfo.InvariantCulture),frame));
            Check(scene.TabletButtons.Count==0,"收回和收起过程中按钮不重新出现");
        }
        using(var f=File.Create(Path.Combine(output,"tablet-open-close.gif"))) gif.Save(f);
        var stripVisual=new DrawingVisual(); using(var dc=stripVisual.RenderOpen()) {
            dc.DrawRectangle(Brushes.White,null,new Rect(0,0,480*5,370*2));
            for(int i=0;i<strip.Count;i++) {
                double x=i%5*480,y=i/5*370; dc.DrawImage(strip[i].Image,new Rect(x,y+25,480,340));
                var label=new FormattedText(strip[i].Caption,CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface("Microsoft YaHei UI"),14,Brushes.Black,1); dc.DrawText(label,new Point(x+10,y+4));
            }
        }
        var stripBitmap=new RenderTargetBitmap(2400,740,96,96,PixelFormats.Pbgra32); stripBitmap.Render(stripVisual); SavePng(stripBitmap,Path.Combine(output,"tablet-open-close-strip.png"));
        Reset("outdoor"); actor.Tablet.Open(save); Until("opening"); Step(.2); Render(); double partial=actor.Tablet.Expansion; Rect partialRect=scene.TabletPanelRect;
        actor.Tablet.Close(save); Render(); Check((Center(scene.TabletPanelRect)-Center(partialRect)).Length<.001 && Math.Abs(actor.Tablet.Expansion-partial)<.001,"展开中取消从同一位置和尺寸连续收回");
        Step(.1); Render(); Check(actor.Tablet.Expansion<partial && scene.TabletButtons.Count==0,"半展开回收不跳为整屏"); actor.Tablet.Cancel(save);
        foreach(string interruption in new[]{"cancel","mode","scene","clear"}) {
            Reset("outdoor"); actor.Tablet.Open(save); Until("ready"); Render(); var oldButtons=scene.TabletButtons.ToArray();
            if (interruption=="cancel") actor.Tablet.Cancel(save);
            if (interruption=="mode") save.Settings.FreeMode=false;
            if (interruption=="scene") Scenes.Switch(save,"indoor");
            if (interruption=="clear") scene.ClearTabletHits();
            foreach(var button in oldButtons) Check(scene.Hit(Center(button.Value))!="tablet:"+button.Key,"过期上下文不保留平板按钮 "+interruption);
        }
        Console.WriteLine("All "+checks+" production WPF tablet checks passed.");
    }
    private static void SavePng(BitmapSource bitmap,string path) { using var f=File.Create(path); var encoder=new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); encoder.Save(f); }
}
