using UnityEngine;

namespace SutureTrainer
{
    /// <summary>
    /// Anillo flotante: la punta de la aguja debe atravesarlo por el centro.
    /// Detecta el cruce del plano del anillo dentro del radio interior.
    /// </summary>
    public class RingTarget : MonoBehaviour
    {
        public float ringRadius = 0.045f;
        public float tubeRadius = 0.006f;
        public bool IsDone { get; private set; }
        public bool IsActive { get; set; }
        public float LastDeviation { get; private set; }

        public System.Action<RingTarget, float> onPassed;

        Renderer rend;
        Material matIdle, matActive, matDone;
        float lastSide;
        bool hasSide;

        public void Build(Material idle, Material active, Material done)
        {
            matIdle = idle; matActive = active; matDone = done;
            var mf = gameObject.AddComponent<MeshFilter>();
            mf.sharedMesh = ProcMesh.Torus(ringRadius, tubeRadius);
            rend = gameObject.AddComponent<MeshRenderer>();
            rend.sharedMaterial = idle;
        }

        void Update()
        {
            if (rend == null) return;
            if (IsDone) { rend.sharedMaterial = matDone; return; }
            rend.sharedMaterial = IsActive ? matActive : matIdle;
        }

        /// <summary>Llamar cada frame con la posición de la punta de la aguja.</summary>
        public void CheckTip(Vector3 tipPos)
        {
            if (IsDone || !IsActive) { hasSide = false; return; }
            Vector3 local = transform.InverseTransformPoint(tipPos);
            float side = local.z;
            float radial = new Vector2(local.x, local.y).magnitude;
            bool near = Mathf.Abs(side) < 0.06f && radial < ringRadius * 1.6f;

            if (!near) { hasSide = false; return; }
            if (hasSide && Mathf.Sign(side) != Mathf.Sign(lastSide) && radial < ringRadius - tubeRadius)
            {
                IsDone = true;
                LastDeviation = radial;
                onPassed?.Invoke(this, radial);
            }
            lastSide = side;
            hasSide = true;
        }
    }
}
