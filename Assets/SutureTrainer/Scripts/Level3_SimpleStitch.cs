using UnityEngine;

namespace SutureTrainer
{
    /// <summary>
    /// Nivel 3 — Punto simple: pasar la aguja por entrada→salida en cada par
    /// de dianas sobre la herida y tirar del hilo. Base también para el nivel 4.
    /// </summary>
    public class Level3_SimpleStitch : TrainingLevel
    {
        [Header("Configuración de sutura")]
        public int stitchPairs = 3;
        public bool cinchAfterEachStitch = true;
        public float pairSpacing = 0.075f;
        public float woundOffset = 0.045f; // distancia lateral de la diana a la herida
        public string customTitle;
        [Tooltip("Muestra el arco ideal de la aguja en el punto activo.")]
        public bool showGuide = true;

        [Tooltip("Segmentos de hilo que hay que pasar en cada punto. Cada uno " +
                 "mide ~16 mm, así que 10 son ~16 cm de tirón. " +
                 "Antes se exigía una cola ABSOLUTA, y como el primer pin nace " +
                 "casi al principio del hilo, el primer punto pedía 48 cm de " +
                 "recorrido y los siguientes 11. Ahora los tres piden lo mismo.")]
        //
        // Bajado de 10 a 5 (~8 cm de tirón). Con 10 había que llevar la aguja
        // tan lejos que el hilo salía del campo, recto y tenso: en la grabación
        // del 28-08 se ve saliéndose del encuadre con tensión 1.38. Un punto se
        // pasa con un tirón corto, no arrastrando el cabo entero.
        public int pullSegments = 5;

        [Tooltip("Cuánto puede apuntar hacia arriba la punta y aun así valer " +
                 "como entrada. 0.1 exigía casi horizontal-o-abajo y costaba " +
                 "mucho acertar; 0.35 admite una inclinación natural.")]
        [Range(0f, 1f)] public float entryTipTolerance = 0.35f;

        int tailAtPin;

        [Tooltip("Informa en consola por qué no se registra la entrada: si " +
                 "falla la distancia o la inclinación de la punta.")]
        // Apagado por defecto, como el debugLog de InstrumentHaptics: es una
        // ayuda para afinar tolerancias, no algo que deba correr en una sesión
        // normal. Encendido, cada intento fallido de entrada escribe en
        // consola y da la impresión de que algo va mal.
        public bool logEntryMisses = false;
        float _nextEntryLog;

        protected override string LevelTitle =>
            string.IsNullOrEmpty(customTitle) ? "Nivel 3 · Punto simple" : customTitle;

        protected SutureNeedle needle;
        protected ThreadBase thread;
        protected TissuePatch tissue;
        protected PunctureTarget[] entries, exits;

        protected int currentPair;
        protected enum Phase { GoToEntry, InsideTissue, PullThrough, Done }
        protected Phase phase = Phase.GoToEntry;

        GameObject guide;

        /// <summary>Órgano portado de SuturingSimulator, si está en la escena.</summary>
        protected OrganRig organ;

