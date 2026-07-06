using UnityEngine;

namespace SutureTrainer
{
    /// <summary>
    /// Brazo esclavo tipo da Vinci: la punta sigue el mando maestro con
    /// escalado de movimiento y clutch. El eje pasa siempre por el trócar
    /// (pivote fijo). Gatillo = cierre de mandíbulas; agarre de Graspables.
    /// </summary>
    public class RoboticArm : MonoBehaviour
    {
        [Header("Referencias (asignadas por SceneBuilder)")]
        public MasterInput master;
        public Transform trocar;      // pivote fijo del eje
        public Transform shaft;       // cilindro visual del eje
        public Transform wrist;       // muñeca (posición objetivo)
        public Transform jawA;
        public Transform jawB;
        public Transform jawAnchor;   // punto de agarre / punta

        [Header("Teleoperación")]
        [Range(0.1f, 1.5f)] public float motionScale = 0.75f;
        public Vector3 rotationOffsetEuler = new Vector3(50f, 0f, 0f);
        public Vector3 workspaceCenter;
        public Vector3 workspaceExtents = new Vector3(0.45f, 0.30f, 0.35f);
        public float maxJawAngle = 28f;
        public float shaftRadius = 0.008f;

        [HideInInspector] public bool frozen;

        public Graspable Held { get; private set; }
        public Vector3 TipPos => jawAnchor != null ? jawAnchor.position : wrist.position;
        public float JawCloseness { get; private set; } // 0 abierto, 1 cerrado
        public float PathLength { get; private set; }

        Vector3 _target;
        Vector3 _lastMaster;
        bool _hasMaster;
        bool _wasClosed;
        Vector3 _lastTip;
        bool _hasTip;

        void Start()
        {
            _target = wrist != null ? wrist.position : transform.position;
            if (workspaceCenter == Vector3.zero) workspaceCenter = _target;
        }

        void Update()
        {
            if (master == null || wrist == null) return;

            // --- posición objetivo (delta del maestro, con clutch) ---
            if (master.IsTracked && !frozen)
            {
                if (!_hasMaster) { _lastMaster = master.WorldPos; _hasMaster = true; }
                Vector3 delta = master.WorldPos - _lastMaster;
                _lastMaster = master.WorldPos;
                if (!master.Clutch)
                    _target += delta * motionScale;
            }
            else _hasMaster = false;

            _target = ClampToWorkspace(_target);
            wrist.position = Vector3.Lerp(wrist.position, _target, 1f - Mathf.Pow(0.001f, Time.deltaTime));

            // --- orientación de la muñeca (EndoWrist) ---
            if (master.IsTracked && !frozen)
            {
                Quaternion goal = master.WorldRot * Quaternion.Euler(rotationOffsetEuler);
                wrist.rotation = Quaternion.Slerp(wrist.rotation, goal, 1f - Mathf.Pow(0.0005f, Time.deltaTime));
            }

            UpdateShaft();
            UpdateJaws();
            UpdateGrasp();
            AccumulatePath();
        }

        Vector3 ClampToWorkspace(Vector3 p)
        {
            Vector3 d = p - workspaceCenter;
            d.x = Mathf.Clamp(d.x, -workspaceExtents.x, workspaceExtents.x);
            d.y = Mathf.Clamp(d.y, -workspaceExtents.y, workspaceExtents.y);
            d.z = Mathf.Clamp(d.z, -workspaceExtents.z, workspaceExtents.z);
            return workspaceCenter + d;
        }

        void UpdateShaft()
        {
            if (shaft == null || trocar == null) return;
            Vector3 a = trocar.position, b = wrist.position;
            Vector3 mid = (a + b) * 0.5f;
            float len = Vector3.Distance(a, b);
            shaft.position = mid;
            if (len > 1e-4f) shaft.up = (b - a).normalized;
            shaft.localScale = new Vector3(shaftRadius * 2f, len * 0.5f, shaftRadius * 2f);
        }

        void UpdateJaws()
        {
            JawCloseness = frozen ? JawCloseness : master.Trigger;
            float open = Mathf.Lerp(maxJawAngle, 0f, JawCloseness);
            if (jawA != null) jawA.localRotation = Quaternion.Euler(-open, 0f, 0f);
            if (jawB != null) jawB.localRotation = Quaternion.Euler(open, 0f, 0f);
        }

        void UpdateGrasp()
        {
            if (frozen) return;
            bool closed = JawCloseness > 0.72f;
            bool open = JawCloseness < 0.35f;

            if (closed && !_wasClosed && Held == null)
            {
                Collider[] hits = Physics.OverlapSphere(TipPos, 0.035f);
                Graspable best = null; float bestD = float.MaxValue;
                foreach (var h in hits)
                {
                    var g = h.GetComponentInParent<Graspable>();
                    if (g == null || g.IsHeld) continue;
                    float d = Vector3.Distance(g.transform.position, TipPos);
                    // punto más cercano del collider cuenta también
                    d = Mathf.Min(d, Vector3.Distance(h.ClosestPoint(TipPos), TipPos));
                    if (d < bestD) { bestD = d; best = g; }
                }
                if (best != null)
                {
                    best.AttachTo(jawAnchor, this);
                    Held = best;
                    master.Haptic(0.6f, 0.06f);
                }
            }
            else if (open && Held != null)
            {
                Held.Detach();
                Held = null;
                master.Haptic(0.25f, 0.04f);
            }
            if (closed) _wasClosed = true;
            else if (open) _wasClosed = false;
        }

        void AccumulatePath()
        {
            if (!_hasTip) { _lastTip = TipPos; _hasTip = true; return; }
            float d = Vector3.Distance(TipPos, _lastTip);
            if (d > 0.0005f && d < 0.2f) PathLength += d;
            _lastTip = TipPos;
        }

        public void ForceRelease()
        {
            if (Held != null) { Held.Detach(); Held = null; }
        }
    }
}
