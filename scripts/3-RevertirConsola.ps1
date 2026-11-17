#Requires -RunAsAdministrator
<#
  REVERTIR - Ejecutar desde TU cuenta (Leo), como administrador.

  Por defecto: desactiva el inicio automático y quita el shell del perfil Consola.
  Opcional:
    -EliminarUsuario   borra el perfil Consola y su carpeta de usuario
    -EliminarArchivos  borra C:\ConsolaShell
#>
param(
    [string]$Usuario = "",
    [switch]$EliminarUsuario,
    [switch]$EliminarArchivos
)

$ErrorActionPreference = "Stop"
. "$PSScriptRoot\Comun.ps1"
if (-not $Usuario) { $Usuario = Get-UsuarioConsola }

# Protección: nunca revertir ni borrar tu cuenta ni una cuenta administradora.
if ($Usuario -eq $env:USERNAME) { throw "'$Usuario' es la cuenta actual; este script solo actúa sobre el perfil de consola. No se cambió nada." }
$previa = Get-LocalUser -Name $Usuario -ErrorAction SilentlyContinue
if ($previa -and (Test-EsAdministrador $previa.SID.Value)) {
    throw "'$Usuario' pertenece a Administradores; este script no modifica cuentas administradoras. No se cambió nada."
}

Write-Host ""
Write-Host "== Desactivando inicio de sesión automático" -ForegroundColor Cyan
$winlogon = "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon"
Set-ItemProperty -Path $winlogon -Name AutoAdminLogon -Value "0" -Type String
Remove-ItemProperty -Path $winlogon -Name DefaultPassword -ErrorAction SilentlyContinue

$cuenta = Get-LocalUser -Name $Usuario -ErrorAction SilentlyContinue
if ($cuenta) {
    $sid = $cuenta.SID.Value
    $perfil = Get-CimInstance Win32_UserProfile -Filter "SID='$sid'" -ErrorAction SilentlyContinue

    Write-Host "== Quitando el shell personalizado de '$Usuario'" -ForegroundColor Cyan
    $raiz = "Registry::HKEY_USERS\$sid"
    $cargadoPorScript = $false

    # Si la sesión de Consola está abierta, su registro ya está cargado; si no, se carga temporalmente.
    if (-not (Test-Path $raiz)) {
        if ($perfil) {
            $hive = Join-Path $perfil.LocalPath "NTUSER.DAT"
            & reg.exe load "HKU\ConsolaTmp" "$hive" | Out-Null
            if ($LASTEXITCODE -ne 0) { throw "No se pudo cargar el registro de '$Usuario'. Reinicia y vuelve a intentar." }
            $raiz = "Registry::HKEY_USERS\ConsolaTmp"
            $cargadoPorScript = $true
        } else {
            $raiz = $null
            Write-Host "   El perfil nunca inició sesión; no hay shell que quitar."
        }
    }

    if ($raiz) {
        try {
            # Quita el shell, las notificaciones desactivadas y los ajustes de RAM (ver Comun.ps1)
            Remove-ModoConsola $raiz
            Write-Host "   Modo consola quitado: '$Usuario' volverá a tener escritorio normal y ajustes de Windows por defecto."
        } finally {
            if ($cargadoPorScript) {
                [gc]::Collect()
                [gc]::WaitForPendingFinalizers()
                Start-Sleep -Seconds 1
                & reg.exe unload "HKU\ConsolaTmp" | Out-Null
            }
        }
    }

    if ($EliminarUsuario) {
        Write-Host "== Eliminando el perfil '$Usuario'" -ForegroundColor Cyan
        if ($perfil) {
            if ($perfil.Loaded) { throw "La sesión de '$Usuario' está abierta. Reinicia el PC y vuelve a ejecutar." }
            $perfil | Remove-CimInstance
        }
        Remove-LocalUser -Name $Usuario
        Write-Host "   Perfil eliminado."
    }
} else {
    Write-Host "El perfil '$Usuario' no existe."
}

if ($EliminarArchivos -and (Test-Path "C:\ConsolaShell")) {
    Write-Host "== Eliminando C:\ConsolaShell" -ForegroundColor Cyan
    Remove-Item "C:\ConsolaShell" -Recurse -Force
}

Write-Host ""
Write-Host "Listo. Al reiniciar verás la pantalla de inicio de sesión normal de Windows." -ForegroundColor Green
