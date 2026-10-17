<#
  构建随心记事本：运行测试、编译 Release、生成安装包和更新清单。

  用法：
    .\build.ps1                         # 完整构建
    .\build.ps1 -Proxy http://127.0.0.1:7890   # 下载 Inno Setup 时走代理
    .\build.ps1 -SkipTests

  产物：
    dist\SuixinNotepad-Setup-<版本>.exe   安装包
    dist\latest.json                      更新清单，与安装包一起放到“更新地址”
#>
param(
    [string]$Proxy = "",
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$Root = $PSScriptRoot
$InnoVersion = '6.7.1'
$InnoSha256 = '00A5A07E8A4BD3A30364CB579D744677B37ADFC22E365099CB01B8BD1E651828'
$InnoDir = Join-Path $Root "tools\InnoSetup-$InnoVersion"
$Iscc = Join-Path $InnoDir 'tools\ISCC.exe'
$AppOut = Join-Path $Root 'build\app'
$Dist = Join-Path $Root 'dist'

function Invoke-Native([string]$File, [string[]]$Arguments) {
    & $File @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$File 执行失败，退出码 $LASTEXITCODE" }
}

# 版本号唯一来源：Directory.Build.props
$props = [xml](Get-Content -Raw -Encoding UTF8 (Join-Path $Root 'Directory.Build.props'))
$Version = ($props.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ }) | Select-Object -First 1
if (-not $Version -or $Version -notmatch '^\d+\.\d+\.\d+$') { throw "Directory.Build.props 中的版本号无效：$Version" }
Write-Host "版本 $Version"

if (-not $SkipTests) {
    Write-Host '运行测试…'
    Invoke-Native 'dotnet' @('test', (Join-Path $Root 'tests\SuixinNotepad.Tests\SuixinNotepad.Tests.csproj'), '-c', 'Release', '--nologo')
}

Write-Host '编译程序…'
if (Test-Path $AppOut) { Remove-Item -Recurse -Force $AppOut }
Invoke-Native 'dotnet' @('build', (Join-Path $Root 'src\SuixinNotepad\SuixinNotepad.csproj'), '-c', 'Release', '-o', $AppOut, '--nologo')

if (-not (Test-Path $Iscc)) {
    Write-Host "下载 Inno Setup $InnoVersion…"
    New-Item -ItemType Directory -Force (Join-Path $Root 'tools') | Out-Null
    $zip = Join-Path $Root "tools\Tools.InnoSetup.$InnoVersion.zip"
    $request = @{ Uri = "https://www.nuget.org/api/v2/package/Tools.InnoSetup/$InnoVersion"; OutFile = $zip; UseBasicParsing = $true }
    if ($Proxy) { $request.Proxy = $Proxy }
    [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
    Invoke-WebRequest @request
    $hash = (Get-FileHash $zip -Algorithm SHA256).Hash
    if ($hash -ne $InnoSha256) {
        Remove-Item -Force $zip
        throw "Inno Setup 安装包校验失败：$hash"
    }
    Expand-Archive -Path $zip -DestinationPath $InnoDir -Force
    Remove-Item -Force $zip
}

Write-Host '生成安装包…'
if (Test-Path $Dist) { Remove-Item -Recurse -Force $Dist }
New-Item -ItemType Directory -Force $Dist | Out-Null
Invoke-Native $Iscc @('/Qp', "/DAppVersion=$Version", "/DSourceDir=$AppOut", "/DOutputDir=$Dist", (Join-Path $Root 'installer\SuixinNotepad.iss'))

$setupName = "SuixinNotepad-Setup-$Version.exe"
$setup = Join-Path $Dist $setupName
if (-not (Test-Path $setup)) { throw "没有找到安装包 $setup" }

# 更新清单：程序按 version 判断是否有新版，下载 file 后用 sha256 校验
$sha = (Get-FileHash $setup -Algorithm SHA256).Hash.ToLowerInvariant()
$manifest = [ordered]@{ version = $Version; file = $setupName; sha256 = $sha } | ConvertTo-Json
[IO.File]::WriteAllText((Join-Path $Dist 'latest.json'), $manifest, (New-Object Text.UTF8Encoding $false))

Write-Host ''
Write-Host "完成：$setup"
Write-Host "更新清单：$(Join-Path $Dist 'latest.json')"
