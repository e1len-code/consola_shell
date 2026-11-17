#Requires -RunAsAdministrator
<#
  Estado de la instalación en una línea JSON (lo usa el panel de administración).
  Solo lee. Si la sesión del perfil está cerrada, abre su registro un instante para leerlo.
#>
param(
    [string]$Usuario = "",
    [string]$Destino = "C:\ConsolaShell"
)

$ErrorActionPreference = "Stop"
. "$PSScriptRoot\Comun.ps1"
if (-not $Usuario) { $Usuario = Get-UsuarioConsola $Destino }

$estado = [ordered]@{
    Usuario        = $Usuario
    Instalado      = Test-Path (Join-Path $Destino "ConsolaShell.exe")
    UsuarioExiste  = $false
    UsuarioEsAdmin = $false
    PerfilCreado   = $false
    SesionAbierta  = $false
    Autologin      = $false
    ModoConsola    = $null
    SteamRecordado = $null
}

$cuenta = Get-LocalUser -Name $Usuario -ErrorAction SilentlyContinue
if ($cuenta) {
    $sid = $cuenta.SID.Value
    $estado.UsuarioExiste = $true
    $estado.UsuarioEsAdmin = Test-EsAdministrador $sid

    $perfil = Get-CimInstance Win32_UserProfile -Filter "SID='$sid'" -ErrorAction SilentlyContinue
    if ($perfil) {
        $estado.PerfilCreado = $true
        $estado.SesionAbierta = [bool]$perfil.Loaded
        try {
            Invoke-EnRegistroDeUsuario $Usuario {
                param($raiz)
                $shell = Get-ItemProperty -Path "$raiz\$RutaWinlogonUsuario" -Name Shell -ErrorAction SilentlyContinue
                $estado.ModoConsola = [bool]($shell -and $shell.Shell -like "*ConsolaShell*")
                $estado.SteamRecordado = Test-SteamRecordado $raiz
            }
        } catch {
            # Sin acceso al registro del perfil: esos dos datos quedan como desconocidos.
        }
    }
}

$winlogon = Get-ItemProperty -Path "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon"
$estado.Autologin = ($winlogon.AutoAdminLogon -eq "1") -and ($winlogon.DefaultUserName -eq $Usuario)

$estado | ConvertTo-Json -Compress
