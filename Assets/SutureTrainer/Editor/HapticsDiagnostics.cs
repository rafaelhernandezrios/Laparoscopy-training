using UnityEditor;
using UnityEngine;

namespace SutureTrainer.EditorTools
{
    /// <summary>
    /// Enciende de golpe TODAS las trazas del háptico y deja un informe.
    ///
    /// Existe porque los interruptores estaban repartidos y uno de ellos ni
    /// siquiera se ve: InstrumentHaptics.debugLog está en el inspector, pero
    /// TouchDevice.VerboseLog es un campo estático que no sale por ningún lado.
    /// Encender "el debug" y no ver nada no significaba que no pasara nada.
    /// </summary>
    public static class HapticsDiagnostics
    {
        const string MenuPath = "SutureTrainer/Háptico: diagnóstico";

        [MenuItem(MenuPath)]
        static void Toggle()
        {
            bool encender = !TouchDevice.VerboseLog;
            TouchDevice.VerboseLog = encender;

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
            Menu.SetChecked(MenuPath, TouchDevice.VerboseLog);
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
            sb.AppendLine($"  Touch activo  : {TouchDevice.Available}");
            sb.AppendLine($"  HapticGain    : {MasterInput.HapticGain:F2}");

            foreach (var m in Object.FindObjectsByType<MasterInput>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
                sb.AppendLine($"  {m.node,-10}    : backend {m.ActiveBackend}, tracked {m.IsTracked}");

            foreach (var h in Object.FindObjectsByType<InstrumentHaptics>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
                sb.AppendLine($"  ganancias     : outputGain {h.outputGain:F2}, " +
                              $"zona muerta {h.forceDeadzone:F3} N, tope {h.maxTotalForce:F2} N");

            if (EditorApplication.isPlaying)
            {
                Vector3 pedida = TouchDevice.LastRequested;
                Vector3 real = TouchDevice.ReadCurrentForce();
                sb.AppendLine($"  fuerza pedida : {pedida.magnitude:F3} N  {pedida}");
                sb.AppendLine($"  fuerza real   : {real.magnitude:F3} N  {real}");

                sb.AppendLine($"  via           : {(TouchDevice.UseConstantForceEffect ? "efecto de fuerza constante" : "setForce crudo")}");

                string err = TouchDevice.ReadError();
                if (!string.IsNullOrEmpty(err)) sb.AppendLine($"  error driver  : {err}");

                if (pedida.magnitude > 0.05f && real.magnitude < 0.01f)
                    sb.AppendLine("  >> Se PIDE fuerza y el aparato renderiza CERO: " +
                                  "el fallo está por debajo de Unity.");
            }
            else
            {
                sb.AppendLine("  (entra en Play y vuelve a lanzarlo para ver fuerzas)");
            }

            return sb.ToString();
        }
    }
}
