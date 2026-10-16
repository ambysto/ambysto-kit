# windows/tests/admin/teardown.ps1 (Windows PowerShell 5.1, run as administrator by the gates runner after the
# AdminSetup suite, and by setup.ps1 first). Reverts everything setup.ps1 made: stops the holder task and process,
# restores the NoRecycleFiles policy to its recorded previous state, unmounts the mount point, removes the symbolic
# links as links (never through them), then deletes the fixture folder. The folder is deleted only when no reparse
# point is left inside it, so a failed unmount can never make the delete recurse into the mounted volume.
# Exit 0 when everything is reverted, 1 otherwise (the runner then reports the machine may be left changed).
$ErrorActionPreference = 'Continue'
$dir = Join-Path $env:PUBLIC 'Ambysto.Kit.Safety.Tests'
$taskName = 'AmbystoKitSafetyTestsHolder'
$policyKey = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer'
$policyName = 'NoRecycleFiles'
$script:failed = $false

function Step([string]$Name, [scriptblock]$Body) {
    try { & $Body }
    catch { Write-Output "teardown: $Name failed: $_"; $script:failed = $true }
}

Step 'holder task' {
    if (Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue) {
        Stop-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
        Unregister-ScheduledTask -TaskName $taskName -Confirm:$false
    }
}

Step 'holder process' {
    $json = Join-Path $dir 'holder.json'
    if (Test-Path -LiteralPath $json) {
        $holderPid = [int](Get-Content -LiteralPath $json -Raw | ConvertFrom-Json).pid
        if ($holderPid -gt 0) { Stop-Process -Id $holderPid -Force -ErrorAction SilentlyContinue }
    }
    $holderScript = Join-Path $dir 'holder.ps1'
    Get-CimInstance Win32_Process -Filter "Name='powershell.exe'" -ErrorAction SilentlyContinue |
        Where-Object { $_.CommandLine -and $_.CommandLine.IndexOf($holderScript, [System.StringComparison]::OrdinalIgnoreCase) -ge 0 } |
        ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
}

Step 'policy' {
    $record = Join-Path $dir 'policy-previous.json'
    if (Test-Path -LiteralPath $record) {
        $previous = Get-Content -LiteralPath $record -Raw | ConvertFrom-Json
        if ($previous.existed) {
            Set-ItemProperty -Path $policyKey -Name $policyName -Value ([int]$previous.value) -Type DWord
        } elseif (Test-Path $policyKey) {
            Remove-ItemProperty -Path $policyKey -Name $policyName -ErrorAction SilentlyContinue
            # setup.ps1 created the key itself: remove it again when nothing else has been put in it since.
            $keyExisted = ($previous.PSObject.Properties.Name -contains 'keyExisted') -and $previous.keyExisted
            if (-not $keyExisted) {
                $key = Get-Item -Path $policyKey
                if ($key.ValueCount -eq 0 -and $key.SubKeyCount -eq 0) { Remove-Item -Path $policyKey }
            }
        }
    }
}

Step 'mount point' {
    $mount = Join-Path $dir 'with-mount-point\mnt'
    if (Test-Path -LiteralPath $mount) {
        $item = Get-Item -LiteralPath $mount -Force
        if ($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) {
            & mountvol $mount /D | Out-Null
            if ($LASTEXITCODE -ne 0) { throw "mountvol /D failed with exit code $LASTEXITCODE" }
        }
    }
}

Step 'symbolic links' {
    foreach ($link in @((Join-Path $dir 'with-file-symlink\file-symlink.txt'), (Join-Path $dir 'with-directory-symlink\directory-symlink'))) {
        if (Test-Path -LiteralPath $link) {
            $item = Get-Item -LiteralPath $link -Force
            if ($item.PSIsContainer) { [System.IO.Directory]::Delete($link) } else { [System.IO.File]::Delete($link) }
        }
    }
}

Step 'fixture folder' {
    if (Test-Path -LiteralPath $dir) {
        # Links and mount points must be gone before a recursive delete (it could otherwise follow them); the WOF
        # compressed file is a reparse point too, but a plain file to delete, so only links and directories count.
        $left = Get-ChildItem -LiteralPath $dir -Recurse -Force -Attributes ReparsePoint -ErrorAction SilentlyContinue |
            Where-Object { $_.PSIsContainer -or $_.LinkType }
        if ($left) { throw "reparse points are still inside the fixture folder, not deleting it: $($left.FullName -join ', ')" }
        & icacls $dir /setowner '*S-1-5-32-544' /T /C | Out-Null
        Remove-Item -LiteralPath $dir -Recurse -Force
    }
}

if ($script:failed) { exit 1 }
Write-Output 'teardown: reverted'
exit 0
