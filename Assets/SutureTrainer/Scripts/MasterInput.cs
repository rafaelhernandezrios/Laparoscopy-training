using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;

namespace SutureTrainer
{
    /// <summary>
    /// Lee un controlador del Quest (mando maestro de la consola).
    /// Expone pose filtrada en espacio de mundo, gatillo (mandíbulas),
    /// botón primario (clutch) y hápticos.
    /// Usa Input System (compatible con XR Device Simulator en editor)
    /// y cae al subsistema XR legacy en dispositivo real.
    /// </summary>
    public class MasterInput : MonoBehaviour
    {
        public enum Backend
        {
            QuestController, // mando de VR (comportamiento original)
            TouchHaptic      // 3D Systems Touch vía OpenHaptics (fuerza real)
        }

        [Header("Fuente de entrada")]
        [Tooltip("Backend por defecto de esta mano. Se puede conmutar en runtime con toggleKey o SetBackend().")]
        public Backend backend = Backend.QuestController;
        [Tooltip("Tecla para conmutar Quest <-> Touch en el editor/PC (opcional). Usa el Input System nuevo.")]
        public Key toggleKey = Key.None;

        public XRNode node = XRNode.RightHand;
        [Tooltip("Transform del Camera Offset del XR Origin (espacio de tracking).")]
        public Transform trackingSpace;
        [Range(0.05f, 1f)] public float smoothing = 0.35f;

        [Header("Calibración Touch (dispositivo -> mundo)")]
        [Tooltip("Al entrar en modo Touch, dejar las mandíbulas SOBRE la aguja " +
                 "en vez de en touchHome. Tiene prioridad sobre touchHome; si " +
                 "no hay aguja en la escena, se usa touchHome igualmente.")]
        public bool touchAnchorOnNeedle;

        [Tooltip("Nombre del aparato en OpenHaptics. Con un solo robot es " +
                 "\"Default Device\"; con dos, los que haya configurado Touch " +
                 "Setup, normalmente \"Right Device\" y \"Left Device\".")]
        public string touchDeviceName = "Default Device";

        /// <summary>Aparato físico de esta mano. Uno por nombre.</summary>
        public TouchDevice Touch => _touch ?? (_touch = TouchDevice.For(touchDeviceName));
        TouchDevice _touch;

        [Tooltip("Ancla del Touch en el mundo. Si se deja vacío, se ancla donde esté el instrumento al conmutar (la posición buena del mando).")]
        public Transform touchHome;
        [Tooltip("Ajuste fino opcional del origen respecto al ancla (por defecto cero).")]
        public Vector3 touchNudge = Vector3.zero;
        [Tooltip("Usar la orientación del gimbal del stylus. Apagado por defecto (el gimbal no está conectado): el instrumento mantiene orientación fija.")]
        public bool useDeviceOrientation = false;
        [Tooltip("Amplifica el volumen de trabajo del stylus (~16cm) hacia el campo quirúrgico.")]
        public float touchWorkspaceScale = 2.5f;
        [Tooltip("Escala fuerza de mundo -> fuerza del dispositivo (Touch ~3.3 N máx).")]
        public float touchForceScale = 1f;
        [Tooltip("Fuerza (N) que produce vibración máxima como fallback en el mando Quest.")]
        public float questForceForFullRumble = 3.0f;

        [Tooltip("Amplitud máxima del aviso vibratorio del Touch, en newtons. " +
                 "Es un aviso, no una fuerza de contacto: debe notarse sin " +
                 "mover la mano.")]
        public float pulseForce = 0.35f;

        [Tooltip("Frecuencia del aviso vibratorio del Touch, en Hz.")]
        public float pulseFrequency = 120f;

        public Backend ActiveBackend => backend;

        // ---------------------------------------------------------------
        // Reproducción háptica: el piloto automático mueve un OBJETIVO y un
        // control PD por fuerza lleva el stylus del Touch hasta él, así que el
        // robot recorre la trayectoria y la simulación lo sigue como si lo
        // moviera la mano. El giro y la pinza los hace el software (el gimbal
        // del Touch no tiene motores).
        // ---------------------------------------------------------------
        [Header("Reproducción háptica (Touch)")]
        // Ganancias en unidades del STYLUS (no del mundo), para que no dependan
        // de touchWorkspaceScale. El bucle corre a la frecuencia de Unity
        // (~60-90 Hz), así que el amortiguamiento tiene que ser casi crítico
        // para la masa del stylus (~45 g): con 0.7 N·s/m vibraba suelto, y por
        // encima de ~5 N·s/m el propio muestreo lo vuelve inestable.
        [Tooltip("Rigidez del resorte hacia el objetivo (N/m en el stylus).")]
        public float guiaRigidez = 100f;
        [Tooltip("Amortiguamiento (N·s/m en el stylus). ~2.5 es casi crítico sin mano.")]
        public float guiaAmortiguamiento = 2.5f;
        [Tooltip("Término integral (N/(m·s) en el stylus): compensa el peso del " +
                 "stylus y la deriva constante, para que no viaje por debajo.")]
        public float guiaIntegral = 60f;
        [Tooltip("Tope de la fuerza integral (N).")]
        public float guiaIntegralMax = 0.7f;

