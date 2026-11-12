using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace ConsolaShell;

public partial class MainWindow : Window
{
    private enum Estado
    {
        Cargando,   // capa visible, esperando a Big Picture
        EnSteam,    // capa oculta, vigilando Steam
        Menu,       // menú de emergencia visible
        Escritorio, // modo mantenimiento: explorer abierto, vigilancia en pausa
        Ocupado     // reiniciando Steam o apagando
    }

    private static readonly Brush ColorAcento = new SolidColorBrush(Color.FromRgb(0x4F, 0xC3, 0xF7));
    private static readonly Brush ColorFondo = new SolidColorBrush(Color.FromRgb(0x05, 0x07, 0x0B));

    private readonly Configuracion _cfg = Configuracion.Cargar();
    private readonly SteamService _steam;
    private readonly Gamepad _mando = new();
    private readonly DispatcherTimer _timerMando = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly DispatcherTimer _timerVigilante = new() { Interval = TimeSpan.FromSeconds(1) };

    private Estado _estado = Estado.Cargando;
    private Estado _estadoAntesDelMenu = Estado.Cargando;
    private DateTime _inicioCarga;
    private DateTime? _ventanaVistaDesde;
    private bool _reintentoBigPicture;
    private int _segundosSinVentana;
    private DateTime? _comboDesde;
    private bool _cerrandoSesion;
    private int _seleccion;
    private List<(string Texto, Action Accion)> _opciones = new();

    public MainWindow()
    {
        InitializeComponent();
        _steam = new SteamService(_cfg);
        TextoPerfil.Text = _cfg.NombrePerfil.ToUpperInvariant();

        _timerMando.Tick += (_, _) => TickMando();
        _timerVigilante.Tick += (_, _) => TickVigilante();
        Loaded += (_, _) => Iniciar();
        PreviewKeyDown += AlPresionarTecla;

        Application.Current.SessionEnding += (_, _) =>
        {
            _cerrandoSesion = true;
            _timerVigilante.Stop();
            Log.Escribir("Fin de sesión / apagado detectado.");
        };
    }

    // ===================== Arranque =====================

    private void Iniciar()
    {
        Giro.BeginAnimation(RotateTransform.AngleProperty,
            new DoubleAnimation(0, 360, TimeSpan.FromSeconds(1.1)) { RepeatBehavior = RepeatBehavior.Forever });

        ActualizarBarraSuperior();
        _timerMando.Start();
        _timerVigilante.Start();

        if (_steam.RutaExe is null)
        {
            MostrarMenu("No se encontró Steam", "Instala Steam o indica su ruta en config.json.");
            return;
        }

        ComenzarCarga("Iniciando Steam…");
    }

    private void ComenzarCarga(string mensaje)
    {
        MostrarCapa(mensaje);
        _estado = Estado.Cargando;
        _inicioCarga = DateTime.Now;
        _ventanaVistaDesde = null;
        _reintentoBigPicture = false;
        _segundosSinVentana = 0;

        if (!_steam.Lanzar())
            MostrarMenu("No se pudo iniciar Steam", @"Revisa el log en %LOCALAPPDATA%\ConsolaShell\log.txt");
    }

    // ===================== Vigilante (cada 1 s) =====================

    private void TickVigilante()
    {
        ActualizarBarraSuperior();
        if (_cerrandoSesion) return;

        switch (_estado)
        {
            case Estado.Cargando: VigilarCarga(); break;
            case Estado.EnSteam: VigilarSteam(); break;
        }
    }

    private void VigilarCarga()
    {
        var transcurrido = DateTime.Now - _inicioCarga;
        var hwnd = _steam.BuscarVentanaBigPicture(soloVisibles: true);

        if (hwnd != IntPtr.Zero)
        {
            // Se espera un momento para que Big Picture termine de dibujarse.
            _ventanaVistaDesde ??= DateTime.Now;
            TextoEstado.Text = "Casi listo…";
            if (DateTime.Now - _ventanaVistaDesde.Value >= TimeSpan.FromSeconds(1.5))
                EntrarASteam(hwnd);
            return;
        }

        _ventanaVistaDesde = null;

        if (transcurrido > TimeSpan.FromSeconds(10) && TextoEstado.Text == "Iniciando Steam…")
            TextoEstado.Text = "Cargando Steam…";

        // Steam abierto pero sin Big Picture (p. ej. tras actualizarse): pedirlo una vez más.
        if (transcurrido > TimeSpan.FromSeconds(20) && !_reintentoBigPicture && _steam.EstaCorriendo())
        {
            _reintentoBigPicture = true;
            Log.Escribir("Big Picture no apareció; se solicita de nuevo.");
            _steam.AbrirBigPicture();
        }

        if (transcurrido > TimeSpan.FromSeconds(_cfg.TiempoMaximoCargaSegundos))
        {
            Log.Escribir("Tiempo de carga agotado.");
            MostrarMenu("Steam no respondió", "Big Picture no apareció a tiempo.");
        }
    }

