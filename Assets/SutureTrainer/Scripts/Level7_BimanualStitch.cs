using UnityEngine;

namespace SutureTrainer
{
    /// <summary>
    /// Actividad 7 — Sutura a dos manos (pinza IZQUIERDA + porta-agujas DERECHO).
    ///
    /// Reproduce el flujo de la sutura laparoscópica intracorpórea, punto a punto:
    ///   1. Cargar la aguja en el porta-agujas derecho.
    ///   2. Contratracción: la izquierda toma el borde del tejido y lo presenta
    ///      (el hígado se deforma de verdad siguiendo la pinza).
    ///   3. Clavar en la entrada con la derecha, sin soltar la contratracción.
    ///   4. Atravesar girando la muñeca derecha hasta la salida.
    ///   5. Transferir: la izquierda suelta el tejido y toma la PUNTA de la aguja.
    ///   6. Tracción: la izquierda tira y pasa el hilo.
    ///   7. Devolver la aguja a la derecha; el punto se aproxima y sigue el siguiente.
    ///
    /// Reutiliza de Level3 las dianas sobre la incisión real, el hilo, el tejido
    /// lógico y la aguja; solo cambia la secuencia y exige las dos manos.
    /// </summary>
    public class Level7_BimanualStitch : Level3_SimpleStitch
    {
        public enum Paso { CargarAguja, Contratraccion, Clavar, Atravesar, Transferir, Traccion, Devolver, Fin }

        [Header("Dos manos")]
        [Tooltip("Holgura para tomar el borde del tejido con la pinza izquierda (m).")]
        public float tissueGraspRadius = 0.016f;
        [Tooltip("Cuánto puede levantarse el labio siguiendo a la pinza (m).")]
        public float liftMax = 0.012f;
        [Tooltip("Holgura para tomar la punta de la aguja al transferirla (m).")]
        public float needleTipCatchRadius = 0.05f;
        [Tooltip("Holgura para devolver la aguja al porta-agujas (m).")]
        public float needleBodyCatchRadius = 0.035f;

        public Paso CurrentStep => paso;
        public int Transfers { get; private set; }

        protected override string LevelTitle => "Actividad 7 · Sutura a dos manos";

        Paso paso = Paso.CargarAguja;
        GameObject marcador;
        bool agarrandoTejido, lPrev, rPrev;
        int graspIdx = -1;
        Vector3 bordeRestLocal;
        int colaAlPinar;

        bool RightHolds => needle != null && needle.Grasp.IsHeld && needle.Grasp.Holder == rightArm;
        bool LeftHolds => needle != null && needle.Grasp.IsHeld && needle.Grasp.Holder == leftArm;

        protected override void Setup()
        {
            base.Setup();
            marcador = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            marcador.name = "BordeTejido";
            Destroy(marcador.GetComponent<Collider>());
            marcador.transform.SetParent(fieldRoot, false);
            marcador.transform.localScale = Vector3.one * 0.008f;
            var mr = marcador.GetComponent<MeshRenderer>();
            if (MaterialSet.I != null && MaterialSet.I.markerIdle != null)
            {
                mr.material = new Material(MaterialSet.I.markerIdle);
                if (mr.material.HasProperty("_BaseColor")) mr.material.SetColor("_BaseColor", new Color(1f, 0.85f, 0.15f));
                if (mr.material.HasProperty("_Color")) mr.material.SetColor("_Color", new Color(1f, 0.85f, 0.15f));
            }
            IniciarPunto(0);
        }

        /// <summary>Dónde se toma el labio: borde de la herida, del lado de la entrada.</summary>
        public Vector3 PuntoBorde(int i) =>
            Vector3.Lerp(entries[i].transform.position, exits[i].transform.position, 0.3f);

        public Vector3 MarcadorPos => marcador != null ? marcador.transform.position : Vector3.zero;

        void IniciarPunto(int i)
        {
            ActivatePair(i);
            marcador.SetActive(true);
            marcador.transform.position = PuntoBorde(i);
            paso = RightHolds ? Paso.Contratraccion : Paso.CargarAguja;
            SetObjective(paso == Paso.CargarAguja
                ? "Toma la aguja con el porta-agujas DERECHO."
                : $"Punto {i + 1}/{stitchPairs}: con la pinza IZQUIERDA toma el borde del tejido (marca amarilla).");
        }

