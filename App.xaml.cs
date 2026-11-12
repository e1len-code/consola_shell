using System.Threading;
using System.Windows;

namespace ConsolaShell;

public partial class App : Application
{
    private static Mutex? _mutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        // Una sola instancia por sesión de Windows.
        _mutex = new Mutex(true, @"Local\ConsolaShell_Instancia", out bool esNueva);
        if (!esNueva)
        {
            Shutdown();
            return;
        }

        // Esta app es el shell de la sesión: si se cae, queda la pantalla negra.
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

        base.OnStartup(e);
        Log.Escribir("ConsolaShell iniciado");

        MainWindow = new MainWindow();
        MainWindow.Show();
    }
}
