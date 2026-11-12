@echo off
setlocal
set "BLEND=%~dp0soreo5_770_220_273.blend"
set "SCRIPT=%~dp0scripts\export_soreo5_sbox.py"
if defined BLENDER_EXE if exist "%BLENDER_EXE%" goto run
set "BLENDER_EXE=D:\Steam\steamapps\common\Blender\blender.exe"
if exist "%BLENDER_EXE%" goto run
for /f "delims=" %%B in ('where blender 2^>nul') do if not defined FOUND_BLENDER set "FOUND_BLENDER=%%B"
if defined FOUND_BLENDER set "BLENDER_EXE=%FOUND_BLENDER%"
if exist "%BLENDER_EXE%" goto run
echo Blender introuvable. Definissez la variable BLENDER_EXE avec le chemin vers blender.exe.
pause
exit /b 1
:run
if not exist "%BLEND%" (echo Fichier Blender introuvable: "%BLEND%" & pause & exit /b 1)
if not exist "%SCRIPT%" (echo Script introuvable: "%SCRIPT%" & pause & exit /b 1)
echo Export du fichier enregistre, materiaux et recalage du vehicule S^&box.
echo Enregistrez le modele dans Blender avant d'utiliser ce lanceur.
"%BLENDER_EXE%" --background "%BLEND%" --python-exit-code 1 --python "%SCRIPT%"
if errorlevel 1 (echo ECHEC: voir les messages ci-dessus. & pause & exit /b 1)
echo Export termine. Le prefab et les articulations ont ete mis a jour.
echo S^&box compile immediatement si son editeur est ouvert.
echo Consultez le rapport dans exports\soreo5_sbox.
pause
