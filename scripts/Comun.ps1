<#
  Funciones compartidas por los scripts de ConsolaShell (no se ejecuta solo).
  Se carga al inicio de cada script con:  . "$PSScriptRoot\Comun.ps1"

  Regla: todo lo que el modo consola cambia vive en el registro del PERFIL DE CONSOLA (su HKCU).
  Nada de aquí toca la cuenta del administrador ni otros perfiles.
#>

# Salida en UTF-8 para que el panel de administración muestre bien las tildes.
try { [Console]::OutputEncoding = [Text.Encoding]::UTF8 } catch { }

$SidAdministradores = "S-1-5-32-544"

# Valores que el modo consola escribe en el HKCU del perfil. Revertir los borra (Windows vuelve a su valor por defecto).
$AjustesConsola = @(
    # Sin notificaciones emergentes
    @{ Ruta = "Software\Microsoft\Windows\CurrentVersion\PushNotifications"; Nombre = "ToastEnabled"; Valor = 0 },
    # Xbox Game Bar: sin grabación en segundo plano (Steam tiene la suya) y el botón Xbox no la abre
    @{ Ruta = "System\GameConfigStore"; Nombre = "GameDVR_Enabled"; Valor = 0 },
    @{ Ruta = "Software\Microsoft\Windows\CurrentVersion\GameDVR"; Nombre = "AppCaptureEnabled"; Valor = 0 },
    @{ Ruta = "Software\Microsoft\GameBar"; Nombre = "UseNexusForGameBarEnabled"; Valor = 0 },
    # Apps de la Tienda en segundo plano (Teléfono, Noticias, Copilot...)
    @{ Ruta = "Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications"; Nombre = "GlobalUserDisabled"; Valor = 1 },
    @{ Ruta = "Software\Microsoft\Windows\CurrentVersion\Search"; Nombre = "BackgroundAppGlobalToggle"; Valor = 0 }
)

$RutaWinlogonUsuario = "Software\Microsoft\Windows NT\CurrentVersion\Winlogon"

<# Nombre del usuario de consola según config.json (cambia si se renombra el perfil). #>
function Get-UsuarioConsola([string]$Destino = "C:\ConsolaShell") {
    $ruta = Join-Path $Destino "config.json"
    try {
        if (Test-Path $ruta) {
            $cfg = Get-Content -Raw -Encoding UTF8 $ruta | ConvertFrom-Json
            if ($cfg.NombreUsuario) { return [string]$cfg.NombreUsuario }
        }
    } catch { }
    return "Consola"
}

<# Cambia claves de config.json conservando las demás. #>
function Set-ConfigConsola([string]$Destino, [hashtable]$Valores) {
    $ruta = Join-Path $Destino "config.json"
    $cfg = if (Test-Path $ruta) { Get-Content -Raw -Encoding UTF8 $ruta | ConvertFrom-Json } else { [pscustomobject]@{} }
    foreach ($clave in $Valores.Keys) {
        $cfg | Add-Member -NotePropertyName $clave -NotePropertyValue $Valores[$clave] -Force
    }
    $cfg | ConvertTo-Json -Depth 5 | Set-Content -Path $ruta -Encoding UTF8
}

