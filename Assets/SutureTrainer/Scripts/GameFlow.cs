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

        /// <summary>
        /// Escena de réplica con el órgano deformable. Va aparte de
        /// <see cref="Scenes"/> a propósito: es una escena de validación, no un
        /// peldaño del temario, así que no debe entrar en la progresión de
        /// NextOf ni en los umbrales por índice de SceneBuilder. Al menú se
        /// añade como botón suelto.
        /// </summary>
        public const string OrganScene = "ST_06_SuturaOrgano";

        /// <summary>Actividad 7: sutura a dos manos (pinza izquierda + porta-agujas).</summary>
        public const string BimanualScene = "ST_07_SuturaDosManos";

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
