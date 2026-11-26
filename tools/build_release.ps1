param(
    [string]$DataDirectory,
    [string]$WebViewDirectory,
    [string]$DotnetCommand='dotnet',
    [switch]$IncludeBrowserRuntime,
    [string]$OutputDirectory,
    [string]$SigningThumbprint,
    [string]$SignToolCommand='signtool.exe',
    [string]$DeepLKeyFile
)
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
if(-not $DataDirectory){$DataDirectory=Join-Path $repo 'data'}
$data=(Resolve-Path -LiteralPath $DataDirectory).Path
foreach($name in @('dictionary.data','dictionary.index.tsv','related.data','chinese.postings')){
    if(-not(Test-Path -LiteralPath (Join-Path $data $name))){throw 'Copy the complete data folder from the portable release first.'}
}
$destination=if($OutputDirectory){[IO.Path]::GetFullPath($OutputDirectory)}else{Join-Path $repo 'dist/MemoLex'}
if(Test-Path -LiteralPath (Join-Path $destination 'MemoLex.runtimeconfig.json')){throw 'Use a fresh output directory for the compact package layout.'}
$runtime=Join-Path $destination 'app'
if(-not $IncludeBrowserRuntime -and (Test-Path -LiteralPath (Join-Path $runtime 'webview2'))){throw 'The build folder contains an optional runtime from an earlier full build. Use a fresh build folder.'}
$translationArguments=@()
if($DeepLKeyFile) {
    $keyPath=(Resolve-Path -LiteralPath $DeepLKeyFile).Path
    if(-not (Test-Path -LiteralPath $keyPath -PathType Leaf)){throw 'The DeepL key file is missing.'}
    $translationArguments+="-p:MemoLexDeepLKeyFile=$keyPath"
}
& $DotnetCommand publish (Join-Path $repo 'src/MemoLex.csproj') -c Release -r win-x64 --self-contained true -p:RuntimeFrameworkVersion=8.0.34 -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false -o $runtime @translationArguments
if($LASTEXITCODE -ne 0){throw 'Application build failed'}
$targetData=Join-Path $runtime 'data';New-Item -ItemType Directory -Path $targetData -Force | Out-Null
Copy-Item -Path (Join-Path $data '*') -Destination $targetData -Recurse -Force
if(-not $WebViewDirectory){$WebViewDirectory=Join-Path $repo 'webview2'}
if($IncludeBrowserRuntime -and (Test-Path -LiteralPath (Join-Path $WebViewDirectory 'msedgewebview2.exe'))){
    $webviewTarget=Join-Path $runtime 'webview2';New-Item -ItemType Directory -Path $webviewTarget -Force | Out-Null
    Copy-Item -Path (Join-Path $WebViewDirectory '*') -Destination $webviewTarget -Recurse -Force
}elseif($IncludeBrowserRuntime){throw 'An optional browser runtime was requested but was not supplied.'}
foreach($file in Get-ChildItem -LiteralPath (Join-Path $repo 'mnemonics') -File){
    if($file.Name -in @('mnemonics.json','mnemonics.meta.json','mnemonics.revisions.json','mnemonics.sources.json','Mnemonics-CC-BY-SA-3.0.txt','README-Mnemonics.txt')){Copy-Item -LiteralPath $file.FullName -Destination $targetData -Force}
}
Copy-Item -LiteralPath (Join-Path $repo '使用说明.md') -Destination $runtime -Force
Copy-Item -LiteralPath (Join-Path $repo 'LICENSE') -Destination (Join-Path $runtime 'LICENSE.txt') -Force
[xml]$project=Get-Content -LiteralPath (Join-Path $repo 'src/MemoLex.csproj') -Raw
& (Join-Path $PSScriptRoot 'build_launcher.ps1') -PayloadDirectory $destination -Version $project.Project.PropertyGroup.Version
if($SigningThumbprint)
{
    $executables=@('MemoLex.exe','MemoLex.dll','Uninstall.exe','app\MemoLex.exe','app\Maintenance.exe') |
        ForEach-Object { Join-Path $destination $_ }
    & (Join-Path $PSScriptRoot 'sign_windows.ps1') -Files $executables -CertificateThumbprint $SigningThumbprint -SignToolCommand $SignToolCommand
}
$manifestArguments=@((Join-Path $PSScriptRoot 'write_package_manifest.py'),$destination,$project.Project.PropertyGroup.Version)
if($IncludeBrowserRuntime){$manifestArguments+='--include-browser-runtime'}
python @manifestArguments
if($LASTEXITCODE -ne 0){throw 'Package inventory failed'}
$application=Start-Process -FilePath (Join-Path $destination 'MemoLex.exe') -ArgumentList '--smoke-test' -WindowStyle Hidden -PassThru
if(-not $application.WaitForExit(30000) -or $application.ExitCode -ne 0){throw 'Offline verification failed'}
if($DeepLKeyFile -or (Test-Path -LiteralPath (Join-Path $repo 'src/Private/DeepL.key') -PathType Leaf)) {
    $smoke=Get-Content -LiteralPath (Join-Path $runtime 'smoke-test.json') -Raw | ConvertFrom-Json
    if($smoke.translationProvider -ne 'DeepL'){throw 'The DeepL key was not loaded by the packaged app.'}
}
Write-Output "Portable files: $destination"