        [Tooltip("Usar el resorte del propio aparato (bucle de servo ~1 kHz): Unity " +
                 "solo mueve el ancla. Es lo que evita la vibración del stylus suelto.")]
        public bool guiaResorteAparato = true;
        [Tooltip("Magnitud de setSpringValues. Calibrado con F11 (PruebaResorte): " +
                 "~3.3 N/mm por unidad, así que 0.045 ≈ 0.15 N/mm (150 N/m).")]
        public float guiaResorteMagnitud = 0.045f;
        [Tooltip("Amortiguamiento que añade Unity sobre el resorte del aparato " +
                 "(N·s/m en el stylus). Bajo: solo evita que oscile suelto.")]
        public float guiaResorteAmortiguamiento = 1.0f;
        [Tooltip("Fuerza máxima de guía (N). El Touch da ~3.3 N de pico y ~0.9 N sostenidos.")]
        public float guiaFuerzaMax = 1.6f;
        [Tooltip("Cuánto puede adelantarse el objetivo al stylus (m en el stylus). " +
                 "Si el robot no llega, la trayectoria espera.")]
        public float guiaCorrea = 0.008f;
        /// <summary>Correa expresada en metros de mundo.</summary>
        public float GuiaCorreaMundo => guiaCorrea * Mathf.Max(0.1f, Touch.WorkspaceScale);
        [Tooltip("Medio recorrido físico usable del stylus alrededor de su punto de " +
                 "inicio (m del aparato). Lo que exceda lo hace el brazo, como el embrague.")]
        public Vector3 guiaAlcance = new Vector3(0.065f, 0.05f, 0.035f);

        public bool GuiaActiva { get; private set; }
        public Vector3 GuiaObjetivo => _guiaPos;
        public float GuiaError { get; private set; }
        /// <summary>Posición de mundo del stylus, sin filtrar (para la prueba de signos).</summary>
        public Vector3 TouchPosMundo { get; private set; }
        /// <summary>Corrección de signo por eje del mundo (la fija la prueba inicial).</summary>
        [System.NonSerialized] public Vector3 guiaSigno = Vector3.one;

        Vector3 _guiaPos, _guiaFuerza, _guiaVel, _guiaPrev, _guiaExtra, _guiaInt;
        float _guiaErrSuma, _guiaErrMax, _guiaLog; int _guiaErrN;
        Quaternion _guiaRot = Quaternion.identity;
        bool _guiaInit;
        float _guiaRampa, _guiaT, _guiaTrig;
        int _frameFuerza = -1;
        Vector3? _fuerzaPrueba;

        public void IniciarGuia()
        {
            GuiaActiva = true;
            _guiaInit = false;
            _guiaRampa = 0f;
            _kbNudge = Vector3.zero;
            _kbGiro = Quaternion.identity;
        }

        public void DetenerGuia()
        {
            if (!GuiaActiva && _fuerzaPrueba == null) return;
            GuiaActiva = false;
            _fuerzaPrueba = null;
            _guiaFuerza = Vector3.zero;
            _guiaExtra = Vector3.zero;
            KbJawOverride = -1f;
            if (backend == Backend.TouchHaptic)
            {
                Touch.SetForceWorld(Vector3.zero);
                Touch.SetSpringRaw(Touch.RawPos, 0f);   // apaga el resorte del aparato
            }
        }

        /// <summary>Fuerza fija de mundo (prueba de signos). null = sin prueba.</summary>
        public void FuerzaDePrueba(Vector3? f)
        {
            _fuerzaPrueba = f;
            if (f == null && backend == Backend.TouchHaptic && !GuiaActiva) Touch.SetForceWorld(Vector3.zero);
        }

        /// <summary>Desplazamiento que el brazo debe hacer por software porque el stylus no llega.</summary>
        public Vector3 ConsumirExtraGuia() { var e = _guiaExtra; _guiaExtra = Vector3.zero; return e; }

