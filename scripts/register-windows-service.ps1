param(
    [string]$ServiceName = "AiDeskApi",
    [string]$DotnetPath = "C:\Program Files\dotnet\dotnet.exe",
    [string]$ApiDllPath = "C:\deploy\aidesk\api\AiDeskApi.dll"
)

$ErrorActionPreference = "Stop"

$binPath = '"' + $DotnetPath + '" "' + $ApiDllPath + '"'

Write-Host "[service] registering service: $ServiceName"
Write-Host "[service] binPath: $binPath"

sc.exe create $ServiceName binPath= $binPath start= auto
sc.exe description $ServiceName "AiDesk ASP.NET Core API"

Write-Host "[service] registration complete"
Write-Host "[service] start with: sc.exe start $ServiceName"
Write-Host "[service] stop with : sc.exe stop $ServiceName"
