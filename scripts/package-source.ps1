param([string]$Destination = 'dist/RinsLittleShelter-source.zip')
$ErrorActionPreference = 'Stop'
$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
. (Join-Path $PSScriptRoot 'release-files.ps1')
$stage = Join-Path $projectRoot ('output/source-package-' + [Guid]::NewGuid().ToString('N'))
$sourceRoot = Join-Path $stage 'RinsLittleShelter-source'
New-Item -ItemType Directory -Path $sourceRoot -Force | Out-Null
Push-Location -LiteralPath $projectRoot
try {
    $files = Get-ShelterPublicFiles $projectRoot
    foreach ($file in $files) {
        $relative = [System.IO.Path]::GetRelativePath($projectRoot, $file.FullName)
        if ($relative.StartsWith('..')) { throw '源文件超出项目范围。' }
        $target = Join-Path $sourceRoot $relative
        New-Item -ItemType Directory -Path (Split-Path $target -Parent) -Force | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $target
    }
    $zipPath = [System.IO.Path]::GetFullPath((Join-Path $projectRoot $Destination))
    if (-not $zipPath.StartsWith($projectRoot + [System.IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw '打包输出必须在项目内。' }
    New-Item -ItemType Directory -Path (Split-Path $zipPath -Parent) -Force | Out-Null
    Write-ShelterZip $sourceRoot $zipPath
    Write-Output "源码与素材已打包：$Destination"
} finally { Pop-Location }

