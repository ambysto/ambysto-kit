# windows/tests/admin/teardown.ps1 (Windows PowerShell 5.1, run as administrator by the gates runner after the
# AdminSetup suite, and by setup.ps1 first). Reverts everything setup.ps1 made for this clone: stops the holder task
# and process, restores the NoRecycleFiles policy to its recorded previous state, removes the File History fixture,
# then removes the fixture folder: every volume mount point inside is unmounted (mountvol /D), every symbolic link
# deleted as a link (never through it), ownership taken and ACLs reset (the fixtures with denies), and only when no
# reparse directory is left does `rmdir /s /q \\?\<folder>` delete the tree (PowerShell 5.1 cannot delete the 260+
# character fixture without the extended prefix). The standard user's own test folder is taken back and removed too
# (a killed fail-closed test can leave items it cannot delete). Leftovers of the earlier layouts under
# %PUBLIC%\Ambysto.Kit.Safety.Tests (flat, and per clone) are cleaned the same way.
# Exit 0 when everything is reverted, 1 otherwise (the runner then reports the machine may be left changed).
$ErrorActionPreference = 'Continue'
$clone = (Get-Item -LiteralPath $PSScriptRoot).Parent.Parent.Parent.Name
$base = Join-Path $env:SystemDrive 'AmbystoKitSafetyTests'
$dir = Join-Path $base $clone
$legacyBase = Join-Path $env:PUBLIC 'Ambysto.Kit.Safety.Tests'
$legacyDir = Join-Path $legacyBase $clone
$taskName = "AmbystoKitSafetyTestsHolder-$clone"
$legacyTaskName = 'AmbystoKitSafetyTestsHolder'
$policyKey = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer'
$policyName = 'NoRecycleFiles'
$legacyNames = @('with-file-symlink', 'with-directory-symlink', 'with-mount-point', 'without-mount-point', 'with-mount-point-and-long-path',
    'with-system-owned', 'with-trustedinstaller-owned', 'with-wof', 'administrators-owned', 'target-dir', 'target.txt', 'held.bin',
    'holder.ps1', 'holder.json', 'handoff.json', 'policy-previous.json', 'filehistory-previous.json')
$script:failed = $false

function Step([string]$Name, [scriptblock]$Body) {
    try { & $Body }
    catch { Write-Output "teardown: $Name failed: $_"; $script:failed = $true }
}

function Stop-HolderTask([string]$Task, [string]$Folder) {
    if (Get-ScheduledTask -TaskName $Task -ErrorAction SilentlyContinue) {
        Stop-ScheduledTask -TaskName $Task -ErrorAction SilentlyContinue
        Unregister-ScheduledTask -TaskName $Task -Confirm:$false
    }
    $json = Join-Path $Folder 'holder.json'
    if (Test-Path -LiteralPath $json) {
        $holderPid = [int](Get-Content -LiteralPath $json -Raw | ConvertFrom-Json).pid
        if ($holderPid -gt 0) { Stop-Process -Id $holderPid -Force -ErrorAction SilentlyContinue }
    }
    $holderScript = Join-Path $Folder 'holder.ps1'
    Get-CimInstance Win32_Process -Filter "Name='powershell.exe'" -ErrorAction SilentlyContinue |
        Where-Object { $_.CommandLine -and $_.CommandLine.IndexOf($holderScript, [System.StringComparison]::OrdinalIgnoreCase) -ge 0 } |
        ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
}

function Restore-Policy([string]$Folder) {
    $record = Join-Path $Folder 'policy-previous.json'
    if (-not (Test-Path -LiteralPath $record)) { return }
    $previous = Get-Content -LiteralPath $record -Raw | ConvertFrom-Json
    if ($previous.existed) {
        Set-ItemProperty -Path $policyKey -Name $policyName -Value ([int]$previous.value) -Type DWord
    } elseif (Test-Path $policyKey) {
        Remove-ItemProperty -Path $policyKey -Name $policyName -ErrorAction SilentlyContinue
        $keyExisted = ($previous.PSObject.Properties.Name -contains 'keyExisted') -and $previous.keyExisted
        if (-not $keyExisted) {
            $key = Get-Item -Path $policyKey
            if ($key.ValueCount -eq 0 -and $key.SubKeyCount -eq 0) { Remove-Item -Path $policyKey }
        }
    }
}

function Remove-FileHistoryFixture([string]$Folder) {
    $record = Join-Path $Folder 'filehistory-previous.json'
    if (-not (Test-Path -LiteralPath $record)) { return }
    $previous = Get-Content -LiteralPath $record -Raw | ConvertFrom-Json
    $root = 'E:\FileHistory'
    $file = if ($previous.PSObject.Properties.Name -contains 'file') { $previous.file } else { Join-Path $root 'ambysto-kit-safety-tests.txt' }
    if (Test-Path -LiteralPath $file) { Remove-Item -LiteralPath $file -Force }
    # Only a folder setup made is removed, and only when nothing else is in it; a real File History folder stays.
    if ($previous.created -and (Test-Path -LiteralPath $root) -and -not (Get-ChildItem -LiteralPath $root -Force)) { Remove-Item -LiteralPath $root -Force }
}

