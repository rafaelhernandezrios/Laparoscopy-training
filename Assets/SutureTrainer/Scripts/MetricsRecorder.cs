using System.Collections.Generic;
using UnityEngine;

namespace SutureTrainer
{
    /// <summary>Registra métricas de la sesión: tiempo, economía de movimiento, errores, precisión.</summary>
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

        float pathBaseL, pathBaseR;

        void Awake() { I = this; }
        void OnDestroy() { if (I == this) I = null; }

        public void StartRun()
        {
            Running = true;
            ElapsedTime = 0f;
            errors.Clear();
            precisionSamples.Clear();
            TensionEvents = 0; NeedleDrops = 0;
            pathBaseL = leftArm != null ? leftArm.PathLength : 0f;
            pathBaseR = rightArm != null ? rightArm.PathLength : 0f;
        }

        public void StopRun() => Running = false;

        void Update() { if (Running) ElapsedTime += Time.deltaTime; }

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
    }
}
