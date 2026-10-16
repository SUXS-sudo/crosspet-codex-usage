param([string]$Dotnet = '', [string]$PythonEnvironment = $env:CROSSPET_PYTHON_ENV)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
if (!$Dotnet) { $Dotnet = 'dotnet' } # Rebuilding requires an installed .NET SDK or -Dotnet <path>.
$work = Join-Path $repo '.task-merge-usage'
$bin = Join-Path $work 'bin'
$target = Join-Path $repo '桌宠用量合并版'
New-Item -ItemType Directory -Path $work,$target -Force | Out-Null
& $Dotnet build (Join-Path $repo 'app\windows\CrossPet.csproj') -c Release -nologo -v quiet "-p:BaseIntermediateOutputPath=$work\obj\" "-p:OutputPath=$bin\"
if ($LASTEXITCODE -ne 0) { throw '编译失败' }
if (Get-Process CrossPet -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq (Join-Path $target 'CrossPet.exe') }) { throw '请先退出运行中的合并版，再重新打包。' }
Copy-Item -Path "$bin\*" -Destination $target -Recurse -Force
foreach ($folder in @('app\web','characters','integrations')) { Copy-Item -LiteralPath (Join-Path $repo $folder) -Destination $target -Recurse -Force }
New-Item -ItemType Directory -Path (Join-Path $target 'tools') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $repo 'tools\integrate.py') -Destination (Join-Path $target 'tools') -Force
foreach ($file in @('VERSION','LICENSE','app\windows\apps.json','app\icon\AppIcon.ico')) { Copy-Item -LiteralPath (Join-Path $repo $file) -Destination $target -Force }
Copy-Item -LiteralPath (Join-Path $repo 'docs\桌宠用量合并版.md') -Destination (Join-Path $target '使用说明.md') -Force
if ($PythonEnvironment -and !(Test-Path -LiteralPath (Join-Path $target 'python'))) {
    if (!(Test-Path -LiteralPath (Join-Path $PythonEnvironment 'conda-meta'))) { throw '请选择独立的 conda 环境路径。' }
    New-Item -ItemType Junction -Path (Join-Path $target 'python') -Target $PythonEnvironment | Out-Null
}
Write-Host "已生成 $target\CrossPet.exe"
