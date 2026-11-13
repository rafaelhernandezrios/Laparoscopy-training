# Protocolo experimental · SutureTrainer neuroadaptativo

Entrenador de sutura laparoscópica en Unity con medición de carga cognitiva por EEG (AURA, vía LSL) y adaptación de la dificultad en lazo cerrado. Este documento describe el diseño de los dos estudios, el equipo, el procesamiento de la señal, el procedimiento de cada sesión paso a paso, el uso del panel del investigador y los datos que se generan.

> Rama `quirofano-y-flujo-de-escenas`. Código del protocolo: `Assets/SutureTrainer/Scripts/Protocolo/`.

---

## 1. Objetivo e hipótesis

**Pregunta general.** ¿Un sistema de entrenamiento que adapta la dificultad según la carga cognitiva medida por EEG mantiene la carga del alumno en una zona óptima mejor que un sistema no adaptativo?

| Estudio | Pregunta | Diseño |
|---|---|---|
| **Estudio 1 · Caracterización** | ¿Cuánta carga cognitiva produce cada actividad, con mandos de Oculus y con el robot háptico? | Intra-sujeto, dificultad fija, sin adaptación |
| **Estudio 2 · Comparativo** | ¿La adaptación por EEG mantiene la carga en la zona óptima mejor que la misma dificultad sin adaptación? | Entre sujetos: grupo **adaptativo** vs grupo **acoplado (yoked)** |

**Hipótesis del Estudio 2**

- **H1 (principal):** el grupo adaptativo pasa un mayor porcentaje del tiempo en la zona óptima de carga y con menor variabilidad del índice que el grupo acoplado.
- **H2:** el desempeño (estrellas, errores, precisión, tiempo) del grupo adaptativo es igual o mejor que el del acoplado.
- **H3:** las tres medidas de carga (EEG, NASA-TLX, tarea secundaria) convergen: la razón theta/alfa se asocia con la carga subjetiva y con el tiempo de reacción.

---

## 2. Diseño

### 2.1 Estudio 1 · Caracterización

- Todos los participantes hacen las actividades a **dificultad fija** (0.5 por defecto) y **sin distractores**.
- Primero con **mandos de Oculus**; al final, con el **robot Touch** (mano derecha).
- Resultado: carga media por actividad y dispositivo (EEG, TLX, tarea secundaria), y el orden de las actividades por carga real.
- Sirve además para calibrar los umbrales que usará el Estudio 2 y estimar el tamaño de muestra.

### 2.2 Estudio 2 · Adaptativo vs acoplado

- **Calibración personal** al inicio de cada sesión (bloques fácil y difícil, ver §5.4): fija los umbrales de la zona óptima de esa persona.
- **Grupo adaptativo:** el EEG mueve la dificultad durante cada actividad.
- **Grupo acoplado (yoked):** cada participante recibe **exactamente la secuencia de dificultad** que tuvo un participante adaptativo (archivo `dificultad_<actividad>.csv`), sin que dependa de su propio EEG.
  - Ambos grupos ven la misma dificultad promedio; lo único que cambia es si la dificultad responde **a esa persona**. Es el control más fuerte para atribuir el efecto a la adaptación.
  - Los participantes acoplados se emparejan uno a uno con adaptativos ya registrados (se elige el par en el panel).
- **Entre sujetos**, para evitar que el aprendizaje de una condición contamine la otra.
- **Tamaño de muestra orientativo:** ~26 por grupo para un efecto grande (d ≈ 0.8, α = 0.05, potencia 0.8). Recalcular con los datos del Estudio 1.

### 2.3 Orden de dispositivos

Oculus primero y robot al final, como progresión de entrenamiento. Es una **limitación declarada**: el bloque del robot se confunde con fatiga y aprendizaje previos.

### 2.4 Ética

Antes de reclutar: aprobación del comité de ética de la UAEH, consentimiento informado, criterios de inclusión/exclusión (p. ej. sin antecedentes neurológicos, visión normal o corregida, sin experiencia previa en cirugía laparoscópica para el grupo novato) y anonimización (ID de participante; ningún dato personal en los archivos).

---

## 3. Equipo y software

| Elemento | Uso |
|---|---|
| **EEG AURA** (8 canales, 250 Hz) + app AURA | Publica por LSL los streams `AURA` (crudo), `AURA_Filtered` y `AURA_Power`. El protocolo usa **`AURA`** |
| **Meta Quest** (Oculus) | Visor y mandos del participante |
| **3D Systems Touch** ("Default Device") | Robot háptico de la mano derecha. Su gimbal no tiene sensores: el giro de la muñeca va por teclado (I/K, J/L, U/O) |
| **Unity 6000.5.7f1**, proyecto `Laparoscopy-training` | Simulador, panel del investigador, cálculo de carga |
| **LSL4Unity** (embebido en `Packages/`) | Lectura del EEG y publicación de marcadores |
| **LabRecorder / VR EEG Studio** | Graba EEG + marcadores + carga en un solo **XDF** |

