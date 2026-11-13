using UnityEditor;

namespace SutureTrainer
{
    /// <summary>
    /// Interruptor persistente del modo de pruebas con teclado y mouse.
    /// Desmarcado = controles reales (mando Quest / 3D Systems Touch).
    /// </summary>
    static class KeyboardControlsMenu
    {
        const string MenuPath = "SutureTrainer/Controles: teclado y mouse (pruebas)";

        [MenuItem(MenuPath)]
        static void Toggle()
        {
            KeyboardControls.Pref = !KeyboardControls.Pref;
            if (EditorApplication.isPlaying) KeyboardControls.Active = KeyboardControls.Pref;
        }

        [MenuItem(MenuPath, validate = true)]
        static bool Validate()
        {
            Menu.SetChecked(MenuPath, KeyboardControls.Pref);
            return true;
        }

        const string LapPath = "SutureTrainer/Vista laparoscópica (cavidad abdominal)";

        [MenuItem(LapPath)]
        static void ToggleLap() => LaparoscopicScene.Pref = !LaparoscopicScene.Pref;

        [MenuItem(LapPath, validate = true)]
        static bool ValidateLap()
        {
            Menu.SetChecked(LapPath, LaparoscopicScene.Pref);
            return true;
        }
    }
}
