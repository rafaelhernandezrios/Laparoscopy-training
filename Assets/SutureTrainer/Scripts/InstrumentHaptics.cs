using UnityEngine;

namespace SutureTrainer
{
    /// <summary>
    /// Calcula la retroalimentación háptica de fuerza para un instrumento y la
    /// envía a su MasterInput (fuerza real en el Touch; vibración en Quest).
    ///
    /// Dos componentes, elegidas por el usuario:
    ///   1) Contacto con tejido: fuerza de penalización cuando la punta penetra
    ///      la superficie de la almohadilla (dirección = normal del tejido).
    ///      Se ablanda al pasar por una diana válida (punctureAllowed).
    ///   2) Restricción del trócar: fuerza de centrado que resiste el
    ///      desplazamiento lateral de la punta fuera del eje de inserción que
    ///      pasa por el trócar (efecto fulcro del puerto).
    ///
    /// Estable por diseño (muelle + amortiguación, con topes de fuerza). Todas
    /// las fuerzas se acotan al máximo del Touch (~3.3 N).
    /// </summary>
    public class InstrumentHaptics : MonoBehaviour
    {
        [Header("Referencias")]
        public RoboticArm arm;
        public MasterInput master;
        [Tooltip("Se autolocaliza en escena si queda vacío (el nivel crea el tejido).")]
        public TissuePatch tissue;

        [Header("Activación")]
        public bool enableTissue = true;
        public bool enableTrocar = true;

        [Header("Tejido (contacto/punción)")]
        [Tooltip("Rigidez del tejido: N por metro de penetración.")]
        public float tissueStiffness = 320f;
        public float tissueDamping = 4f;
        [Tooltip("Factor de fuerza mientras la punta pasa por una diana válida.")]
        [Range(0f, 1f)] public float puncturePassFactor = 0.3f;
        public float maxTissueForce = 2.8f;

        [Header("Realismo de piel")]
        [Tooltip("‘Pop’ de punción: la piel resiste y de golpe cede al atravesarla.")]
        public bool enablePuncturePop = true;
        [Tooltip("Profundidad a la que la piel se rompe y cede (m).")]
        public float punctureDepth = 0.006f;
        [Tooltip("Rigidez residual una vez dentro (0..1). Bajo = ‘pop’ más marcado.")]
        [Range(0.05f, 1f)] public float insideFactor = 0.4f;
        [Tooltip("Fricción/arrastre al deslizar sobre la piel (0..1 de la fuerza normal).")]
        [Range(0f, 1f)] public float surfaceFriction = 0.5f;
        [Tooltip("Micro-textura al deslizar (experimental; el suavizado puede atenuarla).")]
        public bool enableTexture = false;
        public float textureAmplitude = 0.15f;
        public float textureFreq = 22f;

        [Header("Trócar (fulcro)")]
        public float trocarStiffness = 50f;
        public float trocarDamping = 2f;
        [Tooltip("No aplica fuerza de trócar si la desviación lateral es menor a esto (m).")]
        public float trocarDeadzone = 0.015f;
        public float maxTrocarForce = 1.5f;

        [Header("Estabilidad del lazo de fuerza")]
        [Tooltip("Ganancia global de salida. Bájala si vibra, súbela si se siente flojo.")]
        [Range(0f, 1f)] public float outputGain = 0.3f;
        [Tooltip("Fuerzas por debajo de esto se ignoran (mata el ruido en reposo).")]
        public float forceDeadzone = 0.12f;
        [Tooltip("Suavizado paso-bajo (s). Más alto = más estable pero más 'blando'.")]
        public float forceSmoothTime = 0.05f;
        [Tooltip("Máxima variación de fuerza por segundo (N/s). Limita saltos bruscos.")]
        public float forceSlewPerSec = 25f;

        [Header("Global")]
        [Tooltip("Fuerza total máxima enviada al dispositivo (límite del Touch ~3.3 N).")]
        public float maxTotalForce = 2.5f;

        Vector3 _lastTip;
        bool _hasTip;
        float _nextTissueScan;
        Vector3 _fFilt;   // fuerza filtrada
        Vector3 _fSent;   // última fuerza enviada (para slew)
        bool _inSkin;     // la punta está dentro del tejido
        bool _punctured;  // ya atravesó la piel (para el ‘pop’)

        [Header("Diagnóstico")]
        [Tooltip("Escribe en consola tejido/penetración/fuerza para poder regular.")]
        public bool debugLog = true;
        float _nextDbg;
        float _lastPen;   // penetración registrada (m)

        void Reset()
        {
            arm = GetComponent<RoboticArm>();
            if (arm != null) master = arm.master;
        }

        void Awake()
        {
            if (arm == null) arm = GetComponent<RoboticArm>();
            if (master == null && arm != null) master = arm.master;
        }