### 3.1 Montaje del AURA

Orden del arreglo de datos crudos:

| Canal | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 |
|---|---|---|---|---|---|---|---|---|
| Electrodo | Fp1 | Fp2 | **F3** | **Fz** | **F4** | **P3** | **Pz** | **P4** |

- **Theta frontal:** F3, Fz, F4 (índices 2, 3, 4).
- **Alfa parietal:** P3, Pz, P4 (índices 5, 6, 7).
- Si el stream no trae etiquetas y tiene 8 o más canales, el sistema aplica este montaje automáticamente.

---

## 4. Medidas

| Medida | Qué mide | Fuente |
|---|---|---|
| **Razón θ/α regional** | Carga cognitiva (objetiva) | AURA por LSL, calculada en Unity |
| **Score** | Razón normalizada al reposo de la persona (1 = línea base) | Idem |
| **% de tiempo en zona óptima** | Qué tanto se mantuvo la carga entre los umbrales | Controlador adaptativo |
| **NASA-TLX (raw)** | Carga subjetiva: 6 escalas 0–100, total = media | Al terminar cada actividad |
| **Tarea secundaria (DRT)** | Capacidad sobrante: tiempo de reacción, omisiones, falsas | Tono cada 7–14 s; responder con grip o tecla R |
| **Desempeño** | Tiempo, recorrido, errores, precisión (mm), estrellas | Métricas de cada nivel |
| **Dificultad** | Valor 0–1 del lazo y su traza en el tiempo | Controlador adaptativo |

### 4.1 Cálculo del índice de carga

- Ventana de **2 s**, recalculada cada **0.5 s**.
- Por canal: se resta la media, ventana de Hann, FFT; potencia en **theta 4–8 Hz** y **alfa 8–13 Hz**.
- θ = media de F3/Fz/F4 · α = media de P3/Pz/P4 · **razón = θ / α**.
- **Rechazo de artefactos:** la ventana se descarta si algún canal supera **150 µV** (tras quitar la media) o tiene desviación menor a **0.5 µV** (plano/desconectado).
- **Score = razón / razón de línea base** (ojos abiertos).
- El score se publica por LSL como `SutureTrainer_Carga` (canales: `theta_frontal`, `alfa_parietal`, `razon_theta_alfa`, `score_normalizado`).

### 4.2 Lazo adaptativo (Estudio 2, grupo adaptativo)

- El controlador recibe la **media de 4 ventanas (~cada 2 s)** y la suaviza (EMA, λ = 0.6).
- **Zonas:** Baja (score < umbral bajo), Óptima, Alta (score > umbral alto), con histéresis de 0.12.
- Hacen falta **3 ventanas seguidas** en una zona nueva para actuar, y hay un **periodo refractario de 10 s** entre acciones.
- **Baja → sube** la dificultad (+0.08) si no está fallando.
- **Alta → baja** la dificultad (−0.15), salvo **forcejeo productivo** (sobrecarga sin errores nuevos en 20 s): entonces se sostiene.
- La dificultad (0 fácil · 0.5 medio · 1 difícil) mueve:

| Parámetro | Fácil (0) | Medio (0.5) | Difícil (1) |
|---|---|---|---|
| Escala de movimiento | 0.45 | 0.75 | 1.05 |
| Ganancia háptica | 1.40 | 1.00 | 0.60 |
| Umbral de tensión del hilo | 1.45 | 1.12 | 0.95 |
| Radio de diana de punción (m) | 0.040 | 0.028 | 0.018 |
| Radio de anillo (m) | 0.060 | 0.045 | 0.032 |
| Tiempo par (s) | 180 | 120 | 90 |
| Recorrido par (m) | 18 | 12 | 9 |
| Errores máximos para 3 estrellas | 3 | 1 | 0 |

(Los radios y los umbrales de puntuación se fijan al inicio de cada actividad; el resto cambia de forma continua.)

---

## 5. Procedimiento de una sesión

Duración estimada: **45–70 min** según las actividades elegidas.

### 5.1 Antes de que llegue el participante

1. Encender el AURA y abrir su app; comprobar que publica por LSL.
2. Abrir Unity con el proyecto, dar **Play**, pulsar **F4** (panel del investigador).
3. **Buscar AURA** → **Conectar: AURA [EEG] 8 ch 250 Hz**.
4. Abrir LabRecorder (o VR EEG Studio) y seleccionar `AURA`, `SutureTrainer_Markers` y `SutureTrainer_Carga`; empezar a grabar el XDF.
5. Si hay bloque con robot: stylus del Touch fuera de su base.

### 5.2 Configuración en el panel (Nueva sesión)

