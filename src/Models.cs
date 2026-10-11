using System.Text.Json.Serialization;

namespace LittleShelter;

public sealed class Settings
{
    public double Scale { get; set; } = 1;
    public double Opacity { get; set; } = 1;
    public bool Music { get; set; } = true;
    public bool Reminders { get; set; }
    public bool Sound { get; set; }
    public bool EnergySaving { get; set; }
    public bool ClickThrough { get; set; }
    public bool AutoStart { get; set; }
    public bool FreeMode { get; set; }
    public string Weather { get; set; } = "晴";
    public string Theme { get; set; } = "自动";
    public double Left { get; set; } = double.NaN;
    public double Top { get; set; } = double.NaN;
    public double InteractionMinutes { get; set; } = 35;
}

public sealed class Placement
{
    public string Id { get; set; } = "";
    public double X { get; set; }
    public double Y { get; set; }
    public bool Hidden { get; set; }
    public Placement Clone() => new() { Id = Id, X = X, Y = Y, Hidden = Hidden };
}

public sealed class SaveData
{
    public int Version { get; set; } = 1;
    public Settings Settings { get; set; } = new();
    public Dictionary<string, double> DailySeconds { get; set; } = new();
    public List<string> CompanionDates { get; set; } = new();
    public Dictionary<string, int> ChoiceCounts { get; set; } = new();
    public List<Placement> GrowthLayout { get; set; } = new();
    public List<Placement> FreeLayout { get; set; } = new();
    public HashSet<string> Created { get; set; } = new();
    public long Interactions { get; set; }
    public double MusicSeconds { get; set; }
    [JsonIgnore] public int Days => CompanionDates.Count;
    [JsonIgnore] public List<Placement> Layout => Settings.FreeMode ? FreeLayout : GrowthLayout;
}

public sealed record ObjectDef(string Id, string Name, int Day, int Tile, double Width, double Height, double X, double Y)
{
    public static readonly ObjectDef[] All = {
        new("platform", "云台", 0, 0, 420, 95, 240, 310),
        new("grass", "草地", 2, 1, 350, 75, 240, 280),
        new("flower", "花", 3, 2, 45, 50, 320, 254),
        new("path", "小路", 4, 6, 175, 45, 235, 276),
        new("tree", "小树", 5, 3, 110, 156, 370, 249),
        new("bird", "小鸟", 6, 10, 32, 28, 330, 100),
        new("bench", "长椅", 7, 5, 110, 68, 270, 252),
        new("lamp", "灯", 8, 9, 30, 80, 100, 247),
        new("cloud", "云", 10, 4, 105, 55, 165, 92),
        new("windmill", "风车", 12, 8, 63, 102, 75, 244),
        new("house", "小屋", 14, 7, 132, 132, 170, 247),
        new("stars", "星光", 20, 11, 145, 65, 305, 72)
    };
    public static ObjectDef Get(string id) => All.First(d => d.Id == id);
}

public sealed record ActionDef(string Id, string Name, int Pose, double Duration, string Kind, string? ObjectId = null);

public static class Actions
{
    public static readonly ActionDef[] All = {
        new("stand", "轻轻呼吸", 0, 12, "idle"), new("knees", "抱膝发呆", 6, 20, "idle"),
        new("swing", "坐着晃腿", 7, 18, "idle"), new("gaze", "看向远处", 0, 14, "idle"),
        new("sky", "仰望天空", 2, 16, "idle"), new("dress", "整理衣摆", 3, 6, "idle"),
        new("tablet", "查看平板", 4, 14, "idle"), new("catch", "接住花瓣", 5, 8, "idle"),
        new("lie", "躺下休息", 8, 24, "idle"), new("nap", "闭眼小憩", 9, 20, "idle"),
        new("benchSit", "坐在长椅上", 7, 22, "context", "bench"),
        new("treeGaze", "树下看天空", 2, 20, "context", "tree"),
        new("touchFlower", "轻触花朵", 11, 10, "context", "flower"),
        new("watchBird", "看小鸟飞过", 2, 14, "context", "bird"),
        new("touchLight", "触碰光片", 5, 9, "context", "stars"),
        new("homeVisit", "小屋前停留", 0, 15, "context", "house"),
        new("walk", "慢慢走", 0, 5, "move"), new("trot", "小步跑", 0, 3, "move"),
        new("hop", "轻跃平台", 0, 2, "move"), new("turn", "边缘转身", 0, 2, "move"),
        new("visit", "走向景物", 0, 5, "move"),
        new("createTap", "点触生成", 4, 7, "create"),
        new("createSwipe", "划出云带", 10, 7, "create"),
        new("createRaise", "抬手创造", 10, 7, "create"),
        new("createPlace", "摆放景物", 11, 7, "create"),
        new("createMusic", "随音乐创造", 4, 8, "create"),
        new("smile", "轻轻微笑", 1, 5, "interact"),
        new("retreat", "轻退一步", 0, 3, "interact"),
        new("invite", "展示平板", 4, 6, "interact")
    };
    public static ActionDef Get(string id) => All.First(a => a.Id == id);
}
