using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SutureTrainer.EditorTools
{
    /// <summary>
    /// Corre el diagnóstico de la herida sobre todas las escenas de sutura,
    /// para no tener que abrirlas una a una.
    /// </summary>
    public static class OrganWoundCheckBatch
    {
        static readonly string[] Escenas =
        {
            "Assets/SutureTrainer/Scenes/ST_03_PuntoSimple.unity",
            "Assets/SutureTrainer/Scenes/ST_04_SuturaContinua.unity",
            "Assets/SutureTrainer/Scenes/ST_05_NudoIntracorporeo.unity",
            "Assets/SutureTrainer/Scenes/ST_06_SuturaOrgano.unity",
        };

        [MenuItem("SutureTrainer/Diagnóstico: herida en TODAS las escenas")]
        public static void Run()
        {
            foreach (var ruta in Escenas)
            {
                EditorSceneManager.OpenScene(ruta, OpenSceneMode.Single);
                Debug.Log($"===== {System.IO.Path.GetFileNameWithoutExtension(ruta)} =====");
                OrganWoundCheck.Run();
            }
        }
    }
}
