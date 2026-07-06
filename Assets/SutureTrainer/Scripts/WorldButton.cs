using UnityEngine;

namespace SutureTrainer
{
    /// <summary>Botón 3D activable con el láser del controlador.</summary>
    [RequireComponent(typeof(BoxCollider))]
    public class WorldButton : MonoBehaviour
    {
        [Tooltip("Si se define, al pulsar carga esta escena.")]
        public string sceneToLoad;
        public System.Action onClick;

        Renderer rend;
        bool hovered;

        void Awake() { rend = GetComponent<Renderer>(); }

        public void SetHover(bool h)
        {
            if (h == hovered) return;
            hovered = h;
            if (rend != null && MaterialSet.I != null)
                rend.sharedMaterial = h ? MaterialSet.I.buttonHover : MaterialSet.I.buttonIdle;
        }

        public void Click()
        {
            onClick?.Invoke();
            if (!string.IsNullOrEmpty(sceneToLoad))
                GameFlow.Load(sceneToLoad);
        }
    }
}
