using System.Text.Json;
using System.Text.Json.Serialization;
namespace LittleShelter;

public sealed class Storage
{
    private readonly string directory;
    public string FilePath => Path.Combine(directory, "shelter.json");
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals };
    public Storage(string directory) => this.directory = directory;
    public SaveData Load()
    {
        Directory.CreateDirectory(directory);
        foreach (var path in new[] { FilePath, FilePath + ".bak" })
        {
            try
            {
                if (!File.Exists(path)) continue;
                using var document = JsonDocument.Parse(File.ReadAllText(path));
                if (document.RootElement.ValueKind != JsonValueKind.Object || !document.RootElement.TryGetProperty("Version", out _)) continue;
                var save = document.RootElement.Deserialize<SaveData>(Options);
                if (save == null || save.Version != 1 || save.Settings == null || save.DailySeconds == null || save.CompanionDates == null || save.ChoiceCounts == null || save.GrowthLayout == null || save.FreeLayout == null || save.Created == null || save.GrowthLayout.Any(p => p == null) || save.FreeLayout.Any(p => p == null)) continue;
                if (document.RootElement.TryGetProperty("Settings",out var oldSettings)) {
                    bool hasScene=oldSettings.TryGetProperty("GrowthScene",out _);
                    if (save.Settings.GrowthScene == "outdoor" || !hasScene) {
                        save.Settings.LegacyOutdoorAccess=true;
                        if (!hasScene) save.Settings.GrowthScene="outdoor";
                    }
                }
                Sanitize(save); World.Initialize(save); return save;
            }
            catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { }
        }
        var fresh = new SaveData(); World.Initialize(fresh); return fresh;
    }
    public void Write(SaveData save)
    {
        Directory.CreateDirectory(directory);
        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(save, Options));
        if (File.Exists(FilePath)) File.Replace(temp, FilePath, FilePath + ".bak", true);
        else File.Move(temp, FilePath);
    }
    private static void Sanitize(SaveData s)
    {
        Story.Sanitize(s);
        if (s.Settings.StartupAnimationPlayback is not ("adaptive" or "always" or "off")) s.Settings.StartupAnimationPlayback="adaptive";
        s.Settings.Scale = double.IsFinite(s.Settings.Scale) ? Math.Clamp(s.Settings.Scale, .65, 1.8) : 1;
        s.Settings.Opacity = double.IsFinite(s.Settings.Opacity) ? Math.Clamp(s.Settings.Opacity, .35, 1) : 1;
        s.Settings.InteractionMinutes = double.IsFinite(s.Settings.InteractionMinutes) ? Math.Clamp(s.Settings.InteractionMinutes, 15, 120) : 35;
        s.CompanionDates = s.CompanionDates.Where(d => DateOnly.TryParse(d, out _)).Distinct().ToList();
        foreach (var list in new[] { s.GrowthLayout, s.FreeLayout })
        {
            list.RemoveAll(p => !ObjectDef.All.Any(d => d.Id == p.Id) || !double.IsFinite(p.X) || !double.IsFinite(p.Y));
            foreach (var p in list) { p.X = Math.Clamp(p.X, 25, 455); p.Y = Math.Clamp(p.Y, 45, 310); }
        }
        void CleanScenes(Dictionary<string,List<Placement>>? map)
        {
            if (map == null) return;
            foreach (var key in map.Keys.ToArray())
            {
                if (!Scenes.All.Any(d=>d.Id == key && key != "outdoor") || map[key] == null) { map.Remove(key); continue; }
                var list=map[key];
                list.RemoveAll(p=>p == null || !(Scenes.Objects(key).Any(d=>d.Id == p.Id) || key == "indoor" && p.Id == "lamp") || !double.IsFinite(p.X) || !double.IsFinite(p.Y));
                foreach (var p in list) { p.X=Math.Clamp(p.X,25,455); p.Y=Math.Clamp(p.Y,45,310); if (Scenes.Foundation(p.Id)) p.Hidden=false; }
                foreach (var d in Scenes.Objects(key).Where(d=>Scenes.Foundation(d.Id))) if (!list.Any(p=>p.Id == d.Id)) list.Add(World.Default(d));
                var distinct=list.DistinctBy(p=>p.Id).ToList(); list.Clear(); list.AddRange(distinct);
            }
        }
        CleanScenes(s.GrowthSceneLayouts); CleanScenes(s.FreeSceneLayouts);
        if (!Scenes.All.Any(d=>d.Id == s.Settings.FreeScene)) s.Settings.FreeScene="outdoor";
        if (!Scenes.Unlocked(s,s.Settings.GrowthScene)) s.Settings.GrowthScene="cloud";
    }
}
