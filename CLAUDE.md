# ConsolaShell

Shell de "modo consola" para Windows 11: el PC enciende, inicia sesión solo en un perfil local **Consola** (sin contraseña), muestra una pantalla de carga propia y abre **Steam Big Picture**. Todo se maneja con un mando Xbox. Proyecto personal de Leo; el código y los textos de interfaz están en español.

## Decisiones tomadas (no reabrir sin que Leo lo pida)

- **Windows 11 + Steam Big Picture**, no SteamOS ni Bazzite: el PC tiene una RTX 4070 Super y SteamOS aún no soporta NVIDIA oficialmente.
- **Xbox mode de Windows 11 descartado**: su "app de inicio" solo admite la app de Xbox, no Steam.
- **App de escritorio WPF (C#)**, no app de consola (mostraría una ventana negra de comandos).
- **Shell reemplazado solo para el perfil Consola**, vía `HKCU\Software\Microsoft\Windows NT\CurrentVersion\Winlogon\Shell`. La cuenta personal de Leo no se toca y sigue con contraseña. Shell Launcher de Microsoft no aplica (requiere Enterprise/Education).
- **Perfil Consola = usuario estándar**, sin contraseña ni permisos de administrador.
- **Fuera de alcance**: explorador de archivos, instalador de juegos, "modo cursor" (mando como mouse). Se evaluaron y se descartaron por ahora; para instalar juegos se usa la cuenta personal o las herramientas de Steam.
- Pantalla de inicio: un solo perfil, sin clave con el mando (por ahora).

## Flujo

```
Encendido → autologin "Consola" → ConsolaShell (capa "Cargando…") → Steam -gamepadui → Big Picture detectado → fundido → capa oculta → vigilante
```

## Estructura

| Archivo | Responsabilidad |
|---|---|
| `App.xaml(.cs)` | Elige modo: **administración** si la cuenta es administradora (o `--admin`; se reabre elevado con UAC), si no **shell** (o `--shell`). Instancia única por modo (`Mutex Local\`), captura de errores |
| `AdminWindow.xaml(.cs)` | Panel de administración (mouse/teclado): estado, instalar, activar, probar, renombrar, configurar, desactivar, eliminar |
| `Administracion.cs` | Detección de admin/elevación, ejecución oculta de scripts (PowerShell 5.1 por ruta completa, `ArgumentList`), lectura de `Estado.ps1` |
| `MainWindow.xaml(.cs)` | Capa de carga, máquina de estados, vigilante, Centro de control (menú + estado del PC + mandos) |
| `SteamService.cs` | Ubicar `steam.exe`, lanzar, cerrar, detectar la ventana de Big Picture |
| `Gamepad.cs` | Lectura XInput de los 4 puertos, botones combinados, flancos, puerto activo, batería |
| `Sensores.cs` | CPU (`GetSystemTimes`), RAM, top 5 procesos por RAM de la sesión (cada 5 s), GPU NVIDIA (NVML), zona térmica ACPI (PDH), baterías, volumen; todo sin admin y solo lectura |
| `Volumen.cs` | Volumen maestro vía Core Audio (COM mínimo, sin NuGet) |
| `Grafica.cs` | Tarjeta con gráfica de línea (Polyline) de las últimas 60 muestras |
| `NativeMethods.cs` | P/Invoke: XInput, ventanas, LockWorkStation, kernel32, NVML, PDH |
| `Configuracion.cs` / `config.json` | Configuración junto al .exe (`NombreUsuario` = cuenta del perfil de consola) |
| `Log.cs` | Log en `%LOCALAPPDATA%\ConsolaShell\log.txt` |
| `scripts/` | Motor de los cambios al sistema, usados por el panel y como respaldo manual: `Comun.ps1` (funciones y lista de ajustes HKCU), `1-Instalar`, `2-Activar` (dentro del perfil), `ActivarDesdeAdmin`, `RenombrarPerfil`, `Estado` (JSON), `3-Revertir`. Se publican en `scripts\` junto al .exe |

### Estados de `MainWindow`
`Cargando` → `EnSteam` (capa oculta) · `Menu` (Centro de control) · `Escritorio` (mantenimiento, vigilancia en pausa) · `Ocupado` (reiniciando Steam / apagando).

### Comportamientos clave
- Big Picture se detecta por título de ventana (`TitulosBigPicture`, contiene "Big Picture") en procesos `steam`/`steamwebhelper` **de la misma sesión** (puede haber otro Steam abierto en la sesión de Leo).
- En `Cargando` se exige ventana visible ≥1,5 s; en `EnSteam` solo que exista (durante un juego puede estar oculta).
- Steam cerrado → "Reiniciando Steam…". Sin ventana de Big Picture ≥`SegundosSinBigPictureParaPanel` (3 s) → Centro de control; "Volver a Steam" lo reabre con `steam://open/bigpicture`.
- Centro de control también con **Vista + Menú** N segundos. XInput funciona sin foco, así que responde incluso con Steam en primer plano. ◀ ▶ ajustan la opción seleccionada (volumen).
- Rendimiento: sensores solo con el panel visible, en `Task.Run` a 1 Hz (pausados si la sesión se bloquea). Al ocultar la capa (`OcultarCapa`): animación detenida, mando a 10 Hz (30 Hz con menú), GC + `SetProcessWorkingSetSize(-1,-1)`. No se baja la prioridad del proceso: el combo de emergencia debe responder aunque un juego sature la CPU.
- Temperatura real del CPU no disponible sin admin (decisión: no instalar drivers/servicios); se muestra la zona ACPI si existe.
- "Cambiar de usuario" usa `LockWorkStation`. **No** ofrecer "Cerrar sesión": el autologin vuelve a entrar a Consola.
- `OnClosing` cancela el cierre salvo fin de sesión: la app es el shell y si se cierra queda la pantalla negra.

## Compilar y probar

Requiere Windows y el SDK de .NET 10 (WPF no compila en Linux/macOS).

```powershell
dotnet build
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
```

Desde la cuenta de Leo, `publish\ConsolaShell.exe` abre el panel de administración. Para probar el shell: botón *Probar modo consola aquí* (o `ConsolaShell.exe --shell`). Para salir: Vista + Menú → *Modo escritorio* y cerrar el proceso desde el Administrador de tareas.

No hay pruebas automatizadas; la verificación es manual (carga → Big Picture → fundido, Centro de control con Vista + Menú, volumen ◀ ▶, gráficas, batería, cerrar Steam, salir de Big Picture).

## Convenciones

- C# con `Nullable` habilitado, nombres de clases, métodos y variables en español (`ComenzarCarga`, `VigilarSteam`, `_estado`).
- Textos de interfaz en español, cortos, con puntos suspensivos `…` para estados en curso.
- Nada debe lanzar excepciones hacia fuera en el shell: registrar con `Log.Escribir` y seguir funcionando.
- Scripts `.ps1`: UTF-8 **con BOM** (PowerShell 5.1 lee mal las tildes sin BOM) y finales de línea CRLF. Usar SIDs para grupos locales (`S-1-5-32-545` = Usuarios), no nombres, porque Windows está en español.
- No hacer cambios que exijan permisos de administrador dentro del perfil Consola.
- Optimizaciones de RAM/rendimiento **solo en el perfil Consola** (HKCU de Consola), nunca en la cuenta de Leo ni a nivel de sistema (HKLM, servicios, políticas). `2-ActivarModoConsola.ps1` se niega a correr fuera de Consola o en una cuenta administradora; cada ajuste nuevo debe tener su reversión en `3-RevertirConsola.ps1`.

## Cuidado al tocar el sistema

- Nunca ejecutar los scripts de `scripts/` ni modificar el registro, el autologin o el shell sin que Leo lo pida explícitamente: cambian cómo arranca su PC.
- Recuperación si queda pantalla negra: `Ctrl+Alt+Supr` → Administrador de tareas → Ejecutar `explorer.exe`.
- Reversión: panel → *Desactivar modo consola*, o `scripts\3-RevertirConsola.cmd` como administrador desde la cuenta de Leo.
- El nombre del perfil ya no es fijo: se lee de `NombreUsuario` en `config.json` (`Get-UsuarioConsola`). El shell instalado se registra como `"C:\ConsolaShell\ConsolaShell.exe" --shell`.
- Todo script que modifique o borre una cuenta debe rechazar la cuenta actual y las del grupo Administradores (`Test-EsAdministrador`).

## Ideas pendientes (solo si Leo las pide)

- Clave de acceso con secuencia de botones del mando.
- Pantalla "¿Quién juega?" con varios perfiles.
- Volver a evaluar el "modo cursor" con UI Automation si el modo mouse de Steam resulta incómodo.