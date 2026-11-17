using System.Runtime.InteropServices;

namespace ConsolaShell;

/// <summary>
/// Volumen maestro del dispositivo de salida predeterminado (Core Audio, sin paquetes externos).
/// Cada llamada pide el dispositivo actual, así sigue funcionando si se cambia de TV a auriculares.
/// </summary>
public static class Volumen
{
    public static (int Nivel, bool Silencio)? Leer() => ConVolumen(v =>
    {
        v.GetMasterVolumeLevelScalar(out var nivel);
        v.GetMute(out var silencio);
        return ((int)Math.Round(nivel * 100), silencio);
    });

    /// <summary>Sube o baja el volumen en puntos porcentuales; al subir, quita el silencio.</summary>
    public static void Cambiar(int delta) => ConVolumen(v =>
    {
        var ctx = Guid.Empty;
        v.GetMasterVolumeLevelScalar(out var nivel);
        var nuevo = Math.Clamp(Math.Round(nivel * 100) + delta, 0, 100) / 100f;
        v.SetMasterVolumeLevelScalar((float)nuevo, ref ctx);
        if (delta > 0) v.SetMute(false, ref ctx);
        return true;
    });

    public static void AlternarSilencio() => ConVolumen(v =>
    {
        var ctx = Guid.Empty;
        v.GetMute(out var silencio);
        v.SetMute(!silencio, ref ctx);
        return true;
    });

    private static bool _fallaRegistrada;

    private static T? ConVolumen<T>(Func<IAudioEndpointVolume, T> accion) where T : struct
    {
        object? enumerador = null, dispositivo = null, volumen = null;
        try
        {
            enumerador = new MMDeviceEnumeratorCom();
            ((IMMDeviceEnumerator)enumerador).GetDefaultAudioEndpoint(0 /* eRender */, 1 /* eMultimedia */, out var d);
            dispositivo = d;
            var iid = typeof(IAudioEndpointVolume).GUID;
            d.Activate(ref iid, 0x17 /* CLSCTX_ALL */, IntPtr.Zero, out volumen);
            var resultado = accion((IAudioEndpointVolume)volumen);
            _fallaRegistrada = false;
            return resultado;
        }
        catch (Exception ex)
        {
            // Sin dispositivo de audio (TV apagada, HDMI desconectado): no es grave. Se registra una sola vez.
            if (!_fallaRegistrada) Log.Escribir("Volumen no disponible: " + ex.Message);
            _fallaRegistrada = true;
            return null;
        }
        finally
        {
            foreach (var o in new[] { volumen, dispositivo, enumerador })
                if (o is not null && Marshal.IsComObject(o)) Marshal.ReleaseComObject(o);
        }
    }

    // ---------- Interfaces COM mínimas (el orden de los métodos debe coincidir con la vtable) ----------

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumeratorCom { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        void EnumAudioEndpoints(int flujo, int estados, out IntPtr dispositivos);
        void GetDefaultAudioEndpoint(int flujo, int rol, out IMMDevice dispositivo);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        void Activate(ref Guid iid, int contexto, IntPtr parametros, [MarshalAs(UnmanagedType.IUnknown)] out object interfaz);
    }

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        void RegisterControlChangeNotify(IntPtr notificador);
        void UnregisterControlChangeNotify(IntPtr notificador);
        void GetChannelCount(out uint canales);
        void SetMasterVolumeLevel(float nivelDb, ref Guid contexto);
        void SetMasterVolumeLevelScalar(float nivel, ref Guid contexto);
        void GetMasterVolumeLevel(out float nivelDb);
        void GetMasterVolumeLevelScalar(out float nivel);
        void SetChannelVolumeLevel(uint canal, float nivelDb, ref Guid contexto);
        void SetChannelVolumeLevelScalar(uint canal, float nivel, ref Guid contexto);
        void GetChannelVolumeLevel(uint canal, out float nivelDb);
        void GetChannelVolumeLevelScalar(uint canal, out float nivel);
        void SetMute([MarshalAs(UnmanagedType.Bool)] bool silencio, ref Guid contexto);
        void GetMute([MarshalAs(UnmanagedType.Bool)] out bool silencio);
    }
}
