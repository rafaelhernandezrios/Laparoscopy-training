using UnityEngine.SceneManagement;

namespace SutureTrainer
{
    /// <summary>Flujo de escenas del entrenador.</summary>
    public static class GameFlow
    {
        public static readonly string[] Scenes =
        {
            "ST_00_Menu",
            "ST_01_ManejoAguja",
            "ST_02_Anillos",
            "ST_03_PuntoSimple",
            "ST_04_SuturaContinua",
            "ST_05_NudoIntracorporeo"
        };

        public static string MenuScene => Scenes[0];

        public static string NextOf(string current)
        {
            for (int i = 0; i < Scenes.Length - 1; i++)
                if (Scenes[i] == current) return Scenes[i + 1];
            return MenuScene;
        }

        public static void Load(string sceneName) => SceneManager.LoadScene(sceneName);
        public static void Reload() => SceneManager.LoadScene(SceneManager.GetActiveScene().name);
        public static void LoadNext() => Load(NextOf(SceneManager.GetActiveScene().name));
        public static void LoadMenu() => Load(MenuScene);
    }
}
