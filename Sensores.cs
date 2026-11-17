using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;
using static ConsolaShell.NativeMethods;

namespace ConsolaShell;

public sealed record LecturaGpu(double Uso, double Temperatura, double? Watts, double VramUsadaGb, double VramTotalGb);

/// <summary>Programa agrupado por nombre (p. ej. todos los steamwebhelper juntos).</summary>
public sealed record ProcesoRam(string Nombre, int Cantidad, double Mb);

public sealed record LecturaPc(
    double? CpuUso,
    double? TempSistema,
    LecturaGpu? Gpu,
    double RamUsadaGb,
    double RamTotalGb,
    BateriaMando?[] Baterias,
    (int Nivel, bool Silencio)? Volumen,
    IReadOnlyList<ProcesoRam>? Procesos); // null = no se recalculó en esta lectura

/// <summary>
/// Lecturas del PC sin permisos de administrador y sin paquetes externos:
/// CPU y RAM con kernel32, GPU NVIDIA con NVML, temperatura ACPI con PDH, batería con XInput.
/// Se llama desde un hilo en segundo plano y solo mientras el panel está visible.
/// No es seguro llamarla en paralelo: quien la usa garantiza una lectura a la vez.
/// </summary>
public sealed class Sensores
{
    private long _inactivoAnterior, _kernelAnterior, _usuarioAnterior;
    private bool _hayMuestraCpu;

    private bool _nvmlIntentado;
    private IntPtr _gpu;

    private bool _pdhIntentado;
    private IntPtr _consultaPdh, _contadorPdh;
    private double _divisorPdh = 1;

    /// <summary>La lista de procesos se recalcula cada 5 lecturas (≈5 s): es lo más costoso del panel.</summary>
    private const int LecturasEntreProcesos = 5;
    private readonly int _sesion = Process.GetCurrentProcess().SessionId;
    private int _lecturasDesdeProcesos = int.MaxValue;

    private static readonly Dictionary<string, string> NombresAmigables = new(StringComparer.OrdinalIgnoreCase)
    {
        ["steamwebhelper"] = "Steam (interfaz)",
        ["steam"] = "Steam",
        ["dwm"] = "Gráficos de Windows (dwm)",
        ["ConsolaShell"] = "ConsolaShell",
        ["explorer"] = "Explorador de Windows",
        ["msedgewebview2"] = "Edge WebView2",
    };

    public string NombreCpu { get; } = LeerNombreCpu();
    public string? NombreGpu { get; private set; }

    /// <summary>Descarta la muestra de CPU anterior para que el primer valor no sea un promedio viejo.</summary>
    public void Reiniciar()
    {
        _hayMuestraCpu = false;
        _lecturasDesdeProcesos = int.MaxValue;
    }

    public LecturaPc Leer()
    {
        var (ramUsada, ramTotal) = Intentar(LeerRam, (0d, 0d));
        var baterias = new BateriaMando?[4];
        for (int p = 0; p < 4; p++) baterias[p] = Gamepad.LeerBateria(p);

        IReadOnlyList<ProcesoRam>? procesos = null;
        if (++_lecturasDesdeProcesos >= LecturasEntreProcesos)
        {
            _lecturasDesdeProcesos = 0;
            procesos = Intentar<IReadOnlyList<ProcesoRam>?>(LeerProcesos, null);
        }

        return new LecturaPc(
            Intentar<double?>(LeerCpu, null),
            Intentar<double?>(LeerTempSistema, null),
            Intentar<LecturaGpu?>(LeerGpu, null),
            ramUsada,
            ramTotal,
            baterias,
            Volumen.Leer(),
            procesos);
    }

    private static T Intentar<T>(Func<T> lectura, T porDefecto)
    {
        try { return lectura(); }
        catch (Exception ex)
        {
            Log.Escribir("Error leyendo sensor: " + ex.Message);
            return porDefecto;
        }
    }

    // ---------- CPU ----------

    private double? LeerCpu()
    {
        if (!GetSystemTimes(out var inactivo, out var kernel, out var usuario)) return null;

        double? uso = null;
        if (_hayMuestraCpu)
        {
            long total = (kernel - _kernelAnterior) + (usuario - _usuarioAnterior);
            long libre = inactivo - _inactivoAnterior;
            if (total > 0) uso = Math.Clamp(100.0 * (total - libre) / total, 0, 100);
        }

        (_inactivoAnterior, _kernelAnterior, _usuarioAnterior, _hayMuestraCpu) = (inactivo, kernel, usuario, true);
        return uso;
    }

    private static string LeerNombreCpu()
    {
        try
        {
            var nombre = Registry.GetValue(@"HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\CentralProcessor\0",
                "ProcessorNameString", null) as string;
            return string.IsNullOrWhiteSpace(nombre) ? "CPU" : nombre.Trim();
        }
        catch
        {
            return "CPU";
        }
    }

    // ---------- RAM ----------

    private static (double Usada, double Total) LeerRam()
    {
        var m = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        if (!GlobalMemoryStatusEx(ref m)) return (0, 0);
        const double Gb = 1024d * 1024 * 1024;
        return ((m.ullTotalPhys - m.ullAvailPhys) / Gb, m.ullTotalPhys / Gb);
    }