        void ActualizarGuia(Vector3 tPos, ref Quaternion tRot, ref float tTrig, ref bool tSec)
        {
            float ahora = Time.realtimeSinceStartup;
            if (!_guiaInit)
            {
                _guiaInit = true;
                _guiaPos = tPos; _guiaRot = tRot; _guiaPrev = tPos; _guiaVel = Vector3.zero;
                _guiaT = ahora - 0.016f; _guiaTrig = tTrig;
                _guiaInt = Vector3.zero; _guiaErrSuma = _guiaErrMax = 0f; _guiaErrN = 0; _guiaLog = ahora + 2f;
            }
            float dt = Mathf.Clamp(ahora - _guiaT, 1e-3f, 0.1f);
            _guiaT = ahora;

            FindArm();
            float esc = _armCache != null && _armCache.touchMode == RoboticArm.ControlMode.Relativo
                ? Mathf.Max(0.1f, _armCache.motionScale) : 1f;

            // órdenes del piloto (las mismas que en modo teclado)
            if (_kbGiro != Quaternion.identity) { _guiaRot = (_kbGiro * _guiaRot).normalized; _kbGiro = Quaternion.identity; }
            if (_kbNudge != Vector3.zero) { _guiaPos += _kbNudge / esc; _kbNudge = Vector3.zero; }

            // alcance físico: lo que sobra lo mueve el brazo (re-indexado automático)
            Vector3 local = Quaternion.Inverse(_touchAnchorRot) * (_guiaPos - _touchAnchorPos);
            Vector3 lim = guiaAlcance * Touch.WorkspaceScale;
            Vector3 lc = new Vector3(Mathf.Clamp(local.x, -lim.x, lim.x),
                                     Mathf.Clamp(local.y, -lim.y, lim.y),
                                     Mathf.Clamp(local.z, -lim.z, lim.z));
            if (lc != local)
            {
                Vector3 sobra = _touchAnchorRot * (local - lc);
                _guiaPos -= sobra;
                _guiaExtra += sobra * esc;
            }

            // correa: el objetivo no se escapa si el stylus va atrasado
            float escT = Mathf.Max(0.1f, Touch.WorkspaceScale);   // mundo = stylus * escT
            Vector3 off = _guiaPos - tPos;
            if (off.magnitude > GuiaCorreaMundo) _guiaPos = tPos + off.normalized * GuiaCorreaMundo;
            GuiaError = (_guiaPos - tPos).magnitude;

            // PD con velocidad filtrada (tiempo real: vale también grabando)
            Vector3 v = (tPos - _guiaPrev) / dt;
            _guiaPrev = tPos;
            _guiaVel = Vector3.Lerp(_guiaVel, v, 0.6f);   // poco filtro: el retraso desestabiliza
            _guiaRampa = Mathf.MoveTowards(_guiaRampa, 1f, dt / 1.2f);   // entra suave
            Vector3 err = (_guiaPos - tPos) / escT;      // m en el stylus
            Vector3 velS = _guiaVel / escT;              // m/s en el stylus
            if (guiaIntegral > 0f)
                _guiaInt = Vector3.ClampMagnitude(_guiaInt + err * (guiaIntegral * dt * _guiaRampa), guiaIntegralMax);
            Vector3 f;
            if (guiaResorteAparato)
            {
                // El aparato hace el muelle a 1 kHz; aquí solo amortiguamiento + peso.
                Touch.SetSpringRaw(Touch.MundoARaw(_guiaPos), guiaResorteMagnitud * _guiaRampa);
                f = -guiaResorteAmortiguamiento * velS + _guiaInt;
            }
            else f = guiaRigidez * err - guiaAmortiguamiento * velS + _guiaInt;
            f = Vector3.ClampMagnitude(f, guiaFuerzaMax) * _guiaRampa;

            // error de seguimiento en mm del stylus (para afinar ganancias)
            float eMm = err.magnitude * 1000f;
            _guiaErrSuma += eMm; _guiaErrN++; _guiaErrMax = Mathf.Max(_guiaErrMax, eMm);
            if (ahora >= _guiaLog)
            {
                _guiaLog = ahora + 2f;
                Debug.Log($"[Guia] Error de seguimiento (stylus): medio {_guiaErrSuma / Mathf.Max(1, _guiaErrN):0.0} mm, " +
                          $"máx {_guiaErrMax:0.0} mm · integral {_guiaInt.magnitude:0.00} N");
                _guiaErrSuma = _guiaErrMax = 0f; _guiaErrN = 0;
            }
            _guiaFuerza = Vector3.Scale(f, guiaSigno);

            tRot = _guiaRot;
            if (KbJawOverride >= 0f) _guiaTrig = Mathf.MoveTowards(_guiaTrig, KbJawOverride, Time.deltaTime * 6f);
            else _guiaTrig = tTrig;
            tTrig = _guiaTrig;
            tSec = false;   // sin embrague mientras guía el robot
        }

        // ---------------------------------------------------------------
        // Botones del Touch desde teclado y mouse. Los del stylus siguen
        // valiendo (se combinan): esto es para cuando no responden o resultan
        // incómodos. Solo actúa sobre la mano activa (Tab en modo teclado).
        //   Embrague: mantener Ctrl izquierdo o clic derecho
        //   Pinza:    mantener F o clic izquierdo · C la deja cerrada
        // ---------------------------------------------------------------
        bool _touchLatch;

        // ---------------------------------------------------------------
        // Orientación del Touch. Los 3 GDL del gimbal (sensores de giro) del
        // aparato actual no funcionan, así que por defecto NO se usan aunque la
        // escena tenga useDeviceOrientation: la posición la da el robot y el giro
        // de la muñeca se hace con el teclado (I/K J/L U/O, como en modo
        // teclado). F12 lo conmuta para cuando haya un stylus con gimbal sano.
        // ---------------------------------------------------------------
        const string KeyOrientacion = "SutureTrainer.OrientacionTouch";

        public static bool OrientacionTouch
        {
            get => PlayerPrefs.GetInt(KeyOrientacion, 0) == 1;
            set { PlayerPrefs.SetInt(KeyOrientacion, value ? 1 : 0); PlayerPrefs.Save(); }
        }

        Quaternion _touchRotKb = Quaternion.identity;
        bool _touchRotInit;

