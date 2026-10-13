using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LittleShelter;

internal static class VisualTests
{
    [STAThread] private static void Main(string[] args)
    {
        var output = args.Length > 0 ? args[0] : Path.GetFullPath("output/visual-qa"); Directory.CreateDirectory(output);
        var save = new SaveData(); World.Initialize(save); save.Settings.FreeMode = true; save.Settings.Music = false;
        var behavior = new Behavior(2); using var audio = new AudioService();
        var scene = new Scene(save, behavior, audio) { Width = 480, Height = 340 };
        scene.Measure(new Size(480, 340)); scene.Arrange(new Rect(0, 0, 480, 340));
        BitmapSource Render(string file, double scale = 1)
        {
            scene.InvalidateVisual(); scene.UpdateLayout();
            // The first render starts the production 250 ms pose crossfade.
            // Sample after it settles, rather than capturing the outgoing pose.
            var warmup = new RenderTargetBitmap(480, 340, 96, 96, PixelFormats.Pbgra32); warmup.Render(scene);
            scene.Time += .3; scene.InvalidateVisual(); scene.UpdateLayout();
            var target = new RenderTargetBitmap((int)(480 * scale), (int)(340 * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32); target.Render(scene);
            using var f = File.Create(Path.Combine(output, file)); var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(target)); png.Save(f); return target;
        }
        save.Settings.Theme = "白天";
        behavior.Start("stand"); scene.Time = 2; var first = Render("world-day.png");
        var onPaper = new DrawingVisual(); using (var drawing = onPaper.RenderOpen()) { drawing.DrawRectangle(Brushes.White, null, new Rect(0,0,480,340)); drawing.DrawImage(first, new Rect(0,0,480,340)); }
        var paper = new RenderTargetBitmap(480,340,96,96,PixelFormats.Pbgra32); paper.Render(onPaper);
        using (var f=File.Create(Path.Combine(output,"world-on-white.png"))) { var png=new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(paper)); png.Save(f); }
        foreach (var theme in new[] { "清晨", "傍晚", "夜晚", "深夜" }) { save.Settings.Theme = theme; Render("world-" + theme + ".png"); }
        save.Settings.Theme = "白天";
        foreach (var weather in new[] { "雨", "雪", "风" }) { save.Settings.Weather = weather; Render("weather-" + weather + ".png"); }
        save.Settings.Weather = "晴";
        for (int i = 0; i < Actions.All.Length; i++)
        {
            behavior.X = 240; behavior.Y = 252; behavior.Start(Actions.All[i].Id, 280, 252, "flower");
            double sample=Math.Min(1.2,behavior.ActiveDuration*.5); for (int j = 0; j < 12; j++) behavior.Tick(sample/12, save, false);
            scene.Time += 4; Render("action-" + Actions.All[i].Id + ".png");
        }
        behavior.Start("stand"); behavior.X = 240; scene.Time += 3;
        foreach (double scale in new[] { 1.0, 1.5, 2.0 }) Render("dpi-" + scale.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + ".png", scale);
        if (scene.Hit(new Point(0, 0)) != null) throw new Exception("透明角落应穿透");
        var bitmap = Render("hit-map.png");
        byte[] pixels = new byte[480 * 340 * 4]; bitmap.CopyPixels(pixels, 480 * 4, 0);
        int solid = 0, rinHits = 0;
        for (int y = 0; y < 340; y += 2) for (int x = 0; x < 480; x += 2) { if (pixels[(y * 480 + x) * 4 + 3] > 48) solid++; if (scene.Hit(new Point(x, y)) == "rin") rinHits++; }
        if (solid < 500 || rinHits < 100) throw new Exception("图集未渲染或角色无法命中");
        CheckSeatedSwing(output);
        CheckBenchSeat(output);
        CheckLocomotion(output);
        CheckMotionClips(output);
        CheckMotionOpacity();
        CheckMotionTransitions();
        Console.WriteLine("PASS: 35个动作目录项、5种天空、3种天气、3种DPI渲染；角色像素命中，空白穿透。彩蛋完整时间轴另由NurtureVisualTests检查。输出：" + output);
    }
    private static void CheckSeatedSwing(string output)
    {
        var assets = Path.Combine(AppContext.BaseDirectory, "assets");
        var manifest = JsonSerializer.Deserialize<ResourceManifest>(File.ReadAllText(Path.Combine(assets, "manifest.json")))!;
        var atlas = new Atlas(Path.Combine(assets, manifest.Poses), manifest.PoseRegions);
        double spriteScale = 150.0 / atlas.Sprites[0].Height;
        var seated = atlas.Sprites[7];
        int cases = 0;
        foreach (string action in new[] { "swing", "benchSit" })
        foreach (bool mirrored in new[] { false, true })
        foreach (double dpi in new[] { 1.0, 1.5, 2.0 })
        {
            var save = new SaveData(); save.Settings.FreeMode = true; save.Settings.Music = false; save.Settings.Theme = "白天";
            var behavior = new Behavior(1); behavior.Start(action, mirrored ? 200 : 280);
            using var audio = new AudioService();
            var scene = new Scene(save, behavior, audio) { Width = 480, Height = 340 };
            scene.Measure(new Size(480,340)); scene.Arrange(new Rect(0,0,480,340));
            var actor = new Rect(240 - seated.Width * spriteScale / 2, 252 + (action == "swing" ? 45 : 0) - seated.Height * spriteScale, seated.Width * spriteScale, seated.Height * spriteScale);
            BitmapSource Frame(double time)
            {
                scene.Time = time; scene.InvalidateVisual(); scene.UpdateLayout();
                var frame = new RenderTargetBitmap((int)(480*dpi),(int)(340*dpi),96*dpi,96*dpi,PixelFormats.Pbgra32); frame.Render(scene); return frame;
            }
            Frame(0); // Start and settle the normal pose transition.
            var positive = Frame((Math.PI/2 + Math.PI*2)/1.8);
            var negative = Frame((Math.PI*3/2 + Math.PI*2)/1.8);
            double Shift(double fraction) => (RowCenter(positive,actor,fraction,dpi) - RowCenter(negative,actor,fraction,dpi))/dpi;
            double upperLeg = Math.Abs(Shift(.73)), feet = Math.Abs(Shift(.94));
            if (upperLeg < .8 || feet < upperLeg + 1) throw new Exception($"Entire seated leg must move: {action}/{mirrored}/{dpi}, upper={upperLeg:F2}, feet={feet:F2}");
            if (Math.Abs(Shift(.84)-Shift(.80)) > 1.2) throw new Exception($"Seated leg has a displacement step through the calf: {action}/{mirrored}/{dpi}");
            if (Math.Abs(Shift(.50)) > .5) throw new Exception("Seated body should remain anchored while the legs sway.");
            byte[] pixels = new byte[negative.PixelWidth*negative.PixelHeight*4]; negative.CopyPixels(pixels,negative.PixelWidth*4,0);
            int hits = 0, misses = 0;
            for (int y=(int)((actor.Y+actor.Height*.6)*dpi); y<(int)(actor.Bottom*dpi); y+=3)
            for (int x=(int)((actor.X-5)*dpi); x<(int)((actor.Right+5)*dpi); x+=3)
            {
                if (pixels[(y*negative.PixelWidth+x)*4+3] < 245) continue;
                if (scene.Hit(new Point((x+.5)/dpi,(y+.5)/dpi)) == "rin") hits++; else misses++;
            }
            if (hits < 25 || misses > 2) throw new Exception($"Animated legs must remain clickable: hits={hits}, misses={misses}");
            if (action == "swing" && !mirrored && dpi == 2)
            {
                var preview = new DrawingVisual();
                using (var drawing = preview.RenderOpen())
                {
                    drawing.DrawRectangle(Brushes.White,null,new Rect(0,0,720,720));
                    for (int i=0;i<6;i++)
                    {
                        var frame = Frame((Math.PI*2+i*Math.PI/3)/1.8);
                        SavePng(frame,Path.Combine(output,$"seated-swing-phase-{i}.png"));
                        double x=(i%3)*240, y=(i/3)*360;
                        drawing.PushClip(new RectangleGeometry(new Rect(x,y,240,360)));
                        drawing.DrawImage(frame,new Rect(x+120-240*2.4,y+30-actor.Y*2.4,480*2.4,340*2.4)); drawing.Pop();
                    }
                }
                var paper = new RenderTargetBitmap(720,720,96,96,PixelFormats.Pbgra32); paper.Render(preview);
                SavePng(paper,Path.Combine(output,"seated-swing-preview.png"));
            }
            cases++;
        }
        Console.WriteLine($"PASS: {cases} seated cases, complete leg motion, calf continuity, anchored torso and animated hit testing.");
    }
    private static void CheckBenchSeat(string output)
    {
        var assets=Path.Combine(AppContext.BaseDirectory,"assets");
        var manifest=JsonSerializer.Deserialize<ResourceManifest>(File.ReadAllText(Path.Combine(assets,"manifest.json")))!;
        var poses=new Atlas(Path.Combine(assets,manifest.Poses),manifest.PoseRegions);
        var world=new Atlas(Path.Combine(assets,manifest.World),manifest.WorldRegions);
        var seated=poses.Sprites[7]; var benchSprite=world.Sprites[ObjectDef.Get("bench").Tile];
        double scale=150.0/poses.Sprites[0].Height;
        double k=Math.Min(110.0/benchSprite.Width,68.0/benchSprite.Height);
        int cases=0;
        foreach(bool free in new[]{false,true})
        foreach(bool mirror in new[]{false,true})
        foreach(double dpi in new[]{1.0,1.5,2.0})
        {
            var save=new SaveData(); save.Settings.FreeMode=free; save.Settings.Music=false; save.Settings.Theme="白天";
            save.CompanionDates.AddRange(Enumerable.Range(1,7).Select(d=>$"2026-09-{d:00}")); World.Initialize(save);
            Scenes.Switch(save,"outdoor");
            save.Layout.Clear(); var bench=World.Default(ObjectDef.Get("bench")); save.Layout.Add(bench);
            // Deliberately move the chair away from its default location.
            bench.X=mirror ? 205 : 325; bench.Y=mirror ? 263 : 246;
            var behavior=new Behavior(1){ X=bench.X, Y=bench.Y }; behavior.Start("benchSit",mirror ? 120 : 400);
            using var audio=new AudioService(); var scene=new Scene(save,behavior,audio){Width=480,Height=340};
            scene.Measure(new Size(480,340)); scene.Arrange(new Rect(0,0,480,340));
            BitmapSource Frame(double time)
            {
                scene.Time=time; scene.InvalidateVisual(); scene.UpdateLayout();
                var image=new RenderTargetBitmap((int)(480*dpi),(int)(340*dpi),96*dpi,96*dpi,PixelFormats.Pbgra32); image.Render(scene); return image;
            }
            Frame(0); var image=Frame(4);
            double seatY=bench.Y-benchSprite.Height*k*.44;
            // Compare a production-rendered torso scanline to the source seated
            // sprite: this catches registration to the chair feet or backrest.
            byte[] pixels=new byte[image.PixelWidth*image.PixelHeight*4]; image.CopyPixels(pixels,image.PixelWidth*4,0);
            int top=image.PixelHeight;
            for(int y=0;y<image.PixelHeight;y++) for(int x=(int)((bench.X-15)*dpi);x<(int)((bench.X+15)*dpi);x++)
                if(pixels[(y*image.PixelWidth+x)*4+3]>220 && scene.Hit(new Point((x+.5)/dpi,(y+.5)/dpi))=="rin") top=Math.Min(top,y);
            double expectedTop=seatY-seated.Height*scale*.625;
            if(Math.Abs(top/dpi-expectedTop)>2) throw new Exception($"Seated hips miss the wooden seat: free={free}, mirror={mirror}, dpi={dpi}");
            if(scene.Hit(new Point(bench.X,seatY-12))!="rin" || scene.Hit(new Point(0,0))!=null) throw new Exception("Seated actor hit region did not follow the corrected seat placement.");
            // Moving both attachment roots must preserve the exact registration.
            bench.X-=28; bench.Y+=9; behavior.X-=28; behavior.Y+=9; var moved=Frame(4);
            byte[] movedPixels=new byte[moved.PixelWidth*moved.PixelHeight*4]; moved.CopyPixels(movedPixels,moved.PixelWidth*4,0);
            int movedTop=moved.PixelHeight;
            for(int y=0;y<moved.PixelHeight;y++) for(int x=(int)((bench.X-15)*dpi);x<(int)((bench.X+15)*dpi);x++)
                if(movedPixels[(y*moved.PixelWidth+x)*4+3]>220 && scene.Hit(new Point((x+.5)/dpi,(y+.5)/dpi))=="rin") movedTop=Math.Min(movedTop,y);
            if(Math.Abs((movedTop-top)/dpi-9)>1) throw new Exception("Moved bench detached from the seated actor.");
            cases++;
        }
        var complete=new SaveData(); complete.Settings.FreeMode=true; complete.Settings.Music=false; complete.Settings.Theme="白天"; World.Initialize(complete);
        var actor=new Behavior(1){X=270,Y=252}; actor.Start("benchSit"); using var quiet=new AudioService();
        var whole=new Scene(complete,actor,quiet){Width=480,Height=340}; whole.Measure(new Size(480,340)); whole.Arrange(new Rect(0,0,480,340));
        whole.Time=4; whole.InvalidateVisual(); whole.UpdateLayout(); var full=new RenderTargetBitmap(960,680,192,192,PixelFormats.Pbgra32); full.Render(whole);
        SavePng(full,Path.Combine(output,"bench-seat-corrected.png"));
        Console.WriteLine($"PASS: {cases} bench contact cases, wooden-seat registration, moved chair, both modes, both directions, three DPI scales and seated alpha hit regions.");
    }
    private static double RowCenter(BitmapSource bitmap, Rect actor, double fraction, double dpi)
    {
        var pixels=new byte[bitmap.PixelWidth*bitmap.PixelHeight*4]; bitmap.CopyPixels(pixels,bitmap.PixelWidth*4,0);
        int y=(int)Math.Round((actor.Y+actor.Height*fraction)*dpi); double weight=0, sum=0;
        for (int x=(int)((actor.X-6)*dpi); x<(int)((actor.Right+6)*dpi); x++)
        {
            int alpha=pixels[(y*bitmap.PixelWidth+x)*4+3]; if (alpha<160) continue;
            weight+=alpha; sum+=(x+.5)*alpha;
        }
        if (weight==0) throw new Exception("Seated leg scanline is empty.");
        return sum/weight;
    }
    private static void SavePng(BitmapSource bitmap, string path)
    {
        using var file=File.Create(path); var encoder=new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); encoder.Save(file);
    }
    private static void CheckLocomotion(string output)
    {
        var save=new SaveData(); save.Settings.FreeMode=true; save.Settings.Music=false; save.Settings.Theme="白天";
        var behavior=new Behavior(1) { X=120 }; using var audio=new AudioService();
        var scene=new Scene(save,behavior,audio) { Width=480,Height=340 };
        scene.Measure(new Size(480,340)); scene.Arrange(new Rect(0,0,480,340));
        double elapsed=0;
        BitmapSource Frame(bool align=true)
        {
            scene.Time=elapsed; scene.InvalidateVisual(); scene.UpdateLayout();
            var raw=new RenderTargetBitmap(480,340,96,96,PixelFormats.Pbgra32); raw.Render(scene);
            if (!align) return raw;
            var visual=new DrawingVisual(); using (var dc=visual.RenderOpen()) dc.DrawImage(raw,new Rect(240-behavior.X,0,480,340));
            var aligned=new RenderTargetBitmap(480,340,96,96,PixelFormats.Pbgra32); aligned.Render(visual); return aligned;
        }
        void AdvanceTo(double phase)
        {
            while (behavior.StepPhase < phase) { behavior.Tick(.005,save,false); elapsed+=.005; }
        }
        int FeetY(BitmapSource image)
        {
            byte[] pixels=new byte[480*340*4]; image.CopyPixels(pixels,480*4,0);
            for(int y=270;y>=190;y--) for(int x=190;x<290;x++) if(pixels[(y*480+x)*4+3]>220) return y;
            throw new Exception("Motion frame has no visible feet.");
        }
        var root=Path.Combine(AppContext.BaseDirectory,"assets");
        var manifest=JsonSerializer.Deserialize<ResourceManifest>(File.ReadAllText(Path.Combine(root,"manifest.json")))!;
        var atlas=new Atlas(Path.Combine(root,manifest.Trot),manifest.TrotRegions);
        if (atlas.Sprites.Length!=8 || atlas.Sprites.Any(s=>s.CropTop<15 || s.CropTop+s.Height>s.CellHeight-5 || s.CropLeft<5 || s.CropLeft+s.Width>s.CellWidth-5)) throw new Exception("Jog atlas contains clipped or cross-cell sprites.");
        behavior.Start("trot",350,252);
        var contact=Frame(); AdvanceTo(.25); var flight=Frame();
        SavePng(contact,Path.Combine(output,"trot-contact.png")); SavePng(flight,Path.Combine(output,"trot-flight.png"));
        AdvanceTo(.5); var oppositeContact=Frame(); if(Math.Abs(FeetY(oppositeContact)-FeetY(contact))>3) throw new Exception("Both jog contact phases must share the ground line.");
        if (FeetY(flight) > FeetY(contact)-4) throw new Exception("Jogging needs a distinct airborne phase, with feet lifted above the shared ground line.");
        var preview=new DrawingVisual();
        using(var drawing=preview.RenderOpen())
        {
            drawing.DrawRectangle(Brushes.White,null,new Rect(0,0,1120,720));
            behavior.X=120; behavior.Start("trot",350,252);
            for(int i=0;i<8;i++)
            {
                AdvanceTo(i/8.0); var image=Frame(); SavePng(image,Path.Combine(output,$"trot-phase-{i}.png"));
                double x=(i%4)*280, y=(i/4)*360; drawing.PushClip(new RectangleGeometry(new Rect(x,y,280,360)));
                drawing.DrawImage(image,new Rect(x+140-240*1.8,y+35-90*1.8,480*1.8,340*1.8)); drawing.Pop();
            }
        }
        var sheet=new RenderTargetBitmap(1120,720,96,96,PixelFormats.Pbgra32); sheet.Render(preview); SavePng(sheet,Path.Combine(output,"trot-preview.png"));
        World.Initialize(save); behavior.X=120; behavior.Visit("benchSit",270,252);
        int images=0; bool sawWalking=false, sawSettling=false, sawSitting=false;
        double nextCapture=0;
        while (!sawSitting)
        {
            behavior.Tick(.05,save,false); elapsed+=.05;
            sawWalking |= behavior.IsLocomotion;
            sawSettling |= behavior.Settling;
            sawSitting |= behavior.Current.Id == "benchSit";
            if (elapsed>=nextCapture) { SavePng(Frame(false),Path.Combine(output,$"bench-return-{images++:00}.png")); nextCapture=elapsed+.25; }
            if (images>30) throw new Exception("Bench approach did not finish.");
        }
        Frame(false); // Register the new seated image before sampling its crossfade.
        for(int i=0;i<6;i++) { behavior.Tick(.05,save,false); elapsed+=.05; } SavePng(Frame(false),Path.Combine(output,"bench-return-seated.png"));
        if (!sawWalking || !sawSettling || Math.Abs(behavior.X-270)>.001) throw new Exception("Bench approach must walk, settle, then sit at the destination.");
        Console.WriteLine("PASS: distinct jog flight and grounded registration; walk-to-bench, deceleration, standing settle and sit sequence rendered.");
    }
    private static void CheckMotionOpacity()
    {
        int cases=0;
        foreach(string id in new[]{"hop","turn"})
        foreach(bool mirror in new[]{false,true})
        foreach(double dpi in new[]{1.0,1.5,2.0})
        {
            var save=new SaveData(); save.Settings.FreeMode=true; save.Settings.Music=false;
            var behavior=new Behavior(1); if(mirror) behavior.Start("walk",120,252); behavior.Start(id);
            using var audio=new AudioService(); var scene=new Scene(save,behavior,audio){Width=480,Height=340};
            scene.Measure(new Size(480,340)); scene.Arrange(new Rect(0,0,480,340));
            double elapsed=0;
            BitmapSource At(double seconds)
            {
                while(elapsed<seconds-.000001) { double dt=Math.Min(.01,seconds-elapsed); behavior.Tick(dt,save,false); elapsed+=dt; }
                scene.Time=elapsed; scene.InvalidateVisual(); scene.UpdateLayout();
                var bitmap=new RenderTargetBitmap((int)(480*dpi),(int)(340*dpi),96*dpi,96*dpi,PixelFormats.Pbgra32); bitmap.Render(scene); return bitmap;
            }
            At(0); var clip=MotionClips.Find(id)!;
            foreach(int pair in new[]{1,4,6,9})
            {
                var bitmap=At((clip.Keys[pair]+clip.Keys[pair+1])*.5*clip.Duration);
                var pixels=new byte[bitmap.PixelWidth*bitmap.PixelHeight*4]; bitmap.CopyPixels(pixels,bitmap.PixelWidth*4,0);
                int opaque=0;
                for(int y=0;y<bitmap.PixelHeight;y++) for(int x=0;x<bitmap.PixelWidth;x++)
                    if(pixels[(y*bitmap.PixelWidth+x)*4+3]>=250 && scene.Hit(new Point((x+.5)/dpi,(y+.5)/dpi))=="rin") opaque++;
                if(opaque<300*dpi*dpi) throw new Exception($"Pose crossfade makes Rin translucent: {id}/{pair}/{mirror}/{dpi}, opaque pixels={opaque}");
            }
            cases++;
        }
        Console.WriteLine($"PASS: {cases} motion opacity cases, 48 mid-interval samples retain opaque body coverage rather than flashing through the desktop.");
    }
    private static void CheckMotionTransitions()
    {
        int cases=0;
        foreach(string id in new[]{"hop","turn"})
        foreach(bool mirror in new[]{false,true})
        foreach(double dpi in new[]{1.0,1.5,2.0})
        {
            var save=new SaveData(); save.Settings.FreeMode=true; save.Settings.Music=false;
            var behavior=new Behavior(1); if(mirror) behavior.Start("walk",120,252); behavior.Start("stand");
            using var audio=new AudioService(); var scene=new Scene(save,behavior,audio){Width=480,Height=340};
            scene.Measure(new Size(480,340)); scene.Arrange(new Rect(0,0,480,340));
            double elapsed=0;
            BitmapSource Frame(double dt=0)
            {
                if(dt>0) { behavior.Tick(dt,save,false); elapsed+=dt; }
                scene.Time=elapsed; scene.InvalidateVisual(); scene.UpdateLayout();
                var bitmap=new RenderTargetBitmap((int)(480*dpi),(int)(340*dpi),96*dpi,96*dpi,PixelFormats.Pbgra32); bitmap.Render(scene); return bitmap;
            }
            void Opaque(BitmapSource bitmap)
            {
                var pixels=new byte[bitmap.PixelWidth*bitmap.PixelHeight*4]; bitmap.CopyPixels(pixels,bitmap.PixelWidth*4,0);
                int count=0; for(int y=0;y<bitmap.PixelHeight;y++) for(int x=0;x<bitmap.PixelWidth;x++)
                    if(pixels[(y*bitmap.PixelWidth+x)*4+3]>=250 && scene.Hit(new Point((x+.5)/dpi,(y+.5)/dpi))=="rin") count++;
                if(count<200*dpi*dpi) throw new Exception($"Action entry/exit loses opaque body coverage: {id}/{mirror}/{dpi}");
            }
            Frame(); behavior.Start(id); Frame(); Opaque(Frame(.04));
            for(int i=0;i<15;i++) Frame(.02);
            var before=ActorBounds(Frame(),dpi);
            // Restart midway between two authored keys. The first entry frame
            // must retain the actual displayed pose at its current position.
            behavior.Start(id); var after=ActorBounds(Frame(),dpi);
            if(Math.Abs(before.X-after.X)>2 || Math.Abs(before.Y-after.Y)>2 || Math.Abs(before.Width-after.Width)>2 || Math.Abs(before.Height-after.Height)>2)
                throw new Exception("Restarted clip replaced its displayed snapshot with a dominant key pose.");
            Opaque(Frame(.04)); behavior.Start("smile"); Frame(); Opaque(Frame(.04));
            behavior.Start("stand"); Frame(); Opaque(Frame(.125));
            if(scene.Hit(new Point(0,0))!=null) throw new Exception("Action composite obstructs a transparent desktop corner.");
            cases++;
        }
        Console.WriteLine($"PASS: {cases} action-entry, repeated-clip, interruption and nested-exit opacity cases; actual displayed snapshots and composite alpha hit regions.");
    }
    private static Rect ActorBounds(BitmapSource bitmap, double dpi)
    {
        var pixels=new byte[bitmap.PixelWidth*bitmap.PixelHeight*4]; bitmap.CopyPixels(pixels,bitmap.PixelWidth*4,0);
        int l=bitmap.PixelWidth, t=bitmap.PixelHeight, r=0, b=0;
        for(int y=0;y<bitmap.PixelHeight;y++) for(int x=0;x<bitmap.PixelWidth;x++)
        {
            if(pixels[(y*bitmap.PixelWidth+x)*4+3]<160) continue;
            l=Math.Min(l,x); t=Math.Min(t,y); r=Math.Max(r,x); b=Math.Max(b,y);
        }
        if(l>=r || t>=b) throw new Exception("Motion actor is empty.");
        return new Rect(l/dpi,t/dpi,(r-l+1)/dpi,(b-t+1)/dpi);
    }
    private static void CheckMotionClips(string output)
    {
        int cases=0;
        foreach(string id in new[]{"hop","turn"})
        foreach(bool mirror in new[]{false,true})
        foreach(double dpi in new[]{1.0,1.5,2.0})
        {
            var save=new SaveData(); save.Settings.FreeMode=true; save.Settings.Music=false; save.Settings.Theme="白天";
            var behavior=new Behavior(1);
            if(mirror) behavior.Start("walk",200,252);
            behavior.Start(id,mirror ? 120 : 360,252);
            using var audio=new AudioService();
            var scene=new Scene(save,behavior,audio){Width=480,Height=340}; scene.Measure(new Size(480,340)); scene.Arrange(new Rect(0,0,480,340));
            var clip=MotionClips.Find(id)!; double elapsed=0;
            BitmapSource At(double seconds)
            {
                while(elapsed<seconds-.000001) { double dt=Math.Min(.01,seconds-elapsed); behavior.Tick(dt,save,false); elapsed+=dt; }
                scene.Time=elapsed; scene.InvalidateVisual(); scene.UpdateLayout();
                var bitmap=new RenderTargetBitmap((int)(480*dpi),(int)(340*dpi),96*dpi,96*dpi,PixelFormats.Pbgra32); bitmap.Render(scene); return bitmap;
            }
            var frames=new BitmapSource[12]; var bounds=new Rect[12]; var roots=new double[12];
            for(int frame=0;frame<12;frame++)
            {
                double time=Math.Min(clip.Keys[frame]*clip.Duration,clip.Duration-.002);
                frames[frame]=At(time); bounds[frame]=ActorBounds(frames[frame],dpi);
                roots[frame]=behavior.X;
                bool grounded=id=="turn" || frame<4 || frame>=8;
                if(grounded && Math.Abs(bounds[frame].Bottom-252)>2) throw new Exception($"Unstable contact foot: {id}/{frame}/{mirror}/{dpi}");
                if(id=="hop" && frame==6 && bounds[frame].Bottom>210) throw new Exception("Hop apex must have both feet airborne.");
                // Inspect the same production alpha mapping that drives window
                // click-through, including reversed turn art and blended poses.
                byte[] pixels=new byte[frames[frame].PixelWidth*frames[frame].PixelHeight*4]; frames[frame].CopyPixels(pixels,frames[frame].PixelWidth*4,0);
                int hits=0, misses=0;
                for(int y=0;y<frames[frame].PixelHeight;y+=4) for(int x=0;x<frames[frame].PixelWidth;x+=4)
                {
                    if(pixels[(y*frames[frame].PixelWidth+x)*4+3]<220) continue;
                    if(scene.Hit(new Point((x+.5)/dpi,(y+.5)/dpi))=="rin") hits++; else misses++;
                }
                if(hits<70 || misses>3) throw new Exception($"Moving silhouette is not clickable: {id}/{frame}/{mirror}/{dpi}, {hits}/{misses}");
            }
            if(id=="hop" && (bounds[2].Height>bounds[0].Height-15 || bounds[6].Y>bounds[0].Y-5)) throw new Exception("Hop needs real crouching joints and an elevated body, not a translated standing cutout.");
            if(id=="turn" && bounds.Min(b=>b.Width)<bounds.Max(b=>b.Width)*.62) throw new Exception("Turn must retain body volume rather than horizontally squash the image.");
            if(!mirror && dpi==1)
            {
                var preview=new DrawingVisual();
                using(var dc=preview.RenderOpen())
                {
                    dc.DrawRectangle(Brushes.White,null,new Rect(0,0,960,750));
                    for(int i=0;i<12;i++)
                    {
                        SavePng(frames[i],Path.Combine(output,$"{id}-key-{i:00}.png"));
                        double x=i%4*240,y=i/4*250;
                        dc.PushClip(new RectangleGeometry(new Rect(x,y,240,250)));
                        dc.DrawImage(frames[i],new Rect(x+120-roots[i]*1.3,y+8-70*1.3,480*1.3,340*1.3)); dc.Pop();
                    }
                }
                var sheet=new RenderTargetBitmap(960,750,96,96,PixelFormats.Pbgra32); sheet.Render(preview); SavePng(sheet,Path.Combine(output,id+"-preview.png"));
            }
            // Intermediate samples exercise two different visible sprite frames.
            behavior.Start(id,mirror ? 120 : 360,252); elapsed=0; scene.Time=0;
            At(0);
            foreach(double phase in new[]{.255,.445,.59,.775})
            {
                var bitmap=At(phase*clip.Duration);
                if(scene.Hit(new Point(0,0))!=null || ActorBounds(bitmap,dpi).Width<35) throw new Exception("Intermediate animation lost its silhouette or transparent corner.");
            }
            cases++;
        }
        var assets=Path.Combine(AppContext.BaseDirectory,"assets"); var manifest=JsonSerializer.Deserialize<ResourceManifest>(File.ReadAllText(Path.Combine(assets,"manifest.json")))!;
        foreach(var spec in new[]{(manifest.Hop,manifest.HopRegions),(manifest.Turn,manifest.TurnRegions)})
        {
            var atlas=new Atlas(Path.Combine(assets,spec.Item1),spec.Item2);
            if(atlas.Sprites.Length!=12 || atlas.Sprites.Any(s=>s.CropTop<4 || s.CropTop+s.Height>s.CellHeight-4 || s.CropLeft<4 || s.CropLeft+s.Width>s.CellWidth-4)) throw new Exception("Hop/turn atlas has a clipped character or crosses its cell boundary.");
            int unique=atlas.Sprites.Select(s=>Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(s.Pixels))).Distinct().Count();
            if(unique!=12) throw new Exception("Hop/turn frames must be individually drawn poses.");
        }
        foreach(string id in new[]{"hop","turn"})
        {
            var save=new SaveData(); World.Initialize(save); save.Settings.Music=false; save.Settings.Theme="白天";
            var behavior=new Behavior(1); behavior.Start(id); using var audio=new AudioService();
            var scene=new Scene(save,behavior,audio){Width=480,Height=340}; scene.Measure(new Size(480,340)); scene.Arrange(new Rect(0,0,480,340));
            int count=(int)Math.Ceiling((MotionClips.Find(id)!.Duration+.32)/.02);
            for(int i=0;i<=count;i++)
            {
                scene.Time=i*.02; scene.InvalidateVisual(); scene.UpdateLayout();
                var image=new RenderTargetBitmap(480,340,96,96,PixelFormats.Pbgra32); image.Render(scene);
                SavePng(image,Path.Combine(output,$"{id}-motion-{i:000}.png")); behavior.Tick(.02,save,false);
            }
        }
        Console.WriteLine($"PASS: {cases} hop/turn cases (144 key poses), crouch/flight/landing, unsquashed turning, both directions, 3 DPI scales, animated hit regions and full sequence exports.");
    }
}





