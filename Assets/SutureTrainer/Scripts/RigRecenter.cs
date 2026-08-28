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

        [Tooltip("Altura de ojos, en metros, a la que se coloca al usuario. " +
                 "El campo quirúrgico está fijo en y = 1.08 m, así que 1.55 lo " +
                 "deja unos 47 cm por debajo de la vista: postura de consola " +
                 "cómoda tanto sentado como de pie.\n\n" +
                 "Antes se conservaba la altura REAL del usuario, así que " +
                 "sentado el campo quedaba casi a la altura de la cara y de pie " +
                 "demasiado bajo. Pon 0 para volver a ese comportamiento.")]
        public float eyeHeight = 1.55f;

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
            float h = eyeHeight > 0f ? eyeHeight : origin.Camera.transform.position.y;
            origin.MoveCameraToWorldLocation(new Vector3(0f, h, 0f));
        }
    }
}
