using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LittleShelter;

// Render the actual production actor, not a test reconstruction of the atlas.
// Continuous traces retain transition history. A separate cold-key sweep removes
// that history to measure the complete opaque source poses independently.
internal static class MotionExitVisualTests
{
    private static readonly MethodInfo DrawActor=typeof(Scene).GetMethod("DrawPresentRin",BindingFlags.Instance|BindingFlags.NonPublic)!;
    private static readonly List<Result> Results=new();
    private static readonly List<string> Failures=new();
    private static string output="",filter="";
    private static int checks,renders;

    [STAThread] private static int Main(string[] args)
    {
        output=Path.GetFullPath(args.Length>0 ? args[0] : "output/motion-exit/visual");
        filter=args.Length>1 ? args[1] : "";
        Directory.CreateDirectory(output);
        var app=new Application {ShutdownMode=ShutdownMode.OnExplicitShutdown};
        PetWindow? pet=null;
        try {
            var save=new SaveData(); save.Settings.FreeMode=true; save.Settings.Music=false; save.Settings.Sound=false;
            save.Settings.Theme="白天"; save.Settings.Weather="晴"; save.Settings.StartupAnimationEnabled=false;
            save.Settings.Left=save.Settings.Top=0; save.HasSeenStartup=true;
            pet=new PetWindow(new Storage(Path.Combine(output,"isolated-save")),save,true);
            app.MainWindow=pet; pet.Show(); pet.SkipStartup();
            foreach(string id in new[]{"trot","hop","turn"})
            foreach(bool left in new[]{false,true})
            foreach(double dpi in new[]{1.0,1.5,2.0}) {
                foreach(int fps in new[]{30,60})
                foreach(string entry in new[]{"natural","stand","tablet","click"})
                    RunCase(new Fixture(pet,left,dpi),id,entry,fps,false);
                RunCase(new Fixture(pet,left,dpi),id,"cold-natural",60,true);
                if(id=="trot") RunCase(new Fixture(pet,left,dpi),id,"cold-brake",60,true);
            }
        }
        catch(Exception ex) { Failures.Add(Unwrap(ex)); }
        finally { pet?.Close(); }
        File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(new {
            Passed=Failures.Count==0,Checks=checks,Renders=renders,Cases=Results.Count,
            Scope="Production PetWindow/Behavior/Scene.DrawPresentRin. Natural, stand, tablet and click exits; both directions, 30/60 supplied frame clocks and three offscreen densities. Continuous transparent actor frames plus cold opaque-key measurements. Not native input, system DPI, multi-monitor or stability acceptance.",
            Detector="Independent BGRA measurements: scalp/facial-skin lobe, opaque silhouette and sandal bottom, white-blouse box, lower-leg and outer-hand skin masks. No manifest height, anchor or registration scale is used as an expected pixel result. Source pose IDs locate observations; changed limb pixels and a settled standing render provide the independent comparisons.",
            Failures,Results
        },new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"{(Failures.Count==0 ? "PASS" : "FAIL")}: {checks} checks / {Results.Count} motion exit visual cases / {renders} production actor renders. {output}");
        foreach(string failure in Failures.Take(80)) Console.WriteLine(failure);
        return Failures.Count==0 ? 0 : 1;
    }

