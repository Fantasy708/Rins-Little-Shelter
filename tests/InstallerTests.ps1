#requires -Version 5.1
<#
Sandbox integration tests for the standalone Windows installer.
Run independently under powershell.exe and pwsh.exe. Every writable path is
below output/installer-tests/<engine>-<GUID>; fixtures are never executable.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$engineName = if ($PSVersionTable.PSEdition -eq 'Core') { 'pwsh' } else { 'windows-powershell' }
$runName = $engineName + '-' + $PSVersionTable.PSVersion.ToString() + '-' + [Guid]::NewGuid().ToString('N')
$outputRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot 'output/installer-tests'))
$script:TestRoot = Join-Path $outputRoot $runName
$null = New-Item -ItemType Directory -Path $script:TestRoot -Force
$script:Results = New-Object Collections.Generic.List[object]
$script:AssertionCount = 0
$script:LaunchCalls = 0
$script:SimulatedLaunchCalls = 0
$script:AutoStartCalls = New-Object Collections.Generic.List[string]
$started = [DateTime]::UtcNow
$transcriptPath = Join-Path $script:TestRoot 'transcript.txt'
Start-Transcript -LiteralPath $transcriptPath -Force | Out-Null

function Assert-TestPath([string]$Path) {
    $full = [IO.Path]::GetFullPath($Path)
    if (-not $full.StartsWith($script:TestRoot + '\',[StringComparison]::OrdinalIgnoreCase)) { throw "Test attempted to leave sandbox: $full" }
    return $full
}
function New-TestDirectory([string]$Relative) {
    $path = Assert-TestPath (Join-Path $script:TestRoot $Relative)
    $null = New-Item -ItemType Directory -Path $path -Force
    return $path
}
function Write-TestText([string]$Path,[string]$Content) {
    $safe = Assert-TestPath $Path
    $null = New-Item -ItemType Directory -Path (Split-Path $safe -Parent) -Force
    [IO.File]::WriteAllText($safe,$Content,[Text.UTF8Encoding]::new($false))
}
function Assert-Test([bool]$Condition,[string]$Message) {
    $script:AssertionCount++
    if (-not $Condition) { throw "Assertion failed: $Message" }
}
function Assert-Equal($Actual,$Expected,[string]$Message) {
    Assert-Test ($Actual -ceq $Expected) ($Message + "; expected=[$Expected], actual=[$Actual]")
}
function Assert-Throws([scriptblock]$Body,[string]$Pattern,[string]$Message) {
    $caught = $null
    try { & $Body } catch { $caught = $_ }
    Assert-Test ($null -ne $caught) ($Message + ' must throw')
    if ($caught -and $Pattern) { Assert-Test ($caught.Exception.Message -match $Pattern) ($Message + '; unexpected error: ' + $caught.Exception.Message) }
}
function Test-Case([string]$Name,[scriptblock]$Body) {
    $before = $script:AssertionCount
    $watch = [Diagnostics.Stopwatch]::StartNew()
    try {
        & $Body
        $script:Results.Add([pscustomobject]@{Name=$Name;Passed=$true;Assertions=($script:AssertionCount-$before);Milliseconds=$watch.ElapsedMilliseconds;Error=$null})
        Write-Host ('PASS ' + $Name)
    } catch {
        $script:Results.Add([pscustomobject]@{Name=$Name;Passed=$false;Assertions=($script:AssertionCount-$before);Milliseconds=$watch.ElapsedMilliseconds;Error=$_.Exception.Message;Stack=$_.ScriptStackTrace})
        Write-Host ('FAIL ' + $Name + ': ' + $_.Exception.Message) -ForegroundColor Red
    }
}
function New-ZipEntry([string]$Name,[string]$Content='',[int]$Attributes=0) {
    return [pscustomobject]@{Name=$Name;Content=$Content;Attributes=$Attributes}
}
function Get-TextHash([string]$Text) {
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($Text))).Replace('-','').ToLowerInvariant() }
    finally { $sha.Dispose() }
}
function New-FixturePackage([string]$Name,[string]$Version='v1.1.0',$ExtraEntries=@(),[string]$Omit='') {
    $directory = New-TestDirectory ('packages/' + $Name)
    $zipPath = Join-Path $directory ('RinsLittleShelter-' + $Version + '-win-x64.zip')
    $exeText = 'INERT TEST FIXTURE; NEVER LAUNCH; ' + $Version
    $entries = @(
        (New-ZipEntry 'RinsLittleShelter/RinsLittleShelter.exe' $exeText),
        (New-ZipEntry 'RinsLittleShelter/assets/manifest.json' ('{"Version":1,"FixtureVersion":"'+$Version+'"}')),
        (New-ZipEntry 'RinsLittleShelter/assets/catalog.json' '{"Fixture":true}'),
        (New-ZipEntry 'RinsLittleShelter/assets/app/rin-shelter.ico' 'inert icon fixture')
    ) | Where-Object { $_.Name -ne $Omit }
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::Open($zipPath,[IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($spec in @($entries) + @($ExtraEntries)) {
            $entry = $archive.CreateEntry($spec.Name)
            if ($spec.Attributes) { $entry.ExternalAttributes = $spec.Attributes }
            $stream = $entry.Open()
            try { $bytes = [Text.Encoding]::UTF8.GetBytes([string]$spec.Content); $stream.Write($bytes,0,$bytes.Length) }
            finally { $stream.Dispose() }
        }
    } finally { $archive.Dispose() }
    $checksumPath = Join-Path $directory 'SHA256.txt'
    $zipHash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
    $exeHash = Get-TextHash $exeText
    $checksumText = "$zipHash  $([IO.Path]::GetFileName($zipPath))`r`n$exeHash  RinsLittleShelter/RinsLittleShelter.exe`r`n"
    Write-TestText $checksumPath $checksumText
    return [pscustomobject]@{Zip=$zipPath;Checksum=$checksumPath;Version=$Version;ExeText=$exeText;ExeHash=$exeHash;ZipHash=$zipHash;ChecksumText=$checksumText}
}
function Install-Fixture($Package,[string]$Directory,[switch]$Shortcuts,[string]$LinkRoot='') {
    $null = Assert-TestPath $Directory
    if (-not $LinkRoot) { $LinkRoot = New-TestDirectory ('links/' + [Guid]::NewGuid().ToString('N')) }
    $null = Assert-TestPath $LinkRoot
    Invoke-RlsSetup -InstallDirectory $Directory -PackagePath $Package.Zip -ChecksumPath $Package.Checksum -NoLaunch -NoShortcuts:(-not $Shortcuts) -DesktopDirectory (Join-Path $LinkRoot 'Desktop') -StartMenuDirectory (Join-Path $LinkRoot 'Programs')
}
function Uninstall-Fixture([string]$Directory) {
    $null = Assert-TestPath $Directory
    Invoke-RlsSetup -Action Uninstall -InstallDirectory $Directory -NoLaunch -NoShortcuts -DesktopDirectory (Join-Path $script:TestRoot 'unused-desktop') -StartMenuDirectory (Join-Path $script:TestRoot 'unused-start-menu')
}
function Get-FixtureManifest([string]$Directory) { return Get-Content -LiteralPath (Join-Path $Directory '.rin-install.json') -Raw -Encoding UTF8 | ConvertFrom-Json }
function New-ReleaseResponse([string]$Version='v1.1.0',[bool]$Prerelease=$false,[bool]$Draft=$false) {
    $assets = @()
    foreach ($name in @(('RinsLittleShelter-'+$Version+'-win-x64.zip'),'SHA256.txt')) {
        $assets += @{name=$name;state='uploaded';browser_download_url=('https://github.com/Fantasy708/Rins-Little-Shelter/releases/download/'+$Version+'/'+$name)}
    }
    return @{tag_name=$Version;prerelease=$Prerelease;draft=$Draft;assets=$assets}
}

try {
    . (Join-Path $projectRoot 'scripts/Install-RinsLittleShelter.ps1')
    # Guard every possible launch and avoid reading or writing the user's HKCU.
    function Start-Process {
        [CmdletBinding()]param([string]$FilePath,[string]$WorkingDirectory)
        $script:LaunchCalls++
        throw 'TEST GUARD: no process may be launched by this test suite.'
    }
    function Remove-RlsAutoStart([string]$Directory) {
        $safe = Assert-TestPath $Directory
        $script:AutoStartCalls.Add($safe)
    }
    $base = New-FixturePackage 'base'
    $upgrade = New-FixturePackage 'upgrade' 'v1.2.0'

    Test-Case 'first install writes verified payload and ownership manifest' {
        $dir = Join-Path (New-TestDirectory 'first') 'Rin'
        Install-Fixture $base $dir
        $manifest = Get-FixtureManifest $dir
        Assert-Equal $manifest.Version 'v1.1.0' 'manifest release'
        Assert-Equal $manifest.InstallDirectory $dir 'manifest installation path'
        Assert-Equal $manifest.ArchiveSHA256 $base.ZipHash 'manifest archive digest'
        Assert-Equal (Get-RlsHash (Join-Path $dir 'RinsLittleShelter.exe')) $base.ExeHash 'installed EXE digest'
        foreach ($relative in @('RinsLittleShelter.exe','assets/manifest.json','assets/catalog.json','assets/app/rin-shelter.ico','.setup/Install-RinsLittleShelter.ps1','Uninstall-RinsLittleShelter.cmd')) {
            Assert-Test (Test-Path -LiteralPath (Join-Path $dir $relative) -PathType Leaf) ('installed file ' + $relative)
            Assert-Equal @($manifest.Files | Where-Object Path -eq $relative).Count 1 ('owned file ' + $relative)
        }
        Assert-Equal @($manifest.Shortcuts).Count 0 'NoShortcuts creates no records'
        Assert-Equal $script:LaunchCalls 0 'NoLaunch avoids inert fixture execution'
        Uninstall-Fixture $dir
        Assert-Test (-not (Test-Path -LiteralPath $dir)) 'clean uninstall removes only clean owned directory'
    }
    Test-Case 'same release reinstall and upgrade retain unregistered files' {
        $dir = Join-Path (New-TestDirectory 'upgrade') 'Rin'
        Install-Fixture $base $dir
        Write-TestText (Join-Path $dir 'custom/note.txt') 'user-owned note'
        Install-Fixture $base $dir
        Assert-Equal (Get-FixtureManifest $dir).Version 'v1.1.0' 'same release reinstall version'
        Assert-Equal (Get-Content -LiteralPath (Join-Path $dir 'custom/note.txt') -Raw) 'user-owned note' 'reinstall retains unknown bytes'
        Install-Fixture $upgrade $dir
        $manifest = Get-FixtureManifest $dir
        Assert-Equal $manifest.Version 'v1.2.0' 'upgrade version'
        Assert-Equal (Get-RlsHash (Join-Path $dir 'RinsLittleShelter.exe')) $upgrade.ExeHash 'upgrade EXE digest'
        Assert-Equal (Get-Content -LiteralPath (Join-Path $dir 'custom/note.txt') -Raw) 'user-owned note' 'upgrade retains unknown bytes'
        Assert-Equal @($manifest.Files | Where-Object Path -eq 'custom/note.txt').Count 0 'unknown files are not claimed'
        Write-TestText (Join-Path $dir 'assets/catalog.json') 'user modified catalog'
        Uninstall-Fixture $dir
        Assert-Equal (Get-Content -LiteralPath (Join-Path $dir 'custom/note.txt') -Raw) 'user-owned note' 'uninstall retains unknown bytes'
        Assert-Equal (Get-Content -LiteralPath (Join-Path $dir 'assets/catalog.json') -Raw) 'user modified catalog' 'uninstall retains modified owned file'
        Assert-Test (-not (Test-Path -LiteralPath (Join-Path $dir 'RinsLittleShelter.exe'))) 'uninstall removes unchanged EXE'
        Assert-Test (-not (Test-Path -LiteralPath (Join-Path $dir '.rin-install.json'))) 'uninstall removes ownership marker'
    }
    Test-Case 'unknown file conflicts stop upgrade before changing old install' {
        $dir = Join-Path (New-TestDirectory 'conflict') 'Rin'
        Install-Fixture $base $dir
        Write-TestText (Join-Path $dir 'new-file.txt') 'user bytes'
        $conflict = New-FixturePackage 'conflict' 'v1.3.0' @((New-ZipEntry 'RinsLittleShelter/new-file.txt' 'release bytes'))
        Assert-Throws { Install-Fixture $conflict $dir } '同名' 'unknown file collision'
        Assert-Equal (Get-FixtureManifest $dir).Version 'v1.1.0' 'failed conflict retains old marker'
        Assert-Equal (Get-Content -LiteralPath (Join-Path $dir 'new-file.txt') -Raw) 'user bytes' 'failed conflict retains unknown bytes'
        Assert-Equal (Get-RlsHash (Join-Path $dir 'RinsLittleShelter.exe')) $base.ExeHash 'failed conflict retains old EXE'
    }
    Test-Case 'SHA list rejects missing duplicate invalid and mismatched hashes' {
        $dir = Join-Path (New-TestDirectory 'checksum') 'Rin'
        $bad = New-FixturePackage 'bad-checksum'
        $zipName = [IO.Path]::GetFileName($bad.Zip)
        Write-TestText $bad.Checksum ($bad.ExeHash+'  RinsLittleShelter/RinsLittleShelter.exe')
        Assert-Throws { Install-Fixture $bad $dir } '唯一' 'missing ZIP SHA'
        Write-TestText $bad.Checksum ($bad.ChecksumText + $bad.ZipHash+'  '+$zipName)
        Assert-Throws { Install-Fixture $bad $dir } '唯一' 'duplicate ZIP SHA'
        Write-TestText $bad.Checksum ($bad.ZipHash+'  '+$zipName)
        Assert-Throws { Install-Fixture $bad $dir } '唯一' 'missing EXE SHA'
        Write-TestText $bad.Checksum ($bad.ChecksumText + $bad.ExeHash+'  RinsLittleShelter/RinsLittleShelter.exe')
        Assert-Throws { Install-Fixture $bad $dir } '唯一' 'duplicate EXE SHA'
        Write-TestText $bad.Checksum 'not a sha list'
        Assert-Throws { Install-Fixture $bad $dir } '格式' 'invalid SHA syntax'
        Write-TestText $bad.Checksum (('0'*64)+'  '+$zipName+"`n"+$bad.ExeHash+'  RinsLittleShelter/RinsLittleShelter.exe')
        Assert-Throws { Install-Fixture $bad $dir } '便携包 SHA256' 'mismatched ZIP digest'
        Write-TestText $bad.Checksum ($bad.ZipHash+'  '+$zipName+"`n"+('0'*64)+'  RinsLittleShelter/RinsLittleShelter.exe')
        Assert-Throws { Install-Fixture $bad $dir } '主程序 SHA256' 'mismatched EXE digest'
        Assert-Test (-not (Test-Path -LiteralPath $dir)) 'invalid checksums never commit a destination'
    }
    Test-Case 'ZIP traversal absolute ADS case duplicate and symlink entries are rejected' {
        $specifications = @(
            @{Name='traversal';Entry='RinsLittleShelter/../outside.txt';Attrs=0},
            @{Name='absolute';Entry='/outside.txt';Attrs=0},
            @{Name='drive';Entry='RinsLittleShelter/C:/outside.txt';Attrs=0},
            @{Name='ads';Entry='RinsLittleShelter/assets/extra.txt:stream';Attrs=0},
            @{Name='case-duplicate';Entry='RinsLittleShelter/ASSETS/catalog.json';Attrs=0},
            @{Name='symlink';Entry='RinsLittleShelter/assets/link';Attrs=-1577058304},
            @{Name='reparse-attribute';Entry='RinsLittleShelter/assets/reparse';Attrs=1024},
            @{Name='reserved';Entry='RinsLittleShelter/.setup/forged.ps1';Attrs=0}
        )
        foreach ($spec in $specifications) {
            $bad = New-FixturePackage ('zip-'+$spec.Name) 'v1.1.0' @((New-ZipEntry $spec.Entry 'bad' $spec.Attrs))
            $destination = New-TestDirectory ('expand/'+$spec.Name)
            Assert-Throws { Expand-RlsPackage $bad.Zip $destination } '' ('reject ZIP '+$spec.Name)
            Assert-Equal @(Get-ChildItem -LiteralPath $destination -Force).Count 0 ('preflight prevents partial extraction '+$spec.Name)
        }
        $missing = New-FixturePackage 'missing-required' 'v1.1.0' @() 'RinsLittleShelter/assets/catalog.json'
        $destination = New-TestDirectory 'expand/missing-required'
        Assert-Throws { Expand-RlsPackage $missing.Zip $destination } '缺少' 'required payload entry'
        Assert-Test (-not (Test-Path -LiteralPath (Join-Path $script:TestRoot 'outside.txt'))) 'malicious archive did not escape test destination'
    }
    Test-Case 'version mismatch and unregistered existing directories are preserved' {
        $dir = Join-Path (New-TestDirectory 'version') 'Rin'
        Assert-Throws { Invoke-RlsSetup -InstallDirectory $dir -PackagePath $base.Zip -ChecksumPath $base.Checksum -Version 'v9.9.9' -NoLaunch -NoShortcuts } '版本不一致' 'requested release mismatch'
        Assert-Test (-not (Test-Path -LiteralPath $dir)) 'version mismatch creates no installation'
        $existing = New-TestDirectory 'unregistered/Rin'
        Write-TestText (Join-Path $existing 'valuable.txt') 'do not delete'
        Assert-Throws { Install-Fixture $base $existing } '登记' 'install into unregistered directory'
        Assert-Throws { Uninstall-Fixture $existing } '登记' 'uninstall unregistered directory'
        Assert-Equal (Get-Content -LiteralPath (Join-Path $existing 'valuable.txt') -Raw) 'do not delete' 'unregistered contents preserved'
    }
    Test-Case 'junction installation paths and existing tree links are refused' {
        $target = New-TestDirectory 'junction/target'
        Write-TestText (Join-Path $target 'sentinel.txt') 'target bytes'
        $link = Assert-TestPath (Join-Path $script:TestRoot 'junction/link')
        $null = New-Item -ItemType Junction -Path $link -Target $target
        Assert-Test ([bool]((Get-Item -LiteralPath $link -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) 'fixture is actual junction'
        Assert-Throws { Install-Fixture $base (Join-Path $link 'Rin') } '链接' 'junction ancestor'
        Assert-Equal (Get-Content -LiteralPath (Join-Path $target 'sentinel.txt') -Raw) 'target bytes' 'junction target unchanged'
        $dir = Join-Path (New-TestDirectory 'tree-link') 'Rin'
        Install-Fixture $base $dir
        $null = New-Item -ItemType Junction -Path (Join-Path $dir 'unknown-link') -Target $target
        Assert-Throws { Install-Fixture $upgrade $dir } '链接' 'existing tree junction on upgrade'
        Assert-Throws { Uninstall-Fixture $dir } '链接' 'existing tree junction on uninstall'
        Assert-Equal (Get-RlsHash (Join-Path $dir 'RinsLittleShelter.exe')) $base.ExeHash 'link rejection precedes deletion'
        Assert-Equal (Get-Content -LiteralPath (Join-Path $target 'sentinel.txt') -Raw) 'target bytes' 'uninstall does not follow junction'
    }
    Test-Case 'GitHub metadata chooses only expected complete formal releases' {
        $script:MockRelease = New-ReleaseResponse
        $script:MockUris = New-Object Collections.Generic.List[string]
        function Invoke-WebRequest {
            [CmdletBinding()]param([string]$Uri,[switch]$UseBasicParsing,[int]$TimeoutSec,[hashtable]$Headers)
            $script:MockUris.Add($Uri)
            return [pscustomobject]@{Content=($script:MockRelease | ConvertTo-Json -Depth 10)}
        }
        $release = Get-RlsRelease ''
        Assert-Equal $release.Version 'v1.1.0' 'latest formal version'
        Assert-Test $script:MockUris[0].EndsWith('/releases/latest') 'latest route'
        $release = Get-RlsRelease 'v1.1.0'
        Assert-Test $script:MockUris[1].EndsWith('/releases/tags/v1.1.0') 'specific tag route'
        $before = $script:MockUris.Count
        Assert-Throws { Get-RlsRelease 'v1.1.0-preview.1' } '正式版号' 'requested prerelease tag'
        Assert-Equal $script:MockUris.Count $before 'invalid tag does not request network'
        $script:MockRelease = New-ReleaseResponse 'v1.1.0' $true
        Assert-Throws { Get-RlsRelease '' } '正式发行' 'server prerelease'
        $script:MockRelease = New-ReleaseResponse 'v1.1.0' $false $true
        Assert-Throws { Get-RlsRelease '' } '正式发行' 'server draft'
        $script:MockRelease = New-ReleaseResponse 'v1.2.0'
        Assert-Throws { Get-RlsRelease 'v1.1.0' } '正式发行' 'server returned wrong version'
        $script:MockRelease = New-ReleaseResponse
        $script:MockRelease.assets = @($script:MockRelease.assets | Where-Object name -ne 'SHA256.txt')
        Assert-Throws { Get-RlsRelease '' } '缺少' 'missing checksum asset'
        $script:MockRelease = New-ReleaseResponse
        $script:MockRelease.assets = @($script:MockRelease.assets | Where-Object name -eq 'SHA256.txt')
        Assert-Throws { Get-RlsRelease '' } '缺少' 'missing portable asset'
        $script:MockRelease = New-ReleaseResponse
        $script:MockRelease.assets += @($script:MockRelease.assets[0])
        Assert-Throws { Get-RlsRelease '' } '缺少' 'duplicate portable asset'
        $script:MockRelease = New-ReleaseResponse
        $script:MockRelease.assets += @($script:MockRelease.assets[1])
        Assert-Throws { Get-RlsRelease '' } '缺少' 'duplicate checksum asset'
        $script:MockRelease = New-ReleaseResponse
        $script:MockRelease.assets[0].browser_download_url = 'https://example.invalid/foreign.zip'
        Assert-Throws { Get-RlsRelease '' } '预期仓库' 'foreign asset URL'
    }
    Test-Case 'COM shortcuts use correct target working directory icon and ownership' {
        $dir = Join-Path (New-TestDirectory 'shortcuts') 'Rin'
        $links = New-TestDirectory 'shortcut-links'
        Install-Fixture $base $dir -Shortcuts -LinkRoot $links
        $shell = New-Object -ComObject WScript.Shell
        $manifest = Get-FixtureManifest $dir
        Assert-Equal @($manifest.Shortcuts).Count 3 'all shortcuts registered'
        foreach ($record in @($manifest.Shortcuts)) {
            $shortcut = $shell.CreateShortcut($record.Path)
            Assert-Equal $shortcut.TargetPath $record.Target 'COM target'
            Assert-Equal $shortcut.WorkingDirectory $dir 'COM working directory'
            Assert-Equal $shortcut.IconLocation ((Join-Path $dir 'assets/app/rin-shelter.ico')+',0') 'COM icon'
            Assert-Equal (Get-RlsHash $record.Path) $record.SHA256 'shortcut ownership digest'
        }
        Install-Fixture $base $dir -Shortcuts -LinkRoot $links
        Assert-Equal @((Get-FixtureManifest $dir).Shortcuts).Count 3 'same release keeps shortcut ownership'
        $modified = Join-Path $links "Desktop/Rin's Little Shelter.lnk"
        $shortcut = $shell.CreateShortcut($modified); $shortcut.Description = 'user modified'; $shortcut.Save()
        $changedHash = Get-RlsHash $modified
        Install-Fixture $upgrade $dir -Shortcuts -LinkRoot $links
        Assert-Equal (Get-RlsHash $modified) $changedHash 'upgrade preserves modified shortcut'
        Uninstall-Fixture $dir
        Assert-Test (Test-Path -LiteralPath $modified) 'uninstall preserves modified shortcut'
        foreach ($record in @($manifest.Shortcuts | Where-Object Path -ne $modified)) { Assert-Test (-not (Test-Path -LiteralPath $record.Path)) 'uninstall removes unchanged owned shortcut' }
        Assert-Equal (Get-RlsHash $modified) $changedHash 'uninstall preserves changed shortcut bytes'
    }
    Test-Case 'foreign same-name shortcut is never overwritten or uninstalled' {
        $dir = Join-Path (New-TestDirectory 'foreign-shortcut') 'Rin'
        $links = New-TestDirectory 'foreign-shortcut-links'
        $desktop = New-TestDirectory 'foreign-shortcut-links/Desktop'
        $foreign = Join-Path $desktop "Rin's Little Shelter.lnk"
        $foreignTarget = Join-Path $script:TestRoot 'foreign-target.exe'
        Write-TestText $foreignTarget 'inert foreign fixture'
        $shell = New-Object -ComObject WScript.Shell
        $shortcut = $shell.CreateShortcut($foreign); $shortcut.TargetPath=$foreignTarget; $shortcut.WorkingDirectory=$desktop; $shortcut.Save()
        $before = Get-RlsHash $foreign
        Install-Fixture $base $dir -Shortcuts -LinkRoot $links
        Assert-Equal (Get-RlsHash $foreign) $before 'foreign link bytes unchanged'
        Assert-Equal @((Get-FixtureManifest $dir).Shortcuts | Where-Object Path -eq $foreign).Count 0 'foreign link not claimed'
        Uninstall-Fixture $dir
        Assert-Test (Test-Path -LiteralPath $foreign) 'foreign link survives uninstall'
        Assert-Equal (Get-RlsHash $foreign) $before 'foreign link unchanged after uninstall'
    }
    Test-Case 'user-created same-target shortcut is not adopted' {
        $dir = Join-Path (New-TestDirectory 'same-target-shortcut') 'Rin'
        $links = New-TestDirectory 'same-target-links'
        $desktop = New-TestDirectory 'same-target-links/Desktop'
        $foreign = Join-Path $desktop "Rin's Little Shelter.lnk"
        $shell = New-Object -ComObject WScript.Shell
        $shortcut = $shell.CreateShortcut($foreign)
        $shortcut.TargetPath=Join-Path $dir 'RinsLittleShelter.exe'; $shortcut.WorkingDirectory=$dir; $shortcut.Save()
        $before = Get-RlsHash $foreign
        Install-Fixture $base $dir -Shortcuts -LinkRoot $links
        Assert-Equal (Get-RlsHash $foreign) $before 'same-target external link bytes unchanged'
        Assert-Equal @((Get-FixtureManifest $dir).Shortcuts | Where-Object Path -eq $foreign).Count 0 'same-target external link not claimed'
        Uninstall-Fixture $dir
        Assert-Test (Test-Path -LiteralPath $foreign) 'same-target external link survives uninstall'
        Assert-Equal (Get-RlsHash $foreign) $before 'same-target external link unchanged after uninstall'
    }
    Test-Case 'post-install launch error keeps verified installation committed' {
        $dir = Join-Path (New-TestDirectory 'launch-warning') 'Rin'
        $savedStart = (Get-Command Start-Process).ScriptBlock
        try {
            function Start-Process {
                [CmdletBinding()]param([string]$FilePath,[string]$WorkingDirectory)
                $null = Assert-TestPath $FilePath; $null = Assert-TestPath $WorkingDirectory
                $script:SimulatedLaunchCalls++
                throw 'INJECTED simulated process creation failure; nothing was executed.'
            }
            $warnings = @()
            Invoke-RlsSetup -InstallDirectory $dir -PackagePath $base.Zip -ChecksumPath $base.Checksum -NoShortcuts -DesktopDirectory (Join-Path $script:TestRoot 'launch-desktop') -StartMenuDirectory (Join-Path $script:TestRoot 'launch-start-menu') -WarningVariable warnings
        } finally { Set-Item -Path Function:Start-Process -Value $savedStart }
        Assert-Equal $script:SimulatedLaunchCalls 1 'launch failure injected once without creating a process'
        Assert-Test (@($warnings | Where-Object { $_.ToString() -match '安装已完成' }).Count -ge 1) 'launch failure is reported as a warning'
        Assert-Equal (Get-FixtureManifest $dir).Version $base.Version 'verified install remains registered'
        Assert-Equal (Get-RlsHash (Join-Path $dir 'RinsLittleShelter.exe')) $base.ExeHash 'verified payload remains installed'
        Assert-Equal @(Get-ChildItem -LiteralPath (Split-Path $dir -Parent) -Directory -Force | Where-Object Name -like '.rins-*').Count 0 'launch warning leaves no temporary directories'
    }
    Test-Case 'second upgrade move failure restores entire previous installation' {
        $dir = Join-Path (New-TestDirectory 'rollback') 'Rin'
        Install-Fixture $base $dir
        Write-TestText (Join-Path $dir 'unknown.txt') 'rollback user bytes'
        $beforeMarker = Get-RlsHash (Join-Path $dir '.rin-install.json')
        $script:MoveCalls = 0
        $savedMove = (Get-Command Move-RlsDirectory).ScriptBlock
        try {
            function Move-RlsDirectory([string]$From,[string]$To) {
                $null = Assert-TestPath $From; $null = Assert-TestPath $To
                $script:MoveCalls++
                if ($script:MoveCalls -eq 2) { throw 'INJECTED second directory move failure' }
                Move-Item -LiteralPath $From -Destination $To -ErrorAction Stop
            }
            Assert-Throws { Install-Fixture $upgrade $dir } 'INJECTED' 'commit move failure'
        } finally { Set-Item -Path Function:Move-RlsDirectory -Value $savedMove }
        Assert-Equal $script:MoveCalls 3 'old backup is restored with third move'
        Assert-Equal (Get-RlsHash (Join-Path $dir '.rin-install.json')) $beforeMarker 'rollback restores marker bytes'
        Assert-Equal (Get-RlsHash (Join-Path $dir 'RinsLittleShelter.exe')) $base.ExeHash 'rollback restores old EXE'
        Assert-Equal (Get-Content -LiteralPath (Join-Path $dir 'unknown.txt') -Raw) 'rollback user bytes' 'rollback restores unknown bytes'
        Assert-Equal @(Get-ChildItem -LiteralPath (Split-Path $dir -Parent) -Directory -Force | Where-Object Name -like '.rins-*').Count 0 'rollback cleans its bounded temporary paths'
    }
    Test-Case 'suite guards prove no launch and no real HKCU uninstall access' {
        Assert-Equal $script:LaunchCalls 0 'all fixtures remain unexecuted'
        Assert-Test ($script:AutoStartCalls.Count -ge 4) 'uninstall paths called registry mock'
        foreach ($path in $script:AutoStartCalls) { Assert-Test $path.StartsWith($script:TestRoot+'\',[StringComparison]::OrdinalIgnoreCase) 'every mocked autorun removal stayed inside sandbox' }
    }
} catch {
    $script:Results.Add([pscustomobject]@{Name='test harness setup';Passed=$false;Assertions=0;Milliseconds=0;Error=$_.Exception.Message;Stack=$_.ScriptStackTrace})
    Write-Host ('FAIL harness: ' + $_.Exception.Message) -ForegroundColor Red
} finally {
    $failures = @($script:Results | Where-Object { -not $_.Passed })
    $report = [ordered]@{
        Engine=$engineName; PowerShellVersion=$PSVersionTable.PSVersion.ToString(); Edition=[string]$PSVersionTable.PSEdition
        CLRVersion=[string]$PSVersionTable.CLRVersion; OSVersion=[Environment]::OSVersion.Version.ToString()
        StartedUtc=$started.ToString('o'); FinishedUtc=[DateTime]::UtcNow.ToString('o'); Sandbox=$script:TestRoot
        InstallerSHA256=(Get-FileHash -LiteralPath (Join-Path $projectRoot 'scripts/Install-RinsLittleShelter.ps1') -Algorithm SHA256).Hash.ToLowerInvariant()
        CaseCount=$script:Results.Count; PassedCases=($script:Results.Count-$failures.Count); FailedCases=$failures.Count; Assertions=$script:AssertionCount
        LaunchCalls=$script:LaunchCalls; SimulatedLaunchFailureCalls=$script:SimulatedLaunchCalls; MockedAutoStartCalls=$script:AutoStartCalls.Count
        Cases=@($script:Results.ToArray())
        Limitations=@('Synthetic tiny ZIP payloads and inert text EXE fixtures; real product binaries were never started.','GitHub release metadata requests were mocked; live download availability and network transport were not tested.','HKCU autorun access was replaced by a sandbox-only mock; registry removal behavior is not verified.','Native running-process refusal, manual GUI and CMD double-click flows, native background execution, Windows 10, and one-hour stability remain untested.','COM shortcut properties were inspected in isolated test folders; real user Desktop, Start Menu, installation and save directories were untouched.')
    }
    $reportPath = Join-Path $script:TestRoot 'results.json'
    [IO.File]::WriteAllText($reportPath,($report | ConvertTo-Json -Depth 12),[Text.UTF8Encoding]::new($true))
    Write-Host ("Installer tests: $($report.PassedCases)/$($report.CaseCount) cases; $($report.Assertions) assertions; $($report.FailedCases) failed. Report: $reportPath")
    Stop-Transcript | Out-Null
}
if (@($script:Results | Where-Object { -not $_.Passed }).Count) { exit 1 }
exit 0
