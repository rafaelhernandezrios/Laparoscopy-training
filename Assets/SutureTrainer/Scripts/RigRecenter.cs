using UnityEngine;
using Unity.XR.CoreUtils;

namespace SutureTrainer
{
    /// <summary>
    /// Recentra el XR Origin al iniciar la escena: la posición y orientación
    /// reales del usuario pasan a ser (0,0,0) mirando hacia +Z (la consola).
    /// Evita que la escena quede detrás o en espejo si el visor arrancó
    /// mirando en otra dirección.
    /// </summary>
    [RequireComponent(typeof(XROrigin))]
    public class RigRecenter : MonoBehaviour
    {
        public float delay = 0.4f;

        XROrigin origin;
        float timer;
        bool done;

        void Awake() { origin = GetComponent<XROrigin>(); }

        void Update()
        {
            if (done || origin == null || origin.Camera == null) return;
            // espera a tener tracking válido
            if (origin.Camera.transform.localPosition.sqrMagnitude < 1e-6f) return;
            timer += Time.deltaTime;
            if (timer < delay) return;
            Recenter();
            done = true;
        }

        public void Recenter()
        {
            origin.MatchOriginUpCameraForward(Vector3.up, Vector3.forward);
            float h = origin.Camera.transform.position.y;
            origin.MoveCameraToWorldLocation(new Vector3(0f, h, 0f));
        }
    }
}
