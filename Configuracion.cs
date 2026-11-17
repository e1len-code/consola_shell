using System.IO;
using System.Text.Json;

namespace ConsolaShell;

/// <summary>Configuración leída de config.json (junto al .exe).</summary>
public sealed class Configuracion
{
    /// <summary>Cuenta de Windows del perfil de consola (la cambia "Renombrar" del panel de administración).</summary>
    public string NombreUsuario { get; set; } = "Consola";

    /// <summary>Nombre que se muestra en la pantalla de carga.</summary>
    public string NombrePerfil { get; set; } = "Consola";

    /// <summary>Ruta a steam.exe. Vacío = detectar automáticamente.</summary>
    public string RutaSteam { get; set; } = "";

    /// <summary>Argumentos para abrir Steam directamente en Big Picture.</summary>
    public string ArgumentosSteam { get; set; } = "-gamepadui";

    /// <summary>Textos que identifican la ventana de Big Picture (cualquier idioma).</summary>
    public string[] TitulosBigPicture { get; set; } = ["Big Picture"];

    /// <summary>Si Big Picture no aparece en este tiempo, se muestra el menú.</summary>
    public int TiempoMaximoCargaSegundos { get; set; } = 90;

    /// <summary>Segundos manteniendo Vista + Menú para abrir el menú de emergencia.</summary>
    public int SegundosParaMenuEmergencia { get; set; } = 3;

    /// <summary>Segundos sin ventana de Big Picture para mostrar el Centro de control (al salir de Big Picture).</summary>
    public int SegundosSinBigPictureParaPanel { get; set; } = 3;

    private static readonly JsonSerializerOptions Opciones = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static Configuracion Cargar() =>
        Cargar(Path.Combine(AppContext.BaseDirectory, "config.json"), crearSiFalta: true);

    public static Configuracion Cargar(string ruta, bool crearSiFalta = false)
    {
        try
        {
            if (File.Exists(ruta))
                return JsonSerializer.Deserialize<Configuracion>(File.ReadAllText(ruta), Opciones) ?? new();

            var porDefecto = new Configuracion();
            if (crearSiFalta) try { porDefecto.Guardar(ruta); } catch { }
            return porDefecto;
        }
        catch (Exception ex)
        {
            Log.Escribir("config.json inválido, se usan valores por defecto: " + ex.Message);
            return new();
        }
    }

    /// <summary>Guarda en la ruta indicada. Lanza excepción si no se puede escribir (quien llama la muestra).</summary>
    public void Guardar(string ruta) => File.WriteAllText(ruta, JsonSerializer.Serialize(this, Opciones));
}
