using UnityEditor;
using UnityEngine;

namespace SutureTrainer.EditorTools
{
    /// <summary>
    /// Enciende de golpe TODAS las trazas del háptico y deja un informe.
    ///
    /// Existe porque los interruptores estaban repartidos y uno de ellos ni
    /// siquiera se ve: InstrumentHaptics.debugLog está en el inspector, pero
    /// TouchDevice.VerboseLog vive en el propio aparato y no sale por ningún
    /// lado. Encender "el debug" y no ver nada no significaba que no pasara
    /// nada. Con dos robots además hay que verlos por separado.
    /// </summary>
    public static class HapticsDiagnostics
    {
        const string MenuPath = "SutureTrainer/Háptico: diagnóstico";

        [MenuItem(MenuPath)]
        static void Toggle()
        {
            bool encender = true;
            foreach (var d in TouchDevice.Todos) { encender = !d.VerboseLog; break; }
            foreach (var d in TouchDevice.Todos) d.VerboseLog = encender;

            int n = 0;
            foreach (var h in Object.FindObjectsByType<InstrumentHaptics>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                h.debugLog = encender;
                n++;
            }

            if (!encender)
            {
                Debug.Log("[Diag] Trazas del háptico apagadas.");
                return;
            }

            Debug.Log($"[Diag] Trazas encendidas en {n} InstrumentHaptics + TouchDevice.\n" +
                      Informe());
        }

        [MenuItem(MenuPath, validate = true)]
        static bool Validate()
        {
            bool alguno = false;
            foreach (var d in TouchDevice.Todos) if (d.VerboseLog) { alguno = true; break; }
            Menu.SetChecked(MenuPath, alguno);
            return true;
        }

        /// <summary>
        /// Foto del estado de la cadena de fuerza. El dato que decide es la
        /// comparación pedida/renderizada: si se pide fuerza y el aparato
        /// devuelve cero, el problema está en el dispositivo o en la llamada
        /// nativa, no en el modelo de fuerzas del juego.
        /// </summary>
        static string Informe()
        {
            var sb = new System.Text.StringBuilder();

            sb.AppendLine($"  play          : {EditorApplication.isPlaying}");
            sb.AppendLine($"  HapticGain    : {MasterInput.HapticGain:F2}");

            foreach (var m in Object.FindObjectsByType<MasterInput>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
                sb.AppendLine($"  {m.node,-10}    : backend {m.ActiveBackend}, tracked {m.IsTracked}, " +
                              $"aparato \"{m.touchDeviceName}\"");

            foreach (var h in Object.FindObjectsByType<InstrumentHaptics>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
                sb.AppendLine($"  ganancias     : outputGain {h.outputGain:F2}, " +
                              $"zona muerta {h.forceDeadzone:F3} N, tope {h.maxTotalForce:F2} N");

            if (!EditorApplication.isPlaying)
            {
                sb.AppendLine("  (entra en Play y vuelve a lanzarlo para ver fuerzas)");
                return sb.ToString();
            }

            bool ninguno = true;
            foreach (var d in TouchDevice.Todos)
            {
                ninguno = false;
                Vector3 pedida = d.LastRequested;
                Vector3 real = d.ReadCurrentForce();

                sb.AppendLine($"  --- \"{d.DeviceName}\" (activo: {d.Available})");
                sb.AppendLine($"      pedida    : {pedida.magnitude:F3} N  {pedida}");
                sb.AppendLine($"      real      : {real.magnitude:F3} N  {real}");
                sb.AppendLine($"      via       : {(d.UseConstantForceEffect ? "efecto de fuerza constante" : "setForce crudo")}");

                string err = d.ReadError();
                if (!string.IsNullOrEmpty(err)) sb.AppendLine($"      error     : {err}");

                if (pedida.magnitude > 0.05f && real.magnitude < 0.01f)
                    sb.AppendLine("      >> Se PIDE fuerza y renderiza CERO: el fallo " +
                                  "está por debajo de Unity.");
            }

            if (ninguno)
                sb.AppendLine("  Ningún aparato háptico en uso todavía. Conmuta a Touch " +
                              "(F1 derecha / F2 izquierda) y vuelve a lanzarlo.");

            return sb.ToString();
        }
    }
}
