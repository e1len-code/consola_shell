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

Queda `publish\ConsolaShell.exe` y `publish\config.json`.

> Puedes probarlo antes desde tu cuenta ejecutando `publish\ConsolaShell.exe`. Para salir: mantén **Vista + Menú** 3 s → *Modo escritorio*, y ciérralo desde el Administrador de tareas.

## 2. Instalar (desde tu cuenta)

1. `scripts\1-InstalarConsola.cmd` → clic derecho → **Ejecutar como administrador**.
2. Reinicia. El PC entra solo al perfil **Consola**, por ahora con escritorio normal.
3. En Consola: abre Steam, inicia sesión y marca **Recordar contraseña**. Conecta el mando y confirma que Steam lo reconoce.
4. Ejecuta `C:\ConsolaShell\scripts\2-ActivarModoConsola.cmd` (doble clic, sin administrador).
5. Reinicia. Listo: arranca directo a la pantalla de carga y Big Picture.

## Uso con el mando

| Acción | Cómo |
|---|---|
| Menú de emergencia | Mantener **⧉ Vista + ☰ Menú** 3 segundos (funciona incluso dentro de Steam) |
| Navegar el menú | Cruceta o stick izquierdo, **Ⓐ** seleccionar, **Ⓑ** volver |
| Apagar normalmente | Desde Big Picture: botón Steam/Xbox → Energía → Apagar |

Opciones del menú: Volver a Steam, Reiniciar Steam, Cambiar de usuario, Modo escritorio, Reiniciar PC, Apagar.

Comportamiento automático:
- Si cierras Steam, se muestra *Reiniciando Steam…* y vuelve a abrirse.
- Si sales de Big Picture, a los ~8 s vuelve a abrirse.
- Si Big Picture no aparece en 90 s, aparece el menú.

## Volver a tu cuenta

- **Desde el mando:** menú de emergencia → *Cambiar de usuario* → elige tu cuenta e ingresa tu contraseña.
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

## Si algo falla

- **Log:** `C:\Users\Consola\AppData\Local\ConsolaShell\log.txt`
- **Pantalla negra sin nada:** `Ctrl + Alt + Supr` → Administrador de tareas → *Ejecutar nueva tarea* → `explorer.exe` (escritorio) o `C:\ConsolaShell\ConsolaShell.exe`.
- **No entra solo a Consola:** en tu cuenta, Configuración → Cuentas → Opciones de inicio de sesión → desactiva *Para mayor seguridad, permitir solo el inicio de sesión con Windows Hello*.

## Revertir

Desde tu cuenta, `scripts\3-RevertirConsola.cmd` como administrador: quita el inicio automático y el shell. Para borrar también el perfil y los archivos:

```powershell
powershell -ExecutionPolicy Bypass -File C:\ConsolaShell\scripts\3-RevertirConsola.ps1 -EliminarUsuario -EliminarArchivos
```

## Seguridad

El perfil Consola no tiene contraseña: cualquiera con acceso físico al PC entra a Steam. Es un usuario estándar (sin permisos de administrador), así que no puede modificar el sistema ni ver los archivos de tu cuenta.
