using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using AdvancedRope;
using UnityEditor;
using UnityEngine;

namespace SutureTrainer.EditorTools
{
    /// <summary>
    /// Graba una sesión de sutura para poder revisarla fuera del visor: una
    /// tira de PNG del Game view más un CSV con el estado de la herida en cada
    /// instante.
    ///
    /// Existe porque la física de la herida no se puede juzgar con una captura
    /// suelta ni leyendo el código: hay que ver CÓMO evoluciona el hueco entre
    /// los labios mientras se tensa el hilo. El CSV es la mitad importante —
    /// los píxeles dicen si se ve bien, los números dicen por qué no.
    ///
    /// Todo se escribe en `Captures/` en la raíz del proyecto, FUERA de Assets,
    /// para que Unity no importe cada PNG como textura.
    /// </summary>
    public static class SutureCapture
    {
        const string StartPath = "SutureTrainer/Captura: grabar sesión de sutura";
        const string StopPath = "SutureTrainer/Captura: parar grabación";

        const float IntervalSeconds = 0.25f;
        const float MaxSeconds = 40f;

        static bool _recording;
        static string _dir;
        static double _nextSample;
        static double _startTime;
        static int _frame;
        static StringBuilder _csv;

        [MenuItem(StartPath)]
        static void Start()
        {
            if (!EditorApplication.isPlaying)
            {
                EditorUtility.DisplayDialog(
                    "Hay que estar en Play",
                    "La grabación captura la simulación en marcha.\n\n" +
                    "Entra en Play (la escena de sutura), y con el juego " +
                    "corriendo vuelve a lanzar esta opción. Luego sutura " +
                    "normalmente: se graban 40 s o hasta que pares.",
                    "Vale");
                return;
            }

            if (_recording) { Stop(); return; }

            _dir = Path.Combine(
                Directory.GetParent(Application.dataPath).FullName,
                "Captures",
                System.DateTime.Now.ToString("yyyyMMdd_HHmmss"));
            Directory.CreateDirectory(_dir);

            _csv = new StringBuilder();
            _csv.AppendLine(Header());

            WriteSessionInfo();

            _frame = 0;
            _startTime = EditorApplication.timeSinceStartup;
            _nextSample = _startTime;
            _recording = true;

            EditorApplication.update += Tick;
            Debug.Log($"[Captura] Grabando en {_dir}");
        }

        [MenuItem(StopPath)]
        static void Stop()
        {
            if (!_recording) return;

            _recording = false;
            EditorApplication.update -= Tick;

            if (_csv != null && _dir != null)
                File.WriteAllText(Path.Combine(_dir, "herida.csv"), _csv.ToString());

            Debug.Log($"[Captura] {_frame} muestras en {_dir}");
            _csv = null;
        }

        [MenuItem(StopPath, validate = true)]
        static bool StopValidate() => _recording;

        // ------------------------------------------------------------------
        static void Tick()
        {
            // Salir de Play a media grabación tiene que cerrar el CSV, o se
            // pierde todo lo muestreado.
            if (!EditorApplication.isPlaying) { Stop(); return; }

            double now = EditorApplication.timeSinceStartup;
            if (now < _nextSample) return;
            if (now - _startTime > MaxSeconds) { Stop(); return; }

            _nextSample = now + IntervalSeconds;

            ScreenCapture.CaptureScreenshot(
                Path.Combine(_dir, $"frame_{_frame:D3}.png"));

            _csv.AppendLine(Sample((float)(now - _startTime)));
            _frame++;
        }

        static string Header() =>
            "t,tension,pines,huecos_mm,hueco_medio_mm,hueco_max_mm";

