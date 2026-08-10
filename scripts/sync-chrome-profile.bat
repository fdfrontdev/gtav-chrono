@echo off
REM Re-sync the Hermes Chrome profile copy from the real Chrome profile.
REM Close "Chrome (Hermes)" FIRST, then double-click this.
REM Logins made in normal Chrome since the last copy get carried over.
rmdir /S /Q "C:\Users\Dell G7 15\AppData\Local\Google\Chrome\User Data Hermes"
robocopy "C:\Users\Dell G7 15\AppData\Local\Google\Chrome\User Data" "C:\Users\Dell G7 15\AppData\Local\Google\Chrome\User Data Hermes" /E /XD Cache "Code Cache" GPUCache "DawnCache" "GraphiteDawnCache" "Service Worker" Crashpad "component_crx_cache" /XF *.tmp *.log /NFL /NDL /NJH /NJS /NP
echo.
echo Profile synced. Start Chrome (Hermes) again.
pause