    private static void RunCase(Fixture f,string id,string entry,int fps,bool cold)
    {
        string name=$"{id}-{entry}-{(f.Left ? "left" : "right")}-dpi-{f.Dpi:0.0}-{fps}fps";
        if(filter.Length>0 && !System.Text.RegularExpressions.Regex.IsMatch(name,filter)) return;
        var result=new Result {Name=name,Action=id,Entry=entry,Left=f.Left,Dpi=f.Dpi,Fps=fps}; Results.Add(result);
        try {
            f.Reset(); var standing=f.Render(); result.Standing=standing.Metrics;
            Require(standing.Metrics.HeadHeight>20 && standing.Metrics.HeadHeight<40,"independent standing face/scalp detector is valid");
            double distance=52; f.Actor.Start(id,id=="turn" ? f.Actor.X : f.Actor.X+(f.Left ? -distance : distance),f.Actor.Y);
            double duration=f.Actor.ActiveDuration,trigger=duration*.37,elapsed=0,dt=cold ? .001 : 1.0/fps;
            bool requested=entry=="natural" || entry=="cold-natural",sawExit=false;
            bool export=!cold && !f.Left && f.Dpi==1 && fps==60;
            string frames=Path.Combine(output,id,entry,"frames"); if(export) Directory.CreateDirectory(frames);
            var keys=new Dictionary<int,Frame>(); Frame? prior=null,physicalEndFirst=null; int index=0;
            bool renderedCanonicalSlot=false,physicalEndFirstTransition=false;
            while(elapsed<duration+2.5) {
                if(!requested && elapsed+1e-9>=trigger) {
                    if(!cold) {
                        var before=f.Render();
                        Request(f,entry); var zero=f.Render();
                        Check(result,Difference(before,zero)<.01,"request preserves the actual displayed pose at zero supplied time");
                    } else Request(f,"stand");
                    requested=true;
                }
                bool active=f.Actor.MotionExit.Active;
                sawExit|=active;
                bool poses=active && f.Actor.MotionExit.Phase=="poses";
                int key=poses ? f.Actor.MotionExit.PoseFrame : -1;
                if(!cold || poses && !keys.ContainsKey(key)) {
                    if(cold) f.Scene.ResetLifeVisuals();
                    var frame=f.Render(); var m=frame.Metrics;
                    if(active && (f.Actor.MotionExit.Phase=="neutral" || key==f.Actor.MotionExit.LastPoseFrame)) renderedCanonicalSlot=true;
                    if(sawExit && !active && physicalEndFirst==null) {
                        physicalEndFirst=frame;physicalEndFirstTransition=f.Scene.ActorTransitionActive;
                    }
                    result.Samples.Add(new Sample {Frame=index,Time=elapsed,State=f.Actor.Current.Id,Phase=f.Actor.MotionExit.Phase,
                        Key=key,Transition=f.Scene.ActorTransitionActive,Root=new[]{f.Actor.X,f.Actor.Y},Metrics=m});
                    // The delivered PNGs can have a 254 maximum even in solid
                    // body pixels. Test opaque coverage, not an exact encoder
                    // convention; broad fades still fail these two bounds.
                    Check(result,m.Opaque>180*f.Dpi*f.Dpi && m.MaxAlpha>=250,"visible opaque actor frame "+index);
                    if(export) Png(frame.Image,Path.Combine(frames,$"frame-{index:0000}.png"));
                    if(key>=0) {
                        keys[key]=frame;
                        Check(result,Math.Abs(m.Bottom-f.Actor.Y)<=3,"recovery sandals remain on the ground "+key);
                        if(!f.Scene.ActorTransitionActive) {
                            Check(result,Math.Abs(m.HeadHeight-standing.Metrics.HeadHeight)<=standing.Metrics.HeadHeight*.10+1/f.Dpi,
                                $"recovery head stays within 10% plus one edge pixel of the measured standing head ({m.HeadHeight:F2}px)");
                            Check(result,m.Opaque/(double)Math.Max(1,m.Occupied)>.50,"complete recovery pose keeps an opaque interior "+key);
                            Check(result,m.WhiteTorso.Length==4 && m.WhitePixels>80,"complete connected blouse remains visible "+key);
                            if(cold) Check(result,DetachedBodyParts(frame,f.Dpi)==0,"no large disconnected arm or leg below the face "+key);
                            if(prior!=null) {
                                Check(result,Math.Abs(m.HeadX-prior.Metrics.HeadX)<=7+1/f.Dpi,"adjacent recovery poses have no large head-position jump");
                                Check(result,Math.Abs(m.HeadHeight-prior.Metrics.HeadHeight)<=3.5+1/f.Dpi,"adjacent recovery poses have no head-scale jump");
                            }
                            prior=frame;
                        }
                    }
                    if(cold || index%5==0 || key>=0) Hits(result,f,frame,index);
                    index++;
                }
                if(sawExit && !active) break;
                f.Step(dt); elapsed+=dt;
            }
            Check(result,sawExit && !f.Actor.MotionExit.Active,"the actual motion uses and completes a bounded physical exit");
            Check(result,keys.Count>=3,"at least three distinct complete recovery poses were rendered");
            var ordered=keys.OrderBy(p=>p.Key).ToArray();
            if(ordered.Length>1) {
                var first=ordered[0].Value;
                double legs=ordered.Skip(1).Max(p=>SkinChange(first,p.Value,f.Dpi,false));
                double hands=ordered.Skip(1).Max(p=>SkinChange(first,p.Value,f.Dpi,true));
                result.LegSkinChange=legs; result.HandSkinChange=hands;
                // Head-aligned lower-leg/outer-hand masks exclude a whole-image
                // translation. This tests articulated pixel change, not a clock.
                Check(result,legs>.025 || hands>.025,"limbs actually change relative to the head during recovery");
            }
            if(entry is "natural" or "stand" or "cold-natural" or "cold-brake") {
                Check(result,f.Actor.Current.Id=="stand","normal/explicit stand exit reaches standing");
                // Cold sweeps intentionally do not render every supplied Tick.
                // A missing final/neutral Draw must retain the normal fallback.
                // Continuous playback must never add another blend once its
                // actual canonical recovery key has already been displayed.
                var immediate=physicalEndFirst ?? f.Render();
                bool immediateTransition=physicalEndFirst==null ? f.Scene.ActorTransitionActive : physicalEndFirstTransition;
                double immediateClock=f.Clock;
                result.RenderedCanonicalSlot=renderedCanonicalSlot;
                result.ImmediateCanonicalChecked=!cold || renderedCanonicalSlot;
                result.ImmediateTransition=immediateTransition;
                if(result.ImmediateCanonicalChecked)
                    Check(result,!immediateTransition,"first physical-completion frame has no redundant generic standing blend");
                f.Clock+=.4; var settled=f.Render();
                f.Scene.ResetLifeVisuals(); var canonical=f.Render();
                double difference=Difference(settled,canonical); result.CanonicalDifference=difference;
                Check(result,difference==0,"completed exit exactly matches a cold canonical standing render at the same clock/root/facing");
                Check(result,Math.Abs(settled.Metrics.Bottom-f.Actor.Y)<=2,"canonical standing foot contact remains grounded");
                if(export) { Png(settled.Image,Path.Combine(frames,$"frame-{index:0000}.png")); Png(canonical.Image,Path.Combine(output,id,entry,"canonical.png")); }
                if(result.ImmediateCanonicalChecked) {
                    // Build the immediate oracle only after the original
                    // 400ms check, so clearing the renderer cannot hide a late
                    // crossfade. Behavior/root/facing have not advanced here.
                    double settledClock=f.Clock;f.Clock=immediateClock;f.Scene.ResetLifeVisuals();
                    var immediateCanonical=f.Render();f.Clock=settledClock;
                    result.ImmediateCanonicalDifference=Difference(immediate,immediateCanonical);
                    Check(result,result.ImmediateCanonicalDifference==0,"first physical-completion frame exactly equals cold canonical standing at that same clock/root/facing");
                }
            } else if(entry=="tablet") Check(result,f.Actor.Tablet.Active,"queued tablet opens only after recovery completes");
            else Check(result,f.Actor.Current.Id is "smile" or "retreat" or "invite","click response survives recovery without an extra idle action");
            result.Keys=ordered.Select(p=>p.Key).ToArray();
            result.Duration=elapsed;
            if(cold && !f.Left && f.Dpi==1) foreach(var pair in ordered) {
                string path=Path.Combine(output,id,entry); Directory.CreateDirectory(path);
                Png(pair.Value.Image,Path.Combine(path,$"pose-{pair.Key:00}.png"));
            }
        }
        catch(Exception error) { Check(result,false,Unwrap(error)); }
        Console.WriteLine($"{(result.Failures.Count==0 ? "PASS" : "FAIL")}: {name}, {result.Samples.Count} observations");
    }

