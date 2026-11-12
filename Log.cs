using System.IO;

namespace ConsolaShell;

/// <summary>Log simple en %LOCALAPPDATA%\ConsolaShell\log.txt</summary>
public static class Log
{
    private static readonly string Ruta = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ConsolaShell", "log.txt");

    private static readonly object Candado = new();

    public static void Escribir(string mensaje)
    {
        try
        {
            lock (Candado)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Ruta)!);
                var info = new FileInfo(Ruta);
                if (info.Exists && info.Length > 1_000_000) File.Delete(Ruta);
                File.AppendAllText(Ruta, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {mensaje}{Environment.NewLine}");
            }
        }
        catch
        {
            // El log nunca debe tumbar el shell.
        }
    }
}
