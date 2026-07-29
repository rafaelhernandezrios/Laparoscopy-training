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

        public Backend ActiveBackend => backend;

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

            // --- Backend Touch (3D Systems / OpenHaptics) ---
            if (backend == Backend.TouchHaptic)
            {
                // ancla la pose donde está el instrumento al conmutar (posición buena)
                if (!_touchAnchorSet) CaptureTouchAnchor();
                TouchDevice.AnchorPos = _touchAnchorPos;
                TouchDevice.AnchorRot = _touchAnchorRot;
                TouchDevice.LocalOffset = touchNudge;
                TouchDevice.WorkspaceScale = touchWorkspaceScale;
                TouchDevice.ForceScale = touchForceScale;

                if (TouchDevice.TryReadPose(out Vector3 tPos, out Quaternion tRot, out float tTrig, out bool tSec))
                {
                    if (!useDeviceOrientation) tRot = _touchAnchorRot; // orientación fija
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

        public void SetBackend(Backend b)
        {
            backend = b;
            _hasPose = false;
            _touchAnchorSet = false;                              // recaptura el ancla
            if (b == Backend.TouchHaptic) TouchDevice.Recenter(); // arranca en el ancla
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

            if (touchHome != null)
            {
                _touchAnchorPos = touchHome.position;
                _touchAnchorRot = touchHome.rotation;
                return;
            }

            if (!_armSearched)
            {
                _armSearched = true;
                foreach (var a in FindObjectsByType<RoboticArm>(FindObjectsSortMode.None))
                    if (a.master == this) { _armCache = a; break; }
            }

            _touchAnchorRot = trackingSpace != null ? trackingSpace.rotation : Quaternion.identity;
            if (_armCache != null && _armCache.wrist != null) _touchAnchorPos = _armCache.wrist.position;
            else if (_armCache != null) _touchAnchorPos = _armCache.workspaceCenter;
            else _touchAnchorPos = trackingSpace != null ? trackingSpace.position : Vector3.zero;
        }

        /// <summary>
        /// Envía una fuerza de mundo (N) a la mano. En Touch es fuerza real en
        /// el stylus; en Quest se traduce a vibración proporcional (fallback).
        /// La llama InstrumentHaptics cada frame.
        /// </summary>
        public void SendForce(Vector3 worldForce)
        {
            if (backend == Backend.TouchHaptic)
            {
                TouchDevice.SetForceWorld(worldForce);
                return;
            }
            // Fallback Quest: magnitud -> amplitud de vibración
            float a = Mathf.Clamp01(worldForce.magnitude / Mathf.Max(0.01f, questForceForFullRumble));
            if (a > 0.03f && _legacyDevice.isValid)
                _legacyDevice.SendHapticImpulse(0u, a * 0.8f, Time.deltaTime + 0.02f);
        }

        public void Haptic(float amplitude = 0.4f, float duration = 0.05f)
        {
            if (backend == Backend.TouchHaptic)
            {
                // pulso breve de fuerza a lo largo del eje del stylus
                TouchDevice.SetForceWorld(WorldRot * (Vector3.forward * amplitude * 2.5f));
                return;
            }
            if (_legacyDevice.isValid)
                _legacyDevice.SendHapticImpulse(0u, amplitude, duration);
        }

        void OnDestroy() { TouchDevice.Shutdown(); }
    }
}
