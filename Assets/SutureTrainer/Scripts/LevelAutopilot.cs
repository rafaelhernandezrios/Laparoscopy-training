using System;
using System.Collections;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR;

namespace SutureTrainer
{
    /// <summary>
    /// PRUEBA AUTOMÁTICA de las actividades 1-5 (modo teclado).
    ///
    /// Como SutureAutopilot, conduce las pinzas por el MISMO camino que el
    /// teclado (MasterInput.KbNudge / KbRotate / KbJawOverride), así que la
    /// detección la hace el propio nivel: si el piloto completa el ejercicio,
    /// la lógica del nivel funciona. Lee el estado privado de los niveles por
    /// reflexión, sin modificarlos.
    ///
    /// P = arrancar/parar · Shift+P = arrancar y grabar vídeo.
    /// </summary>
    public class LevelAutopilot : MonoBehaviour
    {
        public float speed = 0.07f;      // m/s
        public float rotSpeed = 90f;     // grados/s

        public static LevelAutopilot Running { get; private set; }

        TrainingLevel _lvl;
        RoboticArm _r, _l;
        SutureNeedle _needle;
        bool _grabar;

        /// <summary>false si no hay un nivel en curso (la escena del órgano usa SutureAutopilot).</summary>
        public static bool Toggle(bool grabar)
        {
            if (Running != null) { Running.Fin("parado con P"); return true; }
            TrainingLevel lvl = null;
            foreach (var t in FindObjectsByType<TrainingLevel>())
                if (t.enabled && !t.Finished) { lvl = t; break; }
            if (lvl == null) return false;
            var ap = new GameObject("[LevelAutopilot]").AddComponent<LevelAutopilot>();
            ap._lvl = lvl;
            ap._grabar = grabar;
            return true;
        }

        static object F(object o, string nombre)
        {
            for (var t = o.GetType(); t != null; t = t.BaseType)
            {
                var f = t.GetField(nombre, BindingFlags.NonPublic | BindingFlags.Public |
                                            BindingFlags.Instance | BindingFlags.DeclaredOnly);
                if (f != null) return f.GetValue(o);
            }
            return null;
        }

        MasterInput MR => _r.master;
        MasterInput ML => _l.master;
        bool Sostiene => _needle != null && _needle.Grasp.IsHeld && _needle.Grasp.Holder == _r;

        IEnumerator Start()
        {
            Running = this;
            _r = _lvl.rightArm;
            _l = _lvl.leftArm;
            KeyboardControls.ActiveHand = XRNode.RightHand;
            yield return null;
            _needle = F(_lvl, "needle") as SutureNeedle;

            string escena = SceneManager.GetActiveScene().name;
            Debug.Log($"[PilotoNivel] Inicio en {escena} ({_lvl.GetType().Name}).");
            if (_grabar)
                VideoRecorder.Begin((escena.Length > 5 ? escena.Substring(0, 5) : escena) +
                                    (LaparoscopicScene.Centro.HasValue ? "_laparoscopica" : "_anterior"));
            yield return new WaitForSeconds(1.2f);   // plano inicial

            if (_lvl is Level5_KnotTying l5) yield return Nudo(l5);
            else if (_lvl is Level3_SimpleStitch l3) yield return Puntos(l3);
            else if (_lvl is Level2_RingPass l2) yield return Anillos(l2);
            else if (_lvl is Level1_NeedleHandling l1) yield return Poses(l1);

            Debug.Log($"[PilotoNivel] Ejercicio {(_lvl.Finished ? "COMPLETADO" : "sin terminar")} en {escena}.");
            yield return new WaitForSeconds(3f);      // plano final
            Fin("terminado");
        }

        void Fin(string motivo)
        {
            if (_r != null) MR.KbJawOverride = -1f;
            if (_l != null) ML.KbJawOverride = -1f;
            VideoRecorder.End();
            Debug.Log($"[PilotoNivel] Fin ({motivo}).");
            if (Running == this) Running = null;
            Destroy(gameObject);
        }

        void OnDestroy() { if (Running == this) { Running = null; VideoRecorder.End(); } }

        // ------------------------------------------------------------ primitivas