        /// <summary>
        /// Deja constancia de QUÉ se estaba ejecutando, en un archivo aparte
        /// para no ensuciar el CSV.
        ///
        /// Sin esto no se puede saber leyendo una grabación si corrió
        /// AdvancedRopeThread o VerletThread: las dos dan telemetría muy
        /// parecida y en la sesión del 28-08 hubo que adivinarlo. También
        /// delata el caso en que las escenas no se han reconstruido tras tocar
        /// SceneBuilder, que ya ha despistado dos veces.
        /// </summary>
        static void WriteSessionInfo()
        {
            var th = Object.FindFirstObjectByType<ThreadBase>();
            var rope = Object.FindFirstObjectByType<VerletRopeGenerator>();
            var escena = UnityEngine.SceneManagement.SceneManager.GetActiveScene();

            var sb = new StringBuilder();
            sb.AppendLine($"escena     : {escena.name}");
            sb.AppendLine($"hilo       : {(th != null ? th.GetType().Name : "ninguno")}");
            sb.AppendLine($"rope suelto: {(rope != null ? "si" : "no")}");
            sb.AppendLine($"factoria   : {(TrainingLevel.ThreadFactory != null ? "registrada" : "por defecto")}");

            // Cuándo se construyo la escena, para cazar el caso de estar
            // probando una escena mas vieja que el codigo.
            string ruta = escena.path;
            if (!string.IsNullOrEmpty(ruta) && File.Exists(ruta))
                sb.AppendLine($"escena del : {File.GetLastWriteTime(ruta):yyyy-MM-dd HH:mm:ss}");

            File.WriteAllText(Path.Combine(_dir, "sesion.txt"), sb.ToString());
            Debug.Log("[Captura] " + sb.ToString());
        }

        /// <summary>
        /// Una fila del CSV. El dato clave es el hueco entre labios en cada
        /// par: es lo que una sutura de verdad tiene que ir cerrando, y lo que
        /// permite distinguir "el hilo tensa pero el tejido no responde" de
        /// "el tejido responde pero se pasa de frenada".
        /// </summary>
        static string Sample(float t)
        {
            var organ = Object.FindFirstObjectByType<OrganRig>();
            var thread = Object.FindFirstObjectByType<ThreadBase>();

            float tension = thread != null ? thread.Tension : float.NaN;
            bool pins = thread != null && thread.HasPins;

            // ST_06 no usa ThreadBase sino el rope de AdvancedRope, así que
            // mirando solo ThreadBase la tensión salía NaN en toda la sesión.
            // Aquí se mide igual que VerletThread: longitud recorrida entre
            // extremos frente a la longitud en reposo.
            if (thread == null)
            {
                var rope = Object.FindFirstObjectByType<VerletRopeGenerator>();
                if (rope != null && rope.Nodes != null && rope.Nodes.Count > 1)
                {
                    var nodes = rope.Nodes;
                    float recorrida = 0f;
                    int anclados = 0;
                    for (int i = 1; i < nodes.Count; i++)
                        recorrida += Vector3.Distance(nodes[i - 1].position, nodes[i].position);
                    foreach (var n in nodes) if (n.isPinned) anclados++;

                    float directa = Vector3.Distance(nodes[0].position,
                                                     nodes[nodes.Count - 1].position);
                    tension = recorrida > 1e-4f ? directa / recorrida : 0f;
                    pins = anclados > 0;
                }
            }

            var gaps = new List<float>();
            if (organ != null)
            {
                for (int i = 0; i < organ.PairCount; i++)
                {
                    var e = organ.pairs[i].entry;
                    var x = organ.pairs[i].exit;
                    if (e == null || x == null) continue;
                    gaps.Add(Vector3.Distance(e.transform.position,
                                              x.transform.position) * 1000f);
                }
            }

            float media = 0f, max = 0f;
            foreach (var g in gaps) { media += g; if (g > max) max = g; }
            if (gaps.Count > 0) media /= gaps.Count;

            var inv = CultureInfo.InvariantCulture;
            var lista = string.Join(" ", gaps.ConvertAll(g => g.ToString("F1", inv)));

            return string.Join(",",
                t.ToString("F2", inv),
                tension.ToString("F4", inv),
                pins ? "1" : "0",
                $"\"{lista}\"",
                media.ToString("F1", inv),
                max.ToString("F1", inv));
        }
    }
}