        Quaternion GiroTeclado()
        {
            if (!_touchRotInit) { _touchRotKb = _touchAnchorRot; _touchRotInit = true; }
            var kb = Keyboard.current;
            if (kb == null || KeyboardControls.ActiveHand != node) return _touchRotKb;
            float p = 0f, y = 0f, r = 0f;
            if (kb.iKey.isPressed) p -= 1f;
            if (kb.kKey.isPressed) p += 1f;
            if (kb.jKey.isPressed) y -= 1f;
            if (kb.lKey.isPressed) y += 1f;
            if (kb.uKey.isPressed) r += 1f;
            if (kb.oKey.isPressed) r -= 1f;
            if (kb.backspaceKey.wasPressedThisFrame) _touchRotKb = _touchAnchorRot;
            if (p == 0f && y == 0f && r == 0f) return _touchRotKb;
            var cam = KeyboardControls.ViewCamera;
            Vector3 camRight = cam != null ? cam.transform.right : Vector3.right;
            Vector3 camFwd = cam != null ? cam.transform.forward : Vector3.forward;
            float w = KeyboardControls.RotSpeed * Time.deltaTime;
            Quaternion giro = Quaternion.AngleAxis(p * w, camRight) *
                              Quaternion.AngleAxis(y * w, Vector3.up) *
                              Quaternion.AngleAxis(r * w, camFwd);
            _touchRotKb = (giro * _touchRotKb).normalized;
            return _touchRotKb;
        }

        void BotonesTeclado(ref float trig, ref bool embrague)
        {
            if (KeyboardControls.ActiveHand != node) return;
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            if (kb != null && kb.cKey.wasPressedThisFrame) _touchLatch = !_touchLatch;
            bool cerrar = _touchLatch ||
                          (kb != null && kb.fKey.isPressed) ||
                          (mouse != null && mouse.leftButton.isPressed);
            if (cerrar) trig = 1f;
            if ((kb != null && kb.leftCtrlKey.isPressed) || (mouse != null && mouse.rightButton.isPressed))
                embrague = true;
        }

        void LateUpdate()
        {
            // Si nadie mandó fuerza este frame (sin InstrumentHaptics), se manda la de guía sola.
            if (backend != Backend.TouchHaptic || KeyboardControls.Active) return;
            if ((GuiaActiva || _fuerzaPrueba != null) && _frameFuerza != Time.frameCount)
                Touch.SetForceWorld(_fuerzaPrueba ?? _guiaFuerza);
        }

        public bool IsTracked { get; private set; }
        public Vector3 WorldPos { get; private set; }
        public Quaternion WorldRot { get; private set; }
        public float Trigger { get; private set; }   // 0..1 cierre de mandíbula
        public float Grip { get; private set; }
        public bool Clutch { get; private set; }     // botón primario (A/X)
        public bool Secondary { get; private set; }  // botón secundario (B/Y)
        public bool TriggerPressed => Trigger > 0.7f;

        // Pose de apuntado (aim/pointer) para láser de menús; si el runtime no
        // la expone, se aproxima inclinando la pose del dispositivo.
        public Vector3 AimWorldPos { get; private set; }
        public Quaternion AimWorldRot { get; private set; }
        [Tooltip("Corrección de cabeceo si no hay pose de puntero nativa.")]
        public float aimFallbackPitch = 45f;

        InputAction _posAction;
        InputAction _rotAction;
        InputAction _triggerAction;
        InputAction _gripAction;
        InputAction _primaryAction;
        InputAction _secondaryAction;
        InputAction _aimPosAction;
        InputAction _aimRotAction;
        bool _hasAim;
        Vector3 _rawAimPos;
        Quaternion _rawAimRot = Quaternion.identity;
        UnityEngine.XR.InputDevice _legacyDevice;
        Vector3 _smPos;
        Quaternion _smRot = Quaternion.identity;
        bool _hasPose;

        void OnEnable()
        {
            string hand = node == XRNode.LeftHand ? "{LeftHand}" : "{RightHand}";
            _posAction = new InputAction(binding: $"<XRController>{hand}/devicePosition");
            _rotAction = new InputAction(binding: $"<XRController>{hand}/deviceRotation");
            _triggerAction = new InputAction(binding: $"<XRController>{hand}/trigger");
            _gripAction = new InputAction(binding: $"<XRController>{hand}/grip");
            _primaryAction = new InputAction(binding: $"<XRController>{hand}/primaryButton");
            _secondaryAction = new InputAction(binding: $"<XRController>{hand}/secondaryButton");
            _aimPosAction = new InputAction(binding: $"<XRController>{hand}/pointerPosition");
            _aimRotAction = new InputAction(binding: $"<XRController>{hand}/pointerRotation");
            _aimPosAction.Enable();
            _aimRotAction.Enable();
            _posAction.Enable();
            _rotAction.Enable();
            _triggerAction.Enable();
            _gripAction.Enable();
            _primaryAction.Enable();
            _secondaryAction.Enable();
        }

        void OnDisable()
        {
            _posAction?.Disable();
            _rotAction?.Disable();
            _triggerAction?.Disable();
            _gripAction?.Disable();
            _primaryAction?.Disable();
            _secondaryAction?.Disable();
            _aimPosAction?.Disable();
            _aimRotAction?.Disable();
        }

