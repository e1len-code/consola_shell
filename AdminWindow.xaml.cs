using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ConsolaShell;

/// <summary>
/// Panel de administración: se abre cuando ConsolaShell.exe se ejecuta desde una cuenta administradora.
/// Instala, activa, renombra, configura y elimina el perfil de consola sin escribir comandos.
/// Todo lo que toca el sistema lo hacen los scripts de scripts\ (que se niegan a tocar cuentas administradoras).
/// </summary>
public partial class AdminWindow : Window
{
    private static readonly Brush Verde = Congelar(new SolidColorBrush(Color.FromRgb(0x81, 0xC7, 0x84)));
    private static readonly Brush Rojo = Congelar(new SolidColorBrush(Color.FromRgb(0xFF, 0x8A, 0x80)));
    private static readonly Brush Tenue = Congelar(new SolidColorBrush(Color.FromRgb(0x7F, 0x8D, 0xA3)));

    private Configuracion _cfg = new();
    private string _rutaConfig = "";
    private EstadoConsola? _estado;
    private bool _ocupado;

    private string Usuario => _cfg.NombreUsuario;

    public AdminWindow()
    {
        InitializeComponent();
        TextoSesion.Text = $"Administración · sesión de {Environment.UserName} (administrador). " +
                           "Este panel solo modifica el perfil de consola.";

        if (Administracion.EjecutandoDesdeDestino)
        {
            BtnInstalar.IsEnabled = false;
            NotaInstalar.Text = @"Estás usando la copia instalada en C:\ConsolaShell. Para actualizar, abre el ConsolaShell.exe de la carpeta publish.";
            NotaInstalar.Visibility = Visibility.Visible;
            ChkBorrarArchivos.IsEnabled = false;
            ChkBorrarArchivos.ToolTip = @"No se puede borrar C:\ConsolaShell mientras este panel se ejecuta desde ahí.";
        }

        Loaded += async (_, _) =>
        {
            CargarConfiguracion();
            await ConOcupado(ActualizarEstadoAsync);
        };
    }

    // ===================== Estado =====================

    private async Task ActualizarEstadoAsync()
    {
        ListaEstado.Children.Clear();
        ListaEstado.Children.Add(new TextBlock { Text = "Leyendo estado…", Foreground = Tenue });
        _estado = await Administracion.LeerEstadoAsync(Usuario, Escribir);
        MostrarEstado();
    }

    private void MostrarEstado()
    {
        ListaEstado.Children.Clear();
        var e = _estado;
        if (e is null)
        {
            Fila(false, "No se pudo leer el estado (revisa el registro de abajo).");
            SiguientePaso.Text = "Prueba «Actualizar estado». Si sigue fallando, revisa el registro.";
            foreach (var b in new[] { BtnActivar, BtnRenombrar, BtnRevertir, BtnEliminar }) b.IsEnabled = true;
            return;
        }

        Fila(e.Instalado, e.Instalado ? @"Archivos en C:\ConsolaShell" : "ConsolaShell no está instalado");
        if (!e.UsuarioExiste) Fila(false, $"El perfil «{e.Usuario}» no existe");
        else if (e.UsuarioEsAdmin) Fila(false, $"«{e.Usuario}» es administrador: debe ser usuario estándar");
        else Fila(true, $"Perfil «{e.Usuario}» (usuario estándar)");
        if (e.UsuarioExiste)
            Fila(e.PerfilCreado, e.PerfilCreado ? "Ya inició sesión al menos una vez" : "Aún no ha iniciado sesión nunca");
        Fila(e.Autologin, e.Autologin ? $"Inicio automático en «{e.Usuario}»" : "Inicio automático desactivado");
        Fila(e.SteamRecordado, e.SteamRecordado switch
        {
            true => "Steam con sesión recordada",
            false => "Steam sin sesión recordada",
            null => "Steam: sin datos"
        });
        Fila(e.ModoConsola, e.ModoConsola switch
        {
            true => "Modo consola activo",
            false => "Modo consola inactivo (escritorio normal)",
            null => "Modo consola: sin datos"
        });
        if (e.SesionAbierta) Fila(null, $"La sesión de «{e.Usuario}» está abierta ahora");

        SiguientePaso.Text = "Siguiente paso: " + CalcularSiguientePaso(e);

        BtnActivar.IsEnabled = e.Instalado && e.UsuarioExiste && e.PerfilCreado && !e.UsuarioEsAdmin;
        BtnRenombrar.IsEnabled = e.UsuarioExiste && !e.UsuarioEsAdmin;
        BtnRevertir.IsEnabled = (e.UsuarioExiste || e.Autologin) && !e.UsuarioEsAdmin;
        BtnEliminar.IsEnabled = e.UsuarioExiste && !e.SesionAbierta && !e.UsuarioEsAdmin;
        BtnEliminar.ToolTip = e.SesionAbierta ? "Su sesión está abierta: reinicia el PC y vuelve a intentarlo." : null;
        TxtUsuario.ToolTip = e.UsuarioExiste ? null : "Nombre con el que se creará el perfil al instalar.";
    }

