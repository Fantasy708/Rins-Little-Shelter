# Pack a relocatable ownership list only from a controlled staging directory.
function Add-ShelterPortableUninstaller([string]$Directory, [string]$ProjectRoot, [string]$Version) {
    $base = [IO.Path]::GetFullPath($Directory).TrimEnd('\')
    $project = [IO.Path]::GetFullPath($ProjectRoot).TrimEnd('\')
    if (-not $base.StartsWith($project+'\',[StringComparison]::OrdinalIgnoreCase)) { throw '便携清单只能在项目内的独立暂存目录生成。' }
    if (-not (Test-Path -LiteralPath (Join-Path $base 'RinsLittleShelter.exe') -PathType Leaf)) { throw '暂存目录缺少Rin主程序。' }
    $cursor = $base
    while ($cursor) {
        $item = Get-Item -LiteralPath $cursor -Force -ErrorAction Stop
        if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw '暂存路径含链接。' }
        $cursor = Split-Path $cursor -Parent
    }
    $pending = [Collections.Generic.Stack[string]]::new(); $pending.Push($base)
    while ($pending.Count) {
        foreach ($item in @(Get-ChildItem -LiteralPath $pending.Pop() -Force -ErrorAction Stop)) {
            if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw '暂存目录含链接。' }
            if ($item.PSIsContainer) { $pending.Push($item.FullName) }
            elseif ($item.Name -match '^shelter\.json(?:\.(?:bak|tmp))?$') { throw '不能将个人存档打进便携卸载清单。' }
        }
    }
    if (Test-Path -LiteralPath (Join-Path $base '.rin-install.json')) { throw '不能将已安装目录登记成便携包。' }
    $setup = Join-Path $base '.setup'
    $null = New-Item -ItemType Directory -Path $setup -Force
    foreach ($name in @('Uninstall-RinsLittleShelter.cmd','Uninstall-RinsLittleShelter.ps1')) {
        Copy-Item -LiteralPath (Join-Path $project ('scripts/'+$name)) -Destination (Join-Path $base $name) -Force
    }
    Copy-Item -LiteralPath (Join-Path $project 'scripts/Install-RinsLittleShelter.ps1') -Destination (Join-Path $setup 'Install-RinsLittleShelter.ps1') -Force
    $files = @()
    foreach ($file in @(Get-ChildItem -LiteralPath $base -Recurse -Force -File | Sort-Object FullName)) {
        $relative = $file.FullName.Substring($base.Length+1).Replace('\','/')
        if ($relative -eq '.rin-portable.json') { continue }
        $files += @{Path=$relative;Bytes=$file.Length;SHA256=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()}
    }
    $manifest = @{AppId='Fantasy708.RinsLittleShelter';FormatVersion=1;PackageKind='Portable';Root='.';Version=$Version;Files=$files;Shortcuts=@()}
    [IO.File]::WriteAllText((Join-Path $base '.rin-portable.json'),($manifest | ConvertTo-Json -Depth 8),[Text.UTF8Encoding]::new($false))
}
