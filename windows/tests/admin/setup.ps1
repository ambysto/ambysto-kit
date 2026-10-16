# windows/tests/admin/setup.ps1 (Windows PowerShell 5.1, run as administrator by the gates runner before the
# AdminSetup suite; teardown.ps1 reverts everything). It makes what a standard user cannot:
#   1. the NoRecycleFiles machine policy (the previous value is recorded and restored);
#   2. a file and a directory symbolic link, each alone in a folder;
#   3. a volume mount point (the fixed exFAT volume G: mounted in a folder), alone in a folder;
#   4. a SYSTEM-owned file in a folder, and an Administrators-owned folder with a plain file;
#   5. an elevated holder: a scheduled task running as SYSTEM keeps held.bin open without delete sharing.
# Everything lives under %PUBLIC%\Ambysto.Kit.Safety.Tests (reachable by both accounts) and is described in
# handoff.json there, which the tests read. Exit 0 on success, 1 on any failure (the runner checks).
$ErrorActionPreference = 'Stop'
$dir = Join-Path $env:PUBLIC 'Ambysto.Kit.Safety.Tests'
$taskName = 'AmbystoKitSafetyTestsHolder'
$policyKey = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer'
$policyName = 'NoRecycleFiles'
$usersSid = '*S-1-5-32-545'
$systemSid = '*S-1-5-18'
$administratorsSid = '*S-1-5-32-544'

function Invoke-Native([string]$What, [scriptblock]$Body) {
    & $Body | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "$What failed with exit code $LASTEXITCODE" }
}