        void Update()
        {
            if (toggleKey != Key.None && Keyboard.current != null &&
                Keyboard.current[toggleKey].wasPressedThisFrame)
                SetBackend(backend == Backend.QuestController ? Backend.TouchHaptic : Backend.QuestController);

            // --- Mano sin robot durante la reproducción háptica: la mueve el
            //     piloto en software, igual que en modo teclado ---
            if (GuiaActiva && backend != Backend.TouchHaptic && !KeyboardControls.Active)
            {
                if (!_kbActivo) { _kbActivo = true; _kbInit = false; }
                UpdateKeyboard();
                return;
            }

            // --- Modo de pruebas con teclado y mouse (sin visor ni Touch) ---
            if (KeyboardControls.Active)
            {
                if (!_kbActivo) { _kbActivo = true; _kbInit = false; }
                UpdateKeyboard();
                return;
            }
            _kbActivo = false;

            // --- Backend Touch (3D Systems / OpenHaptics) ---
            if (backend == Backend.TouchHaptic)
            {
                // ancla la pose donde está el instrumento al conmutar (posición buena)
                if (!_touchAnchorSet) CaptureTouchAnchor();
                Touch.AnchorPos = _touchAnchorPos;
                Touch.AnchorRot = _touchAnchorRot;
                Touch.LocalOffset = touchNudge;
                Touch.WorkspaceScale = touchWorkspaceScale;
                Touch.ForceScale = touchForceScale;

                if (Touch.TryReadPose(out Vector3 tPos, out Quaternion tRot, out float tTrig, out bool tSec))
                {
                    TouchPosMundo = tPos;
                    BotonesTeclado(ref tTrig, ref tSec);
                    if (!(useDeviceOrientation && OrientacionTouch)) tRot = GiroTeclado(); // gimbal sin sensores: giro por teclado
                    if (GuiaActiva) ActualizarGuia(tPos, ref tRot, ref tTrig, ref tSec);
                    ApplyWorldPose(tPos, tRot, tTrig, 0f, tSec);
                    return;
                }
                // Touch seleccionado pero no disponible: sin tracking (no cae al mando).
                IsTracked = false;
                return;
            }

            // al usar el mando, olvida el ancla para recapturarla si se vuelve a Touch
            _touchAnchorSet = false;

            // --- Backend Quest (comportamiento original) ---
            if (TryReadInputSystem(out Vector3 rawPos, out Quaternion rawRot, out float trig, out float grip, out bool prim))
            {
                ApplyPose(rawPos, rawRot, trig, grip, prim);
                return;
            }

            if (!_legacyDevice.isValid)
                _legacyDevice = InputDevices.GetDeviceAtXRNode(node);
            if (!_legacyDevice.isValid) { IsTracked = false; return; }

            bool ok = _legacyDevice.TryGetFeatureValue(UnityEngine.XR.CommonUsages.devicePosition, out rawPos);
            ok &= _legacyDevice.TryGetFeatureValue(UnityEngine.XR.CommonUsages.deviceRotation, out rawRot);
            IsTracked = ok;
            if (!ok) return;

            _legacyDevice.TryGetFeatureValue(UnityEngine.XR.CommonUsages.trigger, out trig);
            _legacyDevice.TryGetFeatureValue(UnityEngine.XR.CommonUsages.grip, out grip);
            _legacyDevice.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primaryButton, out prim);
            _legacyDevice.TryGetFeatureValue(UnityEngine.XR.CommonUsages.secondaryButton, out bool sec);
            Secondary = sec;
            ApplyPose(rawPos, rawRot, trig, grip, prim);
        }

        bool TryReadInputSystem(out Vector3 rawPos, out Quaternion rawRot, out float trig, out float grip, out bool prim)
        {
            rawPos = default;
            rawRot = Quaternion.identity;
            trig = grip = 0f;
            prim = false;

            if (_posAction.activeControl == null)
            {
                IsTracked = false;
                return false;
            }

            IsTracked = true;
            rawPos = _posAction.ReadValue<Vector3>();
            rawRot = _rotAction.ReadValue<Quaternion>();
            trig = _triggerAction.ReadValue<float>();
            grip = _gripAction.ReadValue<float>();
            prim = _primaryAction.IsPressed();
            Secondary = _secondaryAction.IsPressed();

            _hasAim = _aimRotAction.activeControl != null;
            if (_hasAim)
            {
                _rawAimPos = _aimPosAction.ReadValue<Vector3>();
                _rawAimRot = _aimRotAction.ReadValue<Quaternion>();
            }
            return true;
        }

        void ApplyPose(Vector3 rawPos, Quaternion rawRot, float trig, float grip, bool prim)
        {
            if (!_hasPose) { _smPos = rawPos; _smRot = rawRot; _hasPose = true; }
            float t = 1f - Mathf.Pow(1f - smoothing, Time.deltaTime * 90f);
            _smPos = Vector3.Lerp(_smPos, rawPos, t);
            _smRot = Quaternion.Slerp(_smRot, rawRot, t);

            if (trackingSpace != null)
            {
                WorldPos = trackingSpace.TransformPoint(_smPos);
                WorldRot = trackingSpace.rotation * _smRot;
            }
            else
            {
                WorldPos = _smPos;
                WorldRot = _smRot;
            }

            Trigger = trig;
            Grip = grip;
            Clutch = prim;

            // pose de apuntado
            if (_hasAim)
            {
                if (trackingSpace != null)
                {
                    AimWorldPos = trackingSpace.TransformPoint(_rawAimPos);
                    AimWorldRot = trackingSpace.rotation * _rawAimRot;
                }
                else { AimWorldPos = _rawAimPos; AimWorldRot = _rawAimRot; }
            }
            else
            {
                AimWorldPos = WorldPos;
                AimWorldRot = WorldRot * Quaternion.Euler(aimFallbackPitch, 0f, 0f);
            }
        }