    private static void Request(Fixture f,string entry)
    {
        if(entry=="tablet") Require(f.Pet.OpenTablet("messages"),"production tablet request accepted");
        else if(entry=="click") f.Actor.Click(f.Pet.Save);
        else f.Pet.PreviewAction("stand");
    }
    private static void Hits(Result result,Fixture f,Frame frame,int index)
    {
        int misses=0,ghosts=0,probes=0,width=frame.Image.PixelWidth;
        int step=Math.Max(1,(int)Math.Round(4*f.Dpi));
        int left=Math.Max(2,(int)(frame.Metrics.Left*f.Dpi)-step),right=Math.Min(width-3,(int)(frame.Metrics.Right*f.Dpi)+step);
        int top=Math.Max(2,(int)(frame.Metrics.Top*f.Dpi)-step),bottom=Math.Min(frame.Image.PixelHeight-3,(int)(frame.Metrics.Bottom*f.Dpi)+step);
        for(int y=top;y<=bottom;y+=step) for(int x=left;x<=right;x+=step) {
            int alpha=frame.Pixels[(y*width+x)*4+3];
            string? hit=f.Scene.Hit(new Point((x+.5)/f.Dpi,(y+.5)/f.Dpi));
            if(alpha>=230) {probes++;if(hit!="rin")misses++;}
            // Require a two-logical-pixel transparent neighbourhood, excluding
            // subpixel interpolation and the production 48-alpha hit fringe.
            if(alpha==0 && hit=="rin" && Empty(frame,x,y,Math.Max(1,(int)Math.Ceiling(2*f.Dpi))))ghosts++;
        }
        Check(result,probes>10 && misses<=3,$"opaque silhouette maps to actual actor hits frame {index}: {misses}/{probes}");
        Check(result,ghosts==0,$"no stale actor hit in transparent gaps frame {index}: {ghosts}");
        Check(result,f.Scene.Hit(new Point(0,0))==null,"outside transparent desktop remains click-through");
    }
    private static bool Empty(Frame f,int x,int y,int radius)
    {
        int width=f.Image.PixelWidth;
        for(int dy=-radius;dy<=radius;dy++) for(int dx=-radius;dx<=radius;dx++) {
            int px=x+dx,py=y+dy;
            if(px>=0 && py>=0 && px<width && py<f.Image.PixelHeight && f.Pixels[(py*width+px)*4+3]>0)return false;
        }
        return true;
    }

