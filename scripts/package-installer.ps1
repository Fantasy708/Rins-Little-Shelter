param([string]$Destination='dist/local/v1.1.0/release')
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$release=[IO.Path]::GetFullPath((Join-Path $root $Destination))
if(-not $release.StartsWith($root+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw '安装脚本包必须输出到项目内。'}
$null=New-Item -ItemType Directory -Path $release -Force
Add-Type -AssemblyName System.IO.Compression
$files=@(
 @{Source='scripts/Install-RinsLittleShelter.ps1';Name='Install-RinsLittleShelter.ps1'},
 @{Source='scripts/Install-RinsLittleShelter.cmd';Name='Install-RinsLittleShelter.cmd'},
 @{Source='scripts/Uninstall-RinsLittleShelter.ps1';Name='Uninstall-RinsLittleShelter.ps1'},
 @{Source='scripts/Uninstall-RinsLittleShelter.cmd';Name='Uninstall-RinsLittleShelter.cmd'},
 @{Source='docs/安装说明.md';Name='安装说明.md'}
)
$zipPath=Join-Path $release 'RinsLittleShelter-Setup.zip'
$stream=[IO.File]::Open($zipPath,[IO.FileMode]::Create)
$archive=[IO.Compression.ZipArchive]::new($stream,[IO.Compression.ZipArchiveMode]::Create)
$sums=@()
try {
 foreach($file in $files){
  $bytes=[IO.File]::ReadAllBytes((Join-Path $root $file.Source))
  if($file.Name.EndsWith('.cmd')){$text=[Text.Encoding]::ASCII.GetString($bytes).Replace("`r`n","`n").Replace("`n","`r`n");$bytes=[Text.Encoding]::ASCII.GetBytes($text)}
  if($file.Name.EndsWith('.ps1') -and ($bytes.Length -lt 3 -or $bytes[0] -ne 0xef -or $bytes[1] -ne 0xbb -or $bytes[2] -ne 0xbf)){throw 'PowerShell脚本需UTF-8 BOM以支持Windows PowerShell5.1。'}
  $entry=$archive.CreateEntry('RinsLittleShelter-Setup/'+$file.Name,[IO.Compression.CompressionLevel]::Optimal)
  $output=$entry.Open();try{$output.Write($bytes,0,$bytes.Length)}finally{$output.Dispose()}
  $sha=[Security.Cryptography.SHA256]::Create();try{$hash=[BitConverter]::ToString($sha.ComputeHash($bytes)).Replace('-','')}finally{$sha.Dispose()}
  $sums+=($hash+'  '+$file.Name)
 }
 $entry=$archive.CreateEntry('RinsLittleShelter-Setup/CHECKSUMS.txt')
 $bytes=[Text.UTF8Encoding]::new($false).GetBytes(($sums -join "`n")+"`n")
 $output=$entry.Open();try{$output.Write($bytes,0,$bytes.Length)}finally{$output.Dispose()}
} finally {$archive.Dispose();$stream.Dispose()}
$scriptCopy=Join-Path $release 'Install-RinsLittleShelter.ps1'
Copy-Item -LiteralPath (Join-Path $root 'scripts/Install-RinsLittleShelter.ps1') -Destination $scriptCopy -Force
$sums=@(((Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash+'  RinsLittleShelter-Setup.zip'),
 ((Get-FileHash -LiteralPath $scriptCopy -Algorithm SHA256).Hash+'  Install-RinsLittleShelter.ps1'))
foreach($name in @('Uninstall-RinsLittleShelter.ps1','Uninstall-RinsLittleShelter.cmd')){
 $target=Join-Path $release $name
 Copy-Item -LiteralPath (Join-Path $root ('scripts/'+$name)) -Destination $target -Force
 $sums+=((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash+'  '+$name)
}
[IO.File]::WriteAllText((Join-Path $release 'SHA256-Setup.txt'),($sums -join "`n")+"`n",[Text.UTF8Encoding]::new($false))
Write-Output "安装脚本包已准备：$zipPath"
