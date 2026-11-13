using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;

namespace SutureTrainer
{
    /// <summary>
    /// Completa el menú en VR sin regenerar la escena: pasa las 6 actividades a
    /// una columna izquierda y añade a la derecha la actividad 7 y los ajustes
    /// del experimento (nivel de distractores, modo y tarea secundaria), que
    /// así se eligen con el láser del mando y no solo con el teclado.
    /// Clona los botones existentes para heredar su aspecto y su lógica.
    /// </summary>
    public static class MenuExtras
    {
        const float Columna = 0.27f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Init()
        {
            SceneManager.sceneLoaded -= AlCargar;
            SceneManager.sceneLoaded += AlCargar;
            Montar();
        }

        static void AlCargar(Scene s, LoadSceneMode m) => Montar();

        static void Montar()
        {
            if (SceneManager.GetActiveScene().name != GameFlow.MenuScene) return;
            var menu = GameObject.Find("Menu");
            if (menu == null || menu.transform.Find("Btn_Extra_7") != null) return;

            WorldButton modelo = null;
            foreach (var b in menu.GetComponentsInChildren<WorldButton>(true))
                if (b.name.StartsWith("Btn_6")) modelo = b;
            if (modelo == null) return;
            var etiquetaModelo = menu.transform.Find("Lbl_" + modelo.name.Substring(4));
            if (etiquetaModelo == null) return;

            // columna izquierda: las seis actividades existentes
            foreach (Transform t in menu.transform)
                if ((t.name.StartsWith("Btn_") || t.name.StartsWith("Lbl_")) && Mathf.Abs(t.localPosition.x) < 0.01f)
                    t.localPosition += new Vector3(-Columna, 0f, 0f);

            float y0 = 0.22f, dy = 0.13f;
            Crear(menu.transform, modelo, etiquetaModelo, "7", 0, y0, () => "7 · Sutura a dos manos",
                  b => b.sceneToLoad = GameFlow.BimanualScene, null);
            Crear(menu.transform, modelo, etiquetaModelo, "Distractores", 2, y0 - 2 * dy,
                  () => $"Distractores: {Nombre(DistractorManager.NivelPref)}", null,
                  () => DistractorManager.NivelPref = (DistractorManager.NivelPref + 1) % 4);
            Crear(menu.transform, modelo, etiquetaModelo, "Modo", 3, y0 - 3 * dy,
                  () => $"Modo: {(DistractorManager.AdaptativoPref ? "adaptativo" : "fijo")}", null,
                  () => DistractorManager.AdaptativoPref = !DistractorManager.AdaptativoPref);
            Crear(menu.transform, modelo, etiquetaModelo, "Tarea2", 4, y0 - 4 * dy,
                  () => $"Tarea secundaria: {(TareaSecundaria.Pref ? "sí" : "no")}", null,
                  () => TareaSecundaria.Pref = !TareaSecundaria.Pref);
        }

        static string Nombre(int n) => n switch { 0 => "ninguno", 1 => "leve", 2 => "medio", _ => "alto" };

        static void Crear(Transform padre, WorldButton modelo, Transform etiquetaModelo, string id, int fila, float y,
                          System.Func<string> texto, System.Action<WorldButton> config, System.Action alPulsar)
        {
            var btn = Object.Instantiate(modelo, padre);
            btn.name = "Btn_Extra_" + id;
            btn.sceneToLoad = "";
            btn.onClick = null;
            var p = modelo.transform.localPosition;
            btn.transform.localPosition = new Vector3(Columna, y, p.z);

            var lbl = Object.Instantiate(etiquetaModelo.gameObject, padre);
            lbl.name = "Lbl_Extra_" + id;
            var lp = etiquetaModelo.localPosition;
            lbl.transform.localPosition = new Vector3(Columna, y, lp.z);
            var tmp = lbl.GetComponent<TMP_Text>();
            if (tmp != null) tmp.text = texto();

            config?.Invoke(btn);
            if (alPulsar != null)
                btn.onClick = () => { alPulsar(); if (tmp != null) tmp.text = texto(); };
        }
    }
}
