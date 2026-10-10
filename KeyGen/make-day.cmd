@echo off
dotnet run --project "%~dp0KeyGen.csproj" -- make --tier day
pause
