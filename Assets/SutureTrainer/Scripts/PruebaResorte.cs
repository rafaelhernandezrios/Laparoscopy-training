using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;

namespace SutureTrainer
{
    /// <summary>
    /// Calibración segura del resorte del aparato (F11). Averigua en qué
    /// unidades trabaja setSpringValues midiendo con getCurrentForce:
    ///   1. Ancla = posición actual, magnitud mínima: la fuerza debe ser ~0.
    ///      Si no lo es, el ancla no está en el marco esperado -> se apaga.
    ///   2. Ancla desplazada 10 mm en cada eje: fuerza / 10 mm = rigidez real
    ///      por unidad de magnitud, y el signo dice si el marco coincide.
    /// Todo con magnitudes pequeñas y apagado inmediato ante cualquier rareza.
    /// </summary>
    public class PruebaResorte : MonoBehaviour
    {
        public float magnitud = 0.02f;
        public float fuerzaMaxSegura = 0.4f;

        /// <summary>Rigidez medida (N/mm) por unidad de magnitud. 0 = sin calibrar.</summary>
        public static float RigidezPorUnidad;

        public static void Lanzar()
        {
            if (FindAnyObjectByType<PruebaResorte>() != null) return;
            new GameObject("[PruebaResorte]").AddComponent<PruebaResorte>();
        }

        MasterInput _m;
        Vector3 _base;
        InstrumentHaptics _haptica;
        bool _hapticaEstaba;

        IEnumerator Start()
        {
            foreach (var m in FindObjectsByType<MasterInput>(FindObjectsSortMode.None))
                if (m.node == XRNode.RightHand) _m = m;
            if (_m == null) { Fin("sin MasterInput derecho"); yield break; }
            if (KeyboardControls.Active) KeyboardControls.Active = false;
            _haptica = ReproduccionHaptica.HapticaDe(_m);
            if (_haptica != null) { _hapticaEstaba = _haptica.enabled; _haptica.enabled = false; }
            _m.FuerzaDePrueba(Vector3.zero);   // ninguna otra fuerza mientras se mide
            _m.Touch.Reintentar();
            if (_m.ActiveBackend != MasterInput.Backend.TouchHaptic) _m.SetBackend(MasterInput.Backend.TouchHaptic);
            for (int i = 0; i < 10; i++) yield return null;
            if (!_m.Touch.Available) { Fin("el Touch no responde"); yield break; }

            var t = _m.Touch;
            Debug.Log("[Resorte] Calibración: deja el stylus FUERA de la base y quieto.");
            yield return new WaitForSecondsRealtime(1.5f);

            // 0) fuerza de base con el resorte apagado (contacto, efectos previos)
            t.SetSpringRaw(t.RawPos, 0f);
            yield return new WaitForSecondsRealtime(0.3f);
            Vector3 fb = Vector3.zero;
            for (int k = 0; k < 10; k++) { yield return null; fb += t.ReadCurrentForce(); }
            fb /= 10f;
            Debug.Log($"[Resorte] Fuerza de base (resorte apagado): {fb:F3} N, pos {t.RawPos:F1} mm");
            _base = fb;

            // 1) ancla en la posición actual
            Vector3 p0 = t.RawPos;
            if (!t.SetSpringRaw(p0, magnitud)) { Fin("HapticsDirect no exporta setSpringValues"); yield break; }
            Vector3 f0 = Vector3.zero;
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 0.25f)   // vigila cada frame: se apaga al instante
            {
                yield return null;
                f0 = t.ReadCurrentForce() - _base;
                if (f0.magnitude > fuerzaMaxSegura) break;
            }
            Debug.Log($"[Resorte] Ancla en la posición actual {p0:F1} mm, magnitud {magnitud}: fuerza {f0:F3} N (|F|={f0.magnitude:F3})");
            if (f0.magnitude > fuerzaMaxSegura) { Apagar(); Fin($"fuerza {f0.magnitude:F2} N con el ancla en sitio: el marco del ancla no es el de getPosition"); yield break; }

            // 2) ancla desplazada 10 mm por eje
            float kSuma = 0f; int kN = 0; string signos = "";
            Vector3[] ejes = { Vector3.right, Vector3.up, Vector3.forward };
            for (int i = 0; i < 3; i++)
            {
                Vector3 pi = t.RawPos;
                t.SetSpringRaw(pi + ejes[i] * 10f, magnitud);
                Vector3 fi = Vector3.zero;
                for (int k = 0; k < 3; k++)                       // ~3 frames, antes de que se mueva mucho
                {
                    yield return null;
                    fi = t.ReadCurrentForce() - _base;
                    if (fi.magnitude > fuerzaMaxSegura * 3f) break;
                }
                if (fi.magnitude <= fuerzaMaxSegura * 3f) yield return new WaitForSecondsRealtime(0.2f);
                Vector3 di = t.RawPos - pi;
                t.SetSpringRaw(t.RawPos, magnitud);
                float comp = Vector3.Dot(fi, ejes[i]);
                Debug.Log($"[Resorte] Eje {"xyz"[i]}: fuerza {fi:F3} N (componente {comp:+0.000;-0.000}), se movió {di:F1} mm");
                if (fi.magnitude > fuerzaMaxSegura * 3f) { Apagar(); Fin("fuerza demasiado alta: magnitud no es N/mm"); yield break; }
                if (Mathf.Abs(comp) > 0.005f) { kSuma += Mathf.Abs(comp) / 10f; kN++; }
                signos += comp >= 0 ? "+" : "-";
                yield return new WaitForSecondsRealtime(0.3f);
            }
            Apagar();
            RigidezPorUnidad = kN > 0 ? kSuma / kN / magnitud : 0f;
            Debug.Log($"[Resorte] Resultado: signos {signos} (deben ser +++), rigidez {kSuma / Mathf.Max(1, kN):F4} N/mm con magnitud {magnitud} " +
                      $"=> {RigidezPorUnidad:F3} N/mm por unidad de magnitud.");
            Fin("calibración completada");
        }

        void Apagar() { if (_m != null) _m.Touch.SetSpringRaw(_m.Touch.RawPos, 0f); }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame) { Apagar(); Fin("parada con Esc"); }
        }

        void OnDestroy()
        {
            Apagar();
            if (_m != null) _m.FuerzaDePrueba(null);
            if (_haptica != null) _haptica.enabled = _hapticaEstaba;
        }

        void Fin(string msg)
        {
            Debug.Log("[Resorte] " + msg);
            var hud = FindAnyObjectByType<HUD>();
            if (hud != null) hud.Flash("Resorte: " + msg, new Color(0.5f, 0.85f, 1f));
            StopAllCoroutines();
            Destroy(gameObject);
        }
    }
}
