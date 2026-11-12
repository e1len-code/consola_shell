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
| `App.xaml(.cs)` | Arranque, instancia única por sesión (`Mutex Local\`), captura de errores no controlados |
| `MainWindow.xaml(.cs)` | Capa de carga, máquina de estados, vigilante, menú de emergencia |
| `SteamService.cs` | Ubicar `steam.exe`, lanzar, cerrar, detectar la ventana de Big Picture |
| `Gamepad.cs` | Lectura XInput de los 4 puertos, botones combinados, flancos |
| `NativeMethods.cs` | P/Invoke: XInput, EnumWindows, SetForegroundWindow, LockWorkStation |
| `Configuracion.cs` / `config.json` | Configuración junto al .exe |
| `Log.cs` | Log en `%LOCALAPPDATA%\ConsolaShell\log.txt` |
| `scripts/` | Instalación (admin, desde la cuenta de Leo), activación (dentro de Consola), reversión |

### Estados de `MainWindow`
`Cargando` → `EnSteam` (capa oculta) · `Menu` (emergencia) · `Escritorio` (mantenimiento, vigilancia en pausa) · `Ocupado` (reiniciando Steam / apagando).

### Comportamientos clave
- Big Picture se detecta por título de ventana (`TitulosBigPicture`, contiene "Big Picture") en procesos `steam`/`steamwebhelper` **de la misma sesión** (puede haber otro Steam abierto en la sesión de Leo).
- En `Cargando` se exige ventana visible ≥1,5 s; en `EnSteam` solo que exista (durante un juego puede estar oculta).
- Steam cerrado → "Reiniciando Steam…". Sin ventana de Big Picture ≥8 s → se reabre con `steam://open/bigpicture`.
- Menú de emergencia: mantener **Vista + Menú** N segundos. XInput funciona sin foco, así que responde incluso con Steam en primer plano.
- "Cambiar de usuario" usa `LockWorkStation`. **No** ofrecer "Cerrar sesión": el autologin vuelve a entrar a Consola.
- `OnClosing` cancela el cierre salvo fin de sesión: la app es el shell y si se cierra queda la pantalla negra.

## Compilar y probar

Requiere Windows y el SDK de .NET 10 (WPF no compila en Linux/macOS).

```powershell
dotnet build
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
```

Probar desde la cuenta de Leo antes de instalar: ejecutar `publish\ConsolaShell.exe` con el mando conectado. Para salir: menú de emergencia → *Modo escritorio* y cerrar el proceso desde el Administrador de tareas.

No hay pruebas automatizadas; la verificación es manual (carga → Big Picture → fundido, menú de emergencia, cerrar Steam, salir de Big Picture).

## Convenciones

- C# con `Nullable` habilitado, nombres de clases, métodos y variables en español (`ComenzarCarga`, `VigilarSteam`, `_estado`).
- Textos de interfaz en español, cortos, con puntos suspensivos `…` para estados en curso.
- Nada debe lanzar excepciones hacia fuera en el shell: registrar con `Log.Escribir` y seguir funcionando.
- Scripts `.ps1`: UTF-8 **con BOM** (PowerShell 5.1 lee mal las tildes sin BOM) y finales de línea CRLF. Usar SIDs para grupos locales (`S-1-5-32-545` = Usuarios), no nombres, porque Windows está en español.
- No hacer cambios que exijan permisos de administrador dentro del perfil Consola.

## Cuidado al tocar el sistema

- Nunca ejecutar los scripts de `scripts/` ni modificar el registro, el autologin o el shell sin que Leo lo pida explícitamente: cambian cómo arranca su PC.
- Recuperación si queda pantalla negra: `Ctrl+Alt+Supr` → Administrador de tareas → Ejecutar `explorer.exe`.
- Reversión: `scripts\3-RevertirConsola.cmd` como administrador desde la cuenta de Leo.

## Ideas pendientes (solo si Leo las pide)

- Clave de acceso con secuencia de botones del mando.
- Pantalla "¿Quién juega?" con varios perfiles.
- Indicador de batería del mando.
- Volver a evaluar el "modo cursor" con UI Automation si el modo mouse de Steam resulta incómodo.