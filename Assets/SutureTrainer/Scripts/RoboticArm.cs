using UnityEngine;
using UnityEngine.InputSystem;

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

        public enum ControlMode
        {
            Absoluto,   // el instrumento sigue la mano 1:1 (más intuitivo)
            Relativo    // deltas escalados con clutch (fiel a consola da Vinci)
        }

        [Header("Teleoperación")]
        public ControlMode mode = ControlMode.Absoluto;

        [Tooltip("Modo cuando esta mano está en el Touch háptico. Relativo por " +
                 "defecto: es lo que hace utilizable un volumen de trabajo de " +
                 "16 cm.")]
        public ControlMode touchMode = ControlMode.Relativo;
        [Range(0.1f, 1.5f)] public float motionScale = 0.75f;
        public Vector3 rotationOffsetEuler = new Vector3(50f, 0f, 0f);

        [Tooltip("Desfase de orientación cuando la mano usa el Touch. Ajústalo " +
                 "en Play si la herramienta no apunta como el stylus.")]
        // Los 180° en Y NO son un valor tanteado: TouchDevice convierte la
        // posición con diag(-1,1,-1) y la orientación con diag(1,1,-1). El
        // primero tiene determinante +1 (no cambia de mano: es un giro de 180°
        // sobre Y) y el segundo -1 (sí lo cambia), así que las dos
        // conversiones discrepan exactamente en ese giro. Con el gimbal
        // apagado no se notaba porque la orientación era fija; al encenderlo,
        // la herramienta sale del revés.
        //
        // Se corrige AQUÍ y no en TouchDevice porque tocar AxisSign cambiaría
        // el mapeo de posición, que está tanteado y funciona.
        public Vector3 touchRotationOffsetEuler = new Vector3(50f, 180f, 0f);
        public Vector3 workspaceCenter;
        public Vector3 workspaceExtents = new Vector3(0.45f, 0.30f, 0.35f);
        public float maxJawAngle = 28f;
        public float shaftRadius = 0.008f;

        [Header("Agarre con el robot")]
        [Tooltip("Asume mandíbulas SIEMPRE cerradas en modo Touch y agarra por " +
                 "proximidad. Solo para un stylus sin botones utilizables.")]
        // Apagado: el Touch sí tiene botones y ya están leídos. TouchDevice
        // saca _btn[0] como gatillo (mandíbulas) y _btn[1] como embrague, así
        // que master.Trigger funciona igual que con el mando. Dejarlo
        // encendido mantenía la pinza cerrada a la fuerza y hacía imposible
        // soltar y recolocar la aguja, que es media maniobra de suturar.
        public bool assumeGraspWithTouch = false;
        [Tooltip("Radio de agarre automático en modo Touch (m).")]
        public float autoGrabRadius = 0.05f;
        [Tooltip("Tecla para SOLTAR lo agarrado en modo Touch (teclado, Input System nuevo).")]
        public Key touchReleaseKey = Key.G;

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
            _modoPrevio = ModoActivo;
        }

        /// <summary>
        /// Modo de control según el aparato que lleva esta mano.
        ///
        /// El mando de Quest se mueve en toda la habitación, así que Absoluto
        /// (la muñeca EN la mano) es lo intuitivo. El Touch tiene un volumen de
        /// trabajo de ~16 cm, y con la actividad extendida 275 mm no llega:
        /// medido, haría falta escalar x2.5, que es justo lo que se descartó
        /// por dejar dianas de 14 mm en 5.6 mm de mano. Relativo rompe ese
        /// compromiso — se avanza por incrementos y se recoloca la mano con el
        /// embrague— así que alcance y precisión dejan de estar atados.
        /// </summary>
        ControlMode ModoActivo =>
            master != null && master.ActiveBackend == MasterInput.Backend.TouchHaptic
                ? touchMode
                : mode;

        ControlMode _modoPrevio;

        void Update()
        {
            if (master == null || wrist == null) return;

            // Al conmutar de aparato (F1) el modo cambia con él. Relativo
            // acumula sobre _target, así que hay que resembrarlo donde está la
            // muñeca AHORA; si no, el instrumento saltaría al objetivo viejo.
            var modo = ModoActivo;
            if (modo != _modoPrevio)
            {
                _modoPrevio = modo;
                _target = wrist.position;
                _hasMaster = false;
            }

            // --- posición objetivo ---
            if (master.IsTracked && !frozen)
            {
                if (modo == ControlMode.Absoluto)
                {
                    // la muñeca del instrumento está EN la mano, con un pequeño
                    // avance hacia delante como si sostuvieras la herramienta
                    _target = master.WorldPos + master.WorldRot * new Vector3(0f, 0f, 0.05f);
                }
                else
                {
                    if (!_hasMaster) { _lastMaster = master.WorldPos; _hasMaster = true; }
                    Vector3 delta = master.WorldPos - _lastMaster;
                    _lastMaster = master.WorldPos;
                    if (!master.Clutch)
                        _target += delta * motionScale;
                }
            }
            else { _hasMaster = false; }

            if (modo == ControlMode.Relativo)
                _target = ClampToWorkspace(_target); // en absoluto la mano manda

            // en absoluto: sin suavizado extra (el MasterInput ya filtra temblor)
            if (modo == ControlMode.Absoluto)
                wrist.position = _target;
            else
                wrist.position = Vector3.Lerp(wrist.position, _target, 1f - Mathf.Pow(0.001f, Time.deltaTime));

            // --- orientación de la muñeca (EndoWrist) ---
            if (master.IsTracked && !frozen)
            {
                Vector3 desfase =
                    master.ActiveBackend == MasterInput.Backend.TouchHaptic
                        ? touchRotationOffsetEuler
                        : rotationOffsetEuler;
                Quaternion goal = master.WorldRot * Quaternion.Euler(desfase);
                if (modo == ControlMode.Absoluto)
                    wrist.rotation = goal;
                else
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

        // Con el Touch (sin botones) asumimos las mandíbulas cerradas.
        bool TouchAutoGrasp =>
            assumeGraspWithTouch && master != null &&
            master.ActiveBackend == MasterInput.Backend.TouchHaptic;

        void UpdateJaws()
        {
            JawCloseness = frozen ? JawCloseness : (TouchAutoGrasp ? 1f : master.Trigger);
            float open = Mathf.Lerp(maxJawAngle, 0f, JawCloseness);
            if (jawA != null) jawA.localRotation = Quaternion.Euler(-open, 0f, 0f);
            if (jawB != null) jawB.localRotation = Quaternion.Euler(open, 0f, 0f);
        }

        void UpdateGrasp()
        {
            if (frozen) return;

            // Modo robot: agarra por proximidad de forma continua; suelta con tecla.
            if (TouchAutoGrasp)
            {
                if (Held == null) TryGrab(autoGrabRadius);
                else if (touchReleaseKey != Key.None && Keyboard.current != null &&
                         Keyboard.current[touchReleaseKey].wasPressedThisFrame)
                {
                    Held.Detach(); Held = null; master.Haptic(0.25f, 0.04f);
                }
                return;
            }

            // Modo mando: agarrar al cerrar, soltar al abrir.
            bool closed = JawCloseness > 0.72f;
            bool open = JawCloseness < 0.35f;
            if (closed && !_wasClosed && Held == null) TryGrab(0.035f);
            else if (open && Held != null)
            {
                Held.Detach(); Held = null; master.Haptic(0.25f, 0.04f);
            }
            if (closed) _wasClosed = true;
            else if (open) _wasClosed = false;
        }

        bool TryGrab(float radius)
        {
            Collider[] hits = Physics.OverlapSphere(TipPos, radius);
            Graspable best = null; float bestD = float.MaxValue;
            foreach (var h in hits)
            {
                var g = h.GetComponentInParent<Graspable>();
                if (g == null || g.IsHeld) continue;
                float d = Vector3.Distance(g.transform.position, TipPos);
                d = Mathf.Min(d, Vector3.Distance(h.ClosestPoint(TipPos), TipPos));
                if (d < bestD) { bestD = d; best = g; }
            }
            if (best != null)
            {
                best.AttachTo(jawAnchor, this);
                Held = best;
                master.Haptic(0.6f, 0.06f);
                AudioFX.Click();
                return true;
            }
            return false;
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
