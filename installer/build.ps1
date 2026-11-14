param(
    [Parameter(Mandatory=$true)][string]$PayloadDirectory,
    [Parameter(Mandatory=$true)][string]$OutputFile,
    [string]$Version,
    [string]$WixCommand='wix'
)
$ErrorActionPreference='Stop'
$projectRoot=Split-Path -Parent $PSScriptRoot
if(-not $Version){[xml]$project=Get-Content -LiteralPath (Join-Path $projectRoot 'src/MemoLex.csproj') -Raw; $Version=$project.Project.PropertyGroup.Version}
$payload=(Resolve-Path -LiteralPath $PayloadDirectory).Path
$output=[IO.Path]::GetFullPath($OutputFile)
$generated=Join-Path (Split-Path -Parent $output) ('Payload-'+$Version+'.wxs')
python (Join-Path $PSScriptRoot 'generate_payload.py') $payload $generated
if($LASTEXITCODE -ne 0){throw 'Payload generation failed'}
& $WixCommand build (Join-Path $PSScriptRoot 'MemoLex.wxs') $generated -arch x64 -culture zh-CN -ext WixToolset.UI.wixext -d "PayloadDir=$payload" -d "SourceDir=$projectRoot" -d "Version=$Version" -pdbtype none -out $output
if($LASTEXITCODE -ne 0){throw 'Installer compilation failed'}
Get-Item -LiteralPath $output | Select-Object Name,Length
