using System.Collections.Generic;
using UnityEngine;

namespace SutureTrainer
{
    /// <summary>
    /// Hilo de sutura simulado con integración Verlet + restricciones de distancia.
    /// Anclado por un extremo a la cola (swage) de la aguja. Soporta "pines"
    /// (puntos de punción en el tejido), deslizamiento del hilo a través de los
    /// pines al tirar, métrica de tensión y cincha de nudo.
    /// </summary>
    public class VerletThread : MonoBehaviour
    {
        public Transform anchor;            // cola de la aguja
        public int particleCount = 70;
        public float totalLength = 1.1f;    // macro (~22 cm reales a 5x)
        public int constraintIterations = 5;
        public float damping = 0.985f;
        public float gravity = 2.5f;
        public Material normalMat;
        public Material tenseMat;
        public float lineWidth = 0.006f;

        [HideInInspector] public Transform tailHolder; // si la cola está agarrada

        public float Tension { get; private set; }         // ratio de estiramiento 0..n
        public bool IsTense => Tension > tenseThreshold;
        public float tenseThreshold = 1.12f;
        public int RemainingTailSegments => pins.Count > 0 ? (particleCount - 1 - pins[pins.Count - 1].index) : particleCount - 1;

        public System.Action onTensionEvent;

        class Pin { public int index; public Vector3 pos; }
        readonly List<Pin> pins = new List<Pin>();

        Vector3[] p, pPrev;
        float segLen;
        LineRenderer lr;
        float lastTensionEvent = -10f;
        bool tenseVisual;

        void Awake()
        {
            segLen = totalLength / (particleCount - 1);
            p = new Vector3[particleCount];
            pPrev = new Vector3[particleCount];
            lr = gameObject.GetComponent<LineRenderer>();
            if (lr == null) lr = gameObject.AddComponent<LineRenderer>();
            lr.positionCount = particleCount;
            lr.startWidth = lineWidth; lr.endWidth = lineWidth;
            lr.numCapVertices = 2; lr.numCornerVertices = 2;
            if (normalMat == null && MaterialSet.I != null) normalMat = MaterialSet.I.thread;
            if (tenseMat == null && MaterialSet.I != null) tenseMat = MaterialSet.I.threadTense;
            if (normalMat != null) lr.sharedMaterial = normalMat;
        }

        void Start()
        {
            Vector3 a = anchor != null ? anchor.position : transform.position;
            for (int i = 0; i < particleCount; i++)
            {
                p[i] = a + Vector3.down * (segLen * i * 0.5f) + Random.insideUnitSphere * 0.002f;
                pPrev[i] = p[i];
            }
        }

        void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            // integración
            for (int i = 0; i < particleCount; i++)
            {
                Vector3 vel = (p[i] - pPrev[i]) * damping;
                pPrev[i] = p[i];
                p[i] += vel + Vector3.down * (gravity * dt * dt);
            }
            // restricciones
            for (int it = 0; it < constraintIterations; it++)
            {
                ApplyAnchors();
                for (int i = 0; i < particleCount - 1; i++)
                {
                    Vector3 d = p[i + 1] - p[i];
                    float dist = d.magnitude;
                    if (dist < 1e-6f) continue;
                    float diff = (dist - segLen) / dist;
                    bool i0Fixed = IsFixed(i);
                    bool i1Fixed = IsFixed(i + 1);
                    if (i0Fixed && i1Fixed) continue;
                    if (i0Fixed) p[i + 1] -= d * diff;
                    else if (i1Fixed) p[i] += d * diff;
                    else { p[i] += d * (diff * 0.5f); p[i + 1] -= d * (diff * 0.5f); }
                }
                ApplyAnchors();
            }
            ComputeTension();
            SlideThroughPins();
        }

        void ApplyAnchors()
        {
            if (anchor != null) p[0] = anchor.position;
            foreach (var pin in pins) p[pin.index] = pin.pos;
            if (tailHolder != null) p[particleCount - 1] = tailHolder.position;
        }

        bool IsFixed(int i)
        {
            if (i == 0 && anchor != null) return true;
            if (i == particleCount - 1 && tailHolder != null) return true;
            for (int k = 0; k < pins.Count; k++) if (pins[k].index == i) return true;
            return false;
        }

        void ComputeTension()
        {
            // estiramiento del tramo entre el ancla y el primer pin (o cola sujeta)
            int end = pins.Count > 0 ? pins[0].index : (tailHolder != null ? particleCount - 1 : -1);
            if (end <= 0) { Tension = 0f; SetTenseVisual(false); return; }
            Vector3 a = p[0], b = p[end];
            float direct = Vector3.Distance(a, b);
            float rest = segLen * end;
            Tension = direct / Mathf.Max(rest, 1e-4f);
            bool tense = Tension > tenseThreshold;
            SetTenseVisual(tense);
            if (tense && Time.time - lastTensionEvent > 1.2f)
            {
                lastTensionEvent = Time.time;
                onTensionEvent?.Invoke();
            }
        }

        void SetTenseVisual(bool tense)
        {
            if (tense == tenseVisual) return;
            tenseVisual = tense;
            if (lr != null && normalMat != null && tenseMat != null)
                lr.sharedMaterial = tense ? tenseMat : normalMat;
        }

        /// <summary>El hilo desliza por el último par de pines al tirar (pull-through).</summary>
        void SlideThroughPins()
        {
            if (pins.Count < 2 || Tension < 1.05f) return;
            var last = pins[pins.Count - 1];
            var prev = pins[pins.Count - 2];
            if (last.index < particleCount - 4)
            {
                prev.index++;
                last.index++;
            }
        }

        void LateUpdate()
        {
            for (int i = 0; i < particleCount; i++) lr.SetPosition(i, p[i]);
        }

        /// <summary>Fija el hilo pasando por los orificios de entrada y salida.</summary>
        public void PinThroughStitch(Vector3 exitHole, Vector3 entryHole)
        {
            int i1 = NearestFreeParticle(exitHole);
            int i2 = Mathf.Min(i1 + 3, particleCount - 2);
            pins.Add(new Pin { index = i1, pos = exitHole });
            pins.Add(new Pin { index = i2, pos = entryHole });
        }

        int NearestFreeParticle(Vector3 pos)
        {
            int minIdx = 2;
            foreach (var pin in pins) minIdx = Mathf.Max(minIdx, pin.index + 2);
            int best = Mathf.Min(minIdx, particleCount - 5);
            float bestD = float.MaxValue;
            for (int i = minIdx; i < particleCount - 4; i++)
            {
                float d = Vector3.SqrMagnitude(p[i] - pos);
                if (d < bestD) { bestD = d; best = i; }
            }
            return best;
        }

        /// <summary>Colapsa los pines del último punto hacia un punto (cinchar).</summary>
        public void CinchTo(Vector3 point)
        {
            int start = Mathf.Max(0, pins.Count - 2); // solo el último par
            for (int i = start; i < pins.Count; i++)
                pins[i].pos = point + Random.insideUnitSphere * 0.004f;
        }

        public void ClearPins() => pins.Clear();
        public bool HasPins => pins.Count > 0;
        public Vector3 TailParticlePos => p != null ? p[particleCount - 1] : transform.position;
        public Vector3 ParticleAt(float t01) => p[Mathf.Clamp(Mathf.RoundToInt(t01 * (particleCount - 1)), 0, particleCount - 1)];
    }
}
