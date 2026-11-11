using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace SutureTrainer
{
    /// <summary>
    /// Cierra el lazo carga cognitiva -> dificultad del ejercicio.
    ///
    /// Recibe un score de carga por ventana (4 s, stride 2 s) desde un
    /// <see cref="LoadSource"/>, lo clasifica en baja/óptima/alta con suavizado,
    /// banda muerta, persistencia y refractario, y mueve un escalar de dificultad
    /// d en [0,1] que se proyecta sobre los parámetros de la plataforma.
    ///
    /// La política es idéntica a scripts/adaptive_replay.py del repo AI-Cognitive
    /// (clase AdaptivePolicy). Si cambias una, cambia la otra.
    /// </summary>
    public class AdaptiveDifficultyController : MonoBehaviour
    {
        public static AdaptiveDifficultyController I { get; private set; }

        public enum Zone { Baja, Optima, Alta }

        [Header("Umbrales (escala del score ordinal, 0..3)")]
        [Tooltip("Por debajo: infracarga. Por encima de thetaHi: sobrecarga.")]
        public float thetaLo = 1.00f;
        public float thetaHi = 1.60f;
        [Tooltip("Banda muerta: salir de una zona exige rebasar el umbral por este margen.")]
        public float hysteresis = 0.12f;

        [Header("Dinámica")]
        [Range(0f, 0.95f)] public float emaLambda = 0.6f;
        [Tooltip("Ventanas consecutivas en la nueva zona antes de actuar (3 = 6 s).")]
        public int persistWindows = 3;
        public float refractorySec = 10f;
        [Tooltip("La bajada es mayor que la subida: aliviar rápido, exigir despacio.")]
        public float stepUp = 0.08f;
        public float stepDown = 0.15f;
        [Range(0f, 1f)] public float dInit = 0.5f;

        [Header("Puerta de desempeño")]
        [Tooltip("Ventana para contar errores recientes. Sin errores = 'va sobrado'.")]
        public float perfWindowSec = 20f;
        [Tooltip("Si está sobrecargado pero sin errores, sostener en vez de aliviar.")]
        public bool holdOnProductiveStruggle = true;

        [Header("Aplicación de parámetros")]
        [Tooltip("Continuos: se aplican al instante. Discretos (radios de diana, " +
                 "umbrales de puntuación): sólo en frontera de ejercicio, para no " +
                 "encoger una diana a media sutura.")]
        public bool applyContinuous = true;
        public float continuousLerpPerSec = 0.5f;

        [Header("Registro")]
        public bool logTrace = true;

        // --- estado de la política ---
        public float Difficulty { get; private set; }
        public Zone CurrentZone { get; private set; } = Zone.Optima;
        public float SmoothedScore { get; private set; }
        public int WindowsSeen { get; private set; }

        float _dTarget;
        bool _hasEma;
        Zone _candidate = Zone.Optima;
        int _candidateCount;
        float _lastActionTime = -1e9f;

        readonly List<string> _trace = new List<string>();
        readonly Queue<KeyValuePair<float, int>> _errorHistory =
            new Queue<KeyValuePair<float, int>>();

        // --- proyección d -> parámetros ---
        // Tres puntos por parámetro: más fácil (d=0), valor de serie (d=0.5) y
        // más difícil (d=1), interpolados por tramos. El valor de serie NO es el
        // punto medio del rango en 6 de los 8 parámetros, así que un lerp de dos
        // puntos desplazaría la plataforma en el ajuste neutro. Con esto,
        // d = 0.5 reproduce exactamente la configuración original.
        struct Knob
        {
            public float easy, ships, hard;
            public Knob(float e, float s, float h) { easy = e; ships = s; hard = h; }
        }

        static readonly Knob KMotionScale = new Knob(0.45f,  0.75f,  1.05f);
        static readonly Knob KHapticGain  = new Knob(1.40f,  1.00f,  0.60f);
        static readonly Knob KTenseThresh = new Knob(1.45f,  1.12f,  0.95f);
        static readonly Knob KPunctureRad = new Knob(0.040f, 0.028f, 0.018f);
        static readonly Knob KRingRad     = new Knob(0.060f, 0.045f, 0.032f);
        static readonly Knob KParTime     = new Knob(180f,   120f,   90f);
        static readonly Knob KParPath     = new Knob(18f,    12f,    9f);
        static readonly Knob KMaxErrors   = new Knob(3f,     1f,     0f);

        /// <summary>Umbrales calibrados de cada nivel, antes de escalarlos.</summary>
        readonly Dictionary<TrainingLevel, (float t, float p, int e)> _baseNivel =
            new Dictionary<TrainingLevel, (float t, float p, int e)>();

        static float Lerp(Knob k, float d)
        {
            d = Mathf.Clamp01(d);
            return d <= 0.5f
                ? Mathf.Lerp(k.easy, k.ships, d / 0.5f)
                : Mathf.Lerp(k.ships, k.hard, (d - 0.5f) / 0.5f);
        }

        void Awake()
        {
            I = this;
            Difficulty = _dTarget = dInit;
            SmoothedScore = 0f;
        }

        void OnDestroy() { if (I == this) I = null; }

        void Start()
        {
            // Los parámetros de frontera (radios de diana) no se pueden fijar
            // aquí: las dianas las crea TrainingLevel.Setup(), cuyo Start() puede
            // correr después que el nuestro. TrainingLevel llama a
            // ApplyBoundaryParameters() justo después de Setup().
            ApplyContinuousParameters(Difficulty);
        }

        void Update()
        {
            if (!applyContinuous) return;
            // acercamiento suave: un salto instantáneo en motionScale se siente
            // como una sacudida del instrumento.
            if (!Mathf.Approximately(Difficulty, _dTarget))
            {
                Difficulty = Mathf.MoveTowards(Difficulty, _dTarget,
                                               continuousLerpPerSec * Time.deltaTime);
                ApplyContinuousParameters(Difficulty);
            }
        }

        // -------------------------------------------------------------------
        // Entrada: una ventana de EEG
        // -------------------------------------------------------------------

        /// <summary>Alimenta un score de carga crudo. Llamar ~0.5 Hz.</summary>
        public void SubmitLoadScore(float raw)
        {
            WindowsSeen++;
            SmoothedScore = _hasEma
                ? emaLambda * SmoothedScore + (1f - emaLambda) * raw
                : raw;
            _hasEma = true;

            SampleErrorHistory();

            Zone z = ZoneOf(SmoothedScore, CurrentZone);

            if (z == _candidate) _candidateCount++;
            else { _candidate = z; _candidateCount = 1; }

            string action = "none";
            if (_candidateCount >= persistWindows && _candidate != CurrentZone)
            {
                CurrentZone = _candidate;
                if (Time.time - _lastActionTime >= refractorySec)
                {
                    bool? perfOk = PerformanceOk();

                    if (CurrentZone == Zone.Baja && perfOk != false)
                    {
                        _dTarget = Mathf.Min(1f, _dTarget + stepUp);
                        action = "up";
                    }
                    else if (CurrentZone == Zone.Alta)
                    {
                        if (holdOnProductiveStruggle && perfOk == true)
                        {
                            // sobrecargado pero sin errores: forcejeo productivo
                            action = "hold_productive";
                        }
                        else
                        {
                            _dTarget = Mathf.Max(0f, _dTarget - stepDown);
                            action = "down";
                        }
                    }

                    if (action != "none")
                    {
                        _lastActionTime = Time.time;
                        if (!applyContinuous) { Difficulty = _dTarget; ApplyContinuousParameters(Difficulty); }
                    }
                }
            }

            if (logTrace)
                _trace.Add(string.Format(CultureInfo.InvariantCulture,
                    "{0:0.00};{1:0.0000};{2:0.0000};{3};{4:0.000};{5}",
                    Time.time, raw, SmoothedScore, CurrentZone, _dTarget, action));
        }

        Zone ZoneOf(float x, Zone current)
        {
            float lo = thetaLo, hi = thetaHi;
            if (current == Zone.Baja) lo += hysteresis;        // salir de baja cuesta
            else if (current == Zone.Alta) hi -= hysteresis;   // salir de alta cuesta
            else { lo -= hysteresis; hi += hysteresis; }       // entrar a un extremo cuesta

            if (x < lo) return Zone.Baja;
            if (x > hi) return Zone.Alta;
            return Zone.Optima;
        }

        /// <summary>
        /// true = va sobrado (sin errores recientes), false = está fallando,
        /// null = no hay señal de desempeño. La ausencia de evidencia nunca se
        /// lee como buen desempeño.
        /// </summary>
        /// <summary>
        /// Muestrea el contador de errores una vez por ventana y descarta lo que
        /// cae fuera de perfWindowSec. Se llama desde SubmitLoadScore para que el
        /// historial exista cuando la política lo consulte.
        /// </summary>
        void SampleErrorHistory()
        {
            var m = MetricsRecorder.I;
            if (m == null || !m.Running) { _errorHistory.Clear(); return; }

            float now = Time.time;
            _errorHistory.Enqueue(new KeyValuePair<float, int>(now, m.ErrorCount));
            while (_errorHistory.Count > 0 && now - _errorHistory.Peek().Key > perfWindowSec)
                _errorHistory.Dequeue();
        }

        bool? PerformanceOk()
        {
            var m = MetricsRecorder.I;
            if (m == null || !m.Running) return null;
            if (_errorHistory.Count < 2) return null;   // aún sin historial suficiente

            int oldest = _errorHistory.Peek().Value;
            return m.ErrorCount == oldest;   // sin errores nuevos en la ventana
        }

        // -------------------------------------------------------------------
        // Proyección sobre la plataforma
        // -------------------------------------------------------------------

        /// <summary>Parámetros que pueden moverse a media ejecución.</summary>
        void ApplyContinuousParameters(float d)
        {
            float ms = Lerp(KMotionScale, d);
            foreach (var arm in FindObjectsByType<RoboticArm>(FindObjectsSortMode.None))
                arm.motionScale = ms;

            MasterInput.HapticGain = Lerp(KHapticGain, d);

            float tt = Lerp(KTenseThresh, d);
            foreach (var th in FindObjectsByType<ThreadBase>(FindObjectsSortMode.None))
                th.tenseThreshold = tt;
        }

        /// <summary>
        /// Parámetros que sólo cambian en frontera de ejercicio: la geometría de
        /// las dianas se construye una vez (RingTarget.Build) y encoger una diana
        /// visible a media maniobra desorienta. Los umbrales de puntuación se
        /// fijan aquí y se sostienen, para que la calificación siga siendo
        /// comparable entre intentos.
        /// </summary>
        public void ApplyBoundaryParameters()
        {
            float d = _dTarget;

            float pr = Lerp(KPunctureRad, d);
            foreach (var t in FindObjectsByType<PunctureTarget>(FindObjectsSortMode.None))
            {
                // La dificultad ajusta la TOLERANCIA, no el tamaño dibujado.
                // Antes rescalaba el marcador a radius*1.6 y con ello volvía a
                // inflarlo a 45 mm justo después de que el nivel lo creara.
                // El tope anatómico lo aplica PunctureTarget.EffectiveRadius.
                t.radius = pr;
                t.ApplyVisual();
            }

            float rr = Lerp(KRingRad, d);
            foreach (var r in FindObjectsByType<RingTarget>(FindObjectsSortMode.None))
                r.ringRadius = rr;

            // ---- MODIFICADO ----
            // Antes se ESCRIBÍAN los valores absolutos (120 s, 12 m, 1 error en
            // dificultad media) en todos los niveles, borrando su calibración:
            // N1 tenía 90 s, N3 210 s, N4 300 s. Ahora se escala la base de cada
            // nivel por la razón respecto al punto medio del knob.
            foreach (var lvl in FindObjectsByType<TrainingLevel>(FindObjectsSortMode.None))
            {
                if (!_baseNivel.TryGetValue(lvl, out var b))
                {
                    b = (lvl.parTimeSec, lvl.parPathMeters, lvl.maxErrors);
                    _baseNivel[lvl] = b;
                }
                lvl.parTimeSec = b.t * Lerp(KParTime, d) / KParTime.ships;
                lvl.parPathMeters = b.p * Lerp(KParPath, d) / KParPath.ships;
                lvl.maxErrors = Mathf.Max(0, Mathf.RoundToInt(b.e * Lerp(KMaxErrors, d) / KMaxErrors.ships));
            }
        }

        // -------------------------------------------------------------------

        /// <summary>Vuelca la traza del lazo junto a las métricas del intento.</summary>
        public string ExportTrace()
        {
            if (!logTrace || _trace.Count == 0) return null;
            try
            {
                string dir = Path.Combine(Application.persistentDataPath, "SutureTrainer");
                Directory.CreateDirectory(dir);
                string stamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string path = Path.Combine(dir, $"carga_adaptativa_{stamp}.csv");
                var rows = new List<string> { "t;score_crudo;score_suave;zona;dificultad;accion" };
                rows.AddRange(_trace);
                File.WriteAllLines(path, rows);
                Debug.Log($"[Adaptive] Traza del lazo exportada a: {path}");
                return path;
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[Adaptive] No se pudo exportar la traza: {ex.Message}");
                return null;
            }
        }
    }
}
