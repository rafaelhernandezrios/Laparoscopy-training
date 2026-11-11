using UnityEngine;

namespace SutureTrainer
{
    /// <summary>Diana de punción (entrada o salida) sobre el tejido.</summary>
    public class PunctureTarget : MonoBehaviour
    {
        public float radius = 0.028f;

        [Tooltip("Diámetro VISIBLE del marcador, en metros. Independiente de " +
                 "radius: el radio es tolerancia de captura, no tamaño.")]
        public float visualDiameter = 0.006f;

        [Tooltip("Tope de tolerancia impuesto por la anatomía. Con el órgano " +
                 "real las dianas quedan a ~24 mm entre sí, así que un radio " +
                 "de 28 mm hacía que tocar una activara la de al lado. 0 = sin tope.")]
        public float maxRadius = 0f;

        /// <summary>Radio efectivo, respetando el tope anatómico.</summary>
        public float EffectiveRadius => maxRadius > 0f ? Mathf.Min(radius, maxRadius) : radius;

        /// <summary>Redibuja el marcador con su tamaño visible.</summary>
        public void ApplyVisual()
        {
            var marker = transform.Find("Marker");
            if (marker != null)
                marker.localScale = new Vector3(visualDiameter, 0.004f, visualDiameter);
        }
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

        public void SetState(State s)
        {
            bool punzada = s == State.Done && CurrentState != State.Done;
            CurrentState = s;
            Apply();
            if (punzada)
            {
                EventMarkers.Send($"tarea/puncion|{(isEntry ? "entrada" : "salida")}|par={pairIndex}");
                DistractorManager.Punzada(transform.position);
            }
        }

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
            Vector3.Distance(tipPos, transform.position) < EffectiveRadius;

        /// <summary>Distancia de la punta al centro (para métrica de precisión).</summary>
        public float Deviation(Vector3 tipPos) => Vector3.Distance(tipPos, transform.position);
    }
}
