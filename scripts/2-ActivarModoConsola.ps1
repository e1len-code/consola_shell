<#
  PASO 2 - Ejecutar DENTRO del perfil "Consola" (no necesita administrador).

  - Reemplaza el escritorio de Windows por ConsolaShell, SOLO para este perfil
  - Desactiva las notificaciones de Windows en este perfil
  - Libera RAM solo en este perfil: sin grabación en segundo plano de Xbox Game Bar
    y sin apps de la Tienda ejecutándose en segundo plano
  Todo se escribe en HKCU (registro de ESTE usuario). No toca otros perfiles ni el sistema.
  Se puede volver a ejecutar sin problema.
  Alternativa sin entrar a este perfil: botón "Activar modo consola" del panel de administración.
#>
param(
    [string]$RutaShell = "C:\ConsolaShell\ConsolaShell.exe",
    [string]$Usuario   = ""
)

$ErrorActionPreference = "Stop"
. "$PSScriptRoot\Comun.ps1"
if (-not $Usuario) { $Usuario = Get-UsuarioConsola (Split-Path $RutaShell) }

# Protección: solo dentro del perfil Consola y nunca con una cuenta administradora.
if ($env:USERNAME -ne $Usuario) {
    throw "Este script solo se ejecuta dentro del perfil '$Usuario'. Perfil actual: '$env:USERNAME'. No se cambió nada."
}
# Administradores por SID (Windows está en español). Aparece aunque UAC filtre el token.
if (whoami /groups | Select-String -SimpleMatch $SidAdministradores) {
    throw "El perfil '$env:USERNAME' pertenece a Administradores. Consola debe ser un usuario estándar. No se cambió nada."
}

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

# Shell personalizado, notificaciones y ajustes de RAM: todo en el HKCU de este perfil (ver Comun.ps1)
Set-ModoConsola "HKCU:" $RutaShell
Write-Host "Shell configurado: $RutaShell"
Write-Host "Notificaciones, grabación de Xbox Game Bar y apps en segundo plano desactivadas en este perfil."

Write-Host ""
Write-Host "Modo consola activado. En el próximo arranque verás la pantalla de carga y luego Big Picture." -ForegroundColor Green
$respuesta = Read-Host "¿Reiniciar ahora? (s/n)"
if ($respuesta -eq "s") { Restart-Computer }
