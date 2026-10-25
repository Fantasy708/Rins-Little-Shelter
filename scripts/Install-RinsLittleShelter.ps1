#requires -Version 5.1
[CmdletBinding()]
param(
    [ValidateSet('Install','Uninstall')][string]$Action = 'Install',
    [string]$InstallDirectory = '',
    [string]$Version = '',
    [string]$PackagePath = '',
    [string]$ChecksumPath = '',
    [switch]$NoShortcuts,
    [switch]$NoLaunch,
    [string]$DesktopDirectory = '',
    [string]$StartMenuDirectory = ''
)

$script:RlsSetupSource = $PSCommandPath
$script:RlsAppId = 'Fantasy708.RinsLittleShelter'
$script:RlsMarker = '.rin-install.json'
$script:RlsPortableMarker = '.rin-portable.json'

function Get-RlsHash([string]$Path) { return (Get-FileHash -LiteralPath $Path -Algorithm SHA256 -ErrorAction Stop).Hash.ToLowerInvariant() }
function Write-RlsJson([string]$Path, $Value) {
    [IO.File]::WriteAllText($Path, ($Value | ConvertTo-Json -Depth 15), [Text.UTF8Encoding]::new($false))
}
function Assert-RlsNoLinks([string]$Path) {
    $current = [IO.Path]::GetFullPath($Path)
    while ($current) {
        $item = Get-Item -LiteralPath $current -Force -ErrorAction SilentlyContinue
        if ($null -ne $item -and ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw "目录含链接，未作更改：$current" }
        $parent = Split-Path $current -Parent
        if ($parent -eq $current) { break }; $current = $parent
    }
}
function Get-RlsTree([string]$Directory) {
    Assert-RlsNoLinks $Directory
    $pending = [Collections.Generic.Stack[string]]::new(); $pending.Push($Directory)
    while ($pending.Count) {
        foreach ($item in @(Get-ChildItem -LiteralPath $pending.Pop() -Force -ErrorAction Stop)) {
            if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "目录含链接，未作更改：$($item.FullName)" }
            Write-Output $item
            if ($item.PSIsContainer) { $pending.Push($item.FullName) }
        }
    }
}
function Get-RlsChild([string]$Root, [string]$Relative) {
    if ([string]::IsNullOrWhiteSpace($Relative) -or $Relative -match '[\\:\x00-\x1f*?"<>|]' -or $Relative.StartsWith('/')) { throw "不安全的相对路径：$Relative" }
    foreach ($part in $Relative.Split('/')) {
        if (-not $part -or $part -in @('.','..') -or $part.EndsWith('.') -or $part.EndsWith(' ') -or $part -match '^(?i:CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|$)') { throw "不安全的文件名：$Relative" }
    }
    $base = [IO.Path]::GetFullPath($Root).TrimEnd('\')
    $path = [IO.Path]::GetFullPath((Join-Path $base $Relative))
    if (-not $path.StartsWith($base + '\', [StringComparison]::OrdinalIgnoreCase)) { throw '文件超出安装目录。' }
    return $path
}
function Get-RlsInstallPath([string]$Directory, [string]$RequestedAction) {
    if (-not $Directory) {
        if ($RequestedAction -eq 'Uninstall' -and (Split-Path (Split-Path $script:RlsSetupSource -Parent) -Leaf) -eq '.setup') {
            $Directory = Split-Path (Split-Path $script:RlsSetupSource -Parent) -Parent
        } else { $Directory = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Programs\RinsLittleShelter' }
    }
    $path = [IO.Path]::GetFullPath($Directory).TrimEnd('\')
    $data = [IO.Path]::GetFullPath((Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'RinsLittleShelter')).TrimEnd('\')
    if ($path.StartsWith('\\') -or $path.Length -lt 5 -or $path -eq [IO.Path]::GetPathRoot($path).TrimEnd('\') -or
        $path.Equals($data,[StringComparison]::OrdinalIgnoreCase) -or $path.StartsWith($data+'\',[StringComparison]::OrdinalIgnoreCase) -or
        $data.StartsWith($path+'\',[StringComparison]::OrdinalIgnoreCase)) { throw '请选择独立的本地程序目录，不能使用存档目录或磁盘根目录。' }
    Assert-RlsNoLinks $path
    return $path
}
function Assert-RlsPlatform {
    $arch = if ($env:PROCESSOR_ARCHITEW6432) { $env:PROCESSOR_ARCHITEW6432 } else { $env:PROCESSOR_ARCHITECTURE }
    $build = [int](Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion' -Name CurrentBuildNumber -ErrorAction Stop).CurrentBuildNumber
    if ($arch -ne 'AMD64' -or $build -lt 19041) { throw '需要 Windows 10（19041及以上）或 Windows 11 x64。' }
}
function Assert-RlsNotRunning([string]$Directory) {
    $exe = Join-Path $Directory 'RinsLittleShelter.exe'
    foreach ($process in @(Get-Process RinsLittleShelter -ErrorAction SilentlyContinue)) {
        try { $path = $process.Path } catch { throw '无法确认旧程序状态，请先从托盘退出 Rin 后重试。' }
        if (-not $path) { throw '无法确认旧程序状态，请先从托盘退出 Rin 后重试。' }
        if ($path.Equals($exe,[StringComparison]::OrdinalIgnoreCase)) { throw '请先从系统托盘正常退出这个程序目录中的 Rin，再重试。' }
    }
}
function Read-RlsManifest([string]$Directory) {
    $marker = $script:RlsMarker
    $path = Join-Path $Directory $marker
    $portable = $false
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        $marker = $script:RlsPortableMarker
        $path = Join-Path $Directory $marker
        $portable = $true
    }
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw '此目录没有本安装脚本的登记文件，未覆盖或删除现有内容。' }
    Assert-RlsNoLinks $path
    $m = Get-Content -LiteralPath $path -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($m.AppId -ne $script:RlsAppId -or $m.FormatVersion -ne 1 -or -not $m.Files) { throw '安装登记无效，未作更改。' }
    if ($portable) {
        if ($m.PackageKind -ne 'Portable' -or $m.Root -ne '.' -or @($m.Shortcuts).Count -ne 0) { throw '便携登记无效，未作更改。' }
    } elseif ($m.InstallDirectory -ne $Directory) { throw '安装登记无效，未作更改。' }
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($file in $m.Files) {
        $null = Get-RlsChild $Directory $file.Path
        if (-not $seen.Add([string]$file.Path) -or $file.SHA256 -notmatch '^[0-9a-fA-F]{64}$' -or $file.Bytes -lt 0) { throw '安装文件清单无效。' }
        if ($file.Path -in @($script:RlsMarker,$script:RlsPortableMarker)) { throw '登记文件不能列为程序文件。' }
        if (($file.Path.Split('/')[-1]) -match '^shelter\.json(?:\.(?:bak|tmp))?$') { throw '存档不能列为卸载文件，未作更改。' }
    }
    if (-not $seen.Contains('RinsLittleShelter.exe')) { throw '安装文件清单缺少主程序，未作更改。' }
    $m | Add-Member -NotePropertyName ResolvedMarker -NotePropertyValue $marker -Force
    return $m
}
function Remove-RlsOwnedTree([string]$Path, [string]$Parent) {
    $pathFull = [IO.Path]::GetFullPath($Path)
    $parentFull = [IO.Path]::GetFullPath($Parent).TrimEnd('\')
    if (-not $pathFull.StartsWith($parentFull+'\',[StringComparison]::OrdinalIgnoreCase) -or (Split-Path $pathFull -Leaf) -notmatch '^\.rins-(work|backup)-[0-9a-f]{32}$') { throw '临时目录不在预期范围内。' }
    if (Test-Path -LiteralPath $pathFull) {
        Assert-RlsNoLinks $pathFull
        $null = @(Get-RlsTree $pathFull)
        Remove-Item -LiteralPath $pathFull -Recurse -Force -ErrorAction Stop
    }
}
function Move-RlsDirectory([string]$From, [string]$To) { Move-Item -LiteralPath $From -Destination $To -ErrorAction Stop }
function Get-RlsRelease([string]$RequestedVersion) {
    $route = 'latest'
    if ($RequestedVersion) {
        if ($RequestedVersion -notmatch '^v\d+\.\d+\.\d+$') { throw '版本号应为 v1.1.0 这样的正式版号。' }
        $route = 'tags/' + $RequestedVersion
    }
    [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
    $response = Invoke-WebRequest -Uri ('https://api.github.com/repos/Fantasy708/Rins-Little-Shelter/releases/'+$route) -UseBasicParsing -TimeoutSec 60 -Headers @{'User-Agent'='RinsLittleShelter-Setup';Accept='application/vnd.github+json'}
    $release = $response.Content | ConvertFrom-Json
    if ($release.draft -or $release.prerelease -or $release.tag_name -notmatch '^v\d+\.\d+\.\d+$' -or ($RequestedVersion -and $release.tag_name -ne $RequestedVersion)) { throw '服务器未返回预期的正式发行。' }
    $name = 'RinsLittleShelter-'+$release.tag_name+'-win-x64.zip'
    $zip = @($release.assets | Where-Object { $_.name -ceq $name -and $_.state -eq 'uploaded' })
    $sum = @($release.assets | Where-Object { $_.name -ceq 'SHA256.txt' -and $_.state -eq 'uploaded' })
    if ($zip.Count -ne 1 -or $sum.Count -ne 1) { throw '此发行缺少完整便携包或唯一的 SHA256 校验文件。' }
    foreach ($asset in @($zip[0],$sum[0])) {
        $expected = 'https://github.com/Fantasy708/Rins-Little-Shelter/releases/download/'+$release.tag_name+'/'+$asset.name
        if ($asset.browser_download_url -cne $expected) { throw '发行下载地址不属于预期仓库。' }
    }
    return @{Version=[string]$release.tag_name;Name=$name;Zip=$zip[0];Checksum=$sum[0]}
}
function Get-RlsChecksum([string]$File, [string]$Name) {
    if ((Get-Item -LiteralPath $File).Length -gt 1MB) { throw '校验清单过大。' }
    $found = @()
    foreach ($line in @(Get-Content -LiteralPath $File -Encoding UTF8)) {
        if (-not $line.Trim()) { continue }
        $match = [regex]::Match($line.Trim(),'^(?<hash>[0-9A-Fa-f]{64})\s+\*?(?<name>.+)$')
        if (-not $match.Success) { throw 'SHA256 清单格式不正确。' }
        if ($match.Groups['name'].Value -ceq $Name) { $found += $match.Groups['hash'].Value.ToLowerInvariant() }
    }
    if ($found.Count -ne 1) { throw "校验清单没有唯一的 $Name 条目。" }
    return $found[0]
}
function Expand-RlsPackage([string]$Zip, [string]$Destination) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($Zip)
    try {
        if ($archive.Entries.Count -gt 10000) { throw '压缩包文件数量超出限制。' }
        $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        [long]$total = 0
        foreach ($entry in $archive.Entries) {
            $name = $entry.FullName
            if ($name -ceq 'RinsLittleShelter/') { continue }
            if (-not $name.StartsWith('RinsLittleShelter/',[StringComparison]::Ordinal)) { throw '压缩包根目录不正确。' }
            $relative = $name.Substring(18).TrimEnd('/')
            $null = Get-RlsChild $Destination $relative
            if (-not $seen.Add($relative) -or (($entry.ExternalAttributes -shr 16) -band 0xF000) -eq 0xA000 -or ($entry.ExternalAttributes -band 0x400)) { throw '压缩包含重复名称或链接。' }
            if ($relative -in @('.rin-portable.json','Uninstall-RinsLittleShelter.cmd','Uninstall-RinsLittleShelter.ps1','.setup/Install-RinsLittleShelter.ps1') -and
                $entry.FullName.EndsWith('/')) { throw '卸载支持文件不能是目录。' }
            if ($relative -eq $script:RlsMarker -or $relative -eq '.setup' -and -not $entry.FullName.EndsWith('/') -or
                $relative.StartsWith('.setup/',[StringComparison]::OrdinalIgnoreCase) -and $relative -cne '.setup/Install-RinsLittleShelter.ps1') { throw '压缩包含安装器保留文件名。' }
            $total += $entry.Length
            if ($total -gt 2GB -or $entry.Length -gt 1GB) { throw '压缩包解压大小超出限制。' }
        }
        foreach ($entry in $archive.Entries) {
            if ($entry.FullName -ceq 'RinsLittleShelter/') { continue }
            $relative = $entry.FullName.Substring(18).TrimEnd('/')
            $path = Get-RlsChild $Destination $relative
            if ($entry.FullName.EndsWith('/')) { $null = New-Item -ItemType Directory -Path $path -Force; continue }
            $null = New-Item -ItemType Directory -Path (Split-Path $path -Parent) -Force
            $sourceStream = $entry.Open(); $outputStream = [IO.File]::Open($path,[IO.FileMode]::CreateNew)
            try { $sourceStream.CopyTo($outputStream) } finally { $outputStream.Dispose(); $sourceStream.Dispose() }
        }
    } finally { $archive.Dispose() }
    foreach ($required in @('RinsLittleShelter.exe','assets/manifest.json','assets/catalog.json','assets/app/rin-shelter.ico')) {
        if (-not (Test-Path -LiteralPath (Get-RlsChild $Destination $required) -PathType Leaf)) { throw "程序包缺少 $required。" }
    }
    $null = Get-Content -LiteralPath (Join-Path $Destination 'assets/manifest.json') -Raw -Encoding UTF8 | ConvertFrom-Json
}
function New-RlsShortcut([string]$Path, [string]$Target, [string]$Directory, $OwnedRecords=@()) {
    Assert-RlsNoLinks $Path
    $shell = New-Object -ComObject WScript.Shell
    $created = $false
    if (Test-Path -LiteralPath $Path) {
        $existing = $shell.CreateShortcut($Path)
        $previous = @($OwnedRecords | Where-Object { $_.Path -eq $Path -and $_.Target -eq $Target -and $_.SHA256 -eq (Get-RlsHash $Path) })
        if ($previous.Count -ne 1 -or -not $existing.TargetPath.Equals($Target,[StringComparison]::OrdinalIgnoreCase)) { Write-Warning "保留已有的同名快捷方式：$Path"; return $null }
    } else {
        $null = New-Item -ItemType Directory -Path (Split-Path $Path -Parent) -Force
        $link = $shell.CreateShortcut($Path); $link.TargetPath = $Target; $link.WorkingDirectory = $Directory
        $link.IconLocation = (Join-Path $Directory 'assets/app/rin-shelter.ico')+',0'; $link.Save(); $created = $true
    }
    return @{Path=$Path;Target=$Target;SHA256=(Get-RlsHash $Path);Created=$created}
}
function Remove-RlsShortcut($Record, [string]$Directory) {
    $path = [IO.Path]::GetFullPath([string]$Record.Path)
    if ([IO.Path]::GetExtension($path) -ne '.lnk' -or -not (Test-Path -LiteralPath $path -PathType Leaf)) { return }
    Assert-RlsNoLinks $path
    $targets = @((Join-Path $Directory 'RinsLittleShelter.exe'),(Join-Path $Directory 'Uninstall-RinsLittleShelter.cmd'))
    if ($Record.Target -notin $targets -or (Get-RlsHash $path) -ne $Record.SHA256) { return }
    $shell = New-Object -ComObject WScript.Shell
    if ($shell.CreateShortcut($path).TargetPath.Equals([string]$Record.Target,[StringComparison]::OrdinalIgnoreCase)) { Remove-Item -LiteralPath $path -Force -ErrorAction Stop }
}
function Remove-RlsAutoStart([string]$Directory) {
    $keyPath = 'Software\Microsoft\Windows\CurrentVersion\Run'
    $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($keyPath,$false)
    if (-not $key) { return }
    try { $value = [string]$key.GetValue('RinsLittleShelter','') } finally { $key.Dispose() }
    $exe = Join-Path $Directory 'RinsLittleShelter.exe'
    if ($value -eq $exe -or $value -eq ('"'+$exe+'"')) {
        $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($keyPath,$true)
        try { $key.DeleteValue('RinsLittleShelter',$false) } finally { $key.Dispose() }
    }
}
function Invoke-RlsUninstall([string]$Directory) {
    $m = Read-RlsManifest $Directory; Assert-RlsNotRunning $Directory
    # Validate the complete tree before deleting any registered file.
    Assert-RlsNoLinks $Directory
    $tree = @(Get-RlsTree $Directory)
    foreach ($record in @($m.Shortcuts)) { if ($record) { Remove-RlsShortcut $record $Directory } }
    Remove-RlsAutoStart $Directory
    foreach ($file in $m.Files) {
        $path = Get-RlsChild $Directory $file.Path
        if (Test-Path -LiteralPath $path -PathType Leaf) {
            if ((Get-Item -LiteralPath $path).Length -eq $file.Bytes -and (Get-RlsHash $path) -eq $file.SHA256) { Remove-Item -LiteralPath $path -Force -ErrorAction Stop }
            else { Write-Warning "保留已修改文件：$path" }
        }
    }
    Remove-Item -LiteralPath (Join-Path $Directory $m.ResolvedMarker) -Force -ErrorAction Stop
    # Only trim directories implied by the registered files; leave user-created empty folders.
    $folders = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($file in $m.Files) {
        $parent = Split-Path (Get-RlsChild $Directory $file.Path) -Parent
        while ($parent -and $parent -ne $Directory) { $null = $folders.Add($parent); $parent = Split-Path $parent -Parent }
    }
    foreach ($folder in @($folders | Sort-Object Length -Descending)) {
        $safe = [IO.Path]::GetFullPath($folder)
        if (-not $safe.StartsWith($Directory+'\',[StringComparison]::OrdinalIgnoreCase)) { throw '空目录超出安装范围。' }
        if ((Test-Path -LiteralPath $safe -PathType Container) -and @(Get-ChildItem -LiteralPath $safe -Force).Count -eq 0) { Remove-Item -LiteralPath $safe -Force }
    }
    if (@(Get-ChildItem -LiteralPath $Directory -Force).Count -eq 0) { Remove-Item -LiteralPath $Directory -Force }
    Write-Host '卸载完成，陪伴进度和存档已保留。'
}
function Invoke-RlsSetup {
    [CmdletBinding()]param(
        [string]$Action='Install',[string]$InstallDirectory='', [string]$Version='',
        [string]$PackagePath='', [string]$ChecksumPath='', [switch]$NoShortcuts,[switch]$NoLaunch,
        [string]$DesktopDirectory='', [string]$StartMenuDirectory=''
    )
    $ErrorActionPreference = 'Stop'; $ProgressPreference = 'SilentlyContinue'
    Assert-RlsPlatform
    $directory = Get-RlsInstallPath $InstallDirectory $Action
    $hash = [Security.Cryptography.SHA256]::Create()
    try { $key = [BitConverter]::ToString($hash.ComputeHash([Text.Encoding]::UTF8.GetBytes($directory.ToLowerInvariant()))).Replace('-','') } finally { $hash.Dispose() }
    $mutex = [Threading.Mutex]::new($false,'Local\RinsLittleShelter-Setup-'+$key)
    $held = $false; $work = ''; $backup = ''; $committed = $false; $moved = $false; $createdLinks = @()
    try {
        try { $held = $mutex.WaitOne(0) } catch [Threading.AbandonedMutexException] { $held = $true }
        if (-not $held) { throw '这个目录正在进行另一次安装或卸载，请稍后再试。' }
        if ($Action -eq 'Uninstall') { Invoke-RlsUninstall $directory; return }
        Assert-RlsNotRunning $directory
        $previous = $null
        if (Test-Path -LiteralPath $directory) {
            if (-not (Test-Path -LiteralPath $directory -PathType Container)) { throw '安装位置已经是一个文件。' }
            if (@(Get-ChildItem -LiteralPath $directory -Force).Count) { $previous = Read-RlsManifest $directory }
        }
        $parent = Split-Path $directory -Parent; $null = New-Item -ItemType Directory -Path $parent -Force
        $work = Join-Path $parent ('.rins-work-'+[Guid]::NewGuid().ToString('N')); $null = New-Item -ItemType Directory -Path $work
        if ($PackagePath -or $ChecksumPath) {
            if (-not $PackagePath -or -not $ChecksumPath) { throw '离线安装需同时指定 -PackagePath 和 -ChecksumPath。' }
            $package = (Resolve-Path -LiteralPath $PackagePath).Path; $checksums = (Resolve-Path -LiteralPath $ChecksumPath).Path
            $name = Split-Path $package -Leaf
            if ($name -notmatch '^RinsLittleShelter-(v\d+\.\d+\.\d+)-win-x64\.zip$') { throw '离线包文件名不是正式便携包。' }
            $packageVersion = $Matches[1]
            if ($Version -and $Version -ne $packageVersion) { throw '离线包与指定版本不一致。' }
        } else {
            Write-Host '正在读取 GitHub 最新正式版…'; $release = Get-RlsRelease $Version
            $packageVersion = $release.Version; $name = $release.Name
            $package = Join-Path $work $name; $checksums = Join-Path $work 'SHA256.txt'
            Write-Host ("正在下载 $packageVersion，请保持联网…")
            Invoke-WebRequest -Uri $release.Checksum.browser_download_url -OutFile $checksums -UseBasicParsing -TimeoutSec 60
            Invoke-WebRequest -Uri $release.Zip.browser_download_url -OutFile $package -UseBasicParsing -TimeoutSec 1800
        }
        Write-Host '正在校验和展开程序…'
        $expected = Get-RlsChecksum $checksums $name
        if ((Get-RlsHash $package) -ne $expected) { throw '便携包 SHA256 不一致，安装已停止，旧程序未更改。' }
        $candidate = Join-Path $work 'payload'; $null = New-Item -ItemType Directory -Path $candidate
        Expand-RlsPackage $package $candidate
        if ((Get-RlsHash (Join-Path $candidate 'RinsLittleShelter.exe')) -ne (Get-RlsChecksum $checksums 'RinsLittleShelter/RinsLittleShelter.exe')) { throw '主程序 SHA256 不一致。' }
        $portableMarker = Join-Path $candidate $script:RlsPortableMarker
        if (Test-Path -LiteralPath $portableMarker) { $null = Read-RlsManifest $candidate; Remove-Item -LiteralPath $portableMarker -Force }
        $setupDirectory = Join-Path $candidate '.setup'; $null = New-Item -ItemType Directory -Path $setupDirectory -Force
        Copy-Item -LiteralPath $script:RlsSetupSource -Destination (Join-Path $setupDirectory 'Install-RinsLittleShelter.ps1') -Force
        $supportRoot = Split-Path $script:RlsSetupSource -Parent
        if ((Split-Path $supportRoot -Leaf) -eq '.setup') { $supportRoot = Split-Path $supportRoot -Parent }
        foreach ($support in @('Uninstall-RinsLittleShelter.cmd','Uninstall-RinsLittleShelter.ps1')) {
            $source = Join-Path $supportRoot $support
            if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { throw '请完整解压安装脚本包，缺少卸载入口。' }
            Copy-Item -LiteralPath $source -Destination (Join-Path $candidate $support) -Force
        }
        $owned = @()
        foreach ($file in @(Get-RlsTree $candidate | Where-Object { -not $_.PSIsContainer })) {
            $owned += @{Path=$file.FullName.Substring($candidate.Length+1).Replace('\','/');Bytes=$file.Length;SHA256=(Get-RlsHash $file.FullName)}
        }
        if ($previous) {
            $known = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
            foreach ($file in $previous.Files) { $null = $known.Add([string]$file.Path) }
            foreach ($item in @(Get-RlsTree $directory)) {
                if ($item.PSIsContainer) { continue }
                $relative = $item.FullName.Substring($directory.Length+1).Replace('\','/')
                if ($relative -eq $previous.ResolvedMarker -or $known.Contains($relative)) { continue }
                $dest = Get-RlsChild $candidate $relative
                if (Test-Path -LiteralPath $dest) { throw "新程序与自定义文件同名，未覆盖：$relative" }
                $null = New-Item -ItemType Directory -Path (Split-Path $dest -Parent) -Force
                Copy-Item -LiteralPath $item.FullName -Destination $dest
                if ((Get-RlsHash $dest) -ne (Get-RlsHash $item.FullName)) { throw '自定义文件备份校验失败。' }
            }
        }
        $manifest = @{AppId=$script:RlsAppId;FormatVersion=1;Version=$packageVersion;InstallDirectory=$directory;ArchiveSHA256=$expected;Files=$owned;Shortcuts=@()}
        if ($previous) { $manifest.Shortcuts = @($previous.Shortcuts) }
        Write-RlsJson (Join-Path $candidate $script:RlsMarker) $manifest
        Assert-RlsNotRunning $directory; Assert-RlsNoLinks $directory
        if (Test-Path -LiteralPath $directory) {
            if ($previous) { $backup = Join-Path $parent ('.rins-backup-'+[Guid]::NewGuid().ToString('N')); Move-RlsDirectory $directory $backup }
            else { Remove-Item -LiteralPath $directory -Force }
        }
        try { Move-RlsDirectory $candidate $directory; $moved = $true }
        catch { if ($backup -and (Test-Path -LiteralPath $backup)) { Move-RlsDirectory $backup $directory; $backup = '' }; throw }
        # Shortcut errors do not invalidate a verified program installation.
        if (-not $NoShortcuts) {
            if (-not $DesktopDirectory) { $DesktopDirectory = [Environment]::GetFolderPath('DesktopDirectory') }
            if (-not $StartMenuDirectory) { $StartMenuDirectory = [Environment]::GetFolderPath('Programs') }
            $links = @(@($DesktopDirectory,"Rin's Little Shelter.lnk",'RinsLittleShelter.exe'),
                @($StartMenuDirectory,"Rin's Little Shelter.lnk",'RinsLittleShelter.exe'),
                @($StartMenuDirectory,"Uninstall Rin's Little Shelter.lnk",'Uninstall-RinsLittleShelter.cmd'))
            foreach ($link in $links) {
                try { $record = New-RlsShortcut (Join-Path $link[0] $link[1]) (Join-Path $directory $link[2]) $directory $manifest.Shortcuts; if ($record) { $manifest.Shortcuts = @($manifest.Shortcuts | Where-Object { $_.Path -ne $record.Path }) + @($record); if ($record.Created) { $createdLinks += $record } } }
                catch { Write-Warning ('快捷方式未创建：'+$_.Exception.Message) }
            }
        }
        Write-RlsJson (Join-Path $directory $script:RlsMarker) $manifest
        $committed = $true
        if ($backup) { try { Remove-RlsOwnedTree $backup $parent; $backup = '' } catch { Write-Warning "旧程序备份已保留：$backup" } }
        Write-Host ("安装完成：$directory`n存档和陪伴进度已保留。")
        if (-not $NoLaunch) {
            try { Start-Process -FilePath (Join-Path $directory 'RinsLittleShelter.exe') -WorkingDirectory $directory -ErrorAction Stop | Out-Null }
            catch { Write-Warning '安装已完成，自动启动未成功；可从快捷方式或安装目录手动启动。' }
        }
    } catch {
        if (-not $committed -and $moved) {
            foreach ($record in $createdLinks) { try { Remove-RlsShortcut $record $directory } catch { Write-Warning '新建快捷方式未能自动清理。' } }
            $failed = Join-Path $work 'failed'; Move-RlsDirectory $directory $failed
            if ($backup -and (Test-Path -LiteralPath $backup)) { Move-RlsDirectory $backup $directory; $backup = '' }
        }
        throw
    } finally {
        if ($work -and (Test-Path -LiteralPath $work)) {
            try { Remove-RlsOwnedTree $work (Split-Path $directory -Parent) } catch { Write-Warning "临时文件已保留：$work" }
        }
        if ($held) { $mutex.ReleaseMutex() }; $mutex.Dispose()
    }
}

if ($MyInvocation.InvocationName -ne '.') {
    try { Invoke-RlsSetup @PSBoundParameters; exit 0 }
    catch { [Console]::Error.WriteLine(('未完成：'+$_.Exception.Message)); exit 1 }
}
