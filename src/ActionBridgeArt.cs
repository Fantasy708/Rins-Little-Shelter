using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Media;
namespace LittleShelter;

// Immutable preparation cache for authored full-body action in-betweens.
// Every output uses local ground-root (0,0), a complete 2x Pbgra32 painting,
// and controls passed through the SAME map as that painting. Loading, source
// isolation and raster registration happen once, never in Get/playback.
internal sealed class ActionBridgeArt
{
    public sealed record Frame(Atlas.Sprite Sprite, Rect Bounds, Point[] Controls);
    private readonly Dictionary<string,Frame[]> sheets = new(StringComparer.Ordinal);
    public int Count { get; private set; }
    public long Bytes { get; private set; }

    public ActionBridgeArt(string assetsRoot)
        : this(Path.Combine(assetsRoot,"rin","action-bridge-art.json"),assetsRoot,
            MotionAnatomy.Load(Path.Combine(assetsRoot,"rin","motion-registration.json"))) { }

    public ActionBridgeArt(string pathToJson,string assetsRoot,MotionAnatomy reference)
    {
        string root=Path.GetFullPath(assetsRoot);
        if(reference==null || new[]{reference.HeadHeight,reference.TorsoLength,reference.LegLength}.Any(v=>!double.IsFinite(v)||v<=0) ||
            Math.Abs(reference.HeadHeight+reference.TorsoLength+reference.LegLength-ActorPoseRegistration.ActorHeight)>.01)
            throw new InvalidDataException("动作交接需要有效的共同头身腿尺寸");
        var metadata=JsonSerializer.Deserialize<Manifest>(File.ReadAllText(pathToJson))
            ?? throw new InvalidDataException("动作交接素材登记为空");
        if(metadata.Version!=1 || metadata.Sheets==null || metadata.Sheets.Length!=3)
            throw new InvalidDataException("动作交接素材版本1需要ground、seat、upper三张图集");
        var expected=new Dictionary<string,int>(StringComparer.Ordinal){{"ground",12},{"seat",8},{"upper",12}};
        foreach(var sheet in metadata.Sheets)
        {
            if(sheet==null || string.IsNullOrWhiteSpace(sheet.Id) || !expected.TryGetValue(sheet.Id,out int frameCount) || sheets.ContainsKey(sheet.Id) ||
                sheet.Regions==null || sheet.Frames==null || sheet.Regions.Length!=frameCount || sheet.Frames.Length!=frameCount ||
                sheet.Regions.Any(r=>r==null || r.Length!=4 || r.Any(v=>!double.IsFinite(v)) || r[0]<0 || r[1]<0 || r[2]<=0 || r[3]<=0 || r[0]+r[2]>1.000000001 || r[1]+r[3]>1.000000001) ||
                sheet.Frames.Any(f=>f==null || f.Frame<0 || f.Frame>=frameCount) || sheet.Frames.Select(f=>f.Frame).Distinct().Count()!=frameCount)
                throw new InvalidDataException("动作交接图集、裁切或帧索引不完整");
            string path=AssetPath(root,sheet.Path);
            var atlas=new Atlas(path,sheet.Regions,isolateConnectedPose:true);
            var cache=new Frame[frameCount];
            foreach(var entry in sheet.Frames)
            {
                var source=atlas.Sprites[entry.Frame]; Validate(entry,source,sheet.Id);
                Frame frame;
                if(entry.UniformScale.HasValue)
                {
                    double k=entry.UniformScale.Value;
                    var rect=new Rect((source.CropLeft-entry.Root[0])*k,(source.CropTop-entry.Root[1])*k,source.Width*k,source.Height*k);
                    var bounds=rect;bounds.Inflate(1,1);bounds=ActorBitmapBlend.PixelBounds(bounds);
                    var bitmap=ActorBitmapBlend.Render(bounds,dc=>dc.DrawImage(source.Image,rect));
                    var sprite=new Atlas.Sprite(bitmap,0,0,bitmap.PixelWidth,bitmap.PixelHeight,0);
                    var controls=entry.Points.Select(p=>new Point((p[0]-entry.Root[0])*k,(p[1]-entry.Root[1])*k)).ToArray();
                    frame=new Frame(sprite,bounds,controls);
                }
                else
                {
                    var landmarks=new MotionLandmarks{HeadX=entry.HeadX,Crown=entry.Crown,Chin=entry.Chin,Hip=entry.Hip,
                        TorsoLength=entry.TorsoLength,LegLength=entry.LegLength,Root=entry.Root};
                    var registered=new MotionPoseFrames(new[]{source},new[]{landmarks},reference);
                    var controls=entry.Points.Select(p=>registered.MapPoint(0,p)).ToArray();
                    var rootPoint=registered.MapPoint(0,entry.Root);
                    if((rootPoint-new Point()).Length>.001)throw new InvalidDataException($"{sheet.Id}:{entry.Frame} ground root did not map to zero");
                    frame=new Frame(registered.Frame(0),registered.Bounds,controls);
                }
                if(frame.Bounds.IsEmpty || frame.Sprite.Image.Format!=PixelFormats.Pbgra32 || !frame.Sprite.Image.IsFrozen ||
                    Math.Abs(frame.Sprite.Width-frame.Bounds.Width*2)>.001 || Math.Abs(frame.Sprite.Height-frame.Bounds.Height*2)>.001 ||
                    frame.Controls.Any(p=>!double.IsFinite(p.X)||!double.IsFinite(p.Y)||!frame.Bounds.Contains(p)))
                    throw new InvalidDataException($"{sheet.Id}:{entry.Frame} registered raster and controls disagree");
                cache[entry.Frame]=frame;Count++;Bytes+=frame.Sprite.Pixels.LongLength+frame.Controls.LongLength*16;
            }
            sheets.Add(sheet.Id,cache);
        }
        if(Count!=32)throw new InvalidDataException("动作交接素材需要完整32帧");
    }

