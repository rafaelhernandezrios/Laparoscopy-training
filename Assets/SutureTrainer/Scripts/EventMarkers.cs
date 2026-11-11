using System;
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
    /// Usa el paquete LSL4Unity (com.labstreaminglayer.lsl4unity), que trae
    /// liblsl para Windows, macOS y Linux. Donde no hay librería (Quest /
    /// Android) LSL se desactiva solo y los eventos siguen yendo al CSV.
    ///
    /// Formato de marca: categoria/evento|clave=valor|... solo ASCII.
    /// </summary>
    public static class EventMarkers
    {
        public const string StreamName = "SutureTrainer_Markers";

        static LSL.StreamOutlet _outlet;
        static bool _intentado;

        public static bool LslActivo => _outlet != null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init()
        {
            _intentado = false;
            _outlet = null;
            Iniciar();
            Application.quitting -= Cerrar;
            Application.quitting += Cerrar;
        }

        static void Iniciar()
        {
            if (_intentado) return;
            _intentado = true;
            try
            {
                var info = new LSL.StreamInfo(StreamName, "Markers", 1, LSL.LSL.IRREGULAR_RATE,
                                              LSL.channel_format_t.cf_string,
                                              "suturetrainer_" + SystemInfo.deviceUniqueIdentifier);
                var desc = info.desc();
                desc.append_child_value("manufacturer", "SutureTrainer");
                desc.append_child_value("version", Application.version);
                _outlet = new LSL.StreamOutlet(info);
                Debug.Log($"[Marcadores] Stream LSL '{StreamName}' publicado (liblsl {LSL.LSL.library_version()}).");
            }
            catch (Exception ex) when (ex is DllNotFoundException || ex is EntryPointNotFoundException ||
                                       ex is BadImageFormatException || ex is TypeInitializationException)
            {
                _outlet = null;
                Debug.LogWarning($"[Marcadores] liblsl no disponible en esta plataforma: los eventos solo se guardan en CSV. ({ex.GetType().Name})");
            }
        }

        static void Cerrar()
        {
            if (_outlet == null) return;
            try { _outlet.Dispose(); } catch { }
            _outlet = null;
        }

        /// <summary>Marca de tiempo en el reloj de LSL (o el de Unity si no hay LSL).</summary>
        public static double Ahora()
        {
            if (_outlet != null)
                try { return LSL.LSL.local_clock(); } catch { }
            return Time.realtimeSinceStartupAsDouble;
        }

        /// <summary>
        /// Autoprueba (F9): busca el stream en la red como lo haría LabRecorder,
        /// abre un receptor, envía "lsl/prueba" y comprueba que llega. Bloquea
        /// ~1-3 s; es para usar antes de la sesión, no durante el ejercicio.
        /// </summary>
        public static string Verificar()
        {
            if (_outlet == null) return "LSL inactivo (sin liblsl en esta plataforma).";
            try
            {
                var encontrados = LSL.LSL.resolve_stream("name", StreamName, 1, 3.0);
                if (encontrados.Length == 0) return "El stream no aparece en la red (¿firewall o red sin multicast?).";
                using (var inlet = new LSL.StreamInlet(encontrados[0]))
                {
                    inlet.open_stream(2.0);
                    string marca = "lsl/prueba|" + System.DateTime.Now.ToString("HHmmss");
                    Send(marca);
                    var buf = new string[1];
                    double t0 = LSL.LSL.local_clock();
                    while (LSL.LSL.local_clock() - t0 < 2.0)
                    {
                        double ts = inlet.pull_sample(buf, 0.5);
                        if (ts > 0 && buf[0] == marca)
                            return $"OK: '{StreamName}' visible en {encontrados[0].hostname()} y el marcador llegó " +
                                   $"({(LSL.LSL.local_clock() - ts) * 1000:0.0} ms).";
                    }
                    return "El stream se ve pero el marcador de prueba no llegó.";
                }
            }
            catch (Exception ex) { return "Error en la verificación LSL: " + ex.Message; }
        }

        /// <summary>Envía un marcador. Usar solo ASCII (sin acentos).</summary>
        public static void Send(string marca)
        {
            Iniciar();
            double ts = Ahora();
            if (_outlet != null)
            {
                try { _outlet.push_sample(new[] { marca }, ts); }
                catch (Exception ex) { Debug.LogWarning($"[Marcadores] Fallo LSL: {ex.Message}"); _outlet = null; }
            }
            MetricsRecorder.I?.AddEvent(marca, ts);
            Debug.Log($"[Marcador] {marca}");
        }
    }
}
