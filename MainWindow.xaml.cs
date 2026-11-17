using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace ConsolaShell;

public partial class MainWindow : Window
{
    private enum Estado
    {
        Cargando,   // capa visible, esperando a Big Picture
        EnSteam,    // capa oculta, vigilando Steam
        Menu,       // Centro de control visible (al salir de Big Picture o con Vista + Menú)
        Escritorio, // modo mantenimiento: explorer abierto, vigilancia en pausa
        Ocupado     // reiniciando Steam o apagando
    }

    /// <summary>Opción del menú. Ajustar (opcional) responde a ◀ ▶.</summary>
    private sealed record Opcion(Func<string> Texto, Action Accion, Action<int>? Ajustar = null);

    private static readonly Color Acento = Color.FromRgb(0x4F, 0xC3, 0xF7);
    private static readonly Brush ColorAcento = Congelar(new SolidColorBrush(Acento));
    private static readonly Brush ColorFondo = Congelar(new SolidColorBrush(Color.FromRgb(0x05, 0x07, 0x0B)));
    private static readonly Brush ColorTenue = Congelar(new SolidColorBrush(Color.FromRgb(0x7F, 0x8D, 0xA3)));
    private static readonly Brush ColorAlerta = Congelar(new SolidColorBrush(Color.FromRgb(0xFF, 0x8A, 0x80)));
    private static readonly Brush ColorBarraVacia = Congelar(new SolidColorBrush(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF)));

    private const int PasoVolumen = 2;
    private static readonly TimeSpan RitmoMandoMenu = TimeSpan.FromMilliseconds(33);
    private static readonly TimeSpan RitmoMandoReposo = TimeSpan.FromMilliseconds(100);

    private readonly Configuracion _cfg = Configuracion.Cargar();
    private readonly SteamService _steam;
    private readonly Gamepad _mando = new();
    private readonly Sensores _sensores = new();
    private readonly DispatcherTimer _timerMando = new() { Interval = RitmoMandoMenu };
    private readonly DispatcherTimer _timerVigilante = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _timerSensores = new() { Interval = TimeSpan.FromSeconds(1) };

    private readonly Grafica _graficaCpu = new("Uso de CPU", 100, Acento);
    private readonly Grafica _graficaTempSistema = new("Temp. placa (ACPI)", 100, Color.FromRgb(0xFF, 0xB7, 0x4D));
    private readonly Grafica _graficaGpu = new("Uso de GPU", 100, Color.FromRgb(0x81, 0xC7, 0x84));
    private readonly Grafica _graficaTempGpu = new("Temp. GPU", 100, Color.FromRgb(0xFF, 0x8A, 0x65));

    private Estado _estado = Estado.Cargando;
    private Estado _estadoAntesDelMenu = Estado.Cargando;
    private DateTime _inicioCarga;
    private DateTime? _ventanaVistaDesde;
    private bool _reintentoBigPicture;
    private int _segundosSinVentana;
    private DateTime? _comboDesde;
    private DateTime? _repetirAjusteDesde;
    private bool _cerrandoSesion;
    private bool _girando;
    private bool _leyendoSensores;
    private int _seleccion;
    private List<Opcion> _opciones = new();

    private (int Nivel, bool Silencio)? _volumen;
    private BateriaMando?[] _baterias = new BateriaMando?[4];
    private int _puertoDibujado = -2;

    public MainWindow()
    {
        InitializeComponent();
        _steam = new SteamService(_cfg);
        TextoPerfil.Text = _cfg.NombrePerfil.ToUpperInvariant();

        foreach (var g in new[] { _graficaCpu, _graficaTempSistema, _graficaGpu, _graficaTempGpu })
            PanelGraficas.Children.Add(g);

        _timerMando.Tick += (_, _) => TickMando();
        _timerVigilante.Tick += (_, _) => TickVigilante();
        _timerSensores.Tick += (_, _) => TickSensores();
        Loaded += (_, _) => Iniciar();
        PreviewKeyDown += AlPresionarTecla;

        // Con la sesión bloqueada (p. ej. el administrador usando su cuenta) no se leen sensores.
        Microsoft.Win32.SystemEvents.SessionSwitch += (_, e) => Dispatcher.BeginInvoke(() =>
        {
            if (e.Reason == Microsoft.Win32.SessionSwitchReason.SessionLock) _timerSensores.Stop();
            else if (e.Reason == Microsoft.Win32.SessionSwitchReason.SessionUnlock && _estado == Estado.Menu) IniciarSensores();
        });

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
            if (++_segundosSinVentana >= Math.Max(1, _cfg.SegundosSinBigPictureParaPanel))
            {
                Log.Escribir("Se salió de Big Picture; se muestra el Centro de control.");
                MostrarMenu("Centro de control");
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
        Girar(true);

        Topmost = true;
        if (!IsVisible) Show();
        WindowState = WindowState.Maximized;
        Activate();
    }

    /// <summary>
    /// Oculta la capa y deja el shell en reposo para no restarle nada a los juegos:
    /// sin animaciones, sin sensores, mando a 10 Hz y memoria física devuelta a Windows.
    /// </summary>
    private void OcultarCapa()
    {
        Topmost = false;
        Hide();
        Girar(false);
        _timerSensores.Stop();

        try
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            using var proceso = Process.GetCurrentProcess();
            NativeMethods.SetProcessWorkingSetSize(proceso.Handle, new IntPtr(-1), new IntPtr(-1));
        }
        catch (Exception ex)
        {
            Log.Escribir("No se pudo liberar memoria: " + ex.Message);
        }
    }

    /// <summary>La animación de carga solo corre cuando se ve; oculta no consume nada.</summary>
    private void Girar(bool activo)
    {
        if (activo == _girando) return;
        _girando = activo;
        Giro.BeginAnimation(RotateTransform.AngleProperty, activo
            ? new DoubleAnimation(0, 360, TimeSpan.FromSeconds(1.1)) { RepeatBehavior = RepeatBehavior.Forever }
            : null);
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
            if (_estado == Estado.EnSteam) OcultarCapa();
        };
        BeginAnimation(OpacityProperty, fundido);
    }

    // ===================== Centro de control =====================

    private void MostrarMenu(string titulo, string? detalle = null)
    {
        if (_estado != Estado.Menu) _estadoAntesDelMenu = _estado;
        _estado = Estado.Menu;

        MostrarCapa("");
        Girar(false);
        PanelCarga.Visibility = Visibility.Collapsed;
        Ayuda.Visibility = Visibility.Collapsed;
        PanelMenu.Visibility = Visibility.Visible;

        TituloMenu.Text = titulo;
        DetalleMenu.Text = detalle ?? "";
        DetalleMenu.Visibility = string.IsNullOrEmpty(detalle) ? Visibility.Collapsed : Visibility.Visible;

        _opciones = new()
        {
            new(() => "Volver a Steam", VolverASteam),
            new(TextoVolumen, AlternarSilencio, CambiarVolumen),
            new(() => "Apagar", () => Energia("/s", "Apagando…")),
            new(() => "Reiniciar PC", () => Energia("/r", "Reiniciando…")),
            new(() => "Reiniciar Steam", () => _ = ReiniciarSteamAsync()),
            new(() => "Cambiar de usuario", CambiarUsuario),
            new(() => "Modo escritorio", ModoEscritorio),
        };
        _seleccion = 0;
        DibujarOpciones();
        IniciarSensores();
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
                    Text = _opciones[i].Texto(),
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
        var opcion = _opciones[_seleccion];
        Log.Escribir($"Menú: {opcion.Texto()}");
        opcion.Accion();
    }

    private void AjustarSeleccion(int direccion)
    {
        if (_opciones.Count == 0) return;
        _opciones[_seleccion].Ajustar?.Invoke(direccion);
    }

    private void CerrarMenu()
    {
        if (_estadoAntesDelMenu == Estado.Escritorio)
        {
            _estado = Estado.Escritorio;
            OcultarCapa();
            return;
        }
        VolverASteam();
    }

    // ===================== Estado del PC (solo con el panel visible) =====================

    private void IniciarSensores()
    {
        if (_timerSensores.IsEnabled) return;

        _sensores.Reiniciar();
        foreach (var g in new[] { _graficaCpu, _graficaTempSistema, _graficaGpu, _graficaTempGpu }) g.Limpiar();
        PanelProcesos.Children.Clear();
        _puertoDibujado = -2;
        _timerSensores.Start();
        TickSensores();
    }

    /// <summary>Lee los sensores en un hilo aparte para que el mando y la interfaz nunca esperen.</summary>
    private async void TickSensores()
    {
        if (_estado != Estado.Menu)
        {
            _timerSensores.Stop();
            return;
        }
        if (_leyendoSensores) return;

        _leyendoSensores = true;
        try
        {
            var lectura = await Task.Run(_sensores.Leer);
            if (_estado == Estado.Menu) MostrarLectura(lectura);
        }
        catch (Exception ex)
        {
            Log.Escribir("Error leyendo el estado del PC: " + ex.Message);
        }
        finally
        {
            _leyendoSensores = false;
        }
    }

    private void MostrarLectura(LecturaPc l)
    {
        _graficaCpu.Agregar(l.CpuUso, $"{l.CpuUso:0}%");

        _graficaTempSistema.Visibility = l.TempSistema is null ? Visibility.Collapsed : Visibility.Visible;
        _graficaTempSistema.Agregar(l.TempSistema, $"{l.TempSistema:0} °C", alerta: l.TempSistema >= 85);

        var visibilidadGpu = l.Gpu is null ? Visibility.Collapsed : Visibility.Visible;
        _graficaGpu.Visibility = _graficaTempGpu.Visibility = visibilidadGpu;
        if (l.Gpu is { } gpu)
        {
            _graficaGpu.Agregar(gpu.Uso, $"{gpu.Uso:0}%");
            _graficaTempGpu.Agregar(gpu.Temperatura, $"{gpu.Temperatura:0} °C", alerta: gpu.Temperatura >= 83);
        }

        TextoHardware.Text = _sensores.NombreGpu is { } nombreGpu ? $"{_sensores.NombreCpu}  ·  {nombreGpu}" : _sensores.NombreCpu;

        var memoria = $"RAM {l.RamUsadaGb:0.0} / {l.RamTotalGb:0} GB";
        if (l.Gpu is { } g)
        {
            memoria += $"     VRAM {g.VramUsadaGb:0.0} / {g.VramTotalGb:0} GB";
            if (g.Watts is double w) memoria += $"     GPU {w:0} W";
        }
        TextoMemoria.Text = memoria;

        if (l.Procesos is { } procesos) DibujarProcesos(procesos);

        _baterias = l.Baterias;
        DibujarMandos();

        if (l.Volumen != _volumen)
        {
            _volumen = l.Volumen;
            DibujarOpciones();
        }
    }

    /// <summary>Top 5 de programas por RAM, con una barra relativa al que más usa.</summary>
    private void DibujarProcesos(IReadOnlyList<ProcesoRam> procesos)
    {
        PanelProcesos.Children.Clear();
        if (procesos.Count == 0) return;

        double maximo = Math.Max(1, procesos[0].Mb);
        foreach (var p in procesos)
        {
            var fila = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            fila.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(250) });
            fila.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            fila.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });

            var nombre = new TextBlock
            {
                Text = p.Cantidad > 1 ? $"{p.Nombre}  ×{p.Cantidad}" : p.Nombre,
                FontSize = 17,
                Foreground = Brushes.White,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center
            };

            var barra = new Rectangle
            {
                Height = 8,
                RadiusX = 4,
                RadiusY = 4,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 8, 0),
                Width = Math.Max(4, 230 * p.Mb / maximo),
                Fill = ColorAcento
            };
            Grid.SetColumn(barra, 1);

            var tamano = new TextBlock
            {
                Text = p.Mb >= 1024 ? $"{p.Mb / 1024:0.0} GB" : $"{p.Mb:0} MB",
                FontSize = 17,
                Foreground = ColorTenue,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(tamano, 2);

            fila.Children.Add(nombre);
            fila.Children.Add(barra);
            fila.Children.Add(tamano);
            PanelProcesos.Children.Add(fila);
        }
    }

    /// <summary>Una fila por mando con 4 barras de batería; el mando que se está usando se resalta.</summary>
    private void DibujarMandos()
    {
        _puertoDibujado = _mando.PuertoActivo;
        PanelMandos.Children.Clear();

        for (int p = 0; p < 4; p++)
        {
            if (!_mando.EstaConectado(p) && _baterias[p] is null) continue;

            bool activo = p == _mando.PuertoActivo;
            var fila = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
            fila.Children.Add(new TextBlock
            {
                Text = activo ? $"● Mando {p + 1}" : $"○ Mando {p + 1}",
                Width = 130,
                FontSize = 20,
                Foreground = activo ? ColorAcento : Brushes.White,
                VerticalAlignment = VerticalAlignment.Center
            });

            var bateria = _baterias[p];
            string texto;
            int barras = 0;
            Brush color = ColorAcento;

            if (bateria is null) texto = "Batería: sin datos";
            else if (bateria.Value.ConCable) { texto = "Con cable"; barras = 4; }
            else if (bateria.Value.Desconocida) texto = "Batería desconocida";
            else
            {
                var nivel = Math.Min(bateria.Value.Nivel, (byte)3);
                barras = nivel + 1;
                texto = nivel switch { 0 => "Vacía, cárgalo", 1 => "Baja", 2 => "Media", _ => "Llena" };
                if (nivel <= 1) color = ColorAlerta;
            }

            for (int i = 0; i < 4; i++)
            {
                fila.Children.Add(new Rectangle
                {
                    Width = 12,
                    Height = 22,
                    RadiusX = 2,
                    RadiusY = 2,
                    Margin = new Thickness(0, 0, 4, 0),
                    Fill = i < barras ? color : ColorBarraVacia,
                    VerticalAlignment = VerticalAlignment.Center
                });
            }

            fila.Children.Add(new TextBlock
            {
                Text = texto,
                FontSize = 18,
                Margin = new Thickness(12, 0, 0, 0),
                Foreground = color == ColorAlerta ? ColorAlerta : ColorTenue,
                VerticalAlignment = VerticalAlignment.Center
            });
            PanelMandos.Children.Add(fila);
        }

        if (PanelMandos.Children.Count == 0)
            PanelMandos.Children.Add(new TextBlock { Text = "Ningún mando conectado", FontSize = 18, Foreground = ColorTenue });
    }

    // ===================== Acciones del menú =====================

    private string TextoVolumen() =>
        _volumen is not { } v ? "Volumen: sin audio"
        : v.Silencio ? "Volumen   ◀  Silencio  ▶"
        : $"Volumen   ◀  {v.Nivel}%  ▶";

    private void CambiarVolumen(int direccion)
    {
        Volumen.Cambiar(direccion * PasoVolumen);
        _volumen = Volumen.Leer();
        DibujarOpciones();
    }

    private void AlternarSilencio()
    {
        Volumen.AlternarSilencio();
        _volumen = Volumen.Leer();
        DibujarOpciones();
    }

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
        OcultarCapa();
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
        // Con el menú abierto se lee a 30 Hz para que responda al instante; en reposo basta 10 Hz.
        var ritmo = _estado == Estado.Menu ? RitmoMandoMenu : RitmoMandoReposo;
        if (_timerMando.Interval != ritmo) _timerMando.Interval = ritmo;

        _mando.Actualizar();

        // Combinación de emergencia: Vista + Menú mantenidos N segundos (funciona en cualquier estado).
        if (_mando.Presionado(Botones.Vista | Botones.Menu))
        {
            _comboDesde ??= DateTime.Now;
            if (_estado is not (Estado.Menu or Estado.Ocupado) &&
                DateTime.Now - _comboDesde.Value >= TimeSpan.FromSeconds(_cfg.SegundosParaMenuEmergencia))
            {
                Log.Escribir("Centro de control abierto con el mando.");
                MostrarMenu("Centro de control");
            }
        }
        else
        {
            _comboDesde = null;
        }

        if (_estado != Estado.Menu) return;

        if (_mando.PuertoActivo != _puertoDibujado) DibujarMandos();

        if (_mando.RecienPresionado(Botones.Arriba)) MoverSeleccion(-1);
        else if (_mando.RecienPresionado(Botones.Abajo)) MoverSeleccion(1);
        else if (_mando.RecienPresionado(Botones.A)) EjecutarSeleccion();
        else if (_mando.RecienPresionado(Botones.B)) CerrarMenu();
        else ManejarAjuste();
    }

    /// <summary>◀ ▶ ajustan la opción; al mantener se repite (400 ms de espera, luego cada 90 ms).</summary>
    private void ManejarAjuste()
    {
        int direccion = _mando.Presionado(Botones.Izquierda) ? -1 : _mando.Presionado(Botones.Derecha) ? 1 : 0;
        if (direccion == 0)
        {
            _repetirAjusteDesde = null;
            return;
        }

        var ahora = DateTime.Now;
        if (_repetirAjusteDesde is null)
        {
            AjustarSeleccion(direccion);
            _repetirAjusteDesde = ahora.AddMilliseconds(400);
        }
        else if (ahora >= _repetirAjusteDesde)
        {
            AjustarSeleccion(direccion);
            _repetirAjusteDesde = ahora.AddMilliseconds(90);
        }
    }

    private void AlPresionarTecla(object sender, KeyEventArgs e)
    {
        // Teclado solo como respaldo dentro del menú.
        if (_estado != Estado.Menu) return;
        switch (e.Key)
        {
            case Key.Up: MoverSeleccion(-1); e.Handled = true; break;
            case Key.Down: MoverSeleccion(1); e.Handled = true; break;
            case Key.Left: AjustarSeleccion(-1); e.Handled = true; break;
            case Key.Right: AjustarSeleccion(1); e.Handled = true; break;
            case Key.Enter: EjecutarSeleccion(); e.Handled = true; break;
            case Key.Escape: CerrarMenu(); e.Handled = true; break;
        }
    }

    private void ActualizarBarraSuperior()
    {
        Reloj.Text = DateTime.Now.ToString("HH:mm");
        IndicadorMando.Text = _mando.Conectado ? "● Mando conectado" : "○ Conecta tu mando";
    }

    private static T Congelar<T>(T objeto) where T : Freezable
    {
        objeto.Freeze();
        return objeto;
    }

    // Este es el shell de la sesión: no se debe cerrar con Alt+F4.
    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_cerrandoSesion) e.Cancel = true;
        base.OnClosing(e);
    }
}
