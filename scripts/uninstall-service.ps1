param(
    [string]$ServiceName = "ha_maxsun"
)

$ErrorActionPreference = "Stop"
$service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($service) {
    if ($service.Status -ne "Stopped") {
        Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
        $service.WaitForStatus("Stopped", [TimeSpan]::FromSeconds(20))
    }

    sc.exe delete $ServiceName | Write-Host
}
else {
    Write-Host "Service '$ServiceName' is not installed."
}