        // Aplica una pose que YA viene en espacio de mundo (backend Touch).
        void ApplyWorldPose(Vector3 wp, Quaternion wr, float trig, float grip, bool clutch)
        {
            if (!_hasPose) { _smPos = wp; _smRot = wr; _hasPose = true; }
            float t = 1f - Mathf.Pow(1f - smoothing, Time.deltaTime * 90f);
            _smPos = Vector3.Lerp(_smPos, wp, t);
            _smRot = Quaternion.Slerp(_smRot, wr, t);

            WorldPos = _smPos;
            WorldRot = _smRot;
            Trigger = trig;
            Grip = grip;
            Clutch = clutch;
            Secondary = false;

            AimWorldPos = WorldPos;
            AimWorldRot = WorldRot * Quaternion.Euler(aimFallbackPitch, 0f, 0f);
            IsTracked = true;
        }

        // ---------------------------------------------------------------
        // Modo teclado y mouse. Produce la misma salida que el mando (pose de
        // mundo + gatillo + secundario) para que RoboticArm, en modo
        // Absoluto, no note la diferencia: muñeca = WorldPos + WorldRot*(0,0,0.05)
        // y orientación = WorldRot * rotationOffsetEuler.
        // ---------------------------------------------------------------
        bool _kbActivo, _kbInit, _kbLatch;
        Vector3 _kbPos, _kbHomePos;
        Quaternion _kbRot = Quaternion.identity, _kbHomeRot = Quaternion.identity;
        float _kbTrig;
        Vector3 _kbNudge;

        /// <summary>Desplazamiento de mundo extra para la muñeca (piloto automático).</summary>
        public void KbNudge(Vector3 delta) => _kbNudge += delta;

        Quaternion _kbGiro = Quaternion.identity;

        /// <summary>Giro de mundo extra de la muñeca, sobre su propio punto (piloto automático).</summary>
        public void KbRotate(Quaternion delta) => _kbGiro = delta * _kbGiro;

        /// <summary>&gt;= 0: fija el cierre de la pinza (piloto automático). -1: teclado.</summary>
        [System.NonSerialized] public float KbJawOverride = -1f;

        static readonly Vector3 KbAvance = new Vector3(0f, 0f, 0.05f);

        void UpdateKeyboard()
        {
            FindArm();
            var arm = _armCache;

            if (!_kbInit)
            {
                // Arrancar EXACTAMENTE donde está el instrumento: deshacer las
                // dos transformaciones que aplica RoboticArm.
                if (arm != null && arm.wrist != null)
                {
                    Quaternion desfase = Quaternion.Euler(arm.rotationOffsetEuler);
                    _kbRot = arm.wrist.rotation * Quaternion.Inverse(desfase);
                    _kbPos = arm.wrist.position - _kbRot * KbAvance;
                }
                _kbHomePos = _kbPos; _kbHomeRot = _kbRot;
                _kbTrig = 0f; _kbLatch = false;
                _kbInit = true;
            }

            var kb = Keyboard.current;
            var mouse = Mouse.current;
            bool activa = KeyboardControls.ActiveHand == node;

            if (activa && kb != null)
            {
                float dt = Time.deltaTime;
                bool fino = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;
                float k = fino ? 0.25f : 1f;

                var cam = KeyboardControls.ViewCamera;
                Vector3 fwd = cam != null ? Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up) : Vector3.forward;
                fwd = fwd.sqrMagnitude > 1e-4f ? fwd.normalized : Vector3.forward;
                Vector3 right = Vector3.Cross(Vector3.up, fwd);
                Vector3 camRight = cam != null ? cam.transform.right : right;
                Vector3 camUp = cam != null ? cam.transform.up : Vector3.up;
                Vector3 camFwd = cam != null ? cam.transform.forward : fwd;

                if (kb.backspaceKey.wasPressedThisFrame) { _kbPos = _kbHomePos; _kbRot = _kbHomeRot; }

                Vector3 muneca = _kbPos + _kbRot * KbAvance;

                Vector3 mov = Vector3.zero;
                if (kb.wKey.isPressed) mov += fwd;
                if (kb.sKey.isPressed) mov -= fwd;
                if (kb.dKey.isPressed) mov += right;
                if (kb.aKey.isPressed) mov -= right;
                if (kb.eKey.isPressed) mov += Vector3.up;
                if (kb.qKey.isPressed) mov -= Vector3.up;
                muneca += mov * (KeyboardControls.MoveSpeed * k * dt);

                if (mouse != null)
                {
                    if (mouse.rightButton.isPressed)
                    {
                        Vector2 d = mouse.delta.ReadValue();
                        muneca += (camRight * d.x + camUp * d.y) * (KeyboardControls.MouseSensitivity * k);
                    }
                    float sc = mouse.scroll.ReadValue().y;
                    if (Mathf.Abs(sc) > 0.01f)
                        muneca += camFwd * (Mathf.Sign(sc) * KeyboardControls.ScrollStep * k);
                }

                if (arm != null)
                {
                    Vector3 dd = muneca - arm.workspaceCenter;
                    Vector3 e = arm.workspaceExtents;
                    dd.x = Mathf.Clamp(dd.x, -e.x, e.x);
                    dd.y = Mathf.Clamp(dd.y, -e.y, e.y);
                    dd.z = Mathf.Clamp(dd.z, -e.z, e.z);
                    muneca = arm.workspaceCenter + dd;
                }

                // giro alrededor de la muñeca, en ejes de la cámara
                float p = 0f, y = 0f, r = 0f;
                if (kb.iKey.isPressed) p -= 1f;
                if (kb.kKey.isPressed) p += 1f;
                if (kb.jKey.isPressed) y -= 1f;
                if (kb.lKey.isPressed) y += 1f;
                if (kb.uKey.isPressed) r += 1f;
                if (kb.oKey.isPressed) r -= 1f;
                float w = KeyboardControls.RotSpeed * k * dt;
                Quaternion giro = Quaternion.AngleAxis(p * w, camRight) *
                                  Quaternion.AngleAxis(y * w, Vector3.up) *
                                  Quaternion.AngleAxis(r * w, camFwd);
                _kbRot = (giro * _kbRot).normalized;
                _kbPos = muneca - _kbRot * KbAvance;

                if (kb.cKey.wasPressedThisFrame) _kbLatch = !_kbLatch;
                bool cerrar = _kbLatch || kb.fKey.isPressed ||
                              (mouse != null && mouse.leftButton.isPressed);
                if (KbJawOverride < 0f)   // con el piloto automático manda él
                    _kbTrig = Mathf.MoveTowards(_kbTrig, cerrar ? 1f : 0f, dt * 6f);
                Secondary = kb.tKey.isPressed;
            }
            else
            {
                // la otra mano se queda quieta y conserva la pinza como estaba
                Secondary = false;
            }

