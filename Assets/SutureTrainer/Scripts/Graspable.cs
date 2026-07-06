using UnityEngine;

namespace SutureTrainer
{
    /// <summary>Objeto que las mandíbulas del instrumento pueden agarrar.</summary>
    public class Graspable : MonoBehaviour
    {
        public bool useGravityOnRelease = true;
        public bool IsHeld { get; private set; }
        public RoboticArm Holder { get; private set; }

        public System.Action<RoboticArm> onGrabbed;
        public System.Action onReleased;

        Transform _originalParent;
        Rigidbody _rb;

        void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _originalParent = transform.parent;
        }

        public void AttachTo(Transform anchor, RoboticArm holder)
        {
            IsHeld = true;
            Holder = holder;
            if (_rb != null) { _rb.isKinematic = true; _rb.linearVelocity = Vector3.zero; }
            transform.SetParent(anchor, true);
            onGrabbed?.Invoke(holder);
        }

        public void Detach()
        {
            IsHeld = false;
            Holder = null;
            transform.SetParent(_originalParent, true);
            if (_rb != null && useGravityOnRelease) { _rb.isKinematic = false; _rb.useGravity = true; }
            onReleased?.Invoke();
        }
    }
}