| Campo | Valor típico |
|---|---|
| ID participante | `P01`, `P02`… (sin nombres) |
| Estudio | 1 · Caracterización / 2 · Comparativo |
| Grupo (Estudio 2) | Adaptativo / Acoplado (elegir el participante adaptativo a replicar) |
| Actividades con Oculus | `1,2,3,4,5,7` |
| Actividades con robot | `1,3` |
| Línea base (s) | 120 |
| Ojos cerrados | Sí (control de reactividad alfa) |
| Descanso (s) | 30 |
| Máximo por actividad (s) | 480 |
| Bloque de calibración (s) | 90 (solo Estudio 2) |
| Dificultad fija / inicial | 0.5 |
| Distractores fijos | 0 |
| Tarea secundaria | Según el diseño (igual para todos) |
| Umbrales (bajo/alto) | 1.15 / 1.6 (en el Estudio 2 los reemplaza la calibración) |
| Modo prueba con teclado | **No** en sesiones reales (solo para pilotos sin visor) |

Pulsar **Iniciar sesión**. El panel arma la lista de pasos; el investigador solo pulsa **Iniciar paso** y, cuando corresponde, **Listo**.

### 5.3 Pasos

| # | Paso | Qué ve el participante | Qué hace el investigador | Resultado |
|---|---|---|---|---|
| 1 | **Señal EEG** | "Estamos preparando la sesión" | Revisar que los canales usados (●) estén en verde y la gráfica estable; pedir parpadeo y apretar mandíbula para ver el artefacto. **Listo** | % de ventanas válidas |
| 2 | **Línea base · ojos abiertos** | Cruz de fijación | Esperar (120 s) | Razón de reposo → score = 1 |
| 3 | **Línea base · ojos cerrados** | "Cierra los ojos… ábrelos al oír el tono" | Esperar | Reactividad alfa OC/OA (esperado > 1.3) |
| 4* | **Calibración · fácil** | Actividad 1 a dificultad 0, sin distractores | Esperar (90 s o hasta terminar) | Score medio fácil |
| 5* | **Calibración · difícil** | Actividad 1 a dificultad 1, distractores nivel 3 | Esperar | Score medio difícil → umbrales personales |
| … | **Actividad N · Oculus** | La actividad | Observar; termina sola (completada o tiempo máximo) | Fila en `resumen_actividades.csv` |
| … | **NASA-TLX** | Cada escala con su pregunta | Preguntar en voz alta y registrar 0–100 (de 5 en 5) | TLX por escala y total |
| … | **Descanso** | Cuenta atrás | Se puede saltar | — |
| … | **Cambio al robot** | Instrucciones | Retirar mandos, mano derecha al stylus; comprobar que se mueve. **Listo** | — |
| … | **Actividad N · robot** | La actividad con el Touch | Embrague: Ctrl izq. o clic derecho · Pinza: F o clic izquierdo (C la deja cerrada) | Fila en el resumen |
| último | **Fin** | "¡Gracias!" | Detener LabRecorder; revisar la carpeta de la sesión | Archivos completos |

\* Solo en el Estudio 2.

**Umbrales personales (Estudio 2):** con *f* = score medio del bloque fácil y *d* = score medio del bloque difícil,
umbral bajo = *f* + 0.3·(*d* − *f*) · umbral alto = *f* + 0.7·(*d* − *f*).
Si *d* − *f* < 0.05 los bloques no se separan y se mantienen los umbrales configurados (anotarlo como incidencia).

### 5.4 Durante las actividades

- El láser del menú se desactiva entre pasos para que el participante no arranque niveles por su cuenta.
- Si el participante sale de la escena (botón de menú), el paso queda como **incompleto**.
- **Saltar paso** y **Paso anterior** permiten corregir el flujo; **Terminar sesión** cierra y guarda todo.

### 5.5 Controles útiles (teclado del investigador)

| Tecla | Función |
|---|---|
| F4 | Mostrar/ocultar el panel del investigador |
| F6 / F7 / F8 | Nivel de distractores · fijo/adaptativo · tarea secundaria |
| F9 | Verificar LSL (resuelve el stream y comprueba que llega un marcador) |
| F10 / Shift+F10 / Esc | Guía automática con el robot · guía + vídeo · detener |
| F11 / F12 | Calibrar el resorte del robot · usar el gimbal del stylus |
| ⏺ Grabar vídeo (panel) | Graba la pantalla con los paneles en tiempo real |

---

## 6. Datos generados

Cada sesión crea `Sesiones/<ID>_<E1|E2adapt|E2acopl>_<fecha>/` (en el editor, dentro del proyecto y fuera de git; en una build, en `persistentDataPath`).

