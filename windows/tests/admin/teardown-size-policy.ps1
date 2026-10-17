# windows/tests/admin/teardown-size-policy.ps1 (Windows PowerShell 5.1, run as administrator by the gates runner after
# the AdminSizePolicy suite, and by its setup first). Restores the "maximum Recycle Bin size" policy to the state
# setup-size-policy.ps1 recorded for this clone (value restored, or removed; a key setup created is removed when empty).
# Exit 0 when reverted, 1 otherwise.
$ErrorActionPreference = 'Continue'
$clone = (Get-Item -LiteralPath $PSScriptRoot).Parent.Parent.Parent.Name
$record = Join-Path $env:ProgramData "AmbystoKitSafetyTests-size-policy-$clone.json"
$policyKey = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer'
$policyName = 'RecycleBinSize'
try {
    if (-not (Test-Path -LiteralPath $record)) { Write-Output 'teardown: nothing recorded'; exit 0 }
    $previous = Get-Content -LiteralPath $record -Raw | ConvertFrom-Json
    if ($previous.existed) {
        Set-ItemProperty -Path $policyKey -Name $policyName -Value ([int]$previous.value) -Type DWord
    } elseif (Test-Path $policyKey) {
        Remove-ItemProperty -Path $policyKey -Name $policyName -ErrorAction SilentlyContinue
        if (-not $previous.keyExisted) {
            $key = Get-Item -Path $policyKey
            if ($key.ValueCount -eq 0 -and $key.SubKeyCount -eq 0) { Remove-Item -Path $policyKey }
        }
    }
    $left = (Get-ItemProperty -Path $policyKey -Name $policyName -ErrorAction SilentlyContinue)
    if (-not $previous.existed -and $null -ne $left) { throw "$policyName is still set" }
    Remove-Item -LiteralPath $record -Force
    Write-Output 'teardown: reverted'
    exit 0
}
catch {
    Write-Output "teardown failed: $_"
    exit 1
}