    private static int DetachedBodyParts(Frame frame,double dpi)
    {
        // Flood-fill the rendered alpha, independently of atlas bounds/anchors.
        // Tiny anti-alias islands, separate sandal straps and bow tips are not
        // treated as an amputated limb. A substantial tall component below the
        // face, separate from the largest body, is a real connection defect.
        int width=frame.Image.PixelWidth,height=frame.Image.PixelHeight;
        var seen=new bool[width*height]; var queue=new Queue<int>();
        var parts=new List<(int Area,int Top,int Bottom)>();
        int x0=Math.Max(0,(int)(frame.Metrics.Left*dpi)-2),x1=Math.Min(width-1,(int)(frame.Metrics.Right*dpi)+2);
        int y0=Math.Max(0,(int)(frame.Metrics.Top*dpi)-2),y1=Math.Min(height-1,(int)(frame.Metrics.Bottom*dpi)+2);
        for(int y=y0;y<=y1;y++) for(int x=x0;x<=x1;x++) {
            int start=y*width+x;
            if(seen[start] || frame.Pixels[start*4+3]<128)continue;
            int count=0,top=y,bottom=y;seen[start]=true;queue.Enqueue(start);
            while(queue.Count>0) {
                int at=queue.Dequeue(),px=at%width,py=at/width;count++;top=Math.Min(top,py);bottom=Math.Max(bottom,py);
                for(int dy=-1;dy<=1;dy++) for(int dx=-1;dx<=1;dx++) {
                    int tx=px+dx,ty=py+dy;if(tx<x0 || tx>x1 || ty<y0 || ty>y1)continue;
                    int next=ty*width+tx;
                    if(!seen[next] && frame.Pixels[next*4+3]>=128){seen[next]=true;queue.Enqueue(next);}
                }
            }
            parts.Add((count,top,bottom));
        }
        return parts.OrderByDescending(p=>p.Area).Skip(1).Count(p=>p.Area>=50*dpi*dpi && p.Top/dpi>frame.Metrics.Chin+10 && (p.Bottom-p.Top+1)/dpi>15);
    }

