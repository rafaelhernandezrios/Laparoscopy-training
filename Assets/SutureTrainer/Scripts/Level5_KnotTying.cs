using UnityEngine;

namespace SutureTrainer
{
    /// <summary>
    /// Nivel 5 — Nudo intracorpóreo: tras un punto simple, enrollar el hilo
    /// alrededor del instrumento izquierdo, agarrar la cola y tirar. 3 lazadas
    /// (2 vueltas + 1 + 1 = nudo de cirujano cuadrado).
    /// </summary>
    public class Level5_KnotTying : Level3_SimpleStitch
    {
        protected override string LevelTitle => "Nivel 5 · Nudo intracorpóreo";

        readonly int[] wrapsPerThrow = { 2, 1, 1 };
        int currentThrow;
        int wrapsDone;
        float wrapAngleAcc;
        Vector3 lastRadial;
        bool hasRadial;

        enum KnotPhase { None, Wrapping, GrabTail, Pull }
        KnotPhase knotPhase = KnotPhase.None;

        Graspable tailGrab;
        Transform tailAnchorFollower;

        void Awake()
        {
            stitchPairs = 1;
            cinchAfterEachStitch = false;
        }

        protected override void OnAllStitchesDone()
        {
            // en lugar de terminar, comienza la fase de nudo
            BeginThrow(0);
        }

        void BeginThrow(int t)
        {
            currentThrow = t;
            wrapsDone = 0;
            wrapAngleAcc = 0f;
            hasRadial = false;
            knotPhase = KnotPhase.Wrapping;
            int need = wrapsPerThrow[t];
            SetObjective($"Lazada {t + 1}/3: enrolla el hilo {need} vuelta{(need > 1 ? "s" : "")} alrededor del instrumento IZQUIERDO (mueve la aguja en círculos alrededor de él).");
            EnsureTailGraspable();
        }

        void EnsureTailGraspable()
        {
            if (tailGrab != null) return;
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "ThreadTail";
            go.transform.SetParent(fieldRoot, false);
            go.transform.localScale = Vector3.one * 0.018f;
            var col = go.GetComponent<SphereCollider>();
            col.radius = 1.4f; // radio de agarre generoso
            var mr = go.GetComponent<MeshRenderer>();
            if (MaterialSet.I != null) mr.sharedMaterial = MaterialSet.I.markerExit;
            tailGrab = go.AddComponent<Graspable>();
            tailGrab.useGravityOnRelease = false;
            tailGrab.onGrabbed += h =>
            {
                thread.tailHolder = h.jawAnchor;
                if (knotPhase == KnotPhase.GrabTail)
                {
                    knotPhase = KnotPhase.Pull;
                    SetObjective("Tira de ambos instrumentos en direcciones opuestas para cinchar la lazada.");
                }
            };
            tailGrab.onReleased += () => { thread.tailHolder = null; };
        }

        protected override void Tick()
        {
            base.Tick();
            if (knotPhase == KnotPhase.None) return;

            // la esfera de la cola sigue al último segmento del hilo
            if (tailGrab != null && !tailGrab.IsHeld)
                tailGrab.transform.position = thread.TailParticlePos;

            switch (knotPhase)
            {
                case KnotPhase.Wrapping: TickWrapping(); break;
                case KnotPhase.Pull: TickPull(); break;
            }
        }

        void TickWrapping()
        {
            if (leftArm == null || needle == null || !needle.Grasp.IsHeld) { hasRadial = false; return; }

            // eje del instrumento izquierdo (trócar → muñeca)
            Vector3 axisOrigin = leftArm.wrist.position;
            Vector3 axis = (leftArm.wrist.position - leftArm.trocar.position).normalized;

            // punto medio del hilo entre aguja y tejido
            Vector3 mid = thread.ParticleAt(0.25f);
            Vector3 toMid = mid - axisOrigin;
            Vector3 radial = Vector3.ProjectOnPlane(toMid, axis);
            if (radial.magnitude < 0.01f || radial.magnitude > 0.25f) { hasRadial = false; return; }
            radial.Normalize();

            if (hasRadial)
            {
                float step = Vector3.SignedAngle(lastRadial, radial, axis);
                if (Mathf.Abs(step) < 30f) wrapAngleAcc += step;
            }
            lastRadial = radial;
            hasRadial = true;

            if (Mathf.Abs(wrapAngleAcc) >= 320f)
            {
                wrapsDone++;
                wrapAngleAcc = 0f;
                leftArm.master.Haptic(0.5f, 0.07f);
                int need = wrapsPerThrow[currentThrow];
                if (wrapsDone >= need)
                {
                    knotPhase = KnotPhase.GrabTail;
                    SetObjective("Suelta una mano si hace falta y AGARRA la cola del hilo (esfera naranja) con el instrumento IZQUIERDO.");
                }
                else FlashInfo($"Vuelta {wrapsDone}/{need}");
            }
        }

        void TickPull()
        {
            if (tailGrab == null || !tailGrab.IsHeld) { knotPhase = KnotPhase.GrabTail; return; }
            float sep = Vector3.Distance(leftArm.TipPos, rightArm.TipPos);
            if (sep > 0.22f && thread.Tension > 1.02f)
            {
                // lazada cinchada
                Vector3 mid = (entries[0].transform.position + exits[0].transform.position) * 0.5f;
                thread.CinchTo(mid);
                FlashInfo($"Lazada {currentThrow + 1}/3 completada");
                rightArm.master.Haptic(0.7f, 0.1f);
                leftArm.master.Haptic(0.7f, 0.1f);

                if (currentThrow + 1 >= wrapsPerThrow.Length)
                {
                    knotPhase = KnotPhase.None;
                    FlashInfo("¡Nudo completado!");
                    Finish();
                }
                else BeginThrow(currentThrow + 1);
            }
        }
    }
}
