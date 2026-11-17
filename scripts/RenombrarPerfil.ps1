#Requires -RunAsAdministrator
<#
  Cambia el nombre del perfil de consola (cuenta de Windows) desde TU cuenta.
  - Renombra la cuenta y su nombre visible en la pantalla de inicio
  - Actualiza el inicio automático si apuntaba a ese perfil
  - Actualiza config.json (NombreUsuario y el nombre de la pantalla de carga)
  La carpeta C:\Users\<nombre anterior> conserva su nombre: Windows no la renombra y no afecta en nada.
  Se niega a tocar cuentas administradoras.
#>
param(
    [Parameter(Mandatory = $true)][string]$NuevoNombre,
    [string]$Usuario = "",
    [string]$Destino = "C:\ConsolaShell"
)

$ErrorActionPreference = "Stop"
. "$PSScriptRoot\Comun.ps1"
if (-not $Usuario) { $Usuario = Get-UsuarioConsola $Destino }

$NuevoNombre = $NuevoNombre.Trim()
if ($NuevoNombre -notmatch '^[\p{L}\p{N}][\p{L}\p{N} _.\-]{0,19}$' -or $NuevoNombre.EndsWith('.')) {
    throw "Nombre no válido. Usa letras, números, espacio, punto, guion o guion bajo (máximo 20, sin terminar en punto)."
}

$cuenta = Get-LocalUser -Name $Usuario -ErrorAction SilentlyContinue
if (-not $cuenta) { throw "El perfil '$Usuario' no existe." }
if (Test-EsAdministrador $cuenta.SID.Value) {
    throw "'$Usuario' pertenece a Administradores; este panel no modifica cuentas administradoras. No se cambió nada."
}
if ($NuevoNombre -ceq $Usuario) {
    Write-Host "El perfil ya se llama '$NuevoNombre'. No hay nada que cambiar."
    return
}
if ($NuevoNombre -ne $Usuario -and (Get-LocalUser -Name $NuevoNombre -ErrorAction SilentlyContinue)) {
    throw "Ya existe un usuario llamado '$NuevoNombre'."
}

Rename-LocalUser -Name $Usuario -NewName $NuevoNombre
Set-LocalUser -Name $NuevoNombre -FullName $NuevoNombre
Write-Host "Cuenta renombrada: '$Usuario' -> '$NuevoNombre'."

$winlogon = "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon"
if ((Get-ItemProperty -Path $winlogon).DefaultUserName -eq $Usuario) {
    Set-ItemProperty -Path $winlogon -Name DefaultUserName -Value $NuevoNombre -Type String
    Write-Host "Inicio automático actualizado."
}

if (Test-Path (Join-Path $Destino "config.json")) {
    Set-ConfigConsola $Destino @{ NombreUsuario = $NuevoNombre; NombrePerfil = $NuevoNombre }
    Write-Host "config.json actualizado."
}

Write-Host "Listo. Si la sesión de '$NuevoNombre' está abierta, el cambio se verá al reiniciar."