    private static Metrics Measure(BitmapSource image,byte[] pixels,double dpi,double rootX,double rootY)
    {
        int width=image.PixelWidth,height=image.PixelHeight,l=width,r=-1,t=height,b=-1,occupied=0,opaque=0,max=0;
        int x0=Math.Max(0,(int)((rootX-145)*dpi)),x1=Math.Min(width-1,(int)((rootX+145)*dpi));
        int y0=Math.Max(0,(int)((rootY-190)*dpi)),y1=Math.Min(height-1,(int)((rootY+12)*dpi));
        for(int y=y0;y<=y1;y++) for(int x=x0;x<=x1;x++) {
            int a=pixels[(y*width+x)*4+3]; max=Math.Max(max,a); if(a>=245)opaque++;if(a>48)occupied++;
            if(a<180)continue;l=Math.Min(l,x);r=Math.Max(r,x);t=Math.Min(t,y);b=Math.Max(b,y);
        }
        if(r<l)return new Metrics();
        int start=Math.Min(height-1,t+(int)(9*dpi)),end=Math.Min(height-1,t+(int)(47*dpi));
        var runs=new double[height]; var centers=new double[height];
        for(int y=start;y<=end;y++) {
            int run=0,best=0;double center=(l+r)*.5;
            for(int x=l;x<=r;x++) {
                if(Skin(pixels,(y*width+x)*4)) {run++;if(run>best){best=run;center=x-(run-1)*.5;}}
                else run=0;
            }
            runs[y]=best;centers[y]=center;
        }
        int radius=Math.Max(1,(int)Math.Round(dpi));var smooth=new double[height];
        for(int y=start;y<=end;y++) smooth[y]=Enumerable.Range(Math.Max(start,y-radius),Math.Min(end,y+radius)-Math.Max(start,y-radius)+1).Average(i=>runs[i]);
        int peak=Enumerable.Range(start,Math.Max(1,end-start+1)).OrderByDescending(y=>smooth[y]).First(),chin=end;
        for(int y=peak+1;y<=end-radius;y++) if(smooth[y]<smooth[peak]*.65 && smooth[Math.Min(end,y+radius)]<smooth[peak]*.65){chin=y;break;}
        double headX=centers[peak]/dpi;
        int wl=width,wr=-1,wt=height,wb=-1,white=0;
        var handL=new Accumulator();var handR=new Accumulator();var kneeL=new Accumulator();var kneeR=new Accumulator();
        for(int y=Math.Max(0,chin+1);y<=b;y++) for(int x=l;x<=r;x++) {
            int p=(y*width+x)*4;double a=pixels[p+3];if(a<180)continue;
            double px=(x+.5)/dpi,py=(y+.5)/dpi;
            if(Skin(pixels,p)) {
                if(py>chin/dpi+10 && py<rootY-57 && Math.Abs(px-headX)>14) (px<headX ? handL : handR).Add(px,py);
                if(py>rootY-57 && py<rootY-16) (px<rootX ? kneeL : kneeR).Add(px,py);
            }
            if(py>rootY-58 || Math.Abs(px-headX)>34)continue;
            double rr=pixels[p+2]*255/a,g=pixels[p+1]*255/a,bb=pixels[p]*255/a;
            if(Math.Min(rr,Math.Min(g,bb))<145 || Math.Max(rr,Math.Max(g,bb))-Math.Min(rr,Math.Min(g,bb))>28)continue;
            white++;wl=Math.Min(wl,x);wr=Math.Max(wr,x);wt=Math.Min(wt,y);wb=Math.Max(wb,y);
        }
        return new Metrics {Left=l/dpi,Right=(r+1)/dpi,Top=t/dpi,Bottom=(b+1)/dpi,Occupied=occupied,Opaque=opaque,MaxAlpha=max,
            HeadX=headX,HeadHeight=(chin-t)/dpi,Chin=chin/dpi,WhitePixels=white/(dpi*dpi),
            WhiteTorso=white==0 ? Array.Empty<double>() : new[]{wl/dpi,wt/dpi,(wr-wl+1)/dpi,(wb-wt+1)/dpi},
            LeftHand=handL.Point(),RightHand=handR.Point(),LeftKnee=kneeL.Point(),RightKnee=kneeR.Point()};
    }
    private static bool Skin(byte[] pixels,int p)
    {
        double a=pixels[p+3];if(a<=180)return false;
        double r=pixels[p+2]*255/a,g=pixels[p+1]*255/a,b=pixels[p]*255/a;
        return r>190 && g>140 && b>115 && r-g>8 && r-g<75 && g-b>4 && g-b<60 && r-b>23;
    }
    private static double SkinChange(Frame a,Frame b,double dpi,bool hands)
    {
        int width=a.Image.PixelWidth;int changed=0,union=0;
        // Align the independently measured scalp and face centre. A pure whole
        // sprite translation then contributes no claimed limb articulation.
        int dx=(int)Math.Round((b.Metrics.HeadX-a.Metrics.HeadX)*dpi),dy=(int)Math.Round((b.Metrics.Top-a.Metrics.Top)*dpi);
        double minY=hands ? a.Metrics.Chin+10 : a.RootY-57,maxY=hands ? a.RootY-57 : a.RootY-16;
        for(int y=Math.Max(0,(int)(minY*dpi));y<Math.Min(a.Image.PixelHeight,(int)(maxY*dpi));y++)
        for(int x=Math.Max(0,(int)((a.Metrics.HeadX-60)*dpi));x<Math.Min(width,(int)((a.Metrics.HeadX+60)*dpi));x++) {
            if(hands && Math.Abs((x+.5)/dpi-a.Metrics.HeadX)<14)continue;
            int bx=x+dx,by=y+dy;if(bx<0 || by<0 || bx>=width || by>=b.Image.PixelHeight)continue;
            bool sa=Skin(a.Pixels,(y*width+x)*4),sb=Skin(b.Pixels,(by*width+bx)*4);
            if(sa||sb)union++;if(sa!=sb)changed++;
        }
        return changed/(double)Math.Max(1,union);
    }
    private static double Difference(Frame a,Frame b)
    {
        long sum=0;for(int i=0;i<a.Pixels.Length;i++)sum+=Math.Abs(a.Pixels[i]-b.Pixels[i]);
        return sum/(double)a.Pixels.Length;
    }
    private static void Png(BitmapSource image,string path) {using var stream=File.Create(path);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));encoder.Save(stream);}
    private static void Check(Result result,bool ok,string message) {checks++;if(ok)return;string text=result.Name+": "+message;result.Failures.Add(text);Failures.Add(text);}
    private static void Require(bool ok,string message) {checks++;if(!ok)throw new InvalidOperationException(message);}
    private static string Unwrap(Exception e)=>(e is TargetInvocationException && e.InnerException!=null ? e.InnerException : e).ToString();
    private sealed class Accumulator {private double x,y;private int n;public void Add(double px,double py){x+=px;y+=py;n++;}public double[] Point()=>n==0 ? Array.Empty<double>() : new[]{x/n,y/n,(double)n};}
    private sealed class Fixture
    {
        public readonly PetWindow Pet;public Behavior Actor=>Pet.Behavior;public Scene Scene=>Pet.Scene;
        public readonly bool Left;public readonly double Dpi;public double Clock;
        public Fixture(PetWindow pet,bool left,double dpi){Pet=pet;Left=left;Dpi=dpi;Clock=Scene.Time+1;}
        public void Reset() {
            Actor.Dragging=false;Actor.CancelActionRecovery();Actor.Journey.Cancel(Pet.Save);Actor.Nurture.Cancel(Pet.Save);Actor.Tablet.Cancel(Pet.Save);Actor.Life.Cancel();
            Require(Pet.SwitchScene("outdoor"),"production outdoor switch");
            Actor.X=240;Actor.Y=252;Actor.Start("stand",240+(Left ? -1 : 1));
            Scene.Measure(new Size(480,340));Scene.Arrange(new Rect(0,0,480,340));Scene.ResetLifeVisuals();
            Render();Clock+=.4;Render();
        }
        public void Step(double seconds){Actor.Tick(seconds,Pet.Save,false);Clock+=seconds;}
        public Frame Render() {
            Scene.Time=Clock;var visual=new DrawingVisual();RenderOptions.SetBitmapScalingMode(visual,BitmapScalingMode.HighQuality);
            using(var dc=visual.RenderOpen())DrawActor.Invoke(Scene,new object[]{dc});
            var bitmap=new RenderTargetBitmap((int)(480*Dpi),(int)(340*Dpi),96*Dpi,96*Dpi,PixelFormats.Pbgra32);bitmap.Render(visual);bitmap.Freeze();
            var pixels=new byte[bitmap.PixelWidth*bitmap.PixelHeight*4];bitmap.CopyPixels(pixels,bitmap.PixelWidth*4,0);renders++;
            return new Frame(bitmap,pixels,Actor.Y,Measure(bitmap,pixels,Dpi,Actor.X,Actor.Y));
        }
    }
    private sealed record Frame(BitmapSource Image,byte[] Pixels,double RootY,Metrics Metrics);
    private sealed class Metrics {
        public double Left{get;set;}public double Right{get;set;}public double Top{get;set;}public double Bottom{get;set;}
        public int Occupied{get;set;}public int Opaque{get;set;}public int MaxAlpha{get;set;}
        public double HeadX{get;set;}public double HeadHeight{get;set;}public double Chin{get;set;}public double WhitePixels{get;set;}
        public double[] WhiteTorso{get;set;}=Array.Empty<double>();public double[] LeftHand{get;set;}=Array.Empty<double>();public double[] RightHand{get;set;}=Array.Empty<double>();
        public double[] LeftKnee{get;set;}=Array.Empty<double>();public double[] RightKnee{get;set;}=Array.Empty<double>();
    }
    private sealed class Sample {public int Frame{get;set;}public double Time{get;set;}public string State{get;set;}="";public string Phase{get;set;}="";public int Key{get;set;}public bool Transition{get;set;}public double[] Root{get;set;}=Array.Empty<double>();public Metrics Metrics{get;set;}=new();}
    private sealed class Result {
        public string Name{get;set;}="";public string Action{get;set;}="";public string Entry{get;set;}="";public bool Left{get;set;}public double Dpi{get;set;}public int Fps{get;set;}
        public Metrics Standing{get;set;}=new();public List<Sample> Samples{get;set;}=new();public int[] Keys{get;set;}=Array.Empty<int>();
        public double Duration{get;set;}public double LegSkinChange{get;set;}public double HandSkinChange{get;set;}public double CanonicalDifference{get;set;}
        public bool RenderedCanonicalSlot{get;set;}public bool ImmediateCanonicalChecked{get;set;}public bool ImmediateTransition{get;set;}public double ImmediateCanonicalDifference{get;set;}
        public List<string> Failures{get;set;}=new();
    }
}
