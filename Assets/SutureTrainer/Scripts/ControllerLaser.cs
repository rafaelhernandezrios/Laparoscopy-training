using UnityEngine;

namespace SutureTrainer
{
    /// <summary>
    /// Láser de selección del controlador derecho para menús y panel de
    /// resultados. Se activa en el menú y al terminar un nivel.
    /// </summary>
    public class ControllerLaser : MonoBehaviour
    {
        public MasterInput master;
        public bool startActive;
        public float maxDistance = 6f;

        LineRenderer lr;
        WorldButton hovered;
        bool active;
        bool lastTrigger;

        void Awake()
        {
            lr = gameObject.AddComponent<LineRenderer>();
            lr.startWidth = 0.004f; lr.endWidth = 0.001f;
            lr.positionCount = 2;
            lr.enabled = false;
        }

        void Start()
        {
            if (MaterialSet.I != null && MaterialSet.I.laser != null)
                lr.sharedMaterial = MaterialSet.I.laser;
            SetActive(startActive);
        }

        public void SetActive(bool a)
        {
            active = a;
            if (lr != null) lr.enabled = a;
            if (!a && hovered != null) { hovered.SetHover(false); hovered = null; }
        }

        void Update()
        {
            if (!active || master == null || !master.IsTracked) { if (lr != null) lr.enabled = false; return; }
            lr.enabled = true;

            Vector3 origin = master.WorldPos;
            Vector3 dir = master.WorldRot * Vector3.forward;
            Vector3 end = origin + dir * maxDistance;

            WorldButton hit = null;
            if (Physics.Raycast(origin, dir, out RaycastHit info, maxDistance))
            {
                end = info.point;
                hit = info.collider.GetComponentInParent<WorldButton>();
            }

            if (hit != hovered)
            {
                if (hovered != null) hovered.SetHover(false);
                hovered = hit;
                if (hovered != null) { hovered.SetHover(true); master.Haptic(0.15f, 0.02f); }
            }

            bool trig = master.TriggerPressed;
            if (trig && !lastTrigger && hovered != null)
                hovered.Click();
            lastTrigger = trig;

            lr.SetPosition(0, origin);
            lr.SetPosition(1, end);
        }
    }
}
