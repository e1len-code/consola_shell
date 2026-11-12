using System.IO;
using System.Text.Json;

namespace ConsolaShell;

/// <summary>Configuración leída de config.json (junto al .exe).</summary>
public sealed class Configuracion
{
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

    private static readonly JsonSerializerOptions Opciones = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static Configuracion Cargar()
    {
        var ruta = Path.Combine(AppContext.BaseDirectory, "config.json");
        try
        {
            if (File.Exists(ruta))
                return JsonSerializer.Deserialize<Configuracion>(File.ReadAllText(ruta), Opciones) ?? new();

            var porDefecto = new Configuracion();
            try { File.WriteAllText(ruta, JsonSerializer.Serialize(porDefecto, Opciones)); } catch { }
            return porDefecto;
        }
        catch (Exception ex)
        {
            Log.Escribir("config.json inválido, se usan valores por defecto: " + ex.Message);
            return new();
        }
    }
}
