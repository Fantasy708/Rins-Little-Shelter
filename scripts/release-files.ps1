# 公开源码与便携包共用的文件清单。原始日志、存档和官网下载参考图留在本机。
function Get-ShelterPublicFiles([string]$Root) {
    foreach ($directory in @('src','tests','scripts','.github','LICENSES')) {
        Get-ChildItem -LiteralPath (Join-Path $Root $directory) -Recurse -File |
            Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' }
    }
    foreach ($directory in @('assets/rin','assets/world','docs/releases')) {
        Get-ChildItem -LiteralPath (Join-Path $Root $directory) -Recurse -File
    }
    foreach ($relative in @(
        'README.md','design.md','AGENTS.md','global.json','.gitignore','.gitattributes',
        'CREDITS.md','THIRD-PARTY-NOTICES.md','assets/manifest.json','assets/catalog.json',
        'assets/reference/README.md','docs/开发说明.md','docs/素材制作.md',
        'docs/验证记录.md','docs/发布状态.md','docs/多场景素材制作.md',
        'docs/screenshots/bench-seat-on-white.png','docs/screenshots/hop-animation.gif',
        'docs/screenshots/turn-animation.gif','docs/screenshots/opacity-before-after.png',
        'docs/screenshots/seated-swing-preview.png','docs/screenshots/trot-preview.png',
        'docs/screenshots/swing-animation.gif','docs/screenshots/scenes-meadow.png',
        'docs/screenshots/scenes-indoor.png','docs/screenshots/swing-memory.png','docs/screenshots/indoor-memory.png',
        'docs/screenshots/swing-runtime.png'
    )) { Get-Item -LiteralPath (Join-Path $Root $relative) -ErrorAction Stop }
    if (Test-Path -LiteralPath (Join-Path $Root 'LICENSE')) {
        Get-Item -LiteralPath (Join-Path $Root 'LICENSE')
    }
}

function Write-ShelterZip([string]$Directory, [string]$Destination) {
    Add-Type -AssemblyName System.IO.Compression
    $stream = [System.IO.File]::Open($Destination, [System.IO.FileMode]::Create)
    $archive = [System.IO.Compression.ZipArchive]::new($stream, [System.IO.Compression.ZipArchiveMode]::Create)
    try {
        $prefix = (Split-Path $Directory -Leaf) + '/'
        foreach ($file in Get-ChildItem -LiteralPath $Directory -Recurse -File -Force) {
            $relative = [System.IO.Path]::GetRelativePath($Directory, $file.FullName).Replace('\','/')
            $entry = $archive.CreateEntry($prefix + $relative, [System.IO.Compression.CompressionLevel]::Optimal)
            $entryStream = $entry.Open()
            $inputStream = [System.IO.File]::OpenRead($file.FullName)
            try { $inputStream.CopyTo($entryStream) }
            finally { $inputStream.Dispose(); $entryStream.Dispose() }
        }
    } finally { $archive.Dispose(); $stream.Dispose() }
}