    private static string CalcularSiguientePaso(EstadoConsola e)
    {
        if (e.UsuarioEsAdmin)
            return $"quita a «{e.Usuario}» del grupo Administradores o usa otro nombre de perfil.";
        if (!e.Instalado || !e.UsuarioExiste || !e.Autologin)
            return "pulsa «Instalar / actualizar».";
        if (!e.PerfilCreado)
            return $"reinicia el PC: entrará solo a «{e.Usuario}» con escritorio normal.";
        if (e.SteamRecordado != true)
            return $"en «{e.Usuario}», abre Steam e inicia sesión marcando «Recordar contraseña». " +
                   "Luego vuelve a tu cuenta (Ctrl+Alt+Supr → Cambiar de usuario) y abre este panel.";
        if (e.ModoConsola != true)
            return "pulsa «Activar modo consola».";
        return "todo listo. Reinicia para entrar en modo consola.";
    }

    private void Fila(bool? ok, string texto)
    {
        var fila = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 3) };
        fila.Children.Add(new TextBlock
        {
            Text = ok switch { true => "✔", false => "✖", null => "•" },
            Width = 26,
            Foreground = ok switch { true => Verde, false => Rojo, null => Tenue }
        });
        fila.Children.Add(new TextBlock { Text = texto, TextWrapping = TextWrapping.Wrap });
        ListaEstado.Children.Add(fila);
    }

    // ===================== Configuración =====================

    private void CargarConfiguracion()
    {
        var instalada = Path.Combine(Administracion.Destino, "config.json");
        _rutaConfig = File.Exists(instalada) ? instalada : Path.Combine(AppContext.BaseDirectory, "config.json");
        _cfg = Configuracion.Cargar(_rutaConfig);

        TxtUsuario.Text = _cfg.NombreUsuario;
        TxtNombrePerfil.Text = _cfg.NombrePerfil;
        TxtRutaSteam.Text = _cfg.RutaSteam;
        TxtTiempoCarga.Text = _cfg.TiempoMaximoCargaSegundos.ToString();
        TxtSegundosMenu.Text = _cfg.SegundosParaMenuEmergencia.ToString();
        TxtSegundosPanel.Text = _cfg.SegundosSinBigPictureParaPanel.ToString();
        TextoRutaConfig.Text = $"{_rutaConfig} · se aplica la próxima vez que se inicie ConsolaShell.";
    }

    private void AlGuardar(object sender, RoutedEventArgs e)
    {
        var nombre = TxtNombrePerfil.Text.Trim();
        var rutaSteam = TxtRutaSteam.Text.Trim().Trim('"');

        string? error =
            nombre.Length == 0 ? "El nombre en pantalla no puede quedar vacío." :
            rutaSteam.Length > 0 && !File.Exists(rutaSteam) ? $"No existe el archivo {rutaSteam}." :
            !Entero(TxtTiempoCarga, 15, 600, out _) ? "La espera máxima de carga debe estar entre 15 y 600 s." :
            !Entero(TxtSegundosMenu, 1, 10, out _) ? "Vista + Menú debe estar entre 1 y 10 s." :
            !Entero(TxtSegundosPanel, 1, 60, out _) ? "El panel al salir de Big Picture debe estar entre 1 y 60 s." :
            null;

        if (error is not null)
        {
            MessageBox.Show(this, error, "Configuración", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _cfg.NombrePerfil = nombre;
        _cfg.RutaSteam = rutaSteam;
        _cfg.TiempoMaximoCargaSegundos = int.Parse(TxtTiempoCarga.Text.Trim());
        _cfg.SegundosParaMenuEmergencia = int.Parse(TxtSegundosMenu.Text.Trim());
        _cfg.SegundosSinBigPictureParaPanel = int.Parse(TxtSegundosPanel.Text.Trim());

        try
        {
            _cfg.Guardar(_rutaConfig);
            Escribir($"✔ Configuración guardada en {_rutaConfig}");
        }
        catch (Exception ex)
        {
            Log.Escribir("No se pudo guardar config.json: " + ex);
            MessageBox.Show(this, "No se pudo guardar: " + ex.Message, "Configuración", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static bool Entero(TextBox caja, int minimo, int maximo, out int valor) =>
        int.TryParse(caja.Text.Trim(), out valor) && valor >= minimo && valor <= maximo;

    // ===================== Acciones =====================

    private async void AlActualizar(object sender, RoutedEventArgs e) => await ConOcupado(ActualizarEstadoAsync);

    private async void AlInstalar(object sender, RoutedEventArgs e)
    {
        // Si el perfil aún no existe, se crea con el nombre escrito en "Perfil de consola".
        var usuario = _estado?.UsuarioExiste == true ? Usuario : TxtUsuario.Text.Trim();
        if (!Administracion.NombreValido(usuario))
        {
            MessageBox.Show(this, "Escribe un nombre de perfil válido: letras, números, espacio, punto, guion o guion bajo (máx. 20).",
                "Instalar", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (string.Equals(usuario, Environment.UserName, StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(this, "El perfil de consola debe ser una cuenta distinta de la tuya.", "Instalar",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (_estado is { SesionAbierta: true, Instalado: true } &&
            !Confirmar($"La sesión de «{usuario}» está abierta y ConsolaShell.exe está en uso: puede que no se pueda reemplazar.\n\n" +
                       "Lo más seguro es reiniciar el PC antes de actualizar. ¿Intentar de todos modos?", "Instalar"))
            return;

        await EjecutarAsync("Instalar / actualizar", "1-InstalarConsola.ps1",
            "-Usuario", usuario,
            "-Origen", Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory),
            "-Destino", Administracion.Destino);
    }

    private async void AlActivar(object sender, RoutedEventArgs e)
    {
        var argumentos = new List<string> { "-Usuario", Usuario, "-Destino", Administracion.Destino };
        if (_estado?.SteamRecordado == false)
        {
            if (!Confirmar($"Steam no tiene sesión recordada en «{Usuario}»: Big Picture pedirá usuario y contraseña en cada arranque.\n\n" +
                           "¿Activar el modo consola de todos modos?", "Activar modo consola"))
                return;
            argumentos.Add("-Forzar");
        }

        if (await EjecutarAsync("Activar modo consola", "ActivarDesdeAdmin.ps1", argumentos.ToArray()))
            Escribir("Reinicia el PC para entrar en modo consola.");
    }

    private async void AlRenombrar(object sender, RoutedEventArgs e)
    {
        var nuevo = TxtUsuario.Text.Trim();
        if (nuevo == Usuario) return;
        if (!Administracion.NombreValido(nuevo))
        {
            MessageBox.Show(this, "Nombre no válido. Usa letras, números, espacio, punto, guion o guion bajo (máx. 20, sin terminar en punto).",
                "Renombrar", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (!Confirmar($"¿Renombrar el perfil «{Usuario}» a «{nuevo}»?\n\nTambién se actualizan el inicio automático y el nombre de la pantalla de carga.",
                "Renombrar perfil"))
            return;

        await EjecutarAsync("Renombrar perfil", "RenombrarPerfil.ps1",
            "-Usuario", Usuario, "-NuevoNombre", nuevo, "-Destino", Administracion.Destino);
    }

    private async void AlRevertir(object sender, RoutedEventArgs e)
    {
        if (!Confirmar($"Se quitarán el inicio automático y el modo consola de «{Usuario}»: volverá a tener escritorio normal.\n\n" +
                       "El perfil y sus datos se conservan. Tu cuenta no cambia. ¿Continuar?", "Desactivar modo consola"))
            return;

        await EjecutarAsync("Desactivar modo consola", "3-RevertirConsola.ps1", "-Usuario", Usuario);
    }

    private async void AlEliminar(object sender, RoutedEventArgs e)
    {
        bool borrarArchivos = ChkBorrarArchivos.IsChecked == true && ChkBorrarArchivos.IsEnabled;
        var detalle = borrarArchivos ? @" y la carpeta C:\ConsolaShell" : "";
        if (!Confirmar($"Se eliminará la cuenta «{Usuario}», su carpeta de usuario (partidas guardadas locales, sesión de Steam de ese perfil…){detalle}.\n\n" +
                       "Esto NO se puede deshacer. Tu cuenta no cambia. ¿Eliminar?", "Eliminar perfil"))
            return;

        var argumentos = new List<string> { "-Usuario", Usuario, "-EliminarUsuario" };
        if (borrarArchivos) argumentos.Add("-EliminarArchivos");
        await EjecutarAsync("Eliminar perfil", "3-RevertirConsola.ps1", argumentos.ToArray());
    }

    private void AlProbar(object sender, RoutedEventArgs e)
    {
        if (!Confirmar("Se abrirá el modo consola en esta cuenta, a pantalla completa y con Steam.\n\n" +
                       "Para salir: mantén Vista + Menú → «Modo escritorio» y cierra ConsolaShell desde el Administrador de tareas. ¿Abrir?",
                "Probar modo consola"))
            return;

        try
        {
            Process.Start(new ProcessStartInfo(Environment.ProcessPath!, "--shell") { UseShellExecute = false });
        }
        catch (Exception ex)
        {
            Escribir("No se pudo abrir el modo consola: " + ex.Message);
        }
    }

    private void AlReiniciar(object sender, RoutedEventArgs e)
    {
        if (!Confirmar("¿Reiniciar el PC ahora? Guarda lo que tengas abierto.", "Reiniciar PC")) return;
        try
        {
            Process.Start(new ProcessStartInfo("shutdown", "/r /t 0") { UseShellExecute = false, CreateNoWindow = true });
        }
        catch (Exception ex)
        {
            Escribir("No se pudo reiniciar: " + ex.Message);
        }
    }

    // ===================== Utilidades =====================

    /// <summary>Ejecuta un script con el panel bloqueado y después relee la configuración y el estado.</summary>
    private async Task<bool> EjecutarAsync(string descripcion, string script, params string[] argumentos)
    {
        bool exito = false;
        await ConOcupado(async () =>
        {
            Escribir("");
            Escribir($"▶ {descripcion}…");
            var codigo = await Administracion.EjecutarScriptAsync(script, argumentos, Escribir);
            exito = codigo == 0;
            Escribir(exito ? "✔ Listo." : $"✖ No se completó (código {codigo}). Revisa los mensajes de arriba.");
            CargarConfiguracion();
            await ActualizarEstadoAsync();
        });
        return exito;
    }

    private async Task ConOcupado(Func<Task> trabajo)
    {
        if (_ocupado) return;
        _ocupado = true;
        Contenido.IsEnabled = false;
        Mouse.OverrideCursor = Cursors.Wait;
        try
        {
            await trabajo();
        }
        catch (Exception ex)
        {
            Log.Escribir("Error en el panel de administración: " + ex);
            Escribir("Error: " + ex.Message);
        }
        finally
        {
            Mouse.OverrideCursor = null;
            Contenido.IsEnabled = true;
            _ocupado = false;
            if (Administracion.EjecutandoDesdeDestino) BtnInstalar.IsEnabled = false;
        }
    }

    /// <summary>Agrega una línea al registro. Se puede llamar desde cualquier hilo.</summary>
    private void Escribir(string linea)
    {
        Dispatcher.BeginInvoke(() =>
        {
            Registro.AppendText(linea + Environment.NewLine);
            Registro.ScrollToEnd();
        });
    }

    private bool Confirmar(string texto, string titulo) =>
        MessageBox.Show(this, texto, titulo, MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No)
        == MessageBoxResult.Yes;

    private static T Congelar<T>(T objeto) where T : Freezable
    {
        objeto.Freeze();
        return objeto;
    }
}
