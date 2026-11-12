@echo off
rem Clic derecho -> "Ejecutar como administrador" desde tu cuenta
rem Para borrar tambien el perfil y los archivos, ejecuta el .ps1 con -EliminarUsuario -EliminarArchivos
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp03-RevertirConsola.ps1"
pause