        protected override void Setup()
        {
            tissue = SpawnTissue();
            organ = OrganRig.I;

            // Bandeja a 11 cm, no a 20. Con la aguja tan apartada, el recorrido
            // que el Touch tenia que cubrir -de la aguja a la diana mas lejana-
            // era de 275 mm; acercandola baja a 185 mm y se embraga bastante
            // menos. No estorba: las dianas no pasan de x = +75 mm.
            var tray = SpawnTray(fieldRoot.position + new Vector3(0.11f, 0.03f, -0.10f));
            needle = SpawnNeedle(tray.transform.position + Vector3.up * 0.02f,
                Quaternion.Euler(90f, 0f, 0f));
            thread = SpawnThread(needle);
            tissue.trackedNeedle = needle;

            // Con el órgano presente, las dianas van sobre la incisión REAL y el
            // parche procedural pasa a ser solo el tejido lógico: se le alinea
            // con la herida (de él salen punctureAllowed y la deformación) pero
            // se le oculta, porque lo que se ve es el órgano.
            bool usarOrgano = organ != null && organ.PairCount > 0;
            if (usarOrgano)
            {
                stitchPairs = Mathf.Min(stitchPairs, organ.PairCount);

                tissue.transform.SetPositionAndRotation(
                    organ.WoundCenter,
                    Quaternion.LookRotation(Vector3.Cross(organ.WoundDir, Vector3.up), Vector3.up));

                foreach (var mr in tissue.GetComponentsInChildren<MeshRenderer>(true))
                    mr.enabled = false;

                // el arco guía debe abrirse lo que abre el par real
                woundOffset = Vector3.Distance(organ.EntryPos(0), organ.ExitPos(0)) * 0.5f;
            }

            entries = new PunctureTarget[stitchPairs];
            exits = new PunctureTarget[stitchPairs];
            float startX = -pairSpacing * (stitchPairs - 1) * 0.5f;
            for (int i = 0; i < stitchPairs; i++)
            {
                if (usarOrgano)
                {
                    entries[i] = SpawnPunctureTarget(organ.EntryPos(i), true, i);
                    exits[i] = SpawnPunctureTarget(organ.ExitPos(i), false, i);
                    continue;
                }

                Vector3 basePos = tissue.WoundCenter + tissue.WoundDir * (startX + i * pairSpacing);
                Vector3 side = Vector3.Cross(Vector3.up, tissue.WoundDir).normalized;
                entries[i] = SpawnPunctureTarget(basePos + side * woundOffset + Vector3.up * 0.014f, true, i);
                exits[i] = SpawnPunctureTarget(basePos - side * woundOffset + Vector3.up * 0.014f, false, i);
            }
            // Tope de tolerancia deducido de la anatomía REAL del órgano.
            //
            // El radio por defecto (28 mm) venía del trazado procedural, donde
            // los pares distaban 75 mm. Sobre la incisión del hígado las dianas
            // quedan a ~24 mm, así que ese radio hacía que al tocar una se
            // activara la de al lado. Se limita a una fracción de la separación
            // mínima real, así nunca se solapan aunque cambie la anatomía.
            if (usarOrgano) LimitarToleranciaAlaAnatomia();

            BuildGuide();
            ActivatePair(0);
            SetObjective("Toma la aguja y clávala en la diana de ENTRADA iluminada (verde), atravesando hacia la salida.");
        }

        /// <summary>
        /// Limita el radio de captura a un 40% de la distancia mínima entre
        /// dianas, de modo que dos zonas nunca puedan solaparse.
        /// </summary>
        void LimitarToleranciaAlaAnatomia()
        {
            // El límite lo marca la separación ENTRADA<->SALIDA del mismo par,
            // no la distancia a las dianas de otros pares.
            //
            // Las fases solo consultan el par actual (GoToEntry mira entries[i],
            // InsideTissue mira exits[i]), así que dos pares distintos nunca
            // compiten. Incluirlos apretaba el tope un 32% de más y hacía muy
            // difícil acertar el indicador verde.
            //
            // Lo que sí hay que evitar es que, al clavar en la entrada, la
            // salida se dé por buena sin haber atravesado el tejido.
            float minima = float.MaxValue;
            for (int i = 0; i < stitchPairs; i++)
            {
                float d = Vector3.Distance(entries[i].transform.position,
                                           exits[i].transform.position);
                if (d > 1e-5f && d < minima) minima = d;
            }

            if (minima == float.MaxValue) return;

            // 0.45 daba 14 mm y el log mostraba que se llegaba de forma
            // consistente a 16-24 mm y nunca por debajo. 0.65 da ~20 mm, que
            // sigue muy por debajo de los 31 mm de separación entrada-salida,
            // así que la salida no puede dispararse sola.
            float tope = minima * 0.65f;
            for (int i = 0; i < stitchPairs; i++)
            {
                entries[i].maxRadius = tope;
                exits[i].maxRadius = tope;
            }

            Debug.Log($"[SutureTrainer] Entrada-salida a {minima * 1000f:F0} mm -> " +
                      $"tolerancia limitada a {tope * 1000f:F0} mm.");
        }