        protected override void Tick()
        {
            if (needle == null || leftArm == null || rightArm == null) return;

            bool lC = leftArm.JawCloseness > 0.72f, rC = rightArm.JawCloseness > 0.72f;
            bool lFlanco = lC && !lPrev, rFlanco = rC && !rPrev;
            lPrev = lC; rPrev = rC;

            if (agarrandoTejido)
            {
                if (leftArm.JawCloseness < 0.35f || LeftHolds) SoltarTejido();
                else ActualizarTejido();
            }

            Vector3 tip = needle.TipPos;
            switch (paso)
            {
                case Paso.CargarAguja:
                    if (RightHolds)
                    {
                        paso = Paso.Contratraccion;
                        SetObjective($"Punto {currentPair + 1}/{stitchPairs}: con la pinza IZQUIERDA toma el borde del tejido (marca amarilla).");
                    }
                    break;

                case Paso.Contratraccion:
                    if (!RightHolds) { paso = Paso.CargarAguja; SetObjective("Toma la aguja con el porta-agujas DERECHO."); break; }
                    if (!agarrandoTejido && lFlanco &&
                        Vector3.Distance(leftArm.TipPos, marcador.transform.position) < tissueGraspRadius)
                        AgarrarTejido();
                    if (agarrandoTejido)
                    {
                        paso = Paso.Clavar;
                        SetObjective("Mantén la contratracción y clava la aguja en la diana de ENTRADA (verde).");
                    }
                    break;

                case Paso.Clavar:
                    if (!agarrandoTejido)
                    {
                        paso = Paso.Contratraccion;
                        SetObjective("Vuelve a tomar el borde del tejido con la IZQUIERDA antes de clavar.");
                        break;
                    }
                    if (RightHolds && entries[currentPair].TipInside(tip) && needle.TipDir.y < entryTipTolerance)
                    {
                        Metrics.AddPrecisionSample(entries[currentPair].Deviation(tip));
                        entries[currentPair].SetState(PunctureTarget.State.Done);
                        if (organ != null && currentPair < organ.PairCount) organ.Suture(organ.pairs[currentPair].entry);
                        exits[currentPair].SetState(PunctureTarget.State.Active);
                        tissue.punctureAllowed = true;
                        AudioFX.Pop();
                        paso = Paso.Atravesar;
                        SetObjective("Gira la muñeca DERECHA siguiendo la curva de la aguja hasta salir por la diana de SALIDA.");
                    }
                    break;

                case Paso.Atravesar:
                    if (RightHolds && exits[currentPair].TipInside(tip))
                    {
                        Metrics.AddPrecisionSample(exits[currentPair].Deviation(tip));
                        exits[currentPair].SetState(PunctureTarget.State.Done);
                        if (organ != null && currentPair < organ.PairCount) organ.Suture(organ.pairs[currentPair].exit);
                        thread.PinThroughStitch(exits[currentPair].transform.position, entries[currentPair].transform.position);
                        colaAlPinar = thread.RemainingTailSegments;
                        tissue.punctureAllowed = false;
                        AudioFX.Pop();
                        marcador.SetActive(false);
                        paso = Paso.Transferir;
                        SetObjective("Suelta el tejido y toma la PUNTA de la aguja con la pinza IZQUIERDA.");
                    }
                    break;

                case Paso.Transferir:
                    if (lFlanco && !agarrandoTejido &&
                        Vector3.Distance(leftArm.TipPos, needle.TipPos) < needleTipCatchRadius)
                    {
                        leftArm.ForceGrab(needle.Grasp, encajar: false);   // se toma por donde se agarra
                        Transfers++;
                        FlashInfo("Aguja transferida a la izquierda");
                        paso = Paso.Traccion;
                        SetObjective("Tira con la IZQUIERDA siguiendo la curva para pasar el hilo.");
                    }
                    break;

                case Paso.Traccion:
                    if (colaAlPinar - thread.RemainingTailSegments >= pullSegments)
                    {
                        paso = Paso.Devolver;
                        SetObjective("Devuelve la aguja al porta-agujas DERECHO.");
                    }
                    break;

                case Paso.Devolver:
                    if (rFlanco && !RightHolds && CercaDeAguja(rightArm, needleBodyCatchRadius))
                    {
                        rightArm.ForceGrab(needle.Grasp, encajar: true);
                        Transfers++;
                        Vector3 mid = (entries[currentPair].transform.position + exits[currentPair].transform.position) * 0.5f;
                        thread.CinchTo(mid);
                        if (organ != null) organ.Tighten();
                        FlashInfo($"Punto {currentPair + 1}/{stitchPairs} completado · {Transfers} transferencias");
                        if (currentPair + 1 >= stitchPairs) { paso = Paso.Fin; Finish(); }
                        else IniciarPunto(currentPair + 1);
                    }
                    break;
            }
        }

        bool CercaDeAguja(RoboticArm brazo, float radio)
        {
            var col = needle.GetComponent<Collider>();
            Vector3 p = col != null ? col.ClosestPoint(brazo.TipPos) : needle.transform.position;
            return Vector3.Distance(p, brazo.TipPos) < radio;
        }

        // ---------------- contratracción sobre la malla deformable ----------------

        void AgarrarTejido()
        {
            agarrandoTejido = true;
            leftArm.master?.Haptic(0.4f, 0.05f);
            AudioFX.Click();
            var dm = organ != null ? organ.deformable : null;
            var sm = organ != null ? organ.sutureManager : null;
            if (dm == null || sm == null || dm.masterVertices == null || dm.masterVertices.Count == 0) return;

            Vector3 local = dm.transform.InverseTransformPoint(marcador.transform.position);
            int mejor = -1; float mejorD = float.MaxValue;
            for (int i = 0; i < dm.masterVertices.Count; i++)
            {
                if (dm.masterVertices[i].isFixed) continue;
                float d = (dm.masterVertices[i].position - local).sqrMagnitude;
                if (d < mejorD) { mejorD = d; mejor = i; }
            }
            if (mejor < 0) return;
            bordeRestLocal = dm.masterVertices[mejor].position;
            sm.sutureVertices.Add(bordeRestLocal);
            graspIdx = sm.sutureVertices.Count - 1;
            dm.SutureVertices(mejor, graspIdx, 2);
        }

        void ActualizarTejido()
        {
            if (graspIdx < 0 || organ == null) return;
            var dm = organ.deformable; var sm = organ.sutureManager;
            if (dm == null || sm == null || graspIdx >= sm.sutureVertices.Count) return;
            Vector3 restWorld = dm.transform.TransformPoint(bordeRestLocal);
            Vector3 d = Vector3.ClampMagnitude(leftArm.TipPos - restWorld, liftMax);
            sm.sutureVertices[graspIdx] = bordeRestLocal + dm.transform.InverseTransformVector(d);
        }

        void SoltarTejido()
        {
            agarrandoTejido = false;
            if (graspIdx >= 0 && organ != null && organ.deformable != null)
                organ.deformable.ReleaseSuture(graspIdx);
            graspIdx = -1;
        }
    }
}
