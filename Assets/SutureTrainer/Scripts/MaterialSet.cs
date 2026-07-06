using UnityEngine;

namespace SutureTrainer
{
    /// <summary>
    /// Referencias a los materiales creados por el SceneBuilder, para que
    /// los objetos creados en runtime usen shaders incluidos en el build.
    /// </summary>
    public class MaterialSet : MonoBehaviour
    {
        public static MaterialSet I { get; private set; }

        public Material metal;
        public Material darkMetal;
        public Material needle;
        public Material thread;
        public Material threadTense;
        public Material tissue;
        public Material wound;
        public Material markerIdle;
        public Material markerEntry;   // activo entrada (verde)
        public Material markerExit;    // activo salida (naranja)
        public Material markerDone;
        public Material ring;
        public Material ringActive;
        public Material ringDone;
        public Material ghost;
        public Material panel;
        public Material buttonIdle;
        public Material buttonHover;
        public Material laser;
        public Material floor;

        void Awake() { I = this; }
        void OnDestroy() { if (I == this) I = null; }
    }
}
