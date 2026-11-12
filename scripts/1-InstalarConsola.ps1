#Requires -RunAsAdministrator
<#
  PASO 1 - Ejecutar desde TU cuenta (Leo), como administrador.

  - Copia ConsolaShell a C:\ConsolaShell
  - Crea el perfil local "Consola" (usuario estándar, sin contraseña)
  - Activa el inicio de sesión automático en "Consola"
  - Quita la animación de primer inicio

  Tu cuenta actual NO cambia: sigue con su contraseña.
#>
param(
    [string]$Usuario = "Consola",
    [string]$Origen  = (Join-Path $PSScriptRoot "..\publish"),
    [string]$Destino = "C:\ConsolaShell"
)

$ErrorActionPreference = "Stop"

Write-Host ""
Write-Host "== 1/4  Copiando ConsolaShell a $Destino" -ForegroundColor Cyan
if (-not (Test-Path (Join-Path $Origen "ConsolaShell.exe"))) {
    throw "No se encontró ConsolaShell.exe en '$Origen'. Primero compila con 'dotnet publish' (ver README)."
}
New-Item -ItemType Directory -Force -Path $Destino | Out-Null
Copy-Item -Path (Join-Path $Origen "*") -Destination $Destino -Recurse -Force

# Scripts que se usarán desde el perfil Consola y para revertir
$destinoScripts = Join-Path $Destino "scripts"
New-Item -ItemType Directory -Force -Path $destinoScripts | Out-Null
Copy-Item -Path (Join-Path $PSScriptRoot "*") -Destination $destinoScripts -Force

Write-Host "== 2/4  Creando el perfil local '$Usuario' sin contraseña" -ForegroundColor Cyan
$cuenta = Get-LocalUser -Name $Usuario -ErrorAction SilentlyContinue
if (-not $cuenta) {
    New-LocalUser -Name $Usuario -NoPassword -FullName $Usuario `
        -Description "Perfil modo consola (Steam Big Picture)" | Out-Null
    Write-Host "   Perfil creado."
} else {
    Write-Host "   El perfil ya existía, se reutiliza."
}
Set-LocalUser -Name $Usuario -PasswordNeverExpires $true

# Grupo "Usuarios" por SID (funciona con Windows en español o inglés). Sin permisos de administrador.
$grupoUsuarios = Get-LocalGroup -SID "S-1-5-32-545"
try {
    Add-LocalGroupMember -Group $grupoUsuarios -Member $Usuario -ErrorAction Stop
} catch {
    # Ya era miembro.
}

Write-Host "== 3/4  Configurando inicio de sesión automático" -ForegroundColor Cyan
# Windows 11: si "Solo permitir Windows Hello" está activo, el autologin no funciona.
$passwordLess = "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\PasswordLess\Device"
if (Test-Path $passwordLess) {
    Set-ItemProperty -Path $passwordLess -Name DevicePasswordLessBuildVersion -Value 0 -Type DWord
}

$winlogon = "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon"
Set-ItemProperty -Path $winlogon -Name AutoAdminLogon    -Value "1"              -Type String
Set-ItemProperty -Path $winlogon -Name DefaultUserName   -Value $Usuario         -Type String
Set-ItemProperty -Path $winlogon -Name DefaultDomainName -Value $env:COMPUTERNAME -Type String
Set-ItemProperty -Path $winlogon -Name DefaultPassword   -Value ""               -Type String
Remove-ItemProperty -Path $winlogon -Name AutoLogonCount -ErrorAction SilentlyContinue

Write-Host "== 4/4  Ajustes de arranque" -ForegroundColor Cyan
$politicas = "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System"
Set-ItemProperty -Path $politicas -Name EnableFirstLogonAnimation -Value 0 -Type DWord

Write-Host ""
Write-Host "Listo. Siguientes pasos:" -ForegroundColor Green
Write-Host "  1. Reinicia el PC: entrará solo al perfil '$Usuario' (todavía con escritorio normal)."
Write-Host "  2. En ese perfil abre Steam, inicia sesión y marca 'Recordar contraseña'."
Write-Host "  3. Ejecuta C:\ConsolaShell\scripts\2-ActivarModoConsola.cmd"
Write-Host "  4. Reinicia. Desde ahí el PC arranca directo a la pantalla de carga y Big Picture."
Write-Host ""
