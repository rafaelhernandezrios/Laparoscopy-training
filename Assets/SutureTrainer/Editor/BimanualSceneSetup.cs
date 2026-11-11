using System.Linq;
using UnityEditor;

namespace SutureTrainer.EditorTools
{
    /// <summary>Añade la escena de la actividad 7 a Build Settings si falta.</summary>
    [InitializeOnLoad]
    static class BimanualSceneSetup
    {
        const string Ruta = "Assets/SutureTrainer/Scenes/ST_07_SuturaDosManos.unity";

        static BimanualSceneSetup()
        {
            EditorApplication.delayCall += () =>
            {
                if (AssetDatabase.LoadAssetAtPath<SceneAsset>(Ruta) == null) return;
                var escenas = EditorBuildSettings.scenes.ToList();
                if (escenas.Any(s => s.path == Ruta)) return;
                escenas.Add(new EditorBuildSettingsScene(Ruta, true));
                EditorBuildSettings.scenes = escenas.ToArray();
                UnityEngine.Debug.Log("[Actividad 7] Escena añadida a Build Settings.");
            };
        }
    }
}