        IEnumerator Ir(MasterInput m, Func<Vector3> actual, Func<Vector3> obj,
                       float tol = 0.002f, float tmax = 6f, float vel = -1f, Func<bool> parar = null)
        {
            float v = vel > 0f ? vel : speed, t = 0f;
            while (t < tmax)
            {
                if (parar != null && parar()) yield break;
                Vector3 d = obj() - actual();
                if (d.magnitude < tol) yield break;
                m.KbNudge(Vector3.ClampMagnitude(d, v * Time.deltaTime));
                t += Time.deltaTime;
                yield return null;
            }
        }

        IEnumerator Pinza(MasterInput m, float cierre, float espera)
        {
            m.KbJawOverride = cierre;
            yield return new WaitForSeconds(espera);
        }

        /// <summary>Gira la muñeca para llevar una dirección del instrumento a otra.</summary>
        void GirarHacia(MasterInput m, Vector3 actual, Vector3 deseada)
        {
            Quaternion d = Quaternion.FromToRotation(actual, deseada);
            d.ToAngleAxis(out float ang, out Vector3 eje);
            if (ang > 180f) ang -= 360f;
            if (Mathf.Abs(ang) < 0.5f || eje.sqrMagnitude < 1e-6f) return;
            m.KbRotate(Quaternion.AngleAxis(Mathf.Sign(ang) * Mathf.Min(Mathf.Abs(ang), rotSpeed * Time.deltaTime), eje));
        }

        IEnumerator AgarrarAguja()
        {
            if (_needle == null) yield break;
            for (int intento = 0; intento < 4 && !Sostiene; intento++)
            {
                yield return Pinza(MR, 0f, 0.35f);
                var col = _needle.GetComponent<Collider>();
                yield return Ir(MR, () => _r.TipPos,
                                () => col != null ? col.ClosestPoint(_r.TipPos) : _needle.transform.position, 0.004f, 8f);
                yield return Pinza(MR, 1f, 0.6f);
            }
            if (!Sostiene) { Debug.LogWarning("[PilotoNivel] No pudo agarrar la aguja."); yield break; }
            Debug.Log("[PilotoNivel] Aguja agarrada.");
            Vector3 arriba = _r.TipPos + Vector3.up * 0.06f;
            yield return Ir(MR, () => _r.TipPos, () => arriba, 0.004f, 3f);
        }

        // ------------------------------------------------------------ nivel 1

        IEnumerator Poses(Level1_NeedleHandling l1)
        {
            yield return AgarrarAguja();
            for (int i = 0; i < l1.repetitions && !_lvl.Finished; i++)
            {
                int antes = (int)F(l1, "done");
                float t = 0f;
                while (!_lvl.Finished && (int)F(l1, "done") == antes && t < 20f)
                {
                    var ghost = F(l1, "ghost") as Transform;
                    if (ghost == null) break;
                    // la aguja es simétrica: vale la pose o la girada 180°
                    Quaternion g = ghost.rotation, g2 = g * Quaternion.Euler(0f, 180f, 0f);
                    Quaternion meta = Quaternion.Angle(_needle.transform.rotation, g) <=
                                      Quaternion.Angle(_needle.transform.rotation, g2) ? g : g2;
                    Quaternion d = meta * Quaternion.Inverse(_needle.transform.rotation);
                    d.ToAngleAxis(out float ang, out Vector3 eje);
                    if (ang > 180f) ang -= 360f;
                    if (Mathf.Abs(ang) > 0.5f && eje.sqrMagnitude > 1e-6f)
                        MR.KbRotate(Quaternion.AngleAxis(Mathf.Sign(ang) * Mathf.Min(Mathf.Abs(ang), rotSpeed * Time.deltaTime), eje));
                    MR.KbNudge(Vector3.ClampMagnitude(ghost.position - _needle.transform.position, speed * Time.deltaTime));
                    t += Time.deltaTime;
                    yield return null;
                }
                Debug.Log($"[PilotoNivel] Pose {i + 1}/{l1.repetitions} {((int)F(l1, "done") > antes || _lvl.Finished ? "conseguida" : "NO conseguida")}.");
            }
        }

        // ------------------------------------------------------------ nivel 2

