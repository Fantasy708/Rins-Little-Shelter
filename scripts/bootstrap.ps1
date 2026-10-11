param()
$ErrorActionPreference = 'Stop'
$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$toolsDirectory = Join-Path $projectRoot '.tools'
$sdkDirectory = Join-Path $toolsDirectory 'dotnet'
$sdkArchive = Join-Path $toolsDirectory 'dotnet-sdk.zip'
$sdkUrl = 'https://builds.dotnet.microsoft.com/dotnet/Sdk/10.0.401/dotnet-sdk-10.0.401-win-x64.zip'
$expectedHash = '24b670ad3d923bfcf47df6c3b034152398b42f6dbc388e10d783aee1cfb5e5817d399fc0ae2a12cfa822a55e61d34830ccb15c50ef6efee437ab874bb7c79430'
New-Item -ItemType Directory -Path $toolsDirectory -Force | Out-Null
if (-not (Test-Path -LiteralPath (Join-Path $sdkDirectory 'sdk/10.0.401'))) {
    if (-not (Test-Path -LiteralPath $sdkArchive)) {
        Write-Output '从微软下载项目专用 .NET 10 SDK…'
        Invoke-WebRequest -Uri $sdkUrl -OutFile $sdkArchive
    }
    $actualHash = (Get-FileHash -LiteralPath $sdkArchive -Algorithm SHA512).Hash
    if ($actualHash -ne $expectedHash) { throw 'SDK 的 SHA512 与微软发布元数据不一致，请检查或重新下载 .tools/dotnet-sdk.zip。' }
    Expand-Archive -LiteralPath $sdkArchive -DestinationPath $sdkDirectory -Force
}
$env:DOTNET_CLI_HOME = Join-Path $toolsDirectory 'cli-home'
& (Join-Path $sdkDirectory 'dotnet.exe') --version
if ($LASTEXITCODE -ne 0) { throw '项目 SDK 验证失败。' }
Write-Output '已准备独立工具链。下一步运行 scripts/build.ps1。'
