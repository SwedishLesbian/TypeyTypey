$ErrorActionPreference = 'Stop'

Push-Location $PSScriptRoot
try {
    dotnet publish .\TypeyTypey.csproj `
        -c Release `
        -r win-x64 `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:EnableCompressionInSingleFile=true

    $exe = Join-Path $PSScriptRoot 'bin\Release\net8.0-windows\win-x64\publish\TypeyTypey.exe'
    Write-Host "`nBuilt: $exe" -ForegroundColor Green
}
finally {
    Pop-Location
}