    // ---------- Qué usa la RAM (solo procesos de ESTA sesión, es decir, del perfil Consola) ----------

    /// <summary>
    /// Los 5 programas que más RAM física usan, sumando sus procesos por nombre.
    /// Es aproximado: la memoria compartida entre procesos se cuenta en cada uno.
    /// Solo lee; no cierra ni modifica nada.
    /// </summary>
    private IReadOnlyList<ProcesoRam> LeerProcesos()
    {
        var porNombre = new Dictionary<string, (int Cantidad, long Bytes)>(StringComparer.OrdinalIgnoreCase);
        foreach (var proceso in Process.GetProcesses())
        {
            using (proceso)
            {
                try
                {
                    if (proceso.SessionId != _sesion) continue;
                    var nombre = proceso.ProcessName;
                    porNombre.TryGetValue(nombre, out var actual);
                    porNombre[nombre] = (actual.Cantidad + 1, actual.Bytes + proceso.WorkingSet64);
                }
                catch
                {
                    // El proceso terminó mientras se leía.
                }
            }
        }

        return porNombre
            .OrderByDescending(p => p.Value.Bytes)
            .Take(5)
            .Select(p => new ProcesoRam(
                NombresAmigables.TryGetValue(p.Key, out var amigable) ? amigable : p.Key,
                p.Value.Cantidad,
                p.Value.Bytes / (1024d * 1024)))
            .ToList();
    }

    // ---------- GPU (NVIDIA) ----------

    private LecturaGpu? LeerGpu()
    {
        if (!_nvmlIntentado)
        {
            _nvmlIntentado = true;
            try
            {
                if (nvmlInit_v2() == 0 && nvmlDeviceGetHandleByIndex_v2(0, out var gpu) == 0)
                {
                    _gpu = gpu;
                    var nombre = new byte[96];
                    if (nvmlDeviceGetName(_gpu, nombre, (uint)nombre.Length) == 0)
                        NombreGpu = Encoding.ASCII.GetString(nombre).TrimEnd('\0').Replace("NVIDIA GeForce ", "");
                    Log.Escribir($"NVML listo: {NombreGpu}");
                }
                else
                {
                    Log.Escribir("NVML no encontró una GPU NVIDIA.");
                }
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
            {
                Log.Escribir("nvml.dll no disponible; no se mostrarán datos de la GPU.");
            }
        }

        if (_gpu == IntPtr.Zero) return null;
        if (nvmlDeviceGetTemperature(_gpu, NVML_TEMPERATURE_GPU, out var temp) != 0) return null;
        nvmlDeviceGetUtilizationRates(_gpu, out var uso);
        nvmlDeviceGetMemoryInfo(_gpu, out var mem);
        double? watts = nvmlDeviceGetPowerUsage(_gpu, out var mw) == 0 ? mw / 1000.0 : null;

        const double Gb = 1024d * 1024 * 1024;
        return new LecturaGpu(uso.Gpu, temp, watts, mem.Usada / Gb, mem.Total / Gb);
    }

    // ---------- Temperatura ACPI (zona térmica de la placa) ----------

    private double? LeerTempSistema()
    {
        if (!_pdhIntentado)
        {
            _pdhIntentado = true;
            PrepararPdh();
        }
        if (_contadorPdh == IntPtr.Zero || PdhCollectQueryData(_consultaPdh) != 0) return null;

        uint tamano = 0;
        if (PdhGetFormattedCounterArray(_contadorPdh, PDH_FMT_DOUBLE, ref tamano, out _, IntPtr.Zero) != PDH_MORE_DATA
            || tamano == 0)
            return null;

        var buffer = Marshal.AllocHGlobal((int)tamano);
        try
        {
            if (PdhGetFormattedCounterArray(_contadorPdh, PDH_FMT_DOUBLE, ref tamano, out var cantidad, buffer) != 0)
                return null;

            double? maxima = null;
            int paso = Marshal.SizeOf<PDH_FMT_COUNTERVALUE_ITEM>();
            for (int i = 0; i < cantidad; i++)
            {
                var item = Marshal.PtrToStructure<PDH_FMT_COUNTERVALUE_ITEM>(buffer + i * paso);
                if (item.Estado > 1) continue; // 0 válido, 1 dato nuevo
                var celsius = item.Valor / _divisorPdh - 273.15; // Kelvin → °C
                if (celsius is > 0 and < 125) maxima = Math.Max(maxima ?? celsius, celsius);
            }
            return maxima;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private void PrepararPdh()
    {
        if (PdhOpenQuery(null, IntPtr.Zero, out var consulta) != 0) return;

        // "High Precision Temperature" está en décimas de Kelvin; "Temperature" en Kelvin.
        foreach (var (ruta, divisor) in new[]
                 {
                     (@"\Thermal Zone Information(*)\High Precision Temperature", 10d),
                     (@"\Thermal Zone Information(*)\Temperature", 1d)
                 })
        {
            if (PdhAddEnglishCounter(consulta, ruta, IntPtr.Zero, out var contador) == 0)
            {
                (_consultaPdh, _contadorPdh, _divisorPdh) = (consulta, contador, divisor);
                return;
            }
        }

        PdhCloseQuery(consulta);
        Log.Escribir("Este PC no expone zonas térmicas ACPI; no se mostrará la temperatura del sistema.");
    }
}
