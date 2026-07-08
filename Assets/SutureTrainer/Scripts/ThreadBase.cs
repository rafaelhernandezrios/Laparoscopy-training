using UnityEngine;

namespace SutureTrainer
{
    /// <summary>
    /// Contrato del hilo de sutura. Los niveles solo dependen de esta clase,
    /// de modo que la implementación (Verlet propia, Obi Rope, etc.) es
    /// intercambiable vía TrainingLevel.ThreadFactory.
    /// </summary>
    public abstract class ThreadBase : MonoBehaviour
    {
        [Tooltip("Extremo fijado a la cola (swage) de la aguja.")]
        public Transform anchor;

        [HideInInspector] public Transform tailHolder; // pinza sujetando la cola

        public System.Action onTensionEvent;

        /// <summary>Ratio de estiramiento (1 = reposo).</summary>
        public abstract float Tension { get; }
        public abstract bool HasPins { get; }
        /// <summary>Segmentos de hilo restantes tras el último pin (cola).</summary>
        public abstract int RemainingTailSegments { get; }
        public abstract Vector3 TailParticlePos { get; }
        public abstract Vector3 ParticleAt(float t01);

        public abstract void PinThroughStitch(Vector3 exitHole, Vector3 entryHole);
        public abstract void CinchTo(Vector3 point);
        public abstract void ClearPins();
    }
}
