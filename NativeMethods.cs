using System.Runtime.InteropServices;
using System.Text;

namespace ConsolaShell;

internal static class NativeMethods
{
    // ---------- XInput (mando Xbox) ----------

    [StructLayout(LayoutKind.Sequential)]
    public struct XINPUT_GAMEPAD
    {
        public ushort wButtons;
        public byte bLeftTrigger;
        public byte bRightTrigger;
        public short sThumbLX;
        public short sThumbLY;
        public short sThumbRX;
        public short sThumbRY;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct XINPUT_STATE
    {
        public uint dwPacketNumber;
        public XINPUT_GAMEPAD Gamepad;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct XINPUT_BATTERY_INFORMATION
    {
        public byte BatteryType;  // 0 desconectado, 1 cable, 2 alcalina, 3 NiMH, 0xFF desconocida
        public byte BatteryLevel; // 0 vacía, 1 baja, 2 media, 3 llena
    }

    public const byte BATTERY_DEVTYPE_GAMEPAD = 0;

    /// <summary>Devuelve 0 (ERROR_SUCCESS) si el mando está conectado.</summary>
    [DllImport("xinput1_4.dll")]
    public static extern uint XInputGetState(uint dwUserIndex, out XINPUT_STATE pState);

    [DllImport("xinput1_4.dll")]
    public static extern uint XInputGetBatteryInformation(uint dwUserIndex, byte devType, out XINPUT_BATTERY_INFORMATION info);

    // ---------- Ventanas ----------

    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    public static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    public const int SW_RESTORE = 9;

    // ---------- Sesión ----------

    /// <summary>Bloquea la sesión y muestra la pantalla para elegir otro usuario.</summary>
    [DllImport("user32.dll")]
    public static extern bool LockWorkStation();

    // ---------- Cuenta actual ----------

    public const int TokenElevationType = 18;
    public const int TokenElevationTypeLimited = 3; // administrador con UAC, sin elevar

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern bool GetTokenInformation(IntPtr token, int clase, out int informacion, int largo, out int devuelto);

    // ---------- Sistema (CPU, RAM, memoria del proceso) ----------

    /// <summary>Tiempos acumulados en unidades de 100 ns. "kernel" incluye el tiempo inactivo.</summary>
    [DllImport("kernel32.dll")]
    public static extern bool GetSystemTimes(out long idle, out long kernel, out long user);

    [StructLayout(LayoutKind.Sequential)]
    public struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll")]
    public static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX buffer);

    /// <summary>Con -1, -1 Windows libera la memoria física que el proceso no está usando.</summary>
    [DllImport("kernel32.dll")]
    public static extern bool SetProcessWorkingSetSize(IntPtr process, IntPtr minimo, IntPtr maximo);

    // ---------- NVML (GPU NVIDIA; nvml.dll viene con el driver, no requiere admin) ----------

    public const int NVML_TEMPERATURE_GPU = 0;

    [StructLayout(LayoutKind.Sequential)]
    public struct NvmlUtilizacion
    {
        public uint Gpu;
        public uint Memoria;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct NvmlMemoria
    {
        public ulong Total;
        public ulong Libre;
        public ulong Usada;
    }

    [DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern int nvmlInit_v2();

    [DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern int nvmlDeviceGetHandleByIndex_v2(uint index, out IntPtr device);

    [DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern int nvmlDeviceGetName(IntPtr device, [Out] byte[] name, uint length);

    [DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern int nvmlDeviceGetTemperature(IntPtr device, int sensor, out uint temperatura);

    [DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern int nvmlDeviceGetUtilizationRates(IntPtr device, out NvmlUtilizacion uso);

    [DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern int nvmlDeviceGetMemoryInfo(IntPtr device, out NvmlMemoria memoria);

    [DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern int nvmlDeviceGetPowerUsage(IntPtr device, out uint miliwatts);

    // ---------- PDH (contadores de rendimiento; zona térmica ACPI sin admin) ----------

    public const uint PDH_FMT_DOUBLE = 0x00000200;
    public const uint PDH_MORE_DATA = 0x800007D2;

    [StructLayout(LayoutKind.Sequential)]
    public struct PDH_FMT_COUNTERVALUE_ITEM
    {
        public IntPtr Nombre;
        public uint Estado;   // 0 = dato válido
        public double Valor;
    }

    [DllImport("pdh.dll", CharSet = CharSet.Unicode, EntryPoint = "PdhOpenQueryW")]
    public static extern uint PdhOpenQuery(string? origen, IntPtr datos, out IntPtr consulta);

    /// <summary>Usa la ruta en inglés aunque Windows esté en español.</summary>
    [DllImport("pdh.dll", CharSet = CharSet.Unicode, EntryPoint = "PdhAddEnglishCounterW")]
    public static extern uint PdhAddEnglishCounter(IntPtr consulta, string ruta, IntPtr datos, out IntPtr contador);

    [DllImport("pdh.dll")]
    public static extern uint PdhCollectQueryData(IntPtr consulta);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode, EntryPoint = "PdhGetFormattedCounterArrayW")]
    public static extern uint PdhGetFormattedCounterArray(IntPtr contador, uint formato, ref uint tamano, out uint cantidad, IntPtr items);

    [DllImport("pdh.dll")]
    public static extern uint PdhCloseQuery(IntPtr consulta);
}
