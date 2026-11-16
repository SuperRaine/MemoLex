param([switch]$TestOnly)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
$jobRoot = [IO.Path]::GetFullPath($PSScriptRoot)
$job = Get-Content -LiteralPath (Join-Path $jobRoot 'job.json') -Raw | ConvertFrom-Json
function NormalDirectory([string]$path) {
    $full=[IO.Path]::GetFullPath($path)
    if($full.TrimEnd('\') -eq [IO.Path]::GetPathRoot($full).TrimEnd('\')){return [IO.Path]::GetPathRoot($full)}
    return $full.TrimEnd('\')
}
$appRoot = NormalDirectory $job.AppDirectory
$sourceRoot = if($job.SourceDirectory){NormalDirectory $job.SourceDirectory}else{$appRoot}
$sourceExe = Join-Path $sourceRoot 'MemoLex.exe'
$exe = Join-Path $appRoot 'MemoLex.exe'
$changed = [Collections.Generic.List[object]]::new()
$exited = $false
function CheckedPath([string]$root, [string]$relative) {
    if ([IO.Path]::IsPathRooted($relative) -or $relative.Contains(':') -or ($relative -split '[/\\]' | Where-Object { $_ -eq '..' -or $_ -eq '.' })) { throw 'Invalid update path' }
    $base = [IO.Path]::GetFullPath($root).TrimEnd('\') + '\'
    $target = [IO.Path]::GetFullPath((Join-Path $base $relative))
    if (-not $target.StartsWith($base, [StringComparison]::OrdinalIgnoreCase)) { throw 'Update path outside directory' }
    $probe = $target
    while ($probe -and $probe.Length -ge $base.TrimEnd('\').Length) {
        if (Test-Path -LiteralPath $probe) {
            if (((Get-Item -LiteralPath $probe -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Linked update path is not supported' }
        }
        $probe = [IO.Path]::GetDirectoryName($probe)
    }
    return $target
}
try {
    if ($appRoot.TrimEnd('\') -eq [IO.Path]::GetPathRoot($appRoot).TrimEnd('\') -or -not (Test-Path -LiteralPath $sourceExe -PathType Leaf)) { throw 'Invalid application directory' }
    if([Diagnostics.FileVersionInfo]::GetVersionInfo($sourceExe).ProductName -ne '墨墨搜词 MemoLex'){throw 'Application identity changed'}
    $migration=-not $sourceRoot.Equals($appRoot,[StringComparison]::OrdinalIgnoreCase)
    if($migration){
        if($job.Portable -or $sourceRoot.TrimEnd('\') -ne [IO.Path]::GetPathRoot($sourceRoot).TrimEnd('\') -or -not $appRoot.Equals((Join-Path $sourceRoot 'MemoLex'),[StringComparison]::OrdinalIgnoreCase)){throw 'Invalid update destination'}
        $null=CheckedPath $sourceRoot 'MemoLex'
        $existing=@(Get-ChildItem -LiteralPath $appRoot -Force -ErrorAction SilentlyContinue)
        if($existing.Count -gt 0 -and (-not(Test-Path -LiteralPath $exe -PathType Leaf) -or [Diagnostics.FileVersionInfo]::GetVersionInfo($exe).ProductName -ne '墨墨搜词 MemoLex')){throw 'Update destination contains unrelated files'}
    }
    $hashStream=[IO.File]::OpenRead($job.Package)
    $hashAlgorithm=[Security.Cryptography.SHA256]::Create()
    try { $packageHash=[BitConverter]::ToString($hashAlgorithm.ComputeHash($hashStream)).Replace('-','') }
    finally { $hashStream.Dispose();$hashAlgorithm.Dispose() }
    if ($packageHash -ne $job.Sha256) { throw 'Package checksum changed' }
    $targets = @()
    if ($job.Portable) {
        foreach ($relative in $job.Files) {
            $source = CheckedPath (Join-Path $jobRoot 'stage') $relative
            $target = CheckedPath $appRoot $relative
            $backup = CheckedPath (Join-Path $jobRoot 'backup') $relative
            if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { throw 'Staged file missing' }
            $targets += [pscustomobject]@{ Source=$source; Target=$target; Backup=$backup; Existed=(Test-Path -LiteralPath $target) }
        }
        # Test write permissions while the current app is still running.
        $probePath = Join-Path $appRoot ('.memolex-update-' + [Guid]::NewGuid().ToString('N'))
        [IO.File]::WriteAllText($probePath, 'update')
        Remove-Item -LiteralPath $probePath
    }
    [IO.File]::WriteAllText((Join-Path $jobRoot 'ready'), 'ready')
    $process = Get-Process -Id $job.ProcessId -ErrorAction SilentlyContinue
    if ($process) {
        if ($process.StartTime.ToUniversalTime().Ticks -ne $job.ProcessStart) { throw 'Application process changed' }
        if (-not $process.WaitForExit(300000)) { throw 'Application did not exit' }
    }
    $exited = $true
    if ($job.Portable) {
        foreach ($file in $targets) {
            if ($file.Existed) {
                [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($file.Backup)) | Out-Null
                [IO.File]::Copy($file.Target, $file.Backup, $false)
            }
            $changed.Add($file)
            [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($file.Target)) | Out-Null
            [IO.File]::Copy($file.Source, $file.Target, $true)
        }
    } else {
        $msi = Start-Process -FilePath 'msiexec.exe' -ArgumentList @('/i', ('"' + $job.Package + '"'), '/passive', '/norestart', 'MEMOLEX_KEEP_USER_DATA=1', ('INSTALLFOLDER="' + $appRoot + '"')) -Wait -PassThru -WindowStyle Hidden
        if ($msi.ExitCode -notin @(0, 3010)) { throw ('Installer exit code ' + $msi.ExitCode) }
    }
    if(-not(Test-Path -LiteralPath $exe -PathType Leaf)){throw 'Updated application is missing'}
    if($migration){
        $runKey='HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
        $run=(Get-ItemProperty -LiteralPath $runKey -Name MemoLex -ErrorAction SilentlyContinue).MemoLex
        if($run -and $run.Trim('"').Equals($sourceExe,[StringComparison]::OrdinalIgnoreCase)){Set-ItemProperty -LiteralPath $runKey -Name MemoLex -Value ('"'+$exe+'"')}
    }
    [IO.File]::WriteAllText((Join-Path $jobRoot 'result.txt'), 'success')
    if (-not $TestOnly) { Start-Process -FilePath $exe -WorkingDirectory $appRoot }
} catch {
    for ($i = $changed.Count - 1; $i -ge 0; $i--) {
        $file = $changed[$i]
        try {
            if ($file.Existed) { [IO.File]::Copy($file.Backup, $file.Target, $true) }
            elseif (Test-Path -LiteralPath $file.Target -PathType Leaf) { Remove-Item -LiteralPath $file.Target }
        } catch { }
    }
    [IO.File]::WriteAllText((Join-Path $jobRoot 'result.txt'), 'failed: ' + $_.Exception.Message)
    if (-not $TestOnly) {
        [Windows.Forms.MessageBox]::Show('更新未完成，已尝试恢复原程序。请打开 GitHub 发布页重新下载安装。', '墨墨搜词更新') | Out-Null
        if ($exited -and (Test-Path -LiteralPath $sourceExe)) { Start-Process -FilePath $sourceExe -WorkingDirectory $sourceRoot }
    }
}
