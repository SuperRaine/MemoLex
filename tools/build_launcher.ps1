param([Parameter(Mandatory=$true)][string]$PayloadDirectory,[Parameter(Mandatory=$true)][string]$Version)
$ErrorActionPreference='Stop'
$project=Split-Path -Parent $PSScriptRoot
$payload=[IO.Path]::GetFullPath($PayloadDirectory)
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if(-not(Test-Path -LiteralPath $compiler)){throw 'The Windows .NET Framework compiler is required for the launcher.'}
$generated=Join-Path (Split-Path -Parent $payload) 'launcher-build'
New-Item -ItemType Directory -Path $generated -Force | Out-Null
$metadata=Join-Path $generated 'AssemblyInfo.cs'
$content=@"
using System.Reflection;
using System.Runtime.CompilerServices;
[assembly: AssemblyVersion("$Version.0")]
[assembly: AssemblyFileVersion("$Version.0")]
[assembly: AssemblyInformationalVersion("$Version")]
[assembly: AssemblyProduct("墨墨搜词 MemoLex")]
[assembly: AssemblyCompany("SuperRaine")]
[assembly: InternalsVisibleTo("MemoLex.PackagingTests")]
"@
[IO.File]::WriteAllText($metadata,$content,[Text.UTF8Encoding]::new($false))
$source=Join-Path $project 'launcher'
$common=@('/nologo','/optimize+','/platform:x64','/codepage:65001','/utf8output','/reference:System.Core.dll','/reference:System.Runtime.Serialization.dll','/reference:System.Windows.Forms.dll','/reference:System.Drawing.dll')
$core=@((Join-Path $source 'PackageModel.cs'),(Join-Path $source 'UninstallOperations.cs'),$metadata)
& $compiler @common /target:library ('/out:'+(Join-Path $payload 'MemoLex.dll')) @core (Join-Path $source 'UninstallWindow.cs') (Join-Path $source 'Bootstrapper.cs')
if($LASTEXITCODE -ne 0){throw 'Launcher library compilation failed'}
foreach($entry in @(@('MemoLex.exe','MemoLex.Entry.LauncherEntry'),@('Uninstall.exe','MemoLex.Entry.UninstallEntry'))){
    $compiled=Join-Path $generated $(if($entry[0] -eq 'MemoLex.exe'){'MemoLex.Launcher.exe'}else{$entry[0]})
    & $compiler @common /target:winexe ('/out:'+$compiled) ('/main:'+$entry[1]) ('/reference:'+(Join-Path $payload 'MemoLex.dll')) ('/win32icon:'+(Join-Path $project 'src/app.ico')) ('/win32manifest:'+(Join-Path $source 'app.manifest')) (Join-Path $source 'EntryPoints.cs') $metadata
    if($LASTEXITCODE -ne 0){throw 'Entry point compilation failed'}
    Copy-Item -LiteralPath $compiled -Destination (Join-Path $payload $entry[0]) -Force
}
& $compiler @common /target:winexe ('/out:'+(Join-Path $payload 'app\Maintenance.exe')) ('/main:MaintenanceEntry') ('/win32manifest:'+(Join-Path $source 'app.manifest')) @core (Join-Path $source 'MaintenanceEntry.cs')
if($LASTEXITCODE -ne 0){throw 'Maintenance helper compilation failed'}
