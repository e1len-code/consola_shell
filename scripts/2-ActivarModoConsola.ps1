<#
  PASO 2 - Ejecutar DENTRO del perfil "Consola" (no necesita administrador).

  - Reemplaza el escritorio de Windows por ConsolaShell, SOLO para este perfil
  - Desactiva las notificaciones de Windows en este perfil
#>
param(
    [string]$RutaShell = "C:\ConsolaShell\ConsolaShell.exe"
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $RutaShell)) {
    throw "No se encontró $RutaShell. Ejecuta primero el paso 1 desde tu cuenta."
}

Write-Host ""
Write-Host "Perfil actual: $env:USERNAME" -ForegroundColor Cyan

# Comprobar que Steam ya tiene sesión recordada en este perfil
$steam = Get-ItemProperty -Path "HKCU:\Software\Valve\Steam" -Name AutoLoginUser -ErrorAction SilentlyContinue
if (-not $steam -or -not $steam.AutoLoginUser) {
    Write-Warning "Parece que aún no iniciaste sesión en Steam con 'Recordar contraseña' en este perfil."
    Write-Warning "Sin eso, Big Picture pedirá usuario y contraseña en cada arranque."
    $respuesta = Read-Host "¿Continuar de todos modos? (s/n)"
    if ($respuesta -ne "s") { return }
}

# Shell personalizado solo para este usuario
$winlogon = "HKCU:\Software\Microsoft\Windows NT\CurrentVersion\Winlogon"
if (-not (Test-Path $winlogon)) { New-Item -Path $winlogon | Out-Null }
Set-ItemProperty -Path $winlogon -Name Shell -Value "`"$RutaShell`"" -Type String
Write-Host "Shell configurado: $RutaShell"

# Sin notificaciones emergentes
$push = "HKCU:\Software\Microsoft\Windows\CurrentVersion\PushNotifications"
if (-not (Test-Path $push)) { New-Item -Path $push | Out-Null }
Set-ItemProperty -Path $push -Name ToastEnabled -Value 0 -Type DWord
Write-Host "Notificaciones desactivadas."

Write-Host ""
Write-Host "Modo consola activado. En el próximo arranque verás la pantalla de carga y luego Big Picture." -ForegroundColor Green
$respuesta = Read-Host "¿Reiniciar ahora? (s/n)"
if ($respuesta -eq "s") { Restart-Computer }