            // piloto automático: corre aunque esta no sea la mano activa
            if (_kbGiro != Quaternion.identity)
            {
                Vector3 m = _kbPos + _kbRot * KbAvance;          // la muñeca no se mueve al girar
                _kbRot = (_kbGiro * _kbRot).normalized;
                _kbPos = m - _kbRot * KbAvance;
                _kbGiro = Quaternion.identity;
            }
            if (_kbNudge != Vector3.zero) { _kbPos += _kbNudge; _kbNudge = Vector3.zero; }
            if (KbJawOverride >= 0f)
                _kbTrig = Mathf.MoveTowards(_kbTrig, KbJawOverride, Time.deltaTime * 6f);

            KeyboardControls.ReportLatch(node, _kbLatch);

            WorldPos = _kbPos;
            WorldRot = _kbRot;
            Trigger = _kbTrig;
            Grip = 0f;
            Clutch = false;
            IsTracked = true;
            AimWorldPos = WorldPos;
            AimWorldRot = WorldRot * Quaternion.Euler(aimFallbackPitch, 0f, 0f);
        }

        public void SetBackend(Backend b)
        {
            backend = b;
            _hasPose = false;
            _touchAnchorSet = false;                              // recaptura el ancla
            _touchRotInit = false;
            if (b == Backend.TouchHaptic) Touch.Recenter(); // arranca en el ancla
        }

        RoboticArm _armCache;
        bool _armSearched;
        Vector3 _touchAnchorPos;
        Quaternion _touchAnchorRot = Quaternion.identity;
        bool _touchAnchorSet;

        // Fija el ancla del Touch (una vez por entrada al modo): el override
        // touchHome si está puesto; si no, la posición ACTUAL del instrumento
        // que maneja este MasterInput (la misma que tenías con el mando), de
        // modo que al conmutar el instrumento se queda donde estaba y se mueve
        // desde ahí. El brazo se localiza una vez en runtime (sin reconstruir).
        void CaptureTouchAnchor()
        {
            _touchAnchorSet = true;

            if (touchAnchorOnNeedle && TryAnchorOnNeedle()) return;

            if (touchHome != null)
            {
                _touchAnchorPos = touchHome.position;
                _touchAnchorRot = touchHome.rotation;
                return;
            }

            FindArm();

            _touchAnchorRot = trackingSpace != null ? trackingSpace.rotation : Quaternion.identity;
            if (_armCache != null && _armCache.wrist != null) _touchAnchorPos = _armCache.wrist.position;
            else if (_armCache != null) _touchAnchorPos = _armCache.workspaceCenter;
            else _touchAnchorPos = trackingSpace != null ? trackingSpace.position : Vector3.zero;
        }

        void FindArm()
        {
            if (_armSearched) return;
            _armSearched = true;
            foreach (var a in FindObjectsByType<RoboticArm>(FindObjectsSortMode.None))
                if (a.master == this) { _armCache = a; break; }
        }

        /// <summary>
        /// Ancla el Touch de modo que las MANDÍBULAS queden sobre la aguja al
        /// conmutar: el stylus empieza en su centro y el instrumento aparece ya
        /// en posición de agarre, sin tener que ir a buscarla.
        ///
        /// El ancla NO es la posición de la aguja tal cual, porque entre el
        /// ancla y las mandíbulas hay dos desfases encadenados:
        ///
        ///   WorldPos  = ancla                          (stylus centrado)
        ///   muñeca    = WorldPos + rot * (0,0,0.05)    (RoboticArm, modo Absoluto)
        ///   mandíbulas= muñeca + (jawAnchor - muñeca)  (jawAnchor sobre el modelo)
        ///
        /// Sin descontarlos, el instrumento aparece medio palmo pasado de la
        /// aguja. El segundo desfase no es una constante: SceneBuilder recoloca
        /// jawAnchor sobre las mandíbulas visibles del modelo importado, así
        /// que se lee del brazo en runtime en vez de darlo por supuesto.
        /// </summary>
        bool TryAnchorOnNeedle()
        {
            var needle = FindFirstObjectByType<SutureNeedle>();
            if (needle == null) return false;

            FindArm();
            if (_armCache == null || _armCache.wrist == null) return false;

            _touchAnchorRot = trackingSpace != null ? trackingSpace.rotation : Quaternion.identity;

            Vector3 avanceMano = _touchAnchorRot * new Vector3(0f, 0f, 0.05f);
            Vector3 mandibulas = _armCache.jawAnchor != null
                ? _armCache.jawAnchor.position - _armCache.wrist.position
                : Vector3.zero;

            _touchAnchorPos = needle.transform.position - avanceMano - mandibulas;

            // El residuo tiene que ser ~0. Si sale grande, el desfase de las
            // mandíbulas no es el que se cree y el instrumento aparecerá
            // desplazado: se ve aquí antes que en el visor.
            float residuo = Vector3.Distance(
                _touchAnchorPos + avanceMano + mandibulas, needle.transform.position);
            Debug.Log($"[Touch] Anclado sobre la aguja. Avance {avanceMano.magnitude * 1000f:F0} mm, " +
                      $"mandíbulas {mandibulas.magnitude * 1000f:F0} mm, residuo {residuo * 1000f:F1} mm.");

            return true;
        }

        /// <summary>
        /// Ganancia global de la salida háptica. La mueve
        /// AdaptiveDifficultyController como guía asistiva: >1 refuerza las
        /// señales de contacto y error cuando el aprendiz está sobrecargado,
        /// &lt;1 las retira cuando va sobrado. Afecta tanto a la fuerza continua
        /// del Touch como a los pulsos.
        /// </summary>
        public static float HapticGain = 1f;

        /// <summary>
        /// Envía una fuerza de mundo (N) a la mano. En Touch es fuerza real en
        /// el stylus; en Quest se traduce a vibración proporcional (fallback).
        /// La llama InstrumentHaptics cada frame.
        /// </summary>
        public void SendForce(Vector3 worldForce)
        {
            worldForce *= HapticGain;

            if (backend == Backend.TouchHaptic)
            {
                if (_fuerzaPrueba != null) worldForce = _fuerzaPrueba.Value;
                else if (GuiaActiva) worldForce += _guiaFuerza;
                Touch.SetForceWorld(worldForce);
                _frameFuerza = Time.frameCount;
                return;
            }
            // Fallback Quest: magnitud -> amplitud de vibración
            float a = Mathf.Clamp01(worldForce.magnitude / Mathf.Max(0.01f, questForceForFullRumble));
            if (a > 0.03f && _legacyDevice.isValid)
                _legacyDevice.SendHapticImpulse(0u, a * 0.8f, Time.deltaTime + 0.02f);
        }

        public void Haptic(float amplitude = 0.4f, float duration = 0.05f)
        {
            amplitude = Mathf.Clamp01(amplitude * HapticGain);

            if (backend == Backend.TouchHaptic)
            {
                // Vibración, NO fuerza.
                //
                // Antes esto llamaba a SetForceWorld, o sea escribía en el mismo
                // efecto de fuerza constante que usa el lazo de contacto: un
                // aviso de error (amplitud 0.9) metía 2.25 N por el eje del
                // stylus y pisaba la fuerza del tejido hasta el siguiente
                // FixedUpdate. Se sentía como un tirón del robot en cada aviso.
                //
                // La vibración es un efecto aparte: notifica sin competir con la
                // fuerza sostenida, y se apaga sola al cumplirse el tiempo, así
                // que además `duration` por fin significa algo aquí.
                Touch.Pulse(WorldRot * Vector3.forward,
                                  amplitude * pulseForce,
                                  pulseFrequency,
                                  Mathf.Max(0.01f, duration));
                return;
            }
            if (_legacyDevice.isValid)
                _legacyDevice.SendHapticImpulse(0u, amplitude, duration);
        }

        // Silencia SOLO su aparato. Desconectar es global y lo hace
        // TouchShutdown al terminar el juego: si cada mano llamara a
        // disconnectAllDevices, la primera en morir se llevaría a la otra.
        void OnDestroy() { if (_touch != null) _touch.Shutdown(); }
    }
}
