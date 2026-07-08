using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SutureTrainer
{
    /// <summary>
    /// Registra métricas de la sesión: tiempo, economía de movimiento, errores,
    /// precisión y trayectorias (20 Hz). Exporta CSV a persistentDataPath.
    /// </summary>
    public class MetricsRecorder : MonoBehaviour
    {
        public static MetricsRecorder I { get; private set; }

        public RoboticArm leftArm, rightArm;

        public bool Running { get; private set; }
        public float ElapsedTime { get; private set; }
        public int ErrorCount => errors.Count;
        public int TensionEvents { get; private set; }
        public int NeedleDrops { get; private set; }

        public readonly List<string> errors = new List<string>();
        readonly List<float> precisionSamples = new List<float>();
        readonly List<string> trajRows = new List<string>();

        float pathBaseL, pathBaseR;
        float sampleTimer;
        const float SamplePeriod = 0.05f; // 20 Hz

        void Awake() { I = this; }
        void OnDestroy() { if (I == this) I = null; }

        public void StartRun()
        {
            Running = true;
            ElapsedTime = 0f;
            errors.Clear();
            precisionSamples.Clear();
            trajRows.Clear();
            trajRows.Add("t;izq_x;izq_y;izq_z;der_x;der_y;der_z;pinza_izq;pinza_der");
            sampleTimer = 0f;
            TensionEvents = 0; NeedleDrops = 0;
            pathBaseL = leftArm != null ? leftArm.PathLength : 0f;
            pathBaseR = rightArm != null ? rightArm.PathLength : 0f;
        }

        public void StopRun() => Running = false;

        void Update()
        {
            if (!Running) return;
            ElapsedTime += Time.deltaTime;

            sampleTimer += Time.deltaTime;
            if (sampleTimer >= SamplePeriod)
            {
                sampleTimer = 0f;
                Vector3 l = leftArm != null ? leftArm.TipPos : Vector3.zero;
                Vector3 r = rightArm != null ? rightArm.TipPos : Vector3.zero;
                float jl = leftArm != null ? leftArm.JawCloseness : 0f;
                float jr = rightArm != null ? rightArm.JawCloseness : 0f;
                trajRows.Add(string.Format(CultureInfo.InvariantCulture,
                    "{0:0.000};{1:0.0000};{2:0.0000};{3:0.0000};{4:0.0000};{5:0.0000};{6:0.0000};{7:0.00};{8:0.00}",
                    ElapsedTime, l.x, l.y, l.z, r.x, r.y, r.z, jl, jr));
            }
        }

        public float TotalPathLength =>
            (leftArm != null ? leftArm.PathLength - pathBaseL : 0f) +
            (rightArm != null ? rightArm.PathLength - pathBaseR : 0f);

        public void AddError(string description)
        {
            if (!Running) return;
            errors.Add($"[{ElapsedTime:0.0}s] {description}");
        }

        public void AddTensionEvent() { if (Running) { TensionEvents++; AddError("Tensión excesiva del hilo"); } }
        public void AddNeedleDrop() { if (Running) { NeedleDrops++; AddError("Aguja soltada / caída"); } }
        public void AddPrecisionSample(float deviationMeters) { if (Running) precisionSamples.Add(deviationMeters); }

        public float AvgPrecision
        {
            get
            {
                if (precisionSamples.Count == 0) return 0f;
                float s = 0f; foreach (var v in precisionSamples) s += v;
                return s / precisionSamples.Count;
            }
        }

        public struct Score
        {
            public int stars;         // 1..3
            public float time;
            public float path;
            public int errorCount;
            public float avgPrecisionMm;
        }

        public Score Evaluate(float parTime, float parPath, int maxErrors3Stars)
        {
            var s = new Score
            {
                time = ElapsedTime,
                path = TotalPathLength,
                errorCount = ErrorCount,
                avgPrecisionMm = AvgPrecision * 1000f
            };
            int stars = 3;
            if (ElapsedTime > parTime || TotalPathLength > parPath) stars--;
            if (ErrorCount > maxErrors3Stars) stars--;
            if (ElapsedTime > parTime * 1.8f || ErrorCount > maxErrors3Stars * 3 + 2) stars = 1;
            s.stars = Mathf.Clamp(stars, 1, 3);
            return s;
        }

        /// <summary>
        /// Exporta el intento a CSV. Devuelve la carpeta de destino.
        /// Archivos: resumen.csv (acumulativo), trayectoria_*.csv, errores_*.csv.
        /// </summary>
        public string Export(Score score)
        {
            try
            {
                string dir = Path.Combine(Application.persistentDataPath, "SutureTrainer");
                Directory.CreateDirectory(dir);
                string scene = SceneManager.GetActiveScene().name;
                string stamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");

                // resumen acumulativo
                string summaryPath = Path.Combine(dir, "resumen.csv");
                if (!File.Exists(summaryPath))
                    File.WriteAllText(summaryPath,
                        "fecha;escena;duracion_s;recorrido_m;errores;eventos_tension;agujas_caidas;precision_mm;estrellas\n");
                File.AppendAllText(summaryPath, string.Format(CultureInfo.InvariantCulture,
                    "{0};{1};{2:0.0};{3:0.00};{4};{5};{6};{7:0.0};{8}\n",
                    System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), scene,
                    score.time, score.path, score.errorCount, TensionEvents,
                    NeedleDrops, score.avgPrecisionMm, score.stars));

                // trayectoria del intento
                File.WriteAllLines(Path.Combine(dir, $"trayectoria_{scene}_{stamp}.csv"), trajRows);

                // errores con timestamp
                if (errors.Count > 0)
                {
                    var sb = new StringBuilder("error\n");
                    foreach (var e in errors) sb.AppendLine(e.Replace(';', ','));
                    File.WriteAllText(Path.Combine(dir, $"errores_{scene}_{stamp}.csv"), sb.ToString());
                }

                Debug.Log($"[SutureTrainer] Métricas exportadas a: {dir}");
                return dir;
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[SutureTrainer] No se pudieron exportar métricas: {ex.Message}");
                return null;
            }
        }
    }
}
