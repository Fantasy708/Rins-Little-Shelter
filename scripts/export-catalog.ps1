$ErrorActionPreference = 'Stop'
$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$definitions = Get-Content -LiteralPath (Join-Path $projectRoot 'src/Models.cs') -Raw
$clipSource = Get-Content -LiteralPath (Join-Path $projectRoot 'src/MotionClips.cs') -Raw
$resourceManifest = Get-Content -LiteralPath (Join-Path $projectRoot 'assets/manifest.json') -Raw | ConvertFrom-Json
$clips = @{}
foreach ($match in [regex]::Matches($clipSource, 'MotionClip (?<name>Hop|Turn) = new\((?<seconds>[\d.]+),\s*new\[\] \{(?<keys>[^}]+)\}')) {
    $clips[$match.Groups['name'].Value.ToLowerInvariant()] = [ordered]@{ Seconds=[double]::Parse($match.Groups['seconds'].Value,[Globalization.CultureInfo]::InvariantCulture); Keys=@($match.Groups['keys'].Value.Split(',')|ForEach-Object {[double]::Parse($_.Trim(),[Globalization.CultureInfo]::InvariantCulture)}) }
}
if ($clips.Count -ne 2) { throw '跳跃/转身时间轴导出失败。' }
$actions = [regex]::Matches($definitions, 'new\("(?<id>[^"]+)",\s*"(?<name>[^"]+)",\s*(?<pose>\d+),\s*(?<duration>\d+),\s*"(?<kind>[^"]+)"(?:,\s*"(?<object>[^"]+)")?\)') | ForEach-Object {
    $id=$_.Groups['id'].Value
    [ordered]@{ Id=$id; Name=$_.Groups['name'].Value; Pose=[int]$_.Groups['pose'].Value; DurationSeconds=[int]$_.Groups['duration'].Value; Kind=$_.Groups['kind'].Value; Object=$_.Groups['object'].Value; FeetAnchor=@(.5,1); Hit='visible alpha times opacity > 48'; WalkFrames=if ($_.Groups['kind'].Value -eq 'move' -and $id -notin @('hop','turn','trot')) { @(0,1,2,3,4,5,6,7) } else { @() }; JogFrames=if ($id -eq 'trot') { @(0,1,2,3,4,5,6,7) } else { @() }; PoseFrames=if ($clips.ContainsKey($id)) { @(0..11) } else { @() }; ClipSeconds=if ($clips.ContainsKey($id)) { $clips[$id].Seconds } else { $null }; FrameKeys=if ($clips.ContainsKey($id)) { $clips[$id].Keys } else { @() }; MotionAtlas=if ($clips.ContainsKey($id)) { 'rin/'+$id+'.png' } elseif ($id -eq 'trot') { 'rin/trot-v2.png' } elseif ($_.Groups['kind'].Value -eq 'move') { 'rin/walk.png' } else { $null } }
}
$sceneSource=Get-Content -LiteralPath (Join-Path $projectRoot 'src/Scenes.cs') -Raw
$objects = [regex]::Matches(($definitions+$sceneSource), 'new\("(?<id>[^"]+)",\s*"(?<name>[^"]+)",\s*(?<day>\d+),\s*(?<tile>-?\d+),\s*(?<w>\d+),\s*(?<h>\d+),\s*(?<x>\d+),\s*(?<y>\d+)\)') | ForEach-Object {
    [ordered]@{ Id=$_.Groups['id'].Value; Name=$_.Groups['name'].Value; Day=[int]$_.Groups['day'].Value; Tile=[int]$_.Groups['tile'].Value; MaxWidth=[int]$_.Groups['w'].Value; MaxHeight=[int]$_.Groups['h'].Value; X=[int]$_.Groups['x'].Value; FeetY=[int]$_.Groups['y'].Value; Anchor=@(.5,1); Hit='cropped sprite alpha > 48 within scaled rectangle' }
}
if ($actions.Count -ne 35 -or $objects.Count -ne 20) { throw '动作/物件源定义发生变化，请更新导出格式。' }
$nurtureSource=Get-Content -LiteralPath (Join-Path $projectRoot 'src/Nurture.cs') -Raw
$nurtureSeconds=@{}
foreach($match in [regex]::Matches($nurtureSource,'(?<name>LowerSeconds|RestSeconds|RiseSeconds)=(?<seconds>[\d.]+)')) { $nurtureSeconds[$match.Groups['name'].Value]=[double]::Parse($match.Groups['seconds'].Value,[Globalization.CultureInfo]::InvariantCulture) }
if ($nurtureSeconds.Count -ne 3) { throw 'Nurture时间轴导出失败。' }
$nurtureEntry=$actions | Where-Object { $_.Id -eq 'nurtureHug' }
$nurtureEntry['PoseFrames']=@(0..19)
$nurtureEntry['MotionAtlas']=$resourceManifest.NurtureAtlas
$nurtureEntry['ClipSeconds']=[Math]::Round($nurtureSeconds.LowerSeconds+$nurtureSeconds.RestSeconds+$nurtureSeconds.RiseSeconds,3)
$nurtureEntry['FrameKeys']=@((0..9 | ForEach-Object { [Math]::Round($_*$nurtureSeconds.LowerSeconds/10,4) })+(0..9 | ForEach-Object { [Math]::Round($nurtureSeconds.LowerSeconds+$nurtureSeconds.RestSeconds+$_*$nurtureSeconds.RiseSeconds/10,4) }))
[ordered]@{ Version=5; Nurture=@{Source="src/Nurture.cs";ManualOnly=$true;Silent=$true;LowerSeconds=$nurtureSeconds.LowerSeconds;RestSeconds=$nurtureSeconds.RestSeconds;RiseSeconds=$nurtureSeconds.RiseSeconds;FrameOrigins=$resourceManifest.NurtureAnchors;StandingReferences=$resourceManifest.NurtureStandingHeights;Contacts=$resourceManifest.NurtureContacts;GrassAtlas=$resourceManifest.NurtureGrassAtlas;GrassRegions=$resourceManifest.NurtureGrassRegions;ClipOrigin="start of lowering; variable footsteps and tablet stow are outside clip"}; Scenes=@(@{Id='cloud';Name='云台';Day=0},@{Id='indoor';Name='屋内';Day=2},@{Id='outdoor';Name='屋外院子';Day=7},@{Id='meadow';Name='室外草坡';Day=10},@{Id='swingTree';Name='树下秋千';Day=20}); Swing=@{BoardingFrames=20;LoopFrames=18;LoopKeys=34;CycleSeconds=4.6;HipAnchors=$resourceManifest.SwingHips;Hands=$resourceManifest.SwingHands}; Memory=@{StorySeconds=25;FadeSeconds=1.5;TemporaryLayout=$true;Silent=$true;UserTriggered=$true}; Source='src/Models.cs'; ClipSource='src/MotionClips.cs'; LogicalSize=@(480,340); ActorStandingHeight=150; LayerSplit=.82; SeatedHipFraction=.56; BenchSeatAnchor=$resourceManifest.BenchSeatAnchor; SeatedContactAnchor=$resourceManifest.SeatedContactAnchor; MotionRegistration='walking head/per-row ground; hop and turn individual sandal anchors in manifest'; MovementTiming='distance-based steps, eased approach, .35-second settle before context; hop airborne-only travel, turn in place'; PoseCrossfadeSeconds=.25; SequenceCrossfadeSeconds=.08; SequenceInterpolation='single opaque registered pose frame; continuous root and lift; premultiplied RGBA action transitions'; CreationEventFraction=.58; CreationFadeSeconds=2.5; Animations=@($actions); Objects=@($objects) } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $projectRoot 'assets/catalog.json') -Encoding utf8
Write-Output '已导出35组动作、20个景物和5个场景。'