    // Cache-owned Controls are read-only to callers. Copy only when preparing a
    // translated/mirrored endpoint; a normal cache lookup allocates nothing.
    public (Atlas.Sprite Sprite,Rect Bounds,Point[] Controls) Get(string sheet,int index)
    {var frame=GetFrame(sheet,index);return(frame.Sprite,frame.Bounds,frame.Controls);}
    public Frame GetFrame(string sheet,int index)
    {
        if(!sheets.TryGetValue(sheet,out var frames))throw new ArgumentException("未知动作交接图集",nameof(sheet));
        if((uint)index>=(uint)frames.Length)throw new ArgumentOutOfRangeException(nameof(index));
        return frames[index];
    }
    private static string AssetPath(string root,string relative)
    {
        if(string.IsNullOrWhiteSpace(relative)||Path.IsPathRooted(relative)||!string.Equals(Path.GetExtension(relative),".png",StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("动作交接素材必须引用资源目录内的PNG");
        string full=Path.GetFullPath(Path.Combine(root,relative));
        string prefix=root.TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar)+Path.DirectorySeparatorChar;
        if(!full.StartsWith(prefix,StringComparison.OrdinalIgnoreCase)||!File.Exists(full))
            throw new InvalidDataException("动作交接PNG不存在或超出资源目录");
        return full;
    }
    private static void Validate(Entry entry,Atlas.Sprite source,string sheet)
    {
        bool InCell(double[] point)=>point!=null && point.Length==2 && point.All(double.IsFinite) &&
            point[0]>=0 && point[0]<=source.CellWidth && point[1]>=0 && point[1]<=source.CellHeight;
        if(!InCell(entry.Root)||entry.Points==null||entry.Points.Length!=17||entry.Points.Any(p=>!InCell(p))||
            new[]{entry.HeadX,entry.Crown,entry.Chin,entry.Hip,entry.TorsoLength,entry.LegLength}.Any(v=>!double.IsFinite(v))||
            entry.HeadX<0||entry.HeadX>source.CellWidth||entry.Crown<0||entry.Chin<=entry.Crown||entry.Chin>source.CellHeight||
            entry.Hip<0||entry.Hip>source.CellHeight||entry.TorsoLength<=0||entry.LegLength<=0||
            (entry.UniformScale.HasValue&&(!double.IsFinite(entry.UniformScale.Value)||entry.UniformScale.Value<=0||entry.UniformScale.Value>4)))
            throw new InvalidDataException($"{sheet}:{entry.Frame} source-cell body registration is invalid");
    }
    private sealed class Manifest
    {
        [JsonRequired] public int Version{get;set;}
        [JsonRequired] public Sheet[] Sheets{get;set;}=Array.Empty<Sheet>();
    }
    private sealed class Sheet
    {
        [JsonRequired] public string Id{get;set;}="";
        [JsonRequired] public string Path{get;set;}="";
        [JsonRequired] public double[][] Regions{get;set;}=Array.Empty<double[]>();
        [JsonRequired] public Entry[] Frames{get;set;}=Array.Empty<Entry>();
    }
    private sealed class Entry
    {
        [JsonRequired] public int Frame{get;set;}
        [JsonRequired] public double HeadX{get;set;}
        [JsonRequired] public double Crown{get;set;}
        [JsonRequired] public double Chin{get;set;}
        [JsonRequired] public double Hip{get;set;}
        [JsonRequired] public double TorsoLength{get;set;}
        [JsonRequired] public double LegLength{get;set;}
        [JsonRequired] public double[] Root{get;set;}=Array.Empty<double>();
        [JsonRequired] public double[][] Points{get;set;}=Array.Empty<double[]>();
        public double? UniformScale{get;set;}
    }
}