# A directory with a reparse point: a volume mount point is unmounted, a symbolic link or junction removed as a link.
function Remove-ReparseDirectory($Item) {
    $path = $Item.FullName
    $isMountPoint = $false
    try { $isMountPoint = [bool]($Item.Target | Where-Object { "$_" -match '^(\\\\\?\\)?Volume\{' }) } catch { }
    if ($isMountPoint -or -not $Item.LinkType) {
        & mountvol $path /D 2>&1 | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "mountvol /D failed for $path with exit code $LASTEXITCODE" }
        if (Test-Path -LiteralPath $path) { [System.IO.Directory]::Delete($path) }
    } else {
        [System.IO.Directory]::Delete($path)
    }
}

# Deletes a fixture tree: ownership taken and ACLs reset (fixtures with denies), mount points unmounted, links removed
# as links, then rmdir through the extended prefix.
function Remove-FixtureTree([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) { return }
    $item = Get-Item -LiteralPath $Path -Force
    if ($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) {
        # The path itself is a link or mount point: never recurse into it.
        if ($item.PSIsContainer) { Remove-ReparseDirectory $item } else { [System.IO.File]::Delete($Path) }
        return
    }
    if (-not $item.PSIsContainer) { Remove-Item -LiteralPath $Path -Force; return }
    & takeown /F $Path /R /D Y 2>&1 | Out-Null
    & icacls $Path /reset /T /C 2>&1 | Out-Null
    # Long paths inside make Get-ChildItem complain; those entries hold no links (setup put the mount points and links
    # at short paths), so the errors are ignored here and the final check below still guards the delete.
    Get-ChildItem -LiteralPath $Path -Recurse -Force -Attributes ReparsePoint -ErrorAction SilentlyContinue | ForEach-Object {
        if ($_.PSIsContainer) { Remove-ReparseDirectory $_ } elseif ($_.LinkType) { [System.IO.File]::Delete($_.FullName) }
    }
    $left = Get-ChildItem -LiteralPath $Path -Recurse -Force -Attributes ReparsePoint -ErrorAction SilentlyContinue |
        Where-Object { $_.PSIsContainer -or $_.LinkType }
    if ($left) { throw "reparse points are still inside $Path, not deleting it: $($left.FullName -join ', ')" }
    & cmd /c rmdir /s /q "\\?\$Path" 2>&1 | Out-Null
    if (Test-Path -LiteralPath $Path) { throw "still there after rmdir: $Path" }
}

# The standard user's own test folder can hold items it can no longer delete (a fail-closed test that denied READ_CONTROL
# to itself and to OWNER RIGHTS and was then killed): an administrator takes them back and removes them.
function Remove-StandardUserLeftovers {
    $consoleUser = (Get-CimInstance Win32_ComputerSystem -ErrorAction SilentlyContinue).UserName
    if (-not $consoleUser) { return }
    $sid = (New-Object System.Security.Principal.NTAccount($consoleUser)).Translate([System.Security.Principal.SecurityIdentifier]).Value
    $profile = (Get-ItemProperty -Path "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList\$sid" -ErrorAction SilentlyContinue).ProfileImagePath
    if (-not $profile) { return }
    $leftovers = Join-Path $profile 'AmbystoKitSafetyTests'
    if (-not (Test-Path -LiteralPath $leftovers)) { return }
    Remove-FixtureTree $leftovers
}

Step 'holder task' { Stop-HolderTask $taskName $dir }
Step 'legacy holder tasks' { Stop-HolderTask $taskName $legacyDir; Stop-HolderTask $legacyTaskName $legacyBase }
Step 'standard user leftovers' { Remove-StandardUserLeftovers }
Step 'policy' { Restore-Policy $dir; Restore-Policy $legacyDir; Restore-Policy $legacyBase }
Step 'file history fixture' { Remove-FileHistoryFixture $dir; Remove-FileHistoryFixture $legacyDir; Remove-FileHistoryFixture $legacyBase }
Step 'fixture folder' { Remove-FixtureTree $dir }
Step 'legacy fixtures' {
    Remove-FixtureTree $legacyDir
    if (Test-Path -LiteralPath $legacyBase) {
        foreach ($name in $legacyNames) { Remove-FixtureTree (Join-Path $legacyBase $name) }
    }
}

if ($script:failed) { exit 1 }
Write-Output 'teardown: reverted'
exit 0
