# windows/tests/admin/setup-size-policy.ps1 (Windows PowerShell 5.1, run as administrator by the gates runner before
# the AdminSizePolicy suite; teardown-size-policy.ps1 reverts it). Sets the machine policy "maximum Recycle Bin size"
# (Policies\Explorer RecycleBinSize), which the gate must treat as a setting it cannot verify (G02). It has its own suite
# because while it is set every approval on the system drive turns into "Could not verify". The previous value is
# recorded per clone and restored. Exit 0 on success, 1 on failure.
$ErrorActionPreference = 'Stop'
$clone = (Get-Item -LiteralPath $PSScriptRoot).Parent.Parent.Parent.Name
$record = Join-Path $env:ProgramData "AmbystoKitSafetyTests-size-policy-$clone.json"
$policyKey = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer'
$policyName = 'RecycleBinSize'
try {
    if (Test-Path -LiteralPath $record) { & (Join-Path $PSScriptRoot 'teardown-size-policy.ps1') | Out-Null }
    $previous = @{ existed = $false; value = 0; keyExisted = (Test-Path $policyKey) }
    if (Test-Path $policyKey) {
        $item = Get-ItemProperty -Path $policyKey -Name $policyName -ErrorAction SilentlyContinue
        if ($null -ne $item) { $previous = @{ existed = $true; value = [int]$item.$policyName; keyExisted = $true } }
    }
    $previous | ConvertTo-Json | Set-Content -Path $record -Encoding UTF8
    New-Item -Path $policyKey -Force | Out-Null
    New-ItemProperty -Path $policyKey -Name $policyName -Value 1024 -PropertyType DWord -Force | Out-Null
    Write-Output "setup: policy $policyName=1024 (previous recorded in $record)"
    exit 0
}
catch {
    Write-Output "setup failed: $_"
    exit 1
}
