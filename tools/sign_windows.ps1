param(
    [Parameter(Mandatory=$true)][string[]]$Files,
    [Parameter(Mandatory=$true)][ValidatePattern('^[0-9a-fA-F]{40}$')][string]$CertificateThumbprint,
    [string]$SignToolCommand='signtool.exe',
    [uri]$TimestampUrl='https://timestamp.digicert.com'
)
$ErrorActionPreference='Stop'
if($TimestampUrl.Scheme -ne 'https'){throw 'Use an HTTPS RFC 3161 timestamp server.'}
$signTool=Get-Command -Name $SignToolCommand -CommandType Application -ErrorAction Stop
foreach($file in $Files)
{
    $path=(Resolve-Path -LiteralPath $file).Path
    & $signTool.Source sign /sha1 $CertificateThumbprint /fd SHA256 /tr $TimestampUrl.AbsoluteUri /td SHA256 $path
    if($LASTEXITCODE -ne 0){throw "Code signing failed: $path"}
    & $signTool.Source verify /pa /tw $path
    if($LASTEXITCODE -ne 0){throw "Signature or timestamp could not be verified: $path"}
}
