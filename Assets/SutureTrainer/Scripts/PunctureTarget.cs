using UnityEngine;

namespace SutureTrainer
{
    /// <summary>Diana de punción (entrada o salida) sobre el tejido.</summary>
    public class PunctureTarget : MonoBehaviour
    {
        public float radius = 0.028f;
        public bool isEntry;
        public int pairIndex;

        public enum State { Idle, Active, Done }
        public State CurrentState { get; private set; } = State.Idle;

        Renderer rend;
        Material idleMat, activeMat, doneMat;
        float pulse;
        Vector3 baseScale = Vector3.one;

        public void Init(Material idle, Material active, Material done)
        {
            idleMat = idle; activeMat = active; doneMat = done;
            rend = GetComponentInChildren<Renderer>();
            if (rend != null) baseScale = rend.transform.localScale;
            Apply();
        }

        public void SetState(State s) { CurrentState = s; Apply(); }

        void Apply()
        {
            if (rend == null) return;
            switch (CurrentState)
            {
                case State.Idle: rend.sharedMaterial = idleMat; break;
                case State.Active: rend.sharedMaterial = activeMat; break;
                case State.Done: rend.sharedMaterial = doneMat; break;
            }
        }

        void Update()
        {
            if (CurrentState == State.Active && rend != null)
            {
                pulse += Time.deltaTime * 4f;
                float s = 1f + Mathf.Sin(pulse) * 0.15f;
                rend.transform.localScale = baseScale * s;
            }
            else if (rend != null) rend.transform.localScale = baseScale;
        }

        public bool TipInside(Vector3 tipPos) =>
            Vector3.Distance(tipPos, transform.position) < radius;

        /// <summary>Distancia de la punta al centro (para métrica de precisión).</summary>
        public float Deviation(Vector3 tipPos) => Vector3.Distance(tipPos, transform.position);
    }
}