try {
    # A previous run that never reached teardown leaves its fixtures and holder: undo them first.
    & (Join-Path $PSScriptRoot 'teardown.ps1') | Out-Null

    New-Item -ItemType Directory -Path $dir -Force | Out-Null
    Invoke-Native 'icacls grant' { icacls $dir /grant "${usersSid}:(OI)(CI)RX" }

    # 1. Policy: record, then disallow.
    $previous = @{ existed = $false; value = 0; keyExisted = (Test-Path $policyKey) }
    if (Test-Path $policyKey) {
        $item = Get-ItemProperty -Path $policyKey -Name $policyName -ErrorAction SilentlyContinue
        if ($null -ne $item) { $previous = @{ existed = $true; value = [int]$item.$policyName; keyExisted = $true } }
    }
    $previous | ConvertTo-Json | Set-Content -Path (Join-Path $dir 'policy-previous.json') -Encoding UTF8
    New-Item -Path $policyKey -Force | Out-Null
    New-ItemProperty -Path $policyKey -Name $policyName -Value 1 -PropertyType DWord -Force | Out-Null

    # 2. Symbolic links.
    $target = Join-Path $dir 'target.txt'
    Set-Content -Path $target -Value 'target' -Encoding ASCII
    $targetDir = Join-Path $dir 'target-dir'
    New-Item -ItemType Directory -Path $targetDir | Out-Null
    Set-Content -Path (Join-Path $targetDir 'inner.txt') -Value 'inner' -Encoding ASCII
    $withFileLink = Join-Path $dir 'with-file-symlink'
    New-Item -ItemType Directory -Path $withFileLink | Out-Null
    $fileLink = Join-Path $withFileLink 'file-symlink.txt'
    New-Item -ItemType SymbolicLink -Path $fileLink -Target $target | Out-Null
    $withDirLink = Join-Path $dir 'with-directory-symlink'
    New-Item -ItemType Directory -Path $withDirLink | Out-Null
    $dirLink = Join-Path $withDirLink 'directory-symlink'
    New-Item -ItemType SymbolicLink -Path $dirLink -Target $targetDir | Out-Null

    # 3. Mount point.
    $withMount = Join-Path $dir 'with-mount-point'
    New-Item -ItemType Directory -Path $withMount | Out-Null
    $mount = Join-Path $withMount 'mnt'
    New-Item -ItemType Directory -Path $mount | Out-Null
    $volume = (Get-Volume -DriveLetter G -ErrorAction Stop).UniqueId
    if (-not $volume) { throw 'no volume GUID for G:' }
    Invoke-Native 'mountvol' { mountvol $mount $volume }

    # 4. Ownership.
    $withSystemOwned = Join-Path $dir 'with-system-owned'
    New-Item -ItemType Directory -Path $withSystemOwned | Out-Null
    $systemOwned = Join-Path $withSystemOwned 'system-owned.txt'
    Set-Content -Path $systemOwned -Value 'owned by SYSTEM' -Encoding ASCII
    Invoke-Native 'icacls setowner SYSTEM' { icacls $systemOwned /setowner $systemSid }
    $administratorsOwned = Join-Path $dir 'administrators-owned'
    New-Item -ItemType Directory -Path $administratorsOwned | Out-Null
    $administratorsFile = Join-Path $administratorsOwned 'plain.txt'
    Set-Content -Path $administratorsFile -Value 'owned by Administrators' -Encoding ASCII
    Invoke-Native 'icacls setowner Administrators' { icacls $administratorsOwned /setowner $administratorsSid /T }
    $sidType = [System.Security.Principal.SecurityIdentifier]
    if ((Get-Acl -LiteralPath $systemOwned).GetOwner($sidType).Value -ne 'S-1-5-18') { throw 'the SYSTEM-owned file is not owned by SYSTEM' }
    if ((Get-Acl -LiteralPath $administratorsOwned).GetOwner($sidType).Value -ne 'S-1-5-32-544') { throw 'the folder is not owned by Administrators' }

    # 4a. A TrustedInstaller-owned file outside every system location ("Owned by Windows" on its own), readable by Users.
    $withTrustedInstaller = Join-Path $dir 'with-trustedinstaller-owned'
    New-Item -ItemType Directory -Path $withTrustedInstaller | Out-Null
    $trustedInstallerFile = Join-Path $withTrustedInstaller 'trustedinstaller-owned.txt'
    Set-Content -Path $trustedInstallerFile -Value 'owned by TrustedInstaller' -Encoding ASCII
    Invoke-Native 'icacls grant Users (TI file)' { icacls $trustedInstallerFile /grant "${usersSid}:RX" }
    Invoke-Native 'icacls setowner TrustedInstaller' { icacls $trustedInstallerFile /setowner 'NT SERVICE\TrustedInstaller' }
    if ((Get-Acl -LiteralPath $trustedInstallerFile).GetOwner($sidType).Value -ne 'S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464') { throw 'the file is not owned by TrustedInstaller' }

    # 4b. A WOF-compressed file (compact /exe), which only an administrator could make on this machine; owned by the
    # standard user (the account logged on to the console) with full control, so it is the user's own item to the gate.
    $withWof = Join-Path $dir 'with-wof'
    New-Item -ItemType Directory -Path $withWof | Out-Null
    $wofFile = Join-Path $withWof 'compressed.bin'
    [System.IO.File]::WriteAllBytes($wofFile, (New-Object byte[] 1048576))
    Invoke-Native 'compact /exe' { compact /c /exe:xpress4k $wofFile }
    # The WOF filter hides the reparse point from the attribute APIs (measured: Get-Item shows Archive only, fsutil
    # reparsepoint query says "not a reparse point"), so the proof is the on-disk layout: tag 0x80000017.
    $layout = (& fsutil file layout $wofFile 2>&1) -join "`n"
    if ($layout -notmatch '0x80000017') { throw "compact /exe did not make a WOF reparse point (fsutil file layout shows no tag 0x80000017):`n$layout" }
    $consoleUser = (Get-CimInstance Win32_ComputerSystem).UserName
    if ($consoleUser) {
        Invoke-Native 'icacls setowner console user' { icacls $wofFile /setowner "$consoleUser" }
        Invoke-Native 'icacls grant console user' { icacls $wofFile /grant "${consoleUser}:F" }
    } else {
        Write-Output 'setup: no console user logged on; the WOF file keeps the administrator as owner'
    }
    Invoke-Native 'icacls grant Users' { icacls $wofFile /grant "${usersSid}:F" }

    # 5. Elevated holder.
    $held = Join-Path $dir 'held.bin'
    [System.IO.File]::WriteAllBytes($held, (New-Object byte[] 4096))
    $holderScript = Join-Path $dir 'holder.ps1'
    @'
param([string]$File, [string]$Out)
$handle = [System.IO.File]::Open($File, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::Read)
[System.IO.File]::WriteAllText($Out, ('{"pid":' + $PID + '}'))
while ($true) { Start-Sleep -Seconds 3600 }
'@ | Set-Content -Path $holderScript -Encoding ASCII
    $holderJson = Join-Path $dir 'holder.json'
    $arguments = '-NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -File "{0}" "{1}" "{2}"' -f $holderScript, $held, $holderJson
    $action = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument $arguments
    $principal = New-ScheduledTaskPrincipal -UserId 'NT AUTHORITY\SYSTEM' -LogonType ServiceAccount -RunLevel Highest
    $settings = New-ScheduledTaskSettingsSet -ExecutionTimeLimit (New-TimeSpan -Hours 2) -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries
    Register-ScheduledTask -TaskName $taskName -Action $action -Principal $principal -Settings $settings -Force | Out-Null
    Start-ScheduledTask -TaskName $taskName
    $deadline = (Get-Date).AddSeconds(60)
    while (-not (Test-Path -LiteralPath $holderJson) -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 500 }
    if (-not (Test-Path -LiteralPath $holderJson)) { throw 'the holder task did not start within 60 s' }
    Start-Sleep -Milliseconds 500
    $holderPid = [int](Get-Content -LiteralPath $holderJson -Raw | ConvertFrom-Json).pid
    if (-not (Get-Process -Id $holderPid -ErrorAction SilentlyContinue)) { throw "the holder process $holderPid is not running" }

    # 6. Handoff.
    [ordered]@{
        heldFile = $held
        holderPid = $holderPid
        fileSymlink = $fileLink
        directorySymlink = $dirLink
        folderWithFileSymlink = $withFileLink
        folderWithDirectorySymlink = $withDirLink
        mountPoint = $mount
        folderWithMountPoint = $withMount
        systemOwnedFile = $systemOwned
        folderWithSystemOwnedFile = $withSystemOwned
        administratorsOwnedFolder = $administratorsOwned
        administratorsOwnedFile = $administratorsFile
        wofFile = $wofFile
        folderWithWofFile = $withWof
        trustedInstallerOwnedFile = $trustedInstallerFile
        folderWithTrustedInstallerOwnedFile = $withTrustedInstaller
    } | ConvertTo-Json | Set-Content -Path (Join-Path $dir 'handoff.json') -Encoding UTF8
    Write-Output "setup: fixtures in $dir, holder pid $holderPid, policy $policyName=1"
    exit 0
}
catch {
    Write-Output "setup failed: $_"
    Write-Output $_.ScriptStackTrace
    exit 1
}
