$ErrorActionPreference = 'Stop'
$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$definitions = Get-Content -LiteralPath (Join-Path $projectRoot 'src/Models.cs') -Raw
$clipSource = Get-Content -LiteralPath (Join-Path $projectRoot 'src/MotionClips.cs') -Raw
$resourceManifest = Get-Content -LiteralPath (Join-Path $projectRoot 'assets/manifest.json') -Raw | ConvertFrom-Json
$climbRegistration = Get-Content -LiteralPath (Join-Path $projectRoot ('assets/' + $resourceManifest.ClimbRegistration)) -Raw | ConvertFrom-Json
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
if ($actions.Count -ne 38 -or $objects.Count -ne 22) { throw '动作/物件源定义发生变化，请更新导出格式。' }
$scenes=@([regex]::Matches($sceneSource,'new\("(?<id>[^"]+)",\s*"(?<name>[^"]+)",\s*(?<day>\d+)\)') | ForEach-Object {
    [ordered]@{Id=$_.Groups['id'].Value;Name=$_.Groups['name'].Value;Day=[int]$_.Groups['day'].Value}
})
if($scenes.Count -ne 7){throw '场景源定义导出失败。'}
$journeySource=Get-Content -LiteralPath (Join-Path $projectRoot 'src/OutdoorJourney.cs') -Raw
$journeySeconds=@{}
foreach($match in [regex]::Matches($journeySource,'(?<name>StepSeconds|PrepareSeconds|SettleSeconds|StandSeconds|LookSeconds|PolarWatchSeconds|SnowSeconds)=(?<seconds>[\d.]+)')) {
    $journeySeconds[$match.Groups['name'].Value]=[double]::Parse($match.Groups['seconds'].Value,[Globalization.CultureInfo]::InvariantCulture)
}
if($journeySeconds.Count -ne 7){throw '户外起步/站稳/回程时间轴导出失败。'}
$outdoor=[ordered]@{
    Source='src/OutdoorJourney.cs';Renderer='src/SceneOutdoor.cs'
    ClimbRigAtlas=$resourceManifest.ClimbRigAtlas;ClimbFrames=1;ClimbRigRegion=$resourceManifest.ClimbRigRegion
    ClimbRegistration=$resourceManifest.ClimbRegistration;TemporalPhases=16
    ClimbOrigins=@($climbRegistration.Climb | ForEach-Object { ,@($_.Root) });RigJoints=$climbRegistration.RigJoints;RigSoles=$climbRegistration.RigSoles
    ClimbHalfStepSeconds=$journeySeconds.StepSeconds;ClimbPrepareSeconds=$journeySeconds.PrepareSeconds
    ClimbSettleSeconds=$journeySeconds.SettleSeconds;CanonicalStandSeconds=$journeySeconds.StandSeconds;SummitLookSeconds=$journeySeconds.LookSeconds
    ClimbEntryExitPlayback='opaque connected-raster bridge between the exact standing drawing and the current climb body; no pose crossfade'
    JourneyWalkFrames=$climbRegistration.JourneyWalk.Count;JourneySkyFrames=$climbRegistration.JourneySky.Count
    GaitPlayback='continuous two-leg hip/knee/ankle skinning with planted support and swinging other leg, shared Journey clock'
    LegacyClimbAtlas=$resourceManifest.ClimbAtlas;LegacyClimbOrigins=$resourceManifest.ClimbAnchors;LegacyClimbReferences=$resourceManifest.ClimbHeights
    LegacyClimbPlayback=$false
    PolarAtlas=$resourceManifest.PolarPoseAtlas;PolarFrames=12;PolarOrigins=$resourceManifest.PolarPoseAnchors;PolarReferences=$resourceManifest.PolarPoseHeights
    MountainImage=$resourceManifest.MountainImage;PolarImage=$resourceManifest.PolarImage;AuroraImage=$resourceManifest.AuroraImage
    Silent=$true;TemporalPolicy='single Behavior clock; continuous route phase; ordinary stop/action/tablet requests recover along the route before latest request dispatch'
    SoftReturn='finish the current ascent route, turn and visibly descend; an active descent continues; drag/context/support invalidation remains a hard cancellation'
}
$climbEntry=$actions | Where-Object {$_.Id -eq 'mountainClimb'}
$climbEntry['MotionAtlas']=$resourceManifest.ClimbRigAtlas
$climbEntry['MotionRegistration']=$resourceManifest.ClimbRegistration
$climbEntry['PoseFrames']=@()
$climbEntry['TemporalPhases']=16
$snowEntry=$actions | Where-Object {$_.Id -eq 'snowTouch'}
$snowEntry['MotionAtlas']=$resourceManifest.PolarPoseAtlas
$snowEntry['PoseFrames']=@(0..11)
$creationSource=Get-Content -LiteralPath (Join-Path $projectRoot 'src/SceneCreation.cs') -Raw
$creationNumbers=@{}
foreach($match in [regex]::Matches($creationSource,'(?<name>CreationParticleLimit|CreationGatherSeconds|CreationMaterializeSeconds|CreationShapeHoldSeconds|CreationRevealSeconds)=(?<number>[\d.]+)')) {
    $creationNumbers[$match.Groups['name'].Value]=[double]::Parse($match.Groups['number'].Value,[Globalization.CultureInfo]::InvariantCulture)
}
if($creationNumbers.Count -ne 5){throw '创造粒子有界参数导出失败。'}
$creationParticles=[ordered]@{Source='src/SceneCreation.cs';FixedPool=[int]$creationNumbers.CreationParticleLimit;GatherSeconds=$creationNumbers.CreationGatherSeconds;MaterializeSeconds=$creationNumbers.CreationMaterializeSeconds;ShapeHoldSeconds=$creationNumbers.CreationShapeHoldSeconds;RevealSeconds=$creationNumbers.CreationRevealSeconds;ShapeFirst=$true;TabletOrbit=$false;ActualEvent='PetWindow ObjectCreated -> World.Create -> Scene.Born';HitTest=$false;Persistent=$false}
$nurtureSource=Get-Content -LiteralPath (Join-Path $projectRoot 'src/Nurture.cs') -Raw
$nurtureSeconds=@{}
foreach($match in [regex]::Matches($nurtureSource,'(?<name>LowerSeconds|RestSeconds|RiseSeconds)=(?<seconds>[\d.]+)')) { $nurtureSeconds[$match.Groups['name'].Value]=[double]::Parse($match.Groups['seconds'].Value,[Globalization.CultureInfo]::InvariantCulture) }
if ($nurtureSeconds.Count -ne 3) { throw 'Nurture时间轴导出失败。' }
$nurtureEntry=$actions | Where-Object { $_.Id -eq 'nurtureHug' }
$nurtureEntry['PoseFrames']=@(0..19)
$nurtureEntry['MotionAtlas']=$resourceManifest.NurtureAtlas
$nurtureEntry['ClipSeconds']=[Math]::Round($nurtureSeconds.LowerSeconds+$nurtureSeconds.RestSeconds+$nurtureSeconds.RiseSeconds,3)
$nurtureEntry['FrameKeys']=@((0..9 | ForEach-Object { [Math]::Round($_*$nurtureSeconds.LowerSeconds/10,4) })+(0..9 | ForEach-Object { [Math]::Round($nurtureSeconds.LowerSeconds+$nurtureSeconds.RestSeconds+$_*$nurtureSeconds.RiseSeconds/10,4) }))
$motionExits=[ordered]@{
    Source='src/MotionExit.cs'; Renderer='src/SceneMotionExit.cs'; Atlas=$resourceManifest.MotionExitAtlas
    Regions=$resourceManifest.MotionExitRegions; RegistrationSource=$resourceManifest.MotionRegistration; RegistrationKey='Stops'
    Frames=24; Rows=@('trot contact variant A','trot contact variant B','hop settle','turn settle'); FramesPerRow=6
    TrotStopSeconds=.30; TurnSettleSeconds=.30; HopSettleSeconds=.22; NeutralHoldSeconds=.07; RequestedTrotBrakeSeconds=.16
    FirstKey='previous actual displayed actor; cold rendering reconstructs the frozen source clip key'
    MiddleKeys='complete authored poses with connected-raster anatomy registration; no internal crossfade'
    FinalKey='canonical basic standing pose 0 with the same root, facing and breathing as ordinary standing'
    Scheduling='one Behavior clock; latest transient request wins; repeated active action is a no-op; drag and context cancellation take priority'
    Persistent=$false; AddsCatalogActions=$false
}
$bridgeArtPath='rin/action-bridge-art.json'
$bridgeArt=Get-Content -LiteralPath (Join-Path $projectRoot ('assets/'+$bridgeArtPath)) -Raw | ConvertFrom-Json
if($bridgeArt.Version -ne 1 -or $bridgeArt.Sheets.Count -ne 3){throw '动作起止专用素材登记格式不完整。'}
$actionBridges=[ordered]@{
    Source='src/ActionBridge.cs';Renderer='src/SceneActionBridge.cs';RasterSource='src/ActorPoseWarp.cs'
    PoseLandmarks='rin/pose-bridges.json';ArtRegistration=$bridgeArtPath;CanonicalStandingPose=0
    ArtSheets=@($bridgeArt.Sheets | ForEach-Object { [ordered]@{Id=$_.Id;Path=$_.Path;Frames=$_.Frames.Count;Regions=$_.Regions} })
    CatalogActionIds=@($actions | ForEach-Object {$_.Id})
    PoseRoute='ordinary poses recover through canonical 0 before the next entry; knees via 11; lying via 11 and 6'
    OrdinarySegmentSeconds=.42;SupportedSegmentSeconds=.58;LyingSegmentSeconds=.62;LocomotionPreparationSeconds=.26
    Playback='complete authored intermediate poses from ground/seat/upper sheets; continuous opaque articulation only between adjacent registered keys; exact existing action endpoints'
    ActionClock='frozen during entry and recovery; the original hold clock and 58% creation event begin after entry'
    DedicatedOwners=@('MotionClips/MotionExit','NurtureSequence','LifeSequence','TabletSession','OutdoorJourney')
    Requests='latest transient request wins; repeating current action does not restart; hard context/drag cancellation clears stale work'
    Persistent=$false;AddsCatalogActions=$false;LocalOutput='dist/local/action-bridges/RinsLittleShelter'
}
foreach($entry in $actions) {
    $entry['EntryExitOwner']=switch($entry.Id) {
        {$_ -in @('trot','hop','turn')} {'ActionBridgeState entry; MotionClips/MotionExit authored completion';break}
        {$_ -in @('ropeHold','swingLife')} {'LifeSequence authored hold/mount/settle/dismount';break}
        'nurtureHug' {'NurtureSequence authored lower/rise';break}
        'mountainClimb' {'OutdoorJourney prepare/stand mesh and visible descent';break}
        'snowTouch' {'OutdoorJourney dedicated raise/hold/retract';break}
        'auroraWatch' {'OutdoorJourney with ActionBridgeState look entry/exit';break}
        'invite' {'ActionBridgeState for direct pose; TabletSession for interactive presentation/stow';break}
        default {'ActionBridgeState; canonical standing is reused for an identical pose'}
    }
}
[ordered]@{ Version=13; ActionBridges=$actionBridges; MotionExits=$motionExits; CreationParticles=$creationParticles; Outdoor=$outdoor; Nurture=[ordered]@{Source="src/Nurture.cs";ManualOnly=$true;Silent=$true;LowerSeconds=$nurtureSeconds.LowerSeconds;RestSeconds=$nurtureSeconds.RestSeconds;RiseSeconds=$nurtureSeconds.RiseSeconds;FrameOrigins=$resourceManifest.NurtureAnchors;StandingReferences=$resourceManifest.NurtureStandingHeights;Contacts=$resourceManifest.NurtureContacts;GrassAtlas=$resourceManifest.NurtureGrassAtlas;GrassRegions=$resourceManifest.NurtureGrassRegions;ClipOrigin="start of lowering; variable footsteps and tablet stow are outside clip"}; Scenes=$scenes; Swing=[ordered]@{BoardingFrames=20;LoopFrames=18;LegacyLoopKeys=34;CycleSeconds=4.6;BoardingSeconds=.65;Playback='continuous articulation of one opaque registered swing pose';LoopPose=0;MemoryChildPose=8;MemoryFatherPose=1;BodyScale='uniform 150/305; local arm registration only';HipAnchors=$resourceManifest.SwingHips;Hands=$resourceManifest.SwingHands}; Memory=[ordered]@{StorySeconds=25;FadeSeconds=1.5;TemporaryLayout=$true;Silent=$true;UserTriggered=$true}; Source='src/Models.cs'; ClipSource='src/MotionClips.cs'; LogicalSize=@(480,340); ActorStandingHeight=150; LayerSplit=.82; SeatedHipFraction=.56; BenchSeatAnchor=$resourceManifest.BenchSeatAnchor; SeatedContactAnchor=$resourceManifest.SeatedContactAnchor; MotionRegistration='walk retains row ground; trot/hop/turn use complete connected-raster anatomy registration; legacy Hop/TurnStandingHeights are not the playback scale'; MotionAnatomySource=$resourceManifest.MotionRegistration; MotionRegistrationSource='src/MotionPoseFrames.cs'; MovementTiming='distance-based steps, eased approach, .35-second settle before context; hop airborne-only travel, turn in place'; ActorRegistrationSource='src/ActorPoseRegistration.cs'; BasicPoseRegistration='upright 0-5/9/10 use own standing height; bent 6/7/8/11 retain pose-0 height; measured alpha support overrides legacy FeetAnchor'; TransitionSource='src/SceneActionBridge.cs'; LegacyTransitionSource='src/SceneActorTransition.cs'; PoseCrossfadeSeconds=0; LegacyPoseCrossfadeSeconds=.24; RapidClipEntryCrossfadeSeconds=0; RapidClipEntryHandoffSeconds=.06; RapidClips=@("hop","turn"); SequenceCrossfadeSeconds=0; SequenceHandoffSeconds=.24; MaxPositionHandoffSeconds=.36; PositionHandoffRule='.24 + min(.12, root distance / 600)'; DragPlayback='frozen actual grab pose; immediate pointer displacement'; GracefulExit='all ordinary postures physically recover through standing; authored trot/hop/turn/Nurture/swing/tablet exits; mountain visible return; one latest transient request'; SequenceInterpolation='complete authored opaque poses; connected-raster interpolation between adjacent real keys; continuous root and lift; exact canonical standing endpoints'; CreationEventFraction=.58; CreationFadeSeconds=$creationNumbers.CreationRevealSeconds; LegacyCreationFadeSeconds=2.5; Animations=@($actions); Objects=@($objects) } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $projectRoot 'assets/catalog.json') -Encoding utf8
Write-Output '已导出38组动作、22个景物和7个场景。'
