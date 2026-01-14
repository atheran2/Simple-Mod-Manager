@echo off
echo Building Simple VS Manager...
dotnet publish VintageStoryModManager/VintageStoryModManager.csproj -c Release
if %ERRORLEVEL% EQU 0 (
    echo.
    echo Build successful!
    echo Output: VintageStoryModManager\bin\Release\net8.0-windows\win-x64\publish\
    explorer "VintageStoryModManager\bin\Release\net8.0-windows\win-x64\publish"
) else (
    echo.
    echo Build failed with error code %ERRORLEVEL%
)
pause