        /// <summary>Arco fantasma con la trayectoria ideal de la aguja.</summary>
        void BuildGuide()
        {
            if (!showGuide) return;
            guide = new GameObject("StitchGuide");
            guide.transform.SetParent(fieldRoot, false);
            var mesh = ProcMesh.ArcTube(woundOffset, 180f, 0.0025f,
                out _, out _, out _, out _, taperTip: false);
            guide.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = guide.AddComponent<MeshRenderer>();
            if (MaterialSet.I != null) mr.sharedMaterial = MaterialSet.I.ghost;
            guide.SetActive(false);
        }

        void PlaceGuide(int i)
        {
            if (guide == null) return;
            Vector3 e = entries[i].transform.position;
            Vector3 x = exits[i].transform.position;
            Vector3 mid = (e + x) * 0.5f;
            Vector3 dir = (x - e).normalized;
            // extremos del arco en ±Y local sobre las dianas; panza hacia abajo (dentro del tejido)
            Vector3 fwd = Vector3.Cross(Vector3.down, dir);
            guide.transform.SetPositionAndRotation(mid, Quaternion.LookRotation(fwd, dir));
            guide.SetActive(true);
        }

        protected void ActivatePair(int i)
        {
            currentPair = i;
            phase = Phase.GoToEntry;
            entries[i].SetState(PunctureTarget.State.Active);
            tissue.punctureAllowed = false;
            PlaceGuide(i);
        }

        protected override void Tick()
        {
            if (needle == null) return;
            Vector3 tip = needle.TipPos;
            bool held = needle.Grasp.IsHeld;

            switch (phase)
            {
                case Phase.GoToEntry:
                    DiagnosticarDiana(entries[currentPair], tip, held, "entrada", true);
                    if (held && entries[currentPair].TipInside(tip) && needle.TipDir.y < entryTipTolerance)
                    {
                        Metrics.AddPrecisionSample(entries[currentPair].Deviation(tip));
                        entries[currentPair].SetState(PunctureTarget.State.Done);
                        // sutura real: fija el hilo y deforma el órgano
                        if (organ != null && currentPair < organ.PairCount)
                            organ.Suture(organ.pairs[currentPair].entry);
                        exits[currentPair].SetState(PunctureTarget.State.Active);
                        tissue.punctureAllowed = true;
                        phase = Phase.InsideTissue;
                        AudioFX.Pop();
                        FlashInfo("Entrada correcta · gira la muñeca siguiendo la curva de la aguja");
                        SetObjective("Rota la aguja siguiendo su curvatura hasta salir por la diana de SALIDA.");
                        if (rightArm != null) rightArm.master.Haptic(0.5f, 0.08f);
                    }
                    break;

                case Phase.InsideTissue:
                    DiagnosticarDiana(exits[currentPair], tip, held, "salida", false);
                    if (held && exits[currentPair].TipInside(tip))
                    {
                        Metrics.AddPrecisionSample(exits[currentPair].Deviation(tip));
                        exits[currentPair].SetState(PunctureTarget.State.Done);
                        if (organ != null && currentPair < organ.PairCount)
                            organ.Suture(organ.pairs[currentPair].exit);
                        thread.PinThroughStitch(exits[currentPair].transform.position,
                                                entries[currentPair].transform.position);
                        // punto de partida para medir el paso de hilo
                        tailAtPin = thread.RemainingTailSegments;
                        tissue.punctureAllowed = false;
                        phase = Phase.PullThrough;
                        AudioFX.Pop();
                        if (guide != null) guide.SetActive(false);
                        SetObjective("Tira de la aguja para pasar el hilo, dejando una cola corta.");
                    }
                    break;

                case Phase.PullThrough:
                    // Avance RELATIVO desde donde quedó el pin: igual de largo
                    // en todos los puntos, en vez de depender de cuánta cola
                    // quede en total.
                    if (tailAtPin - thread.RemainingTailSegments >= pullSegments)
                        OnStitchThreadPulled();
                    break;
            }
        }

