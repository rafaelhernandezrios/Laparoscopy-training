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
        public XRNode node = XRNode.RightHand;
        [Tooltip("Transform del Camera Offset del XR Origin (espacio de tracking).")]
        public Transform trackingSpace;
        [Range(0.05f, 1f)] public float smoothing = 0.35f;

        public bool IsTracked { get; private set; }
        public Vector3 WorldPos { get; private set; }
        public Quaternion WorldRot { get; private set; }
        public float Trigger { get; private set; }   // 0..1 cierre de mandíbula
        public float Grip { get; private set; }
        public bool Clutch { get; private set; }     // botón primario (A/X)
        public bool TriggerPressed => Trigger > 0.7f;

        InputAction _posAction;
        InputAction _rotAction;
        InputAction _triggerAction;
        InputAction _gripAction;
        InputAction _primaryAction;
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
            _posAction.Enable();
            _rotAction.Enable();
            _triggerAction.Enable();
            _gripAction.Enable();
            _primaryAction.Enable();
        }

        void OnDisable()
        {
            _posAction?.Disable();
            _rotAction?.Disable();
            _triggerAction?.Disable();
            _gripAction?.Disable();
            _primaryAction?.Disable();
        }

        void Update()
        {
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
        }

        public void Haptic(float amplitude = 0.4f, float duration = 0.05f)
        {
            if (_legacyDevice.isValid)
                _legacyDevice.SendHapticImpulse(0u, amplitude, duration);
        }
    }
}
