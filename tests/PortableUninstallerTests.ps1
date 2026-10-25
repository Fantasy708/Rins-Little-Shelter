#requires -Version 5.1
<#
Isolated portable-uninstaller integration tests. Payload EXEs are inert text.
All mutations stay in output/portable-uninstaller-tests; no application launch,
live download, real HKCU access, Desktop, Start Menu, or save-file write occurs.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$engineName = if ($PSVersionTable.PSEdition -eq 'Core') { 'pwsh' } else { 'windows-powershell' }
$script:TestRoot = Join-Path (Join-Path $projectRoot 'output/portable-uninstaller-tests') ($engineName+'-'+$PSVersionTable.PSVersion.ToString()+'-'+[Guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $script:TestRoot -Force
$script:Results = New-Object Collections.Generic.List[object]
$script:Assertions = 0
$script:LaunchCalls = 0
$script:AutoStartCalls = New-Object Collections.Generic.List[string]
$script:Processes = @()
$started = [DateTime]::UtcNow
Start-Transcript -LiteralPath (Join-Path $script:TestRoot 'transcript.txt') -Force | Out-Null

function Assert-TestPath([string]$Path) {
    $full = [IO.Path]::GetFullPath($Path)
    if (-not $full.StartsWith($script:TestRoot+'\',[StringComparison]::OrdinalIgnoreCase)) { throw ('Test path escaped sandbox: '+$full) }
    return $full
}
function New-TestDirectory([string]$Relative) {
    $safe = Assert-TestPath (Join-Path $script:TestRoot $Relative)
    $null = New-Item -ItemType Directory -Path $safe -Force
    return $safe
}
function Write-TestText([string]$Path,[string]$Content) {
    $safe = Assert-TestPath $Path
    $null = New-Item -ItemType Directory -Path (Split-Path $safe -Parent) -Force
    [IO.File]::WriteAllText($safe,$Content,[Text.UTF8Encoding]::new($false))
}
function Assert-Test([bool]$Condition,[string]$Message) {
    $script:Assertions++
    if (-not $Condition) { throw ('Assertion failed: '+$Message) }
}
function Assert-Equal($Actual,$Expected,[string]$Message) { Assert-Test ($Actual -ceq $Expected) ($Message+'; expected=['+$Expected+']; actual=['+$Actual+']') }
function Assert-Throws([scriptblock]$Body,[string]$Message) {
    $caught = $null
    try { & $Body } catch { $caught = $_ }
    Assert-Test ($null -ne $caught) ($Message+' must throw')
}
function Test-Case([string]$Name,[scriptblock]$Body) {
    $before = $script:Assertions
    $watch = [Diagnostics.Stopwatch]::StartNew()
    try {
        & $Body
        $script:Results.Add([pscustomobject]@{Name=$Name;Passed=$true;Assertions=($script:Assertions-$before);Milliseconds=$watch.ElapsedMilliseconds;Error=$null})
        Write-Host ('PASS '+$Name)
    } catch {
        $script:Results.Add([pscustomobject]@{Name=$Name;Passed=$false;Assertions=($script:Assertions-$before);Milliseconds=$watch.ElapsedMilliseconds;Error=$_.Exception.Message;Stack=$_.ScriptStackTrace})
        Write-Host ('FAIL '+$Name+': '+$_.Exception.Message) -ForegroundColor Red
    }
}
function New-PortableFixture([string]$Name) {
    $dir = New-TestDirectory ('fixtures/'+$Name+'/Rin')
    Write-TestText (Join-Path $dir 'RinsLittleShelter.exe') 'INERT TEXT FIXTURE; NEVER EXECUTE; v1.1.0'
    Write-TestText (Join-Path $dir 'assets/manifest.json') '{"Version":1,"Fixture":true}'
    Write-TestText (Join-Path $dir 'assets/catalog.json') '{"Fixture":true}'
    Write-TestText (Join-Path $dir 'assets/app/rin-shelter.ico') 'inert fixture icon'
    Add-ShelterPortableUninstaller -Directory $dir -ProjectRoot $projectRoot -Version 'v1.1.0'
    return $dir
}
function Read-PortableFixture([string]$Directory) { return (Get-Content -LiteralPath (Join-Path $Directory '.rin-portable.json') -Encoding UTF8 -Raw | ConvertFrom-Json) }
function Write-PortableFixture([string]$Directory,$Manifest) { Write-TestText (Join-Path $Directory '.rin-portable.json') ($Manifest | ConvertTo-Json -Depth 15) }
function Uninstall-Fixture([string]$Directory) {
    $null = Assert-TestPath $Directory
    Invoke-RlsSetup -Action Uninstall -InstallDirectory $Directory -NoLaunch -NoShortcuts
}
function New-FixtureArchive([string]$Directory,[string]$Name) {
    $packageDir = New-TestDirectory ('archives/'+$Name)
    $zip = Join-Path $packageDir 'RinsLittleShelter-v1.1.0-win-x64.zip'
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::Open($zip,[IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($file in @(Get-ChildItem -LiteralPath $Directory -Recurse -Force -File)) {
            $relative = $file.FullName.Substring($Directory.Length+1).Replace('\','/')
            $entry = $archive.CreateEntry('RinsLittleShelter/'+$relative)
            $stream = $entry.Open()
            try { $bytes = [IO.File]::ReadAllBytes($file.FullName); $stream.Write($bytes,0,$bytes.Length) }
            finally { $stream.Dispose() }
        }
    } finally { $archive.Dispose() }
    $sha = Join-Path $packageDir 'SHA256.txt'
    Write-TestText $sha ((Get-RlsHash $zip)+'  '+[IO.Path]::GetFileName($zip)+"`r`n"+(Get-RlsHash (Join-Path $Directory 'RinsLittleShelter.exe'))+'  RinsLittleShelter/RinsLittleShelter.exe'+"`r`n")
    return [pscustomobject]@{Zip=$zip;SHA=$sha}
}

try {
    . (Join-Path $projectRoot 'scripts/Install-RinsLittleShelter.ps1')
    . (Join-Path $projectRoot 'scripts/portable-uninstall.ps1')
    . (Join-Path $projectRoot 'scripts/Uninstall-RinsLittleShelter.ps1')
    function Start-Process {
        [CmdletBinding()]param([string]$FilePath,[string]$WorkingDirectory)
        $script:LaunchCalls++
        throw 'TEST GUARD: no product process may be launched.'
    }
    function Remove-RlsAutoStart([string]$Directory) { $script:AutoStartCalls.Add((Assert-TestPath $Directory)) }
    function Get-Process { [CmdletBinding()]param([string]$Name) return $script:Processes }

    Test-Case 'helper creates relative complete ownership and both entry points' {
        $dir = New-PortableFixture 'manifest'
        $manifest = Read-PortableFixture $dir
        Assert-Equal $manifest.AppId 'Fantasy708.RinsLittleShelter' 'application identity'
        Assert-Equal $manifest.FormatVersion 1 'format'
        Assert-Equal $manifest.PackageKind 'Portable' 'portable kind'
        Assert-Equal $manifest.Root '.' 'relocatable root'
        Assert-Equal @($manifest.Shortcuts).Count 0 'no claimed shortcuts'
        Assert-Equal @($manifest.Files).Count 7 'four payload files and three uninstall support files'
        foreach ($file in @($manifest.Files)) {
            $path = Join-Path $dir $file.Path
            Assert-Test (Test-Path -LiteralPath $path -PathType Leaf) ('owned file exists: '+$file.Path)
            Assert-Equal (Get-RlsHash $path) $file.SHA256 ('owned hash: '+$file.Path)
            Assert-Equal (Get-Item -LiteralPath $path).Length $file.Bytes ('owned bytes: '+$file.Path)
            Assert-Test (-not [IO.Path]::IsPathRooted($file.Path)) 'ownership paths are relative'
        }
        Assert-Equal (Read-RlsManifest $dir).ResolvedMarker '.rin-portable.json' 'portable marker recognized'
        Assert-Test ((Get-Content -LiteralPath (Join-Path $dir 'Uninstall-RinsLittleShelter.cmd') -Raw) -match 'Uninstall-RinsLittleShelter\.ps1') 'CMD delegates to included script'
        Assert-Test ($null -ne (Get-Command Invoke-RlsPortableUninstall -CommandType Function)) 'dot-source declares wrapper without executing it'
    }
    Test-Case 'moved Chinese and spaces directory cleanly uninstalls itself' {
        $source = New-PortableFixture 'relocation'
        $destination = Assert-TestPath (Join-Path (New-TestDirectory '移动目录 带空格') '凛 的小世界')
        $null = Assert-TestPath $source
        Move-Item -LiteralPath $source -Destination $destination
        Assert-Test (-not (Test-Path -LiteralPath $source)) 'original location no longer exists'
        Assert-Equal (Read-RlsManifest $destination).Root '.' 'moved ownership remains valid'
        Uninstall-Fixture $destination
        Assert-Test (-not (Test-Path -LiteralPath $destination)) 'all unchanged owned files and empty root removed'
        Assert-Equal $script:AutoStartCalls[$script:AutoStartCalls.Count-1] $destination 'autorun mock uses relocated path'
    }
    Test-Case 'unknown modified and co-located save files remain byte exact' {
        $dir = New-PortableFixture 'preservation'
        $retained = @{'custom/用户笔记.txt'='user owned';'assets/catalog.json'='user modified catalog';'shelter.json'='personal save';'shelter.json.bak'='personal backup';'shelter.json.tmp'='personal temporary save'}
        $hashes = @{}
        foreach ($relative in $retained.Keys) { Write-TestText (Join-Path $dir $relative) $retained[$relative]; $hashes[$relative] = Get-RlsHash (Join-Path $dir $relative) }
        $null = New-Item -ItemType Directory -Path (Join-Path $dir 'user-empty-directory')
        Uninstall-Fixture $dir
        foreach ($relative in $retained.Keys) { Assert-Equal (Get-RlsHash (Join-Path $dir $relative)) $hashes[$relative] ('retained bytes: '+$relative) }
        Assert-Test (Test-Path -LiteralPath (Join-Path $dir 'user-empty-directory')) 'unknown empty directory retained'
        Assert-Test (-not (Test-Path -LiteralPath (Join-Path $dir 'RinsLittleShelter.exe'))) 'unchanged product removed'
        Assert-Test (-not (Test-Path -LiteralPath (Join-Path $dir '.rin-portable.json'))) 'portable ownership marker removed'
    }
    Test-Case 'default save directory and unsafe roots are rejected before any mutation' {
        $data = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'RinsLittleShelter'
        $count = $script:AutoStartCalls.Count
        foreach ($path in @($data,(Join-Path $data 'nested'),[IO.Path]::GetPathRoot($script:TestRoot),(Split-Path $data -Parent))) {
            Assert-Throws { Get-RlsInstallPath $path 'Uninstall' } ('refuse unsafe directory '+$path)
        }
        Assert-Equal $script:AutoStartCalls.Count $count 'no registry-removal call while refusing protected directories'
    }
    Test-Case 'corrupt structural manifests refuse deletion' {
        foreach ($kind in @('json','identity','format','kind','root','shortcuts','empty-files','missing-exe')) {
            $dir = New-PortableFixture ('corrupt-'+$kind)
            $m = Read-PortableFixture $dir
            switch ($kind) {
                'identity' { $m.AppId='Other.Application' }
                'format' { $m.FormatVersion=99 }
                'kind' { $m.PackageKind='Installed' }
                'root' { $m.Root='..' }
                'shortcuts' { $m.Shortcuts=@(@{Path='unexpected.lnk'}) }
                'empty-files' { $m.Files=@() }
                'missing-exe' { $m.Files=@($m.Files | Where-Object Path -ne 'RinsLittleShelter.exe') }
            }
            if ($kind -eq 'json') { Write-TestText (Join-Path $dir '.rin-portable.json') '{broken json' }
            else { Write-PortableFixture $dir $m }
            $hash = Get-RlsHash (Join-Path $dir 'RinsLittleShelter.exe')
            Assert-Throws { Uninstall-Fixture $dir } ('reject corrupt manifest '+$kind)
            Assert-Equal (Get-RlsHash (Join-Path $dir 'RinsLittleShelter.exe')) $hash ('no partial deletion '+$kind)
        }
    }
    Test-Case 'escaping duplicate invalid and save ownership entries refuse whole operation' {
        $paths = @('../outside.txt','/outside.txt','C:/outside.txt','assets\outside.txt','assets/file:stream','assets/trailing.','assets/NUL','.rin-portable.json','.rin-install.json','shelter.json','sub/shelter.json.bak','sub/shelter.json.tmp')
        $index = 0
        foreach ($relative in $paths) {
            $dir = New-PortableFixture ('unsafe-'+$index); $index++
            $m = Read-PortableFixture $dir
            $m.Files += [pscustomobject]@{Path=$relative;Bytes=0;SHA256=('0'*64)}
            Write-PortableFixture $dir $m
            Assert-Throws { Uninstall-Fixture $dir } ('reject owned path '+$relative)
            Assert-Test (Test-Path -LiteralPath (Join-Path $dir 'RinsLittleShelter.exe')) 'unsafe entry preflight leaves first owned file'
        }
        foreach ($kind in @('duplicate','digest','negative-bytes')) {
            $dir = New-PortableFixture ('bad-file-'+$kind)
            $m = Read-PortableFixture $dir
            if ($kind -eq 'duplicate') { $m.Files += [pscustomobject]@{Path='RINSLITTLESHELTER.EXE';Bytes=0;SHA256=('0'*64)} }
            elseif ($kind -eq 'digest') { $m.Files[0].SHA256='invalid digest' }
            else { $m.Files[0].Bytes=-1 }
            Write-PortableFixture $dir $m
            Assert-Throws { Uninstall-Fixture $dir } ('reject file record '+$kind)
            Assert-Test (Test-Path -LiteralPath (Join-Path $dir 'RinsLittleShelter.exe')) 'bad record prevents partial deletion'
        }
        Assert-Test (-not (Test-Path -LiteralPath (Join-Path $script:TestRoot 'outside.txt'))) 'no path traversal artifact'
    }
    Test-Case 'installed marker takes priority and never falls back from invalid installed record' {
        $dir = New-PortableFixture 'marker-priority'
        $m = Read-PortableFixture $dir
        $m | Add-Member -NotePropertyName InstallDirectory -NotePropertyValue $dir
        Write-TestText (Join-Path $dir '.rin-install.json') ($m | ConvertTo-Json -Depth 15)
        Assert-Equal (Read-RlsManifest $dir).ResolvedMarker '.rin-install.json' 'installed ownership wins'
        Write-TestText (Join-Path $dir '.rin-install.json') '{broken installed json'
        Assert-Throws { Uninstall-Fixture $dir } 'valid portable marker cannot bypass invalid installed ownership'
        Assert-Test (Test-Path -LiteralPath (Join-Path $dir 'RinsLittleShelter.exe')) 'invalid installed ownership preserves product'
    }
    Test-Case 'pack helper refuses personal saves installed roots and setup junctions' {
        foreach ($relative in @('shelter.json','sub/shelter.json.bak','sub/shelter.json.tmp','.rin-install.json')) {
            $dir = New-PortableFixture ('pack-refusal-'+[Guid]::NewGuid().ToString('N'))
            Write-TestText (Join-Path $dir $relative) 'do not claim me'
            $hash = Get-RlsHash (Join-Path $dir '.rin-portable.json')
            Assert-Throws { Add-ShelterPortableUninstaller $dir $projectRoot 'v1.1.0' } ('pack refusal '+$relative)
            Assert-Equal (Get-RlsHash (Join-Path $dir '.rin-portable.json')) $hash 'failed pack leaves ownership unchanged'
        }
        $dir = New-PortableFixture 'setup-link'
        $outside = New-TestDirectory 'outside-link-target'
        Write-TestText (Join-Path $outside 'sentinel.txt') 'outside contents'
        $setup = Assert-TestPath (Join-Path $dir '.setup')
        $backup = Assert-TestPath (Join-Path $dir '.setup-original')
        Move-Item -LiteralPath $setup -Destination $backup
        $null = New-Item -ItemType Junction -Path $setup -Target $outside
        Assert-Test ([bool]((Get-Item -LiteralPath $setup -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) 'fixture is a real junction'
        Assert-Throws { Add-ShelterPortableUninstaller $dir $projectRoot 'v1.1.0' } 'pack rejects setup junction'
        Assert-Throws { Uninstall-Fixture $dir } 'uninstall rejects setup junction before deletion'
        Assert-Test (Test-Path -LiteralPath (Join-Path $dir 'RinsLittleShelter.exe')) 'junction refusal preserves payload'
        Assert-Equal (Get-Content -LiteralPath (Join-Path $outside 'sentinel.txt') -Raw) 'outside contents' 'junction target bytes retained'
    }
    Test-Case 'running product refusal and foreign process do not kill processes' {
        $dir = New-PortableFixture 'running'
        try {
            $script:Processes = @([pscustomobject]@{Path=(Join-Path $dir 'RinsLittleShelter.exe')})
            Assert-Throws { Uninstall-Fixture $dir } 'running same-directory product refused'
            Assert-Test (Test-Path -LiteralPath (Join-Path $dir 'RinsLittleShelter.exe')) 'running refusal leaves payload'
            $script:Processes = @([pscustomobject]@{Path=(Join-Path $script:TestRoot 'different/RinsLittleShelter.exe')})
            Uninstall-Fixture $dir
            Assert-Test (-not (Test-Path -LiteralPath $dir)) 'foreign-directory process does not block unrelated portable cleanup'
        } finally { $script:Processes=@() }
    }
    Test-Case 'complete new portable ZIP installs then uses installed ownership to uninstall' {
        $source = New-PortableFixture 'install-source'
        $package = New-FixtureArchive $source 'new-portable'
        $destination = Assert-TestPath (Join-Path (New-TestDirectory 'installation') 'Rin 安装')
        Invoke-RlsSetup -InstallDirectory $destination -PackagePath $package.Zip -ChecksumPath $package.SHA -NoLaunch -NoShortcuts
        Assert-Test (Test-Path -LiteralPath (Join-Path $destination '.rin-install.json')) 'installed ownership created'
        Assert-Test (-not (Test-Path -LiteralPath (Join-Path $destination '.rin-portable.json'))) 'archive portable marker removed before installed ownership'
        $manifest = Read-RlsManifest $destination
        Assert-Equal $manifest.ResolvedMarker '.rin-install.json' 'installed engine selects installed ownership'
        foreach ($relative in @('Uninstall-RinsLittleShelter.cmd','Uninstall-RinsLittleShelter.ps1','.setup/Install-RinsLittleShelter.ps1')) {
            Assert-Test (Test-Path -LiteralPath (Join-Path $destination $relative)) ('installed support '+$relative)
            Assert-Equal @($manifest.Files | Where-Object Path -eq $relative).Count 1 ('installed support ownership '+$relative)
        }
        Uninstall-Fixture $destination
        Assert-Test (-not (Test-Path -LiteralPath $destination)) 'installed inert package cleans itself'
        Assert-Test (Test-Path -LiteralPath (Join-Path $source '.rin-portable.json')) 'original portable source remains untouched'
    }
    Test-Case 'wrapper missing-support failure returns nonzero without importing registry engine' {
        $dir = New-TestDirectory 'wrapper-missing-support'
        $entry = Join-Path $dir 'Uninstall-RinsLittleShelter.ps1'
        Copy-Item -LiteralPath (Join-Path $projectRoot 'scripts/Uninstall-RinsLittleShelter.ps1') -Destination $entry
        $command = Join-Path $PSHOME $(if ($PSVersionTable.PSEdition -eq 'Core') { 'pwsh.exe' } else { 'powershell.exe' })
        $priorPreference = $ErrorActionPreference
        try {
            $ErrorActionPreference='Continue'
            $response = & $command -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File $entry -NoPause 2>&1
            $childExit = $LASTEXITCODE
        } finally { $ErrorActionPreference=$priorPreference }
        Write-TestText (Join-Path $dir 'child-output.txt') ($response | Out-String)
        Assert-Equal $childExit 1 'missing support returns expected failure code'
        Assert-Test (-not (Test-Path -LiteralPath (Join-Path $dir '.rin-install.json'))) 'failed wrapper never claims or removes files'
        $source = Get-Content -LiteralPath $entry -Raw -Encoding UTF8
        Assert-Test ($source -match '\.setup/Install-RinsLittleShelter\.ps1') 'wrapper locates portable bundled engine'
        Assert-Test ($source -match "Join-Path \`$entryRoot 'Install-RinsLittleShelter.ps1'") 'wrapper locates Setup bundle engine'
        Assert-Test ($source -match '\$Directory = \$entryRoot') 'portable wrapper chooses its own root'
    }
    Test-Case 'wrapper rejects setup junction before importing any target code' {
        $dir = New-TestDirectory 'wrapper-setup-junction'
        $entry = Join-Path $dir 'Uninstall-RinsLittleShelter.ps1'
        Copy-Item -LiteralPath (Join-Path $projectRoot 'scripts/Uninstall-RinsLittleShelter.ps1') -Destination $entry
        Write-TestText (Join-Path $dir 'RinsLittleShelter.exe') 'INERT TEXT; DO NOT EXECUTE'
        $target = New-TestDirectory 'wrapper-junction-target'
        $sentinel = Assert-TestPath (Join-Path $target 'engine-executed.txt')
        $fakeEngine = Join-Path $target 'Install-RinsLittleShelter.ps1'
        $fakeBody = '[IO.File]::WriteAllText('''+$sentinel.Replace("'","''")+''',''UNEXPECTED ENGINE IMPORT'')'
        Write-TestText $fakeEngine $fakeBody
        $before = Get-RlsHash $fakeEngine
        $junction = Assert-TestPath (Join-Path $dir '.setup')
        $null = New-Item -ItemType Junction -Path $junction -Target $target
        Assert-Test ([bool]((Get-Item -LiteralPath $junction -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) 'wrapper fixture contains actual junction'
        $command = Join-Path $PSHOME $(if ($PSVersionTable.PSEdition -eq 'Core') { 'pwsh.exe' } else { 'powershell.exe' })
        $priorPreference = $ErrorActionPreference
        try {
            $ErrorActionPreference='Continue'
            $response = & $command -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File $entry -NoPause 2>&1
            $childExit = $LASTEXITCODE
        } finally { $ErrorActionPreference=$priorPreference }
        Write-TestText (Join-Path $dir 'child-output.txt') ($response | Out-String)
        Assert-Equal $childExit 1 'linked support returns expected failure code'
        Assert-Test (-not (Test-Path -LiteralPath $sentinel)) 'linked engine body was never imported or executed'
        Assert-Equal (Get-RlsHash $fakeEngine) $before 'linked support target bytes preserved'
        Assert-Test (Test-Path -LiteralPath (Join-Path $dir 'RinsLittleShelter.exe')) 'early link refusal leaves payload untouched'
    }
    Test-Case 'suite proves no product launch and only bounded mocked autorun removals' {
        Assert-Equal $script:LaunchCalls 0 'inert product fixtures never executed'
        Assert-Test ($script:AutoStartCalls.Count -ge 4) 'positive uninstalls reached autorun mock'
        foreach ($path in $script:AutoStartCalls) { Assert-Test $path.StartsWith($script:TestRoot+'\',[StringComparison]::OrdinalIgnoreCase) 'autorun calls restricted to disposable test paths' }
    }
} catch {
    $script:Results.Add([pscustomobject]@{Name='test harness setup';Passed=$false;Assertions=0;Milliseconds=0;Error=$_.Exception.Message;Stack=$_.ScriptStackTrace})
    Write-Host ('FAIL harness: '+$_.Exception.Message) -ForegroundColor Red
} finally {
    $failures = @($script:Results | Where-Object { -not $_.Passed })
    $report = [ordered]@{
        Engine=$engineName;PowerShellVersion=$PSVersionTable.PSVersion.ToString();Edition=[string]$PSVersionTable.PSEdition
        StartedUtc=$started.ToString('o');FinishedUtc=[DateTime]::UtcNow.ToString('o');Sandbox=$script:TestRoot
        InstallerSHA256=(Get-FileHash -LiteralPath (Join-Path $projectRoot 'scripts/Install-RinsLittleShelter.ps1') -Algorithm SHA256).Hash.ToLowerInvariant()
        PortableHelperSHA256=(Get-FileHash -LiteralPath (Join-Path $projectRoot 'scripts/portable-uninstall.ps1') -Algorithm SHA256).Hash.ToLowerInvariant()
        UninstallEntrySHA256=(Get-FileHash -LiteralPath (Join-Path $projectRoot 'scripts/Uninstall-RinsLittleShelter.ps1') -Algorithm SHA256).Hash.ToLowerInvariant()
        Cases=@($script:Results.ToArray());CaseCount=$script:Results.Count;PassedCases=($script:Results.Count-$failures.Count);FailedCases=$failures.Count;Assertions=$script:Assertions
        ProductLaunchCalls=$script:LaunchCalls;MockedAutoStartCalls=$script:AutoStartCalls.Count
        Limitations=@('All product EXEs are inert text fixtures; no real Rin binary was launched or uninstalled.','Registry removal is mocked and Get-Process is mocked; real HKCU and real process behavior are not verified.','Positive cleanup exercises the real shared engine, not native CMD double-click or a successful wrapper subprocess.','Two wrapper subprocesses verify early missing-support and setup-junction refusal; neither imports or executes the registry engine.','No real Desktop, Start Menu or save files were written; protected default save paths are checked via nonmutating validation.','Native background self-removal, GUI flows, Windows 10, and one-hour stability remain untested.')
    }
    $reportPath = Join-Path $script:TestRoot 'results.json'
    [IO.File]::WriteAllText($reportPath,($report | ConvertTo-Json -Depth 15),[Text.UTF8Encoding]::new($true))
    Write-Host ("Portable uninstall tests: $($report.PassedCases)/$($report.CaseCount) cases; $($report.Assertions) assertions; $($report.FailedCases) failed. Report: $reportPath")
    Stop-Transcript | Out-Null
}
if (@($script:Results | Where-Object { -not $_.Passed }).Count) { exit 1 }
exit 0
