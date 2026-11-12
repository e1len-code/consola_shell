using System.Diagnostics;
using System.IO;
using System.Text;
using Microsoft.Win32;

namespace ConsolaShell;

/// <summary>Lanza, detecta y cierra Steam dentro de la sesión actual.</summary>
public sealed class SteamService
{
    private readonly Configuracion _cfg;
    private readonly int _sesion = Process.GetCurrentProcess().SessionId;

    public string? RutaExe { get; }

    public SteamService(Configuracion cfg)
    {
        _cfg = cfg;
        RutaExe = BuscarSteam();
        Log.Escribir(RutaExe is null ? "Steam no encontrado." : $"Steam: {RutaExe}");
    }

    private string? BuscarSteam()
    {
        if (!string.IsNullOrWhiteSpace(_cfg.RutaSteam) && File.Exists(_cfg.RutaSteam))
            return _cfg.RutaSteam;

        try
        {
            if (Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamExe", null) is string exe)
            {
                exe = exe.Replace('/', '\\');
                if (File.Exists(exe)) return exe;
            }

            if (Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", null) is string dir)
            {
                var ruta = Path.Combine(dir, "steam.exe");
                if (File.Exists(ruta)) return ruta;
            }
        }
        catch (Exception ex)
        {
            Log.Escribir("Error leyendo el registro de Steam: " + ex.Message);
        }

        var porDefecto = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam", "steam.exe");
        return File.Exists(porDefecto) ? porDefecto : null;
    }

    /// <summary>Procesos steam.exe de ESTA sesión (ignora el Steam de otro usuario abierto).</summary>
    private List<Process> ProcesosSteam() =>
        Process.GetProcessesByName("steam")
            .Where(p => { try { return p.SessionId == _sesion; } catch { return false; } })
            .ToList();

    public bool EstaCorriendo() => ProcesosSteam().Count > 0;

    /// <summary>Si Steam no corre, lo inicia en Big Picture; si ya corre, abre Big Picture.</summary>
    public bool Lanzar()
    {
        if (RutaExe is null) return false;
        var argumentos = EstaCorriendo() ? "steam://open/bigpicture" : _cfg.ArgumentosSteam;
        return Ejecutar(argumentos);
    }

    public bool AbrirBigPicture() => RutaExe is not null && Ejecutar("steam://open/bigpicture");

    private bool Ejecutar(string argumentos)
    {
        try
        {
            Log.Escribir($"Ejecutando Steam {argumentos}");
            Process.Start(new ProcessStartInfo(RutaExe!, argumentos)
            {
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(RutaExe!)!
            });
            return true;
        }
        catch (Exception ex)
        {
            Log.Escribir("No se pudo ejecutar Steam: " + ex.Message);
            return false;
        }
    }

    /// <summary>Pide a Steam que se cierre; si no lo hace a tiempo, lo termina.</summary>
    public async Task CerrarAsync(TimeSpan espera)
    {
        if (RutaExe is null || !EstaCorriendo()) return;

        Ejecutar("-shutdown");
        var limite = DateTime.Now + espera;
        while (DateTime.Now < limite)
        {
            await Task.Delay(500);
            if (!EstaCorriendo()) return;
        }

        Log.Escribir("Steam no se cerró a tiempo; se fuerza el cierre.");
        foreach (var p in ProcesosSteam())
        {
            try { p.Kill(entireProcessTree: true); } catch { }
        }
        await Task.Delay(1000);
    }

    /// <summary>
    /// Busca la ventana de Big Picture (título configurable, proceso de Steam, misma sesión).
    /// </summary>
    public IntPtr BuscarVentanaBigPicture(bool soloVisibles)
    {
        IntPtr encontrada = IntPtr.Zero;

        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (soloVisibles && !NativeMethods.IsWindowVisible(hwnd)) return true;

            int largo = NativeMethods.GetWindowTextLength(hwnd);
            if (largo == 0) return true;

            var sb = new StringBuilder(largo + 1);
            NativeMethods.GetWindowText(hwnd, sb, sb.Capacity);
            var titulo = sb.ToString();
            if (!_cfg.TitulosBigPicture.Any(t => titulo.Contains(t, StringComparison.OrdinalIgnoreCase)))
                return true;

            NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
            try
            {
                using var proceso = Process.GetProcessById((int)pid);
                var nombre = proceso.ProcessName.ToLowerInvariant();
                if ((nombre == "steamwebhelper" || nombre == "steam") && proceso.SessionId == _sesion)
                {
                    encontrada = hwnd;
                    return false; // detener la enumeración
                }
            }
            catch
            {
                // El proceso pudo terminar entre medio; seguir buscando.
            }
            return true;
        }, IntPtr.Zero);

        return encontrada;
    }
}
