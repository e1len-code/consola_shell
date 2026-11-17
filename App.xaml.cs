using System.ComponentModel;
using System.Diagnostics;
using System.Threading;
using System.Windows;

namespace ConsolaShell;

/// <summary>
/// Un solo .exe con dos modos:
/// - Shell (perfil de consola, usuario estándar, o con --shell): pantalla de carga + Steam Big Picture.
/// - Administración (cuenta administradora, o con --admin): panel para instalar y gestionar el perfil.
/// </summary>
public partial class App : Application
{
    private static Mutex? _mutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        RegistrarErrores();
        base.OnStartup(e);

        bool forzarShell = e.Args.Contains("--shell", StringComparer.OrdinalIgnoreCase);
        bool forzarAdmin = e.Args.Contains("--admin", StringComparer.OrdinalIgnoreCase);

        if (!forzarShell && (forzarAdmin || Administracion.EsUsuarioAdministrador()))
            IniciarAdministracion();
        else
            IniciarShell();
    }

    private void IniciarShell()
    {
        // Una sola instancia por sesión de Windows.
        _mutex = new Mutex(true, @"Local\ConsolaShell_Instancia", out bool esNueva);
        if (!esNueva)
        {
            Shutdown();
            return;
        }

        Log.Escribir("ConsolaShell iniciado");
        MainWindow = new MainWindow();
        MainWindow.Show();
    }

    private void IniciarAdministracion()
    {
        // Los scripts necesitan permisos de administrador: se vuelve a abrir elevado (aviso de UAC).
        if (!Administracion.EstaElevado())
        {
            try
            {
                Process.Start(new ProcessStartInfo(Environment.ProcessPath!, "--admin")
                {
                    UseShellExecute = true,
                    Verb = "runas"
                });
            }
            catch (Win32Exception)
            {
                MessageBox.Show("El panel de administración de ConsolaShell necesita permisos de administrador.",
                    "ConsolaShell", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            Shutdown();
            return;
        }

        _mutex = new Mutex(true, @"Local\ConsolaShell_Administracion", out bool esNueva);
        if (!esNueva)
        {
            Shutdown();
            return;
        }

        Log.Escribir("Panel de administración iniciado");
        MainWindow = new AdminWindow();
        MainWindow.Show();
    }

    private void RegistrarErrores()
    {
        // En modo shell, si la app se cae queda la pantalla negra.
        // Por eso se registran los errores y se intenta seguir funcionando.
        DispatcherUnhandledException += (_, a) =>
        {
            Log.Escribir("Error no controlado: " + a.Exception);
            a.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, a) =>
            Log.Escribir("Error fatal: " + a.ExceptionObject);
        TaskScheduler.UnobservedTaskException += (_, a) =>
        {
            Log.Escribir("Error en tarea: " + a.Exception);
            a.SetObserved();
        };
    }
}