| Archivo | Contenido |
|---|---|
| `sesion.txt` | Configuración, stream y canales usados, umbrales (y los calibrados) |
| `pasos.csv` | `n;tipo;titulo;inicio_s;fin_s;estado;resultado` |
| `resumen_actividades.csv` | Una fila por actividad: `clave;actividad;dispositivo;condicion;fin;tiempo_s;recorrido_m;errores;precision_mm;estrellas;razon_media;score_medio;pct_zona_optima;pct_ventanas_validas;dificultad_media;dificultad_final;t2_estimulos;t2_aciertos;t2_omisiones;t2_falsas;t2_rt_medio;tlx_mental;tlx_fisica;tlx_temporal;tlx_rendimiento;tlx_esfuerzo;tlx_frustracion;tlx_total` |
| `carga.csv` | Serie cada 0.5 s: `t_sesion;lsl;paso;tipo;actividad;dispositivo;theta_f;alfa_p;razon;score;zona;dificultad` (`tipo = espera` entre pasos) |
| `dificultad_<clave>.csv` | Traza `t;d` de cada actividad; es la que reproduce el grupo acoplado |

Además, Unity guarda por intento las métricas, la trayectoria y los eventos (`eventos_*.csv`, con marca LSL) en `persistentDataPath/SutureTrainer/`.

### 6.1 Marcadores LSL (`SutureTrainer_Markers`)

Formato `categoria/evento|clave=valor|…`. Los principales:

- `protocolo/sesion/inicio|id=…|estudio=…|grupo=…` · `protocolo/sesion/fin|motivo=…`
- `protocolo/paso/inicio|n=…|tipo=…|act=…|disp=…` · `protocolo/paso/fin|n=…|tipo=…|estado=…`
- `protocolo/base/abiertos|razon=…|validas=…` · `protocolo/base/cerrados|reactividad_alfa=…`
- `protocolo/calibracion/facil|score=…` · `protocolo/calibracion/dificil|score=…` · `protocolo/umbrales|bajo=…|alto=…`
- `protocolo/tlx|act=…|mental=…|…|total=…`
- `nivel/inicio` · `nivel/fin|estrellas=…|errores=…|tiempo=…` · `tarea/puncion|…`
- `distractor/…`, `tarea2/…`, `guia/…`, `eeg/conectado`, `eeg/perdido`

---

## 7. Análisis previsto

**Estudio 1**
- Carga por actividad y dispositivo: modelo mixto con score (y TLX, RT de la tarea secundaria) ~ actividad × dispositivo + (1 | participante).
- Validez convergente: correlación intra-sujeto entre score, TLX total y RT.
- Ordenar las actividades por carga; estimar la variabilidad entre sujetos para el cálculo de muestra.

**Estudio 2**
- **H1:** % de tiempo en zona óptima y desviación estándar del score, adaptativo vs acoplado (t de Welch o modelo mixto por actividad).
- **H2:** desempeño (estrellas, errores, precisión, tiempo) por grupo.
- **H3:** relación score–TLX–RT dentro de cada grupo.
- Sensibilidad: repetir con la configuración clásica de un canal (Fz/Pz) y con la regional.

**Exclusiones:** sesiones con < 70 % de ventanas válidas, reactividad alfa OC/OA < 1 (sospecha de mal contacto) o pasos incompletos se analizan aparte y se reportan.

---

## 8. Lista de verificación rápida

- [ ] App AURA publicando `AURA` por LSL; impedancias/contacto revisados (F4 y P3 suelen dar problemas).
- [ ] Unity en Play, panel F4, AURA conectado, ventanas válidas > 90 %.
- [ ] LabRecorder grabando `AURA` + `SutureTrainer_Markers` + `SutureTrainer_Carga`.
- [ ] ID correcto, estudio y grupo correctos (acoplado: par elegido).
- [ ] **Modo prueba con teclado desactivado.**
- [ ] Robot: stylus fuera de la base; conecta como "Default Device".
- [ ] Al terminar: detener LabRecorder, revisar `Sesiones/<ID>_…/` y respaldar.

## 9. Problemas frecuentes

| Síntoma | Causa probable | Qué hacer |
|---|---|---|
| "Buscar AURA" no encuentra streams | La app AURA dejó de publicar | Reconectar la diadema en la app AURA y volver a buscar |
| Canal en rojo / σ muy baja | Mal contacto o canal muerto | Ajustar el electrodo; si es F3–F4/P3–P4 no iniciar hasta corregir |
| "ARTEFACTO" constante | Movimiento, mandíbula, cable | Pedir al participante que se relaje; revisar cables |
| Reactividad alfa < 1.3 | Ojos no cerrados o mal contacto parietal | Repetir la línea base (Paso anterior) |
| El robot no responde | Touch Smart Setup abierto o sin calibrar | Cerrar Touch Smart Setup, recalibrar, volver a intentar |
| El participante salió de la actividad | Usó el botón de volver al menú | Paso anterior → repetir la actividad |
