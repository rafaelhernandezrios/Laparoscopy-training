using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SutureTrainer.EditorTools
{
    /// <summary>
    /// Hace que Play arranque SIEMPRE en el menú, esté abierta la escena que
    /// esté.
    ///
    /// Unity reproduce por defecto la escena que tengas abierta en el editor, y
    /// como SceneBuilder deja abierta la última que construye, pulsar Play
    /// entraba directo en ST_06 saltándose el menú. La progresión real del
    /// entrenador empieza en el menú, así que aquí se fija el punto de entrada
    /// igual que en una build.
    ///
    /// Se puede desactivar desde **SutureTrainer → Play arranca en el menú**
    /// (marca de verificación) cuando estés iterando sobre una escena suelta y
    /// no quieras pasar por el menú cada vez.
    /// </summary>
    [InitializeOnLoad]
    public static class PlayFromMenu
    {
        const string MenuPath = "SutureTrainer/Play arranca en el menú";
        const string PrefKey = "SutureTrainer.PlayFromMenu";

        static PlayFromMenu()
        {
            // Retrasado: al recargar dominio, el AssetDatabase todavía puede no
            // resolver la escena, y asignar null desactivaría el arranque sin
            // avisar.
            EditorApplication.delayCall += Apply;
        }

        static bool Enabled
        {
            get => EditorPrefs.GetBool(PrefKey, true);
            set => EditorPrefs.SetBool(PrefKey, value);
        }

        [MenuItem(MenuPath)]
        static void Toggle()
        {
            Enabled = !Enabled;
            Apply();
        }

        [MenuItem(MenuPath, validate = true)]
        static bool ToggleValidate()
        {
            Menu.SetChecked(MenuPath, Enabled);
            return true;
        }

        static void Apply()
        {
            if (!Enabled)
            {
                EditorSceneManager.playModeStartScene = null;
                return;
            }

            string path = $"Assets/SutureTrainer/Scenes/{GameFlow.MenuScene}.unity";
            var asset = AssetDatabase.LoadAssetAtPath<SceneAsset>(path);

            if (asset == null)
            {
                // Todavía no se ha construido el menú: no es un error, solo
                // significa que aún no hay punto de entrada que fijar.
                EditorSceneManager.playModeStartScene = null;
                return;
            }

            EditorSceneManager.playModeStartScene = asset;
        }
    }
}
