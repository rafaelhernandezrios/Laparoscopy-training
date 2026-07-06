using UnityEngine;

namespace SutureTrainer
{
    /// <summary>
    /// Aguja curva de sutura (media circunferencia). Genera su malla en Awake.
    /// Escala macro 5x: representa una aguja de ~26 mm.
    /// </summary>
    [RequireComponent(typeof(Graspable))]
    public class SutureNeedle : MonoBehaviour
    {
        public float arcRadius = 0.055f;   // radio del arco (macro)
        public float arcDegrees = 180f;
        public float tubeRadius = 0.0035f;
        public Material material;

        public Transform Tip { get; private set; }
        public Transform Tail { get; private set; }
        public Vector3 TipPos => Tip.position;
        public Vector3 TailPos => Tail.position;
        public Vector3 TipDir => Tip.forward;

        public Graspable Grasp { get; private set; }

        void Awake()
        {
            Grasp = GetComponent<Graspable>();

            var mesh = ProcMesh.ArcTube(arcRadius, arcDegrees, tubeRadius,
                out Vector3 tailP, out Vector3 tailT, out Vector3 tipP, out Vector3 tipT);

            var mf = gameObject.GetComponent<MeshFilter>();
            if (mf == null) mf = gameObject.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            var mr = gameObject.GetComponent<MeshRenderer>();
            if (mr == null) mr = gameObject.AddComponent<MeshRenderer>();
            if (material != null) mr.sharedMaterial = material;
            else if (MaterialSet.I != null) mr.sharedMaterial = MaterialSet.I.needle;

            Tip = new GameObject("Tip").transform;
            Tip.SetParent(transform, false);
            Tip.localPosition = tipP;
            Tip.localRotation = Quaternion.LookRotation(tipT, Vector3.forward);

            Tail = new GameObject("Tail").transform;
            Tail.SetParent(transform, false);
            Tail.localPosition = tailP;

            var box = gameObject.GetComponent<BoxCollider>();
            if (box == null) box = gameObject.AddComponent<BoxCollider>();
            box.center = mesh.bounds.center;
            box.size = mesh.bounds.size + Vector3.one * 0.004f;

            var rb = gameObject.GetComponent<Rigidbody>();
            if (rb == null) rb = gameObject.AddComponent<Rigidbody>();
            rb.mass = 0.02f;
            rb.isKinematic = true; // se activa la gravedad al soltar (Graspable)
            rb.interpolation = RigidbodyInterpolation.Interpolate;
        }
    }
}
