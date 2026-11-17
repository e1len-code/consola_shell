namespace ConsolaShell;

[Flags]
public enum Botones : ushort
{
    Ninguno   = 0,
    Arriba    = 0x0001,
    Abajo     = 0x0002,
    Izquierda = 0x0004,
    Derecha   = 0x0008,
    Menu      = 0x0010, // ☰ (Start)
    Vista     = 0x0020, // ⧉ (Back)
    StickIzq  = 0x0040,
    StickDer  = 0x0080,
    LB        = 0x0100,
    RB        = 0x0200,
    A         = 0x1000,
    B         = 0x2000,
    X         = 0x4000,
    Y         = 0x8000
}

/// <summary>Batería de un mando según XInput.</summary>
public readonly record struct BateriaMando(byte Tipo, byte Nivel)
{
    public bool ConCable => Tipo == 1;
    public bool Desconocida => Tipo == 0xFF;
}

/// <summary>
/// Lee los 4 puertos XInput y combina sus botones.
/// XInput funciona aunque la app no tenga el foco, así que el menú de
/// emergencia responde incluso mientras Steam está en primer plano.
/// </summary>
public sealed class Gamepad
{
    private const short UmbralStick = 16000;
    private readonly bool[] _conectados = new bool[4];
    private Botones _anterior;
    private Botones _actual;
    private bool _xinputDisponible = true;

    public bool Conectado { get; private set; }

    /// <summary>Último puerto (0-3) en el que se presionó algo; -1 si ninguno.</summary>
    public int PuertoActivo { get; private set; } = -1;

    public bool EstaConectado(int puerto) => _conectados[puerto];

    public void Actualizar()
    {
        _anterior = _actual;
        _actual = Botones.Ninguno;
        Conectado = false;
        if (!_xinputDisponible) return;

        for (uint puerto = 0; puerto < 4; puerto++)
        {
            _conectados[puerto] = false;
            try
            {
                if (NativeMethods.XInputGetState(puerto, out var estado) != 0) continue;

                Conectado = _conectados[puerto] = true;
                var g = estado.Gamepad;
                var b = (Botones)g.wButtons;

                // El stick izquierdo también navega como cruceta.
                if (g.sThumbLY > UmbralStick) b |= Botones.Arriba;
                if (g.sThumbLY < -UmbralStick) b |= Botones.Abajo;
                if (g.sThumbLX < -UmbralStick) b |= Botones.Izquierda;
                if (g.sThumbLX > UmbralStick) b |= Botones.Derecha;

                if (b != Botones.Ninguno) PuertoActivo = (int)puerto;
                _actual |= b;
            }
            catch (DllNotFoundException)
            {
                _xinputDisponible = false;
                Log.Escribir("xinput1_4.dll no disponible; el mando no funcionará en el shell.");
                return;
            }
        }

        if (PuertoActivo >= 0 && !_conectados[PuertoActivo])
            PuertoActivo = Array.IndexOf(_conectados, true);
    }

    public bool Presionado(Botones b) => (_actual & b) == b;

    public bool RecienPresionado(Botones b) => (_actual & b) == b && (_anterior & b) != b;

    /// <summary>Batería del mando en el puerto; null si no hay mando o XInput no responde.</summary>
    public static BateriaMando? LeerBateria(int puerto)
    {
        try
        {
            if (NativeMethods.XInputGetBatteryInformation((uint)puerto, NativeMethods.BATTERY_DEVTYPE_GAMEPAD, out var info) != 0
                || info.BatteryType == 0)
                return null;
            return new BateriaMando(info.BatteryType, info.BatteryLevel);
        }
        catch
        {
            return null;
        }
    }
}
