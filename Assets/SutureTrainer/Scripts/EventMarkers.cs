using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace SutureTrainer
{
    /// <summary>
    /// Marcadores de eventos para alinear el EEG con lo que pasa en la simulación.
    ///
    /// Publica un stream LSL de tipo "Markers" (nombre SutureTrainer_Markers,
    /// 1 canal de texto, irregular) que VR EEG Studio / LabRecorder graban junto
    /// al EEG con el mismo reloj. Además cada evento se guarda en el CSV de la
    /// sesión (eventos_*.csv) con el tiempo del intento y la marca LSL.
    ///
    /// Requiere Assets/Plugins/x86_64/lsl.dll (liblsl, release oficial de
    /// sccn/liblsl). Si no está —o en Quest/Android—, LSL se desactiva solo y
    /// los eventos siguen yendo al CSV.
    ///
    /// Formato de marca: categoria/evento|clave=valor|... solo ASCII.
    /// </summary>
    public static class EventMarkers
    {
        public const string StreamName = "SutureTrainer_Markers";

        static IntPtr _outlet = IntPtr.Zero;
        static bool _intentado;

        public static bool LslActivo => _outlet != IntPtr.Zero;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init()
        {
            _intentado = false;
            _outlet = IntPtr.Zero;
            Iniciar();
            Application.quitting += Cerrar;
        }

        static void Iniciar()
        {
            if (_intentado) return;
            _intentado = true;
            try
            {
                IntPtr info = Native.lsl_create_streaminfo(StreamName, "Markers", 1, 0.0, 3 /*cft_string*/,
                                                           "suturetrainer_" + SystemInfo.deviceUniqueIdentifier);
                _outlet = Native.lsl_create_outlet(info, 0, 360);
                Debug.Log($"[Marcadores] Stream LSL '{StreamName}' publicado.");
            }
            catch (Exception ex) when (ex is DllNotFoundException || ex is EntryPointNotFoundException ||
                                       ex is BadImageFormatException)
            {
                _outlet = IntPtr.Zero;
                Debug.LogWarning("[Marcadores] liblsl no disponible (falta Assets/Plugins/x86_64/lsl.dll): " +
                                 "los eventos solo se guardan en CSV.");
            }
        }

        static void Cerrar()
        {
            if (_outlet == IntPtr.Zero) return;
            try { Native.lsl_destroy_outlet(_outlet); } catch { }
            _outlet = IntPtr.Zero;
        }

        /// <summary>Marca de tiempo en el reloj de LSL (o el de Unity si no hay LSL).</summary>
        public static double Ahora()
        {
            if (_outlet != IntPtr.Zero)
                try { return Native.lsl_local_clock(); } catch { }
            return Time.realtimeSinceStartupAsDouble;
        }

        /// <summary>Envía un marcador. Usar solo ASCII (sin acentos).</summary>
        public static void Send(string marca)
        {
            Iniciar();
            double ts = Ahora();
            if (_outlet != IntPtr.Zero)
            {
                try { Native.lsl_push_sample_str(_outlet, new[] { marca }); }
                catch (Exception ex) { Debug.LogWarning($"[Marcadores] Fallo LSL: {ex.Message}"); _outlet = IntPtr.Zero; }
            }
            MetricsRecorder.I?.AddEvent(marca, ts);
            Debug.Log($"[Marcador] {marca}");
        }

        static class Native
        {
            const string Dll = "lsl";

            [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
            public static extern IntPtr lsl_create_streaminfo(string name, string type, int channel_count,
                                                              double nominal_srate, int channel_format, string source_id);

            [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
            public static extern IntPtr lsl_create_outlet(IntPtr info, int chunk_size, int max_buffered);

            [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
            public static extern int lsl_push_sample_str(IntPtr outlet,
                [MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPStr)] string[] data);

            [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
            public static extern double lsl_local_clock();

            [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
            public static extern void lsl_destroy_outlet(IntPtr outlet);
        }
    }
}
