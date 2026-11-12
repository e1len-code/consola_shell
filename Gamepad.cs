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

/// <summary>
/// Lee los 4 puertos XInput y combina sus botones.
/// XInput funciona aunque la app no tenga el foco, así que el menú de
/// emergencia responde incluso mientras Steam está en primer plano.
/// </summary>
public sealed class Gamepad
{
    private const short UmbralStick = 16000;
    private Botones _anterior;
    private Botones _actual;
    private bool _xinputDisponible = true;

    public bool Conectado { get; private set; }

    public void Actualizar()
    {
        _anterior = _actual;
        _actual = Botones.Ninguno;
        Conectado = false;
        if (!_xinputDisponible) return;

        for (uint puerto = 0; puerto < 4; puerto++)
        {
            try
            {
                if (NativeMethods.XInputGetState(puerto, out var estado) != 0) continue;

                Conectado = true;
                var b = (Botones)estado.Gamepad.wButtons;

                // El stick izquierdo también navega como cruceta.
                if (estado.Gamepad.sThumbLY > UmbralStick) b |= Botones.Arriba;
                if (estado.Gamepad.sThumbLY < -UmbralStick) b |= Botones.Abajo;

                _actual |= b;
            }
            catch (DllNotFoundException)
            {
                _xinputDisponible = false;
                Log.Escribir("xinput1_4.dll no disponible; el mando no funcionará en el shell.");
                return;
            }
        }
    }

    public bool Presionado(Botones b) => (_actual & b) == b;

    public bool RecienPresionado(Botones b) => (_actual & b) == b && (_anterior & b) != b;
}