        void FixedUpdate()
        {
            if (arm == null || master == null) return;
            // En Quest la "fuerza" es solo vibración; permite desactivarla.
            if (master.ActiveBackend != MasterInput.Backend.TouchHaptic && !SendToQuestToo)
                return;

            float dt = Mathf.Max(1e-4f, Time.fixedDeltaTime);
            Vector3 tip = arm.TipPos;
            Vector3 vel = _hasTip ? (tip - _lastTip) / dt : Vector3.zero;

            Vector3 fT = enableTissue ? TissueForce(tip, vel) : Vector3.zero;
            Vector3 fR = enableTrocar ? TrocarForce(tip, vel) : Vector3.zero;
            Vector3 force = fT + fR;

            // ganancia de seguridad
            force *= Mathf.Clamp01(outputGain);

            // zona muerta: nada de fuerza si es diminuta (evita zumbido en reposo)
            if (force.magnitude < forceDeadzone) force = Vector3.zero;

            // filtro paso-bajo: elimina la oscilación de alta frecuencia (el "vibrar")
            float a = 1f - Mathf.Exp(-dt / Mathf.Max(1e-3f, forceSmoothTime));
            _fFilt = Vector3.Lerp(_fFilt, force, a);

            // límite de variación (slew) para no dar saltos bruscos de fuerza
            Vector3 dF = _fFilt - _fSent;
            float maxStep = forceSlewPerSec * dt;
            if (dF.magnitude > maxStep) dF = dF.normalized * maxStep;
            _fSent += dF;

            _fSent = Vector3.ClampMagnitude(_fSent, maxTotalForce);
            master.SendForce(_fSent);

            if (debugLog && Time.time >= _nextDbg)
            {
                _nextDbg = Time.time + 0.4f;
                Debug.Log($"[Haptics] tejido={(tissue != null)} enPiel={_inSkin} " +
                          $"pen={_lastPen * 1000f:F1}mm  |fTejido|={fT.magnitude:F2}N " +
                          $"|fTrocar|={fR.magnitude:F2}N  gain={outputGain:F2}  |enviado|={_fSent.magnitude:F2}N");
            }

            _lastTip = tip; _hasTip = true;
        }

        [Tooltip("Enviar también la fuerza como vibración cuando la mano usa el mando Quest.")]
        public bool SendToQuestToo = true;

        Vector3 TissueForce(Vector3 tip, Vector3 vel)
        {
            if (tissue == null)
            {
                if (Time.time >= _nextTissueScan)
                {
                    _nextTissueScan = Time.time + 0.5f;
                    tissue = FindFirstObjectByType<TissuePatch>();
                }
                if (tissue == null) return Vector3.zero;
            }

            // superficie como plano: punto = centro de la herida, normal = up local
            Vector3 n = tissue.transform.up;
            Vector3 rel = tip - tissue.WoundCenter;
            float signed = Vector3.Dot(rel, n);      // >0 por encima, <0 penetrando

            // fuera del tejido: reinicia el estado de la piel
            if (signed >= 0f) { _inSkin = false; _punctured = false; _lastPen = -signed; return Vector3.zero; }

            // ¿está sobre la almohadilla?
            Vector3 local = tissue.transform.InverseTransformPoint(tip);
            bool over = Mathf.Abs(local.x) < tissue.width * 0.5f &&
                        Mathf.Abs(local.z) < tissue.depth * 0.5f;
            if (!over) { _inSkin = false; _punctured = false; _lastPen = 0f; return Vector3.zero; }

            float pen = -signed;                     // profundidad de penetración (m)
            _lastPen = pen;
            float vn = Vector3.Dot(vel, n);          // velocidad a lo largo de la normal

            // --- rigidez normal, con ‘pop’ de punción ---
            float stiff = tissueStiffness;
            if (enablePuncturePop)
            {
                if (!_punctured && pen >= punctureDepth) _punctured = true; // rompe la piel
                if (_punctured) stiff *= insideFactor;                      // ya dentro: cede
            }
            float mag = Mathf.Max(0f, pen * stiff - vn * tissueDamping);
            if (tissue.punctureAllowed) mag *= puncturePassFactor;
            mag = Mathf.Min(mag, maxTissueForce);
            Vector3 f = n * mag;

            // --- fricción de superficie: arrastre al deslizar sobre la piel ---
            Vector3 vTang = vel - Vector3.Project(vel, n);
            float vt = vTang.magnitude;
            if (surfaceFriction > 0f && vt > 1e-5f)
            {
                float ramp = Mathf.Clamp01(vt / 0.02f);   // se desvanece cerca de v=0 (evita chattering)
                f += -(vTang / vt) * (surfaceFriction * mag * ramp);
            }

            // --- micro-textura opcional al deslizar ---
            if (enableTexture)
            {
                float rough = textureAmplitude * Mathf.Clamp01(vTang.magnitude * 15f);
                f += n * (rough * Mathf.Sin(Time.time * textureFreq * 6.2831853f));
            }

            _inSkin = true;
            return f;
        }

        Vector3 TrocarForce(Vector3 tip, Vector3 vel)
        {
            if (arm.trocar == null) return Vector3.zero;

            Vector3 o = arm.trocar.position;
            // eje de inserción: del trócar hacia el centro del espacio de trabajo
            Vector3 axis = arm.workspaceCenter - o;
            if (axis.sqrMagnitude < 1e-6f) axis = arm.trocar.up;
            axis.Normalize();

            Vector3 rel = tip - o;
            Vector3 lateral = rel - Vector3.Project(rel, axis);   // desviación perpendicular al eje
            Vector3 latVel = vel - Vector3.Project(vel, axis);

            // zona muerta: cerca del eje no hay fuerza (evita empuje/zumbido constante)
            float d = lateral.magnitude;
            if (d < trocarDeadzone) return Vector3.zero;
            lateral *= (d - trocarDeadzone) / d;   // fuerza proporcional al exceso

            Vector3 f = -lateral * trocarStiffness - latVel * trocarDamping;
            return Vector3.ClampMagnitude(f, maxTrocarForce);
        }
    }
}
