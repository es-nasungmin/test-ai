param(
    [string]$ProjectRoot = "C:\deploy\aidesk\source\AIDeskPJ",
    [string]$PublishDir = "C:\deploy\aidesk\api",
    [string]$FrontendDir = "C:\deploy\aidesk\frontend",
    [string]$ApiBaseUrl = "/api"
)

$ErrorActionPreference = "Stop"

Write-Host "[deploy] project root: $ProjectRoot"
Write-Host "[deploy] publish dir : $PublishDir"
Write-Host "[deploy] frontend dir: $FrontendDir"

Push-Location $ProjectRoot
try {
    Write-Host "[deploy] backend publish"
    dotnet publish .\AiDeskApi\AiDeskApi.csproj -c Release -o $PublishDir

    Write-Host "[deploy] frontend build"
    Push-Location .\AiDeskClient
    try {
        npm ci
        $env:VITE_API_BASE_URL = $ApiBaseUrl
        npm run build

        if (Test-Path $FrontendDir) {
            Remove-Item $FrontendDir -Recurse -Force
        }
        Copy-Item .\dist $FrontendDir -Recurse
    }
    finally {
        Pop-Location
    }

    Write-Host "[deploy] done"
    Write-Host "[deploy] restart Windows Service / IIS manually if needed"
}
finally {
    Pop-Location
}
