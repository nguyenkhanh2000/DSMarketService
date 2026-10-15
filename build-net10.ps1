#requires -Version 7.0
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
Write-Host 'SDK đang có:'
dotnet --list-sdks
if ($LASTEXITCODE -ne 0) { throw 'Cần cài .NET 10 SDK trước khi build.' }
dotnet restore DSMarketService.sln --configfile NuGet.Config
if ($LASTEXITCODE -ne 0) { throw 'Restore lỗi. Kiểm tra kết nối NuGet.' }
dotnet build DSMarketService.sln -c Release --no-restore
if ($LASTEXITCODE -ne 0) { throw 'Build lỗi, không publish.' }
dotnet publish src/DSMarketService/DSMarketService.csproj -c Release -o ./publish --no-restore
if ($LASTEXITCODE -ne 0) { throw 'Publish lỗi.' }
Write-Host 'Đã restore/build/publish thành công trên máy hiện tại.'
