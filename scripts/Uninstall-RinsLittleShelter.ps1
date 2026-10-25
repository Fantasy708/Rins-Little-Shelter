#requires -Version 5.1
[CmdletBinding()]
param([string]$InstallDirectory = '', [switch]$NoPause)

$script:RlsUninstallSource = $PSCommandPath
function Invoke-RlsPortableUninstall([string]$Directory = '') {
    $entryRoot = Split-Path $script:RlsUninstallSource -Parent
    $engine = Join-Path $entryRoot '.setup/Install-RinsLittleShelter.ps1'
    if (-not (Test-Path -LiteralPath $engine -PathType Leaf)) { $engine = Join-Path $entryRoot 'Install-RinsLittleShelter.ps1' }
    if (-not (Test-Path -LiteralPath $engine -PathType Leaf)) { throw '卸载支持文件缺失，请完整解压程序包或安装脚本包。' }
    $cursor = [IO.Path]::GetFullPath($engine)
    while ($cursor) {
        $item = Get-Item -LiteralPath $cursor -Force -ErrorAction Stop
        if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw '卸载支持路径含链接，未作更改。' }
        $cursor = Split-Path $cursor -Parent
    }
    if (-not $Directory -and ((Test-Path -LiteralPath (Join-Path $entryRoot 'RinsLittleShelter.exe')) -or
        (Test-Path -LiteralPath (Join-Path $entryRoot '.rin-install.json')) -or (Test-Path -LiteralPath (Join-Path $entryRoot '.rin-portable.json')) -or
        (Split-Path (Split-Path $engine -Parent) -Leaf) -eq '.setup')) { $Directory = $entryRoot }
    # Dot sourcing loads the same validated uninstall implementation used by setup.
    . $engine
    Invoke-RlsSetup -Action Uninstall -InstallDirectory $Directory
}

if ($MyInvocation.InvocationName -ne '.') {
    $rinUninstallExit = 0
    try { Invoke-RlsPortableUninstall $InstallDirectory }
    catch { [Console]::Error.WriteLine(('卸载未完成：' + $_.Exception.Message)); $rinUninstallExit = 1 }
    if (-not $NoPause -and $env:RIN_SETUP_NO_PAUSE -ne '1') { $null = Read-Host '按回车关闭窗口' }
    exit $rinUninstallExit
}