        /// <summary>
        /// Trabajar sobre las dianas del punto en curso no es punción indebida:
        /// es exactamente la maniobra que se está pidiendo. Se da un margen de
        /// tres radios, el mismo con el que DiagnosticarDiana considera que la
        /// punta "está cerca".
        /// </summary>
        protected override bool PuncionEsperada(Vector3 tip)
        {
            if (entries == null || exits == null) return false;
            if (currentPair < 0 || currentPair >= entries.Length) return false;

            return CercaDeDiana(entries[currentPair], tip) ||
                   CercaDeDiana(exits[currentPair], tip);
        }

        static bool CercaDeDiana(PunctureTarget diana, Vector3 tip) =>
            diana != null &&
            Vector3.Distance(tip, diana.transform.position) < diana.EffectiveRadius * 3f;

        protected virtual void OnStitchThreadPulled()
        {
            if (cinchAfterEachStitch)
            {
                Vector3 mid = (entries[currentPair].transform.position + exits[currentPair].transform.position) * 0.5f;
                thread.CinchTo(mid);

                // Cerrar la herida AQUÍ, al terminar el punto. Antes esto
                // dependía de pulsar el botón secundario en algún momento sin
                // indicación alguna, así que los labios se juntaban a destiempo
                // y parecía aleatorio.
                if (organ != null) organ.Tighten();

                FlashInfo($"Punto {currentPair + 1}/{stitchPairs} completado · herida aproximada");
            }
            NextStitchOrFinish();
        }

        protected void NextStitchOrFinish()
        {
            phase = Phase.Done;
            if (currentPair + 1 >= stitchPairs)
            {
                OnAllStitchesDone();
            }
            else
            {
                ActivatePair(currentPair + 1);
                SetObjective($"Siguiente punto ({currentPair + 1}/{stitchPairs}): entra por la diana verde.");
            }
        }

        /// <summary>
        /// Cuando la punta está cerca de la entrada pero no registra, dice CUÁL
        /// de las dos condiciones falla: la distancia o la inclinación. Llevaba
        /// varias rondas ajustando el radio sin comprobar si el problema era el
        /// filtro de dirección.
        /// </summary>
        void DiagnosticarDiana(PunctureTarget diana, Vector3 tip, bool held,
                               string cual, bool exigeInclinacion)
        {
            if (!logEntryMisses || Time.time < _nextEntryLog) return;

            float d = Vector3.Distance(tip, diana.transform.position);
            if (d > diana.EffectiveRadius * 3f) return;   // ni cerca: no molestar

            _nextEntryLog = Time.time + 0.5f;
            float inclinacion = needle.TipDir.y;

            bool okDist = d < diana.EffectiveRadius;
            bool okAng = !exigeInclinacion || inclinacion < entryTipTolerance;
            if (held && okDist && okAng) return;          // va a entrar igualmente

            Debug.Log($"[Nivel3] {cual} {currentPair}: " +
                      $"agarrada={(held ? "sí" : "NO")} | " +
                      $"distancia {d * 1000f:F0} mm (límite {diana.EffectiveRadius * 1000f:F0}) {(okDist ? "OK" : "FALLA")} | " +
                      (exigeInclinacion
                        ? $"inclinación {inclinacion:F2} (límite {entryTipTolerance:F2}) {(okAng ? "OK" : "FALLA")}"
                        : "inclinación: no se exige"));
        }

        protected virtual void OnAllStitchesDone()
        {
            // Sutura continua (nivel 4): no se cincha punto a punto, se tensa
            // TODA la línea al final. Sin esto la herida se quedaba abierta
            // tras los 5 pases, que no es una práctica coherente.
            TightenAllIfNeeded();
            Finish();
        }

        /// <summary>
        /// Aproxima los labios de los puntos dados cuando el nivel no cincha
        /// tras cada puntada. Un tensado por puntada completada, igual que en
        /// el camino de cinchado inmediato.
        /// </summary>
        protected void TightenAllIfNeeded()
        {
            if (cinchAfterEachStitch || organ == null) return;
            for (int i = 0; i < stitchPairs; i++) organ.Tighten();
        }
    }
}
