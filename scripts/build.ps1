param([switch]$SkipTests, [string]$OutputDirectory = 'dist/local/v1.1.0-maintenance-20261025/RinsLittleShelter')
$ErrorActionPreference = 'Stop'
$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
. (Join-Path $PSScriptRoot 'release-files.ps1')
Push-Location -LiteralPath $projectRoot
try {
    $dotnetPath = Join-Path $projectRoot '.tools\dotnet\dotnet.exe'
    if (-not (Test-Path -LiteralPath $dotnetPath)) { $dotnetPath = (Get-Command dotnet -ErrorAction Stop).Source }
    $env:DOTNET_CLI_HOME = Join-Path $projectRoot '.tools\cli-home'
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
    $buildOutput = Join-Path $projectRoot 'output\build-script\'
    & (Join-Path $PSScriptRoot 'export-catalog.ps1')
    foreach ($json in @('assets/manifest.json','assets/catalog.json')) {
        $path = Join-Path $projectRoot $json
        [System.IO.File]::WriteAllText($path,[System.IO.File]::ReadAllText($path).Replace("`r`n","`n"),[System.Text.UTF8Encoding]::new($false))
    }
    if (-not $SkipTests) {
        & $dotnetPath run --project tests/CoreTests.csproj -c Release "-p:BaseOutputPath=$buildOutput"
        if ($LASTEXITCODE -ne 0) { throw '核心检查失败。' }
        & $dotnetPath run --project tests/SceneTests.csproj -c Release "-p:BaseOutputPath=$buildOutput"
        if ($LASTEXITCODE -ne 0) { throw '多场景和记忆检查失败。' }
        & $dotnetPath run --project tests/OutdoorTests.csproj -c Release "-p:BaseOutputPath=$buildOutput"
        if ($LASTEXITCODE -ne 0) { throw '山脉/极地成长和动作逻辑检查失败。' }
        & $dotnetPath run --project tests/ClimbContinuityTests.csproj -c Release "-p:BaseOutputPath=$buildOutput" -- output/climb-continuity-qa
        if ($LASTEXITCODE -ne 0) { throw '登山交替步态/体型/连续移动检查失败。' }
        & $dotnetPath run --project tests/TabletTests.csproj -c Release "-p:BaseOutputPath=$buildOutput"
        if ($LASTEXITCODE -ne 0) { throw '平板/来信逻辑检查失败。' }
        & $dotnetPath run --project tests/NurtureTests.csproj -c Release "-p:BaseOutputPath=$buildOutput"
        if ($LASTEXITCODE -ne 0) { throw 'Nurture草地彩蛋逻辑检查失败。' }
        & $dotnetPath run --project tests/ActionRecoveryTests.csproj -c Release "-p:BaseOutputPath=$buildOutput"
        if ($LASTEXITCODE -ne 0) { throw '动作恢复和最新请求调度检查失败。' }
        & $dotnetPath run --project tests/AllActionBridgeTests.csproj -c Release "-p:BaseOutputPath=$buildOutput" -- output/all-action-bridge-qa
        if ($LASTEXITCODE -ne 0) { throw '38组动作进入/回站/时钟/最新请求检查失败。' }
        & $dotnetPath run --project tests/ActorPoseWarpTests.csproj -c Release "-p:BaseOutputPath=$buildOutput" -- output/actor-pose-warp-qa
        if ($LASTEXITCODE -ne 0) { throw '完整人物相邻姿态变形/透明轮廓检查失败。' }
        & $dotnetPath run --project tests/ActionBridgeVisualTests.csproj -c Release "-p:BaseOutputPath=$buildOutput" -- output/action-bridge-visual-qa
        if ($LASTEXITCODE -ne 0) { throw '动作起止专用姿态/生产绘图/离屏缩放检查失败。' }
        & $dotnetPath run --project tests/ActionTransitionTests.csproj -c Release "-p:BaseOutputPath=$buildOutput" -- output/action-transition-qa
        if ($LASTEXITCODE -ne 0) { throw '动作交接/实际像素/三档DPI检查失败。' }
        & $dotnetPath run --project tests/MotionScaleTests.csproj -c Release "-p:BaseOutputPath=$buildOutput" -- output/motion-scale-qa
        if ($LASTEXITCODE -ne 0) { throw '小跑/轻跃/转身体型和落脚检查失败。' }
        & $dotnetPath run --project tests/MotionExitTests.csproj -c Release "-p:BaseOutputPath=$buildOutput"
        if ($LASTEXITCODE -ne 0) { throw '动作收尾/请求替换/中断检查失败。' }
        & $dotnetPath run --project tests/MotionExitVisualTests.csproj -c Release "-p:BaseOutputPath=$buildOutput" -- output/motion-exit-qa
        if ($LASTEXITCODE -ne 0) { throw '动作收尾完整姿势/落脚/三档DPI检查失败。' }
        & $dotnetPath run --project tests/ActionRecoveryWindowTests.csproj -c Release "-p:BaseOutputPath=$buildOutput" -- output/action-recovery-window-qa
        if ($LASTEXITCODE -ne 0) { throw '窗口冻结/恢复与动作接续检查失败。' }
        & $dotnetPath run --project tests/VisualTests.csproj -c Release "-p:BaseOutputPath=$buildOutput" -- output/visual-qa
        if ($LASTEXITCODE -ne 0) { throw '画面检查失败。' }
        & $dotnetPath run --project tests/SceneVisualTests.csproj -c Release "-p:BaseOutputPath=$buildOutput" -- output/scene-qa
        if ($LASTEXITCODE -ne 0) { throw '多场景画面检查失败。' }
        & $dotnetPath run --project tests/OutdoorVisualTests.csproj -c Release "-p:BaseOutputPath=$buildOutput" -- output/outdoor-visual-qa
        if ($LASTEXITCODE -ne 0) { throw '山脉/极地绘图和落脚检查失败。' }
        & $dotnetPath run --project tests/OutdoorWindowTests.csproj -c Release "-p:BaseOutputPath=$buildOutput" -- output/outdoor-window-qa
        if ($LASTEXITCODE -ne 0) { throw '户外动作窗口集成检查失败。' }
        & $dotnetPath run --project tests/TabletVisualTests.csproj -c Release "-p:BaseOutputPath=$buildOutput" -- output/tablet-visual-qa
        if ($LASTEXITCODE -ne 0) { throw '平板绘图/文字/命中检查失败。' }
        & $dotnetPath run --project tests/TabletWindowTests.csproj -c Release "-p:BaseOutputPath=$buildOutput" -- output/tablet-window-qa
        if ($LASTEXITCODE -ne 0) { throw '平板生产窗口集成检查失败。' }
        & $dotnetPath run --project tests/CreationVisualTests.csproj -c Release "-p:BaseOutputPath=$buildOutput" -- output/creation-visual-qa
        if ($LASTEXITCODE -ne 0) { throw '白色粒子创造/节点/中断/命中检查失败。' }
        & $dotnetPath run --project tests/CreationMaterialTests.csproj -c Release "-p:BaseOutputPath=$buildOutput" -- output/creation-material-qa
        if ($LASTEXITCODE -ne 0) { throw '白色形体/局部显色/透明边缘/缓存检查失败。' }
        & $dotnetPath run --project tests/NurtureVisualTests.csproj -c Release "-p:BaseOutputPath=$buildOutput" -- output/nurture-visual-qa
        if ($LASTEXITCODE -ne 0) { throw 'Nurture草地/动作绘图检查失败。' }
        & $dotnetPath run --project tests/StartupTests.csproj -c Release "-p:BaseOutputPath=$buildOutput" -- output/startup-qa
        if ($LASTEXITCODE -ne 0) { throw '开场时序/存档/中断/绘图检查失败。' }
        & $dotnetPath run --project tests/AudioTests.csproj -c Release "-p:BaseOutputPath=$buildOutput"
        if ($LASTEXITCODE -ne 0) { throw '声音启停检查失败。' }
        & $dotnetPath run --project tests/RecallTests.csproj -c Release "-p:BaseOutputPath=$buildOutput" -- output/recall-qa
        if ($LASTEXITCODE -ne 0) { throw 'Shelter创造回顾检查失败。' }
        & $dotnetPath run --project tests/WindowSettingsTests.csproj -c Release "-p:BaseOutputPath=$buildOutput" -- output/window-settings-qa
        if ($LASTEXITCODE -ne 0) { throw '窗口置顶设置检查失败。' }
        & $dotnetPath run --project tests/SessionStateTests.csproj -c Release "-p:BaseOutputPath=$buildOutput" -- output/session-state-qa
        if ($LASTEXITCODE -ne 0) { throw '启动锁屏状态、计时和恢复检查失败。' }
        & $dotnetPath run --project tests/MemoryResourceTests.csproj -c Release "-p:BaseOutputPath=$buildOutput" -- output/memory-resource-qa --verify
        if ($LASTEXITCODE -ne 0) { throw '内存分配、场景回收和托盘资源检查失败。' }
        & $dotnetPath build tests/WalkAllocationTests.csproj -c Release "-p:BaseOutputPath=$buildOutput"
        if ($LASTEXITCODE -ne 0) { throw '步行分配检查宿主编译失败。' }
        $testBinaries = Join-Path $buildOutput 'Release/net10.0-windows10.0.19041.0'
        & $dotnetPath (Join-Path $testBinaries 'WalkAllocationTests.dll') (Join-Path $testBinaries 'RinsLittleShelter.dll') output/walk-allocation-qa
        if ($LASTEXITCODE -ne 0) { throw '步行目标跟随、动作中断和变形缓冲区检查失败。' }
    }
    & $dotnetPath publish src/LittleShelter.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=false -p:IncludeNativeLibrariesForSelfExtract=true "-p:BaseOutputPath=$buildOutput" -o $OutputDirectory
    if ($LASTEXITCODE -ne 0) { throw '发布失败。请先退出正在运行的发布版本。' }
    Copy-Item -LiteralPath assets/catalog.json -Destination (Join-Path $OutputDirectory 'assets/catalog.json')
    if (-not (Test-Path -LiteralPath (Join-Path $OutputDirectory 'assets/rin/motion-registration.json'))) { throw '发布包缺少动作体型登记。' }
    $runtimeManifest = Get-Content -LiteralPath (Join-Path $OutputDirectory 'assets/manifest.json') -Raw | ConvertFrom-Json
    if ([string]::IsNullOrWhiteSpace($runtimeManifest.MotionExitAtlas)) { throw '发布清单缺少动作收尾图集路径。' }
    $exitAsset = 'assets/' + $runtimeManifest.MotionExitAtlas
    if (-not (Test-Path -LiteralPath (Join-Path $OutputDirectory $exitAsset))) { throw "发布包缺少动作收尾图集：$exitAsset" }
    if ([string]::IsNullOrWhiteSpace($runtimeManifest.ClimbRigAtlas)) { throw '发布清单缺少登山蒙皮源图路径。' }
    if ([string]::IsNullOrWhiteSpace($runtimeManifest.ClimbRegistration)) { throw '发布清单缺少登山体型登记路径。' }
    foreach ($relative in @($runtimeManifest.ClimbRigAtlas,$runtimeManifest.ClimbRegistration)) {
        $climbAsset = 'assets/' + $relative
        if (-not (Test-Path -LiteralPath (Join-Path $OutputDirectory $climbAsset))) { throw "发布包缺少登山动作素材：$climbAsset" }
    }
    foreach ($relative in @('assets/rin/pose-bridges.json','assets/rin/action-bridge-art.json','assets/rin/bridge-ground-v1.png','assets/rin/bridge-seat-v1.png','assets/rin/bridge-upper-v1.png')) {
        if (-not (Test-Path -LiteralPath (Join-Path $OutputDirectory $relative))) { throw "发布包缺少动作起止素材或登记：$relative" }
    }
    foreach ($icon in @('assets/app/rin-shelter.png','assets/app/rin-shelter.ico')) {
        if (-not (Test-Path -LiteralPath (Join-Path $OutputDirectory $icon))) { throw "发布包缺少应用图标：$icon" }
    }
    foreach ($asset in @('assets\manifest.json','assets\rin\poses.png','assets\rin\walk.png','assets\rin\trot-v2.png','assets\rin\hop.png','assets\rin\turn.png','assets\world\objects.png','assets\world\scenes.png','assets\world\swing-tree.png','assets\world\bedroom.png','assets\rin\swing-life.png','assets\rin\swing-loop.png','assets\rin\family-memory.png','assets\rin\tablet-life.png','assets\rin\nurture-life.png','assets\world\nurture-grass.png')) {
        if (-not (Test-Path -LiteralPath (Join-Path $OutputDirectory $asset))) { throw "发布包缺少素材：$asset" }
    }
    Copy-Item -LiteralPath README.md -Destination (Join-Path $OutputDirectory '使用说明.md')
    foreach($asset in @('assets/rin/mountain-life.png','assets/rin/polar-life.png','assets/world/mountain.png','assets/world/polar.png','assets/world/polar-aurora.png')) {
        if(-not(Test-Path -LiteralPath (Join-Path $OutputDirectory $asset))) { throw "发布包缺少户外探索素材：$asset" }
    }
    foreach ($document in @('design.md','AGENTS.md','CREDITS.md','THIRD-PARTY-NOTICES.md')) { Copy-Item -LiteralPath $document -Destination $OutputDirectory }
    if (Test-Path -LiteralPath LICENSE) { Copy-Item -LiteralPath LICENSE -Destination $OutputDirectory }
    Copy-Item -LiteralPath LICENSES -Destination $OutputDirectory -Recurse -Force
    $docsDirectory = Join-Path $OutputDirectory 'docs'
    New-Item -ItemType Directory -Path $docsDirectory -Force | Out-Null
    foreach ($document in @('开发说明.md','开发日志.md','素材制作.md','验证记录.md','发布状态.md','内存风险排查.md','安装说明.md','多场景素材制作.md','平板创造交互设计.md','平板消息界面设计.md','Nurture素材制作.md','开场动画设计.md','动作收尾过渡.md','登山动作修复.md','动作起止过渡.md','动作过渡检查表.md','动作过渡素材制作记录.md')) {
        Copy-Item -LiteralPath (Join-Path 'docs' $document) -Destination $docsDirectory
    }
    if (Test-Path -LiteralPath 'docs/平板参考核对.md') { Copy-Item -LiteralPath 'docs/平板参考核对.md' -Destination $docsDirectory }
    Copy-Item -LiteralPath 'docs/户外探索素材制作.md' -Destination $docsDirectory
    Copy-Item -LiteralPath 'docs/白色粒子创造设计.md' -Destination $docsDirectory
    Copy-Item -LiteralPath 'docs/Shelter创造回顾设计.md' -Destination $docsDirectory
    $localNotesDirectory = Join-Path $docsDirectory 'releases'
    New-Item -ItemType Directory -Path $localNotesDirectory -Force | Out-Null
    Copy-Item -LiteralPath 'docs/releases/v1.1.0.md' -Destination $localNotesDirectory
    $localPreviewDirectory = Join-Path $docsDirectory 'screenshots'
    New-Item -ItemType Directory -Path $localPreviewDirectory -Force | Out-Null
    Get-ShelterPublicFiles $projectRoot |
        Where-Object { $_.DirectoryName -eq (Join-Path $projectRoot 'docs/screenshots') } |
        Copy-Item -Destination $localPreviewDirectory
    Compress-Archive -LiteralPath $OutputDirectory -DestinationPath "$OutputDirectory-win-x64.zip" -Force
    Write-Output "完成：$OutputDirectory/RinsLittleShelter.exe"
} finally { Pop-Location }