    private void VigilarSteam()
    {
        if (!_steam.EstaCorriendo())
        {
            Log.Escribir("Steam se cerró; se reinicia.");
            ComenzarCarga("Reiniciando Steam…");
            return;
        }

        // Durante un juego la ventana puede quedar oculta, por eso aquí no se exige que sea visible.
        if (_steam.BuscarVentanaBigPicture(soloVisibles: false) == IntPtr.Zero)
        {
            if (++_segundosSinVentana >= 8)
            {
                Log.Escribir("Se salió de Big Picture; se vuelve a abrir.");
                ComenzarCarga("Volviendo a Big Picture…");
            }
        }
        else
        {
            _segundosSinVentana = 0;
        }
    }

    // ===================== Transiciones de pantalla =====================

    private void MostrarCapa(string mensaje)
    {
        BeginAnimation(OpacityProperty, null);
        Opacity = 1;
        PanelMenu.Visibility = Visibility.Collapsed;
        PanelCarga.Visibility = Visibility.Visible;
        Ayuda.Visibility = Visibility.Visible;
        TextoEstado.Text = mensaje;

        Topmost = true;
        if (!IsVisible) Show();
        WindowState = WindowState.Maximized;
        Activate();
    }

    private void EntrarASteam(IntPtr hwnd)
    {
        _estado = Estado.EnSteam;
        _segundosSinVentana = 0;
        Log.Escribir("Big Picture listo.");

        // Pasar el foco a Big Picture mientras esta ventana todavía es la activa.
        if (NativeMethods.IsIconic(hwnd)) NativeMethods.ShowWindow(hwnd, NativeMethods.SW_RESTORE);
        NativeMethods.SetForegroundWindow(hwnd);

        var fundido = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(450));
        fundido.Completed += (_, _) =>
        {
            if (_estado != Estado.EnSteam) return;
            Topmost = false;
            Hide();
        };
        BeginAnimation(OpacityProperty, fundido);
    }

    // ===================== Menú de emergencia =====================

    private void MostrarMenu(string titulo, string? detalle = null)
    {
        if (_estado != Estado.Menu) _estadoAntesDelMenu = _estado;
        _estado = Estado.Menu;

        MostrarCapa("");
        PanelCarga.Visibility = Visibility.Collapsed;
        Ayuda.Visibility = Visibility.Collapsed;
        PanelMenu.Visibility = Visibility.Visible;

        TituloMenu.Text = titulo;
        DetalleMenu.Text = detalle ?? "";
        DetalleMenu.Visibility = string.IsNullOrEmpty(detalle) ? Visibility.Collapsed : Visibility.Visible;

        _opciones = new()
        {
            ("Volver a Steam", VolverASteam),
            ("Reiniciar Steam", () => _ = ReiniciarSteamAsync()),
            ("Cambiar de usuario", CambiarUsuario),
            ("Modo escritorio", ModoEscritorio),
            ("Reiniciar PC", () => Energia("/r", "Reiniciando…")),
            ("Apagar", () => Energia("/s", "Apagando…")),
        };
        _seleccion = 0;
        DibujarOpciones();
    }

    private void DibujarOpciones()
    {
        ListaMenu.Children.Clear();
        for (int i = 0; i < _opciones.Count; i++)
        {
            bool seleccionada = i == _seleccion;
            ListaMenu.Children.Add(new Border
            {
                Background = seleccionada ? ColorAcento : Brushes.Transparent,
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(20, 12, 20, 12),
                Margin = new Thickness(0, 4, 0, 4),
                Child = new TextBlock
                {
                    Text = _opciones[i].Texto,
                    FontSize = 24,
                    Foreground = seleccionada ? ColorFondo : Brushes.White
                }
            });
        }
    }

    private void MoverSeleccion(int delta)
    {
        if (_opciones.Count == 0) return;
        _seleccion = (_seleccion + delta + _opciones.Count) % _opciones.Count;
        DibujarOpciones();
    }

    private void EjecutarSeleccion()
    {
        if (_opciones.Count == 0) return;
        var (texto, accion) = _opciones[_seleccion];
        Log.Escribir($"Menú: {texto}");
        accion();
    }

    private void CerrarMenu()
    {
        if (_estadoAntesDelMenu == Estado.Escritorio)
        {
            _estado = Estado.Escritorio;
            Topmost = false;
            Hide();
            return;
        }
        VolverASteam();
    }

    // ===================== Acciones del menú =====================

    private void VolverASteam()
    {
        var hwnd = _steam.EstaCorriendo() ? _steam.BuscarVentanaBigPicture(soloVisibles: false) : IntPtr.Zero;
        if (hwnd != IntPtr.Zero)
        {
            MostrarCapa("Volviendo a Steam…");
            EntrarASteam(hwnd);
        }
        else
        {
            ComenzarCarga("Iniciando Steam…");
        }
    }

    private async Task ReiniciarSteamAsync()
    {
        _estado = Estado.Ocupado;
        MostrarCapa("Cerrando Steam…");
        await _steam.CerrarAsync(TimeSpan.FromSeconds(20));
        ComenzarCarga("Iniciando Steam…");
    }

    private void CambiarUsuario()
    {
        // Bloquea la sesión: desde ahí se elige otra cuenta. Al volver, el menú sigue abierto.
        NativeMethods.LockWorkStation();
    }

    private void ModoEscritorio()
    {
        _estado = Estado.Escritorio;
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Escribir("No se pudo abrir el explorador: " + ex.Message);
        }
        Topmost = false;
        Hide();
    }

    private void Energia(string argumento, string mensaje)
    {
        _estado = Estado.Ocupado;
        MostrarCapa(mensaje);
        try
        {
            Process.Start(new ProcessStartInfo("shutdown", $"{argumento} /t 0")
            {
                UseShellExecute = false,
                CreateNoWindow = true
            });
        }
        catch (Exception ex)
        {
            Log.Escribir("No se pudo apagar/reiniciar: " + ex.Message);
            MostrarMenu("No se pudo completar la acción", ex.Message);
        }
    }

    // ===================== Entrada =====================

    private void TickMando()
    {
        _mando.Actualizar();

        // Combinación de emergencia: Vista + Menú mantenidos N segundos (funciona en cualquier estado).
        if (_mando.Presionado(Botones.Vista | Botones.Menu))
        {
            _comboDesde ??= DateTime.Now;
            if (_estado is not (Estado.Menu or Estado.Ocupado) &&
                DateTime.Now - _comboDesde.Value >= TimeSpan.FromSeconds(_cfg.SegundosParaMenuEmergencia))
            {
                Log.Escribir("Menú de emergencia abierto con el mando.");
                MostrarMenu("Menú");
            }
        }
        else
        {
            _comboDesde = null;
        }

        if (_estado != Estado.Menu) return;

        if (_mando.RecienPresionado(Botones.Arriba)) MoverSeleccion(-1);
        else if (_mando.RecienPresionado(Botones.Abajo)) MoverSeleccion(1);
        else if (_mando.RecienPresionado(Botones.A)) EjecutarSeleccion();
        else if (_mando.RecienPresionado(Botones.B)) CerrarMenu();
    }

    private void AlPresionarTecla(object sender, KeyEventArgs e)
    {
        // Teclado solo como respaldo dentro del menú.
        if (_estado != Estado.Menu) return;
        switch (e.Key)
        {
            case Key.Up: MoverSeleccion(-1); e.Handled = true; break;
            case Key.Down: MoverSeleccion(1); e.Handled = true; break;
            case Key.Enter: EjecutarSeleccion(); e.Handled = true; break;
            case Key.Escape: CerrarMenu(); e.Handled = true; break;
        }
    }

    private void ActualizarBarraSuperior()
    {
        Reloj.Text = DateTime.Now.ToString("HH:mm");
        IndicadorMando.Text = _mando.Conectado ? "● Mando conectado" : "○ Conecta tu mando";
    }

    // Este es el shell de la sesión: no se debe cerrar con Alt+F4.
    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_cerrandoSesion) e.Cancel = true;
        base.OnClosing(e);
    }
}
