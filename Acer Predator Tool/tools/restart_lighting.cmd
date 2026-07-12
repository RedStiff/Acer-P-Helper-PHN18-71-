sc.exe stop AcerLightingService
timeout /t 2 /nobreak >nul
taskkill /F /IM OpenRGB.exe 2>nul
timeout /t 1 /nobreak >nul
sc.exe start AcerLightingService
