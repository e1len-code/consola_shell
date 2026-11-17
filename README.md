# ConsolaShell

Arranque estilo consola para Windows 11: el PC enciende, entra solo al perfil **Consola** (sin contraseña), muestra una pantalla de carga y abre **Steam Big Picture**. Tu cuenta personal no cambia.

```
Encendido → Windows → autologin "Consola" → ConsolaShell (Cargando…) → Big Picture
```

## Requisitos

- Windows 11 con Steam instalado.
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) solo para compilar (el ejecutable final es autocontenido).

## 1. Compilar

En la carpeta del proyecto:

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
```

Queda `publish\ConsolaShell.exe`, `publish\config.json` y `publish\scripts\`.

## 2. Instalar con el panel (desde tu cuenta)

Abre `publish\ConsolaShell.exe` con doble clic desde tu cuenta. Como tu cuenta es administradora, en lugar del modo consola se abre el **panel de administración** (Windows pide permiso con UAC). El panel muestra el estado y el **siguiente paso** en cada momento:

1. **Instalar / actualizar**: copia los archivos a `C:\ConsolaShell`, crea el perfil (por defecto *Consola*; escribe otro nombre en «Perfil de consola» antes si quieres) y activa el inicio automático.
2. **Reiniciar PC**: entra solo al perfil, por ahora con escritorio normal. Ahí abre Steam, inicia sesión con **Recordar contraseña** y confirma que reconoce el mando.
3. Vuelve a tu cuenta (`Ctrl + Alt + Supr` → *Cambiar de usuario*) y abre de nuevo el panel desde `C:\ConsolaShell\ConsolaShell.exe`.
4. **Activar modo consola** y **Reiniciar PC**. Listo: arranca directo a la pantalla de carga y Big Picture.

| Panel | Qué hace |
|---|---|
| Estado | Archivos, perfil, inicio automático, sesión de Steam y modo consola, con el siguiente paso a seguir |
| Instalar / actualizar | Instala o actualiza los archivos; conserva el `config.json` instalado. Para actualizar, abre el panel desde la carpeta `publish` nueva (no desde `C:\ConsolaShell`) |
| Activar modo consola | Activa el shell y los ajustes de RAM en el perfil, sin tener que entrar a él |
| Probar modo consola aquí | Abre el modo consola en tu cuenta para probar |
| Perfil de consola → Renombrar | Cambia el nombre de la cuenta, el inicio automático y el texto de la pantalla de carga. La carpeta `C:\Users\<nombre anterior>` conserva su nombre |
| Configuración | Edita `config.json` (nombre en pantalla, ruta de Steam, tiempos) |
| Desactivar modo consola | Quita el inicio automático y el modo consola; el perfil se conserva |
| Eliminar perfil… | Borra la cuenta y su carpeta (opcional: también `C:\ConsolaShell`). Requiere que su sesión esté cerrada |

**Seguridad del panel:** solo modifica el perfil de consola. Los scripts se niegan a actuar sobre tu cuenta o sobre cualquier cuenta administradora. En el perfil de consola (usuario estándar) el mismo `.exe` siempre arranca en modo consola.

Activar el modo consola además libera RAM **solo en ese perfil**: desactiva las notificaciones, la grabación en segundo plano de Xbox Game Bar (y que el botón Xbox la abra) y las apps de la Tienda en segundo plano. Desactivar o eliminar lo deshace.

> Los scripts de `scripts\` siguen funcionando a mano como respaldo (por ejemplo `2-ActivarModoConsola.cmd` dentro del perfil de consola), pero el panel ya no los necesita.

## Uso con el mando

| Acción | Cómo |
|---|---|
| Centro de control | Sale solo al salir de Big Picture, o mantén **⧉ Vista + ☰ Menú** 3 segundos (funciona incluso dentro de Steam) |
| Navegar el menú | Cruceta o stick izquierdo, **Ⓐ** seleccionar, **◀ ▶** ajustar (volumen), **Ⓑ** volver |
| Apagar normalmente | Desde Big Picture: botón Steam/Xbox → Energía → Apagar |

### Centro de control

- **Opciones:** Volver a Steam, Volumen (◀ ▶ cambia de 2 en 2, mantener repite, Ⓐ silencia), Apagar, Reiniciar PC, Reiniciar Steam, Cambiar de usuario, Modo escritorio.
- **Estado del PC** (gráficas del último minuto): uso de CPU, uso y temperatura de la GPU, RAM, VRAM y consumo de la GPU.
- **Qué usa la RAM:** los 5 programas del perfil Consola que más memoria usan (se actualiza cada ~5 s). Solo informa, no cierra nada.
- **Mandos:** batería de cada mando conectado (4 barras); el que estás usando se resalta.

Límites, porque el perfil Consola no tiene permisos de administrador:
- La **temperatura del CPU** real no se puede leer sin admin. Si la placa la expone, se muestra la zona térmica ACPI como *Temp. placa (ACPI)*; en muchas placas de escritorio no existe y la tarjeta no aparece.
- Los datos de GPU usan NVML (incluido con el driver de NVIDIA). Con otra marca de GPU esas tarjetas no aparecen.
- Algunos mandos por Bluetooth no informan batería: se muestra *Batería desconocida*.

Para no restarle rendimiento a los juegos, los sensores solo se leen con el panel visible (en un hilo aparte, 1 vez por segundo). Con Big Picture en pantalla el shell queda en reposo: sin animaciones, lee el mando a 10 Hz y devuelve su memoria a Windows.

Comportamiento automático:
- Si cierras Steam, se muestra *Reiniciando Steam…* y vuelve a abrirse.
- Si sales de Big Picture, a los ~3 s aparece el Centro de control.
- Si Big Picture no aparece en 90 s, aparece el menú.

## Volver a tu cuenta

- **Desde el mando:** Centro de control → *Cambiar de usuario* → elige tu cuenta e ingresa tu contraseña.
- **Con teclado:** `Ctrl + Alt + Supr` → *Cambiar de usuario*.

> No uses *Cerrar sesión* en Consola: el inicio automático vuelve a entrar a Consola.

## Configuración (`C:\ConsolaShell\config.json`)

| Clave | Para qué |
|---|---|
| `NombrePerfil` | Texto grande de la pantalla de carga |
| `RutaSteam` | Ruta a `steam.exe` si no se detecta sola |
| `ArgumentosSteam` | Por defecto `-gamepadui` (abre en Big Picture) |
| `TitulosBigPicture` | Texto del título de la ventana de Big Picture; agrega otro si tu Steam está en un idioma que no lo contenga |
| `TiempoMaximoCargaSegundos` | Espera antes de mostrar el menú |
| `SegundosParaMenuEmergencia` | Tiempo manteniendo Vista + Menú |
| `SegundosSinBigPictureParaPanel` | Segundos sin Big Picture antes de mostrar el Centro de control |

## Si algo falla

- **Log:** `C:\Users\Consola\AppData\Local\ConsolaShell\log.txt`
- **Pantalla negra sin nada:** `Ctrl + Alt + Supr` → Administrador de tareas → *Ejecutar nueva tarea* → `explorer.exe` (escritorio) o `C:\ConsolaShell\ConsolaShell.exe`.
- **No entra solo a Consola:** en tu cuenta, Configuración → Cuentas → Opciones de inicio de sesión → desactiva *Para mayor seguridad, permitir solo el inicio de sesión con Windows Hello*.

## Revertir

Desde el panel: **Desactivar modo consola** o **Eliminar perfil…**. Sin el panel: `scripts\3-RevertirConsola.cmd` como administrador desde tu cuenta quita el inicio automático y el shell. Para borrar también el perfil y los archivos:

```powershell
powershell -ExecutionPolicy Bypass -File C:\ConsolaShell\scripts\3-RevertirConsola.ps1 -EliminarUsuario -EliminarArchivos
```

## Seguridad

El perfil Consola no tiene contraseña: cualquiera con acceso físico al PC entra a Steam. Es un usuario estándar (sin permisos de administrador), así que no puede modificar el sistema ni ver los archivos de tu cuenta.
