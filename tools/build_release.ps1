param([string]$DataDirectory,[string]$WebViewDirectory,[string]$DotnetCommand='dotnet')
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
if(-not $DataDirectory){$DataDirectory=Join-Path $repo 'data'}
$data=(Resolve-Path -LiteralPath $DataDirectory).Path
foreach($name in @('dictionary.data','dictionary.index.tsv','related.data','chinese.postings')){
    if(-not(Test-Path -LiteralPath (Join-Path $data $name))){throw 'Copy the complete data folder from the portable release first.'}
}
$destination=Join-Path $repo 'dist/MemoLex'
& $DotnetCommand publish (Join-Path $repo 'src/MemoLex.csproj') -c Release -r win-x64 --self-contained true -p:RuntimeFrameworkVersion=8.0.34 -p:DebugType=None -p:DebugSymbols=false -o $destination
if($LASTEXITCODE -ne 0){throw 'Application build failed'}
$targetData=Join-Path $destination 'data';New-Item -ItemType Directory -Path $targetData -Force | Out-Null
Copy-Item -Path (Join-Path $data '*') -Destination $targetData -Recurse -Force
if(-not $WebViewDirectory){$WebViewDirectory=Join-Path $repo 'webview2'}
if(Test-Path -LiteralPath (Join-Path $WebViewDirectory 'msedgewebview2.exe')){
    $webviewTarget=Join-Path $destination 'webview2';New-Item -ItemType Directory -Path $webviewTarget -Force | Out-Null
    Copy-Item -Path (Join-Path $WebViewDirectory '*') -Destination $webviewTarget -Recurse -Force
}else{Write-Warning 'Fixed WebView2 runtime was not supplied. The official login will require an installed Evergreen WebView2 runtime.'}
foreach($file in Get-ChildItem -LiteralPath (Join-Path $repo 'mnemonics') -File){
    if($file.Name -in @('mnemonics.json','mnemonics.meta.json','Mnemonics-CC-BY-SA-3.0.txt','README-Mnemonics.txt')){Copy-Item -LiteralPath $file.FullName -Destination $targetData -Force}
}
Copy-Item -LiteralPath (Join-Path $repo '使用说明.md') -Destination $destination -Force
Copy-Item -LiteralPath (Join-Path $repo 'LICENSE') -Destination (Join-Path $destination 'LICENSE.txt') -Force
$application=Start-Process -FilePath (Join-Path $destination 'MemoLex.exe') -ArgumentList '--smoke-test' -WindowStyle Hidden -PassThru
if(-not $application.WaitForExit(30000) -or $application.ExitCode -ne 0){throw 'Offline verification failed'}
Write-Output "Portable files: $destination"
