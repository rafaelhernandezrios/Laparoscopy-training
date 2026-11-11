using UnityEngine;
using UnityEngine.SceneManagement;

namespace SutureTrainer
{
    /// <summary>
    /// Superficies sobre las que se APOYA el hilo: mesa/suelo de la cavidad,
    /// bandeja y la superficie real del hígado.
    ///
    /// Van en una capa propia (sin nombre, índice 30) para que solo el hilo
    /// las consulte: así no empuja la aguja ni las pinzas, y el agarre
    /// (RoboticArm.TryGrab) las ignora porque no llevan Graspable.
    /// </summary>
    public static class ThreadSurfaces
    {
        public const int Layer = 30;
        public static int Mask => 1 << Layer;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Init()
        {
            SceneManager.sceneLoaded -= AlCargar;
            SceneManager.sceneLoaded += AlCargar;
            Construir();
        }

        static void AlCargar(Scene s, LoadSceneMode m) => Construir();

        static void Construir()
        {
            var deform = Object.FindAnyObjectByType<DeformableMesh>();
            if (deform == null && Object.FindAnyObjectByType<TrainingLevel>() == null) return;
            if (GameObject.Find("[SuperficiesHilo]") != null) return;
            new GameObject("[SuperficiesHilo]");

            var mesa = GameObject.Find("FieldBase");
            if (mesa != null) AddBox(mesa.transform, "SuperficieHilo_Mesa");

            if (deform != null && deform.meshFilter != null && deform.meshFilter.sharedMesh != null)
            {
                var go = new GameObject("SuperficieHilo_Higado") { layer = Layer };
                go.transform.SetParent(deform.transform, false);
                go.AddComponent<MeshCollider>().sharedMesh = deform.meshFilter.sharedMesh;
            }
        }

        /// <summary>Caja unitaria hija (copia la forma de un cubo escalado, p. ej. la bandeja).</summary>
        public static void AddBox(Transform padre, string nombre = "SuperficieHilo")
        {
            if (padre == null) return;
            var go = new GameObject(nombre) { layer = Layer };
            go.transform.SetParent(padre, false);
            go.AddComponent<BoxCollider>();
        }

        /// <summary>Altura de la superficie más alta bajo el punto, si la hay.</summary>
        public static bool Altura(Vector3 p, out float y)
        {
            if (Physics.Raycast(p + Vector3.up * 0.25f, Vector3.down, out RaycastHit hit, 0.5f,
                                Mask, QueryTriggerInteraction.Ignore))
            {
                y = hit.point.y;
                return true;
            }
            y = 0f;
            return false;
        }
    }
}
