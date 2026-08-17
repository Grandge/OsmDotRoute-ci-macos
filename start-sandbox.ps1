<#
.SYNOPSIS
  Sandbox (Server + Web dev server) を起動する。

.DESCRIPTION
  Server (dotnet run) と Web (npm run dev) を別ウィンドウで並行起動する。
  各ウィンドウで Ctrl+C すると個別に停止できる。
  本スクリプトはリポジトリルートに置き、samples/Sandbox/ 配下の 2 つのプロジェクトを起動する。

.PARAMETER NoBrowser
  既定では Web 起動後にブラウザを http://localhost:5174 で開く。
  このフラグでブラウザ起動を抑止する。

.PARAMETER SkipNpmInstall
  node_modules が無いときに自動実行される npm install を強制スキップする場合に指定。

.PARAMETER ServerUrl
  Server の URL（既定 http://127.0.0.1:5280）。launchSettings.json と整合させること。

.PARAMETER WebUrl
  Web 開発サーバーの URL（既定 http://localhost:5174）。

.EXAMPLE
  ./start-sandbox.ps1

.EXAMPLE
  ./start-sandbox.ps1 -NoBrowser
#>
[CmdletBinding()]
param(
    [switch] $NoBrowser,
    [switch] $SkipNpmInstall,
    [string] $ServerUrl = 'http://127.0.0.1:5280',
    [string] $WebUrl = 'http://localhost:5174'
)

$ErrorActionPreference = 'Stop'
$repoRoot  = Split-Path -Parent $MyInvocation.MyCommand.Path
$serverDir = Join-Path $repoRoot 'samples/Sandbox/Server'
$webDir    = Join-Path $repoRoot 'samples/Sandbox/Web'

function Test-CommandAvailable {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string] $Name,
        [Parameter(Mandatory)] [string] $Hint
    )
    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "必須コマンドが見つかりません: $Name`n  $Hint"
    }
}

Test-CommandAvailable 'dotnet' '.NET 9 SDK をインストールしてください: https://dotnet.microsoft.com/download'
Test-CommandAvailable 'npm'    'Node.js (npm 同梱) をインストールしてください: https://nodejs.org/'

if (-not (Test-Path $serverDir)) { throw "Server ディレクトリが見つかりません: $serverDir" }
if (-not (Test-Path $webDir))    { throw "Web ディレクトリが見つかりません: $webDir" }

# Web: 初回のみ npm install
$nodeModules = Join-Path $webDir 'node_modules'
if (-not $SkipNpmInstall -and -not (Test-Path $nodeModules)) {
    Write-Host '[Sandbox] npm install を実行します (初回のみ)…' -ForegroundColor Cyan
    Push-Location $webDir
    try {
        npm install
        if ($LASTEXITCODE -ne 0) { throw "npm install が失敗しました (exit $LASTEXITCODE)" }
    } finally {
        Pop-Location
    }
}

# pwsh (PowerShell 7+) があれば優先。無ければ Windows PowerShell 5.1 を使う
$shellExe = $null
$pwsh = Get-Command 'pwsh' -ErrorAction SilentlyContinue
if ($pwsh) { $shellExe = $pwsh.Source }
if (-not $shellExe) { $shellExe = (Get-Command 'powershell').Source }

function Wait-ForHttpReady {
    param(
        [Parameter(Mandatory = $true)] [string] $Url,
        [Parameter(Mandatory = $true)] [string] $Label,
        [int] $TimeoutSeconds = 90
    )
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        try {
            $resp = Invoke-WebRequest -Uri $Url -UseBasicParsing -TimeoutSec 2 -ErrorAction Stop
            if ($resp.StatusCode -ge 200 -and $resp.StatusCode -lt 500) {
                return $true
            }
        } catch {
            Start-Sleep -Milliseconds 500
        }
    }
    Write-Host "[Sandbox] $Label の起動を $TimeoutSeconds 秒待っても応答がありません ($Url)" -ForegroundColor Yellow
    return $false
}

Write-Host "[Sandbox] Server を起動します ($ServerUrl)…" -ForegroundColor Cyan
$serverArgs = @(
    '-NoExit',
    '-NoProfile',
    '-Command',
    "Set-Location -LiteralPath '$serverDir'; `$Host.UI.RawUI.WindowTitle = 'Sandbox.Server'; dotnet run"
)
$serverProc = Start-Process -FilePath $shellExe -ArgumentList $serverArgs -PassThru

# Web は Server が listen 開始してから起動する (初期描画の ECONNREFUSED を防ぐ)
Write-Host '[Sandbox] Server の起動を待機中… (初回ビルドで 10〜30 秒かかります)' -ForegroundColor Cyan
$serverReady = Wait-ForHttpReady -Url ("$ServerUrl/api/version") -Label 'Server'
if ($serverReady) {
    Write-Host "[Sandbox] Server 起動 OK (PID=$($serverProc.Id))" -ForegroundColor Green
}

Write-Host "[Sandbox] Web 開発サーバーを起動します ($WebUrl)…" -ForegroundColor Cyan
$webArgs = @(
    '-NoExit',
    '-NoProfile',
    '-Command',
    "Set-Location -LiteralPath '$webDir'; `$Host.UI.RawUI.WindowTitle = 'Sandbox.Web'; npm run dev"
)
$webProc = Start-Process -FilePath $shellExe -ArgumentList $webArgs -PassThru

Write-Host ''
Write-Host '起動コマンドを 2 つのウィンドウへ送出しました。' -ForegroundColor Green
Write-Host ("  Server  PID={0}  ({1})" -f $serverProc.Id, $ServerUrl)
Write-Host ("  Web     PID={0}  ({1})" -f $webProc.Id,    $WebUrl)
Write-Host ''
Write-Host '停止するには各ウィンドウで Ctrl+C → ウィンドウを閉じてください。' -ForegroundColor Yellow

if (-not $NoBrowser) {
    if (Wait-ForHttpReady -Url $WebUrl -Label 'Web' -TimeoutSeconds 30) {
        Write-Host "[Sandbox] ブラウザを開きます: $WebUrl" -ForegroundColor Cyan
        Start-Process $WebUrl | Out-Null
    }
}
