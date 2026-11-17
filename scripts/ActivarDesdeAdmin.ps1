#Requires -RunAsAdministrator
<#
  Activa el modo consola desde TU cuenta (administrador), sin entrar al perfil de consola.
  Es lo que usa el botón "Activar modo consola" del panel. Equivale al paso 2.
  Solo escribe en el registro (HKCU) del perfil de consola.
#>
param(
    [string]$Usuario = "",
    [string]$Destino = "C:\ConsolaShell",
    [switch]$Forzar   # activar aunque Steam no tenga sesión recordada
)

$ErrorActionPreference = "Stop"
. "$PSScriptRoot\Comun.ps1"
if (-not $Usuario) { $Usuario = Get-UsuarioConsola $Destino }

$rutaShell = Join-Path $Destino "ConsolaShell.exe"
if (-not (Test-Path $rutaShell)) { throw "ConsolaShell no está instalado en $Destino. Usa primero 'Instalar / actualizar'." }

$cuenta = Get-LocalUser -Name $Usuario -ErrorAction SilentlyContinue
if (-not $cuenta) { throw "El perfil '$Usuario' no existe. Usa primero 'Instalar / actualizar'." }
if (Test-EsAdministrador $cuenta.SID.Value) {
    throw "'$Usuario' pertenece a Administradores. El modo consola solo se activa en un usuario estándar. No se cambió nada."
}

Invoke-EnRegistroDeUsuario $Usuario {
    param($raiz)
    if (-not (Test-SteamRecordado $raiz) -and -not $Forzar) {
        throw "Steam no tiene sesión recordada en '$Usuario'. Entra una vez a ese perfil, inicia sesión en Steam con 'Recordar contraseña' y vuelve a intentar."
    }
    Set-ModoConsola $raiz $rutaShell
}

Write-Host "Modo consola activado en '$Usuario'. Se aplica en su próximo inicio de sesión."
