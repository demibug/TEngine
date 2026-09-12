[CmdletBinding()]
param(
    [int[]]$Ports = @(8081, 8082),
    [switch]$SkipInstall
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\UnityProject'))
$publishRoot = Join-Path $projectRoot 'LocalPublish'
$installScript = Join-Path $PSScriptRoot 'instal.bat'
$runtimeRoot = Join-Path $projectRoot 'Temp\LocalPublishServers'

if (-not (Test-Path -LiteralPath $publishRoot)) {
    New-Item -ItemType Directory -Path $publishRoot | Out-Null
    Write-Host "已创建发布目录：$publishRoot"
}

$serverCommand = Get-Command 'server.cmd' -CommandType Application -ErrorAction SilentlyContinue
if ($null -eq $serverCommand) {
    $serverCommand = Get-Command 'server' -CommandType Application -ErrorAction SilentlyContinue
}

if ($null -eq $serverCommand) {
    if ($SkipInstall) {
        throw '没有找到 server 命令，并且指定了 -SkipInstall。请先运行 Tools\FileServer\instal.bat。'
    }

    if (-not (Test-Path -LiteralPath $installScript)) {
        throw "找不到安装脚本：$installScript"
    }

    Write-Host '未检测到 yumu-static-server，开始执行 instal.bat...'
    & $installScript
    if ($LASTEXITCODE -ne 0) {
        throw "instal.bat 执行失败，退出码：$LASTEXITCODE"
    }

    $serverCommand = Get-Command 'server.cmd' -CommandType Application -ErrorAction SilentlyContinue
    if ($null -eq $serverCommand) {
        $serverCommand = Get-Command 'server' -CommandType Application -ErrorAction SilentlyContinue
    }
    if ($null -eq $serverCommand) {
        throw '安装完成后仍找不到 server 命令。请确认 npm 全局 bin 目录已经加入 PATH。'
    }
}

New-Item -ItemType Directory -Path $runtimeRoot -Force | Out-Null

foreach ($port in $Ports) {
    if ($port -lt 1 -or $port -gt 65535) {
        throw "端口不合法：$port"
    }

    $listener = Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue
    if ($null -ne $listener) {
        Write-Host "端口 $port 已被监听，跳过启动。"
        continue
    }

    $stdoutPath = Join-Path $runtimeRoot "server-$port.stdout.log"
    $stderrPath = Join-Path $runtimeRoot "server-$port.stderr.log"
    $process = Start-Process `
        -FilePath $serverCommand.Source `
        -ArgumentList @('-p', $port, '-cors', '--no-openbrowser') `
        -WorkingDirectory $publishRoot `
        -WindowStyle Hidden `
        -RedirectStandardOutput $stdoutPath `
        -RedirectStandardError $stderrPath `
        -PassThru

    Start-Sleep -Milliseconds 800
    if ($process.HasExited) {
        throw "端口 $port 的服务器启动失败。请检查日志：$stderrPath"
    }

    Write-Host "已启动：http://127.0.0.1:$port/（PID $($process.Id)）"
}

Write-Host "发布根目录：$publishRoot"
Write-Host "运行日志：$runtimeRoot"