        IEnumerator Anillos(Level2_RingPass l2)
        {
            yield return AgarrarAguja();
            var rings = F(l2, "rings") as RingTarget[];
            if (rings == null) yield break;
            for (int i = 0; i < rings.Length && !_lvl.Finished; i++)
            {
                var ring = rings[i];
                Vector3 c = ring.transform.position, n = ring.transform.forward;
                float lado = Mathf.Sign(Vector3.Dot(_needle.TipPos - c, n));
                if (lado == 0f) lado = 1f;
                Vector3 a = c + n * (0.05f * lado), b = c - n * (0.05f * lado);
                yield return Ir(MR, () => _needle.TipPos, () => a, 0.003f, 8f);
                yield return Ir(MR, () => _needle.TipPos, () => b, 0.003f, 6f, 0.05f, () => ring.IsDone);
                yield return new WaitForSeconds(0.3f);
                Debug.Log($"[PilotoNivel] Anillo {i + 1}: {(ring.IsDone ? "pasado" : "NO pasado")}.");
            }
        }

        // ------------------------------------------------------------ niveles 3-5

        IEnumerator Puntos(Level3_SimpleStitch l3)
        {
            yield return AgarrarAguja();
            for (int i = 0; i < l3.stitchPairs && !_lvl.Finished; i++)
                yield return Puntada(l3, i);
        }

        IEnumerator Puntada(Level3_SimpleStitch l3, int i)
        {
            var entries = F(l3, "entries") as PunctureTarget[];
            var exits = F(l3, "exits") as PunctureTarget[];
            if (entries == null || i >= entries.Length) yield break;
            var e = entries[i];
            var x = exits[i];
            Func<Vector3> E = () => e.transform.position;
            Func<Vector3> X = () => x.transform.position;

            // 1) encima de la entrada, orientando la punta hacia abajo y hacia la salida
            float t = 0f;
            while (t < 8f)
            {
                Vector3 deseada = ((X() - E()).normalized * 0.5f + Vector3.down).normalized;
                GirarHacia(MR, _needle.TipDir, deseada);
                Vector3 d = E() + Vector3.up * 0.025f - _needle.TipPos;
                MR.KbNudge(Vector3.ClampMagnitude(d, speed * Time.deltaTime));
                if (d.magnitude < 0.003f && Vector3.Angle(_needle.TipDir, deseada) < 8f) break;
                t += Time.deltaTime;
                yield return null;
            }

            // 2) entrar
            yield return Ir(MR, () => _needle.TipPos, E, 0.002f, 4f, 0.04f,
                            () => e.CurrentState == PunctureTarget.State.Done);
            yield return new WaitForSeconds(0.2f);
            if (e.CurrentState != PunctureTarget.State.Done)
                Debug.LogWarning($"[PilotoNivel] Entrada {i} no registrada (inclinación {_needle.TipDir.y:F2}).");

            // 3) atravesar por debajo de la superficie hasta la salida
            Vector3 medio = (E() + X()) * 0.5f + Vector3.down * 0.01f;
            yield return Ir(MR, () => _needle.TipPos, () => medio, 0.003f, 3f, 0.04f);
            yield return Ir(MR, () => _needle.TipPos, X, 0.002f, 4f, 0.04f,
                            () => x.CurrentState == PunctureTarget.State.Done);
            yield return new WaitForSeconds(0.2f);

            // 4) tirar del hilo hasta que el nivel dé el punto por terminado
            // El hilo solo corre por los agujeros con el tramo TENSO (distancia
            // aguja-punto mayor que el hilo entre ellos, ~13 cm y creciendo), así
            // que se tira lejos y se sostiene hasta que el nivel cuenta el punto.
            Func<bool> pasado = () => _lvl.Finished || (int)F(l3, "phase") != 2;   // Phase.PullThrough
            // Adaptativo: se aleja mientras el hilo no esté tenso y se sostiene
            // cuando lo está (en la sutura continua el cabo de la aguja crece
            // con cada punto, así que la distancia necesaria también).
            var hilo = F(l3, "thread") as ThreadBase;
            Vector3 dir = (Vector3.up + Vector3.right * 0.6f).normalized;
            float dist = 0.12f, t4 = 0f;
            while (!pasado() && t4 < 15f)
            {
                bool tenso = hilo != null && hilo.Tension >= 1.08f;
                if (!tenso) dist = Mathf.Min(dist + 0.10f * Time.deltaTime, 0.50f);
                Vector3 lejos = X() + dir * dist;
                MR.KbNudge(Vector3.ClampMagnitude(lejos - _needle.TipPos, 0.12f * Time.deltaTime));
                t4 += Time.deltaTime;
                yield return null;
            }
            // volver cerca del campo para el siguiente punto
            Vector3 vuelta = X() + Vector3.up * 0.06f;
            yield return Ir(MR, () => _needle.TipPos, () => vuelta, 0.005f, 5f, 0.10f);
            Debug.Log($"[PilotoNivel] Punto {i + 1}: {(pasado() ? "completado" : "hilo sin pasar")}.");
        }

