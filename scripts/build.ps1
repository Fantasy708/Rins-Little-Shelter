param([switch]$SkipTests, [string]$OutputDirectory = 'dist/RinsLittleShelter')
$ErrorActionPreference = 'Stop'
$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Push-Location -LiteralPath $projectRoot
try {
    $dotnetPath = Join-Path $projectRoot '.tools\dotnet\dotnet.exe'
    if (-not (Test-Path -LiteralPath $dotnetPath)) { $dotnetPath = (Get-Command dotnet -ErrorAction Stop).Source }
    $env:DOTNET_CLI_HOME = Join-Path $projectRoot '.tools\cli-home'
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
    $buildOutput = Join-Path $projectRoot 'output\build-script\'
    & (Join-Path $PSScriptRoot 'export-catalog.ps1')
    if (-not $SkipTests) {
        & $dotnetPath run --project tests/CoreTests.csproj -c Release
        if ($LASTEXITCODE -ne 0) { throw '核心检查失败。' }
        & $dotnetPath run --project tests/SceneTests.csproj -c Release
        if ($LASTEXITCODE -ne 0) { throw '多场景和记忆检查失败。' }
        & $dotnetPath run --project tests/VisualTests.csproj -c Release "-p:BaseOutputPath=$buildOutput" -- output/visual-qa
        if ($LASTEXITCODE -ne 0) { throw '画面检查失败。' }
        & $dotnetPath run --project tests/SceneVisualTests.csproj -c Release "-p:BaseOutputPath=$buildOutput" -- output/scene-qa
        if ($LASTEXITCODE -ne 0) { throw '多场景画面检查失败。' }
        & $dotnetPath run --project tests/AudioTests.csproj -c Release "-p:BaseOutputPath=$buildOutput"
        if ($LASTEXITCODE -ne 0) { throw '声音启停检查失败。' }
    }
    & $dotnetPath publish src/LittleShelter.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true "-p:BaseOutputPath=$buildOutput" -o $OutputDirectory
    if ($LASTEXITCODE -ne 0) { throw '发布失败。请先退出正在运行的发布版本。' }
    Copy-Item -LiteralPath assets/catalog.json -Destination (Join-Path $OutputDirectory 'assets/catalog.json')
    foreach ($asset in @('assets\manifest.json','assets\rin\poses.png','assets\rin\walk.png','assets\rin\trot-v2.png','assets\rin\hop.png','assets\rin\turn.png','assets\world\objects.png','assets\world\scenes.png','assets\world\swing-tree.png','assets\rin\swing-life.png','assets\rin\swing-loop.png','assets\rin\family-memory.png')) {
        if (-not (Test-Path -LiteralPath (Join-Path $OutputDirectory $asset))) { throw "发布包缺少素材：$asset" }
    }
    Copy-Item -LiteralPath README.md -Destination (Join-Path $OutputDirectory '使用说明.md')
    foreach ($document in @('design.md','AGENTS.md','CREDITS.md','THIRD-PARTY-NOTICES.md')) { Copy-Item -LiteralPath $document -Destination $OutputDirectory }
    if (Test-Path -LiteralPath LICENSE) { Copy-Item -LiteralPath LICENSE -Destination $OutputDirectory }
    Copy-Item -LiteralPath LICENSES -Destination $OutputDirectory -Recurse -Force
    $docsDirectory = Join-Path $OutputDirectory 'docs'
    New-Item -ItemType Directory -Path $docsDirectory -Force | Out-Null
    foreach ($document in @('开发说明.md','素材制作.md','验证记录.md','发布状态.md','多场景素材制作.md')) {
        Copy-Item -LiteralPath (Join-Path 'docs' $document) -Destination $docsDirectory
    }
    Compress-Archive -LiteralPath $OutputDirectory -DestinationPath "$OutputDirectory-win-x64.zip" -Force
    Write-Output "完成：$OutputDirectory/RinsLittleShelter.exe"
} finally { Pop-Location }
