using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ConsolaShell;

/// <summary>Resultado de scripts\Estado.ps1.</summary>
public sealed record EstadoConsola(
    string Usuario,
    bool Instalado,
    bool UsuarioExiste,
    bool UsuarioEsAdmin,
    bool PerfilCreado,
    bool SesionAbierta,
    bool Autologin,
    bool? ModoConsola,
    bool? SteamRecordado);

/// <summary>
/// Soporte del panel de administración. Los cambios en el sistema los hacen los scripts de
/// la carpeta scripts\ (un solo lugar con esa lógica); aquí solo se ejecutan y se lee su salida.
/// </summary>
public static partial class Administracion
{
    public const string Destino = @"C:\ConsolaShell";

    public static string CarpetaScripts => Path.Combine(AppContext.BaseDirectory, "scripts");

    /// <summary>True si este .exe es el instalado (no se puede actualizar ni borrar a sí mismo).</summary>
    public static bool EjecutandoDesdeDestino =>
        string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(AppContext.BaseDirectory)),
            Destino, StringComparison.OrdinalIgnoreCase);

    /// <summary>Cuenta del grupo Administradores, esté o no elevada en este momento (UAC).</summary>
    public static bool EsUsuarioAdministrador()
    {
        try
        {
            using var identidad = WindowsIdentity.GetCurrent();
            if (new WindowsPrincipal(identidad).IsInRole(WindowsBuiltInRole.Administrator)) return true;
            return NativeMethods.GetTokenInformation(identidad.Token, NativeMethods.TokenElevationType,
                       out var tipo, sizeof(int), out _)
                   && tipo == NativeMethods.TokenElevationTypeLimited;
        }
        catch (Exception ex)
        {
            Log.Escribir("No se pudo comprobar si la cuenta es administradora: " + ex.Message);
            return false;
        }
    }

    public static bool EstaElevado()
    {
        using var identidad = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identidad).IsInRole(WindowsBuiltInRole.Administrator);
    }

    /// <summary>Mismas reglas que RenombrarPerfil.ps1 (se validan en los dos lados).</summary>
    public static bool NombreValido(string nombre) =>
        RegexNombre().IsMatch(nombre) && !nombre.EndsWith('.');

    [GeneratedRegex(@"^[\p{L}\p{N}][\p{L}\p{N} _.\-]{0,19}$")]
    private static partial Regex RegexNombre();

    /// <summary>
    /// Ejecuta un script de scripts\ con PowerShell oculto. Cada línea de salida va a <paramref name="salida"/>
    /// (en el hilo que la produce). Devuelve el código de salida; los argumentos se pasan sin interpretar.
    /// </summary>
    public static async Task<int> EjecutarScriptAsync(string script, IEnumerable<string> argumentos, Action<string> salida)
    {
        var ruta = Path.Combine(CarpetaScripts, script);
        if (!File.Exists(ruta))
        {
            salida($"No se encontró {ruta}. Vuelve a publicar el proyecto (la carpeta scripts va junto al .exe).");
            return -1;
        }

        // Ruta completa de PowerShell 5.1 del sistema: no depende del PATH.
        var powershell = Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe");
        var inicio = new ProcessStartInfo(powershell)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = CarpetaScripts
        };
        foreach (var a in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", ruta })
            inicio.ArgumentList.Add(a);
        foreach (var a in argumentos) inicio.ArgumentList.Add(a);

        try
        {
            using var proceso = new Process { StartInfo = inicio };
            proceso.OutputDataReceived += (_, e) => { if (e.Data is { Length: > 0 } l) salida(l); };
            proceso.ErrorDataReceived += (_, e) => { if (e.Data is { Length: > 0 } l) salida("ERROR: " + l); };
            proceso.Start();
            proceso.BeginOutputReadLine();
            proceso.BeginErrorReadLine();
            await proceso.WaitForExitAsync();
            Log.Escribir($"Script {script} terminó con código {proceso.ExitCode}");
            return proceso.ExitCode;
        }
        catch (Exception ex)
        {
            Log.Escribir($"No se pudo ejecutar {script}: {ex}");
            salida("No se pudo ejecutar PowerShell: " + ex.Message);
            return -1;
        }
    }

    /// <summary>Ejecuta Estado.ps1 y lee su línea JSON. Null si falla (los detalles van a <paramref name="salida"/>).</summary>
    public static async Task<EstadoConsola?> LeerEstadoAsync(string usuario, Action<string> salida)
    {
        string? json = null;
        var otras = new List<string>();
        var codigo = await EjecutarScriptAsync("Estado.ps1", ["-Usuario", usuario, "-Destino", Destino], linea =>
        {
            if (linea.TrimStart().StartsWith('{')) json = linea;
            else lock (otras) otras.Add(linea);
        });

        if (codigo != 0 || json is null)
        {
            foreach (var l in otras) salida(l);
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<EstadoConsola>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (Exception ex)
        {
            salida("Respuesta de Estado.ps1 no válida: " + ex.Message);
            return null;
        }
    }
}