<# ¿La cuenta con este SID pertenece a Administradores? (por SID: Windows está en español) #>
function Test-EsAdministrador([string]$Sid) {
    try {
        return [bool](Get-LocalGroupMember -SID $SidAdministradores -ErrorAction Stop |
            Where-Object { $_.SID.Value -eq $Sid })
    } catch {
        # Get-LocalGroupMember falla si el grupo tiene cuentas huérfanas; respaldo con ADSI.
        $sidGrupo = New-Object Security.Principal.SecurityIdentifier $SidAdministradores
        $nombreGrupo = $sidGrupo.Translate([Security.Principal.NTAccount]).Value.Split('\')[-1]
        $grupo = [ADSI]"WinNT://$env:COMPUTERNAME/$nombreGrupo,group"
        foreach ($miembro in @($grupo.Invoke("Members"))) {
            $bytes = $miembro.GetType().InvokeMember("objectSid", "GetProperty", $null, $miembro, $null)
            if ($bytes -and (New-Object Security.Principal.SecurityIdentifier($bytes, 0)).Value -eq $Sid) { return $true }
        }
        return $false
    }
}

<#
  Ejecuta $Accion con la raíz del registro (HKCU) del usuario indicado, desde otra cuenta (admin).
  Si su sesión está abierta usa HKEY_USERS\<SID>; si no, carga su NTUSER.DAT temporalmente y lo descarga al final.
#>
function Invoke-EnRegistroDeUsuario([string]$Usuario, [scriptblock]$Accion) {
    $cuenta = Get-LocalUser -Name $Usuario -ErrorAction SilentlyContinue
    if (-not $cuenta) { throw "El perfil '$Usuario' no existe." }
    $sid = $cuenta.SID.Value

    $raiz = "Registry::HKEY_USERS\$sid"
    $cargadoAqui = $false
    if (-not (Test-Path $raiz)) {
        $perfil = Get-CimInstance Win32_UserProfile -Filter "SID='$sid'" -ErrorAction SilentlyContinue
        if (-not $perfil) {
            throw "El perfil '$Usuario' todavía no ha iniciado sesión nunca. Reinicia para que entre una vez."
        }
        $hive = Join-Path $perfil.LocalPath "NTUSER.DAT"
        & reg.exe load "HKU\ConsolaTmp" "$hive" | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "No se pudo abrir el registro de '$Usuario'. Reinicia y vuelve a intentar." }
        $raiz = "Registry::HKEY_USERS\ConsolaTmp"
        $cargadoAqui = $true
    }

    try {
        & $Accion $raiz
    } finally {
        if ($cargadoAqui) {
            [gc]::Collect()
            [gc]::WaitForPendingFinalizers()
            Start-Sleep -Seconds 1
            & reg.exe unload "HKU\ConsolaTmp" | Out-Null
        }
    }
}

<# Activa el modo consola en una raíz de registro de usuario ("HKCU:" o la de Invoke-EnRegistroDeUsuario). #>
function Set-ModoConsola([string]$Raiz, [string]$RutaShell) {
    $winlogon = "$Raiz\$RutaWinlogonUsuario"
    if (-not (Test-Path $winlogon)) { New-Item -Path $winlogon -Force | Out-Null }
    # --shell fuerza el modo consola aunque algo raro hiciera parecer administrador al perfil.
    Set-ItemProperty -Path $winlogon -Name Shell -Value "`"$RutaShell`" --shell" -Type String

    foreach ($a in $AjustesConsola) {
        $ruta = "$Raiz\$($a.Ruta)"
        if (-not (Test-Path $ruta)) { New-Item -Path $ruta -Force | Out-Null }
        Set-ItemProperty -Path $ruta -Name $a.Nombre -Value $a.Valor -Type DWord
    }
}

<# Quita el shell y los ajustes del modo consola de una raíz de registro de usuario. #>
function Remove-ModoConsola([string]$Raiz) {
    Remove-ItemProperty -Path "$Raiz\$RutaWinlogonUsuario" -Name Shell -ErrorAction SilentlyContinue
    foreach ($a in $AjustesConsola) {
        Remove-ItemProperty -Path "$Raiz\$($a.Ruta)" -Name $a.Nombre -ErrorAction SilentlyContinue
    }
}

<# ¿Steam tiene una sesión recordada en esta raíz de registro? #>
function Test-SteamRecordado([string]$Raiz) {
    $steam = Get-ItemProperty -Path "$Raiz\Software\Valve\Steam" -Name AutoLoginUser -ErrorAction SilentlyContinue
    return [bool]($steam -and $steam.AutoLoginUser)
}