        // ------------------------------------------------------------ nivel 5

        string FaseNudo(Level5_KnotTying l5) => F(l5, "knotPhase")?.ToString() ?? "";

        IEnumerator Nudo(Level5_KnotTying l5)
        {
            yield return Puntos(l5);          // un punto simple primero

            // Máquina de estados guiada por la fase del propio nivel: así se
            // recupera sola si algo se suelta o una fase tarda más de la cuenta.
            var hilo = F(l5, "thread") as ThreadBase;
            Vector3 casaIzq = _l.TipPos;      // donde empieza la pinza izquierda
            string previa = "";
            float tFase = 0f, tTotal = 0f, ang = 0f;
            Vector3 rObj = Vector3.zero, lObj = Vector3.zero;

            while (!_lvl.Finished && tTotal < 150f)
            {
                string fase = FaseNudo(l5);
                if (fase != previa)
                {
                    Debug.Log($"[PilotoNivel] Nudo: fase {fase} (lazada {(int)F(l5, "currentThrow") + 1}/3).");
                    previa = fase; tFase = 0f;
                    if (fase == "Wrapping") ML.KbJawOverride = 0f;   // soltar la cola para enrollar
                    if (fase == "Pull")
                    {
                        rObj = _r.TipPos + new Vector3(0.16f, 0.07f, 0f);
                        lObj = _l.TipPos + new Vector3(-0.16f, 0.07f, 0f);
                    }
                }
                float dt = Time.deltaTime;

                switch (fase)
                {
                    case "Wrapping":
                    {
                        // pinza izquierda de vuelta al campo, quieta
                        ML.KbNudge(Vector3.ClampMagnitude(casaIzq - _l.TipPos, 0.10f * dt));
                        if (tFase < 0.8f) break;
                        // girar la COLA de la aguja (donde va el hilo) alrededor de la pinza izquierda
                        Vector3 o = _l.wrist.position;
                        Vector3 eje = (_l.wrist.position - _l.trocar.position).normalized;
                        Vector3 u = Vector3.Cross(eje, Vector3.up).normalized;
                        if (u.sqrMagnitude < 0.5f) u = Vector3.Cross(eje, Vector3.right).normalized;
                        Vector3 v = Vector3.Cross(eje, u);
                        ang += 150f * Mathf.Deg2Rad * dt;
                        Vector3 obj = o + (u * Mathf.Cos(ang) + v * Mathf.Sin(ang)) * 0.05f;
                        MR.KbNudge(Vector3.ClampMagnitude(obj - _needle.TailPos, 0.20f * dt));
                        break;
                    }
                    case "GrabTail":
                    {
                        var cola = GameObject.Find("ThreadTail");
                        if (cola == null) break;
                        float d = Vector3.Distance(_l.TipPos, cola.transform.position);
                        // abrir, acercarse, cerrar; si falla, volver a abrir (el agarre es en el flanco)
                        if (d > 0.006f || tFase % 2.5f < 0.4f) ML.KbJawOverride = 0f;
                        else ML.KbJawOverride = 1f;
                        ML.KbNudge(Vector3.ClampMagnitude(cola.transform.position - _l.TipPos, 0.10f * dt));
                        break;
                    }
                    case "Pull":
                    {
                        // separar las pinzas hasta que el hilo quede tenso y la lazada cinche
                        bool tenso = hilo != null && hilo.Tension > 1.05f &&
                                     Vector3.Distance(_l.TipPos, _r.TipPos) > 0.24f;
                        if (!tenso && tFase > 3f)
                        {
                            rObj += new Vector3(0.03f, 0.01f, 0f) * dt;
                            lObj += new Vector3(-0.03f, 0.01f, 0f) * dt;
                        }
                        MR.KbNudge(Vector3.ClampMagnitude(rObj - _r.TipPos, 0.08f * dt));
                        ML.KbNudge(Vector3.ClampMagnitude(lObj - _l.TipPos, 0.08f * dt));
                        break;
                    }
                }
                tFase += dt; tTotal += dt;
                yield return null;
            }
            ML.KbJawOverride = 0f;
        }
    }
}
