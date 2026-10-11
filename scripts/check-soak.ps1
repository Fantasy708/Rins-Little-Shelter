param([string]$Csv = 'docs/verification/soak.csv', [switch]$Wait)
$ErrorActionPreference = 'Stop'
$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$csvPath = if ([System.IO.Path]::IsPathRooted($Csv)) { $Csv } else { Join-Path $projectRoot $Csv }
$deadline = [DateTime]::UtcNow.AddMinutes(75)
while ($Wait -and -not (Test-Path -LiteralPath "$csvPath.complete")) {
    if ([DateTime]::UtcNow -gt $deadline) { throw '等待稳定性检查完成超时。' }
    if (Test-Path -LiteralPath $csvPath) {
        $latest = Import-Csv -LiteralPath $csvPath | Select-Object -Last 1
        if ($latest) { Write-Output ("稳定性 {0:0.0}/60 分钟 · 内存 {1} MB · CPU {2}% · 帧 {3} · {4}" -f ([double]$latest.seconds/60),$latest.working_set_mb,$latest.cpu_percent,$latest.frames,$latest.action) }
    }
    Start-Sleep -Seconds 50
}
if (-not (Test-Path -LiteralPath "$csvPath.complete")) { throw '测试尚未完成，不能报告通过。' }
$rows = @(Import-Csv -LiteralPath $csvPath)
if ($rows.Count -eq 0 -or [double]$rows[-1].seconds -lt 3600) { throw '运行不足一小时。' }
$warm = @($rows | Where-Object { [double]$_.seconds -ge 120 })
$cpu = $warm | ForEach-Object { [double]$_.cpu_percent } | Measure-Object -Average -Maximum
$memory = $warm | ForEach-Object { [double]$_.working_set_mb } | Measure-Object -Average -Minimum -Maximum
$handles = $warm | ForEach-Object { [int]$_.handles } | Measure-Object -Minimum -Maximum
$gap = 0.0
for ($i=1;$i -lt $rows.Count;$i++) { $gap=[Math]::Max($gap, [double]$rows[$i].seconds-[double]$rows[$i-1].seconds) }
$summary = [ordered]@{ Seconds=[double]$rows[-1].seconds; Samples=$rows.Count; CpuAveragePercent=[Math]::Round($cpu.Average,3); CpuMaxPercent=[Math]::Round($cpu.Maximum,3); WorkingSetAverageMB=[Math]::Round($memory.Average,1); WorkingSetMinMB=$memory.Minimum; WorkingSetMaxMB=$memory.Maximum; WorkingSetFinalMB=[double]$rows[-1].working_set_mb; HandleMin=$handles.Minimum; HandleMax=$handles.Maximum; HandleFinal=[int]$rows[-1].handles; RenderFPS=[Math]::Round(([long]$warm[-1].frames-[long]$warm[0].frames)/([double]$warm[-1].seconds-[double]$warm[0].seconds),2); LongestSampleGapSeconds=[Math]::Round($gap,1); AudioMaxLevel=($rows|ForEach-Object{[double]$_.audio_level}|Measure-Object -Maximum).Maximum; Actions=@($rows.action|Sort-Object -Unique); Completed=Get-Content -LiteralPath "$csvPath.complete" }
$summary | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath ($csvPath + '.summary.json') -Encoding utf8
$summary | ConvertTo-Json -Depth 4
