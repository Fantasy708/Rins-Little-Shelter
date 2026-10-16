param(
    [string]$Version = 'v1.0.0',
    [string]$ProgramDirectory = 'dist/local/v1.0.0/RinsLittleShelter',
    [string]$ReleaseDirectory = ''
)
$ErrorActionPreference = 'Stop'
$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
. (Join-Path $PSScriptRoot 'release-files.ps1')
if ($Version -notmatch '^[A-Za-z0-9][A-Za-z0-9.\-]*$') { throw '版本号只能含英文字母、数字、点和连字符。' }
$programRoot = [System.IO.Path]::GetFullPath((Join-Path $projectRoot $ProgramDirectory))
$stage = Join-Path $projectRoot ('output/release-package-' + [Guid]::NewGuid().ToString('N'))
$portableRoot = Join-Path $stage 'RinsLittleShelter'
$releaseRoot = [System.IO.Path]::GetFullPath((Join-Path $projectRoot $(if ($ReleaseDirectory.Length -gt 0) { $ReleaseDirectory } else { 'dist/local/' + $Version + '/release' })))
if (-not $releaseRoot.StartsWith($projectRoot+[System.IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw '版本包输出必须在项目内。' }
New-Item -ItemType Directory -Path $portableRoot,$releaseRoot -Force | Out-Null

# 复用已检查的EXE和原始PNG，不重新编译或改变程序哈希。
$runtimeFiles = @('RinsLittleShelter.exe','assets/manifest.json','assets/catalog.json',
    'assets/rin/poses.png','assets/rin/walk.png','assets/rin/trot-v2.png',
    'assets/rin/hop.png','assets/rin/turn.png','assets/world/objects.png',
    'assets/rin/swing-life.png','assets/rin/swing-loop.png','assets/rin/family-memory.png','assets/rin/tablet-life.png','assets/rin/nurture-life.png','assets/world/nurture-grass.png',
    'assets/world/scenes.png','assets/world/swing-tree.png','assets/world/bedroom.png',
    'assets/rin/mountain-life.png','assets/rin/polar-life.png','assets/world/mountain.png','assets/world/polar.png','assets/world/polar-aurora.png',
    'assets/app/rin-shelter.png','assets/app/rin-shelter.ico')
foreach ($relative in $runtimeFiles) {
    $target = Join-Path $portableRoot $relative
    New-Item -ItemType Directory -Path (Split-Path $target -Parent) -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $programRoot $relative) -Destination $target
}
foreach ($relative in @('README.md','design.md','AGENTS.md','CREDITS.md','THIRD-PARTY-NOTICES.md')) {
    Copy-Item -LiteralPath (Join-Path $projectRoot $relative) -Destination $portableRoot
}
Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md') -Destination (Join-Path $portableRoot '使用说明.md')
if (Test-Path -LiteralPath (Join-Path $projectRoot 'LICENSE')) {
    Copy-Item -LiteralPath (Join-Path $projectRoot 'LICENSE') -Destination $portableRoot
}
Copy-Item -LiteralPath (Join-Path $projectRoot 'LICENSES') -Destination $portableRoot -Recurse
$publicDocs = Join-Path $portableRoot 'docs'
New-Item -ItemType Directory -Path $publicDocs -Force | Out-Null
foreach ($relative in @('docs/开发说明.md','docs/素材制作.md','docs/验证记录.md','docs/发布状态.md','docs/多场景素材制作.md',
    'docs/平板创造交互设计.md','docs/平板消息界面设计.md','docs/开发日志.md','docs/Nurture素材制作.md','docs/开场动画设计.md')) {
    Copy-Item -LiteralPath (Join-Path $projectRoot $relative) -Destination $publicDocs
}
if (Test-Path -LiteralPath (Join-Path $projectRoot 'docs/平板参考核对.md')) {
    Copy-Item -LiteralPath (Join-Path $projectRoot 'docs/平板参考核对.md') -Destination $publicDocs
}
Copy-Item -LiteralPath (Join-Path $projectRoot 'docs/户外探索素材制作.md') -Destination $publicDocs
Copy-Item -LiteralPath (Join-Path $projectRoot 'docs/白色粒子创造设计.md') -Destination $publicDocs
Copy-Item -LiteralPath (Join-Path $projectRoot 'docs/Shelter创造回顾设计.md') -Destination $publicDocs
Copy-Item -LiteralPath (Join-Path $projectRoot 'docs/releases') -Destination $publicDocs -Recurse
New-Item -ItemType Directory -Path (Join-Path $publicDocs 'screenshots') -Force | Out-Null
Get-ShelterPublicFiles $projectRoot | Where-Object { $_.DirectoryName -eq (Join-Path $projectRoot 'docs/screenshots') } |
    Copy-Item -Destination (Join-Path $publicDocs 'screenshots')

$portableZip = Join-Path $releaseRoot "RinsLittleShelter-$Version-win-x64.zip"
$sourceZip = Join-Path $releaseRoot "RinsLittleShelter-$Version-source.zip"
Write-ShelterZip $portableRoot $portableZip
& (Join-Path $PSScriptRoot 'package-source.ps1') -Destination ([System.IO.Path]::GetRelativePath($projectRoot, $sourceZip))
$hashLines = @(
    ((Get-FileHash -LiteralPath (Join-Path $portableRoot 'RinsLittleShelter.exe') -Algorithm SHA256).Hash + '  RinsLittleShelter/RinsLittleShelter.exe'),
    ((Get-FileHash -LiteralPath $portableZip -Algorithm SHA256).Hash + '  ' + (Split-Path $portableZip -Leaf)),
    ((Get-FileHash -LiteralPath $sourceZip -Algorithm SHA256).Hash + '  ' + (Split-Path $sourceZip -Leaf))
)
[System.IO.File]::WriteAllLines((Join-Path $releaseRoot 'SHA256.txt'), $hashLines, [System.Text.UTF8Encoding]::new($false))
Copy-Item -LiteralPath (Join-Path $projectRoot "docs/releases/$Version.md") -Destination (Join-Path $releaseRoot 'RELEASE_NOTES.md')
Write-Output "本地版本附件已准备：$releaseRoot（本脚本不上传或发布到GitHub）"
