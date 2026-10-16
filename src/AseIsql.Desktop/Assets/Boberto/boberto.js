/* ── Boberto 2.0 — mascota compartida de SMG_APPS ────────────────────────────
 *
 * Vive en un hueco que le reserva la sidebar de cada app (`[data-boberto-dock]`,
 * ver "Dock" más abajo): respira, parpadea, gesticula, mira al cursor y, al
 * tocarlo, abre su estudio para personalizarlo. UN SOLO motor para las tres apps.
 *
 * Hasta octubre de 2026 caminaba por el hueco libre del topbar. Se mudó porque
 * ese hueco dependía del layout de cada topbar (anclas declaradas por app,
 * límites recalculados en cada resize, zoom y carga de fuentes) y se achicaba
 * cada vez que una app sumaba un control. Un slot declarado en la sidebar no
 * tiene nada que medir.
 *
 * ── Qué cambió en 2.0 ──
 *
 * El personaje dejó de ser un sprite. Hasta 1.x era una grilla de 16×18 celdas
 * pintadas de a un fillRect, y cada gesto era una matriz de píxeles escrita a
 * mano; ahora es un dibujo vectorial —curvas, degradados, rim light y brillos
 * especulares en los ojos— y cada gesto es un puñado de parámetros de pose.
 *
 * Se sigue dibujando sobre <canvas> y no sobre SVG a propósito: chatbot.js y
 * finder.js nos pasan un <canvas> ya creado, y las animaciones CSS del wrap
 * (plt-idle, plt-squash, plt-levita, plt-skatehop…) apuntan a `#plt-wrap canvas`.
 * Cambiar el elemento rompía las dos cosas sin ganar nada — Canvas2D traza las
 * mismas curvas.
 *
 * El motor viejo NO se borró: quedó congelado en shared/js/boberto-pixel.js con
 * la misma API. Volver atrás es cambiar smg_boberto_engine() a 'pixel' en
 * shared/render.php.
 *
 * ── Lo que aporta cada app ──
 *
 * Se declara en `window.BOBERTO_HOST` ANTES de cargar este archivo (ver
 * <app>/js/boberto-host.js):
 *
 *   window.BOBERTO_HOST = {
 *     tips: ["…"]                    // de qué habla
 *   };
 *
 * Es lo único que depende del host además del slot: qué sabe (DBMON habla de
 * SQL y Sybase, JiraToolkit de Jira y planificación). `leftAnchors`, que
 * declaraba dónde arrancaba su paseo por el topbar, se tolera y se ignora: ya
 * no camina. El resto —dibujo, sombreros, ojos, efectos, preview— es idéntico
 * y por eso vive acá.
 *
 * El LUGAR lo declara quien renderiza: la app pone un `<div data-boberto-dock>`
 * en su sidebar y el motor lo espera (MutationObserver), lo adopta y le pone
 * rol de botón. Sin slot, Boberto no aparece; no hay fallback al topbar.
 *
 * Su configuración es COMPARTIDA entre las apps (claves smg_boberto_*): el
 * sombrero que le elegís en una lo lleva puesto en las otras. Los ids de skin,
 * ojo y accesorio son los MISMOS que en 1.x, así que el rework no le resetea
 * el Boberto a nadie.
 * ------------------------------------------------------------------------- */
(function () {
  'use strict';

  /* ══ LA CAJA DE DIBUJO ══════════════════════════════════════════════════
     Son DOS cajas y confundirlas es lo que venía recortando accesorios.

     `U` es la caja del PERSONAJE: 16x18 unidades, y es el sistema de
     coordenadas en el que está escrito todo el dibujo (BODY.cx = 8 es su eje).
     Nada de eso se toca nunca.

     `BOX` es la caja del LIENZO: el personaje MÁS aire alrededor. Existe porque
     los accesorios se salen del cuerpo —un arco cruzado, un gorro alto, unas
     alas— y todo lo que caía fuera de las 16x18 se cortaba contra el borde del
     canvas. Se midió: NUEVE de treinta y un accesorios tocaban el borde (mago,
     vikingo, chef, copa, samurái y gladiador por arriba; arquero, capa y alas
     de murciélago por los costados; la capa además por abajo).

     El aire se agrega moviendo el ORIGEN del contexto, no reescribiendo
     coordenadas: `paintFrame` traslada por PAD y de ahí en adelante todo dibuja
     exactamente donde dibujaba. Un accesorio nuevo tampoco tiene que enterarse.

     Para saber si algún día vuelve a faltar aire, el chequeo es el mismo con el
     que se midió: renderizar cada accesorio, leer el alfa y preguntar si toca
     la primera o la última fila o columna del lienzo. */
  const U   = { W: 16, H: 18 };
  const PAD = { l: 2, r: 2, t: 2, b: 1 };
  const BOX = { W: U.W + PAD.l + PAD.r, H: U.H + PAD.t + PAD.b };

  /* MW/MH son la medida del LIENZO, y por eso salen de BOX.
     Las usan las partículas de los gestos, que se posicionan dentro del
     contenedor: con el valor del personaje, el centro que
     calculaban («(MW * SC) / 2») quedaba corrido dos unidades respecto del
     centro real del canvas. */
  const SC = 2, MW = BOX.W, MH = BOX.H;

  // Lo que aporta la app que lo hospeda. Si no declara nada, Boberto igual
  // funciona y se queda callado. Preferible a romper una app que todavía no se
  // enganchó.
  const HOST = window.BOBERTO_HOST || {};

  /**
   * Migración de las claves de cuando cada app tenía su propio Boberto.
   *
   * Corre una sola vez y sólo copia lo que todavía no existe en la clave nueva.
   * Sin esto el usuario abre la app y lo encuentra sin sombrero, con la piel
   * por defecto y los efectos apagados: para él no es "se unificó", es "se
   * borró lo que yo había configurado".
   *
   * `jp_` va primero porque JiraToolkit es el que traía el motor más avanzado
   * y, con él, la config más completa.
   */
  (function migrateLegacyKeys() {
    try {
      if (localStorage.getItem('smg_boberto_migrated') === '1') return;
      for (let i = 0; i < localStorage.length; i++) {
        const k = localStorage.key(i);
        if (!k) continue;
        const m = k.match(/^(jp|dbmon)_(mascota_[a-z_]+|boberto_[a-z_0-9]+)$/);
        if (!m) continue;
        const dest = 'smg_' + m[2];
        if (localStorage.getItem(dest) === null) localStorage.setItem(dest, localStorage.getItem(k));
      }
      localStorage.setItem('smg_boberto_migrated', '1');
    } catch (e) {}
  })();
  const LS_KEY = 'smg_mascota_enabled';

  // ── Accesorios de cabeza (colores FIJOS, no dependen del tema) ──
  // Igual que el vestuario MJ_*: se distinguen en cualquier color de cuerpo.
  const HAT_DARK = '#2a2c34';  // H — auriculares/gorro (oscuro)
  const HAT_POM  = '#f0eadd';  // P — pompón / realce
  const HAT_BULB = '#ffe6a8';  // A — bulbo de antena / LED (con glow)

  /*
   * El color propio de cada pieza cuando la persona no eligió ninguno.
   *
   * El color por defecto del accesorio es un grafito: le queda bien a unos
   * auriculares o a una galera, y mal a casi todo lo demás. Una capa gris,
   * un ovni negro o una bufanda grafito dejan de leerse como lo que son. Con
   * el grafito de fábrica, la pieza usa el suyo; un color elegido a mano
   * siempre gana. Empezó con el pañuelo del pirata.
   */
  const HAT_NATURAL = {
    pirata: '#8f2d18', capa: '#b3202a', ovni: '#a9b3c2', gorro: '#2f63c8',
    mago: '#4a2f8f', vikingo: '#8d97a3', gladiador: '#b5894a', bufanda: '#c23b33',
    yukata: '#2d4a7a', jetpack: '#d9a21b',
    brasita: '#c63a24', caparazon: '#2a9d8f', brotito: '#4f9a45', chispa: '#4b3fc4',
    sombrita: '#2c3554',
  };

  // Corpus de tips: lo pone cada app (ver BOBERTO_HOST).
  const CONSEJOS = Array.isArray(HOST.tips) ? HOST.tips : [];

  // ── Paletas — Jira-blue como primario ─────────────────────────────────
  //
  // O = outline exterior  E = highlight blanco  F = cuerpo (Jira-blue)
  // W = trim/hombros      D = visor oscuro       C = pupilas (azul claro)
  // M = acento cyan       R = cinturón           G = LEDs verdes
  // B = piernas/oscuro    J = burbuja (naranja Jira)   K = taza café
  const PALS = {
    default: {  // Clawd terracota — neutro
      _: null,
      O: '#2a1206', E: '#ffd9ad', F: '#d96c3f',
      W: '#a8431f', D: '#4a2212', C: '#ffd98a',
      M: '#ffb238', R: '#7c3317', G: '#5fd38a',
      B: '#4a2212', J: '#ff5630', K: '#ff8b00',
      glow: '#ff7a45', glowA: '#ff7a4540',
    },
    stellar: {  // tema violeta → naranja un toque más rosado
      _: null,
      O: '#2a1010', E: '#ffd6c2', F: '#e0653f',
      W: '#b0421f', D: '#4a2018', C: '#ffd5a0',
      M: '#ff9d5c', R: '#7e2f1e', G: '#6ee7b7',
      B: '#3e1a14', J: '#f0abfc', K: '#f59e0b',
      glow: '#ff8a5c', glowA: '#ff8a5c40',
    },
    cosmos: {  // tema azul profundo → naranja más rojizo
      _: null,
      O: '#220e05', E: '#ffcea0', F: '#d05f33',
      W: '#9e3a18', D: '#451f10', C: '#ffce80',
      M: '#ff9a30', R: '#722c14', G: '#2da870',
      B: '#3f1c0e', J: '#ff4020', K: '#e08000',
      glow: '#ff6f3a', glowA: '#ff6f3a40',
    },
    stellar2: {  // alias para 'stellar'
      _: null,
      O: '#2a1010', E: '#ffd6c2', F: '#e0653f',
      W: '#b0421f', D: '#4a2018', C: '#ffd5a0',
      M: '#ff9d5c', R: '#7e2f1e', G: '#6ee7b7',
      B: '#3e1a14', J: '#f0abfc', K: '#f59e0b',
      glow: '#ff8a5c', glowA: '#ff8a5c40',
    },
    day: {  // light theme → contorno y cuerpo más oscuros para verse sobre claro
      _: null,
      O: '#3a1606', E: '#f0b27a', F: '#d2591f',
      W: '#a23c12', D: '#5a2a14', C: '#ffb74d',
      M: '#e07b1a', R: '#7a2e10', G: '#0e9f6e',
      B: '#5a2a14', J: '#de350b', K: '#b45309',
      glow: '#d2591f', glowA: '#d2591f40',
    },
    light: {
      _: null,
      O: '#3a1606', E: '#f0b27a', F: '#d2591f',
      W: '#a23c12', D: '#5a2a14', C: '#ffb74d',
      M: '#e07b1a', R: '#7a2e10', G: '#0e9f6e',
      B: '#5a2a14', J: '#de350b', K: '#b45309',
      glow: '#d2591f', glowA: '#d2591f40',
    },
    teal: {  // tema teal → naranja Clawd contrasta fuerte
      _: null,
      O: '#2a1206', E: '#ffd9ad', F: '#df6e3a',
      W: '#ab4520', D: '#4a2212', C: '#ffd98a',
      M: '#ffb238', R: '#7c3317', G: '#34d399',
      B: '#4a2212', J: '#fb923c', K: '#d97706',
      glow: '#ff7a45', glowA: '#ff7a4540',
    },
    abyss: {  // tema casi negro → naranja más brillante para destacar
      _: null,
      O: '#1a0a04', E: '#ffdcb0', F: '#e8703f',
      W: '#b8481f', D: '#4a2212', C: '#ffd98a',
      M: '#ffb238', R: '#883518', G: '#20a060',
      B: '#4a2212', J: '#ff4020', K: '#e07800',
      glow: '#ff8347', glowA: '#ff834740',
    },
  };

  // ── Skins de usuario ──────────────────────────────────────────────────
  // 'auto' = paleta según el tema (PALS). Un preset o 'custom' = paleta
  // derivada de un único color base (más fácil de configurar).
  const LS_SKIN = 'smg_boberto_skin';
  const LS_SKIN_COLOR = 'smg_boberto_skin_color';
  const LS_EYE = 'smg_boberto_eye';
  const LS_HAT = 'smg_boberto_hat';
  const LS_HAT_COLOR = 'smg_boberto_hat_color';
  const LS_HAT_COLOR2 = 'smg_boberto_hat_color2';
  const HAT_PRESETS = [
    { id: 'auriculares', name: 'Auriculares', icon: '🎧' },
    { id: 'antena',      name: 'Antena',      icon: '📡' },
    { id: 'copete',      name: 'Copete',      icon: '⚡' },
    { id: 'cresta',      name: 'Cresta LED',  icon: '🪩' },
    { id: 'gorro',       name: 'Gorro',       icon: '🧢' },
    { id: 'mago',        name: 'Gorro Mago',  icon: '🧙‍♂️' },
    { id: 'corona',      name: 'Corona',      icon: '👑' },
    { id: 'vikingo',     name: 'Casco Vikingo', icon: '🪖' },
    { id: 'chef',        name: 'Sombrero Chef', icon: '👨‍🍳' },
    { id: 'halo',        name: 'Halo Ángel',  icon: '😇' },
    { id: 'lentes',      name: 'Lentes Sol',  icon: '🕶️' },
    { id: 'gato',        name: 'Orejas Gato', icon: '🐱' },
    { id: 'ovni',        name: 'Mini OVNI',   icon: '🛸' },
    { id: 'santa',       name: 'Gorro Santa', icon: '🎅' },
    { id: 'copa',        name: 'Sombrero Copa', icon: '🎩' },
    { id: 'pirata',      name: 'Pirata',      icon: '🏴‍☠️' },
    { id: 'samurai',     name: 'Samurái',     icon: '⚔️' },
    { id: 'astronauta',  name: 'Astronauta',  icon: '🧑‍🚀' },
    { id: 'buzo',        name: 'Escafandra',  icon: '🤿' },
    { id: 'alas',        name: 'Alas',        icon: '🪽' },
    { id: 'jetpack',     name: 'Jetpack',     icon: '🚀' },
    { id: 'demonio',     name: 'Demonio',     icon: '😈' },
    { id: 'capa',        name: 'Capa',        icon: '🦸' },
    { id: 'arquero',     name: 'Arquero',     icon: '🏹' },
    { id: 'bufanda',     name: 'Bufanda',     icon: '🧣' },
    { id: 'gladiador',   name: 'Gladiador',   icon: '🛡️' },
    { id: 'helice',      name: 'Gorro hélice', icon: '🌀' },
    { id: 'yukata',      name: 'Yukata',      icon: '👘' },
    { id: 'murcielago',  name: 'Murciélago',  icon: '🦇' },
    { id: 'alasmurcielago', name: 'Alas Murciélago', icon: '🦇' },
    { id: 'bata',        name: 'Bata',        icon: '🥼' },
    { id: 'brasita',     name: 'Disfraz Brasita',   icon: '🔥', grupo: 'disfraz' },
    { id: 'caparazon',   name: 'Disfraz Caparazón', icon: '🐢', grupo: 'disfraz' },
    { id: 'brotito',     name: 'Disfraz Brotito',   icon: '🌱', grupo: 'disfraz' },
    { id: 'chispa',      name: 'Disfraz Chispa',    icon: '⚡', grupo: 'disfraz' },
    { id: 'sombrita',    name: 'Disfraz Sombrita',  icon: '👻', grupo: 'disfraz' },
    { id: 'limpio',      name: 'Sin accesorio', icon: '∅' },
  ];
  const DISFRAZ_IDS = { brasita: 1, caparazon: 1, brotito: 1, chispa: 1, sombrita: 1 };

  /*
   * Skins de personaje.
   *
   * En la interfaz se llaman "Skin", pero en el código son `character`: acá
   * "skin" ya es el color del cuerpo (LS_SKIN, SKIN_PRESETS, setSkin), y dos
   * cosas distintas con el mismo nombre terminan pisándose.
   *
   * Un accesorio VISTE a Boberto y respeta lo que la persona eligió para el
   * cuerpo. Un personaje lo REEMPLAZA, con colores fijos: con uno activo el
   * motor ignora el color del cuerpo y de los ojos, el brillo y el accesorio.
   * El accesorio no se borra; queda guardado debajo y vuelve al sacar el
   * personaje.
   *
   * Los de serie "Materiales" son la excepción: siguen siendo su silueta, sólo
   * cambia la materia, así que el accesorio se dibuja encima. El color del
   * cuerpo y de los ojos sigue siendo el del material.
   *
   * Esta lista es lo que muestran las interfaces. El dibujo de cada uno vive en
   * CHAR_DEF, junto a los accesorios, porque usa sus mismos trazos.
   */
  const LS_CHARACTER = 'smg_boberto_character';
  const CHARACTERS = [
    { id: 'madera',         name: 'Madera y lianas', serie: 'Materiales',   icon: '🪵' },
    { id: 'talla',          name: 'Madera tallada', serie: 'Materiales',     icon: '🌀' },
    { id: 'gemas',          name: 'Gemas',          serie: 'Materiales',     icon: '💎' },
    { id: 'piedra',         name: 'Piedra y musgo', serie: 'Materiales',     icon: '🪨' },
    { id: 'kintsugi',       name: 'Kintsugi',       serie: 'Materiales',     icon: '🏺' },
    { id: 'lava',           name: 'Obsidiana y lava', serie: 'Materiales',   icon: '🌋' },
    { id: 'escarcha',       name: 'Escarcha',       serie: 'Materiales',     icon: '❄️' },
    { id: 'nebulosa',       name: 'Nebulosa',       serie: 'Materiales',     icon: '🌌' },
    { id: 'celestial',      name: 'Celestial',      serie: 'Materiales',     icon: '✨' },
  ];
  /* Los personajes de franquicia que se retiraron el 14/10/2026, y el disfraz
   * que los reemplaza. Los cuatro de Rick and Morty además habían sido
   * accesorios, así que pueden aparecer tanto en la clave del personaje como
   * en la del accesorio. Los que tienen un primo entre los disfraces pasan a
   * ese; el resto vuelve al Boberto de siempre con su accesorio. */
  const PERSONAJE_RETIRADO = {
    charmander: 'brasita', squirtle: 'caparazon', bulbasaur: 'brotito',
    pikachu: 'chispa', gengar: 'sombrita',
    rick: '', morty: '', rick_pepinillo: '', evil_morty: '', gojo: '', gojo_ojos: '',
    sukuna: '', goku: '', goku_ssj: '', goku_ssj4: '', krilin: '', freezer: '',
    cell: '', majin_buu: '',
  };
  const esRetirado = (id) => !!id && Object.prototype.hasOwnProperty.call(PERSONAJE_RETIRADO, id);
  const esCharacter = (id) => !!id && CHARACTERS.some(x => x.id === id);
  // La materia no reemplaza a Boberto: es la misma cabeza, así que el
  // accesorio calza. El resto de los personajes no.
  const esMateria = (id) => {
    const x = CHARACTERS.find(c => c.id === id);
    return !!(x && x.serie === 'Materiales');
  };
  // Paletas del accesorio. Viven acá y no en el panel porque ahora hay DOS
  // interfaces que las ofrecen —el panel de Ajustes y la vitrina de la
  // landing— y dos listas escritas a mano se separan en el próximo color.
  const HAT_COLOR_PRESETS = [
    { id: '#2a2c34', name: 'Grafito' }, { id: '#ffffff', name: 'Blanco' },
    { id: '#5b8cff', name: 'Azul' },    { id: '#10b981', name: 'Esmeralda' },
    { id: '#f59e0b', name: 'Ámbar' },   { id: '#f43f5e', name: 'Rojo' },
    { id: '#ec4899', name: 'Rosa' },    { id: '#bd93f9', name: 'Violeta' },
    { id: '#39ff14', name: 'Neón' },    { id: '#ff7a3d', name: 'Naranja' },
  ];
  // El detalle acepta además 'auto': el color natural que trae cada accesorio.
  const HAT_DETAIL_PRESETS = [
    { id: 'auto',    name: 'Natural' },
    { id: '#ffffff', name: 'Blanco' },  { id: '#f0b429', name: 'Oro' },
    { id: '#dc2626', name: 'Rojo' },    { id: '#5b8cff', name: 'Azul' },
    { id: '#10b981', name: 'Esmeralda' }, { id: '#ec4899', name: 'Rosa' },
    { id: '#bd93f9', name: 'Violeta' }, { id: '#39ff14', name: 'Neón' },
    { id: '#1b1f27', name: 'Negro' },
  ];
  const SKIN_PRESETS = [
    { id: 'terracota', name: 'Terracota', color: '#d96c3f' },
    { id: 'jira',      name: 'Jira Blue', color: '#2684ff' },
    { id: 'esmeralda', name: 'Esmeralda', color: '#10b981' },
    { id: 'violeta',   name: 'Violeta',   color: '#8b5cf6' },
    { id: 'ambar',     name: 'Ámbar',     color: '#f59e0b' },
    { id: 'rosa',      name: 'Rosa',      color: '#ec4899' },
  ];
  // Color de ojos. 'auto' = el ámbar/derivado que trae cada paleta.
  const EYE_PRESETS = [
    { id: '#ffd98a', name: 'Ámbar' },
    { id: '#ffffff', name: 'Blanco' },
    { id: '#7df9ff', name: 'Cyan' },
    { id: '#5fd38a', name: 'Verde' },
    { id: '#2684ff', name: 'Azul' },
    { id: '#ff5630', name: 'Rojo' },
    { id: '#c77dff', name: 'Violeta' },
  ];
  let skin = '', skinColor = '', eyeColor = '';
  try { skin = localStorage.getItem(LS_SKIN) || 'auto'; } catch (e) { skin = 'auto'; }
  try { skinColor = localStorage.getItem(LS_SKIN_COLOR) || '#d96c3f'; } catch (e) { skinColor = '#d96c3f'; }
  try { eyeColor = localStorage.getItem(LS_EYE) || 'auto'; } catch (e) { eyeColor = 'auto'; }
  // Accesorio por default. Es UNO solo y no depende del diseño de interfaz: el
  // accesorio es una elección de la persona, no una consecuencia del tema. Antes
  // el default se deducía de `data-skin`, así que estrenar otro diseño te cambiaba
  // el Boberto sin que lo pidieras.
  const HAT_DEFAULT = 'alasmurcielago';
  let hat = '';
  try { hat = localStorage.getItem(LS_HAT) || HAT_DEFAULT; } catch (e) { hat = HAT_DEFAULT; }

  // Personaje activo. '' es "sin personaje"; en storage se guarda 'none' para
  // distinguir "lo sacó a propósito" de "nunca eligió".
  let character = '';
  try {
    const guardado = localStorage.getItem(LS_CHARACTER);
    character = esCharacter(guardado) ? guardado : '';
    // Un personaje retirado: se saca, y si tiene disfraz primo se pone ese
    // como accesorio. Se persiste en el acto, para que el estudio no muestre
    // un id que ya no está en ningún catálogo.
    if (esRetirado(guardado)) {
      if (PERSONAJE_RETIRADO[guardado]) hat = PERSONAJE_RETIRADO[guardado];
      localStorage.setItem(LS_CHARACTER, 'none');
      localStorage.setItem(LS_HAT, hat);
    }
    // Rick y Morty también vivieron en la clave del accesorio.
    if (esRetirado(hat)) {
      hat = PERSONAJE_RETIRADO[hat] || HAT_DEFAULT;
      localStorage.setItem(LS_HAT, hat);
    }
  } catch (e) { character = ''; }

  // Color del accesorio (material principal del gorro/auriculares/etc.).
  let hatColor = '';
  try { hatColor = localStorage.getItem(LS_HAT_COLOR) || HAT_DARK; } catch (e) { hatColor = HAT_DARK; }
  // Segundo color del accesorio: el detalle (visera, pompón, gema, hoja de la
  // katana…). 'auto' = el color natural que trae cada accesorio, que es como se
  // veía antes de existir este eje. Así estrenarlo no le cambia el sombrero a
  // nadie: sólo lo desbloquea para el que lo quiera tocar.
  let hatColor2 = '';
  try { hatColor2 = localStorage.getItem(LS_HAT_COLOR2) || 'auto'; } catch (e) { hatColor2 = 'auto'; }
  // Colores del accesorio en curso (derivados de hatColor): material, realce, bulbo.
  let _hatBulb = HAT_BULB, _hatMat = HAT_DARK, _hatRealce = HAT_POM;

  // ── Efectos de "respiración glow" (combinables, por separado) ──────────
  // Dos targets independientes: 'body' (cuerpo/ojos/núcleo) y 'acc' (bulbo/LED
  // del accesorio). Cada uno guarda una lista de efectos activos. Sin efectos
  // = glow estático (comportamiento histórico).
  const LS_FX_BODY = 'smg_boberto_fx_body';
  const LS_FX_ACC  = 'smg_boberto_fx_acc';
  const FX_EFFECTS = [
    { id: 'pulso',    name: 'Pulse',    desc: 'Respiración suave del glow' },
    { id: 'latido',   name: 'Beat',     desc: 'Doble pulso tipo corazón' },
    { id: 'parpadeo', name: 'Blink',    desc: 'Titileo nervioso' },
    { id: 'halo',     name: 'Halo',     desc: 'Aura más amplia y constante' },
    { id: 'rgb',      name: 'RGB',      desc: 'Cicla el color tipo mouse RGB' },
  ];
  const _fxIds = FX_EFFECTS.map(e => e.id);
  function _readFx(key) {
    try {
      const arr = JSON.parse(localStorage.getItem(key) || '[]');
      return Array.isArray(arr) ? arr.filter(id => _fxIds.includes(id)) : [];
    } catch (e) { return []; }
  }
  let fxBody = _readFx(LS_FX_BODY);
  // Por defecto (sin nada guardado), el accesorio viene sin efectos (apagado).
  // Hacemos una migración única para limpiar el valor histórico '["rgb"]' si el usuario lo heredó.
  try {
    if (localStorage.getItem('smg_boberto_fx_acc_migrated_v1') === null) {
      if (localStorage.getItem(LS_FX_ACC) === '["rgb"]') {
        localStorage.removeItem(LS_FX_ACC);
      }
      localStorage.setItem('smg_boberto_fx_acc_migrated_v1', '1');
    }
  } catch (e) {}

  /*
   * El accesorio admite UN solo efecto: 'rgb'.
   *
   * Los otros cuatro se retiraron porque en un accesorio chico —un bulbo, un
   * LED, la punta de un arco— la diferencia entre respirar, latir y titilar no
   * se ve, y encima obligaba a elegirle un color de brillo aparte. RGB cicla
   * solo y es el unico que ahi se lee.
   *
   * El filtro NO es cosmetico: hay que sanear lo que quedo guardado. Quien
   * tenga ["pulso","halo"] de antes lo seguiria aplicando con el control ya
   * borrado del panel — un efecto encendido y sin forma de apagarlo. Es la
   * misma trampa que el eje `data-edge` huerfano de JiraToolkit, que esta
   * escrita en el CLAUDE.md del proyecto justamente para no repetirla.
   */
  const FX_ACC_PERMITIDOS = ['rgb'];
  let fxAcc;
  try { fxAcc = (localStorage.getItem(LS_FX_ACC) === null) ? [] : _readFx(LS_FX_ACC); }
  catch (e) { fxAcc = []; }
  const _fxAccLimpio = fxAcc.filter(id => FX_ACC_PERMITIDOS.includes(id));
  // El saneo se PERSISTE, no solo se aplica en memoria. Si quedara el valor
  // viejo guardado y algun dia volvieran los efectos al accesorio, se le
  // reactivarian de golpe los que tenia hace meses. Un estado que reaparece
  // solo es peor que uno que se pierde.
  if (_fxAccLimpio.length !== fxAcc.length) {
    try { localStorage.setItem(LS_FX_ACC, JSON.stringify(_fxAccLimpio)); } catch (e) {}
  }
  fxAcc = _fxAccLimpio;

  // Color del neón (glow) por target. 'auto' = derivado del color base.
  const LS_GLOW_BODY = 'smg_boberto_glow_body';
  const LS_GLOW_ACC  = 'smg_boberto_glow_acc';
  let glowBody = '', glowAcc = '';
  try { glowBody = localStorage.getItem(LS_GLOW_BODY) || 'auto'; } catch (e) { glowBody = 'auto'; }
  // El accesorio ya no tiene selector de color de brillo, asi que su unico valor
  // valido es 'auto' (derivado del color del accesorio). Se fuerza en vez de
  // leerlo, y se borra la clave: un hex guardado de antes seguiria pintando sin
  // control que lo cambie, y dejarlo en storage es un estado que reaparece solo
  // el dia que el selector vuelva.
  glowAcc = 'auto';
  try { localStorage.removeItem(LS_GLOW_ACC); } catch (e) {}
  // Paleta del color de brillo del cuerpo. Vivía escrita en el panel de
  // Ajustes; se mudó acá cuando ese control pasó al estudio, por la misma
  // razón que las paletas del accesorio: quien ofrece la lista es el motor.
  const GLOW_PRESETS = [
    { id: 'auto',    name: 'Auto' },
    { id: '#39ff14', name: 'Neón' },    { id: '#2684ff', name: 'Azul' },
    { id: '#10b981', name: 'Esmeralda' }, { id: '#f59e0b', name: 'Ámbar' },
    { id: '#f43f5e', name: 'Rojo' },    { id: '#ec4899', name: 'Rosa' },
    { id: '#bd93f9', name: 'Violeta' }, { id: '#7df9ff', name: 'Cyan' },
    { id: '#ffffff', name: 'Blanco' },  { id: '#ff7a3d', name: 'Naranja' },
  ];

  // Multiplicadores de glow vivos (1 = estático). Los lee drawF cada vez.
  let _bodyM = 1, _accM = 1;
  // Offsets de matiz para el efecto RGB (grados). Los avanza el ticker.
  let _bodyHue = 0, _accHue = 0;
  const _fxHas = (list, id) => list.indexOf(id) >= 0;
  // Rota el matiz de un color hex en `deg` grados (memoizado).
  const _rotCache = {};
  function rotHue(hex, deg) {
    const key = hex + '|' + (deg | 0);
    if (_rotCache[key]) return _rotCache[key];
    const { h, s, l } = hexToHsl(hex);
    return (_rotCache[key] = hslToHex(h + deg, s, l));
  }

  // Combina los efectos de una lista en un multiplicador para el instante t (ms).
  // null cuando la lista está vacía → el caller usa el glow estático.
  function fxMul(effects, t) {
    if (!effects || !effects.length) return null;
    let m = 1, any = false;
    for (let i = 0; i < effects.length; i++) {
      const e = effects[i];
      if (e === 'rgb') continue; // 'rgb' recolorea, no modula la intensidad
      any = true;
      if (e === 'pulso') {
        // Respiracion: lenta y PROFUNDA. Antes bajaba solo a 0.45 y el halo
        // nunca se apagaba lo suficiente para que se notara el vaiven.
        m *= 0.30 + 0.70 * (0.5 + 0.5 * Math.sin(t / 1600));
      } else if (e === 'latido') {
        // Dos golpes secos y una pausa larga, como un corazon. El pico sube por
        // ENCIMA de 1 a proposito: lo que distingue un latido de una
        // respiracion es el golpe, no el promedio.
        const p = (t % 1100) / 1100;
        const beat = Math.exp(-Math.pow((p - 0.10) / 0.055, 2))
                   + 0.72 * Math.exp(-Math.pow((p - 0.30) / 0.055, 2));
        m *= 0.22 + 1.45 * Math.min(1, beat);
      } else if (e === 'parpadeo') {
        // Se APAGA de verdad entre destellos. Antes su valle era 0.6, asi que
        // se leia como un temblor y no como un parpadeo.
        const p = (t % 900) / 900;
        m *= (p < 0.10 || (p > 0.20 && p < 0.28)) ? 1.15 : 0.10;
      } else if (e === 'halo') {
        // Halo: no anima, ENSANCHA. Estaba en 1.35, el mismo valor que el pico
        // de pulso, asi que no se distinguia de el.
        m *= 2.10;
      }
    }
    // Piso bajo a proposito: 'parpadeo' necesita poder apagarse casi del todo,
    // y con el piso anterior (0.15 sobre un rango que empezaba en 0.4) no habia
    // margen para que ningun efecto se notara distinto de los otros.
    return any ? Math.max(0.06, m) : null;
  }

  // ── Color utils (HSL) ─────────────────────────────────────────────────
  const _clamp = (v, a, b) => Math.max(a, Math.min(b, v));
  function hexToHsl(hex) {
    hex = String(hex).replace('#', '');
    if (hex.length === 3) hex = hex.split('').map(c => c + c).join('');
    const r = parseInt(hex.slice(0, 2), 16) / 255, g = parseInt(hex.slice(2, 4), 16) / 255, b = parseInt(hex.slice(4, 6), 16) / 255;
    const mx = Math.max(r, g, b), mn = Math.min(r, g, b); let h = 0, s = 0; const l = (mx + mn) / 2;
    if (mx !== mn) {
      const d = mx - mn; s = l > 0.5 ? d / (2 - mx - mn) : d / (mx + mn);
      if (mx === r) h = (g - b) / d + (g < b ? 6 : 0); else if (mx === g) h = (b - r) / d + 2; else h = (r - g) / d + 4;
      h *= 60;
    }
    return { h, s: s * 100, l: l * 100 };
  }
  function hslToHex(h, s, l) {
    s = _clamp(s, 0, 100) / 100; l = _clamp(l, 0, 100) / 100; h = ((h % 360) + 360) % 360;
    const c = (1 - Math.abs(2 * l - 1)) * s, x = c * (1 - Math.abs((h / 60) % 2 - 1)), m = l - c / 2;
    let r = 0, g = 0, b = 0;
    if (h < 60) [r, g, b] = [c, x, 0]; else if (h < 120) [r, g, b] = [x, c, 0];
    else if (h < 180) [r, g, b] = [0, c, x]; else if (h < 240) [r, g, b] = [0, x, c];
    else if (h < 300) [r, g, b] = [x, 0, c]; else [r, g, b] = [c, 0, x];
    const to = v => Math.round((v + m) * 255).toString(16).padStart(2, '0');
    return '#' + to(r) + to(g) + to(b);
  }
  // Deriva la paleta completa de Boberto a partir de un color de cuerpo.
  // Cuerpo/contornos siguen el color; ojos ámbar y LEDs verdes quedan fijos
  // para mantener el carácter de la mascota.
  function derivePal(base) {
    const { h, s, l } = hexToHsl(base);
    return {
      _: null,
      F: base,
      W: hslToHex(h, s, l - 18),
      O: hslToHex(h, s + 4, l - 34),
      E: hslToHex(h, s - 28, l + 28),
      R: hslToHex(h, s - 4, l - 26),
      B: hslToHex(h, s - 8, l - 30),
      D: hslToHex(h, s - 40, _clamp(l - 44, 8, 22)),
      C: '#ffd98a', M: '#ffb238', G: '#5fd38a', J: '#ff5630', K: '#ff8b00',
      glow: hslToHex(h, s, l + 6),
      glowA: hslToHex(h, s, l + 6) + '40',
    };
  }
  const _derivedCache = {};
  function derivePalCached(color) { return _derivedCache[color] || (_derivedCache[color] = derivePal(color)); }

  function getBasePal() {
    if (skin === 'auto') {
      const t = document.documentElement.getAttribute('data-theme') || '';
      return PALS[t] || PALS.default;
    }
    if (skin === 'custom') return derivePalCached(skinColor);
    const p = SKIN_PRESETS.find(x => x.id === skin);
    return p ? derivePalCached(p.color) : PALS.default;
  }

  // Aplica el color de ojos elegido sobre la paleta base. Memoiza por
  // (base, eyeColor) para que el loop principal mantenga estable la
  // referencia (np === pal) y no reconstruya frames en cada iteración.
  let _palMemo = { base: null, eye: null, out: null };
  function getPal() {
    const base = getBasePal();
    if (_palMemo.base === base && _palMemo.eye === eyeColor) return _palMemo.out;
    const out = eyeColor === 'auto' ? base : Object.assign({}, base, { C: eyeColor });
    _palMemo = { base, eye: eyeColor, out };
    return out;
  }

  let enabled      = localStorage.getItem(LS_KEY) !== 'false';
  // Lo antes posible: el CSS de las apps lee el atributo para no pintar ni un
  // cuadro de una peana que no va (ver sincronizarApagado).
  publicarApagado();
  let adviceEnabled = localStorage.getItem('smg_mascota_advice') !== 'false';
  let paused    = false;
  // Boberto esta prestado a la VITRINA (mountStage) y no al dock de la sidebar.
  //
  // Hay dos vitrinas: la de la landing, que no tiene sidebar, y el estudio que
  // abre el dock al tocarlo (shared/js/boberto-estudio.js). Las peanas de las
  // apps NO pasan por aca: cada una dibuja su propio canvas con un espejo (ver
  // crearEspejo), justamente para que la mascota del dock no se mueva de ahi.
  //
  // La bandera es necesaria porque el bucle del dock y la adopcion de slots
  // tienen que ceder cuando el wrap se muda: si no, un renderSidebar() de
  // JiraToolkit a mitad del estudio se lo llevaria de vuelta a la sidebar.
  let _staged   = false;
  let isBusy    = false;
  let isNear    = false;

  let pal = getPal();
  resolveHatColors();

  /* El avatar del chatbot ya no tiene estado propio: es un espejo, igual que
     las peanas. Tenia cinco variables sueltas y su propio bucle de ochenta
     lineas — cuando hubo que poner a Boberto en once huecos mas, ese bucle
     era justo lo que habia que generalizar en vez de copiar. Ver
     crearEspejo(). */

  // ══ Boberto 2.0 — arte vectorial ══════════════════════════════════════
  //
  // Reemplaza la grilla de 16×18 celdas por curvas, gradientes y brillos
  // especulares. El espacio de diseño SIGUE siendo 16×18, pero ahora las
  // unidades son continuas: `s` dejó de ser "lado de la celda" y pasó a ser
  // "píxeles por unidad". Las firmas no cambiaron a propósito — chatbot.js y
  // finder.js nos pasan un <canvas> y una escala, y así siguen andando.
  //
  // Se dibuja sobre canvas y no sobre SVG por esa misma razón: las apps ya
  // tienen el <canvas> en la mano, y las animaciones CSS (plt-idle, plt-squash,
  // plt-levita, plt-skatehop…) apuntan a `#plt-wrap canvas`. Cambiar el elemento
  // costaba romper esas tres cosas sin ganar nada — Canvas2D dibuja las mismas
  // curvas que SVG.
  //
  // Lo que sí murió con la grilla: `avoidCollision()`. Existía porque el pintor
  // viejo reconocía el ROL de un píxel comparando su color, así que dos roles
  // que caían en el mismo hex se pisaban. Acá el rol se pasa como argumento.

  // Espacio de diseño. Sigue siendo el mismo footprint que ocupaba la grilla,
  // así que quien lo hospeda no se entera del cambio.
  // Caja del cuerpo, en unidades. El personaje es una gota: panza ancha abajo,
  // hombros apenas más angostos arriba.
  const BODY = { cx: 8, top: 3.30, bot: 15.05, rx: 6.10 };

  /**
   * Mezcla dos colores hex.
   *
   * Hace falta además de adjustL() porque subir lightness sobre un cuerpo muy
   * saturado da un brillo fluorescente: la luz real también desatura. Los tonos
   * claros se mezclan hacia blanco, no se "suben".
   */
  function mixHex(a, b, t) {
    const pa = parseInt(a.slice(1), 16), pb = parseInt(b.slice(1), 16);
    const ch = (sh) => {
      const va = (pa >> sh) & 255, vb = (pb >> sh) & 255;
      return Math.round(va + (vb - va) * t);
    };
    const to = v => v.toString(16).padStart(2, '0');
    return '#' + to(ch(16)) + to(ch(8)) + to(ch(0));
  }
  function withA(hex, a) {
    const p = parseInt(hex.slice(1), 16);
    return 'rgba(' + ((p >> 16) & 255) + ',' + ((p >> 8) & 255) + ',' + (p & 255) + ',' + a + ')';
  }

  /**
   * Paleta del pintor, derivada de la paleta de la app.
   *
   * Se DERIVA y no se declara suelta para que Boberto siga al tema: cambiar de
   * skin cambia `pal.F` y todo el sombreado se recalcula solo. Con tonos
   * literales, cambiar de tema lo dejaría con el cuerpo nuevo y las luces del
   * tema anterior — que es exactamente el bug que nadie reporta y todos ven.
   */
  function vecPal(st) {
    return vestirDisfraz(vecPalBase(st), st);
  }

  /*
   * Con un disfraz puesto, brazos, manos y pies son del traje. Las mangas y
   * las patas los tapan en reposo, pero hay manos que dibuja el gesto
   * (saludar, la laptop, el puntero) encima de todo: sin esto la mano que
   * saluda salía del color del cuerpo, como si el traje no tuviera mangas.
   * Va acá y no en el disfraz para que le llegue a todo el que pinte.
   */
  function vestirDisfraz(P, st) {
    if (!DISFRAZ_IDS[hat]) return P;
    const ch = charActual();
    if (ch && !esMateria(character)) return P;
    const tela = accPal(st).mat;
    const luz = mixHex(tela, '#ffffff', 0.12), sombra = adjustL(tela, -26);
    return Object.assign({}, P, { armA: luz, armB: sombra, handA: luz, handB: sombra, feetA: tela, feetB: sombra });
  }

  function vecPalBase(st) {
    // Un personaje trae su paleta fija: ni el tema, ni el color elegido, ni el
    // color de ojos, ni el ciclado RGB del cuerpo lo alcanzan.
    const ch = charActual();
    if (ch) return pintorCharacter(ch);
    const rot = (st && st.bodyHue) ? (c) => rotHue(c, st.bodyHue) : (c) => c;
    // 'auto' = el ámbar que trae la paleta del skin. Ahora que el iris se ve,
    // este valor pasó de decorativo a visible: es el default y tiene que
    // seguir al tema, no ser un color fijo.
    const tint = (eyeColor && eyeColor !== 'auto') ? eyeColor : rot(pal.C);
    return completarPintor(pintorDe(rot(pal.F), tint, {
      prop:   rot(pal.J || '#ff5630'),
      prop2:  rot(pal.K || '#ff8b00'),
      scan:   rot(pal.G || '#5fd38a'),
      accent: rot(pal.M || '#ffb238'),
    }));
  }

  /**
   * El sombreado completo a partir de un color de cuerpo y un tinte de ojos.
   *
   * Es la misma derivación para Boberto y para los personajes: así un
   * personaje sólo declara los tonos que lo distinguen (el amarillo de
   * Pikachu, los brazos de Morty) y el resto —brillo, rim light, sombra—
   * sale con la misma luz que el Boberto de siempre.
   */
  function pintorDe(F, tint, extra) {
    return Object.assign({
      core:  F,
      lift:  mixHex(adjustL(F, 22), '#ffffff', 0.18),
      gloss: mixHex(adjustL(F, 34), '#ffffff', 0.45),
      mid:   adjustL(F, -7),
      deep:  adjustL(F, -21),
      edge:  adjustL(F, -33),
      rim:   mixHex(adjustL(F, 26), '#ffffff', 0.30),
      // Ojo casi negro pero TEÑIDO del cuerpo. Un negro puro sobre un cuerpo
      // cálido se ve pegoteado, como un agujero en vez de un ojo.
      eye:     mixHex(adjustL(F, -46), '#07050a', 0.72),
      eyeEdge: mixHex(adjustL(F, -38), '#0d090e', 0.55),
      eyeLift: mixHex(adjustL(F, -42), tint, 0.16),
      glint:   mixHex(tint, '#ffffff', 0.55),
      tint:    tint,
      shadow:  adjustL(F, -40),
    }, extra);
  }

  /**
   * Brazos, pies y mano del puntero.
   *
   * En Boberto salen del mismo cuerpo, así que por defecto se copian de él y
   * el dibujo es idéntico al de siempre. Existen como tonos aparte porque en
   * un personaje NO son el cuerpo: a Rick los brazos y los pies le quedaban
   * del color elegido por la persona, que es justo lo que un personaje no
   * tiene que heredar.
   */
  function completarPintor(P) {
    if (!P.armA)  P.armA  = P.core;
    if (!P.armB)  P.armB  = P.deep;
    if (!P.feetA) P.feetA = P.mid;
    if (!P.feetB) P.feetB = P.edge;
    if (!P.handA) P.handA = P.armA;
    if (!P.handB) P.handB = P.armB;
    return P;
  }

  // Colores de utilería de un personaje (laptop, skate, burbuja…): los de la
  // paleta por defecto, fijos, para que tampoco los mueva el tema.
  const UTILERIA_FIJA = { prop: '#ff5630', prop2: '#ff8b00', scan: '#5fd38a', accent: '#ffb238' };
  // Memo por la DEFINICIÓN del personaje, no por un id: las entradas de
  // CHAR_DEF no lo llevan adentro y dos personajes no pueden compartir paleta.
  const _pintorChar = new Map();
  function pintorCharacter(ch) {
    if (_pintorChar.has(ch)) return _pintorChar.get(ch);
    const d = ch.pal || {};
    const P = pintorDe(d.base, d.tint || '#ffd98a', UTILERIA_FIJA);
    Object.keys(d).forEach(k => { if (k !== 'base' && k !== 'tint') P[k] = d[k]; });
    completarPintor(P);
    _pintorChar.set(ch, P);
    return P;
  }

  // ── Trazos base ───────────────────────────────────────────────────────

  /**
   * La gota. Dos elipses pegadas: media superior más angosta y alta, media
   * inferior más ancha y corta — la panza.
   *
   * K es el factor kappa, el largo de tirante que hace que una bézier cúbica
   * aproxime un cuarto de círculo. Con tirantes arbitrarios los laterales
   * salen casi verticales y el fondo plano, y el personaje deja de leerse como
   * una gota para leerse como un rectángulo redondeado. Se probó.
   */
  // `seguir` agrega la gota al trazado en curso en vez de empezar uno nuevo:
  // para recortar lo que queda FUERA del cuerpo, con un rectángulo y evenodd.
  const K = 0.5523;
  function blobPath(c, cx, top, bot, rx, seguir) {
    const h = bot - top;
    const my = top + h * 0.52;          // línea del ecuador
    const th = my - top, bh = bot - my;
    const trx = rx * 0.94;              // hombros apenas más angostos
    if (!seguir) c.beginPath();
    c.moveTo(cx, top);
    c.bezierCurveTo(cx + trx * K, top,        cx + trx, my - th * K, cx + trx, my);
    c.bezierCurveTo(cx + rx,  my + bh * K,    cx + rx * K,  bot,     cx, bot);
    c.bezierCurveTo(cx - rx * K, bot,         cx - rx, my + bh * K,  cx - rx, my);
    c.bezierCurveTo(cx - trx, my - th * K,    cx - trx * K, top,     cx, top);
    c.closePath();
  }

  function ellipsePath(c, x, y, rx, ry) {
    c.beginPath();
    c.ellipse(x, y, Math.max(rx, 0.01), Math.max(ry, 0.01), 0, 0, Math.PI * 2);
  }

  function roundRect(c, x, y, w, h, r) {
    const rr = Math.max(0, Math.min(r, w / 2, h / 2));
    c.beginPath();
    c.moveTo(x + rr, y);
    c.arcTo(x + w, y,     x + w, y + h, rr);
    c.arcTo(x + w, y + h, x,     y + h, rr);
    c.arcTo(x,     y + h, x,     y,     rr);
    c.arcTo(x,     y,     x + w, y,     rr);
    c.closePath();
  }

  // Motas del cuerpo. Deterministas a propósito: sorteadas por frame, el cuerpo
  // hormiguearía en cada parpadeo.
  const SPECKS = [
    [-3.1, -1.2, 0.20], [2.6, 0.4, 0.17], [-1.4, 2.9, 0.22], [3.4, 3.3, 0.15],
    [-3.8, 1.8, 0.14],  [1.1, 4.4, 0.18], [-2.2, 4.7, 0.13], [4.1, 1.2, 0.12],
  ];

  // ── Cuerpo ────────────────────────────────────────────────────────────
  function drawBody(c, P, pose) {
    const cx  = BODY.cx;
    const top = BODY.top + (pose.bodyDY || 0);
    const bot = BODY.bot + (pose.bodyDY || 0);
    const rx  = BODY.rx * (pose.bodyRX || 1);
    const h   = bot - top;
    const cy  = top + h * 0.5;

    // Sombra de contacto. Sin ella el personaje flota y se nota enseguida.
    c.save();
    c.globalAlpha = 0.28;
    ellipsePath(c, cx, BODY.bot + 1.5, rx * 0.80, 0.60);
    c.fillStyle = P.shadow;
    c.fill();
    c.restore();

    // Volumen: radial con la luz arriba a la izquierda.
    blobPath(c, cx, top, bot, rx);
    const g = c.createRadialGradient(
      cx - rx * 0.38, top + h * 0.22, rx * 0.10,
      cx,             cy + h * 0.10,  rx * 1.42
    );
    g.addColorStop(0.00, P.lift);
    g.addColorStop(0.42, P.core);
    g.addColorStop(0.80, P.mid);
    g.addColorStop(1.00, P.deep);
    c.fillStyle = g;
    c.fill();

    // Todo lo que sigue vive DENTRO de la silueta.
    c.save();
    c.clip();

    c.fillStyle = withA(P.gloss, 0.10);
    SPECKS.forEach(sp => { ellipsePath(c, cx + sp[0], cy + sp[1], sp[2], sp[2]); c.fill(); });

    // Oclusión del borde inferior: hunde la panza y despega la silueta del fondo.
    const og = c.createLinearGradient(0, cy, 0, bot);
    og.addColorStop(0, withA(P.edge, 0));
    og.addColorStop(1, withA(P.edge, 0.55));
    c.fillStyle = og;
    c.fillRect(cx - rx, cy, rx * 2, h);

    // Gloss superior: el brillo ancho y suave.
    c.save();
    c.globalAlpha = 0.55;
    const gg = c.createRadialGradient(
      cx - rx * 0.30, top + h * 0.14, 0.2,
      cx - rx * 0.30, top + h * 0.14, rx * 0.92
    );
    gg.addColorStop(0, withA(P.gloss, 0.85));
    gg.addColorStop(1, withA(P.gloss, 0));
    c.fillStyle = gg;
    ellipsePath(c, cx - rx * 0.26, top + h * 0.17, rx * 0.72, h * 0.20);
    c.fill();
    c.restore();

    // Rim light pegado al borde inferior-derecho. Es lo que convierte una
    // mancha de color en un volumen.
    c.lineWidth = 0.95;
    const rg = c.createLinearGradient(cx - rx, top, cx + rx, bot);
    rg.addColorStop(0.00, withA(P.rim, 0));
    rg.addColorStop(0.48, withA(P.rim, 0));
    rg.addColorStop(0.78, withA(P.rim, 0.85));
    rg.addColorStop(1.00, withA(P.rim, 0.25));
    c.strokeStyle = rg;
    blobPath(c, cx, top, bot, rx);
    c.stroke();

    c.restore();

    // Contorno exterior finito: lo separa del topbar sin dibujarle un marco.
    blobPath(c, cx, top, bot, rx);
    c.lineWidth = 0.42;
    c.strokeStyle = withA(P.edge, 0.75);
    c.stroke();
  }

  // ── Patas ─────────────────────────────────────────────────────────────
  // Dónde van los dos pies en la pose. Aparte de drawFeet porque los
  // personajes que les agregan tinta o suela los necesitan en el mismo lugar.
  function lugarPies(pose) {
    // `pies` los fija en el lienzo, sin seguir al cuerpo: en el skate un pie
    // queda plantado en la tabla (o empuja en el piso) mientras el cuerpo se
    // agacha o salta. Van por acá para que la tinta y las suelas de cada
    // personaje los acompañen sin enterarse.
    if (pose.pies) return pose.pies.map(p => [p[0], p[1]]);
    const y  = BODY.bot + (pose.bodyDY || 0) + 0.55;
    const ph = pose.feet || 0;
    const off = ph === 1 ? [-0.5, 0.3] : ph === 2 ? [0.4, -0.45] : [0, 0];
    return [[BODY.cx - 2.05, y + off[0] * 0.5], [BODY.cx + 2.05, y + off[1] * 0.5]];
  }

  // Muñones cortos bajo la panza. `feet` es la fase de caminata.
  function drawFeet(c, P, pose) {
    if (pose.hideFeet) return;
    const y  = BODY.bot + (pose.bodyDY || 0) + 0.55;
    lugarPies(pose).forEach(([x, fy]) => {
      ellipsePath(c, x, fy, 1.15, 0.80);
      // Con pies fijados cada uno lleva su propio degradé: el que empuja en el
      // piso queda lejos de la altura común. Sin `pies`, el de siempre.
      const gy = pose.pies ? fy : y;
      const g = c.createLinearGradient(0, gy - 0.9, 0, gy + 0.9);
      g.addColorStop(0, P.feetA);
      g.addColorStop(1, P.feetB);
      c.fillStyle = g;
      c.fill();
    });
  }

  // ── Brazos ────────────────────────────────────────────────────────────
  // Un brazo del motor: `lado` -1 es el izquierdo, 1 el derecho; `up`, en
  // alto (el saludo). Aparte de drawArms porque un personaje con pelo o
  // cresta encima del brazo lo vuelve a pintar delante (brazoAlFrente).
  function pintarBrazo(c, P, pose, lado, up) {
    const cy = BODY.top + (BODY.bot - BODY.top) * 0.60 + (pose.bodyDY || 0);
    const dx = lado * BODY.rx * (pose.bodyRX || 1) * 0.92;
    const ch = charActual();
    c.save();
    c.translate(BODY.cx + dx, up ? cy - 2.7 : cy);
    c.rotate(up ? (dx > 0 ? -0.8 : 0.8) : (dx > 0 ? -0.18 : 0.18));
    ellipsePath(c, 0, 0, 1.00, 1.30);
    const g = c.createLinearGradient(-1, -1.3, 1, 1.3);
    g.addColorStop(0, P.armA);
    g.addColorStop(1, P.armB);
    c.fillStyle = g;
    c.fill();
    // Lo que el personaje lleva en el brazo (manos, marcas), en las
    // coordenadas del muñón: así sigue al brazo cuando saluda.
    if (ch && ch.brazo) ch.brazo(c, P, { side: dx > 0 ? 1 : -1, up: !!up });
    c.restore();
  }
  function drawArms(c, P, pose) {
    if (pose.armL !== false) pintarBrazo(c, P, pose, -1, pose.arm === 'L');
    if (pose.armR !== false) pintarBrazo(c, P, pose, 1, pose.arm === 'R');
  }

  // ── Ojos ──────────────────────────────────────────────────────────────
  //
  // Grandes, oscuros y brillantes: cargan toda la expresión. El párpado NO se
  // dibuja como tapa encima — se cambia la FORMA del ojo. A 36 píxeles de alto
  // una tapa de un píxel no se lee; un arco sí.
  function drawEyes(c, P, pose) {
    // Un personaje puede no tener ojos a la vista (Gojo con la venda): ahí no
    // hay parpadeo ni mirada que dibujar, y pintarlos encima de la venda sería
    // peor que no tenerlos.
    const ch = charActual();
    if (ch && ch.sinOjos) return;
    const gx   = (pose.gaze ? pose.gaze[0] : 0) * 0.62;
    const gy   = (pose.gaze ? pose.gaze[1] : 0) * 0.48;
    // `ojos` corre la posición de reposo: los ojos de Pikachu o de Sukuna no
    // están donde los tiene Boberto. La mirada y el salto se suman igual.
    const lugar = ch && ch.ojos;
    const eyeY = (lugar ? lugar.y : BODY.top + (BODY.bot - BODY.top) * 0.38) + (pose.bodyDY || 0) + gy;
    const sep  = lugar ? lugar.sep : 2.28, rx = 1.82, ry = 2.16;

    const shape = (side) => {
      const k = pose.eyes || 'open';
      if (k === 'wink')  return side < 0 ? 'shut' : 'open';
      if (k === 'winkR') return side > 0 ? 'shut' : 'open';
      return k;
    };

    [-1, 1].forEach(side => {
      const x = BODY.cx + side * sep + gx;
      const s = shape(side);
      const sy = s === 'wide' ? 1.14 : 1;

      // El personaje dibuja su propio ojo pero conserva parpadeos, guiños y
      // mirada: recibe la forma que pide la pose. Si devuelve false, esa forma
      // la resuelve el ojo de Boberto (el corazón y la espiral, en general).
      // gx/gy van aparte porque x/y ya los incluyen: un ojo que no se mueve
      // entero (el de Gojo, el de Sukuna) los resta y corre sólo el iris.
      if (ch && ch.ojo && ch.ojo(c, P, { side, x, y: eyeY, forma: s, sy, rx, ry, gx, gy, pose })) return;

      // ── Ojos que no son un óvalo: corazón y espiral ──
      if (s === 'heart') {
        const r = rx * 1.06;
        c.beginPath();
        c.moveTo(x, eyeY + r * 0.95);
        c.bezierCurveTo(x - r * 1.35, eyeY - r * 0.10, x - r * 0.60, eyeY - r * 1.20, x, eyeY - r * 0.35);
        c.bezierCurveTo(x + r * 0.60, eyeY - r * 1.20, x + r * 1.35, eyeY - r * 0.10, x, eyeY + r * 0.95);
        c.closePath();
        const hg = c.createLinearGradient(0, eyeY - r, 0, eyeY + r);
        hg.addColorStop(0, mixHex(P.tint, '#ffffff', 0.35));
        hg.addColorStop(1, P.tint);
        c.fillStyle = hg; c.fill();
        ellipsePath(c, x - r * 0.42, eyeY - r * 0.42, r * 0.22, r * 0.17);
        c.fillStyle = withA('#ffffff', 0.85); c.fill();
        return;
      }
      if (s === 'spiral') {
        c.beginPath();
        c.lineWidth = 0.40; c.lineCap = 'round';
        c.strokeStyle = P.eye;
        for (let t = 0; t <= Math.PI * 4.2; t += 0.22) {
          const rr = 0.16 + t * 0.135;
          // `giroEspiral` la hace girar de un cuadro al otro (el mareo).
          const px = x + Math.cos(t + side * 1.4 + (pose.giroEspiral || 0)) * rr;
          const py = eyeY + Math.sin(t + side * 1.4 + (pose.giroEspiral || 0)) * rr * 0.92;
          t === 0 ? c.moveTo(px, py) : c.lineTo(px, py);
        }
        c.stroke();
        return;
      }

      if (s === 'shut' || s === 'happy') {
        c.beginPath();
        const w = rx * 1.05, k = s === 'happy' ? -1.15 : 0.95;
        c.moveTo(x - w, eyeY + (s === 'happy' ? 0.55 : 0));
        c.quadraticCurveTo(x, eyeY + k, x + w, eyeY + (s === 'happy' ? 0.55 : 0));
        c.lineWidth = 0.72;
        c.lineCap = 'round';
        c.strokeStyle = P.eye;
        c.stroke();
        return;
      }

      // Halo: apoya el ojo sobre el cuerpo en vez de recortarlo en seco.
      ellipsePath(c, x, eyeY, rx * 1.20, ry * sy * 1.16);
      c.fillStyle = withA(P.eyeEdge, 0.42);
      c.fill();

      c.save();
      // Si el ojo está achinado (squint), simulamos los párpados cortando el área de dibujo
      if (s === 'squint') {
        c.beginPath();
        // Un rectángulo que recorta el ojo dejándolo en un 55% de su altura original
        c.rect(x - rx * 1.5, eyeY - ry * 0.25, rx * 3, ry * 0.55);
        c.clip();
      }

      ellipsePath(c, x, eyeY, rx, ry * sy);
      const g = c.createLinearGradient(0, eyeY - ry * sy, 0, eyeY + ry * sy);
      g.addColorStop(0.00, P.eyeEdge);
      g.addColorStop(0.45, P.eye);
      g.addColorStop(1.00, P.eyeLift);   // rebote del piso en el borde inferior
      c.fillStyle = g;
      c.fill();

      c.save();
      c.clip();

      /**
       * Iris.
       *
       * Existe para que el color de ojos del panel se VEA. Antes ese ajuste
       * sólo movía el rebote del borde inferior y la chispa chica: elegías
       * cyan y no pasaba nada, así que el control estaba de adorno.
       *
       * Es un anillo y no un disco a propósito. El personaje tiene que seguir
       * leyendo con ojos oscuros y vidriosos como la referencia; un disco de
       * color pleno lo convierte en otro personaje. El anillo se ve —incluso a
       * 32 píxeles de alto, porque va contra el negro— sin comerse la pupila.
       */
      const irisR = rx * 0.74;
      const irisY = eyeY + ry * sy * 0.06;
      const ig = c.createRadialGradient(x, irisY, irisR * 0.28, x, irisY, irisR);
      ig.addColorStop(0.00, withA(P.tint, 0));
      ig.addColorStop(0.52, withA(P.tint, 0.10));
      ig.addColorStop(0.80, withA(mixHex(P.tint, '#ffffff', 0.25), 0.92));
      ig.addColorStop(1.00, withA(adjustL(P.tint, -26), 0.85));
      ellipsePath(c, x, irisY, irisR, irisR * 0.94);
      c.fillStyle = ig;
      c.fill();

      // Pupila: se come el centro del iris y devuelve la mirada al negro.
      ellipsePath(c, x, irisY, irisR * 0.52, irisR * 0.56);
      c.fillStyle = withA(P.eye, 0.96);
      c.fill();

      // Especular grande arriba-izquierda + chispa chica abajo-derecha. Las dos
      // juntas leen como "húmedo"; una sola lee como plástico.
      ellipsePath(c, x - rx * 0.34, eyeY - ry * sy * 0.42, rx * 0.31, ry * sy * 0.26);
      c.fillStyle = withA('#ffffff', 0.95);
      c.fill();
      ellipsePath(c, x + rx * 0.36, eyeY + ry * sy * 0.34, rx * 0.155, ry * sy * 0.135);
      c.fillStyle = withA(P.glint, 0.72);
      c.fill();
      
      c.restore(); // Restaura el clip del iris
      c.restore(); // Restaura el clip de los párpados (squint)
    });
  }

  // ── Boca ──────────────────────────────────────────────────────────────
  function drawMouth(c, P, pose) {
    // Un personaje con boca propia la dibuja entera, con o sin `pose.mouth`:
    // la sonrisa de Sukuna o la mueca de Gojo son parte de la cara, no un gesto.
    const ch = charActual();
    if (ch && ch.boca) { ch.boca(c, P, pose); return; }
    if (!pose.mouth) return;
    const y = BODY.top + (BODY.bot - BODY.top) * 0.63 + (pose.bodyDY || 0);
    if (pose.mouth === 'o') {
      ellipsePath(c, BODY.cx, y + 0.25, 0.50, 0.64);
      c.fillStyle = withA(P.eye, 0.80);
      c.fill();
      return;
    }
    c.lineWidth = 0.52;
    c.lineCap = 'round';
    c.strokeStyle = withA(P.eye, 0.85);
    c.beginPath();
    if (pose.mouth === 'smile') {
      c.moveTo(BODY.cx - 1.10, y - 0.15);
      c.quadraticCurveTo(BODY.cx, y + 0.90, BODY.cx + 1.10, y - 0.15);
    } else {
      c.moveTo(BODY.cx - 0.90, y + 0.25);
      c.lineTo(BODY.cx + 0.90, y + 0.25);
    }
    c.stroke();
  }

  // ── Accesorios de cabeza ──────────────────────────────────────────────
  //
  // Cada accesorio es una función que dibuja en unidades sobre la cabeza. Los
  // ids son los MISMOS que en el motor pixel: el sombrero guardado en
  // localStorage tiene que seguir puesto después del rework — para el usuario
  // esto es un cambio de dibujo, no un reset de su Boberto.

  // Colores del accesorio, derivados del color elegido. `_hatMat/_hatRealce/
  // _hatBulb` quedan expuestos porque accGlowColor() los lee para el halo.
  function resolveHatColors() {
    let base = hatColor;
    if (HAT_NATURAL[hat] && (!base || base === HAT_DARK)) base = HAT_NATURAL[hat];
    const H = base || HAT_DARK;
    const hl = hexToHsl(H);
    _hatMat    = H;
    _hatRealce = hslToHex(hl.h, hl.s, _clamp(hl.l + 34, 0, 96));
    _hatBulb   = hslToHex(hl.h, _clamp(hl.s + 6, 0, 100), _clamp(hl.l + 24, 18, 92));
  }

  /**
   * Paleta viva del accesorio: segundo color, ciclado RGB y pulso de brillo.
   *
   * El RGB del accesorio pasa por acá y SÓLO por acá. Es a propósito: mientras
   * los colores del accesorio se resuelvan en un único lugar, no hay forma de
   * que el ciclado se le escape al cuerpo — y a la inversa, ningún adorno queda
   * afuera del efecto por estar escrito con un literal adentro de su función.
   * Por eso ni blanco, ni rojo, ni acero, ni oro se escriben en los accesorios:
   * se piden acá.
   */
  function accPal(st) {
    const rgb = !!(st && st.accRGB);
    const dl  = (st && st.accDL) || 0;

    // Todo color del accesorio entra por acá. `k` es cuánto lo arrastra el
    // ciclado: 1 para el material propio, menos para los colores que le dan
    // identidad a la pieza (el blanco del gorro de chef, el oro de la corona),
    // que se tiñen sin dejar de reconocerse.
    const live = (hex, k) => {
      let out = hex;
      if (rgb) {
        const t = (k == null ? 1 : k);
        out = t >= 1 ? hslToHex(_accHue, 82, 52) : mixHex(out, hslToHex(_accHue, 88, 60), t);
      }
      return dl ? adjustL(out, dl) : out;
    };

    const M = live(_hatMat, 1);
    return {
      mat: M,
      realce: rgb ? hslToHex(_accHue, 88, 72) : live(_hatRealce),
      bulb:   rgb ? hslToHex(_accHue, 92, 78) : live(_hatBulb),
      dark: adjustL(M, -14), lite: adjustL(M, 12),
      /**
       * Color del detalle. Cada accesorio pasa el suyo natural; si el usuario
       * eligió un segundo color, gana el suyo. Con 'auto' el accesorio se ve
       * exactamente como se veía antes de que este eje existiera.
       */
      alt(natural, k) {
        const base = (hatColor2 && hatColor2 !== 'auto') ? hatColor2 : natural;
        return live(base, k == null ? 0.78 : k);
      },
      altDark(natural) { return adjustL(this.alt(natural), -16); },
      altLite(natural) { return adjustL(this.alt(natural), 16); },
      /** Color propio de la pieza: no lo elige el usuario, pero el RGB lo tiñe. */
      fixed(hex, k) { return live(hex, k == null ? 0.72 : k); },
    };
  }

  // Píxeles por unidad del trazo en curso. Hace falta porque shadowBlur de
  // Canvas2D NO pasa por la matriz de transformación: se mide en píxeles de
  // dispositivo. Sin esto el halo del bulbo es gigante en la preview (escala 3)
  // y microscópico en el topbar (escala 2).
  let _k = 2;

  // La pose que se está pintando. Los accesorios reciben sólo la paleta, y a
  // casi todos les alcanza; la bata necesita además saber dónde están los
  // brazos para vestirlos con sus mangas (un brazo levantado no lleva la manga
  // en el mismo lugar que uno caído). Lo fija paintFrame antes de cada cuadro.
  let _poseActual = null;

  // Dibuja algo con el halo del accesorio encima. El halo se apaga solo cuando
  // no hay efecto activo (_accM ~ 0 sólo si el usuario lo apagó).
  /**
   * Dibuja algo con el halo del accesorio encima.
   *
   * `yTope` es la altura del punto más alto que va a ocupar ese halo. Cuando se
   * pasa, el desenfoque se recorta para no salirse por arriba del lienzo: el
   * halo se extiende hacia los cuatro lados desde lo que pinta `fn`, así que un
   * LED a 0.6 unidades del borde con 2.4 de desenfoque deja una franja
   * luminosa cortada en seco contra el canto. En el topbar no se nota; en la
   * vitrina de la landing, a 20 px por unidad, sí.
   */
  function glowing(c, st, blurUnits, fn, yTope) {
    c.save();
    if (_accM > 0.02) {
      const tope = (yTope == null) ? blurUnits : Math.max(0, yTope - 0.15);
      c.shadowColor = (st && st.accGlow) || '#ffffff';
      c.shadowBlur  = _k * Math.min(blurUnits, tope) * (0.55 + 0.55 * _accM);
    }
    fn();
    c.restore();
  }

  const HEAD = { cx: 8, top: BODY.top, rx: BODY.rx * 0.80 };

  /*
   * La tela de la bata. Blanca SIEMPRE, sea cual sea el color de accesorio
   * elegido: una bata grafito o neón deja de leerse como bata. El RGB la tiñe
   * apenas (fixed con k bajo), igual que al gorro de chef, para que el efecto
   * no se le escape.
   *
   * La costura es un gris medio y no un borde del tema: sobre un tema claro el
   * blanco se funde con el fondo, y lo que dibuja la silueta es esa línea.
   *
   * Se guarda en `_bataTela` porque la mano del puntero (un prop, que no recibe
   * la paleta del accesorio) tiene que vestir la MISMA manga.
   */
  let _bataTela = null;
  function telaBata(A) {
    const tela = A.fixed('#f5f7fa', 0.35);
    _bataTela = {
      luz: mixHex(tela, '#ffffff', 0.6),
      tela: tela,
      sombra: adjustL(tela, -17),
      costura: mixHex(tela, '#4f5866', 0.62),
    };
    return _bataTela;
  }

  /*
   * Una manga: el mismo muñón que drawArms, un poco más grande y en tela. Va
   * ENCIMA del cuerpo (los brazos se pintan detrás), así que la parte que cae
   * dentro de la silueta se lee como el brazo apoyado contra la bata.
   */
  function mangaBata(c, T, x, y, rot, puno) {
    c.save();
    c.translate(x, y);
    c.rotate(rot);
    ellipsePath(c, 0, 0, 1.08, 1.40);
    const g = c.createLinearGradient(-1.1, -1.4, 1.1, 1.4);
    g.addColorStop(0, T.luz);
    g.addColorStop(1, T.sombra);
    c.fillStyle = g; c.fill();
    c.lineWidth = 0.28; c.strokeStyle = T.costura; c.stroke();
    // El puño: una línea corta en la punta libre de la manga (`puno` = 1 abajo,
    // -1 arriba). Es lo que la separa de un segundo bulto de tela.
    if (puno) {
      c.lineWidth = 0.20;
      c.beginPath();
      c.moveTo(-0.72, puno * 0.82); c.quadraticCurveTo(0, puno * 1.12, 0.72, puno * 0.82);
      c.stroke();
    }
    c.restore();
  }

  /* ══ DISFRACES ══════════════════════════════════════════════════════════
   *
   * Boberto con un pijama-disfraz de cuerpo entero (un kigurumi): el traje
   * tapa la silueta y deja la cara en una abertura, así que se lee como ÉL
   * disfrazado y no como otro bicho. Reemplazan a los personajes de
   * franquicia (ver PERSONAJE_RETIRADO): las cinco criaturas son inventadas
   * y evocan un tipo —fuego, agua, planta, electricidad, fantasma— sin
   * copiar a ninguna.
   *
   * Son ACCESORIOS y no personajes, a propósito: el traje es del color del
   * accesorio y la panza del segundo color, así que cada persona lo arma a
   * su gusto, y el brillo y el RGB del accesorio les llegan como a cualquier
   * otro. Sin color elegido usan el suyo (HAT_NATURAL).
   *
   * Lo que hace leer "disfraz" son tres cosas, y las tres son de acá:
   *   · los OJOS DE LA CRIATURA bordados en la capucha, encima de los de
   *     Boberto;
   *   · el borde enrollado de la abertura, con la sombra que deja en la cara;
   *   · la costura de la capucha y el pespunte de la panza.
   *
   * Todo sale de la silueta de la pose (siluetaDe), así que un salto o un
   * aplastado mueven el traje con el cuerpo. La abertura no puede tapar los
   * ojos: el aro llega a ±4.3 en x y a 5.3/10.3 en y, y la elipse de la cara
   * los contiene con aire.
   */
  function marcoDisfraz() {
    const p = _poseActual || {};
    const s = siluetaDe(p);
    const h = s.bot - s.top;
    return {
      p, cx: s.cx, top: s.top, bot: s.bot, rx: s.rx, h,
      // La capucha se infla por encima de la cabeza: ahí van los ojos de la
      // criatura, que pegados al borde de la cara no tenían aire.
      cap: s.top - 0.95,
      // La abertura de la cara, centrada en los ojos.
      fx: s.cx, fy: s.top + h * 0.385, frx: s.rx * 0.85, fry: h * 0.305,
      // La panza.
      by: s.top + h * 0.9, brx: s.rx * 0.64, bry: h * 0.21,
    };
  }

  // Los tonos de la tela, derivados del color del accesorio.
  function telaDisfraz(A, natural2) {
    const tela = A.mat;
    const panza = A.alt(natural2);
    return {
      tela, panza,
      luz: mixHex(tela, '#ffffff', 0.32),
      medio: adjustL(tela, -9),
      sombra: adjustL(tela, -26),
      costura: mixHex(adjustL(tela, -40), '#000000', 0.25),
      panzaLuz: mixHex(panza, '#ffffff', 0.35),
      panzaSombra: adjustL(panza, -18),
    };
  }

  function siluetaTraje(c, M) {
    blobPath(c, M.cx, M.top - 1.45, M.bot + 0.15, M.rx + 0.45);
  }
  function abertura(c, M, extra) {
    const e = extra || 0;
    c.ellipse(M.fx, M.fy, M.frx + e, M.fry + e, 0, 0, Math.PI * 2);
  }

  /*
   * El traje entero: cuerpo y capucha de una pieza, la panza, el borde de la
   * cara, las mangas y las patas. Cada disfraz agrega lo suyo encima con
   * `encima` (panza decorada, cuernos, antenas…).
   */
  function trajeDisfraz(c, A, T, M, op) {
    const o = op || {};
    // Cuerpo con la cara recortada.
    c.save();
    c.beginPath(); siluetaTraje(c, M);
    c.moveTo(M.fx + M.frx, M.fy); abertura(c, M);
    const g = c.createRadialGradient(M.cx - M.rx * 0.42, M.top + M.h * 0.15, 0.3, M.cx, M.top + M.h * 0.55, M.rx * 1.5);
    g.addColorStop(0, T.luz);
    g.addColorStop(0.42, T.tela);
    g.addColorStop(0.8, T.medio);
    g.addColorStop(1, T.sombra);
    c.fillStyle = g; c.fill('evenodd');
    c.clip('evenodd');
    // Pliegues de la capucha a los costados de la cara.
    [-1, 1].forEach(sd => {
      c.beginPath();
      c.moveTo(M.fx + sd * (M.frx + 0.5), M.fy - M.fry * 0.55);
      c.quadraticCurveTo(M.fx + sd * (M.frx + 1.05), M.fy, M.fx + sd * (M.frx + 0.55), M.fy + M.fry * 0.6);
      c.lineWidth = 0.22; c.lineCap = 'round'; c.strokeStyle = withA(T.sombra, 0.55); c.stroke();
    });
    // Donde la capucha termina y empieza el cuerpo: una costura curva bajo la cara.
    c.beginPath();
    c.moveTo(M.cx - M.rx - 0.5, M.fy + M.fry * 0.35);
    c.quadraticCurveTo(M.cx, M.fy + M.fry * 1.55, M.cx + M.rx + 0.5, M.fy + M.fry * 0.35);
    c.lineWidth = 0.16; c.strokeStyle = withA(T.costura, 0.55); c.stroke();
    c.setLineDash([0.2, 0.18]);
    c.lineWidth = 0.06; c.strokeStyle = withA(T.luz, 0.7); c.stroke();
    c.setLineDash([]);
    // Panza.
    if (!o.sinPanza) {
      c.save();
      c.beginPath(); c.rect(M.cx - 9, M.top - 3, 18, 24); abertura(c, M, 0.45);
      c.clip('evenodd');
      ellipsePath(c, M.cx, M.by, M.brx, M.bry);
      const pg = c.createRadialGradient(M.cx - 1.2, M.by - M.bry * 0.6, 0.2, M.cx, M.by, M.brx * 1.1);
      pg.addColorStop(0, T.panzaLuz); pg.addColorStop(0.55, T.panza); pg.addColorStop(1, T.panzaSombra);
      c.fillStyle = pg; c.fill();
      // Pespunte del borde de la panza.
      c.setLineDash([0.22, 0.18]);
      ellipsePath(c, M.cx, M.by, M.brx - 0.3, M.bry - 0.3);
      c.lineWidth = 0.07; c.strokeStyle = withA(T.panzaSombra, 0.9); c.stroke();
      c.setLineDash([]);
      if (o.panza) o.panza(c);
      c.restore();
    }
    // Rim light abajo a la derecha, como el cuerpo de Boberto.
    const rg = c.createLinearGradient(M.cx - M.rx, M.top, M.cx + M.rx, M.bot);
    rg.addColorStop(0.5, 'rgba(255,255,255,0)');
    rg.addColorStop(0.8, 'rgba(255,255,255,0.32)');
    rg.addColorStop(1, 'rgba(255,255,255,0.08)');
    c.beginPath(); siluetaTraje(c, M);
    c.lineWidth = 0.7; c.strokeStyle = rg; c.stroke();
    c.restore();
    c.beginPath(); siluetaTraje(c, M);
    c.lineWidth = 0.32; c.strokeStyle = withA(T.costura, 0.85); c.stroke();

    // La sombra que el borde de la capucha deja sobre la cara.
    c.save();
    c.beginPath(); abertura(c, M); c.clip();
    c.beginPath(); abertura(c, M);
    c.lineWidth = 0.7; c.strokeStyle = 'rgba(0,0,0,0.17)'; c.stroke();
    c.restore();
    // El borde enrollado de la abertura.
    c.beginPath(); abertura(c, M, 0.22);
    const bg = c.createLinearGradient(0, M.fy - M.fry, 0, M.fy + M.fry);
    bg.addColorStop(0, T.luz); bg.addColorStop(1, T.medio);
    c.lineWidth = 0.55; c.strokeStyle = bg; c.stroke();
    c.beginPath(); abertura(c, M, 0.5);
    c.lineWidth = 0.08; c.strokeStyle = withA(T.costura, 0.7); c.stroke();

    // Mangas, donde drawArms dejó los brazos (la misma cuenta que la bata).
    const p = M.p;
    const TM = { luz: T.luz, sombra: T.sombra, costura: T.costura };
    const cyA = M.top + M.h * 0.60;
    const manga = (sd, up) => mangaBata(c, TM,
      M.cx + sd * BODY.rx * 0.92, up ? cyA - 2.7 : cyA,
      up ? (sd > 0 ? -0.8 : 0.8) : (sd > 0 ? -0.18 : 0.18), up ? 0 : 1);
    if (p.armL !== false) manga(-1, p.arm === 'L');
    if (p.armR !== false) manga(1, p.arm === 'R');

    // Patas, con sus almohadillas.
    if (!p.hideFeet && !o.sinPatas) {
      lugarPies(p).forEach(([x, y]) => {
        ellipsePath(c, x, y, 1.34, 0.92);
        const fg = c.createLinearGradient(0, y - 0.9, 0, y + 0.9);
        fg.addColorStop(0, T.tela); fg.addColorStop(1, T.sombra);
        c.fillStyle = fg; c.fill();
        c.lineWidth = 0.2; c.strokeStyle = withA(T.costura, 0.85); c.stroke();
        [-0.52, 0, 0.52].forEach((dx, i) => {
          ellipsePath(c, x + dx, y + 0.42 - (i === 1 ? 0.08 : 0), 0.24, 0.18);
          c.fillStyle = T.panza; c.fill();
        });
      });
    }
  }

  /* Los ojos de la criatura, bordados en la capucha: blanco con pupila, un
     brillo y el pespunte alrededor. `pupila` puede ser 'rendija' (reptil). */
  function ojosCapucha(c, M, o) {
    const y = M.cap + (o.dy || 0.45);
    [-1, 1].forEach(sd => {
      const x = M.cx + sd * (o.sep || 1.4), r = o.r || 0.5;
      ellipsePath(c, x, y, r, r * 1.08);
      c.fillStyle = o.blanco || '#fbfaf4'; c.fill();
      c.lineWidth = 0.09; c.strokeStyle = 'rgba(0,0,0,0.55)'; c.stroke();
      c.setLineDash([0.12, 0.1]);
      ellipsePath(c, x, y, r + 0.16, r * 1.08 + 0.16);
      c.lineWidth = 0.05; c.strokeStyle = withA(o.hilo || '#ffffff', 0.6); c.stroke();
      c.setLineDash([]);
      if (o.pupila === 'rendija') {
        c.beginPath(); c.ellipse(x + sd * 0.04, y + 0.04, r * 0.2, r * 0.78, 0, 0, Math.PI * 2);
      } else {
        c.beginPath(); c.ellipse(x + sd * 0.06, y + 0.08, r * 0.52, r * 0.62, 0, 0, Math.PI * 2);
      }
      c.fillStyle = o.iris || '#1b1420'; c.fill();
      ellipsePath(c, x - r * 0.28, y - r * 0.36, r * 0.24, r * 0.22);
      c.fillStyle = '#ffffff'; c.fill();
    });
  }

  // Una cola gruesa en la base que se afina: rellena, no trazada, para que
  // el ancho cambie a lo largo. Devuelve la punta y la dirección final.
  function colaDisfraz(c, pts, w0, w1, relleno, borde) {
    const N = 36, izq = [], der = [];
    const en = (t) => {
      const u = 1 - t;
      return [0, 1].map(k => u * u * u * pts[0][k] + 3 * u * u * t * pts[1][k] + 3 * u * t * t * pts[2][k] + t * t * t * pts[3][k]);
    };
    for (let i = 0; i <= N; i++) {
      const t = i / N;
      const [x, y] = en(t), [x2, y2] = en(Math.min(1, t + 0.01)), [x1, y1] = en(Math.max(0, t - 0.01));
      const dx = x2 - x1, dy = y2 - y1, l = Math.hypot(dx, dy) || 1;
      const w = w0 + (w1 - w0) * t;
      izq.push([x - dy / l * w, y + dx / l * w]);
      der.push([x + dy / l * w, y - dx / l * w]);
    }
    c.beginPath();
    izq.forEach(([x, y], i) => i ? c.lineTo(x, y) : c.moveTo(x, y));
    for (let i = der.length - 1; i >= 0; i--) c.lineTo(der[i][0], der[i][1]);
    c.closePath();
    c.fillStyle = relleno; c.fill();
    c.lineWidth = 0.16; c.strokeStyle = borde; c.stroke();
    const [ex, ey] = en(1), [px, py] = en(0.97);
    return { x: ex, y: ey, ang: Math.atan2(ey - py, ex - px), izq, der, en };
  }

  const HATS = {
    limpio() {},

    /**
     * Disfraz Brasita: una salamandra de fuego.
     *
     * Rojo brasa con la panza ámbar en escamas, dos cuernos que se encienden
     * en la punta y ojos de reptil en la capucha. La llama de la cola va
     * atrás (HATS_BACK.brasita).
     */
    brasita(c, A, st) {
      const T = telaDisfraz(A, '#f4b43c');
      const M = marcoDisfraz();
      trajeDisfraz(c, A, T, M, {
        // Escamas de la panza: arcos que se achican hacia abajo.
        panza: (cc) => {
          for (let i = 0; i < 5; i++) {
            const y = M.by - M.bry * 0.75 + i * M.bry * 0.38;
            cc.beginPath();
            cc.ellipse(M.cx, y, M.brx * (0.95 - i * 0.06), 0.55, 0, 0.12 * Math.PI, 0.88 * Math.PI);
            cc.lineWidth = 0.1; cc.strokeStyle = withA(T.panzaSombra, 0.8); cc.stroke();
          }
        },
      });
      // Cresta: tres púas chicas por el lomo de la capucha.
      [-0.75, 0, 0.75].forEach((dx, i) => {
        const y = M.cap - 0.5 + Math.abs(dx) * 0.25;
        const alto = i === 1 ? 1.0 : 0.75;
        c.beginPath();
        c.moveTo(M.cx + dx - 0.36, y + 0.15);
        c.quadraticCurveTo(M.cx + dx - 0.05, y - alto * 0.55, M.cx + dx + 0.12, y - alto);
        c.quadraticCurveTo(M.cx + dx + 0.18, y - alto * 0.4, M.cx + dx + 0.36, y + 0.15);
        c.closePath();
        c.fillStyle = T.medio; c.fill();
        c.lineWidth = 0.08; c.strokeStyle = T.costura; c.stroke();
      });
      // Cuernos de brasa: oscuros en la base y encendidos en la punta.
      const brasa = A.fixed('#ffb347');
      [-1, 1].forEach(sd => {
        const bx = M.cx + sd * 2.15, by = M.cap + 0.35;
        const cuerno = () => {
          c.beginPath();
          c.moveTo(bx - sd * 0.55, by + 0.25);
          c.bezierCurveTo(bx - sd * 0.5, by - 1.0, bx + sd * 0.3, by - 1.9, bx + sd * 1.15, by - 2.35);
          c.bezierCurveTo(bx + sd * 0.55, by - 1.55, bx + sd * 0.5, by - 0.6, bx + sd * 0.6, by + 0.2);
          c.closePath();
        };
        glowing(c, st, 1.0, () => {
          cuerno();
          const g = c.createLinearGradient(bx, by + 0.3, bx + sd * 1.1, by - 2.35);
          g.addColorStop(0, adjustL(T.tela, -30));
          g.addColorStop(0.55, A.fixed('#ff6a1a'));
          g.addColorStop(1, mixHex(brasa, '#ffffff', 0.55));
          c.fillStyle = g; c.fill();
        }, by - 2.35);
        cuerno(); c.lineWidth = 0.08; c.strokeStyle = withA(T.costura, 0.8); c.stroke();
        // Anillos del cuerno.
        [0.35, 0.65].forEach(t => {
          c.beginPath();
          c.moveTo(bx - sd * 0.45 + sd * t * 0.9, by - t * 1.3);
          c.lineTo(bx + sd * 0.45 + sd * t * 0.7, by - t * 1.15 + 0.1);
          c.lineWidth = 0.07; c.strokeStyle = withA(T.costura, 0.6); c.stroke();
        });
      });
      ojosCapucha(c, M, { sep: 1.35, r: 0.52, dy: 0.62, pupila: 'rendija', blanco: '#ffe9a8', iris: '#2a1006' });
    },

    /**
     * Disfraz Caparazón: una tortuga marina.
     *
     * Verde agua, con la panza arena en placas y dos aletas de coral en la
     * capucha. El caparazón de coral asoma por los costados desde atrás
     * (HATS_BACK.caparazon). De colores lejos de la tortuga celeste que todo
     * el mundo conoce, a propósito.
     */
    caparazon(c, A) {
      const T = telaDisfraz(A, '#f4e2b8');
      const M = marcoDisfraz();
      const coral = A.fixed('#e8765a', 0.5);
      trajeDisfraz(c, A, T, M, {
        // Plastrón: placas.
        panza: (cc) => {
          cc.lineWidth = 0.11; cc.strokeStyle = withA(T.panzaSombra, 0.9);
          cc.beginPath(); cc.moveTo(M.cx, M.by - M.bry); cc.lineTo(M.cx, M.by + M.bry); cc.stroke();
          [-0.45, 0.1, 0.62].forEach(k => {
            const y = M.by + k * M.bry;
            cc.beginPath(); cc.moveTo(M.cx - M.brx, y); cc.quadraticCurveTo(M.cx, y + 0.35, M.cx + M.brx, y); cc.stroke();
          });
        },
      });
      // Aletas de coral a los costados de la capucha.
      [-1, 1].forEach(sd => {
        const x0 = M.cx + sd * 4.5, y0 = M.cap + 1.1;
        c.beginPath();
        c.moveTo(x0, y0 + 0.6);
        c.bezierCurveTo(x0 + sd * 1.0, y0 - 0.9, x0 + sd * 2.6, y0 - 1.3, x0 + sd * 3.1, y0 - 0.6);
        c.bezierCurveTo(x0 + sd * 2.4, y0 - 0.3, x0 + sd * 2.7, y0 + 0.4, x0 + sd * 2.2, y0 + 0.75);
        c.bezierCurveTo(x0 + sd * 1.6, y0 + 0.5, x0 + sd * 1.2, y0 + 1.3, x0 + sd * 0.6, y0 + 1.6);
        c.closePath();
        const g = c.createLinearGradient(x0, y0 - 1, x0 + sd * 3, y0 + 1);
        g.addColorStop(0, mixHex(coral, '#ffffff', 0.3)); g.addColorStop(1, adjustL(coral, -18));
        c.fillStyle = g; c.fill();
        c.lineWidth = 0.1; c.strokeStyle = adjustL(coral, -40); c.stroke();
        // Radios de la aleta.
        [[1.2, -0.5, 2.6, -0.9], [1.1, 0.0, 2.3, 0.15]].forEach(([a, b, cc2, d]) => {
          c.beginPath(); c.moveTo(x0 + sd * a, y0 + b); c.lineTo(x0 + sd * cc2, y0 + d);
          c.lineWidth = 0.06; c.strokeStyle = withA(adjustL(coral, -35), 0.7); c.stroke();
        });
      });
      ojosCapucha(c, M, { sep: 1.45, r: 0.5, dy: 0.5, iris: '#0d2a33' });
    },

    /**
     * Disfraz Brotito: un dinosaurio de planta.
     *
     * Verde hoja con manchas, panza verde claro y un brote de dos hojas que
     * le crece de la nuca (HATS_BACK.brotito, para que el tallo nazca detrás
     * de la capucha).
     */
    brotito(c, A) {
      const T = telaDisfraz(A, '#d6ef9e');
      const M = marcoDisfraz();
      trajeDisfraz(c, A, T, M);
      // Manchas: más oscuras, de bordes blandos, fuera de la cara y la panza.
      c.save();
      c.beginPath(); siluetaTraje(c, M); c.clip();
      [[-4.9, 1.3, 0.62, 0.5], [4.6, 1.0, 0.5, 0.4], [-5.7, 6.0, 0.55, 0.45], [5.6, 5.4, 0.62, 0.5], [-4.4, 9.9, 0.5, 0.4], [4.9, 10.2, 0.45, 0.36]]
        .forEach(([dx, dy, rx, ry]) => {
          ellipsePath(c, M.cx + dx, M.top + dy, rx, ry);
          c.fillStyle = withA(adjustL(T.tela, -24), 0.75); c.fill();
          ellipsePath(c, M.cx + dx - rx * 0.25, M.top + dy - ry * 0.3, rx * 0.35, ry * 0.3);
          c.fillStyle = withA(T.luz, 0.35); c.fill();
        });
      c.restore();
      // Orejitas redondas.
      [-1, 1].forEach(sd => {
        ellipsePath(c, M.cx + sd * 3.6, M.cap + 0.45, 0.62, 0.5);
        c.fillStyle = T.medio; c.fill();
        c.lineWidth = 0.1; c.strokeStyle = T.costura; c.stroke();
        ellipsePath(c, M.cx + sd * 3.6, M.cap + 0.5, 0.32, 0.25);
        c.fillStyle = T.panza; c.fill();
      });
      ojosCapucha(c, M, { sep: 1.4, r: 0.5, dy: 0.5, iris: '#173014' });
    },

    /**
     * Disfraz Chispa: un bicho eléctrico de cable.
     *
     * Índigo con la panza celeste, dos antenas con una chispa encendida en la
     * punta y rayos bordados en la capucha. La cola es un cable en espiral
     * con su chispa (HATS_BACK.chispa): nada de orejas largas ni rayo
     * amarillo, que es otro bicho.
     */
    chispa(c, A, st) {
      const T = telaDisfraz(A, '#9fe6ff');
      const M = marcoDisfraz();
      const rayo = A.fixed('#ffe45c', 0.5);
      trajeDisfraz(c, A, T, M, {
        // Un rayo chico bordado en la panza.
        panza: (cc) => {
          const x = M.cx, y = M.by - 0.2;
          cc.beginPath();
          cc.moveTo(x + 0.35, y - 1.0); cc.lineTo(x - 0.45, y + 0.15); cc.lineTo(x + 0.05, y + 0.15);
          cc.lineTo(x - 0.35, y + 1.0); cc.lineTo(x + 0.5, y - 0.2); cc.lineTo(x - 0.02, y - 0.2);
          cc.closePath();
          cc.fillStyle = withA(T.tela, 0.7); cc.fill();
        },
      });
      // Rayos a los costados de la capucha.
      [-1, 1].forEach(sd => {
        const x = M.cx + sd * 4.3, y = M.cap + 1.1;
        c.beginPath();
        c.moveTo(x, y); c.lineTo(x + sd * 1.0, y + 0.8); c.lineTo(x + sd * 0.45, y + 0.9);
        c.lineTo(x + sd * 1.25, y + 1.9); c.lineTo(x + sd * 0.05, y + 1.05); c.lineTo(x + sd * 0.55, y + 0.95);
        c.closePath();
        c.fillStyle = rayo; c.fill();
        c.lineWidth = 0.07; c.strokeStyle = adjustL(rayo, -40); c.stroke();
      });
      // Antenas: tallo flexible y la chispa.
      [-1, 1].forEach(sd => {
        const x0 = M.cx + sd * 1.5, y0 = M.cap - 0.2;
        const x1 = M.cx + sd * 3.1, y1 = M.cap - 2.75;
        c.beginPath();
        c.moveTo(x0, y0);
        c.bezierCurveTo(x0 + sd * 0.1, y0 - 1.3, x1 - sd * 0.9, y1 + 0.6, x1, y1);
        c.lineWidth = 0.3; c.lineCap = 'round'; c.strokeStyle = T.sombra; c.stroke();
        c.lineWidth = 0.08; c.strokeStyle = withA(T.luz, 0.7); c.stroke();
        glowing(c, st, 1.8, () => {
          ellipsePath(c, x1, y1, 0.55, 0.55);
          const g = c.createRadialGradient(x1 - 0.15, y1 - 0.18, 0.05, x1, y1, 0.6);
          g.addColorStop(0, '#ffffff'); g.addColorStop(0.5, rayo); g.addColorStop(1, adjustL(rayo, -25));
          c.fillStyle = g; c.fill();
        }, y1 - 0.55);
        // Chispitas alrededor.
        c.lineWidth = 0.07; c.strokeStyle = withA(mixHex(rayo, '#ffffff', 0.5), 0.9);
        [[0.75, -0.2, 1.1, -0.45], [-0.3, -0.75, -0.45, -1.1], [0.5, 0.55, 0.85, 0.8]].forEach(([a, b, cc2, d]) => {
          c.beginPath(); c.moveTo(x1 + sd * a, y1 + b); c.lineTo(x1 + sd * cc2, y1 + d); c.stroke();
        });
      });
      ojosCapucha(c, M, { sep: 1.4, r: 0.5, dy: 0.5, iris: '#140f3a' });
    },

    /**
     * Disfraz Sombrita: un fantasma de sábana.
     *
     * Pizarra oscuro, con la capucha que gotea arriba como una sábana, una
     * fila de colmillos sobre la cara —la capucha "se come" a Boberto— y los
     * ojos de la criatura encendidos. No tiene patas: el ruedo flota en
     * jirones por debajo. La cola de humo va atrás (HATS_BACK.sombrita).
     */
    sombrita(c, A, st) {
      const T = telaDisfraz(A, '#4a5a86');
      const M = marcoDisfraz();
      // El ruedo en jirones, debajo del traje: tapa los pies. Las puntas
      // siguen la panza (más largas al centro) y no pasan del ancho del cuerpo.
      if (!M.p.hideFeet) {
        // Se angosta hacia abajo como la panza: con los costados derechos,
        // el ruedo asomaba por debajo como un cajón.
        const n = 4, x0 = M.cx - M.rx * 0.66, x1 = M.cx + M.rx * 0.66;
        c.beginPath();
        c.moveTo(M.cx - M.rx * 0.8, M.bot - 1.6);
        c.lineTo(x0, M.bot + 1.2);
        for (let i = 0; i < n; i++) {
          const a = x0 + (x1 - x0) * i / n, b = x0 + (x1 - x0) * (i + 1) / n;
          // Los valles bajan hasta tapar las suelas (y ~16.4): más arriba,
          // entre jirón y jirón asomaban los pies.
          const m = (a + b) / 2, largo = 2.2 - Math.abs(m - M.cx) / M.rx * 0.6;
          c.quadraticCurveTo(a + (b - a) * 0.15, M.bot + largo * 0.7, m, M.bot + largo);
          c.quadraticCurveTo(b - (b - a) * 0.15, M.bot + largo * 0.7, b, M.bot + 1.45);
        }
        c.lineTo(M.cx + M.rx * 0.8, M.bot - 1.6);
        c.closePath();
        const g = c.createLinearGradient(0, M.bot - 1.6, 0, M.bot + 2.2);
        g.addColorStop(0, T.tela); g.addColorStop(1, withA(T.sombra, 0.55));
        c.fillStyle = g; c.fill();
        c.lineWidth = 0.1; c.strokeStyle = withA(T.costura, 0.5); c.stroke();
      }
      trajeDisfraz(c, A, T, M, { sinPatas: true });
      // Gotas de la sábana arriba de la capucha.
      c.beginPath();
      for (let i = -3; i <= 3; i++) {
        const x = M.cx + i * 1.05, alto = 0.75 + (Math.abs(i) % 2) * 0.5 - Math.abs(i) * 0.05;
        const yb = M.cap - 0.35 + Math.abs(i) * 0.18;
        c.moveTo(x - 0.55, yb + 0.3);
        c.quadraticCurveTo(x - 0.42, yb - alto, x, yb - alto - 0.15);
        c.quadraticCurveTo(x + 0.42, yb - alto, x + 0.55, yb + 0.3);
      }
      c.fillStyle = T.tela; c.fill();
      c.lineWidth = 0.08; c.strokeStyle = withA(T.costura, 0.7); c.stroke();
      // Colmillos sobre la cara.
      const diente = A.fixed('#f2f6ff', 0.3);
      for (let i = -3; i <= 3; i++) {
        const x = M.fx + i * 1.15;
        const y = M.fy - M.fry * Math.sqrt(Math.max(0, 1 - ((x - M.fx) / M.frx) ** 2)) - 0.12;
        const largo = i === -2 || i === 2 ? 0.75 : 0.5;
        c.beginPath();
        c.moveTo(x - 0.3, y - 0.1); c.quadraticCurveTo(x - 0.08, y + largo * 0.6, x, y + largo);
        c.quadraticCurveTo(x + 0.08, y + largo * 0.6, x + 0.3, y - 0.1);
        c.closePath();
        c.fillStyle = diente; c.fill();
        c.lineWidth = 0.05; c.strokeStyle = 'rgba(0,0,0,0.35)'; c.stroke();
      }
      const brillo = A.fixed('#7ff6ff', 0.5);
      glowing(c, st, 1.2, () => {
        ojosCapucha(c, M, { sep: 1.5, r: 0.48, dy: 0.25, blanco: brillo, iris: '#06303a', hilo: brillo });
      });
    },


    /**
     * Auriculares.
     *
     * Grandes y POR FUERA de la silueta. La primera versión era una vincha de
     * 0.7 de grosor con cápsulas de 1.5: apoyada sobre un cuerpo de 12 unidades
     * de ancho se leía como una diadema fina, no como unos auriculares. Las
     * cápsulas van casi contra el borde del lienzo porque ahí es donde un
     * accesorio de cabeza se distingue del cuerpo.
     */
    auriculares(c, A, st) {
      const cx = BODY.cx, cy = BODY.top + 4.3;
      const arcoR = 6.35;

      // Vincha: arco grueso que pasa BIEN por arriba de la cabeza.
      c.beginPath();
      c.lineWidth = 1.15; c.lineCap = 'round';
      c.strokeStyle = A.mat;
      c.arc(cx, cy, arcoR, Math.PI * 1.13, Math.PI * 1.87);
      c.stroke();
      c.beginPath();
      c.lineWidth = 0.34;
      c.strokeStyle = withA(mixHex(A.mat, '#ffffff', 0.55), 0.75);
      c.arc(cx, cy - 0.30, arcoR, Math.PI * 1.16, Math.PI * 1.84);
      c.stroke();

      [-1, 1].forEach(sd => {
        const x = cx + sd * arcoR;
        // Horquilla que baja del arco a la cápsula.
        c.beginPath();
        c.lineWidth = 0.52; c.lineCap = 'butt';
        c.strokeStyle = adjustL(A.mat, -16);
        c.moveTo(x, cy - 1.0); c.lineTo(x, cy + 0.5);
        c.stroke();
        // Cápsula.
        roundRect(c, x - 1.35, cy + 0.2, 2.7, 4.3, 1.15);
        const g = c.createLinearGradient(x - 1.4, cy, x + 1.4, cy + 4.5);
        g.addColorStop(0, mixHex(A.mat, '#ffffff', 0.30));
        g.addColorStop(0.55, A.mat);
        g.addColorStop(1, adjustL(A.mat, -26));
        c.fillStyle = g; c.fill();
        // Almohadilla, corrida hacia el lado del cuerpo.
        const off = sd > 0 ? -1.05 : -0.55;
        roundRect(c, x + off, cy + 0.62, 1.6, 3.45, 0.78);
        c.fillStyle = A.alt(A.realce); c.fill();
        roundRect(c, x + off + 0.25, cy + 0.95, 1.1, 2.8, 0.55);
        c.fillStyle = withA(adjustL(A.mat, -30), 0.55); c.fill();
      });

      // Brazo del micrófono y su luz.
      const mx = cx + arcoR;
      c.beginPath();
      c.lineWidth = 0.34; c.lineCap = 'round';
      c.strokeStyle = adjustL(A.mat, -10);
      c.moveTo(mx - 0.9, cy + 3.9);
      c.quadraticCurveTo(mx - 2.6, cy + 5.6, mx - 3.9, cy + 5.2);
      c.stroke();
      glowing(c, st, 3.0, () => {
        ellipsePath(c, mx - 4.15, cy + 5.15, 0.48, 0.48);
        c.fillStyle = A.bulb; c.fill();
      });
    },

    /**
     * Antena.
     *
     * Era un palito de dos unidades con una bolita: al tamaño de la sidebar se
     * leía como un pelo parado. Ahora tiene lo que la hace antena de robot:
     * un zócalo de metal, un resorte que la deja bambolear y el bulbo con su
     * filamento, alto y apenas inclinado para que tenga gesto.
     */
    antena(c, A, st) {
      const cx = HEAD.cx;
      const y = HEAD.top;
      const metal = A.mat;
      const luzMetal = mixHex(A.lite, '#ffffff', 0.45);

      // Zócalo: una cúpula chata con un aro, apoyada en la coronilla.
      ellipsePath(c, cx, y + 0.35, 1.45, 0.42);
      c.fillStyle = 'rgba(0,0,0,0.35)'; c.fill();
      c.beginPath();
      c.moveTo(cx - 1.25, y + 0.3);
      c.quadraticCurveTo(cx - 1.2, y - 0.75, cx, y - 0.8);
      c.quadraticCurveTo(cx + 1.2, y - 0.75, cx + 1.25, y + 0.3);
      c.quadraticCurveTo(cx, y + 0.6, cx - 1.25, y + 0.3);
      c.closePath();
      const bg = c.createLinearGradient(cx - 1.3, 0, cx + 1.3, 0);
      bg.addColorStop(0, adjustL(metal, -10));
      bg.addColorStop(0.35, luzMetal);
      bg.addColorStop(1, adjustL(metal, -32));
      c.fillStyle = bg; c.fill();
      c.beginPath(); c.ellipse(cx, y - 0.62, 0.55, 0.16, 0, 0, Math.PI * 2);
      c.fillStyle = adjustL(metal, -36); c.fill();

      // Resorte: espiras de abajo hacia arriba, cada una con su brillo.
      const base = y - 0.7, alto = 1.7, espiras = 6;
      for (let i = 0; i < espiras; i++) {
        const yy = base - (i + 0.5) * (alto / espiras);
        const dx = i * 0.05;
        c.beginPath(); c.ellipse(cx + dx, yy, 0.42, 0.13, -0.12, 0, Math.PI * 2);
        c.lineWidth = 0.13; c.strokeStyle = adjustL(metal, -24); c.stroke();
        c.beginPath(); c.ellipse(cx + dx, yy, 0.42, 0.13, -0.12, Math.PI * 1.05, Math.PI * 1.6);
        c.lineWidth = 0.06; c.strokeStyle = luzMetal; c.stroke();
      }
      // Vara: sale del resorte y se inclina hacia la derecha.
      const vx = cx + 0.3, vy = base - alto;
      const bx = cx + 1.05, by = -0.95;
      c.beginPath();
      c.moveTo(vx, vy);
      c.quadraticCurveTo(vx + 0.05, vy - 1.0, bx, by + 0.6);
      c.lineWidth = 0.22; c.lineCap = 'round'; c.strokeStyle = adjustL(metal, -12); c.stroke();
      c.lineWidth = 0.07; c.strokeStyle = withA(luzMetal, 0.8); c.stroke();

      // Bulbo: vidrio con filamento, y el halo del accesorio.
      const bulb = A.alt(A.bulb, 1);
      glowing(c, st, 2.0, () => {
        ellipsePath(c, bx, by, 0.72, 0.72);
        const g = c.createRadialGradient(bx - 0.22, by - 0.25, 0.05, bx, by, 0.75);
        g.addColorStop(0, '#ffffff');
        g.addColorStop(0.35, mixHex(bulb, '#ffffff', 0.55));
        g.addColorStop(1, bulb);
        c.fillStyle = g; c.fill();
      }, by - 0.72);
      c.beginPath();
      c.moveTo(bx - 0.22, by + 0.3);
      c.lineTo(bx - 0.12, by - 0.08); c.lineTo(bx, by + 0.08); c.lineTo(bx + 0.12, by - 0.08); c.lineTo(bx + 0.22, by + 0.3);
      c.lineWidth = 0.06; c.strokeStyle = withA(adjustL(bulb, -35), 0.8); c.stroke();
      // Casquillo del bulbo.
      roundRect(c, bx - 0.32, by + 0.55, 0.64, 0.32, 0.08);
      c.fillStyle = adjustL(metal, -20); c.fill();
      ellipsePath(c, bx - 0.28, by - 0.3, 0.16, 0.1);
      c.fillStyle = withA('#ffffff', 0.95); c.fill();
    },

    /**
     * Copete.
     *
     * Un jopo con onda: sube desde la coronilla, avanza y se enrula en la
     * punta. El de antes era un bulto con dos jorobas y se leía como una
     * boina mal puesta. Lo que lo hace pelo son los MECHONES: tres lenguas
     * superpuestas, cada una con su brillo a lo largo y no un degradé redondo.
     */
    copete(c, A) {
      const cx = HEAD.cx;
      const pelo = A.mat;
      const brillo = A.alt(A.realce);
      const hondo = adjustL(A.dark, -22);

      // Sombra sobre la cabeza.
      ellipsePath(c, cx + 0.2, 4.05, 2.9, 0.5);
      c.fillStyle = 'rgba(0,0,0,0.3)'; c.fill();

      const mechon = (trazar, gx0, gy0, gx1, gy1) => {
        trazar();
        const g = c.createLinearGradient(gx0, gy0, gx1, gy1);
        g.addColorStop(0, hondo);
        g.addColorStop(0.55, pelo);
        g.addColorStop(1, adjustL(pelo, 14));
        c.fillStyle = g; c.fill();
        c.lineWidth = 0.08; c.strokeStyle = withA(adjustL(A.dark, -35), 0.6); c.stroke();
      };
      // Mechón de atrás, a la izquierda: da volumen sin competir.
      mechon(() => {
        c.beginPath();
        c.moveTo(cx - 2.9, 4.1);
        c.bezierCurveTo(cx - 3.3, 2.3, cx - 2.3, 1.2, cx - 0.6, 1.0);
        c.quadraticCurveTo(cx - 1.6, 2.0, cx - 1.1, 3.9);
        c.closePath();
      }, cx - 3, 4, cx - 1, 1);
      // El jopo principal, con el rulo de la punta.
      mechon(() => {
        c.beginPath();
        c.moveTo(cx - 2.2, 4.2);
        c.bezierCurveTo(cx - 2.6, 1.4, cx - 0.9, -0.55, cx + 1.4, -0.5);
        c.bezierCurveTo(cx + 3.15, -0.45, cx + 3.55, 1.15, cx + 2.4, 1.75);
        c.bezierCurveTo(cx + 2.65, 1.0, cx + 2.2, 0.55, cx + 1.55, 0.7);
        c.bezierCurveTo(cx + 0.4, 0.95, cx + 0.5, 2.6, cx + 2.6, 4.05);
        c.quadraticCurveTo(cx, 4.55, cx - 2.2, 4.2);
        c.closePath();
      }, cx - 2, 4, cx + 2, -0.5);
      // Mechón chico de adelante, que cae hacia la frente.
      mechon(() => {
        c.beginPath();
        c.moveTo(cx - 0.6, 4.25);
        c.bezierCurveTo(cx - 0.8, 2.8, cx + 0.2, 2.1, cx + 1.2, 2.3);
        c.quadraticCurveTo(cx + 0.4, 3.0, cx + 0.9, 4.25);
        c.closePath();
      }, cx - 0.6, 4.2, cx + 1, 2.2);
      // Brillos a lo largo de los mechones: es lo que dice "pelo".
      c.lineCap = 'round';
      [[cx - 1.7, 3.6, cx - 1.6, 0.9, cx + 0.9, 0.05, 0.22],
       [cx - 1.0, 3.8, cx - 0.9, 1.7, cx + 0.6, 0.85, 0.12],
       [cx + 1.6, 0.0, cx + 2.6, -0.1, cx + 2.75, 0.9, 0.1]].forEach(([x0, y0, x1, y1, x2, y2, w]) => {
        c.beginPath(); c.moveTo(x0, y0); c.quadraticCurveTo(x1, y1, x2, y2);
        c.lineWidth = w; c.strokeStyle = withA(brillo, 0.65); c.stroke();
      });
      // Separaciones finas entre hebras.
      [[cx - 1.3, 4.0, cx - 1.3, 1.6, cx + 0.4, 0.4], [cx + 0.2, 4.1, cx - 0.1, 2.4, cx + 1.0, 1.3]].forEach(([x0, y0, x1, y1, x2, y2]) => {
        c.beginPath(); c.moveTo(x0, y0); c.quadraticCurveTo(x1, y1, x2, y2);
        c.lineWidth = 0.07; c.strokeStyle = withA(hondo, 0.7); c.stroke();
      });
    },

    /**
     * Cresta LED.
     *
     * Una cresta mohicana de púas de luz, sobre un riel de metal que sigue la
     * curva de la cabeza. Eran cinco dientitos de 1.5 que de lejos se leían
     * como una corona rota; ahora son siete púas altas que se abren en
     * abanico y tienen el brillo adentro, no sólo el halo.
     */
    cresta(c, A, st) {
      const cx = HEAD.cx;
      const led = A.alt(A.bulb, 1);
      // La coronilla: la misma curva de la silueta, para que el riel apoye.
      const cabeza = (dx) => 9.41 - 6.11 * Math.sqrt(Math.max(0, 1 - (dx / 5.73) ** 2));

      const PUAS = [-2.7, -1.8, -0.9, 0, 0.9, 1.8, 2.7];
      PUAS.forEach((dx, i) => {
        const yb = cabeza(dx) + 0.25;
        const alto = 3.1 - Math.abs(dx) * 0.42;
        const inclina = dx * 0.16;
        const ancho = 0.42;
        glowing(c, st, 1.6, () => {
          c.beginPath();
          c.moveTo(cx + dx - ancho, yb);
          c.quadraticCurveTo(cx + dx - ancho * 0.4 + inclina * 0.5, yb - alto * 0.6, cx + dx + inclina, yb - alto);
          c.quadraticCurveTo(cx + dx + ancho * 0.4 + inclina * 0.5, yb - alto * 0.6, cx + dx + ancho, yb);
          c.closePath();
          const g = c.createLinearGradient(0, yb, 0, yb - alto);
          g.addColorStop(0, adjustL(led, -30));
          g.addColorStop(0.5, led);
          g.addColorStop(1, mixHex(led, '#ffffff', 0.7));
          c.fillStyle = g; c.fill();
        }, yb - alto);
        // El filamento: una línea blanca por el centro de la púa.
        c.beginPath();
        c.moveTo(cx + dx, yb - 0.2);
        c.quadraticCurveTo(cx + dx + inclina * 0.5, yb - alto * 0.6, cx + dx + inclina * 0.95, yb - alto * 0.92);
        c.lineWidth = 0.08; c.lineCap = 'round';
        c.strokeStyle = withA('#ffffff', i % 2 ? 0.6 : 0.85); c.stroke();
      });
      // Riel de metal: va encima de la base de las púas.
      c.beginPath();
      for (let dx = -3.3; dx <= 3.31; dx += 0.3) c.lineTo(cx + dx, cabeza(dx) + 0.05);
      for (let dx = 3.3; dx >= -3.31; dx -= 0.3) c.lineTo(cx + dx, cabeza(dx) + 0.7);
      c.closePath();
      const rg = c.createLinearGradient(0, 3.2, 0, 4.6);
      rg.addColorStop(0, A.lite); rg.addColorStop(1, adjustL(A.dark, -20));
      c.fillStyle = rg; c.fill();
      // Tornillos del riel.
      [-2.25, 0, 2.25].forEach(dx => {
        ellipsePath(c, cx + dx, cabeza(dx) + 0.38, 0.13, 0.13);
        c.fillStyle = mixHex(A.lite, '#ffffff', 0.4); c.fill();
      });
    },

    /**
     * Gorra.
     *
     * Era una cúpula con una cinta: se leía como un bombín. Lo que la hace
     * gorra son los gajos que se juntan en el botón, el parche del frente y
     * la VISERA, que vista de frente es una lámina que se acerca: el canto
     * de arriba claro y la panza de abajo en sombra. La visera no baja de
     * y 5.2, porque más abajo pisa los ojos.
     */
    gorro(c, A) {
      const cx = HEAD.cx;
      const tela = A.mat;
      const parche = A.alt(A.realce);

      // Sombra de contacto.
      ellipsePath(c, cx, 4.75, 4.6, 0.7);
      c.fillStyle = 'rgba(0,0,0,0.35)'; c.fill();

      // Copa: seis gajos que nacen en el botón.
      const copa = () => {
        c.beginPath();
        c.moveTo(cx - 4.55, 4.6);
        c.bezierCurveTo(cx - 4.7, 2.0, cx - 2.6, 0.75, cx, 0.75);
        c.bezierCurveTo(cx + 2.6, 0.75, cx + 4.7, 2.0, cx + 4.55, 4.6);
        c.quadraticCurveTo(cx, 5.05, cx - 4.55, 4.6);
        c.closePath();
      };
      copa();
      const g = c.createRadialGradient(cx - 1.4, 1.6, 0.2, cx + 0.3, 3.2, 5.0);
      g.addColorStop(0, A.lite);
      g.addColorStop(0.5, tela);
      g.addColorStop(1, adjustL(A.dark, -14));
      c.fillStyle = g; c.fill();
      c.save();
      c.clip();
      // Costuras de los gajos, con su pespunte.
      [-2.9, -1.2, 1.2, 2.9].forEach(dx => {
        c.beginPath(); c.moveTo(cx, 0.8);
        c.quadraticCurveTo(cx + dx * 0.95, 1.4, cx + dx * 1.5, 4.9);
        c.lineWidth = 0.1; c.strokeStyle = withA(adjustL(A.dark, -30), 0.7); c.stroke();
        c.setLineDash([0.18, 0.16]);
        c.beginPath(); c.moveTo(cx + 0.12 * Math.sign(dx), 0.85);
        c.quadraticCurveTo(cx + dx * 0.95 + 0.14 * Math.sign(dx), 1.45, cx + dx * 1.5 + 0.16 * Math.sign(dx), 4.9);
        c.lineWidth = 0.05; c.strokeStyle = withA(A.lite, 0.8); c.stroke();
        c.setLineDash([]);
      });
      // Ojalillos de ventilación.
      [-2.1, 2.1].forEach(dx => {
        ellipsePath(c, cx + dx, 1.75, 0.13, 0.1);
        c.fillStyle = adjustL(A.dark, -35); c.fill();
      });
      c.restore();
      // Parche del frente: una estrella sobre un óvalo, en el segundo color.
      ellipsePath(c, cx, 2.85, 1.15, 0.95);
      c.fillStyle = parche; c.fill();
      c.lineWidth = 0.08; c.strokeStyle = withA(adjustL(parche, -30), 0.8); c.stroke();
      c.beginPath();
      for (let i = 0; i < 10; i++) {
        const a = -Math.PI / 2 + i * Math.PI / 5, r = i % 2 ? 0.28 : 0.62;
        c.lineTo(cx + Math.cos(a) * r, 2.88 + Math.sin(a) * r);
      }
      c.closePath();
      c.fillStyle = tela; c.fill();
      // Botón.
      ellipsePath(c, cx, 0.78, 0.42, 0.2);
      c.fillStyle = adjustL(tela, -10); c.fill();
      ellipsePath(c, cx - 0.1, 0.72, 0.18, 0.08);
      c.fillStyle = withA('#ffffff', 0.4); c.fill();

      // Visera: la panza en sombra primero y el canto claro encima.
      c.beginPath();
      c.moveTo(cx - 4.4, 4.35);
      c.quadraticCurveTo(cx, 5.0, cx + 4.4, 4.35);
      c.quadraticCurveTo(cx + 4.7, 4.75, cx + 4.1, 5.0);
      c.quadraticCurveTo(cx, 5.75, cx - 4.1, 5.0);
      c.quadraticCurveTo(cx - 4.7, 4.75, cx - 4.4, 4.35);
      c.closePath();
      const vg = c.createLinearGradient(0, 4.3, 0, 5.4);
      vg.addColorStop(0, adjustL(tela, 10));
      vg.addColorStop(0.55, tela);
      vg.addColorStop(1, adjustL(A.dark, -30));
      c.fillStyle = vg; c.fill();
      c.beginPath();
      c.moveTo(cx - 4.3, 4.45);
      c.quadraticCurveTo(cx, 5.1, cx + 4.3, 4.45);
      c.lineWidth = 0.08; c.strokeStyle = withA(A.lite, 0.9); c.stroke();
      // Pespuntes de la visera.
      c.setLineDash([0.18, 0.16]);
      c.beginPath();
      c.moveTo(cx - 3.7, 4.75);
      c.quadraticCurveTo(cx, 5.35, cx + 3.7, 4.75);
      c.lineWidth = 0.05; c.strokeStyle = withA(A.lite, 0.7); c.stroke();
      c.setLineDash([]);
    },

    /**
     * Gorro de mago.
     *
     * A lo Gandalf: ala ANCHA, cono alto y punta caída hacia un costado. La
     * primera versión era un triangulito de tres unidades sobre la cabeza y se
     * leía como una capucha.
     *
     * Sobre la cabeza hay 3.3 unidades de aire, así que el cono arranca DENTRO
     * del cuerpo y el ala le tapa el nacimiento: eso es lo que le compra altura
     * sin salirse del lienzo.
     *
     * El ala en +1.5 y no en +2.1: más abajo se veía hundido sobre los ojos.
     * La punta queda en -0.7 y el lienzo arranca en -PAD.t (-2): sobra aire.
     */
    mago(c, A, st) {
      const cx = BODY.cx, yAla = BODY.top + 1.5;

      // 1. Sombra de contacto del ala sobre la cabeza
      c.save();
      c.beginPath();
      c.ellipse(cx, yAla + 0.2, 5.5, 1.2, 0, 0, Math.PI * 2);
      c.fillStyle = 'rgba(0, 0, 0, 0.45)';
      c.fill();
      c.restore();

      // 2. Ala trasera (profundidad)
      c.beginPath();
      c.moveTo(cx - 6.0, yAla + 0.3);
      c.quadraticCurveTo(cx, yAla - 1.5, cx + 6.0, yAla + 0.3);
      c.quadraticCurveTo(cx, yAla - 0.8, cx - 6.0, yAla + 0.3);
      c.fillStyle = adjustL(A.mat, -35);
      c.fill();

      // 3. Cono principal
      c.beginPath();
      c.moveTo(cx - 3.2, yAla - 0.2);
      c.bezierCurveTo(cx - 2.5, yAla - 2.8, cx - 0.6, yAla - 4.5, cx + 3.1, yAla - 5.5); // Punta derecha
      c.bezierCurveTo(cx + 2.1, yAla - 3.9, cx + 2.4, yAla - 1.8, cx + 3.4, yAla - 0.2);
      c.quadraticCurveTo(cx, yAla + 0.3, cx - 3.2, yAla - 0.2); // Base curva
      c.closePath();
      
      const g = c.createRadialGradient(cx - 1.5, yAla - 2.5, 0.4, cx + 1.0, yAla - 1.5, 5.0);
      g.addColorStop(0, adjustL(A.mat, 15));
      g.addColorStop(0.4, A.mat);
      g.addColorStop(1, adjustL(A.mat, -28));
      c.fillStyle = g; c.fill();

      // Pliegue profundo
      c.beginPath();
      c.lineWidth = 0.35;
      c.lineCap = 'round';
      c.strokeStyle = withA(adjustL(A.mat, -40), 0.6);
      c.moveTo(cx - 1.4, yAla - 0.6);
      c.bezierCurveTo(cx - 1.0, yAla - 2.5, cx + 0.8, yAla - 4.1, cx + 2.9, yAla - 5.25);
      c.stroke();
      
      // Resalte del pliegue (highlight)
      c.beginPath();
      c.lineWidth = 0.2;
      c.strokeStyle = withA(adjustL(A.mat, 25), 0.5);
      c.moveTo(cx - 1.1, yAla - 0.6);
      c.bezierCurveTo(cx - 0.7, yAla - 2.5, cx + 1.1, yAla - 4.1, cx + 3.2, yAla - 5.15);
      c.stroke();

      // 4. Ala frontal (wavy brim con grosor)
      c.beginPath();
      c.moveTo(cx - 6.6, yAla + 0.4);
      c.quadraticCurveTo(cx - 3.4, yAla - 1.5, cx, yAla - 1.25);
      c.quadraticCurveTo(cx + 3.4, yAla - 1.5, cx + 6.6, yAla + 0.4);
      c.quadraticCurveTo(cx + 3.4, yAla + 1.6, cx, yAla + 1.7);
      c.quadraticCurveTo(cx - 3.4, yAla + 1.6, cx - 6.6, yAla + 0.4);
      c.closePath();
      
      const ag = c.createLinearGradient(0, yAla - 1.5, 0, yAla + 1.7);
      ag.addColorStop(0, adjustL(A.mat, 12));
      ag.addColorStop(0.3, A.mat);
      ag.addColorStop(1, adjustL(A.mat, -32));
      c.fillStyle = ag; c.fill();

      // Borde del ala (highlight grosor)
      c.beginPath();
      c.moveTo(cx - 6.6, yAla + 0.4);
      c.quadraticCurveTo(cx - 3.4, yAla + 1.6, cx, yAla + 1.7);
      c.quadraticCurveTo(cx + 3.4, yAla + 1.6, cx + 6.6, yAla + 0.4);
      c.lineWidth = 0.25;
      c.strokeStyle = adjustL(A.mat, 20);
      c.stroke();

      // 5. Cinta
      c.beginPath();
      c.moveTo(cx - 3.2, yAla - 0.2);
      c.quadraticCurveTo(cx, yAla - 1.0, cx + 3.3, yAla - 0.3);
      c.lineTo(cx + 3.4, yAla - 0.8);
      c.quadraticCurveTo(cx, yAla - 1.6, cx - 3.0, yAla - 0.7);
      c.closePath();
      const cg = c.createLinearGradient(cx - 3, yAla - 1.6, cx + 3, yAla);
      cg.addColorStop(0, adjustL(A.alt(A.realce), 10));
      cg.addColorStop(1, adjustL(A.alt(A.realce), -20));
      c.fillStyle = cg; c.fill();

      // Estrellas
      glowing(c, st, 2.6, () => {
        c.fillStyle = A.alt(A.bulb, 1);
        [[-1.1, -1.6, 0.28], [0.8, -3.2, 0.24], [1.9, -4.3, 0.18], [0.1, -0.6, 0.22]].forEach(pt => {
          ellipsePath(c, cx + pt[0], yAla + pt[1], pt[2], pt[2] * 0.9);
          c.fill();
        });
      });
    },

    /**
     * Corona.
     *
     * Era una diadema chica de tres puntas, apoyada en la coronilla como un
     * adorno de torta. Una corona real se reconoce por tres cosas que ésta
     * ahora tiene: el aro ancho con piedras engarzadas, cinco florones con
     * perlas en la punta y el terciopelo rojo que asoma entre ellos. Va más
     * ancha que la coronilla, así se lee que la CIÑE.
     */
    corona(c, A, st) {
      const cx = HEAD.cx;
      const oro = A.alt('#f0b429');
      const oroD = adjustL(oro, -28);
      const oroL = mixHex(oro, '#ffffff', 0.6);
      const terciopelo = A.fixed('#a3172f', 0.6);
      const yAro = 3.55, altoAro = 1.15;
      // El aro sigue una curva: más bajo al centro, porque rodea la cabeza.
      const aro = (dx, dy) => yAro + dy + 0.35 * (1 - (dx / 4.2) ** 2);

      // Sombra sobre la cabeza.
      ellipsePath(c, cx, yAro + 1.35, 4.0, 0.55);
      c.fillStyle = 'rgba(0,0,0,0.32)'; c.fill();

      // Terciopelo: la cúpula que asoma entre los florones.
      c.beginPath();
      c.moveTo(cx - 3.6, aro(-3.6, 0.2));
      c.bezierCurveTo(cx - 3.5, 0.4, cx + 3.5, 0.4, cx + 3.6, aro(3.6, 0.2));
      c.closePath();
      const tg = c.createRadialGradient(cx - 1.0, 1.2, 0.2, cx, 2.4, 3.8);
      tg.addColorStop(0, mixHex(terciopelo, '#ffffff', 0.25));
      tg.addColorStop(0.6, terciopelo);
      tg.addColorStop(1, adjustL(terciopelo, -25));
      c.fillStyle = tg; c.fill();

      // Florones: cinco puntas, la del medio más alta.
      const FLORONES = [[-3.4, 1.3], [-1.75, 0.55], [0, -0.1], [1.75, 0.55], [3.4, 1.3]];
      c.beginPath();
      c.moveTo(cx - 3.95, aro(-3.95, 0));
      FLORONES.forEach(([dx, yy], i) => {
        const prev = i ? FLORONES[i - 1][0] : -3.95;
        const valle = (prev + dx) / 2;
        if (i) c.quadraticCurveTo(cx + valle, aro(valle, -0.2), cx + valle, aro(valle, -0.05));
        c.lineTo(cx + dx - 0.45, aro(dx - 0.45, -0.7));
        c.lineTo(cx + dx, yy);
        c.lineTo(cx + dx + 0.45, aro(dx + 0.45, -0.7));
      });
      c.lineTo(cx + 3.95, aro(3.95, 0));
      c.lineTo(cx + 3.95, aro(3.95, altoAro));
      for (let dx = 3.95; dx >= -3.951; dx -= 0.5) c.lineTo(cx + dx, aro(dx, altoAro));
      c.closePath();
      const g = c.createLinearGradient(cx - 4, 0, cx + 4, 0);
      g.addColorStop(0, oroD);
      g.addColorStop(0.25, oroL);
      g.addColorStop(0.5, oro);
      g.addColorStop(0.75, oroL);
      g.addColorStop(1, oroD);
      c.fillStyle = g; c.fill();
      c.lineWidth = 0.08; c.strokeStyle = withA(adjustL(oro, -45), 0.85); c.stroke();

      // El aro: dos filetes y las piedras.
      [0.08, altoAro - 0.12].forEach(dy => {
        c.beginPath();
        for (let dx = -3.95; dx <= 3.951; dx += 0.5) c.lineTo(cx + dx, aro(dx, dy));
        c.lineWidth = 0.08; c.strokeStyle = withA(oroD, 0.9); c.stroke();
      });
      const PIEDRAS = [[-2.7, '#2f66e0'], [-0.9, '#16a862'], [0.9, '#16a862'], [2.7, '#2f66e0']];
      PIEDRAS.forEach(([dx, col]) => {
        const px = cx + dx, py = aro(dx, altoAro / 2);
        const piedra = A.fixed(col, 0.5);
        ellipsePath(c, px, py, 0.34, 0.3);
        c.fillStyle = oroD; c.fill();
        ellipsePath(c, px, py, 0.26, 0.23);
        const pg = c.createRadialGradient(px - 0.08, py - 0.08, 0.02, px, py, 0.28);
        pg.addColorStop(0, mixHex(piedra, '#ffffff', 0.6)); pg.addColorStop(1, adjustL(piedra, -20));
        c.fillStyle = pg; c.fill();
      });
      // La piedra del centro, la grande, con el halo del accesorio.
      const rx = cx, ry = aro(0, altoAro / 2);
      glowing(c, st, 1.6, () => {
        c.beginPath();
        c.moveTo(rx, ry - 0.55); c.lineTo(rx + 0.45, ry); c.lineTo(rx, ry + 0.55); c.lineTo(rx - 0.45, ry);
        c.closePath();
        const rg = c.createLinearGradient(rx - 0.45, ry - 0.55, rx + 0.45, ry + 0.55);
        rg.addColorStop(0, '#ffb3c0'); rg.addColorStop(0.45, A.fixed('#de1f48', 0.5)); rg.addColorStop(1, '#520515');
        c.fillStyle = rg; c.fill();
      });
      c.lineWidth = 0.07; c.strokeStyle = oroD; c.stroke();
      // Perlas en las puntas de los florones, y la del centro con halo.
      glowing(c, st, 1.4, () => {
        FLORONES.forEach(([dx, yy], i) => {
          const r = i === 2 ? 0.42 : 0.3;
          ellipsePath(c, cx + dx, yy - r * 0.6, r, r);
          const pg = c.createRadialGradient(cx + dx - r * 0.35, yy - r, r * 0.1, cx + dx, yy - r * 0.6, r);
          pg.addColorStop(0, '#ffffff'); pg.addColorStop(0.55, A.bulb); pg.addColorStop(1, adjustL(A.bulb, -25));
          c.fillStyle = pg; c.fill();
        });
      }, -0.1 - 0.42 * 1.6);
    },

    vikingo(c, A) {
      const cx = HEAD.cx;
      const y = HEAD.top + 1.2;
      const metal = A.mat;
      const metalLite = A.lite;
      const metalDark = A.dark;
      const hueso = A.alt('#e8e2d2');

      // 1. Sombra de contacto
      c.save();
      c.beginPath();
      c.ellipse(cx, y + 0.3, 3.8, 0.8, 0, 0, Math.PI * 2);
      const sGrad = c.createRadialGradient(cx, y, 1.0, cx, y + 0.3, 3.8);
      sGrad.addColorStop(0, 'rgba(0, 0, 0, 0.55)');
      sGrad.addColorStop(1, 'rgba(0, 0, 0, 0)');
      c.fillStyle = sGrad;
      c.fill();
      c.restore();

      // 2. Cúpula principal (Hachi)
      c.beginPath();
      c.moveTo(cx - 3.4, y + 0.2);
      c.bezierCurveTo(cx - 3.4, y - 3.2, cx + 3.4, y - 3.2, cx + 3.4, y + 0.2);
      c.quadraticCurveTo(cx, y - 0.3, cx - 3.4, y + 0.2);
      c.closePath();
      const g = c.createRadialGradient(cx - 1.2, y - 2.0, 0.4, cx, y - 0.5, 3.8);
      g.addColorStop(0, metalLite);
      g.addColorStop(0.5, metal);
      g.addColorStop(1, adjustL(metalDark, -15));
      c.fillStyle = g; c.fill();

      // 3. Bandas metálicas (refuerzos cruzados)
      // Banda vertical
      c.beginPath();
      c.moveTo(cx - 0.4, y - 0.2);
      c.lineTo(cx - 0.3, y - 3.1);
      c.lineTo(cx + 0.3, y - 3.1);
      c.lineTo(cx + 0.4, y - 0.2);
      c.quadraticCurveTo(cx, y - 0.3, cx - 0.4, y - 0.2);
      c.fillStyle = metalDark; c.fill();
      c.beginPath(); // Bisel
      c.moveTo(cx - 0.2, y - 0.25);
      c.lineTo(cx - 0.1, y - 3.0);
      c.lineWidth = 0.2; c.strokeStyle = metalLite; c.stroke();

      // 4. Aro base (Brim)
      c.beginPath();
      c.moveTo(cx - 3.6, y + 0.1);
      c.quadraticCurveTo(cx, y + 0.8, cx + 3.6, y + 0.1);
      c.quadraticCurveTo(cx + 3.8, y + 0.7, cx + 3.4, y + 1.0);
      c.quadraticCurveTo(cx, y + 1.6, cx - 3.4, y + 1.0);
      c.quadraticCurveTo(cx - 3.8, y + 0.7, cx - 3.6, y + 0.1);
      c.closePath();
      const bg = c.createLinearGradient(cx - 3.5, y + 0.1, cx + 3.5, y + 1.5);
      bg.addColorStop(0, metalDark);
      bg.addColorStop(0.5, metal);
      bg.addColorStop(1, adjustL(metalDark, -20));
      c.fillStyle = bg; c.fill();

      // Remaches en la base
      [ -2.2, -1.1, 0, 1.1, 2.2 ].forEach(rx => {
        ellipsePath(c, cx + rx, y + 0.75 + Math.abs(rx) * -0.08, 0.18, 0.18);
        c.fillStyle = metalLite; c.fill();
        ellipsePath(c, cx + rx, y + 0.75 + Math.abs(rx) * -0.08, 0.08, 0.08);
        c.fillStyle = '#ffffff'; c.fill();
      });

      // 5. Cuernos
      [-1, 1].forEach(sd => {
        c.save();
        // Base del cuerno
        ellipsePath(c, cx + sd * 3.2, y - 0.2, 0.6, 0.8);
        c.fillStyle = metalDark; c.fill();

        // Cuerno de hueso
        c.beginPath();
        c.moveTo(cx + sd * 2.8, y - 0.4);
        c.bezierCurveTo(cx + sd * 4.8, y - 1.2, cx + sd * 5.8, y - 2.8, cx + sd * 5.1, y - 4.5);
        c.bezierCurveTo(cx + sd * 4.4, y - 2.6, cx + sd * 3.4, y - 1.4, cx + sd * 2.6, y - 0.0);
        c.closePath();
        
        const hGrad = c.createLinearGradient(cx + sd * 2.5, y, cx + sd * 5.5, y - 4.5);
        hGrad.addColorStop(0, adjustL(hueso, -20));
        hGrad.addColorStop(0.4, hueso);
        hGrad.addColorStop(0.8, mixHex(hueso, '#ffffff', 0.6));
        hGrad.addColorStop(1, adjustL(hueso, -10));
        c.fillStyle = hGrad; c.fill();

        // Rayas de textura del cuerno
        c.lineWidth = 0.2;
        c.strokeStyle = withA(adjustL(hueso, -35), 0.4);
        for(let i=1; i<=4; i++) {
            c.beginPath();
            let t = i / 6;
            let startX = cx + sd * (2.8 + t * 2.0);
            let startY = y - 0.4 - t * 0.8;
            c.moveTo(startX, startY);
            c.lineTo(startX - sd * 0.3, startY + 0.5);
            c.stroke();
        }
        c.restore();
      });
    },

    chef(c, A) {
      const cx = HEAD.cx;
      const y = HEAD.top + 0.8;
      const tela = A.alt('#f4f6fb');
      const telaDark = adjustL(tela, -15);
      const telaShadow = adjustL(tela, -30);

      // Sombra
      c.save();
      c.beginPath();
      c.ellipse(cx, y + 0.1, 3.2, 0.7, 0, 0, Math.PI * 2);
      const sGrad = c.createRadialGradient(cx, y - 0.1, 0.8, cx, y + 0.1, 3.2);
      sGrad.addColorStop(0, 'rgba(0, 0, 0, 0.4)');
      sGrad.addColorStop(1, 'rgba(0, 0, 0, 0)');
      c.fillStyle = sGrad;
      c.fill();
      c.restore();

      // Nubes (Hongo del chef)
      const nGrad = c.createRadialGradient(cx - 1.5, y - 3.5, 0.5, cx, y - 1.5, 4.5);
      nGrad.addColorStop(0, mixHex(tela, '#ffffff', 0.8));
      nGrad.addColorStop(0.6, tela);
      nGrad.addColorStop(1, telaShadow);
      
      c.fillStyle = nGrad;
      [[-1.8, -2.4, 1.4], [1.8, -2.4, 1.4], [-0.8, -3.2, 1.6], [0.8, -3.2, 1.6], [0, -3.8, 1.8]].forEach(pt => {
        ellipsePath(c, cx + pt[0], y + pt[1], pt[2], pt[2]); c.fill();
      });

      // Sombras entre nubes para volumen
      c.fillStyle = withA(telaShadow, 0.3);
      ellipsePath(c, cx, y - 2.0, 2.5, 1.0); c.fill();

      // Banda inferior cilíndrica
      c.beginPath();
      c.moveTo(cx - 2.8, y - 0.8);
      c.lineTo(cx - 2.8, y + 0.3);
      c.quadraticCurveTo(cx, y + 0.8, cx + 2.8, y + 0.3);
      c.lineTo(cx + 2.8, y - 0.8);
      c.quadraticCurveTo(cx, y - 0.3, cx - 2.8, y - 0.8);
      c.closePath();

      const bGrad = c.createLinearGradient(cx - 2.8, y, cx + 2.8, y);
      bGrad.addColorStop(0, telaShadow);
      bGrad.addColorStop(0.2, telaDark);
      bGrad.addColorStop(0.5, mixHex(tela, '#ffffff', 0.5));
      bGrad.addColorStop(0.8, telaDark);
      bGrad.addColorStop(1, telaShadow);
      c.fillStyle = bGrad; c.fill();
      
      // Pliegues en la banda
      c.lineWidth = 0.2;
      c.strokeStyle = withA(telaShadow, 0.5);
      [-1.5, -0.5, 0.5, 1.5].forEach(lx => {
         c.beginPath(); c.moveTo(cx + lx, y - 0.5); c.lineTo(cx + lx, y + 0.5 + Math.abs(lx)*-0.05); c.stroke();
      });
    },

    halo(c, A, st) {
      const cx = HEAD.cx;
      const y = HEAD.top - 1.05;
      const bulb = A.alt(A.bulb, 1);
      
      // Luz sobre la cabeza
      c.save();
      c.beginPath();
      c.ellipse(cx, HEAD.top + 0.2, 2.5, 0.7, 0, 0, Math.PI * 2);
      const sGrad = c.createRadialGradient(cx, HEAD.top - 0.2, 0.5, cx, HEAD.top + 0.2, 2.5);
      sGrad.addColorStop(0, withA(bulb, 0.4));
      sGrad.addColorStop(1, 'rgba(0,0,0,0)');
      c.fillStyle = sGrad; c.fill();
      c.restore();

      glowing(c, st, 2.8, () => {
        c.beginPath();
        c.ellipse(cx, y, 2.5, 0.78, 0, 0, Math.PI * 2);
        c.ellipse(cx, y, 2.1, 0.65, 0, 0, Math.PI * 2, true);
        c.fillStyle = bulb;
        c.fill();
        // Inner core
        c.beginPath();
        c.ellipse(cx, y, 2.3, 0.71, 0, 0, Math.PI * 2);
        c.lineWidth = 0.2;
        c.strokeStyle = '#ffffff';
        c.stroke();
      }, y - 0.78);
      // Destellos alrededor del aro: sin ellos era un anillo gris flotando.
      [[-2.9, -0.35, 0.34], [2.7, -0.7, 0.26], [1.1, 0.85, 0.2]].forEach(([dx, dy, r]) =>
        destello(c, cx + dx, y + dy, r, mixHex(bulb, '#ffffff', 0.5), 0.5));
    },

    lentes(c, A) {
      const cx = HEAD.cx;
      const y = BODY.top + (BODY.bot - BODY.top) * 0.38;
      const cristal = A.alt('#161a21');
      const marco = A.mat;

      // Sombras de contacto
      [-1, 1].forEach(sd => {
        c.save();
        c.beginPath();
        roundRect(c, cx + sd * 2.40 - 1.95, y - 1.1, 3.9, 2.7, 0.85);
        c.fillStyle = 'rgba(0,0,0,0.25)';
        c.fill();
        c.restore();
      });

      // Lentes
      [-1, 1].forEach(sd => {
        const lx = cx + sd * 2.40;
        // Marco exterior
        roundRect(c, lx - 2.15, y - 1.55, 4.3, 3.1, 1.0);
        c.fillStyle = adjustL(marco, -20); c.fill();
        
        // Marco interior frontal
        roundRect(c, lx - 1.95, y - 1.35, 3.9, 2.7, 0.85);
        c.fillStyle = marco; c.fill();
        
        // Cristal
        roundRect(c, lx - 1.6, y - 1.0, 3.2, 2.0, 0.5);
        const g = c.createLinearGradient(lx - 1.6, y - 1.0, lx + 1.6, y + 1.0);
        g.addColorStop(0, adjustL(cristal, 20));
        g.addColorStop(1, cristal);
        c.fillStyle = g; c.fill();
        
        // Reflejo
        c.save();
        c.clip();
        c.globalAlpha = 0.30;
        c.beginPath();
        c.moveTo(lx - 1.9, y + 1.4);
        c.lineTo(lx - 0.5, y - 1.4);
        c.lineTo(lx + 0.3, y - 1.4);
        c.lineTo(lx - 1.1, y + 1.4);
        c.closePath();
        c.fillStyle = '#ffffff'; c.fill();
        c.restore();
      });

      // Puente y patillas
      c.beginPath();
      c.lineWidth = 0.4; c.lineCap = 'round';
      c.strokeStyle = marco;
      // Puente central
      c.moveTo(cx - 0.45, y - 0.2); c.quadraticCurveTo(cx, y - 0.4, cx + 0.45, y - 0.2);
      // Patillas (curvas hacia atrás)
      c.moveTo(cx - 4.5, y - 0.5); c.quadraticCurveTo(cx - 5.5, y - 0.7, cx - 6.0, y - 1.2);
      c.moveTo(cx + 4.5, y - 0.5); c.quadraticCurveTo(cx + 5.5, y - 0.7, cx + 6.0, y - 1.2);
      c.stroke();
    },

    gato(c, A) {
      const cx = HEAD.cx;
      const y = HEAD.top + 1.0;
      const dentro = A.alt('#e8899f');
      
      // Sombra
      c.save();
      c.beginPath();
      c.ellipse(cx, y + 0.3, 3.2, 0.5, 0, 0, Math.PI * 2);
      c.fillStyle = 'rgba(0,0,0,0.2)';
      c.fill();
      c.restore();

      [-1, 1].forEach(sd => {
        c.save();
        
        // Base trasera (volumen)
        c.beginPath();
        c.moveTo(cx + sd * 1.35, y + 0.6);
        c.lineTo(cx + sd * 2.65, y - 2.9);
        c.lineTo(cx + sd * 4.05, y - 0.15);
        c.closePath();
        c.fillStyle = adjustL(A.dark, -15); c.fill();

        // Oreja principal
        c.beginPath();
        c.moveTo(cx + sd * 1.05, y + 0.6);
        c.quadraticCurveTo(cx + sd * 1.7, y - 1.2, cx + sd * 2.45, y - 3.0);
        c.quadraticCurveTo(cx + sd * 3.1, y - 1.5, cx + sd * 3.75, y - 0.15);
        c.quadraticCurveTo(cx + sd * 2.4, y + 0.8, cx + sd * 1.05, y + 0.6); // base curva
        c.closePath();
        
        const g = c.createRadialGradient(cx + sd * 2.45, y - 2.0, 0.5, cx + sd * 2.45, y, 3.5);
        g.addColorStop(0, A.lite);
        g.addColorStop(0.5, A.mat);
        g.addColorStop(1, A.dark);
        c.fillStyle = g; c.fill();
        
        // Interior
        c.beginPath();
        c.moveTo(cx + sd * 1.65, y + 0.25);
        c.quadraticCurveTo(cx + sd * 2.1, y - 0.8, cx + sd * 2.45, y - 2.2);
        c.quadraticCurveTo(cx + sd * 2.8, y - 1.0, cx + sd * 3.25, y - 0.25);
        c.quadraticCurveTo(cx + sd * 2.4, y + 0.4, cx + sd * 1.65, y + 0.25);
        c.closePath();
        
        const ig = c.createLinearGradient(0, y - 2.0, 0, y + 0.5);
        ig.addColorStop(0, adjustL(dentro, 15));
        ig.addColorStop(1, adjustL(dentro, -20));
        c.fillStyle = ig; c.fill();

        c.restore();

        // Bigotes
        c.beginPath();
        c.lineWidth = 0.25;
        c.lineCap = 'round';
        c.strokeStyle = withA(adjustL(A.dark, -30), 0.6);
        
        const wx = cx + sd * 3.0;
        const wy = BODY.top + (BODY.bot - BODY.top) * 0.45;

        c.moveTo(wx, wy - 0.3);
        c.lineTo(wx + sd * 2.0, wy - 0.5);
        
        c.moveTo(wx, wy);
        c.lineTo(wx + sd * 2.2, wy + 0.1);
        
        c.moveTo(wx, wy + 0.3);
        c.lineTo(wx + sd * 2.0, wy + 0.6);
        c.stroke();
      });
    },

    /**
     * Mini OVNI.
     *
     * Un plato volador de metal que flota sobre la cabeza y lo está
     * abduciendo. El de antes era un disco oscuro chato y se leía como un
     * sombrero visto de canto. Lo que lo hace OVNI: la cúpula de vidrio con
     * un tripulante, el anillo de luces en el canto y el haz que baja.
     *
     * El haz termina en la coronilla: si bajara a la cara, lavaría los ojos.
     */
    ovni(c, A, st) {
      const cx = HEAD.cx;
      const y = 1.2;
      const metal = A.mat;
      const luz = A.alt(A.bulb, 1);
      const verde = A.fixed('#7ddc6a');

      // Haz: cono de luz con anillos que bajan.
      c.save();
      c.beginPath();
      c.moveTo(cx - 1.4, y + 0.9);
      c.lineTo(cx + 1.4, y + 0.9);
      c.lineTo(cx + 3.3, 4.2);
      c.quadraticCurveTo(cx, 4.75, cx - 3.3, 4.2);
      c.closePath();
      const hg = c.createLinearGradient(0, y + 0.9, 0, 4.6);
      hg.addColorStop(0, withA(luz, 0.65));
      hg.addColorStop(1, withA(luz, 0.08));
      c.fillStyle = hg; c.fill();
      c.clip();
      [2.4, 3.3, 4.1].forEach((yy, i) => {
        c.beginPath(); c.ellipse(cx, yy, 1.9 + i * 0.6, 0.2, 0, 0, Math.PI * 2);
        c.lineWidth = 0.07; c.strokeStyle = withA('#ffffff', 0.45 - i * 0.1); c.stroke();
      });
      c.restore();

      // Panza del plato: una cúpula chica abajo, con su anillo encendido.
      c.beginPath(); c.ellipse(cx, y + 0.5, 2.1, 0.65, 0, 0, Math.PI);
      c.fillStyle = adjustL(metal, -30); c.fill();
      glowing(c, st, 1.4, () => {
        c.beginPath(); c.ellipse(cx, y + 0.75, 1.25, 0.28, 0, 0, Math.PI * 2);
        c.fillStyle = luz; c.fill();
      });

      // Cúpula de vidrio con el tripulante adentro.
      const domo = () => { c.beginPath(); c.ellipse(cx, y - 0.25, 1.75, 1.6, 0, Math.PI, 0); c.closePath(); };
      domo();
      c.fillStyle = withA(mixHex(luz, '#9fe8ff', 0.6), 0.35); c.fill();
      c.save();
      domo(); c.clip();
      // El tripulante: cabeza verde con dos ojos grandes y antenitas.
      ellipsePath(c, cx, y - 0.55, 0.62, 0.68);
      c.fillStyle = verde; c.fill();
      [-1, 1].forEach(sd => {
        c.beginPath(); c.ellipse(cx + sd * 0.25, y - 0.6, 0.2, 0.28, sd * 0.4, 0, Math.PI * 2);
        c.fillStyle = '#0b1a10'; c.fill();
        ellipsePath(c, cx + sd * 0.25 - 0.06, y - 0.7, 0.06, 0.06);
        c.fillStyle = '#ffffff'; c.fill();
        c.beginPath(); c.moveTo(cx + sd * 0.25, y - 1.15); c.lineTo(cx + sd * 0.45, y - 1.55);
        c.lineWidth = 0.06; c.strokeStyle = adjustL(verde, -25); c.stroke();
        ellipsePath(c, cx + sd * 0.45, y - 1.6, 0.1, 0.1);
        c.fillStyle = verde; c.fill();
      });
      c.restore();
      // Reflejo del vidrio.
      c.beginPath(); c.ellipse(cx - 0.7, y - 1.05, 0.5, 0.22, -0.6, 0, Math.PI * 2);
      c.fillStyle = withA('#ffffff', 0.75); c.fill();
      domo();
      c.lineWidth = 0.08; c.strokeStyle = withA('#ffffff', 0.55); c.stroke();

      // Plato: disco de metal con la luz arriba a la izquierda.
      c.beginPath(); c.ellipse(cx, y, 4.4, 0.95, 0, 0, Math.PI * 2);
      const g = c.createLinearGradient(0, y - 0.95, 0, y + 0.95);
      g.addColorStop(0, mixHex(A.lite, '#ffffff', 0.55));
      g.addColorStop(0.45, metal);
      g.addColorStop(1, adjustL(A.dark, -26));
      c.fillStyle = g; c.fill();
      // Junta del domo con el plato y el canto.
      c.beginPath(); c.ellipse(cx, y - 0.25, 1.85, 0.32, 0, 0, Math.PI);
      c.lineWidth = 0.12; c.strokeStyle = adjustL(A.dark, -20); c.stroke();
      c.beginPath(); c.ellipse(cx, y + 0.1, 4.4, 0.85, 0, 0.05 * Math.PI, 0.95 * Math.PI);
      c.lineWidth = 0.1; c.strokeStyle = withA(adjustL(A.dark, -35), 0.8); c.stroke();
      c.beginPath(); c.ellipse(cx, y - 0.05, 4.2, 0.8, 0, 1.08 * Math.PI, 1.6 * Math.PI);
      c.lineWidth = 0.09; c.strokeStyle = withA('#ffffff', 0.7); c.stroke();
      // Anillo de luces por el canto: alternan la luz del accesorio y un ámbar.
      const ambar = A.fixed('#ffb03a');
      glowing(c, st, 1.2, () => {
        for (let i = 0; i < 9; i++) {
          const a = Math.PI * (0.12 + i * 0.095);
          const lx = cx + Math.cos(a) * 3.85, ly = y + 0.12 + Math.sin(a) * 0.62;
          ellipsePath(c, lx, ly, 0.2, 0.13);
          c.fillStyle = i % 2 ? ambar : luz; c.fill();
        }
      });
    },

    santa(c, A) {
      const cx = HEAD.cx;
      const y = HEAD.top + 0.8;
      const tela = A.alt('#dc2626');
      const felpa = A.fixed('#f4f6fb');

      // Sombra
      c.save();
      c.beginPath();
      c.ellipse(cx, y + 0.3, 3.5, 0.7, 0, 0, Math.PI * 2);
      c.fillStyle = 'rgba(0,0,0,0.4)'; c.fill();
      c.restore();

      // Cuerpo del gorro
      c.beginPath();
      c.moveTo(cx - 3.4, y + 0.2);
      c.bezierCurveTo(cx - 1.5, y - 4.5, cx + 4.5, y - 3.2, cx + 3.4, y - 2.8);
      c.bezierCurveTo(cx + 2.0, y - 1.0, cx + 2.8, y + 0.2, cx + 3.4, y + 0.2);
      c.quadraticCurveTo(cx, y - 0.2, cx - 3.4, y + 0.2);
      c.closePath();
      
      const g = c.createRadialGradient(cx - 1.0, y - 1.5, 0.5, cx, y, 4.0);
      g.addColorStop(0, adjustL(tela, 15));
      g.addColorStop(0.5, tela);
      g.addColorStop(1, adjustL(tela, -25));
      c.fillStyle = g; c.fill();

      // Pliegues
      c.beginPath();
      c.moveTo(cx - 0.5, y - 0.1); c.quadraticCurveTo(cx + 1.0, y - 2.0, cx + 3.0, y - 2.5);
      c.lineWidth = 0.4; c.strokeStyle = withA(adjustL(tela, -35), 0.4); c.stroke();
      
      // Pompón
      c.beginPath();
      c.ellipse(cx + 3.6, y - 2.8, 0.85, 0.85, 0, 0, Math.PI * 2);
      const pg = c.createRadialGradient(cx + 3.4, y - 3.0, 0.1, cx + 3.6, y - 2.8, 0.85);
      pg.addColorStop(0, '#ffffff');
      pg.addColorStop(0.7, felpa);
      pg.addColorStop(1, adjustL(felpa, -20));
      c.fillStyle = pg; c.fill();

      // Banda de felpa
      c.beginPath();
      c.moveTo(cx - 3.7, y + 0.1);
      c.quadraticCurveTo(cx - 3.7, y - 1.0, cx - 3.4, y - 1.0);
      c.quadraticCurveTo(cx, y - 1.2, cx + 3.4, y - 1.0);
      c.quadraticCurveTo(cx + 3.7, y - 1.0, cx + 3.7, y + 0.1);
      c.quadraticCurveTo(cx, y + 1.0, cx - 3.7, y + 0.1);
      c.closePath();
      const fg = c.createLinearGradient(0, y - 1.0, 0, y + 0.6);
      fg.addColorStop(0, mixHex(felpa, '#ffffff', 0.5));
      fg.addColorStop(0.6, felpa);
      fg.addColorStop(1, adjustL(felpa, -25));
      c.fillStyle = fg; c.fill();
    },

    copa(c, A) {
      const cx = HEAD.cx;
      const y = HEAD.top + 0.85;

      // Sombra
      c.save();
      c.beginPath();
      c.ellipse(cx, y + 0.1, 4.0, 0.8, 0, 0, Math.PI * 2);
      c.fillStyle = 'rgba(0,0,0,0.45)'; c.fill();
      c.restore();

      // Ala del sombrero (Brim)
      c.beginPath();
      c.ellipse(cx, y - 0.2, 3.8, 0.9, 0, 0, Math.PI * 2);
      const brimG = c.createRadialGradient(cx, y - 0.5, 1.5, cx, y - 0.2, 4.0);
      brimG.addColorStop(0, A.mat);
      brimG.addColorStop(1, adjustL(A.dark, -20));
      c.fillStyle = brimG; c.fill();

      // Cilindro principal (Copa)
      c.beginPath();
      c.moveTo(cx - 2.2, y - 0.6);
      c.lineTo(cx - 2.15, y - 3.8);
      c.quadraticCurveTo(cx, y - 4.2, cx + 2.15, y - 3.8);
      c.lineTo(cx + 2.2, y - 0.6);
      c.quadraticCurveTo(cx, y - 0.1, cx - 2.2, y - 0.6);
      c.closePath();
      
      const g = c.createLinearGradient(cx - 2.2, y - 2.0, cx + 2.2, y - 2.0);
      g.addColorStop(0, adjustL(A.dark, -10));
      g.addColorStop(0.2, A.mat);
      g.addColorStop(0.6, A.lite);
      g.addColorStop(0.85, A.mat);
      g.addColorStop(1, adjustL(A.dark, -25));
      c.fillStyle = g; c.fill();

      // Tapa superior del cilindro
      c.beginPath();
      c.ellipse(cx, y - 3.8, 2.15, 0.45, 0, 0, Math.PI * 2);
      c.fillStyle = adjustL(A.mat, 15); c.fill();

      // Cinta
      c.beginPath();
      c.moveTo(cx - 2.2, y - 0.6);
      c.lineTo(cx - 2.2, y - 1.5);
      c.quadraticCurveTo(cx, y - 1.0, cx + 2.2, y - 1.5);
      c.lineTo(cx + 2.2, y - 0.6);
      c.quadraticCurveTo(cx, y - 0.1, cx - 2.2, y - 0.6);
      c.closePath();
      
      // Cinta de seda bordó por defecto: gris claro, como estaba, se leía
      // como una costura y la galera quedaba un tubo negro.
      const seda = A.alt('#9e1b2e');
      const cg = c.createLinearGradient(cx - 2.2, y - 1.0, cx + 2.2, y - 1.0);
      cg.addColorStop(0, adjustL(seda, -15));
      cg.addColorStop(0.45, mixHex(seda, '#ffffff', 0.25));
      cg.addColorStop(1, adjustL(seda, -25));
      c.fillStyle = cg; c.fill();
      // El moño de la cinta, del lado izquierdo.
      [-1, 1].forEach(sd => {
        c.beginPath();
        c.moveTo(cx - 1.45, y - 1.0);
        c.quadraticCurveTo(cx - 1.45 + sd * 0.75, y - 1.75, cx - 1.45 + sd * 0.85, y - 0.95);
        c.quadraticCurveTo(cx - 1.45 + sd * 0.7, y - 0.45, cx - 1.45, y - 1.0);
        c.fillStyle = adjustL(seda, sd < 0 ? -10 : 6); c.fill();
      });
      ellipsePath(c, cx - 1.45, y - 1.0, 0.2, 0.24);
      c.fillStyle = adjustL(seda, -22); c.fill();
      // Brillo vertical de la felpa y canto del ala.
      c.beginPath();
      c.moveTo(cx - 1.25, y - 3.65); c.lineTo(cx - 1.25, y - 1.6);
      c.lineWidth = 0.22; c.lineCap = 'round'; c.strokeStyle = withA('#ffffff', 0.22); c.stroke();
      c.beginPath(); c.ellipse(cx, y - 0.2, 3.8, 0.9, 0, Math.PI * 0.05, Math.PI * 0.95);
      c.lineWidth = 0.1; c.strokeStyle = withA(A.lite, 0.8); c.stroke();
    },

    /**
     * Pirata: pañuelo y parche.
     *
     * El pañuelo era un domito liso y el parche un cuadrado negro que tapaba
     * dos tercios del ojo: por abajo asomaba el iris y se leía como un ojo
     * tapado con cinta. Ahora el pañuelo cubre toda la coronilla, con lunares,
     * la calavera al frente y el nudo con las puntas al costado; el parche
     * tapa el ojo ENTERO, tiene costura y la tira cruza la frente por debajo
     * del pañuelo.
     *
     * Va en la capa de adelante (HATS_FRONT): el parche tiene que pintarse
     * encima del ojo.
     */
    pirata(c, A) {
      const cx = HEAD.cx;
      const ey = BODY.top + (BODY.bot - BODY.top) * 0.38;
      const ex = cx + 2.28;
      const parche = A.alt('#15151a');
      const blanco = A.fixed('#f4efe4', 0.4);

      // Tira del parche: por la frente hacia la izquierda y por la derecha.
      c.lineCap = 'round';
      c.beginPath();
      c.moveTo(cx - 5.2, 5.3);
      c.quadraticCurveTo(cx - 1.0, 4.45, ex - 1.4, ey - 1.6);
      c.moveTo(ex + 1.8, ey - 0.6);
      c.quadraticCurveTo(cx + 5.6, ey - 0.8, cx + 5.9, ey + 0.3);
      c.lineWidth = 0.32; c.strokeStyle = parche; c.stroke();

      // Parche: un escudo redondeado de cuero que cubre el ojo con su aro.
      const forma = () => {
        c.beginPath();
        c.moveTo(ex - 2.0, ey - 1.55);
        c.quadraticCurveTo(ex, ey - 2.75, ex + 2.0, ey - 1.55);
        c.quadraticCurveTo(ex + 2.35, ey + 0.9, ex, ey + 2.6);
        c.quadraticCurveTo(ex - 2.35, ey + 0.9, ex - 2.0, ey - 1.55);
        c.closePath();
      };
      c.save();
      c.translate(0.12, 0.18); forma();
      c.fillStyle = 'rgba(0,0,0,0.35)'; c.fill();
      c.restore();
      forma();
      const pg = c.createRadialGradient(ex - 0.7, ey - 1.0, 0.2, ex, ey, 2.8);
      pg.addColorStop(0, adjustL(parche, 22));
      pg.addColorStop(1, adjustL(parche, -10));
      c.fillStyle = pg; c.fill();
      // Costura por dentro del borde.
      c.save();
      c.translate(ex, ey); c.scale(0.86, 0.86); c.translate(-ex, -ey);
      forma();
      c.restore();
      c.setLineDash([0.22, 0.18]);
      c.lineWidth = 0.07; c.strokeStyle = withA(adjustL(parche, 45), 0.8); c.stroke();
      c.setLineDash([]);
      // Brillo del cuero.
      c.beginPath(); c.ellipse(ex - 0.8, ey - 1.15, 0.6, 0.22, -0.5, 0, Math.PI * 2);
      c.fillStyle = withA('#ffffff', 0.18); c.fill();

      // Pañuelo: cubre la coronilla hasta arriba de los ojos.
      const panuelo = () => {
        c.beginPath();
        c.moveTo(cx - 5.0, 5.05);
        c.bezierCurveTo(cx - 5.2, 2.2, cx - 2.8, 1.35, cx, 1.35);
        c.bezierCurveTo(cx + 2.8, 1.35, cx + 5.2, 2.2, cx + 5.0, 5.05);
        c.quadraticCurveTo(cx, 4.5, cx - 5.0, 5.05);
        c.closePath();
      };
      panuelo();
      const dg = c.createRadialGradient(cx - 1.3, 2.2, 0.2, cx, 3.3, 5.2);
      dg.addColorStop(0, A.lite);
      dg.addColorStop(0.55, A.mat);
      dg.addColorStop(1, adjustL(A.dark, -22));
      c.fillStyle = dg; c.fill();
      c.save();
      panuelo(); c.clip();
      // Lunares.
      [[-3.6, 3.6], [-2.4, 2.4], [-3.2, 4.6], [2.5, 2.5], [3.6, 3.8], [1.2, 1.9], [-1.0, 1.9], [3.3, 4.7], [-4.3, 4.2]]
        .forEach(([dx, yy]) => { ellipsePath(c, cx + dx, yy, 0.22, 0.19); c.fillStyle = withA(blanco, 0.85); c.fill(); });
      // Pliegues de la tela, que van hacia el nudo.
      [[3.1, 1.9], [3.6, 3.0]].forEach(([dx, yy]) => {
        c.beginPath(); c.moveTo(cx + dx - 2.4, yy - 0.4); c.quadraticCurveTo(cx + dx - 0.8, yy + 0.2, cx + 4.9, 4.2);
        c.lineWidth = 0.12; c.strokeStyle = withA(adjustL(A.dark, -30), 0.5); c.stroke();
      });
      c.restore();
      // Dobladillo.
      c.beginPath(); c.moveTo(cx - 5.0, 5.05); c.quadraticCurveTo(cx, 4.5, cx + 5.0, 5.05);
      c.lineWidth = 0.2; c.strokeStyle = adjustL(A.dark, -15); c.stroke();
      // Calavera y tibias al frente.
      const sx = cx - 0.2, sy = 2.95;
      c.lineWidth = 0.26; c.strokeStyle = blanco;
      [[-1, 1], [1, -1]].forEach(([a, b]) => {
        c.beginPath(); c.moveTo(sx - 0.85, sy + 0.25 * a + 0.35); c.lineTo(sx + 0.85, sy + 0.25 * b + 0.35); c.stroke();
      });
      ellipsePath(c, sx, sy - 0.12, 0.55, 0.5);
      c.fillStyle = blanco; c.fill();
      roundRect(c, sx - 0.3, sy + 0.2, 0.6, 0.38, 0.1);
      c.fillStyle = blanco; c.fill();
      [-0.2, 0.2].forEach(dx => {
        ellipsePath(c, sx + dx, sy - 0.12, 0.13, 0.15);
        c.fillStyle = adjustL(A.dark, -25); c.fill();
      });

      // Nudo y puntas, del lado derecho.
      [[0.15, 1.9, 3.1], [0.75, 1.55, 2.6]].forEach(([dx, w, l]) => {
        c.beginPath();
        c.moveTo(cx + 4.9, 4.2);
        c.quadraticCurveTo(cx + 5.8 + dx, 4.4, cx + 5.5 + w + dx, 4.2 + l);
        c.quadraticCurveTo(cx + 5.3 + dx, 4.7 + l * 0.5, cx + 4.85, 4.9);
        c.closePath();
        const tg = c.createLinearGradient(cx + 4.9, 4.2, cx + 6.5, 4.2 + l);
        tg.addColorStop(0, A.mat); tg.addColorStop(1, adjustL(A.dark, -25));
        c.fillStyle = tg; c.fill();
      });
      ellipsePath(c, cx + 4.95, 4.45, 0.62, 0.55);
      const ng = c.createRadialGradient(cx + 4.8, 4.25, 0.05, cx + 4.95, 4.45, 0.65);
      ng.addColorStop(0, A.lite); ng.addColorStop(1, adjustL(A.dark, -20));
      c.fillStyle = ng; c.fill();
    },

    /**
     * Kabuto.
     *
     * Lo que hace leer "samurái" de lejos es la SILUETA del casco, no la
     * cantidad de piezas: un cuenco de laca que se apoya sobre la cabeza, las
     * dos solapas vueltas hacia atrás (fukigaeshi) y la media luna dorada al
     * frente. La versión anterior era un casquito chico encaramado arriba,
     * con láminas sueltas pegadas a las mejillas y un cordón que cruzaba la
     * cara: se leía como un collar con cajitas, no como una armadura.
     *
     * La cara queda libre: el borde del cuenco no baja de y 5.15 (el aro del
     * ojo arranca en 5.3) y las solapas viven por fuera de x 3.5 / 12.5. La
     * coraza (dō) arranca debajo de la boca. El cubrenuca y la katana van
     * detrás del cuerpo (HATS_BACK.samurai).
     *
     * La laca sigue el color del accesorio y el oro el segundo color. El rojo
     * del cordón es de la pieza.
     */
    samurai(c, A, st) {
      const cx = HEAD.cx;
      const oro = A.alt('#e6b84a');
      const oroL = A.altLite('#e6b84a');
      const oroD = A.altDark('#e6b84a');
      const rojo = A.fixed('#c43122');
      const laca = A.mat;
      // Laca: la luz es una franja angosta y muy clara. Con un degradé ancho
      // el cuenco se leía de plástico; el brillo duro es lo que dice "laca".
      const lacaLuz = mixHex(A.lite, '#ffffff', 0.35);

      // Dō: la coraza de la panza, en láminas de laca cosidas con cordón. Va
      // por debajo de la boca (la sonrisa baja hasta y ~11.2) y adentro de la
      // silueta, como el yukata. Las juntas se curvan hacia abajo en el centro
      // porque rodean una esfera vista de frente: rectas, la coraza se leía
      // como un cartel pegado encima.
      const junta = (b, x) => b - 0.5 * Math.min(1, ((x - cx) / BODY.rx) ** 2);
      const JUNTAS = [11.5, 12.45, 13.4, 14.35, 15.6];
      c.save();
      blobPath(c, cx, BODY.top, BODY.bot, BODY.rx);
      c.clip();
      const tramo = (b) => {
        for (let x = cx - BODY.rx - 0.5; x <= cx + BODY.rx + 0.5; x += 0.5) c.lineTo(x, junta(b, x));
      };
      for (let i = 0; i < JUNTAS.length - 1; i++) {
        const a = JUNTAS[i], b = JUNTAS[i + 1];
        c.beginPath();
        c.moveTo(cx - BODY.rx - 0.5, junta(a, cx - BODY.rx - 0.5));
        tramo(a);
        for (let x = cx + BODY.rx + 0.5; x >= cx - BODY.rx - 0.5; x -= 0.5) c.lineTo(x, junta(b, x) + 0.05);
        c.closePath();
        const lg = c.createLinearGradient(0, a - 0.5, 0, b);
        lg.addColorStop(0, adjustL(laca, 12 - i * 3));
        lg.addColorStop(1, adjustL(A.dark, -16 - i * 3));
        c.fillStyle = lg;
        c.fill();
        // Kebiki: el cordón cosido en columnas apretadas a lo ancho.
        c.lineWidth = 0.2;
        c.strokeStyle = rojo;
        for (let x = cx - BODY.rx; x <= cx + BODY.rx + 0.01; x += 0.62) {
          const y0 = junta(a, x) + 0.2;
          c.beginPath(); c.moveTo(x, y0); c.lineTo(x, y0 + 0.5); c.stroke();
        }
        // Canto de oro abajo de cada lámina, con su sombra sobre la siguiente.
        c.beginPath(); c.moveTo(cx - BODY.rx - 0.5, junta(b, cx - BODY.rx - 0.5)); tramo(b);
        c.lineWidth = 0.22; c.strokeStyle = withA(adjustL(A.dark, -40), 0.7); c.stroke();
        c.beginPath(); c.moveTo(cx - BODY.rx - 0.5, junta(b, cx - BODY.rx - 0.5) - 0.1); tramo(b - 0.1);
        c.lineWidth = 0.08; c.strokeStyle = oro; c.stroke();
      }
      // Volumen: la coraza es una lámina curva, más clara a la izquierda.
      const vg = c.createLinearGradient(cx - BODY.rx, 0, cx + BODY.rx, 0);
      vg.addColorStop(0, withA(lacaLuz, 0.18));
      vg.addColorStop(0.45, withA(lacaLuz, 0));
      vg.addColorStop(1, withA('#000000', 0.3));
      c.beginPath(); c.moveTo(cx - BODY.rx - 0.5, junta(JUNTAS[0], cx - BODY.rx - 0.5)); tramo(JUNTAS[0]);
      c.lineTo(cx + BODY.rx + 0.5, 16); c.lineTo(cx - BODY.rx - 0.5, 16); c.closePath();
      c.fillStyle = vg; c.fill();
      // El borde de arriba: un ribete grueso, que es lo que la despega del cuerpo.
      c.beginPath(); c.moveTo(cx - BODY.rx - 0.5, junta(JUNTAS[0], cx - BODY.rx - 0.5)); tramo(JUNTAS[0]);
      c.lineWidth = 0.36; c.strokeStyle = adjustL(A.dark, -30); c.stroke();
      c.lineWidth = 0.12; c.strokeStyle = oro; c.stroke();
      c.restore();
      // El mon del clan al centro del pecho.
      ellipsePath(c, cx, 12.95, 0.5, 0.5);
      c.fillStyle = adjustL(A.dark, -25); c.fill();
      c.lineWidth = 0.12; c.strokeStyle = oro; c.stroke();
      for (let i = 0; i < 3; i++) {
        const a = -Math.PI / 2 + i * (Math.PI * 2 / 3);
        ellipsePath(c, cx + Math.cos(a) * 0.2, 12.95 + Math.sin(a) * 0.2, 0.12, 0.12);
        c.fillStyle = oroL; c.fill();
      }

      // Fukigaeshi: la solapa se dobla hacia afuera y muestra el forro.
      [-1, 1].forEach((sd) => {
        const X = (dx) => cx + sd * dx;
        const solapa = () => {
          c.beginPath();
          c.moveTo(X(4.35), 3.45);
          c.quadraticCurveTo(X(5.7), 2.85, X(6.85), 2.95);
          c.quadraticCurveTo(X(7.45), 3.9, X(7.0), 5.05);
          c.quadraticCurveTo(X(5.9), 5.55, X(4.6), 5.15);
          c.closePath();
        };
        solapa();
        const sg = c.createLinearGradient(X(4.4), 3.0, X(7.1), 5.2);
        sg.addColorStop(0, adjustL(laca, 10));
        sg.addColorStop(1, adjustL(A.dark, -18));
        c.fillStyle = sg;
        c.fill();
        // El forro rojo asoma por el canto exterior, donde la solapa se dobla.
        c.save();
        solapa(); c.clip();
        c.beginPath();
        c.moveTo(X(6.55), 2.7);
        c.quadraticCurveTo(X(7.15), 3.9, X(6.75), 5.35);
        c.lineTo(X(7.8), 5.35); c.lineTo(X(7.8), 2.7); c.closePath();
        c.fillStyle = adjustL(rojo, -8);
        c.fill();
        c.restore();
        solapa();
        c.lineWidth = 0.13; c.strokeStyle = oro; c.stroke();
        // El mon: un anillo de oro con tres puntos, como un sello de clan.
        const mx = X(5.75), my = 4.15;
        ellipsePath(c, mx, my, 0.5, 0.5);
        const mg = c.createRadialGradient(mx - 0.15, my - 0.18, 0.05, mx, my, 0.5);
        mg.addColorStop(0, oroL); mg.addColorStop(1, oroD);
        c.fillStyle = mg; c.fill();
        ellipsePath(c, mx, my, 0.33, 0.33);
        c.fillStyle = adjustL(A.dark, -20); c.fill();
        for (let i = 0; i < 3; i++) {
          const a = -Math.PI / 2 + i * (Math.PI * 2 / 3);
          ellipsePath(c, mx + Math.cos(a) * 0.15, my + Math.sin(a) * 0.15, 0.09, 0.09);
          c.fillStyle = oroL; c.fill();
        }
      });

      // Hachi: el cuenco. Más ancho que la cabeza, así se lee que la cubre.
      const cuenco = () => {
        c.beginPath();
        c.moveTo(cx - 4.95, 4.95);
        c.bezierCurveTo(cx - 5.1, 2.35, cx - 2.95, 0.9, cx, 0.9);
        c.bezierCurveTo(cx + 2.95, 0.9, cx + 5.1, 2.35, cx + 4.95, 4.95);
        c.quadraticCurveTo(cx, 5.4, cx - 4.95, 4.95);
        c.closePath();
      };
      cuenco();
      const hg = c.createRadialGradient(cx - 1.4, 1.9, 0.2, cx + 0.4, 3.4, 5.2);
      hg.addColorStop(0, A.lite);
      hg.addColorStop(0.5, laca);
      hg.addColorStop(1, adjustL(A.dark, -22));
      c.fillStyle = hg;
      c.fill();
      c.save();
      c.clip();
      // Suji: las placas remachadas que forman el cuenco, como meridianos que
      // nacen en el tehen. Cada una es una arista: sombra a un lado, luz al otro.
      for (let k = -5; k <= 5; k++) {
        if (k === 0) continue;
        const fin = cx + k * 0.92;
        const ctl = cx + k * 0.78;
        c.beginPath(); c.moveTo(cx + k * 0.06, 1.05);
        c.quadraticCurveTo(ctl, 1.7, fin, 5.3);
        c.lineWidth = 0.12; c.strokeStyle = withA(adjustL(A.dark, -30), 0.75); c.stroke();
        c.beginPath(); c.moveTo(cx + k * 0.06 - 0.07, 1.05);
        c.quadraticCurveTo(ctl - 0.08, 1.7, fin - 0.09, 5.3);
        c.lineWidth = 0.05; c.strokeStyle = withA(lacaLuz, 0.45); c.stroke();
      }
      // Brillo de laca: una franja curva arriba a la izquierda.
      c.beginPath();
      c.moveTo(cx - 3.9, 3.4);
      c.quadraticCurveTo(cx - 3.6, 1.75, cx - 1.6, 1.3);
      c.quadraticCurveTo(cx - 3.1, 2.15, cx - 3.35, 3.55);
      c.closePath();
      c.fillStyle = withA(lacaLuz, 0.75);
      c.fill();
      // Koshimaki y mabizashi: la banda baja del cuenco, más oscura, con el
      // ribete de oro arriba. Es la visera, y lo que separa el casco de la cara.
      c.beginPath();
      c.moveTo(cx - 5.4, 4.05);
      c.quadraticCurveTo(cx, 4.55, cx + 5.4, 4.05);
      c.lineTo(cx + 5.4, 6); c.lineTo(cx - 5.4, 6); c.closePath();
      c.fillStyle = withA(adjustL(A.dark, -26), 0.85);
      c.fill();
      c.restore();
      c.beginPath();
      c.moveTo(cx - 5.0, 4.06);
      c.quadraticCurveTo(cx, 4.56, cx + 5.0, 4.06);
      c.lineWidth = 0.14; c.strokeStyle = oro; c.stroke();
      cuenco();
      c.lineWidth = 0.1; c.strokeStyle = withA(adjustL(A.dark, -35), 0.8); c.stroke();
      // Labio de la visera: oro, con una línea de luz encima.
      c.beginPath();
      c.moveTo(cx - 4.95, 4.95);
      c.quadraticCurveTo(cx, 5.4, cx + 4.95, 4.95);
      c.lineWidth = 0.2; c.strokeStyle = oroD; c.stroke();
      c.lineWidth = 0.08; c.strokeStyle = oroL; c.stroke();
      // Tehen: el aro de la coronilla.
      c.beginPath(); c.ellipse(cx, 1.0, 0.55, 0.2, 0, 0, Math.PI * 2);
      c.fillStyle = oro; c.fill();
      c.lineWidth = 0.06; c.strokeStyle = oroD; c.stroke();

      // Maedate: la media luna, ancha y fina, con la base sobre la frente.
      const punta = -1.25;
      glowing(c, st, 1.2, () => {
        c.beginPath();
        c.moveTo(cx - 5.05, punta);
        c.bezierCurveTo(cx - 4.25, 1.65, cx - 1.95, 2.5, cx, 2.5);
        c.bezierCurveTo(cx + 1.95, 2.5, cx + 4.25, 1.65, cx + 5.05, punta);
        c.bezierCurveTo(cx + 3.75, 0.95, cx + 1.7, 1.72, cx, 1.72);
        c.bezierCurveTo(cx - 1.7, 1.72, cx - 3.75, 0.95, cx - 5.05, punta);
        c.closePath();
        const kg = c.createLinearGradient(cx - 4, punta, cx + 4, 2.5);
        kg.addColorStop(0, oroL);
        kg.addColorStop(0.5, oro);
        kg.addColorStop(1, oroD);
        c.fillStyle = kg;
        c.fill();
      }, punta + 1.2);
      // Filo de luz por el canto de adentro, sólo en la mitad que mira a la luz.
      c.beginPath();
      c.moveTo(cx - 4.75, punta + 0.4);
      c.bezierCurveTo(cx - 3.55, 1.1, cx - 1.6, 1.82, cx + 0.4, 1.82);
      c.lineWidth = 0.09; c.lineCap = 'round';
      c.strokeStyle = withA('#ffffff', 0.7); c.stroke();
      c.beginPath();
      c.moveTo(cx - 5.05, punta);
      c.bezierCurveTo(cx - 4.25, 1.65, cx - 1.95, 2.5, cx, 2.5);
      c.bezierCurveTo(cx + 1.95, 2.5, cx + 4.25, 1.65, cx + 5.05, punta);
      c.lineWidth = 0.07; c.strokeStyle = withA(oroD, 0.9); c.stroke();

      // Haraidate: la pieza que sostiene la luna, con una gema roja.
      c.beginPath();
      c.moveTo(cx - 0.7, 2.1);
      c.lineTo(cx + 0.7, 2.1);
      c.lineTo(cx + 0.5, 3.35);
      c.quadraticCurveTo(cx, 3.6, cx - 0.5, 3.35);
      c.closePath();
      const bg = c.createLinearGradient(cx - 0.7, 2.1, cx + 0.7, 3.5);
      bg.addColorStop(0, oroL); bg.addColorStop(1, oroD);
      c.fillStyle = bg; c.fill();
      c.lineWidth = 0.06; c.strokeStyle = withA(adjustL(A.dark, -30), 0.7); c.stroke();
      ellipsePath(c, cx, 2.75, 0.3, 0.3);
      const rg = c.createRadialGradient(cx - 0.1, 2.65, 0.03, cx, 2.75, 0.3);
      rg.addColorStop(0, mixHex(rojo, '#ffffff', 0.45)); rg.addColorStop(1, adjustL(rojo, -18));
      c.fillStyle = rg; c.fill();
    },

    /**
     * Casco de astronauta.
     *
     * Boberto no tiene cuello, así que el casco no puede ser una burbuja
     * apoyada en la cabeza: tiene que ENVOLVER todo el torso superior, como
     * una pecera, y cerrar abajo con un collar. Con la esfera chica y baja le
     * cortaba la cabeza a la altura del mentón y le cruzaba la panza.
     *
     * La otra regla, aprendida a los golpes: nada de lo que pinta el casco
     * puede caer encima de los ojos. El visor dorado va sólo por arriba de
     * ellos y los reflejos, contra los bordes. Si el dorado o el reflejo les
     * pasan por encima, los ojos se lavan y el personaje pierde la mirada, que
     * es lo único que realmente lo hace un personaje.
     */
    astronauta(c, A, st) {
      const cx = HELMET.cx, cy = HELMET.cy, r = HELMET.r;

      // Sombra proyectada del casco sobre el cuerpo
      c.save();
      c.beginPath();
      c.ellipse(cx, cy + r - 0.2, 4.8, 1.2, 0, 0, Math.PI * 2);
      c.fillStyle = 'rgba(0,0,0,0.35)'; c.fill();
      c.restore();

      // Collar: la base sobre la que se apoya el casco (volumétrico 2.5D)
      c.beginPath();
      c.ellipse(cx, cy + r - 0.9, 4.6, 1.1, 0, 0, Math.PI * 2);
      const cg = c.createLinearGradient(0, cy + r - 2.0, 0, cy + r + 0.2);
      cg.addColorStop(0, mixHex(A.mat, '#ffffff', 0.6));
      cg.addColorStop(0.4, A.mat);
      cg.addColorStop(1, adjustL(A.mat, -35));
      c.fillStyle = cg; c.fill();
      
      // Hueco del collar
      c.beginPath();
      c.ellipse(cx, cy + r - 1.1, 4.2, 0.9, 0, 0, Math.PI * 2);
      c.fillStyle = adjustL(A.mat, -45); c.fill();

      // Casquete: un aro grueso, no un disco. El centro queda libre para la cara.
      c.save();
      c.beginPath();
      c.arc(cx, cy, r, 0, Math.PI * 2);
      c.arc(cx, cy, r - 0.92, 0, Math.PI * 2, true);
      c.closePath();
      
      const sg = c.createRadialGradient(cx - r * 0.4, cy - r * 0.4, r * 0.2, cx, cy, r * 1.1);
      sg.addColorStop(0.00, mixHex(A.mat, '#ffffff', 0.8));
      sg.addColorStop(0.35, A.mat);
      sg.addColorStop(1.00, adjustL(A.mat, -40));
      c.fillStyle = sg;
      c.fill('evenodd');
      
      // Sombra interior del marco
      c.beginPath();
      c.arc(cx, cy, r - 0.92, 0, Math.PI * 2);
      c.lineWidth = 0.3;
      c.strokeStyle = withA(adjustL(A.mat, -50), 0.6);
      c.stroke();
      c.restore();

      // Antena, naciendo del aro
      const ax = cx + r * Math.cos(-0.72), ay = cy + r * Math.sin(-0.72);
      c.beginPath();
      c.lineWidth = 0.4; c.lineCap = 'round';
      c.strokeStyle = adjustL(A.mat, -20);
      c.moveTo(ax, ay);
      c.quadraticCurveTo(ax + 1.4, ay - 1.1, ax + 0.5, ay - 1.7);
      c.stroke();
      
      // Highlight antena
      c.beginPath();
      c.lineWidth = 0.15;
      c.strokeStyle = mixHex(A.mat, '#ffffff', 0.6);
      c.moveTo(ax, ay);
      c.quadraticCurveTo(ax + 1.4, ay - 1.1, ax + 0.5, ay - 1.7);
      c.stroke();

      glowing(c, st, 2.0, () => {
        c.beginPath();
        c.arc(ax + 0.5, ay - 1.95, 0.45, 0, Math.PI * 2);
        const lg = c.createRadialGradient(ax + 0.3, ay - 2.1, 0.05, ax + 0.5, ay - 1.95, 0.45);
        lg.addColorStop(0, '#ffffff');
        lg.addColorStop(0.3, A.alt(A.bulb, 1));
        lg.addColorStop(1, adjustL(A.alt(A.bulb, 1), -20));
        c.fillStyle = lg; c.fill();
      }, ay - 1.95 - 0.45);
    },

    /**
     * Escafandra de buzo.
     *
     * Misma geometría que el casco de astronauta, pero al revés: el bronce es
     * OPACO y la cara se ve por la portilla. Por eso el disco de la portilla se
     * pinta acá, antes de los ojos: los ojos caen encima y quedan detrás del
     * vidrio sin que haya que dibujarlos dos veces.
     *
     * Antes el casquete se trazaba con arc(…, PI*0.06, PI*0.94), que es la
     * mitad de ABAJO: la escafandra terminaba siendo una faja de bronce
     * cruzándole la panza.
     */
    buzo(c, A, st) {
      const cx = HELMET.cx, cy = HELMET.cy, r = HELMET.r;
      const bronce = A.alt('#b9863c');

      // Gorguera.
      roundRect(c, cx - 4.6, cy + r - 1.6, 9.2, 1.7, 0.75);
      c.fillStyle = adjustL(bronce, -24); c.fill();

      // Casquete opaco.
      ellipsePath(c, cx, cy, r, r);
      const g = c.createRadialGradient(cx - r * 0.42, cy - r * 0.48, r * 0.08, cx, cy, r * 1.05);
      g.addColorStop(0.00, mixHex(bronce, '#ffffff', 0.52));
      g.addColorStop(0.50, bronce);
      g.addColorStop(1.00, adjustL(bronce, -30));
      c.fillStyle = g; c.fill();

      // Válvula de aire.
      roundRect(c, cx - 0.72, cy - r - 0.72, 1.44, 1.0, 0.32);
      c.fillStyle = adjustL(bronce, -14); c.fill();
      glowing(c, st, 1.4, () => {
        ellipsePath(c, cx, cy - r - 0.76, 0.28, 0.28);
        c.fillStyle = A.bulb; c.fill();
      }, cy - r - 0.76 - 0.28);

      // Portillas laterales, contra el borde y no flotando en el medio.
      [-1, 1].forEach(sd => {
        const px = cx + sd * (r - 1.15), py = cy + 0.9;
        ellipsePath(c, px, py, 0.80, 0.90);
        c.fillStyle = adjustL(bronce, -26); c.fill();
        ellipsePath(c, px, py, 0.48, 0.56);
        c.fillStyle = withA('#0d1a24', 0.92); c.fill();
      });

      // Vidrio de la portilla: va ACÁ para que los ojos se dibujen encima.
      ellipsePath(c, cx, HELMET.faceY, HELMET.faceR, HELMET.faceR);
      const vg = c.createRadialGradient(cx - 1.4, HELMET.faceY - 1.6, 0.3, cx, HELMET.faceY, HELMET.faceR);
      vg.addColorStop(0, '#16323f');
      vg.addColorStop(1, '#0a1a24');
      c.fillStyle = vg; c.fill();
    },

    /** Cuernos de demonio. La cola va en la capa de atrás. */
    demonio(c, A) {
      const y = HEAD.top + 0.9;
      const cuerno = A.alt('#c8452f');
      
      // Sombra base en la cabeza
      [-1, 1].forEach(sd => {
        c.save();
        c.beginPath();
        c.ellipse(HEAD.cx + sd * 1.9, y + 0.7, 1.4, 0.4, 0, 0, Math.PI * 2);
        c.fillStyle = 'rgba(0,0,0,0.3)';
        c.fill();
        c.restore();
      });

      [-1, 1].forEach(sd => {
        c.save();
        c.translate(HEAD.cx + sd * 1.9, y + 0.5);
        c.scale(sd, 1);
        
        // Cuerno volumétrico
        c.beginPath();
        c.moveTo(-0.85, 0.35);
        c.bezierCurveTo(-0.75, -1.6, 0.15, -2.6, 1.45, -3.15);
        c.bezierCurveTo(0.55, -2.1, 0.45, -1.0, 0.60, 0.20);
        c.closePath();
        
        const g = c.createRadialGradient(-0.2, 0.2, 0.2, 0.5, -1.5, 3.5);
        g.addColorStop(0, mixHex(cuerno, '#ffffff', 0.2));
        g.addColorStop(0.5, cuerno);
        g.addColorStop(1, adjustL(cuerno, -35));
        c.fillStyle = g; c.fill();
        
        // Relieve anillado (textura del cuerno)
        c.beginPath();
        c.lineWidth = 0.2;
        c.strokeStyle = withA(adjustL(cuerno, -40), 0.4);
        for(let i=1; i<4; i++) {
          const t = i/4;
          const yPos = 0.2 - t * 2.5;
          const xPos = -0.4 + t * 1.2;
          c.moveTo(xPos - 0.4, yPos);
          c.quadraticCurveTo(xPos + 0.2, yPos + 0.3, xPos + 0.5, yPos - 0.2);
        }
        c.stroke();
        c.restore();
      });
    },

    /**
     * Lo de adelante de la capa: el lazo que la ata.
     *
     * Boberto no tiene cuello, así que el cordón no puede ir donde va en una
     * persona. Antes cruzaba la frente y se leía como una vincha. Ahora nace
     * en los dos broches de los costados —donde la capa se engancha— y baja
     * a atarse debajo de la boca, con el medallón en el nudo.
     */
    capa(c, A, st) {
      const cx = BODY.cx;
      const yNudo = 12.25;
      const oro = A.alt('#f0b429');
      const cordon = adjustL(A.mat, -14);
      [-1, 1].forEach(sd => {
        const bx = cx + sd * 5.55, by = 9.0;
        c.beginPath();
        c.moveTo(bx, by);
        c.quadraticCurveTo(cx + sd * 3.6, yNudo + 0.2, cx + sd * 0.5, yNudo);
        c.lineWidth = 0.36; c.lineCap = 'round'; c.strokeStyle = cordon; c.stroke();
        c.lineWidth = 0.1; c.strokeStyle = withA(mixHex(A.mat, '#ffffff', 0.45), 0.7); c.stroke();
        // Broche del costado.
        ellipsePath(c, bx, by, 0.5, 0.5);
        const bg = c.createRadialGradient(bx - 0.18, by - 0.2, 0.05, bx, by, 0.55);
        bg.addColorStop(0, mixHex(oro, '#ffffff', 0.55)); bg.addColorStop(1, adjustL(oro, -22));
        c.fillStyle = bg; c.fill();
      });
      // Las dos puntas del lazo que cuelgan del nudo.
      [-1, 1].forEach(sd => {
        c.beginPath();
        c.moveTo(cx + sd * 0.25, yNudo + 0.3);
        c.quadraticCurveTo(cx + sd * 0.7, yNudo + 1.1, cx + sd * 0.5, yNudo + 1.75);
        c.lineWidth = 0.3; c.lineCap = 'round'; c.strokeStyle = cordon; c.stroke();
      });
      // Medallón.
      glowing(c, st, 2.0, () => {
        ellipsePath(c, cx, yNudo, 0.82, 0.82);
        const g = c.createRadialGradient(cx - 0.3, yNudo - 0.32, 0.05, cx, yNudo, 0.9);
        g.addColorStop(0, mixHex(oro, '#ffffff', 0.6));
        g.addColorStop(1, adjustL(oro, -20));
        c.fillStyle = g; c.fill();
      });
      ellipsePath(c, cx, yNudo, 0.82, 0.82);
      c.lineWidth = 0.08; c.strokeStyle = adjustL(oro, -35); c.stroke();
      // Grabado del medallón: una estrella de cuatro puntas.
      c.beginPath();
      c.moveTo(cx, yNudo - 0.55);
      c.quadraticCurveTo(cx, yNudo, cx + 0.55, yNudo); c.quadraticCurveTo(cx, yNudo, cx, yNudo + 0.55);
      c.quadraticCurveTo(cx, yNudo, cx - 0.55, yNudo); c.quadraticCurveTo(cx, yNudo, cx, yNudo - 0.55);
      c.fillStyle = adjustL(oro, -32); c.fill();
    },

    /**
     * El arnés de la mochila cohete. Sin él, los tanques de atrás parecen
     * flotar al lado del cuerpo. Las correas bajan por los costados, por
     * fuera de los ojos, y se cierran con una hebilla debajo de la boca.
     */
    jetpack(c, A) {
      const cx = BODY.cx;
      const cinta = A.fixed('#2b2f38', 0.4);
      const borde = adjustL(cinta, 22);
      const hebilla = A.alt('#c9d3e0');
      const yCinto = 11.85;
      c.save();
      blobPath(c, cx, BODY.top, BODY.bot, BODY.rx);
      c.clip();
      [-1, 1].forEach(sd => {
        c.beginPath();
        c.moveTo(cx + sd * 4.1, 3.6);
        c.quadraticCurveTo(cx + sd * 5.7, 7.5, cx + sd * 4.6, yCinto);
        c.lineWidth = 0.75; c.lineCap = 'butt'; c.strokeStyle = cinta; c.stroke();
        c.lineWidth = 0.06; c.strokeStyle = withA(borde, 0.9); c.stroke();
      });
      c.beginPath();
      c.moveTo(cx - 6.5, yCinto - 0.6);
      c.quadraticCurveTo(cx, yCinto + 0.6, cx + 6.5, yCinto - 0.6);
      c.lineWidth = 0.7; c.strokeStyle = cinta; c.stroke();
      c.lineWidth = 0.06; c.strokeStyle = withA(borde, 0.9); c.stroke();
      c.restore();
      // Hebilla de suelta rápida.
      roundRect(c, cx - 0.85, yCinto - 0.55, 1.7, 1.1, 0.3);
      const g = c.createLinearGradient(0, yCinto - 0.55, 0, yCinto + 0.55);
      g.addColorStop(0, mixHex(hebilla, '#ffffff', 0.5)); g.addColorStop(1, adjustL(hebilla, -30));
      c.fillStyle = g; c.fill();
      c.lineWidth = 0.07; c.strokeStyle = adjustL(hebilla, -45); c.stroke();
      ellipsePath(c, cx, yCinto, 0.28, 0.28);
      c.fillStyle = A.fixed('#e0483c', 0.5); c.fill();
    },

    /** Vincha del arquero. El arco y el carcaj van a la espalda. */
    arquero(c, A) {
      const y = HEAD.top + 1.35;
      
      // Vincha (2.5D)
      c.beginPath();
      c.lineWidth = 0.8; c.lineCap = 'round';
      c.strokeStyle = adjustL(A.mat, -15);
      c.moveTo(BODY.cx - 4.1, y + 0.3);
      c.quadraticCurveTo(BODY.cx, y - 1.5, BODY.cx + 4.1, y + 0.3);
      c.stroke();
      
      // Highlight vincha
      c.beginPath();
      c.lineWidth = 0.25; c.lineCap = 'round';
      c.strokeStyle = mixHex(A.mat, '#ffffff', 0.4);
      c.moveTo(BODY.cx - 4.1, y + 0.1);
      c.quadraticCurveTo(BODY.cx, y - 1.7, BODY.cx + 4.1, y + 0.1);
      c.stroke();
      // Nudo lateral.
      ellipsePath(c, BODY.cx - 3.9, y + 0.25, 0.52, 0.44);
      c.fillStyle = adjustL(A.mat, -16); c.fill();
      // Pluma clavada en la vincha.
      const pl = A.alt('#4fa96b');
      c.save();
      c.translate(BODY.cx + 2.9, y - 0.55);
      c.rotate(-0.42);
      c.beginPath();
      c.moveTo(0, 0.4);
      c.quadraticCurveTo(-0.68, -1.6, 0.05, -3.3);
      c.quadraticCurveTo(0.72, -1.6, 0, 0.4);
      c.closePath();
      const g = c.createLinearGradient(-0.7, 0, 0.7, 0);
      g.addColorStop(0, adjustL(pl, -18));
      g.addColorStop(1, mixHex(pl, '#ffffff', 0.35));
      c.fillStyle = g; c.fill();
      c.beginPath();
      c.lineWidth = 0.11;
      c.strokeStyle = withA(adjustL(pl, -34), 0.9);
      c.moveTo(0.02, 0.35); c.lineTo(0.05, -3.1);
      c.stroke();
      c.restore();
    },

    /**
     * Bufanda tejida.
     *
     * Va sobre la panza y no bajo la cabeza: Boberto no tiene cuello, y a la
     * altura de los ojos se los tapa.
     *
     * El punto es HORIZONTAL. La primera versión lo tejía con rayas verticales
     * finitas y a este tamaño la bufanda se leía como una dentadura.
     *
     * La punta CUELGA del nudo, por delante. Antes salía por detrás y subía
     * hasta la oreja, como una bandera, y el nudo era una pelota lisa; ahora
     * el nudo tiene la vuelta de la tela y la punta cae con su fleco.
     */
    bufanda(c, A) {
      const y = BODY.top + 8.5;
      const lana = A.mat, franja = A.alt(adjustL(A.mat, 26));
      const hondo = adjustL(lana, -34);
      // Punto: hileras de "v" cortas, como se ve un tejido de cerca.
      const puntos = (x0, x1, y0, filas, curva) => {
        c.lineWidth = 0.09;
        c.strokeStyle = withA(hondo, 0.45);
        for (let f = 0; f < filas; f++) {
          for (let x = x0; x <= x1; x += 0.5) {
            const yb = y0 + f * 0.62 + (curva ? curva(x) : 0);
            c.beginPath();
            c.moveTo(x - 0.2, yb); c.lineTo(x, yb + 0.28); c.lineTo(x + 0.2, yb);
            c.stroke();
          }
        }
      };

      // Banda: gruesa y curvada siguiendo la panza.
      c.save();
      c.beginPath();
      c.moveTo(BODY.cx - 5.5, y - 1.35);
      c.quadraticCurveTo(BODY.cx, y + 0.35, BODY.cx + 5.5, y - 1.35);
      c.lineTo(BODY.cx + 5.5, y + 0.95);
      c.quadraticCurveTo(BODY.cx, y + 2.65, BODY.cx - 5.5, y + 0.95);
      c.closePath();
      const g = c.createLinearGradient(0, y - 1.4, 0, y + 2.6);
      g.addColorStop(0, adjustL(lana, 12));
      g.addColorStop(1, adjustL(lana, -18));
      c.fillStyle = g; c.fill();
      c.clip();
      c.fillStyle = withA(franja, 0.85);
      c.beginPath();
      c.moveTo(BODY.cx - 6, y - 0.55);
      c.quadraticCurveTo(BODY.cx, y + 1.15, BODY.cx + 6, y - 0.55);
      c.lineTo(BODY.cx + 6, y + 0.25);
      c.quadraticCurveTo(BODY.cx, y + 1.95, BODY.cx - 6, y + 0.25);
      c.closePath();
      c.fill();
      puntos(BODY.cx - 5.6, BODY.cx + 5.6, y - 1.15, 4, (x) => 0.85 * (1 - ((x - BODY.cx) / 5.5) ** 2));
      c.restore();

      // La punta que cuelga: arranca en el nudo y cae hasta abajo de la panza.
      const kx = BODY.cx - 3.9, ky = y + 0.55;
      const punta = () => {
        c.beginPath();
        c.moveTo(kx - 0.9, ky + 0.2);
        c.quadraticCurveTo(kx - 1.35, ky + 2.6, kx - 0.85, ky + 5.1);
        c.lineTo(kx + 1.0, ky + 4.85);
        c.quadraticCurveTo(kx + 0.5, ky + 2.5, kx + 0.8, ky + 0.3);
        c.closePath();
      };
      punta();
      const pg = c.createLinearGradient(kx - 1, 0, kx + 1, 0);
      pg.addColorStop(0, adjustL(lana, -14)); pg.addColorStop(0.5, lana); pg.addColorStop(1, adjustL(lana, -22));
      c.fillStyle = pg; c.fill();
      c.save();
      punta(); c.clip();
      c.fillStyle = withA(franja, 0.85);
      [1.6, 3.3].forEach(dy => {
        c.beginPath();
        c.moveTo(kx - 2, ky + dy); c.lineTo(kx + 2, ky + dy - 0.2);
        c.lineTo(kx + 2, ky + dy + 0.55); c.lineTo(kx - 2, ky + dy + 0.75);
        c.closePath(); c.fill();
      });
      puntos(kx - 1.3, kx + 1.3, ky + 0.5, 7, null);
      c.restore();
      // Fleco.
      c.lineWidth = 0.17; c.lineCap = 'round';
      c.strokeStyle = adjustL(lana, -8);
      for (let i = 0; i < 5; i++) {
        const t = i / 4;
        const x0 = kx - 0.8 + t * 1.75, y0 = ky + 5.08 - t * 0.25;
        c.beginPath();
        c.moveTo(x0, y0);
        c.lineTo(x0 - 0.1 + (i % 2) * 0.12, y0 + 0.75 + (i % 2) * 0.15);
        c.stroke();
      }

      // Nudo: la vuelta de la tela, con su pliegue.
      c.beginPath();
      c.moveTo(kx - 1.15, ky - 0.75);
      c.quadraticCurveTo(kx, ky - 1.25, kx + 1.15, ky - 0.6);
      c.quadraticCurveTo(kx + 1.35, ky + 0.35, kx + 0.85, ky + 0.85);
      c.quadraticCurveTo(kx, ky + 1.2, kx - 0.95, ky + 0.8);
      c.quadraticCurveTo(kx - 1.45, ky + 0.0, kx - 1.15, ky - 0.75);
      c.closePath();
      const kg = c.createRadialGradient(kx - 0.4, ky - 0.5, 0.1, kx, ky, 1.5);
      kg.addColorStop(0, adjustL(lana, 16));
      kg.addColorStop(1, adjustL(lana, -24));
      c.fillStyle = kg; c.fill();
      c.lineWidth = 0.1;
      c.strokeStyle = withA(hondo, 0.7);
      // Un solo pliegue en diagonal: con dos curvas paralelas el nudo
      // parecía una carita.
      c.beginPath();
      c.moveTo(kx - 0.95, ky - 0.55);
      c.quadraticCurveTo(kx - 0.1, ky - 0.1, kx + 0.55, ky + 0.85);
      c.stroke();
    },

    /**
     * Casco de gladiador.
     *
     * Era un cuenquito con un nasal y una cresta de dos unidades: se leía
     * como una campana. Ahora tiene la silueta del casco romano: un cuenco
     * pulido que cubre la cabeza, un ala que se abre arriba de los ojos, las
     * carrilleras colgando POR FUERA de la cara y la cresta de crin alta, que
     * es lo primero que se ve de lejos.
     */
    gladiador(c, A) {
      const cx = HEAD.cx;
      const metal = A.mat;
      const metalL = mixHex(A.lite, '#ffffff', 0.45);
      const metalD = adjustL(A.dark, -22);
      const crin = A.alt('#c0392b');

      // Cresta de crin: un abanico de cerdas que sube desde el soporte.
      // Alto acotado: la punta queda en y -1.5 y el lienzo arranca en -2.
      const base = 1.15;
      for (let i = 0; i < 30; i++) {
        const t = i / 29;
        const a = -Math.PI / 2 + (t - 0.5) * 1.5;
        const largo = 2.6 - Math.abs(t - 0.5) * 1.4 + ((i * 7) % 5) * 0.05;
        const x1 = cx + Math.cos(a) * largo * 0.9, y1 = base + Math.sin(a) * largo;
        c.beginPath();
        c.moveTo(cx + (t - 0.5) * 0.9, base + 0.2);
        c.quadraticCurveTo(cx + Math.cos(a) * largo * 0.3, base + Math.sin(a) * largo * 0.6, x1, y1);
        c.lineWidth = 0.32; c.lineCap = 'round';
        c.strokeStyle = i % 3 === 0 ? adjustL(crin, -22) : i % 3 === 1 ? crin : mixHex(crin, '#ffffff', 0.18);
        c.stroke();
      }
      // Soporte de la cresta.
      roundRect(c, cx - 0.7, base - 0.1, 1.4, 0.9, 0.2);
      c.fillStyle = metalD; c.fill();

      // Carrilleras: cuelgan de los costados del cuenco, por fuera de los ojos.
      [-1, 1].forEach(sd => {
        c.beginPath();
        c.moveTo(cx + sd * 4.3, 4.2);
        c.quadraticCurveTo(cx + sd * 5.95, 4.6, cx + sd * 5.75, 6.6);
        c.quadraticCurveTo(cx + sd * 5.5, 8.4, cx + sd * 4.75, 8.6);
        c.quadraticCurveTo(cx + sd * 4.55, 6.4, cx + sd * 4.3, 4.2);
        c.closePath();
        const cg = c.createLinearGradient(cx + sd * 4.3, 4.2, cx + sd * 5.9, 8.4);
        cg.addColorStop(0, metalL); cg.addColorStop(0.5, metal); cg.addColorStop(1, metalD);
        c.fillStyle = cg; c.fill();
        c.lineWidth = 0.08; c.strokeStyle = withA(metalD, 0.9); c.stroke();
        // Remaches.
        [5.6, 7.3].forEach(yy => {
          ellipsePath(c, cx + sd * 5.15, yy, 0.13, 0.13);
          c.fillStyle = metalL; c.fill();
        });
      });

      // Cuenco pulido.
      const cuenco = () => {
        c.beginPath();
        c.moveTo(cx - 4.6, 4.75);
        c.bezierCurveTo(cx - 4.8, 2.2, cx - 2.7, 1.1, cx, 1.1);
        c.bezierCurveTo(cx + 2.7, 1.1, cx + 4.8, 2.2, cx + 4.6, 4.75);
        c.quadraticCurveTo(cx, 5.15, cx - 4.6, 4.75);
        c.closePath();
      };
      cuenco();
      const g = c.createRadialGradient(cx - 1.5, 2.0, 0.2, cx + 0.5, 3.4, 5.0);
      g.addColorStop(0, metalL);
      g.addColorStop(0.45, metal);
      g.addColorStop(1, metalD);
      c.fillStyle = g; c.fill();
      c.save();
      cuenco(); c.clip();
      // Reflejo duro del metal pulido.
      c.beginPath();
      c.moveTo(cx - 3.8, 3.8);
      c.quadraticCurveTo(cx - 3.4, 1.9, cx - 1.4, 1.5);
      c.quadraticCurveTo(cx - 2.9, 2.4, cx - 3.25, 3.9);
      c.closePath();
      c.fillStyle = withA('#ffffff', 0.6); c.fill();
      // Nervio central, de adelante hacia atrás.
      c.beginPath(); c.moveTo(cx, 1.1); c.lineTo(cx, 5.2);
      c.lineWidth = 0.32; c.strokeStyle = withA(metalD, 0.5); c.stroke();
      c.lineWidth = 0.1; c.strokeStyle = withA(metalL, 0.8); c.stroke();
      c.restore();
      // Ala: se abre hacia afuera, arriba de los ojos.
      c.beginPath();
      c.moveTo(cx - 5.3, 4.35);
      c.quadraticCurveTo(cx, 5.0, cx + 5.3, 4.35);
      c.quadraticCurveTo(cx + 5.5, 4.8, cx + 5.0, 4.95);
      c.quadraticCurveTo(cx, 5.55, cx - 5.0, 4.95);
      c.quadraticCurveTo(cx - 5.5, 4.8, cx - 5.3, 4.35);
      c.closePath();
      const ag = c.createLinearGradient(0, 4.3, 0, 5.4);
      ag.addColorStop(0, metalL); ag.addColorStop(1, metalD);
      c.fillStyle = ag; c.fill();
      // Remaches del ala.
      for (let i = -3; i <= 3; i++) {
        const dx = i * 1.3;
        ellipsePath(c, cx + dx, 4.85 + 0.4 * (1 - (dx / 5) ** 2) - 0.1, 0.1, 0.1);
        c.fillStyle = metalD; c.fill();
      }
    },

    /** Gorro con hélice. Gajos de colores y tres palas. */
    helice(c, A, st) {
      const y = HEAD.top + 0.85;
      const gajo = A.alt('#e0483c');
      const cx = BODY.cx;

      // Sombra proyectada en la cabeza
      c.save();
      c.beginPath();
      c.ellipse(cx, y + 0.5, 3.6, 0.7, 0, 0, Math.PI * 2);
      c.fillStyle = 'rgba(0,0,0,0.3)';
      c.fill();
      c.restore();

      // Gorro por gajos (Volumétrico)
      c.save();
      c.beginPath();
      c.arc(cx, y + 0.6, 3.5, Math.PI, 0);
      c.clip();

      for (let i = 0; i < 4; i++) {
        const a0 = Math.PI + (i / 4) * Math.PI, a1 = Math.PI + ((i + 1) / 4) * Math.PI;
        c.beginPath();
        c.moveTo(cx, y + 0.6);
        c.arc(cx, y + 0.6, 4.0, a0, a1);
        c.closePath();
        
        const col = i % 2 ? gajo : A.mat;
        // Gradiente radial descentrado para darle volumen esférico al gajo
        const gg = c.createRadialGradient(cx - 1.0, y - 1.5, 0.5, cx, y + 0.6, 4.5);
        gg.addColorStop(0, mixHex(col, '#ffffff', 0.4));
        gg.addColorStop(0.4, col);
        gg.addColorStop(1, adjustL(col, -30));
        
        c.fillStyle = gg;
        c.fill();
      }
      c.restore();

      // Ribete 2.5D
      c.beginPath();
      c.roundRect(cx - 3.6, y + 0.35, 7.2, 0.8, 0.4);
      const bg = c.createLinearGradient(cx - 3.6, y + 0.35, cx - 3.6, y + 1.15);
      bg.addColorStop(0, mixHex(A.mat, '#ffffff', 0.3));
      bg.addColorStop(0.5, adjustL(A.mat, -15));
      bg.addColorStop(1, adjustL(A.mat, -40));
      c.fillStyle = bg; c.fill();

      // Eje metálico
      c.beginPath();
      c.roundRect(cx - 0.25, y - 3.15, 0.5, 1.4, 0.25);
      const eg = c.createLinearGradient(cx - 0.25, 0, cx + 0.25, 0);
      eg.addColorStop(0, '#8892a0');
      eg.addColorStop(0.5, '#ffffff');
      eg.addColorStop(1, '#56606d');
      c.fillStyle = eg; c.fill();

      // Hélice: tres palas con paso y gradientes de luz
      const palas = [[-2.35, 0.45], [0.4, -0.85], [2.15, 0.62]];
      palas.forEach((pl, i) => {
        c.beginPath();
        c.moveTo(cx, y - 3.0);
        c.quadraticCurveTo(cx + pl[0] * 0.65, y - 3.0 - pl[1] * 0.9, cx + pl[0], y - 3.0 - pl[1]);
        c.quadraticCurveTo(cx + pl[0] * 0.6, y - 2.6 - pl[1] * 0.5, cx, y - 2.6);
        c.closePath();
        
        const isBack = i === 1;
        const pcol = isBack ? adjustL(gajo, -25) : gajo;
        const pg = c.createLinearGradient(cx, y - 3.0, cx + pl[0], y - 3.0 - pl[1]);
        pg.addColorStop(0, adjustL(pcol, -15));
        pg.addColorStop(0.8, mixHex(pcol, '#ffffff', isBack ? 0 : 0.3));
        pg.addColorStop(1, pcol);
        
        c.fillStyle = pg;
        c.fill();
        
        // Borde fino
        c.lineWidth = 0.15;
        c.strokeStyle = adjustL(pcol, -30);
        c.stroke();
      });

      // Tuerca superior brillante
      glowing(c, st, 2.0, () => {
        c.beginPath();
        c.ellipse(cx, y - 3.07, 0.45, 0.45, 0, 0, Math.PI * 2);
        const tg = c.createRadialGradient(cx - 0.1, y - 3.15, 0.05, cx, y - 3.07, 0.45);
        tg.addColorStop(0, '#ffffff');
        tg.addColorStop(0.3, A.bulb);
        tg.addColorStop(1, adjustL(A.bulb, -20));
        c.fillStyle = tg; c.fill();
      }, y - 3.07 - 0.45);
    },
    /**
     * Yukata: kimono masculino de verano.
     *
     * Cinco decisiones, y las cinco salieron de mirar el render:
     *
     * 1. RECORTADO contra la silueta. Se hace un clip con el mismo blobPath()
     *    que dibuja el cuerpo, asi que ninguna solapa se sale por el borde ni
     *    hay que adivinar el ancho a cada altura. Es lo unico que garantiza que
     *    siga calzando si algun dia el cuerpo cambia de forma.
     * 2. Cruza IZQUIERDA SOBRE DERECHA vista de frente. No es un detalle
     *    decorativo: al reves (derecha sobre izquierda) es como se viste a un
     *    difunto en Japon. Ya que el accesorio se llama yukata, que este bien.
     * 3. Empieza DEBAJO de los ojos (y = 7.4). Boberto no tiene cuello ni
     *    hombros; subiendo mas, el cuello del kimono le cruza la cara.
     * 4. El borde de arriba de la tela arranca a la MISMA altura que las puntas
     *    de la V y no mas abajo. Al principio bajaba 1.6 unidades y el corte
     *    horizontal a la altura de las mejillas se leia como un babero.
     * 5. El obi es GRUESO y va a media panza, con falda visible debajo. La
     *    primera version lo dejo fino y pegado al borde inferior: se perdia
     *    contra la tela y el traje entero parecia una sola mancha.
     *
     * No lleva mangas: en un cuerpo sin brazos visibles, una manga suelta a los
     * costados queda como una oreja mas.
     */
    yukata(c, A, st) {
      const tela   = A.mat;
      const franja = A.alt(adjustL(A.mat, 34));
      const cx = BODY.cx;
      const yCuello = BODY.top + 4.10;    // debajo de la linea de los ojos
      const yV      = BODY.top + 7.05;    // donde se cruzan las solapas
      const yObi    = BODY.top + 7.15;    // la faja, a media panza
      const yBajo   = BODY.bot + 0.60;    // se pasa: el clip lo corta

      c.save();
      // Todo el traje vive adentro de la silueta.
      blobPath(c, cx, BODY.top, BODY.bot, BODY.rx);
      c.clip();

      // -- Tela: de los "hombros" para abajo, con la V recortada en el medio.
      //    Una sola pieza, para que el degradado sea continuo.
      c.beginPath();
      c.moveTo(cx - BODY.rx - 1, yCuello);
      c.lineTo(cx - BODY.rx - 1, yBajo);
      c.lineTo(cx + BODY.rx + 1, yBajo);
      c.lineTo(cx + BODY.rx + 1, yCuello);
      c.lineTo(cx + 3.15, yCuello);
      c.quadraticCurveTo(cx + 1.35, yV - 1.25, cx, yV);
      c.quadraticCurveTo(cx - 1.35, yV - 1.25, cx - 3.15, yCuello);
      c.closePath();
      const g = c.createLinearGradient(cx - BODY.rx, yCuello, cx + BODY.rx, yBajo);
      g.addColorStop(0, adjustL(tela, 16));
      g.addColorStop(0.55, tela);
      g.addColorStop(1, adjustL(tela, -28));
      c.fillStyle = g;
      c.fill();
      // Kasuri: el estampado de cruces chicas del algodón de verano. Liso,
      // el traje se leía como un overol.
      c.save();
      c.clip();
      c.lineWidth = 0.09; c.lineCap = 'round';
      c.strokeStyle = withA(franja, 0.5);
      for (let fila = 0; fila < 7; fila++) {
        for (let x = cx - BODY.rx + (fila % 2) * 0.7; x <= cx + BODY.rx; x += 1.4) {
          const yy = yCuello + 0.5 + fila * 1.05;
          c.beginPath();
          c.moveTo(x - 0.22, yy); c.lineTo(x + 0.22, yy);
          c.moveTo(x, yy - 0.22); c.lineTo(x, yy + 0.22);
          c.stroke();
        }
      }
      c.restore();

      // -- Solapa de encima (la izquierda del personaje, o sea la derecha de
      //    quien mira). Ver la decision 2. Va un tono mas clara para que el
      //    cruce se vea sin dibujar una linea.
      c.beginPath();
      c.moveTo(cx + 3.15, yCuello);
      c.quadraticCurveTo(cx + 1.35, yV - 1.25, cx, yV);
      c.lineTo(cx + 1.30, yV + 0.30);
      c.quadraticCurveTo(cx + 2.75, yV - 1.35, cx + 4.35, yCuello);
      c.closePath();
      c.fillStyle = adjustL(tela, 12);
      c.fill();

      // -- Ribete del cuello: las dos franjas claras que dibujan la V. Es lo
      //    que hace que se lea "kimono" y no "pechera".
      c.lineWidth = 0.60;
      c.lineJoin = 'round';
      c.lineCap = 'round';
      c.strokeStyle = withA(franja, 0.95);
      c.beginPath();
      c.moveTo(cx - 3.15, yCuello);
      c.quadraticCurveTo(cx - 1.35, yV - 1.25, cx, yV);
      c.stroke();
      c.beginPath();
      c.moveTo(cx + 3.85, yCuello);
      c.quadraticCurveTo(cx + 2.15, yV - 1.30, cx + 0.75, yV + 0.15);
      c.stroke();

      // -- Obi: banda gruesa y curvada, siguiendo la panza. Recta se leeria
      //    como una etiqueta pegada encima.
      c.beginPath();
      c.moveTo(cx - BODY.rx - 1, yObi + 0.15);
      c.quadraticCurveTo(cx, yObi + 1.45, cx + BODY.rx + 1, yObi + 0.15);
      c.lineTo(cx + BODY.rx + 1, yObi + 1.95);
      c.quadraticCurveTo(cx, yObi + 3.25, cx - BODY.rx - 1, yObi + 1.95);
      c.closePath();
      const og = c.createLinearGradient(0, yObi, 0, yObi + 3.3);
      og.addColorStop(0, adjustL(franja, 12));
      og.addColorStop(0.5, franja);
      og.addColorStop(1, adjustL(franja, -34));
      c.fillStyle = og;
      c.fill();
      // Cordon fino sobre el obi: el detalle que le da tres dimensiones.
      c.lineWidth = 0.17;
      c.strokeStyle = withA(adjustL(tela, -30), 0.6);
      c.beginPath();
      c.moveTo(cx - BODY.rx, yObi + 1.20);
      c.quadraticCurveTo(cx, yObi + 2.50, cx + BODY.rx, yObi + 1.20);
      c.stroke();

      // -- Nudo del obi, adentro del contorno y chico. Afuera y grande —como
      //    estaba— parecia una herramienta que el personaje sostenia.
      const nx = cx - BODY.rx * 0.58, ny = yObi + 1.40;
      glowing(c, st, 1.2, () => {
        roundRect(c, nx - 0.95, ny - 0.62, 1.90, 1.24, 0.32);
        // Mas OSCURO que el obi, no mas claro: la primera version lo dejo
        // mas claro y se perdia contra la faja.
        c.fillStyle = adjustL(franja, -26);
        c.fill();
      });
      c.lineWidth = 0.14;
      c.strokeStyle = withA(adjustL(franja, 22), 0.8);
      c.beginPath();
      c.moveTo(nx, ny - 0.50); c.lineTo(nx, ny + 0.50);
      c.stroke();

      // -- Caida de la falda: tres puntadas que se abren hacia abajo.
      c.lineWidth = 0.14;
      c.strokeStyle = withA(adjustL(tela, 28), 0.45);
      [-2.2, 0.3, 2.7].forEach((dx) => {
        c.beginPath();
        c.moveTo(cx + dx, yObi + 3.55);
        c.quadraticCurveTo(cx + dx * 1.3, yObi + 4.7, cx + dx * 1.55, yBajo);
        c.stroke();
      });

      c.restore();
    },

    /**
     * Bata: la de laboratorio, que es también la del profesor. Nació como el
     * vestuario de la guía de Buenas prácticas de DBMON, y queda en el
     * catálogo para todas las apps.
     *
     * Las decisiones, y por qué:
     *
     * 1. Blanca aunque el accesorio sea de otro color (ver telaBata). El color
     *    elegido va a los botones y el segundo color a la lapicera: son los
     *    dos detalles que se pueden personalizar sin que deje de ser una bata.
     * 2. Recortada contra la silueta, como la yukata, pero con la silueta DE
     *    LA POSE (bodyDY, bodyRX): la yukata usa la caja quieta y en un salto
     *    la tela se despega medio renglón del cuerpo.
     * 3. La V baja hasta DEBAJO de la boca (y = 12.55 contra 10.7). Más arriba
     *    la boca queda pintada sobre la tela y se lee como una mancha.
     * 4. Solapas con muesca y costura gris: a 2 px por unidad, en el topbar,
     *    lo único que distingue una bata de un babero blanco son las solapas.
     * 5. Mangas donde están los brazos (ver `_poseActual`): sin ellas los
     *    muñones del color del cuerpo asoman a los costados y parece un
     *    delantal, no una prenda con mangas.
     *
     * Sin anteojos, a propósito: los ojos ocupan casi toda la cara y quedan a
     * menos de una unidad uno del otro, así que dos marcos redondos se tocan y
     * se leen como antiparras.
     */
    // `o.baja` corre hacia abajo el arranque de la tela (hombros y cuello),
    // en unidades. El accesorio la usa en 0. Rick la pide más baja: con la cara
    // de Rick la bata a la altura de siempre le llegaba casi a los ojos y se
    // leía como un cuello alto, no como una bata abierta.
    bata(c, A, o) {
      const T = telaBata(A);
      const p = _poseActual || {};
      const baja = (o && o.baja) || 0;
      const dy = p.bodyDY || 0;
      const cx = BODY.cx, top = BODY.top + dy, bot = BODY.bot + dy;
      const rx = BODY.rx * (p.bodyRX || 1);
      const yHombro = top + 4.05 + baja;          // la tela arranca a los costados, bajo los ojos
      const yCuello = top + 4.75 + baja;          // donde empieza la V
      const yV      = top + 9.25 + baja * 0.35;   // el fondo de la V, debajo de la boca
      const aV = 3.35;              // media abertura de la V arriba

      c.save();
      blobPath(c, cx, top, bot, rx);
      c.clip();

      // -- Tela: una pieza con la V recortada, luz arriba a la izquierda como
      //    el cuerpo. Con otra dirección de luz la bata parece pegada encima.
      c.beginPath();
      c.moveTo(cx - rx - 1, yHombro);
      c.lineTo(cx - rx - 1, bot + 1);
      c.lineTo(cx + rx + 1, bot + 1);
      c.lineTo(cx + rx + 1, yHombro);
      c.quadraticCurveTo(cx + aV + 1.2, yHombro, cx + aV, yCuello);
      c.lineTo(cx, yV);
      c.lineTo(cx - aV, yCuello);
      c.quadraticCurveTo(cx - aV - 1.2, yHombro, cx - rx - 1, yHombro);
      c.closePath();
      const g = c.createLinearGradient(cx - rx, yHombro, cx + rx, bot);
      g.addColorStop(0, T.luz);
      g.addColorStop(0.45, T.tela);
      g.addColorStop(1, T.sombra);
      c.fillStyle = g;
      c.fill();

      // -- Solapas: una por lado, a lo largo de la V, con la muesca afuera.
      [-1, 1].forEach(sd => {
        c.beginPath();
        c.moveTo(cx + sd * aV, yCuello);
        c.lineTo(cx + sd * 0.12, yV - 0.05);
        c.lineTo(cx + sd * 1.85, yV - 1.05);
        c.lineTo(cx + sd * 3.30, yCuello + 2.15);
        c.lineTo(cx + sd * 4.55, yCuello + 0.95);
        c.closePath();
        c.fillStyle = mixHex(T.tela, T.sombra, sd < 0 ? 0.22 : 0.55);
        c.fill();
        c.lineWidth = 0.22;
        c.strokeStyle = T.costura;
        c.stroke();
      });

      // -- Cierre: la costura del frente y dos botones del color elegido.
      c.lineWidth = 0.20;
      c.strokeStyle = withA(T.costura, 0.8);
      c.beginPath();
      c.moveTo(cx, yV); c.lineTo(cx, bot + 1);
      c.stroke();
      // El segundo botón no baja más que el borde de la panza: con la V más
      // abajo quedaría cortado por la silueta.
      [0.85, 1.95].forEach(d => {
        ellipsePath(c, cx - 0.55, baja ? Math.min(yV + d, bot - 0.95) : yV + d, 0.27, 0.27);
        c.fillStyle = A.mat; c.fill();
        c.lineWidth = 0.10; c.strokeStyle = T.costura; c.stroke();
      });

      // -- Bolsillo del pecho con la lapicera asomando. Del lado izquierdo
      //    del personaje (derecha de quien mira), que es donde lo lleva una bata.
      const bx = cx + 1.45, by = yV + 0.05, bw = 1.95, bh = 1.30;
      const lap = A.alt('#2f6fe4');
      roundRect(c, bx + 1.00, by - 0.95, 0.40, 1.60, 0.16);
      c.fillStyle = lap; c.fill();
      roundRect(c, bx + 1.06, by - 1.05, 0.28, 0.38, 0.10);
      c.fillStyle = adjustL(lap, -22); c.fill();
      roundRect(c, bx, by, bw, bh, 0.22);
      c.fillStyle = mixHex(T.tela, T.sombra, 0.35); c.fill();
      c.lineWidth = 0.18; c.strokeStyle = T.costura; c.stroke();
      c.beginPath();
      c.moveTo(bx + 0.1, by + 0.30); c.lineTo(bx + bw - 0.1, by + 0.30);
      c.lineWidth = 0.14; c.stroke();

      // -- Oclusión abajo, como la del cuerpo: sin ella la bata es un recorte
      //    plano y el personaje pierde el volumen de la panza.
      const og = c.createLinearGradient(0, top + (bot - top) * 0.55, 0, bot);
      og.addColorStop(0, withA(T.costura, 0));
      og.addColorStop(1, withA(T.costura, 0.30));
      c.fillStyle = og;
      c.fillRect(cx - rx - 1, top, rx * 2 + 2, bot - top + 1);
      c.restore();

      // -- La costura de afuera, sólo donde hay tela. Es la que separa el
      //    blanco de un fondo claro.
      c.save();
      c.beginPath();
      c.rect(cx - rx - 1, yHombro - 0.1, rx * 2 + 2, bot - yHombro + 2);
      c.clip();
      blobPath(c, cx, top, bot, rx);
      c.lineWidth = 0.40;
      c.strokeStyle = T.costura;
      c.stroke();
      c.restore();

      // -- Mangas, donde drawArms dejó los brazos.
      const cyA = top + (bot - top) * 0.60;
      const manga = (sd, up) => mangaBata(c, T,
        cx + sd * BODY.rx * 0.92, up ? cyA - 2.7 : cyA,
        up ? (sd > 0 ? -0.8 : 0.8) : (sd > 0 ? -0.18 : 0.18), up ? 0 : 1);
      if (p.armL !== false) manga(-1, p.arm === 'L');
      if (p.armR !== false) manga(1, p.arm === 'R');
    },

    /**
     * Murciélago.
     *
     * Un murciélago chico posado en la coronilla, con las alas abiertas. El
     * de antes era un ala sola saliendo de una grieta en la cabeza: a tamaño
     * de sidebar se leía como un paraguas roto. Lo que hace que se lea como
     * bicho son la CARA (orejas, ojos y colmillos) y la simetría de las alas.
     */
    murcielago(c, A) {
      const cx = HEAD.cx + 0.2;
      const y = 2.15;
      const piel = A.mat;
      const membrana = adjustL(A.mat, 8);
      const hueso = adjustL(A.dark, -22);
      const ojo = A.fixed('#ffd34a');
      // Dibujado en su propia escala y agrandado desde las patas: así queda
      // apoyado donde está y se lee también en la sidebar.
      c.save();
      c.translate(cx, y + 1.35); c.scale(1.3, 1.3); c.translate(-cx, -(y + 1.35));

      // Patitas agarradas a la cabeza.
      [-1, 1].forEach(sd => {
        c.beginPath(); c.moveTo(cx + sd * 0.35, y + 0.8); c.lineTo(cx + sd * 0.45, y + 1.35);
        c.lineWidth = 0.14; c.lineCap = 'round'; c.strokeStyle = hueso; c.stroke();
      });
      // Alas: un brazo que sube al codo y tres dedos; la membrana festoneada
      // entre punta y punta.
      [-1, 1].forEach(sd => {
        c.save();
        c.translate(cx + sd * 0.6, y - 0.1);
        c.scale(sd, 1);
        const codo = [1.4, -1.25];
        const puntas = [[3.6, -1.55], [4.2, -0.2], [3.4, 0.95], [1.5, 1.05]];
        c.beginPath();
        c.moveTo(0, 0.2);
        c.lineTo(codo[0], codo[1]);
        c.lineTo(puntas[0][0], puntas[0][1]);
        for (let i = 1; i < puntas.length; i++) {
          const [x0, y0] = puntas[i - 1], [x1, y1] = puntas[i];
          c.quadraticCurveTo((x0 + x1) / 2 - 0.45, (y0 + y1) / 2 - 0.1, x1, y1);
        }
        c.quadraticCurveTo(0.7, 0.75, 0, 0.75);
        c.closePath();
        const g = c.createRadialGradient(codo[0], codo[1] + 0.6, 0.2, codo[0] + 1, codo[1] + 1, 3.6);
        g.addColorStop(0, mixHex(membrana, '#ffffff', 0.18));
        g.addColorStop(1, adjustL(membrana, -28));
        c.fillStyle = g; c.fill();
        c.lineWidth = 0.06; c.strokeStyle = withA(hueso, 0.8); c.stroke();
        c.beginPath();
        c.moveTo(0, 0.2); c.lineTo(codo[0], codo[1]);
        puntas.slice(0, 3).forEach(([px, py]) => { c.moveTo(codo[0], codo[1]); c.lineTo(px, py); });
        c.lineWidth = 0.13; c.lineJoin = 'round'; c.strokeStyle = hueso; c.stroke();
        // Garrita en el codo.
        c.beginPath(); c.moveTo(codo[0], codo[1]); c.lineTo(codo[0] - 0.15, codo[1] - 0.4);
        c.lineWidth = 0.09; c.strokeStyle = hueso; c.stroke();
        c.restore();
      });
      // Cuerpo y cabeza en una sola pieza peluda, con las orejas.
      c.beginPath();
      c.moveTo(cx - 0.75, y - 0.55);
      c.lineTo(cx - 0.62, y - 1.45);
      c.lineTo(cx - 0.25, y - 0.85);
      c.quadraticCurveTo(cx, y - 0.95, cx + 0.25, y - 0.85);
      c.lineTo(cx + 0.62, y - 1.45);
      c.lineTo(cx + 0.75, y - 0.55);
      c.bezierCurveTo(cx + 1.0, y + 0.1, cx + 0.75, y + 0.95, cx, y + 1.0);
      c.bezierCurveTo(cx - 0.75, y + 0.95, cx - 1.0, y + 0.1, cx - 0.75, y - 0.55);
      c.closePath();
      const bg = c.createRadialGradient(cx - 0.3, y - 0.5, 0.05, cx, y, 1.3);
      bg.addColorStop(0, mixHex(A.lite, '#ffffff', 0.2));
      bg.addColorStop(1, adjustL(piel, -20));
      c.fillStyle = bg; c.fill();
      // Interior de las orejas.
      [-1, 1].forEach(sd => {
        c.beginPath();
        c.moveTo(cx + sd * 0.66, y - 0.7); c.lineTo(cx + sd * 0.6, y - 1.18); c.lineTo(cx + sd * 0.38, y - 0.85);
        c.closePath();
        c.fillStyle = A.fixed('#d9737f', 0.4); c.fill();
      });
      // Pelusa del pecho.
      c.beginPath(); c.ellipse(cx, y + 0.45, 0.42, 0.4, 0, 0, Math.PI * 2);
      c.fillStyle = withA(mixHex(piel, '#ffffff', 0.25), 0.7); c.fill();
      // Ojos y colmillos.
      [-1, 1].forEach(sd => {
        ellipsePath(c, cx + sd * 0.3, y - 0.4, 0.2, 0.22);
        c.fillStyle = ojo; c.fill();
        ellipsePath(c, cx + sd * 0.3 + 0.02, y - 0.38, 0.09, 0.12);
        c.fillStyle = '#120a02'; c.fill();
        ellipsePath(c, cx + sd * 0.3 - 0.05, y - 0.47, 0.05, 0.05);
        c.fillStyle = '#ffffff'; c.fill();
        c.beginPath();
        c.moveTo(cx + sd * 0.1, y - 0.08); c.lineTo(cx + sd * 0.17, y + 0.14); c.lineTo(cx + sd * 0.24, y - 0.08);
        c.closePath();
        c.fillStyle = '#ffffff'; c.fill();
      });
      c.restore();
    },
  };

  /**
   * Accesorios que además dibujan DETRÁS del cuerpo.
   *
   * Es una capa aparte y no un dibujo más dentro del accesorio porque el orden
   * importa: la cola del gato tiene que quedar tapada por la panza, o parece
   * apoyada encima.
   */
  const HATS_BACK = {
    /* ── Lo de atrás de los disfraces (ver DISFRACES) ── */

    /** Brasita: la cola de salamandra con la llama encendida en la punta. */
    brasita(c, A, st) {
      const T = telaDisfraz(A, '#f4b43c');
      const M = marcoDisfraz();
      const base = M.bot - 1.4;
      const cola = colaDisfraz(c,
        [[M.cx + 3.0, base], [M.cx + 7.6, base + 1.4], [M.cx + 8.7, base - 3.2], [M.cx + 7.4, base - 5.6]],
        1.0, 0.36, T.tela, T.costura);
      // Bandas de escamas sobre la cola.
      c.lineWidth = 0.1; c.strokeStyle = withA(T.sombra, 0.75);
      [10, 16, 22, 28].forEach(i => {
        const [ax, ay] = cola.izq[i], [bx, by] = cola.der[i];
        c.beginPath(); c.moveTo(ax, ay); c.quadraticCurveTo((ax + bx) / 2 + 0.15, (ay + by) / 2 + 0.25, bx, by); c.stroke();
      });
      // Llama: tres lenguas, de la de afuera a la del centro, y el halo propio.
      const x = cola.x, y = cola.y - 0.15;
      c.save();
      c.shadowColor = A.fixed('#ff7a1a'); c.shadowBlur = _k * 0.9;
      glowing(c, st, 1.5, () => {
        [[A.fixed('#ff5a14'), 1.0], [A.fixed('#ffa62b'), 0.68], [A.fixed('#fff3c4', 0.3), 0.38]].forEach(([col, k]) => {
          c.beginPath();
          c.moveTo(x + 0.1 * k, y - 2.3 * k);
          c.bezierCurveTo(x + 1.15 * k, y - 1.1 * k, x + 1.0 * k, y + 0.7 * k, x, y + 0.75 * k);
          c.bezierCurveTo(x - 1.0 * k, y + 0.7 * k, x - 1.05 * k, y - 0.85 * k, x - 0.25 * k, y - 1.35 * k);
          c.quadraticCurveTo(x - 0.15 * k, y - 1.9 * k, x + 0.1 * k, y - 2.3 * k);
          c.fillStyle = col; c.fill();
        });
      }, y - 2.3);
      c.restore();
    },

    /** Caparazón: el caparazón de coral, que asoma por los costados y arriba. */
    caparazon(c, A) {
      const M = marcoDisfraz();
      const coral = A.fixed('#e8765a', 0.5);
      const borde = A.fixed('#f6dcb0', 0.4);
      // Angosto y alto, como una mochila: más ancho que el cuerpo rodeaba la
      // cabeza (un marco) y más bajo asomaba abajo como un plato. Así sólo
      // se ve por los costados, entre la capucha y la panza.
      const cy = M.top + M.h * 0.52, rx = M.rx + 1.9, ry = M.h * 0.47;
      // El ribete claro y el domo.
      ellipsePath(c, M.cx, cy, rx + 0.35, ry + 0.35);
      c.fillStyle = borde; c.fill();
      c.lineWidth = 0.14; c.strokeStyle = adjustL(coral, -45); c.stroke();
      ellipsePath(c, M.cx, cy, rx, ry);
      const g = c.createRadialGradient(M.cx - rx * 0.4, cy - ry * 0.5, 0.5, M.cx, cy, rx * 1.1);
      g.addColorStop(0, mixHex(coral, '#ffffff', 0.35)); g.addColorStop(0.6, coral); g.addColorStop(1, adjustL(coral, -28));
      c.fillStyle = g; c.fill();
      // Placas: anillo de escudos marginales con sus juntas, y hexágonos.
      c.save();
      ellipsePath(c, M.cx, cy, rx, ry); c.clip();
      c.lineWidth = 0.12; c.strokeStyle = withA(adjustL(coral, -45), 0.8);
      ellipsePath(c, M.cx, cy, rx - 1.15, ry - 1.15); c.stroke();
      for (let i = 0; i < 18; i++) {
        const a = i / 18 * Math.PI * 2;
        c.beginPath();
        c.moveTo(M.cx + Math.cos(a) * (rx - 1.15), cy + Math.sin(a) * (ry - 1.15));
        c.lineTo(M.cx + Math.cos(a) * rx, cy + Math.sin(a) * ry);
        c.stroke();
      }
      [[-1, -0.45], [1, -0.45], [-1, 0.25], [1, 0.25]].forEach(([sd, k]) => {
        const hx = M.cx + sd * (rx - 2.1), hy = cy + k * ry;
        c.beginPath();
        for (let i = 0; i < 6; i++) { const a = i / 6 * Math.PI * 2 + Math.PI / 6; c.lineTo(hx + Math.cos(a) * 1.0, hy + Math.sin(a) * 1.0); }
        c.closePath();
        c.fillStyle = withA(mixHex(coral, '#ffffff', 0.25), 0.6); c.fill(); c.stroke();
      });
      c.restore();
      // Colita.
      const T = telaDisfraz(A, '#f4e2b8');
      colaDisfraz(c, [[M.cx + 2.2, M.bot - 0.6], [M.cx + 3.6, M.bot + 0.4], [M.cx + 4.6, M.bot + 0.2], [M.cx + 4.7, M.bot - 0.5]],
        0.5, 0.12, T.tela, T.costura);
    },

    /** Brotito: el brote de la nuca (tallo, dos hojas y un zarcillo) y la colita. */
    brotito(c, A) {
      const T = telaDisfraz(A, '#d6ef9e');
      const M = marcoDisfraz();
      const hoja = A.fixed('#66b84a', 0.5);
      const nace = [M.cx + 0.3, M.cap - 0.1];
      // Tallo.
      c.beginPath();
      c.moveTo(nace[0], nace[1]);
      c.quadraticCurveTo(nace[0] - 0.25, nace[1] - 0.8, nace[0] + 0.05, nace[1] - 1.35);
      c.lineWidth = 0.42; c.lineCap = 'round'; c.strokeStyle = adjustL(hoja, -28); c.stroke();
      // Dos hojas, una más grande.
      const tope = [nace[0] + 0.05, nace[1] - 1.35];
      [[-1, 2.45, 3.0], [1, 2.25, 2.6]].forEach(([sd, ang, L]) => {
        c.save();
        c.translate(tope[0], tope[1]); c.rotate(-Math.PI / 2 + sd * (0.62 + (ang - 2.25) * 0.5));
        c.beginPath();
        c.moveTo(0, 0);
        c.bezierCurveTo(L * 0.25, -L * 0.42, L * 0.75, -L * 0.36, L, 0);
        c.bezierCurveTo(L * 0.75, L * 0.3, L * 0.25, L * 0.36, 0, 0);
        const g = c.createLinearGradient(0, -L * 0.3, L, L * 0.2);
        g.addColorStop(0, mixHex(hoja, '#ffffff', 0.25)); g.addColorStop(1, adjustL(hoja, -22));
        c.fillStyle = g; c.fill();
        c.lineWidth = 0.1; c.strokeStyle = adjustL(hoja, -45); c.stroke();
        // Nervaduras.
        c.beginPath(); c.moveTo(L * 0.05, 0); c.quadraticCurveTo(L * 0.5, -0.12, L * 0.9, 0);
        [0.3, 0.5, 0.7].forEach(t => {
          c.moveTo(L * t, -0.05); c.lineTo(L * (t + 0.12), -L * 0.2);
          c.moveTo(L * t, 0.02); c.lineTo(L * (t + 0.12), L * 0.17);
        });
        c.lineWidth = 0.06; c.strokeStyle = withA(adjustL(hoja, -45), 0.7); c.stroke();
        // Brillo.
        c.beginPath(); c.ellipse(L * 0.35, -L * 0.15, L * 0.18, L * 0.05, -0.2, 0, Math.PI * 2);
        c.fillStyle = withA('#ffffff', 0.35); c.fill();
        c.restore();
      });
      // Zarcillo.
      c.beginPath();
      for (let a = 0; a <= Math.PI * 2.6; a += 0.2) {
        const r = 0.55 * (1 - a / (Math.PI * 3));
        const x = tope[0] + 0.3 + Math.cos(a) * r, y = tope[1] + 0.9 - 0.55 + Math.sin(a) * r;
        a ? c.lineTo(x, y) : c.moveTo(x, y);
      }
      c.lineWidth = 0.08; c.strokeStyle = adjustL(hoja, -20); c.stroke();
      // Colita.
      colaDisfraz(c, [[M.cx + 2.8, M.bot - 1.2], [M.cx + 6.2, M.bot - 0.4], [M.cx + 7.2, M.bot - 1.3], [M.cx + 7.0, M.bot - 2.2]],
        0.85, 0.2, T.tela, T.costura);
    },

    /** Chispa: la cola de cable en espiral, con la chispa en la punta. */
    chispa(c, A, st) {
      const T = telaDisfraz(A, '#9fe6ff');
      const M = marcoDisfraz();
      const rayo = A.fixed('#ffe45c', 0.5);
      // El eje de la cola: sale de atrás por la derecha y sube.
      const P = [[M.cx + 3.2, M.bot - 1.6], [M.cx + 7.2, M.bot - 0.6], [M.cx + 8.6, M.bot - 4.2], [M.cx + 7.6, M.bot - 6.6]];
      const en = (t) => {
        const u = 1 - t;
        return [0, 1].map(k => u * u * u * P[0][k] + 3 * u * u * t * P[1][k] + 3 * u * t * t * P[2][k] + t * t * t * P[3][k]);
      };
      // El cable: un resorte alrededor del eje, con su brillo.
      const vueltas = 7, N = 140;
      const resorte = () => {
        c.beginPath();
        for (let i = 0; i <= N; i++) {
          const t = i / N;
          const [x, y] = en(t), [x2, y2] = en(Math.min(1, t + 0.01));
          const dx = x2 - x, dy = y2 - y, l = Math.hypot(dx, dy) || 1;
          const r = 0.68 * (1 - t * 0.4);
          const a = t * vueltas * Math.PI * 2;
          const px = x - dy / l * Math.sin(a) * r + dx / l * Math.cos(a) * 0.12;
          const py = y + dx / l * Math.sin(a) * r + dy / l * Math.cos(a) * 0.12;
          i ? c.lineTo(px, py) : c.moveTo(px, py);
        }
      };
      resorte(); c.lineWidth = 0.46; c.lineCap = 'round'; c.lineJoin = 'round'; c.strokeStyle = T.sombra; c.stroke();
      resorte(); c.lineWidth = 0.26; c.strokeStyle = T.tela; c.stroke();
      resorte(); c.lineWidth = 0.06; c.strokeStyle = withA(T.luz, 0.85); c.stroke();
      // La chispa de la punta.
      const [ex, ey] = en(1);
      glowing(c, st, 2.0, () => {
        c.beginPath();
        for (let i = 0; i < 8; i++) {
          const a = i / 8 * Math.PI * 2, r = i % 2 ? 0.38 : 0.95;
          c.lineTo(ex + Math.cos(a) * r, ey + Math.sin(a) * r);
        }
        c.closePath();
        c.fillStyle = rayo; c.fill();
        ellipsePath(c, ex, ey, 0.32, 0.32);
        c.fillStyle = '#ffffff'; c.fill();
      }, ey - 0.95);
    },

    /** Sombrita: la cola de humo, bocanadas que se apagan. */
    sombrita(c, A) {
      const T = telaDisfraz(A, '#4a5a86');
      const M = marcoDisfraz();
      [[3.2, -1.6, 1.25, 0.9], [5.6, -2.8, 1.05, 0.75], [7.2, -4.4, 0.85, 0.6], [8.0, -6.1, 0.6, 0.45], [8.2, -7.3, 0.38, 0.3]]
        .forEach(([dx, dy, r, a]) => {
          const x = M.cx + dx, y = M.bot + dy;
          const g = c.createRadialGradient(x - r * 0.3, y - r * 0.3, 0.05, x, y, r);
          g.addColorStop(0, withA(T.luz, a)); g.addColorStop(1, withA(T.tela, a * 0.6));
          ellipsePath(c, x, y, r, r * 0.85);
          c.fillStyle = g; c.fill();
        });
    },

    /**
     * Lo de atrás del samurái: el shikoro (el cubrenuca de láminas) y la
     * katana.
     *
     * El shikoro va DETRÁS del cuerpo a propósito: así sólo asoma por los
     * costados, como una campana, y ensancha la silueta de la cabeza sin
     * tocar la cara. Delante, las láminas eran cajas pegadas a las mejillas.
     * Cada lámina es una banda curva entera: el cuerpo tapa el centro y lo que
     * queda a la vista son las puntas, con su cordón (odoshi) cosido.
     *
     * La katana cruza la espalda y asoma por el hombro derecho: el mango y la
     * tsuba arriba, la punta de la vaina abajo a la izquierda. Antes eran dos
     * katanas cruzando la panza.
     */
    samurai(c, A) {
      const cx = HEAD.cx;
      const oro = A.alt('#e6b84a');
      const oroL = A.altLite('#e6b84a');
      const oroD = A.altDark('#e6b84a');
      const rojo = A.fixed('#c43122');
      const marfil = A.fixed('#f1e9d8');
      const laca = A.mat;

      // Katana: primero, así el shikoro le pasa por encima.
      c.save();
      c.translate(cx + 6.0, 1.9);
      c.rotate(2.415);
      // Saya (vaina): desde la tsuba hacia abajo a la izquierda.
      roundRect(c, 0.25, -0.34, 16.9, 0.68, 0.3);
      const vg = c.createLinearGradient(0, -0.34, 0, 0.34);
      vg.addColorStop(0, adjustL(laca, 14));
      vg.addColorStop(0.35, laca);
      vg.addColorStop(1, adjustL(A.dark, -26));
      c.fillStyle = vg; c.fill();
      c.lineWidth = 0.07; c.strokeStyle = withA(adjustL(A.dark, -40), 0.8); c.stroke();
      // Kojiri: el regatón de la punta.
      roundRect(c, 16.35, -0.36, 0.85, 0.72, 0.3);
      c.fillStyle = oro; c.fill();
      // Tsuba: el disco de la guarda.
      c.beginPath(); c.ellipse(0.12, 0, 0.2, 0.68, 0, 0, Math.PI * 2);
      c.fillStyle = oroD; c.fill();
      c.beginPath(); c.ellipse(0.05, 0, 0.13, 0.6, 0, 0, Math.PI * 2);
      c.fillStyle = oro; c.fill();
      // Tsuka: el mango, con el cordón en rombos sobre la piel de raya.
      roundRect(c, -2.75, -0.3, 2.75, 0.6, 0.2);
      c.fillStyle = marfil; c.fill();
      c.save();
      roundRect(c, -2.75, -0.3, 2.75, 0.6, 0.2); c.clip();
      c.lineWidth = 0.17; c.strokeStyle = adjustL(A.dark, -30);
      for (let i = 0; i < 5; i++) {
        const x0 = -2.55 + i * 0.52;
        c.beginPath(); c.moveTo(x0, -0.32); c.lineTo(x0 + 0.42, 0.32); c.stroke();
        c.beginPath(); c.moveTo(x0 + 0.42, -0.32); c.lineTo(x0, 0.32); c.stroke();
      }
      c.restore();
      // Kashira: la tapa del pomo.
      roundRect(c, -3.05, -0.32, 0.42, 0.64, 0.16);
      c.fillStyle = oro; c.fill();
      c.restore();

      // Shikoro: tres láminas, de la más baja a la más alta, para que cada
      // una monte sobre la siguiente como tejas.
      const LAMINAS = [
        // [ancho y altura de la punta arriba, ancho y altura abajo]
        [7.05, 8.45, 7.95, 10.15],
        [6.3, 6.95, 7.35, 8.75],
        [5.3, 5.3, 6.65, 7.3],
      ];
      const curva = (w, yE) => [cx - w, yE, cx, yE - 2.6, cx + w, yE];
      const punto = (w, yE, t) => {
        const [x0, y0, xc, yc, x1, y1] = curva(w, yE);
        const u = 1 - t;
        return [u * u * x0 + 2 * u * t * xc + t * t * x1, u * u * y0 + 2 * u * t * yc + t * t * y1];
      };
      LAMINAS.forEach(([wa, ya, wb, yb], i) => {
        const a = curva(wa, ya), b = curva(wb, yb);
        const forma = () => {
          c.beginPath();
          c.moveTo(a[0], a[1]); c.quadraticCurveTo(a[2], a[3], a[4], a[5]);
          // La punta se levanta apenas hacia afuera: el borde vuelto del shikoro.
          c.quadraticCurveTo(b[4] + 0.15, (a[5] + b[5]) / 2, b[4], b[5] - 0.15);
          c.quadraticCurveTo(b[2], b[3], b[0], b[1] - 0.15);
          c.quadraticCurveTo(b[0] - 0.15, (a[1] + b[1]) / 2, a[0], a[1]);
          c.closePath();
        };
        forma();
        const lg = c.createLinearGradient(0, ya - 1, 0, yb);
        lg.addColorStop(0, adjustL(laca, 6 - i * 2));
        lg.addColorStop(1, adjustL(A.dark, -20));
        c.fillStyle = lg; c.fill();
        // Odoshi: el cordón que cose las láminas, en columnas verticales.
        c.save();
        forma(); c.clip();
        c.lineCap = 'butt';
        [0.03, 0.085, 0.14, 0.86, 0.915, 0.97].forEach(t => {
          const [x0, y0] = punto(wa, ya, t);
          const [x1, y1] = punto(wb, yb, t);
          c.beginPath(); c.moveTo(x0, y0 - 0.2); c.lineTo(x1, y1);
          c.lineWidth = 0.24; c.strokeStyle = rojo; c.stroke();
          c.lineWidth = 0.07; c.strokeStyle = withA('#ffffff', 0.35); c.stroke();
        });
        c.restore();
        // Ribete de oro en el canto de abajo.
        c.beginPath();
        c.moveTo(b[0], b[1] - 0.15); c.quadraticCurveTo(b[2], b[3], b[4], b[5] - 0.15);
        c.lineWidth = 0.11; c.strokeStyle = oro; c.stroke();
        forma();
        c.lineWidth = 0.06; c.strokeStyle = withA(adjustL(A.dark, -35), 0.7); c.stroke();
      });
      // Un remache de oro donde el cordón cierra cada punta de la lámina baja.
      [0.05, 0.95].forEach(t => {
        const [x, y] = punto(7.95, 10.15, t);
        ellipsePath(c, x, y - 0.4, 0.14, 0.14);
        c.fillStyle = oroL; c.fill();
      });
    },

    /**
     * Cola de gato.
     *
     * Era un trazo de ancho parejo que asomaba un muñón gris al costado. Una
     * cola se lee por la FORMA: gruesa en la base, se afina y se curva en
     * signo de pregunta, con la punta de otro tono. Va rellena, no trazada,
     * para que el ancho pueda cambiar a lo largo.
     */
    gato(c, A) {
      const cola = A.mat;
      const punta = A.alt(A.realce);
      // Columna de la cola: base escondida detrás de la panza, sale por la
      // derecha y se enrula arriba.
      const P = [[10.6, 13.6], [15.6, 14.6], [16.6, 10.4], [15.4, 7.6], [14.1, 6.6], [13.6, 7.7]];
      const bez = (t) => {
        // Dos cúbicas pegadas: P0..P3 y P3..P5 (con el tirante reflejado).
        if (t < 0.6) {
          const u = t / 0.6, v = 1 - u;
          const [a, b, cc, d] = [P[0], P[1], P[2], P[3]];
          return [v * v * v * a[0] + 3 * v * v * u * b[0] + 3 * v * u * u * cc[0] + u * u * u * d[0],
                  v * v * v * a[1] + 3 * v * v * u * b[1] + 3 * v * u * u * cc[1] + u * u * u * d[1]];
        }
        const u = (t - 0.6) / 0.4, v = 1 - u;
        const a = P[3], b = [2 * P[3][0] - P[2][0], 2 * P[3][1] - P[2][1]], d = P[5];
        const cc = P[4];
        return [v * v * v * a[0] + 3 * v * v * u * b[0] + 3 * v * u * u * cc[0] + u * u * u * d[0],
                v * v * v * a[1] + 3 * v * v * u * b[1] + 3 * v * u * u * cc[1] + u * u * u * d[1]];
      };
      const N = 40, izq = [], der = [];
      for (let i = 0; i <= N; i++) {
        const t = i / N;
        const [x, y] = bez(t);
        const [x2, y2] = bez(Math.min(1, t + 0.01));
        const [x1, y1] = bez(Math.max(0, t - 0.01));
        const dx = x2 - x1, dy = y2 - y1, l = Math.hypot(dx, dy) || 1;
        const w = 0.72 * (1 - t * 0.55) + (t > 0.9 ? -(t - 0.9) * 3.5 : 0);
        izq.push([x - dy / l * w, y + dx / l * w]);
        der.push([x + dy / l * w, y - dx / l * w]);
      }
      const forma = () => {
        c.beginPath();
        izq.forEach(([x, y], i) => i ? c.lineTo(x, y) : c.moveTo(x, y));
        for (let i = der.length - 1; i >= 0; i--) c.lineTo(der[i][0], der[i][1]);
        c.closePath();
      };
      forma();
      const g = c.createLinearGradient(14, 14, 17, 7);
      g.addColorStop(0, adjustL(cola, -18));
      g.addColorStop(0.6, cola);
      g.addColorStop(1, adjustL(cola, 10));
      c.fillStyle = g; c.fill();
      c.lineWidth = 0.08; c.strokeStyle = withA(adjustL(A.dark, -30), 0.6); c.stroke();
      // La punta, de otro tono: lo último de la cola.
      c.save();
      forma(); c.clip();
      ellipsePath(c, 13.9, 7.2, 1.0, 1.1);
      c.fillStyle = punta; c.fill();
      c.restore();
      // Brillo a lo largo del lomo.
      c.beginPath();
      for (let i = 8; i <= 30; i++) {
        const [x, y] = izq[i];
        const [cxm, cym] = bez(i / N);
        i === 8 ? c.moveTo((x + cxm) / 2, (y + cym) / 2) : c.lineTo((x + cxm) / 2, (y + cym) / 2);
      }
      c.lineWidth = 0.12; c.lineCap = 'round'; c.strokeStyle = withA(mixHex(cola, '#ffffff', 0.5), 0.5); c.stroke();
    },

    /**
     * Alas de ángel.
     *
     * La versión anterior abría las plumas en abanico HACIA ARRIBA desde el
     * hombro: lo único que asomaba por encima de la cabeza eran las puntas, y
     * se leían como dos mechones de pelo parado. Un ala se reconoce por el
     * borde de ataque —el arco de arriba, con plumas cortas y redondas— y por
     * las remeras largas que cuelgan hacia afuera y abajo. Las dos cosas caen
     * donde el cuerpo no tapa: el arco por encima del hombro y las remeras al
     * costado.
     *
     * Las remeras cuelgan casi verticales: abiertas en abanico, la punta de
     * la última se salía del lienzo (que llega a 10 unidades del centro).
     */
    alas(c, A) {
      const pluma = A.alt('#eef2f8');
      const sombra = adjustL(pluma, -30);
      // Borde de ataque, en coordenadas del ala derecha (x desde el centro).
      const L0 = [2.2, 6.2], L1 = [3.4, -0.6], L2 = [6.4, 1.6];
      const borde = (t) => {
        const u = 1 - t;
        return [u * u * L0[0] + 2 * u * t * L1[0] + t * t * L2[0], u * u * L0[1] + 2 * u * t * L1[1] + t * t * L2[1]];
      };
      // Una pluma: almendra con la punta redondeada y el raquis marcado.
      const plumaEn = (x, y, ang, largo, ancho, tono) => {
        c.save();
        c.translate(x, y); c.rotate(ang);
        c.beginPath();
        c.moveTo(0, -ancho * 0.45);
        c.quadraticCurveTo(largo * 0.65, -ancho * 0.6, largo, 0);
        c.quadraticCurveTo(largo * 0.65, ancho * 0.6, 0, ancho * 0.45);
        c.closePath();
        const g = c.createLinearGradient(0, 0, largo, 0);
        g.addColorStop(0, adjustL(pluma, tono - 16));
        g.addColorStop(1, adjustL(pluma, tono));
        c.fillStyle = g; c.fill();
        c.lineWidth = 0.07; c.strokeStyle = withA(sombra, 0.6); c.stroke();
        c.beginPath(); c.moveTo(largo * 0.08, 0); c.lineTo(largo * 0.8, 0);
        c.lineWidth = 0.05; c.strokeStyle = withA(sombra, 0.45); c.stroke();
        c.restore();
      };
      [-1, 1].forEach(sd => {
        c.save();
        c.translate(BODY.cx, 0);
        c.scale(sd, 1);
        // Remeras primarias: largas, cuelgan del tramo de afuera del borde.
        for (let i = 0; i < 6; i++) {
          const t = 0.55 + i * 0.09;
          const [x, y] = borde(Math.min(1, t));
          plumaEn(x - 0.3, y + 0.4, 1.42 - i * 0.04, 5.4 + i * 0.25, 1.25, -12 + i * 2);
        }
        // Secundarias: más cortas, desde el tramo del medio.
        for (let i = 0; i < 5; i++) {
          const t = 0.3 + i * 0.08;
          const [x, y] = borde(t);
          plumaEn(x, y + 0.5, 1.5 - i * 0.04, 3.6 + i * 0.25, 1.15, -4);
        }
        // Coberteras: escamas cortas y claras a lo largo del borde.
        for (let i = 0; i < 9; i++) {
          const t = 0.12 + i * 0.1;
          const [x, y] = borde(Math.min(1, t));
          plumaEn(x - 0.1, y + 0.15, 1.15, 1.7, 0.95, 6);
        }
        // El borde de ataque, como un canto redondeado.
        c.beginPath();
        c.moveTo(L0[0], L0[1]); c.quadraticCurveTo(L1[0], L1[1], L2[0], L2[1]);
        c.lineWidth = 0.55; c.lineCap = 'round'; c.strokeStyle = adjustL(pluma, 4); c.stroke();
        c.lineWidth = 0.14; c.strokeStyle = withA('#ffffff', 0.9); c.stroke();
        c.restore();
      });
    },

    /**
     * Alas de murciélago.
     *
     * La geometría (hombro, muñeca y cuatro puntas) quedó; cambió cómo se
     * pinta. Antes los huesos eran trazos negros de ancho parejo encima de
     * una membrana gris: se leían como varillas de paraguas. Ahora la
     * membrana va por PAÑOS entre dedo y dedo, cada uno festoneado hacia la
     * muñeca y más claro cerca del hueso, que es donde la piel se estira; los
     * huesos se afinan hacia la punta y tienen nudillos, y en la muñeca asoma
     * la garra del pulgar.
     */
    alasmurcielago(c, A) {
      const cy = BODY.top + (BODY.bot - BODY.top) * 0.45;
      const hueso = adjustL(A.dark, -18);
      const huesoLuz = mixHex(A.lite, '#ffffff', 0.25);
      const membrana = adjustL(A.mat, 6);
      const M = [3.1, -3.1];
      const P = [[6.8, -4.0], [8.1, -0.9], [6.8, 1.8], [3.1, 3.6]];

      [-1, 1].forEach(sd => {
        c.save();
        c.translate(BODY.cx + sd * 2.2, cy);
        c.scale(sd, 1);
        c.rotate(-0.45);

        // Paños: [desde, hasta] entre puntas, y el último vuelve al cuerpo.
        const panos = [[[0, 0], M, P[0]], [M, P[0], P[1]], [M, P[1], P[2]], [M, P[2], P[3]]];
        panos.forEach(([a, b, d], i) => {
          c.beginPath();
          c.moveTo(a[0], a[1]); c.lineTo(b[0], b[1]);
          // Festón: el borde libre se curva hacia la muñeca.
          const mx = (b[0] + d[0]) / 2, my = (b[1] + d[1]) / 2;
          if (i === 0) c.lineTo(d[0], d[1]);
          else c.quadraticCurveTo(mx + (M[0] - mx) * 0.32, my + (M[1] - my) * 0.32, d[0], d[1]);
          c.closePath();
          const g = c.createRadialGradient(M[0], M[1], 0.3, M[0] + 1.5, M[1] + 2, 5.5);
          g.addColorStop(0, mixHex(membrana, '#ffffff', 0.22));
          g.addColorStop(0.55, membrana);
          g.addColorStop(1, adjustL(membrana, -34));
          c.fillStyle = g; c.fill();
        });
        // Último paño, del dedo de abajo al costado del cuerpo.
        c.beginPath();
        c.moveTo(0, 0); c.lineTo(M[0], M[1]); c.lineTo(P[3][0], P[3][1]);
        c.quadraticCurveTo(1.4, 1.6, 0, 1.4);
        c.closePath();
        c.fillStyle = adjustL(membrana, -18); c.fill();
        // Venitas de la membrana.
        c.lineWidth = 0.05; c.strokeStyle = withA(adjustL(membrana, -40), 0.5);
        [[0.45, 0.6], [0.55, 0.35], [0.5, 0.5]].forEach(([t, k], i) => {
          const a = P[i], b = P[i + 1];
          c.beginPath();
          c.moveTo(M[0] + (a[0] - M[0]) * t, M[1] + (a[1] - M[1]) * t);
          c.quadraticCurveTo(M[0] + ((a[0] + b[0]) / 2 - M[0]) * k, M[1] + ((a[1] + b[1]) / 2 - M[1]) * k,
            M[0] + (b[0] - M[0]) * t, M[1] + (b[1] - M[1]) * t);
          c.stroke();
        });
        // Huesos: brazo y dedos, cada uno más fino en la punta.
        const huesoDe = (a, b, w0, w1) => {
          const dx = b[0] - a[0], dy = b[1] - a[1], l = Math.hypot(dx, dy);
          const nx = -dy / l, ny = dx / l;
          c.beginPath();
          c.moveTo(a[0] + nx * w0, a[1] + ny * w0);
          c.lineTo(b[0] + nx * w1, b[1] + ny * w1);
          c.lineTo(b[0] - nx * w1, b[1] - ny * w1);
          c.lineTo(a[0] - nx * w0, a[1] - ny * w0);
          c.closePath();
          c.fillStyle = hueso; c.fill();
          c.beginPath();
          c.moveTo(a[0] + nx * w0 * 0.4, a[1] + ny * w0 * 0.4);
          c.lineTo(b[0] + nx * w1 * 0.4, b[1] + ny * w1 * 0.4);
          c.lineWidth = 0.07; c.strokeStyle = withA(huesoLuz, 0.55); c.stroke();
        };
        huesoDe([0, 0], M, 0.38, 0.3);
        P.slice(0, 3).forEach(p => huesoDe(M, p, 0.22, 0.06));
        huesoDe(M, P[3], 0.18, 0.06);
        // Nudillos a media falange.
        P.slice(0, 3).forEach(p => {
          ellipsePath(c, M[0] + (p[0] - M[0]) * 0.55, M[1] + (p[1] - M[1]) * 0.55, 0.17, 0.17);
          c.fillStyle = hueso; c.fill();
        });
        // Muñeca y garra del pulgar.
        ellipsePath(c, M[0], M[1], 0.36, 0.36);
        c.fillStyle = hueso; c.fill();
        c.beginPath();
        c.moveTo(M[0] - 0.15, M[1] - 0.25);
        c.quadraticCurveTo(M[0] - 0.35, M[1] - 1.0, M[0] + 0.25, M[1] - 1.15);
        c.quadraticCurveTo(M[0] - 0.05, M[1] - 0.8, M[0] + 0.2, M[1] - 0.3);
        c.closePath();
        c.fillStyle = A.fixed('#e8e2d2', 0.4); c.fill();
        c.restore();
      });
    },

    /**
     * Mochila cohete.
     *
     * Los tanques estaban tan adentro que el cuerpo los tapaba casi enteros:
     * asomaban dos rayitas al costado. Ahora van más afuera y más altos, así
     * se ven las ojivas por encima de los hombros y las toberas con la llama
     * por debajo de la panza. El arnés va adelante (HATS.jetpack).
     */
    jetpack(c, A) {
      const metal = A.alt('#c9d3e0');
      const franja = A.mat;
      const llama = A.fixed('#ff8b00', 0.5);
      const top = 2.6, bot = 12.9;
      [-1, 1].forEach(sd => {
        const x = BODY.cx + sd * 5.55;
        // Tanque.
        roundRect(c, x - 1.15, top, 2.3, bot - top, 1.1);
        const g = c.createLinearGradient(x - 1.2, 0, x + 1.2, 0);
        g.addColorStop(0, adjustL(metal, 6));
        g.addColorStop(0.3, mixHex(metal, '#ffffff', 0.5));
        g.addColorStop(0.6, metal);
        g.addColorStop(1, adjustL(metal, -32));
        c.fillStyle = g; c.fill();
        c.lineWidth = 0.08; c.strokeStyle = withA(adjustL(metal, -45), 0.7); c.stroke();
        // Ojiva de color y dos franjas.
        c.save();
        roundRect(c, x - 1.15, top, 2.3, bot - top, 1.1); c.clip();
        const og = c.createLinearGradient(x - 1.2, 0, x + 1.2, 0);
        og.addColorStop(0, adjustL(franja, 10)); og.addColorStop(0.35, mixHex(franja, '#ffffff', 0.3));
        og.addColorStop(1, adjustL(franja, -25));
        c.fillStyle = og;
        c.fillRect(x - 1.3, top - 0.1, 2.6, 1.6);
        c.fillRect(x - 1.3, top + 3.2, 2.6, 0.45);
        c.fillRect(x - 1.3, top + 3.95, 2.6, 0.2);
        c.restore();
        // Remaches.
        [top + 2.3, bot - 1.2].forEach(yy => {
          [-0.6, 0, 0.6].forEach(dx => {
            ellipsePath(c, x + dx, yy, 0.08, 0.08);
            c.fillStyle = adjustL(metal, -40); c.fill();
          });
        });
        // Tobera: campana oscura con el borde claro.
        c.beginPath();
        c.moveTo(x - 0.75, bot - 0.1);
        c.lineTo(x + 0.75, bot - 0.1);
        c.lineTo(x + 1.15, bot + 1.0);
        c.lineTo(x - 1.15, bot + 1.0);
        c.closePath();
        const tg = c.createLinearGradient(x - 1.2, 0, x + 1.2, 0);
        tg.addColorStop(0, adjustL(metal, -30)); tg.addColorStop(0.4, adjustL(metal, -8)); tg.addColorStop(1, adjustL(metal, -48));
        c.fillStyle = tg; c.fill();
        c.beginPath(); c.ellipse(x, bot + 1.0, 1.15, 0.22, 0, 0, Math.PI * 2);
        c.fillStyle = A.fixed('#ffd27a', 0.5); c.fill();
        // Llama: tres lenguas, de la de afuera a la del centro.
        [[1.05, 3.4, llama, 0.85], [0.72, 2.5, mixHex(llama, '#ffdd55', 0.6), 0.9], [0.38, 1.5, '#fff6d8', 0.95]].forEach(([w, l, col, a]) => {
          c.beginPath();
          c.moveTo(x - w, bot + 1.05);
          c.bezierCurveTo(x - w * 0.9, bot + 1.05 + l * 0.6, x - w * 0.2, bot + 1.05 + l * 0.8, x, bot + 1.05 + l);
          c.bezierCurveTo(x + w * 0.2, bot + 1.05 + l * 0.8, x + w * 0.9, bot + 1.05 + l * 0.6, x + w, bot + 1.05);
          c.closePath();
          c.fillStyle = withA(col, a); c.fill();
        });
      });
    },

    /**
     * Cola de demonio.
     *
     * Era un trazo fino con una flechita al final, del lado de atrás: apenas
     * asomaba. Ahora es una cola rellena que se afina, sale por la izquierda
     * dibujando una S y termina en la punta de corazón, que es la forma que
     * todo el mundo reconoce como "diablo".
     */
    demonio(c, A) {
      const cola = A.alt('#c8452f');
      const P = [[5.4, 13.9], [0.6, 15.4], [-1.4, 11.2], [0.6, 9.2]];
      const en = (t) => {
        const u = 1 - t;
        return [0, 1].map(k => u * u * u * P[0][k] + 3 * u * u * t * P[1][k] + 3 * u * t * t * P[2][k] + t * t * t * P[3][k]);
      };
      const N = 32, a = [], b = [];
      for (let i = 0; i <= N; i++) {
        const t = i / N;
        const [x, y] = en(t), [x2, y2] = en(Math.min(1, t + 0.01)), [x1, y1] = en(Math.max(0, t - 0.01));
        const dx = x2 - x1, dy = y2 - y1, l = Math.hypot(dx, dy) || 1;
        const w = 0.42 * (1 - t * 0.6);
        a.push([x - dy / l * w, y + dx / l * w]);
        b.push([x + dy / l * w, y - dx / l * w]);
      }
      c.beginPath();
      a.forEach(([x, y], i) => i ? c.lineTo(x, y) : c.moveTo(x, y));
      for (let i = b.length - 1; i >= 0; i--) c.lineTo(b[i][0], b[i][1]);
      c.closePath();
      const g = c.createLinearGradient(-1, 9, 5, 15);
      g.addColorStop(0, adjustL(cola, 8)); g.addColorStop(1, adjustL(cola, -22));
      c.fillStyle = g; c.fill();
      c.lineWidth = 0.07; c.strokeStyle = withA(adjustL(cola, -40), 0.7); c.stroke();
      // Brillo del lomo.
      c.beginPath();
      for (let i = 6; i <= 26; i++) {
        const [x, y] = en(i / N), [ax, ay] = b[i];
        i === 6 ? c.moveTo((x + ax) / 2, (y + ay) / 2) : c.lineTo((x + ax) / 2, (y + ay) / 2);
      }
      c.lineWidth = 0.1; c.lineCap = 'round'; c.strokeStyle = withA(mixHex(cola, '#ffffff', 0.45), 0.6); c.stroke();
      // Punta de corazón, girada según la cola.
      const [tx, ty] = P[3];
      c.save();
      c.translate(tx, ty);
      c.rotate(Math.atan2(P[3][1] - P[2][1], P[3][0] - P[2][0]) + Math.PI / 2);
      c.beginPath();
      c.moveTo(0, 0.25);
      c.bezierCurveTo(-1.25, -0.2, -0.85, -1.35, 0, -0.75);
      c.bezierCurveTo(0.85, -1.35, 1.25, -0.2, 0, 0.25);
      c.closePath();
      const pg = c.createLinearGradient(-0.9, 0.9, 0.9, -0.3);
      pg.addColorStop(0, mixHex(cola, '#ffffff', 0.35)); pg.addColorStop(1, adjustL(cola, -25));
      c.fillStyle = pg; c.fill();
      c.lineWidth = 0.08; c.strokeStyle = adjustL(cola, -40); c.stroke();
      c.restore();
    },

    /** Manguera de aire del buzo. Nace del casco y se pierde hacia arriba. */
    buzo(c, A) {
      const bronce = A.alt('#b9863c');
      // Punto de anclaje sobre la propia esfera: si se elige a ojo, la manguera
      // queda despegada y parece un garabato al lado de la cabeza.
      const x0 = HELMET.cx + HELMET.r * Math.cos(-0.62), y0 = HELMET.cy + HELMET.r * Math.sin(-0.62);
      c.beginPath();
      c.lineWidth = 0.62; c.lineCap = 'round';
      c.strokeStyle = adjustL(bronce, -26);
      // Termina DENTRO del lienzo, con la punta redondeada. Llevada hasta el
      // canto se leía como una manguera cortada, no como una que se va.
      c.moveTo(x0, y0);
      c.bezierCurveTo(x0 + 2.7, y0 - 0.5, x0 + 2.0, y0 - 2.7, x0 + 0.3, y0 - 3.4);
      c.stroke();
      // Anillado de la manguera.
      c.lineWidth = 0.16;
      c.strokeStyle = withA(mixHex(bronce, '#ffffff', 0.4), 0.75);
      for (let i = 0; i <= 5; i++) {
        const t = i / 5;
        const px = x0 + (2.5 - 1.9 * t * t) * t * 1.15;
        const py = y0 - 3.4 * t * t - 0.35 * t;
        c.beginPath();
        c.moveTo(px - 0.30, py - 0.22);
        c.lineTo(px + 0.30, py + 0.22);
        c.stroke();
      }
    },

    /**
     * REGLA de la capa de atrás, aprendida con estos tres accesorios:
     *
     * el cuerpo mide 12.2 unidades de ancho en un lienzo de 16, así que lo que
     * se dibuja detrás sólo se VE donde despeja la silueta — arriba de la
     * cabeza, abajo de la panza, o saliendo en diagonal. Las katanas funcionaron
     * por eso. La primera versión de la capa, el carcaj y la bufanda quedaba
     * entera adentro del contorno: el código corría, el dibujo estaba, y no se
     * veía nada.
     */

    /** Capa: cuello parado por arriba de la cabeza y vuelo por debajo. */
    capa(c, A) {
      const yHom = BODY.top + 1.5, yBot = BODY.bot + 1.35;
      // El forro va más oscuro que la tela: claro, el cuello parado se leía
      // como una mancha rosa detrás de la cabeza.
      const tela = A.mat, forro = A.alt(mixHex(A.mat, '#14060a', 0.45));

      // Sombra proyectada por la capa en el piso
      c.save();
      c.beginPath();
      c.ellipse(BODY.cx, yBot + 0.5, 8.0, 1.2, 0, 0, Math.PI * 2);
      c.fillStyle = 'rgba(0,0,0,0.2)';
      c.fill();
      c.restore();

      // Vuelo: arranca angosto en los hombros y se abre bien afuera del cuerpo.
      c.beginPath();
      c.moveTo(BODY.cx - 3.2, yHom);
      c.bezierCurveTo(BODY.cx - 6.4, yHom + 4.5, BODY.cx - 7.4, yBot - 3.0, BODY.cx - 7.9, yBot);
      for (let i = 0; i < 4; i++) {
        const x0 = BODY.cx - 7.9 + i * 3.95;
        c.quadraticCurveTo(x0 + 1.0, yBot + 0.95, x0 + 1.97, yBot - 0.35);
        c.quadraticCurveTo(x0 + 2.95, yBot - 1.15, x0 + 3.95, yBot);
      }
      c.bezierCurveTo(BODY.cx + 7.4, yBot - 3.0, BODY.cx + 6.4, yHom + 4.5, BODY.cx + 3.2, yHom);
      c.closePath();
      
      const g = c.createLinearGradient(BODY.cx - 7, yHom, BODY.cx + 7, yBot);
      g.addColorStop(0, adjustL(tela, -20));
      g.addColorStop(0.5, tela);
      g.addColorStop(1, adjustL(tela, -35));
      c.fillStyle = g; c.fill();

      // Pliegues volumétricos (en lugar de simples líneas)
      c.save();
      c.clip();
      [-2.6, -1.1, 0.7, 2.2].forEach(d => {
        c.beginPath();
        c.moveTo(BODY.cx + d * 0.6 - 0.5, yHom + 0.5);
        c.quadraticCurveTo(BODY.cx + d * 1.7 - 1.0, yHom + 6, BODY.cx + d * 2.3 - 1.5, yBot + 1);
        c.lineTo(BODY.cx + d * 2.3 + 1.0, yBot + 1);
        c.quadraticCurveTo(BODY.cx + d * 1.7 + 0.5, yHom + 6, BODY.cx + d * 0.6 + 0.5, yHom + 0.5);
        c.closePath();
        
        const pg = c.createLinearGradient(BODY.cx + d * 1.7 - 1.0, 0, BODY.cx + d * 1.7 + 1.0, 0);
        pg.addColorStop(0, 'rgba(0,0,0,0.3)');
        pg.addColorStop(0.5, 'rgba(0,0,0,0)');
        pg.addColorStop(1, 'rgba(255,255,255,0.1)');
        c.fillStyle = pg;
        c.fill();
        
        // Línea de tensión principal
        c.beginPath();
        c.lineWidth = 0.2;
        c.strokeStyle = adjustL(tela, -45);
        c.moveTo(BODY.cx + d * 0.6, yHom + 0.5);
        c.quadraticCurveTo(BODY.cx + d * 1.7, yHom + 6, BODY.cx + d * 2.3, yBot + 1);
        c.stroke();
      });
      c.restore();

      // Cuello parado: sube por ARRIBA de la cabeza, que es donde no compite
      // con el cuerpo. Es lo que hace que la capa se lea de una.
      c.beginPath();
      c.moveTo(BODY.cx - 3.4, yHom + 0.6);
      c.quadraticCurveTo(BODY.cx - 4.9, BODY.top - 2.6, BODY.cx - 2.5, BODY.top - 2.2);
      c.quadraticCurveTo(BODY.cx, BODY.top - 0.6, BODY.cx + 2.5, BODY.top - 2.2);
      c.quadraticCurveTo(BODY.cx + 4.9, BODY.top - 2.6, BODY.cx + 3.4, yHom + 0.6);
      c.closePath();
      const cg = c.createLinearGradient(0, BODY.top - 2.6, 0, yHom + 0.6);
      cg.addColorStop(0, forro);
      cg.addColorStop(1, adjustL(tela, -14));
      c.fillStyle = cg; c.fill();
      c.lineWidth = 0.24;
      c.strokeStyle = withA(adjustL(tela, -34), 0.7);
      c.stroke();
    },

    /** Carcaj: asoma por arriba del hombro derecho. */
    arquero(c, A) {
      const cy = BODY.top + (BODY.bot - BODY.top) * 0.44;
      const madera = A.alt('#8a5a2b');
      // Carcaj cruzado: el emplumado sale por encima del hombro derecho.
      c.save();
      c.translate(BODY.cx + 1.6, cy + 1.2);
      c.rotate(0.55);
      for (let i = -1; i <= 1; i++) {
        const x = i * 0.78;
        c.beginPath();
        c.lineWidth = 0.20;
        c.strokeStyle = adjustL(madera, 12);
        c.moveTo(x, -2.4); c.lineTo(x, -8.4);
        c.stroke();
        [[-0.58, adjustL(A.alt('#4fa96b'), 0)], [0.58, adjustL(A.alt('#4fa96b'), -16)]].forEach(f => {
          c.beginPath();
          c.moveTo(x, -6.9);
          c.lineTo(x + f[0], -7.6);
          c.lineTo(x, -8.4);
          c.closePath();
          c.fillStyle = f[1]; c.fill();
        });
      }
      roundRect(c, -1.35, -2.6, 2.7, 6.4, 0.6);
      const g = c.createLinearGradient(-1.4, 0, 1.4, 0);
      g.addColorStop(0, adjustL(madera, 14));
      g.addColorStop(1, adjustL(madera, -28));
      c.fillStyle = g; c.fill();
      c.lineWidth = 0.22;
      c.strokeStyle = withA(adjustL(madera, -36), 0.85);
      [-1.6, 0.6, 2.7].forEach(dy => {
        c.beginPath(); c.moveTo(-1.35, dy); c.lineTo(1.35, dy); c.stroke();
      });
      c.restore();
    },

  };

  /**
   * Geometría compartida de los cascos cerrados.
   *
   * Vive afuera de cada accesorio porque el casquete y el vidrio se dibujan en
   * capas distintas (uno antes de los ojos, el otro después) y tienen que
   * coincidir al milímetro. Con las medidas repetidas en las dos funciones,
   * mover el casco significaba acordarse de tocar los dos lados.
   *
   * `faceY`/`faceR` acotan la zona donde están los ojos: es la que ningún
   * reflejo puede pisar.
   */
  const HELMET = { cx: 8, cy: 7.80, r: 6.15, faceY: 7.95, faceR: 4.05 };

  /**
   * Accesorios que dibujan POR DELANTE de la cara.
   *
   * Es una capa aparte de HATS_FRONT (lentes, pirata) porque estos además
   * dibujan sobre la cabeza: el casco de astronauta necesita el aro por detrás
   * de los ojos y el vidrio por delante, y con un solo booleano no se puede.
   */
  const HATS_OVER = {
    astronauta(c, A) {
      const cx = HELMET.cx, cy = HELMET.cy, r = HELMET.r;
      c.save();
      
      // Vidrio base, recortado
      c.beginPath();
      c.arc(cx, cy, r - 0.9, 0, Math.PI * 2);
      c.clip();

      // Fondo del vidrio: esférico profundo
      const g = c.createRadialGradient(cx - r * 0.4, cy - r * 0.4, r * 0.1, cx, cy, r);
      g.addColorStop(0.00, withA('#dff0ff', 0.1));
      g.addColorStop(0.70, withA('#4a7fb5', 0.15));
      g.addColorStop(1.00, withA('#0b1d3a', 0.6));
      c.fillStyle = g;
      c.fillRect(cx - r, cy - r, r * 2, r * 2);

      // Visor dorado: envolvente
      const topeVisor = HELMET.faceY - HELMET.faceR * 0.72;
      const vg = c.createLinearGradient(0, cy - r, 0, topeVisor + 0.5);
      vg.addColorStop(0, withA(A.alt('#f0b429'), 0.85));
      vg.addColorStop(1, withA(A.alt('#f0b429'), 0));
      c.fillStyle = vg;
      c.beginPath();
      c.moveTo(cx - r, cy - r);
      c.lineTo(cx + r, cy - r);
      c.lineTo(cx + r, topeVisor);
      c.quadraticCurveTo(cx, topeVisor + 1.5, cx - r, topeVisor);
      c.closePath();
      c.fill();

      // Reflejos curvos sobre el cristal
      c.beginPath();
      c.arc(cx, cy, r - 1.2, Math.PI * 0.75, Math.PI * 1.15);
      c.lineWidth = 0.8;
      c.lineCap = 'round';
      c.strokeStyle = withA('#ffffff', 0.4);
      c.stroke();
      
      c.beginPath();
      c.arc(cx, cy, r - 1.2, Math.PI * 1.18, Math.PI * 1.25);
      c.lineWidth = 0.8;
      c.lineCap = 'round';
      c.strokeStyle = withA('#ffffff', 0.4);
      c.stroke();

      c.restore();

      // Brillo del canto del aro: cierra la silueta contra el fondo oscuro.
      c.beginPath();
      c.lineWidth = 0.26;
      c.strokeStyle = withA(mixHex(A.mat, '#ffffff', 0.8), 0.85);
      c.arc(cx, cy, r - 0.10, Math.PI * 1.08, Math.PI * 1.75);
      c.stroke();
    },

    arquero(c, A, st) {
      const madera = A.alt('#8a5a2b');
      const cuerda = A.fixed('#e6ded0');
      /* `vecPal(st)` y NO `getPal()`.
       *
       * Son dos paletas distintas y sólo una tiene los tonos del CUERPO.
       * `getPal()` devuelve la del tema —{F, C, eye, aca, muda}— y `vecPal(st)`
       * la derivada para el dibujo vectorial, que es donde viven `core`, `deep`
       * y compañía. Con la equivocada, la mano que agarra el arco pedía
       * `P.core` y recibía `undefined`: `addColorStop` lanza con un color que no
       * parsea y el render del accesorio ABORTA a la mitad.
       *
       * El síntoma no se parecía a la causa. El estado sí cambiaba —
       * `getHat()` devolvía 'arquero'— pero como el dibujo nunca terminaba, se
       * veía como que el click no hacía nada; y era el único accesorio así.
       * Antes de buscar el bug en el botón, mirar la consola.
       *
       * `st` ya llega por parámetro: era sólo usarlo. */
      const P = vecPal(st);

      c.save();
      // Arco cruzado delante (a la altura de la panza, por la izquierda)
      c.translate(BODY.cx - 2.8, BODY.top + 8.0);
      c.rotate(-0.75); // Más acostado para acompañar la curva
      
      // Cuerpo del arco 2.5D
      c.beginPath();
      c.lineWidth = 0.7; c.lineCap = 'round';
      c.strokeStyle = madera;
      c.moveTo(0, -8.6);
      c.quadraticCurveTo(-3.5, 0, 0, 8.6);
      c.stroke();
      
      // Highlight del arco para darle volumen cilíndrico
      c.beginPath();
      c.lineWidth = 0.2; c.lineCap = 'round';
      c.strokeStyle = mixHex(madera, '#ffffff', 0.25);
      c.moveTo(0, -8.4);
      c.quadraticCurveTo(-3.1, 0, 0, 8.4);
      c.stroke();
      
      // Empuñadura central
      c.beginPath();
      c.lineWidth = 1.1;
      c.strokeStyle = adjustL(madera, -24);
      c.moveTo(-1.75, -1.2); c.lineTo(-1.75, 1.2);
      c.stroke();
      
      // Cuerda tensa
      c.beginPath();
      c.lineWidth = 0.2;
      c.strokeStyle = withA(cuerda, 0.9);
      c.moveTo(0, -8.6); c.lineTo(0, 8.6);
      c.stroke();

      // Mano agarrando el arco (superpuesta a la empuñadura)
      c.translate(-1.8, 0); 
      c.rotate(0.6); // Contrarresta un poco para que la mano quede natural
      
      // Sombra propia de la mano
      c.beginPath();
      c.ellipse(0, 0, 1.1, 1.4, 0, 0, Math.PI * 2);
      const g = c.createLinearGradient(-1.1, -1.4, 1.1, 1.4);
      g.addColorStop(0, P.core);
      g.addColorStop(1, P.deep);
      c.fillStyle = g;
      c.fill();
      
      // Borde suave de la mano
      c.lineWidth = 0.2;
      c.strokeStyle = adjustL(P.deep, -10);
      c.stroke();
      
      c.restore();
    },

    buzo(c, A) {
      const cx = HELMET.cx, fy = HELMET.faceY, fr = HELMET.faceR;
      const bronce = A.alt('#b9863c');

      // Reflejo del vidrio: una franja arriba de los ojos, nunca encima.
      c.save();
      ellipsePath(c, cx, fy, fr - 0.2, fr - 0.2);
      c.clip();
      c.globalAlpha = 0.20;
      c.fillStyle = '#dff2ff';
      c.beginPath();
      c.moveTo(cx - fr, fy - fr * 0.95);
      c.lineTo(cx + fr, fy - fr * 1.15);
      c.lineTo(cx + fr, fy - fr * 0.55);
      c.lineTo(cx - fr, fy - fr * 0.35);
      c.closePath();
      c.fill();
      c.restore();

      // Marco de la portilla.
      c.beginPath();
      c.lineWidth = 0.80;
      c.strokeStyle = adjustL(bronce, -20);
      c.arc(cx, fy, fr, 0, Math.PI * 2);
      c.stroke();
      c.beginPath();
      c.lineWidth = 0.22;
      c.strokeStyle = withA(mixHex(bronce, '#ffffff', 0.6), 0.85);
      c.arc(cx, fy, fr - 0.34, 0, Math.PI * 2);
      c.stroke();

      // Tornillos sobre el marco.
      for (let i = 0; i < 12; i++) {
        const a = (i / 12) * Math.PI * 2 - Math.PI / 2;
        const bx = cx + Math.cos(a) * fr, by = fy + Math.sin(a) * fr;
        ellipsePath(c, bx, by, 0.22, 0.22);
        c.fillStyle = mixHex(bronce, '#ffffff', 0.45); c.fill();
        ellipsePath(c, bx + 0.06, by + 0.06, 0.10, 0.10);
        c.fillStyle = withA(adjustL(bronce, -34), 0.8); c.fill();
      }
    },
  };

  // Accesorios que van DELANTE de la cara en vez de sobre la cabeza.
  const HATS_FRONT = { lentes: 1, pirata: 1 };

  /* ══ MATERIALES ═════════════════════════════════════════════════════════
   *
   * El dibujo de cada material (la lista para las interfaces es CHARACTERS,
   * que hoy sólo tiene materiales). Cada entrada declara su paleta fija y
   * engancha capas del MISMO pintor que Boberto, así que parpadea, mira,
   * salta y agarra la laptop sin reimplementar nada de eso:
   *
   *   pal      tonos fijos. `base` es el color de cuerpo del que se deriva el
   *            sombreado y `tint` el de los ojos corazón; el resto pisa lo
   *            derivado (lift, core, mid, deep, edge, armA/armB, feetA/feetB,
   *            handA/handB…). Ver pintorCharacter.
   *   halo     brillo propio y fijo alrededor del cuerpo: {color, blur}.
   *   back     detrás del cuerpo (el hongo de la piedra).
   *   head     sobre el cuerpo y debajo de los ojos: la textura.
   *   ojo      un ojo, con la forma que pide la pose. false = el de Boberto.
   *   brazo    decora un brazo, en las coordenadas del muñón.
   *
   * El motor acepta además bodyRX, ojos, sinOjos, boca, front, over y bata:
   * los usaban los personajes de franquicia (Rick, Gojo, Pikachu, Goku…),
   * que se retiraron el 14/10/2026. Una herramienta interna no tiene por qué
   * cargar personajes ajenos; en su lugar están los disfraces, que son
   * accesorios y respetan lo que la persona eligió para el cuerpo.
   *
   * Las capas reciben (c, P, pose) con la pose EFECTIVA —bodyDY y bodyRX
   * incluidos—, así que un salto o un estornudo mueven la textura con el
   * cuerpo.
   */

  // La silueta de la pose en curso, para recortar ropa y piel contra ella.
  function siluetaDe(pose) {
    const dy = (pose && pose.bodyDY) || 0;
    return {
      cx: BODY.cx, dy: dy, top: BODY.top + dy, bot: BODY.bot + dy,
      rx: BODY.rx * ((pose && pose.bodyRX) || 1),
    };
  }

  // Altura de los ojos de Boberto, con salto y mirada: la usan las cejas, la
  // nariz y el parche, que tienen que acompañar al ojo.
  function alturaOjos(pose) {
    const p = pose || {};
    return BODY.top + (BODY.bot - BODY.top) * 0.38 + (p.bodyDY || 0) + (p.gaze ? p.gaze[1] * 0.48 : 0);
  }

  // Formas que un ojo de personaje deja en manos del ojo de Boberto.
  const FORMAS_DE_BOBERTO = { heart: 1, spiral: 1, shut: 1, happy: 1 };

  function llenarElipse(c, x, y, rx, ry, color, rot) {
    c.beginPath();
    c.ellipse(x, y, Math.max(rx, 0.01), Math.max(ry, 0.01), rot || 0, 0, Math.PI * 2);
    c.fillStyle = color;
    c.fill();
  }

  /*
   * Un ojo que se queda en su lugar mientras la mirada corre el iris.
   *
   * En un ojo de material (anillos, gema) la cuenca se queda quieta y lo
   * que se mueve es lo de adentro: corrida entera, se lee como un párpado
   * torcido.
   *
   * Devuelve el alto relativo del ojo según la forma: achinado lo aplasta,
   * sorprendido lo abre un poco.
   */
  function aperturaOjo(e) {
    if (e.forma === 'squint') return 0.55;
    return e.sy || 1;
  }

  // Tinta: contorno oscuro de los trazos con relleno plano (props).
  const TINTA = '#15101c', TINTA_W = 0.3;

  // Elipse con relleno y, si se pide, tinta: `tinta` es true (el ancho de
  // siempre) o un ancho. Deja el trazado armado, para recortar contra él.
  function elipseTinta(c, x, y, rx, ry, relleno, tinta, rot) {
    c.beginPath();
    c.ellipse(x, y, Math.max(rx, 0.01), Math.max(ry, 0.01), rot || 0, 0, Math.PI * 2);
    if (relleno) { c.fillStyle = relleno; c.fill(); }
    if (tinta) { c.strokeStyle = TINTA; c.lineWidth = tinta === true ? TINTA_W : tinta; c.stroke(); }
  }

  function trazoPoli(c, pts) {
    c.beginPath();
    c.moveTo(pts[0][0], pts[0][1]);
    for (let i = 1; i < pts.length; i++) c.lineTo(pts[i][0], pts[i][1]);
    c.closePath();
  }

  // Polígono con tinta. Las puntas van redondeadas: un inglete en un ángulo
  // agudo se estira muy por fuera del dibujo.
  function poliTinta(c, pts, relleno) {
    trazoPoli(c, pts);
    if (relleno) { c.fillStyle = relleno; c.fill(); }
    c.strokeStyle = TINTA; c.lineWidth = TINTA_W; c.lineJoin = 'round'; c.stroke();
  }

  /* ── Skins de material: madera, talla, gemas, piedra y kintsugi ──
   *
   * Paleta fija, como un personaje, pero el accesorio se dibuja encima: la
   * materia es la silueta de Boberto, no otro cuerpo. Comparten trazos:
   * todo se escribe en coordenadas RELATIVAS a la silueta (u de -1 a 1 sobre
   * el ancho, v de 0 a 1 sobre el alto), así la textura acompaña el salto y el
   * aplastado de cada pose sin una versión por cuadro.
   *
   * La zona de los ojos (u ±0.75, v 0.17–0.60) y la de la boca quedan libres
   * de lo que brilla o tiene mucho contraste: la cara tiene que seguir
   * mandando.
   */

  // Por debajo de ~5 px por unidad (la sidebar colapsada) los detalles finos
  // son ruido: a ese tamaño se dibuja sólo lo que se lee.
  function texturaFina() {
    return _k / Math.min(window.devicePixelRatio || 1, 3) >= 5;
  }

  // La silueta de la pose con sus conversores de coordenadas relativas.
  function marcoMaterial(pose) {
    const s = siluetaDe(pose);
    const h = s.bot - s.top;
    return Object.assign(s, { h, X: (u) => s.cx + u * s.rx, Y: (v) => s.top + v * h });
  }

  // Cuánto se angosta una franja vertical a la altura v: los meridianos se
  // juntan arriba y abajo. Paralelas, las franjas se leían como una lámina
  // pegada encima; convergiendo, el cuerpo sigue siendo una esfera.
  function anchoMeridiano(v, curva) {
    const t = Math.min(0.98, Math.max(0.02, v));
    return Math.pow(Math.sin(Math.PI * t), curva);
  }

  // Tramos de bézier cúbicas en coordenadas relativas:
  // [inicio, c1, c2, fin, c1, c2, fin…].
  function trazarTramos(c, m, t) {
    c.beginPath(); c.moveTo(m.X(t[0][0]), m.Y(t[0][1]));
    for (let i = 1; i + 2 < t.length; i += 3) {
      c.bezierCurveTo(m.X(t[i][0]), m.Y(t[i][1]), m.X(t[i + 1][0]), m.Y(t[i + 1][1]), m.X(t[i + 2][0]), m.Y(t[i + 2][1]));
    }
  }
  function trazarPolilinea(c, m, pts) {
    c.beginPath();
    pts.forEach(([u, v], i) => i ? c.lineTo(m.X(u), m.Y(v)) : c.moveTo(m.X(u), m.Y(v)));
  }

  // Una grieta de verdad no va recta entre dos vértices: zigzaguea. Parte cada
  // tramo en `n` y corre los puntos intermedios a los costados, con semilla
  // fija para que la grieta no tiemble entre cuadros.
  function quebrar(pts, n, amp, semilla) {
    let s = semilla;
    const r = () => { s = (s * 16807) % 2147483647; return (s - 1) / 2147483646 - 0.5; };
    const out = [pts[0]];
    for (let i = 1; i < pts.length; i++) {
      const [u0, v0] = pts[i - 1], [u1, v1] = pts[i];
      const du = u1 - u0, dv = v1 - v0, len = Math.hypot(du, dv) || 1;
      for (let j = 1; j <= n; j++) {
        const t = j / n, k = j === n ? 0 : r() * amp;
        out.push([u0 + du * t - (dv / len) * k, v0 + dv * t + (du / len) * k]);
      }
    }
    return out;
  }

  // Un trazo que brilla. shadowBlur va en píxeles del lienzo y no lo escala la
  // transformación: por eso se multiplica por _k. La línea de luz del centro
  // sólo aparece cuando hay tamaño para verla.
  function trazoBrillo(c, ancho, trazar, color, luz, blur) {
    c.save();
    c.lineCap = 'round'; c.lineJoin = 'round';
    c.shadowColor = color; c.shadowBlur = _k * blur;
    trazar(); c.strokeStyle = color; c.lineWidth = ancho; c.stroke();
    c.shadowBlur = 0;
    if (luz && texturaFina()) {
      trazar(); c.strokeStyle = withA(luz, 0.85); c.lineWidth = ancho * 0.35; c.stroke();
    }
    c.restore();
  }

  // Una espiral que se cierra hacia adentro, aplastada un poco en vertical.
  function trazarEspiral(c, x, y, r, vueltas, sentido) {
    c.beginPath();
    const fin = Math.PI * 2 * vueltas;
    for (let a = 0; a <= fin; a += 0.2) {
      const rr = r * (1 - a / (fin + 0.6));
      const px = x + Math.cos(a * sentido) * rr, py = y + Math.sin(a * sentido) * rr * 0.8;
      a ? c.lineTo(px, py) : c.moveTo(px, py);
    }
  }

  // Destello de cuatro puntas: lo que hace leer "pulido" a una piedra.
  function destello(c, x, y, r, color, blur) {
    c.save();
    c.shadowColor = color; c.shadowBlur = _k * blur;
    c.beginPath();
    c.moveTo(x, y - r);
    c.quadraticCurveTo(x, y, x + r, y); c.quadraticCurveTo(x, y, x, y + r);
    c.quadraticCurveTo(x, y, x - r, y); c.quadraticCurveTo(x, y, x, y - r);
    c.fillStyle = color; c.fill();
    c.restore();
  }

  /* Decorado de los pies. Los pies los pinta el motor con dos tonos y nada
     más, así que la materia se les agrega desde la capa de cabeza: recortada
     al pie y FUERA del cuerpo, porque esa capa va después y taparía la panza.
     Sin esto el cuerpo era de gema y los pies de plástico. */
  function enPies(c, pose, fn) {
    if (pose.hideFeet || !texturaFina()) return;
    const m = marcoMaterial(pose);
    lugarPies(pose).forEach(([x, y], i) => {
      c.save();
      ellipsePath(c, x, y, 1.15, 0.80); c.clip();
      c.beginPath(); c.rect(x - 2, y - 2, 4, 4);
      blobPath(c, m.cx, m.top, m.bot, m.rx, true);
      c.clip('evenodd');
      fn(x, y, i ? 1 : -1);
      c.restore();
    });
  }

  // Una hoja: almendra con nervadura, delineada con el trazo que se le pase.
  function hojaMaterial(c, x, y, ang, largo, relleno, trazo) {
    const forma = () => {
      c.save(); c.translate(x, y); c.rotate(ang);
      c.beginPath(); c.moveTo(0, 0);
      c.quadraticCurveTo(largo * 0.5, -largo * 0.34, largo, 0);
      c.quadraticCurveTo(largo * 0.5, largo * 0.34, 0, 0);
      c.moveTo(largo * 0.08, 0); c.lineTo(largo * 0.78, 0);
      c.restore();
    };
    if (relleno) { forma(); c.fillStyle = relleno; c.fill(); }
    trazo(forma);
  }

  /* Volumen para una textura OPACA. Las que tapan el cuerpo entero se comen
     la luz de drawBody, y sin esto el personaje queda plano como una
     calcomanía: se repone el brillo de arriba, la oclusión de la panza y el
     rim light, por encima de la materia. */
  function volumenMaterial(c, m, brillo) {
    const og = c.createLinearGradient(0, m.Y(0.45), 0, m.bot);
    og.addColorStop(0, 'rgba(0,0,0,0)');
    og.addColorStop(1, 'rgba(0,0,0,0.42)');
    c.fillStyle = og; c.fillRect(m.cx - m.rx, m.Y(0.45), m.rx * 2, m.h);
    const gg = c.createRadialGradient(m.X(-0.3), m.Y(0.15), 0.2, m.X(-0.3), m.Y(0.15), m.rx * 0.85);
    gg.addColorStop(0, withA('#ffffff', brillo));
    gg.addColorStop(1, withA('#ffffff', 0));
    c.fillStyle = gg; c.fillRect(m.cx - m.rx, m.top, m.rx * 2, m.h);
    const rg = c.createLinearGradient(m.cx - m.rx, m.top, m.cx + m.rx, m.bot);
    rg.addColorStop(0.55, 'rgba(255,255,255,0)');
    rg.addColorStop(0.85, 'rgba(255,255,255,0.30)');
    rg.addColorStop(1, 'rgba(255,255,255,0.08)');
    blobPath(c, m.cx, m.top, m.bot, m.rx);
    // Fino a propósito: a 0.7 se sumaba al contorno y al halo y la cabeza
    // quedaba con un borde grueso, doble, en los cuerpos oscuros.
    c.strokeStyle = rg; c.lineWidth = 0.38; c.stroke();
  }

  // Pseudoazar con semilla: motas que no cambian entre cuadros ni entre cargas.
  function motasFijas(semilla, n, zona) {
    let s = semilla;
    const r = () => { s = (s * 16807) % 2147483647; return (s - 1) / 2147483646; };
    const out = [];
    while (out.length < n) {
      const u = zona.u0 + r() * (zona.u1 - zona.u0), v = zona.v0 + r() * (zona.v1 - zona.v0);
      const radio = zona.r0 + r() * (zona.r1 - zona.r0), tono = r();
      // Fuera de los ojos: una mota encima del iris se lee como una basura.
      const ojo = Math.min(...[-0.37, 0.37].map(ou => ((u - ou) / 0.38) ** 2 + ((v - 0.38) / 0.22) ** 2));
      if (ojo > 1) out.push([u, v, radio, tono]);
    }
    return out;
  }

  /* Ojo de anillos: una cuenca oscura y adentro anillos concéntricos, como el
     corte de un tronco o una piedra pulida. La cuenca queda quieta y lo de
     adentro sigue la mirada: así se lee que el ojo mira, no que se mueve la
     talla. Cerrado, es una ranura tallada: un trazo oscuro con el filo claro
     de la materia debajo; el negro del ojo de Boberto se leía pintado encima. */
  function ojoAnillos(T) {
    return function (c, P, e) {
      if (e.forma === 'shut' || e.forma === 'happy') {
        ranuraTallada(c, e, T.ranura, T.filo);
        return true;
      }
      if (FORMAS_DE_BOBERTO[e.forma]) return false;
      const k = aperturaOjo(e);
      c.save();
      c.translate(e.x - e.gx, e.y - e.gy);
      c.scale(1, k);
      const cuenca = c.createRadialGradient(0, 0, 0.4, 0, 0, 2.3);
      cuenca.addColorStop(0, T.fondo);
      cuenca.addColorStop(1, T.cuenca);
      ellipsePath(c, 0, 0, e.rx * 1.2, e.ry * 1.1);
      c.fillStyle = cuenca; c.fill();
      c.strokeStyle = T.borde; c.lineWidth = 0.16; c.stroke();
      c.clip();
      const gx = e.gx * 0.9, gy = e.gy * 0.9;
      llenarElipse(c, gx, gy, 1.48, 1.56, T.anillo);
      llenarElipse(c, gx, gy, 1.18, 1.25, T.iris);
      ellipsePath(c, gx, gy, 0.92, 0.97);
      c.strokeStyle = T.linea; c.lineWidth = 0.12; c.stroke();
      llenarElipse(c, gx, gy, 0.68, 0.72, T.pupila);
      llenarElipse(c, gx - 0.55, gy - 0.62, 0.42, 0.38, withA('#ffffff', 0.92));
      llenarElipse(c, gx + 0.48, gy + 0.50, 0.16, 0.14, withA('#ffffff', 0.70));
      c.restore();
      return true;
    };
  }
  function ranuraTallada(c, e, oscuro, filo) {
    const feliz = e.forma === 'happy', w = e.rx * 1.05, x = e.x, y = e.y;
    const arco = (dy) => {
      c.beginPath();
      c.moveTo(x - w, y + dy + (feliz ? 0.55 : 0));
      c.quadraticCurveTo(x, y + dy + (feliz ? -1.15 : 0.95), x + w, y + dy + (feliz ? 0.55 : 0));
    };
    c.save(); c.lineCap = 'round';
    arco(0.32); c.strokeStyle = withA(filo, 0.75); c.lineWidth = 0.22; c.stroke();
    arco(0); c.strokeStyle = oscuro; c.lineWidth = 0.62; c.stroke();
    c.restore();
  }

  // ── Madera y lianas: nogal veteado con lianas turquesa que brillan ──
  const MADERA = {
    claro: '#e0b483', oscuro: '#3b2112', nudo: '#2a160a',
    liana: '#3fe3c8', lianaLuz: '#c4fff3',
  };
  const OJO_MADERA = {
    fondo: '#1a0d05', cuenca: '#5a361d', borde: '#24130a',
    anillo: '#e6c89c', iris: '#4a2a14', linea: '#a8794a', pupila: '#0b0603',
    ranura: '#2a160a', filo: MADERA.claro,
  };
  // [u de partida, amplitud del vaivén, fase, ¿veta clara?, grosor]
  const VETAS_MADERA = [
    [-0.86, 0.05, 0.4, 0, 0.22], [-0.70, 0.07, 1.3, 1, 0.14], [-0.52, 0.06, 2.1, 0, 0.30],
    [-0.36, 0.08, 0.2, 1, 0.20], [-0.18, 0.05, 2.8, 0, 0.16], [0.00, 0.07, 1.7, 1, 0.26],
    [0.16, 0.06, 0.9, 0, 0.22], [0.34, 0.09, 2.4, 1, 0.14], [0.52, 0.06, 1.1, 0, 0.28],
    [0.70, 0.07, 0.5, 1, 0.18], [0.86, 0.05, 2.0, 0, 0.20],
  ];
  // El nudo, abajo a la derecha: las vetas cercanas se abren para rodearlo.
  const NUDO_MADERA = { u: 0.46, v: 0.76 };
  const LIANAS_MADERA = [
    [[-0.22, 1.02], [-0.55, 0.95], [-0.82, 0.82], [-0.80, 0.64], [-0.79, 0.56], [-0.71, 0.53], [-0.68, 0.58]],
    [[-0.52, 0.95], [-0.42, 0.87], [-0.31, 0.82], [-0.34, 0.75], [-0.37, 0.71], [-0.45, 0.73], [-0.42, 0.78]],
    [[0.20, 1.02], [0.54, 0.96], [0.82, 0.84], [0.80, 0.66], [0.79, 0.57], [0.71, 0.54], [0.68, 0.59]],
    [[0.56, 0.94], [0.48, 0.86], [0.36, 0.84], [0.30, 0.89], [0.27, 0.93], [0.33, 0.95], [0.36, 0.92]],
    [[-0.42, 0.14], [-0.26, 0.03], [0.04, 0.03], [0.13, 0.08], [0.19, 0.12], [0.11, 0.16], [0.06, 0.12]],
    [[0.50, 0.07], [0.42, 0.12], [0.34, 0.12], [0.33, 0.08], [0.32, 0.05], [0.37, 0.04], [0.39, 0.07]],
  ];
  // Hojas: [u, v, ángulo hacia donde apunta, largo en unidades].
  const HOJAS_MADERA = [
    [-0.62, 0.92, -2.4, 1.35], [-0.81, 0.74, -2.0, 1.2], [-0.70, 0.88, 2.7, 0.95],
    [-0.40, 0.85, -1.2, 0.9], [-0.79, 0.62, -2.6, 0.8],
    [0.62, 0.93, -0.7, 1.35], [0.81, 0.75, -1.1, 1.2], [0.42, 0.97, 0.2, 0.95],
    [0.79, 0.63, -0.5, 0.8],
    [-0.30, 0.09, -2.7, 0.85], [0.10, 0.04, -0.4, 0.7],
  ];
  // Zarcillos: rulos chicos que salen de las lianas. [u, v, radio, sentido]
  const ZARCILLOS_MADERA = [
    [-0.62, 0.50, 0.42, 1], [0.62, 0.51, 0.42, -1], [-0.46, 0.99, 0.36, -1],
    [0.12, 0.99, 0.34, 1], [-0.50, 0.11, 0.30, -1],
  ];
  const MOTAS_MADERA = [[0.06, 0.88], [-0.12, 0.80], [0.60, 0.70], [-0.58, 0.68], [0.55, 0.20], [-0.55, 0.20]];

  function trazoLiana(c, ancho, trazar) {
    trazoBrillo(c, ancho, trazar, MADERA.liana, MADERA.lianaLuz, 0.55);
  }

  function texturaMadera(c, P, pose) {
    const m = marcoMaterial(pose);
    const fina = texturaFina();
    c.save(); blobPath(c, m.cx, m.top, m.bot, m.rx); c.clip();

    // Vetas: semitransparentes, para que el volumen de drawBody siga debajo.
    // La veta RODEA lo que encuentra —los ojos y el nudo— como rodea la
    // madera de verdad una rama: paralelas y derechas se leían como un tonel.
    // Entre veta y veta va una franja de otro tono (el anillo de un año).
    const vetaU = (u0, amp, fase, v) => {
      let u = (u0 + amp * Math.sin(v * 5.2 + fase)) * anchoMeridiano(v, 0.55);
      [[-0.37, 0.38, 0.30], [0.37, 0.38, 0.30], [NUDO_MADERA.u, NUDO_MADERA.v, 0.16]].forEach(([ou, ov, f]) => {
        const du = u - ou, dv = (v - ov) * 1.5;
        u += Math.sign(du || 1) * f * Math.exp(-(du * du + dv * dv) / (f * 0.22));
      });
      return u;
    };
    if (fina) {
      VETAS_MADERA.forEach(([u0, amp, fase], i) => {
        if (i % 2 || i + 1 >= VETAS_MADERA.length) return;
        const [u1, amp1, fase1] = VETAS_MADERA[i + 1];
        c.beginPath();
        for (let v = -0.02; v <= 1.03; v += 0.05) c.lineTo(m.X(vetaU(u0, amp, fase, v)), m.Y(v));
        for (let v = 1.03; v >= -0.02; v -= 0.05) c.lineTo(m.X(vetaU(u1, amp1, fase1, v)), m.Y(v));
        c.closePath();
        c.fillStyle = withA(MADERA.oscuro, 0.13); c.fill();
      });
    }
    VETAS_MADERA.forEach(([u0, amp, fase, clara, ancho], i) => {
      if (!fina && i % 2) return;
      c.beginPath();
      for (let v = -0.02; v <= 1.03; v += 0.04) {
        const u = vetaU(u0, amp, fase, v);
        v <= -0.02 ? c.moveTo(m.X(u), m.Y(v)) : c.lineTo(m.X(u), m.Y(v));
      }
      c.strokeStyle = clara ? withA(MADERA.claro, 0.34) : withA(MADERA.oscuro, 0.46);
      c.lineWidth = fina ? ancho : ancho * 1.6;
      c.stroke();
      // Poros: rayitas cortas sobre la veta oscura.
      if (fina && !clara) {
        for (let v = 0.08 + (i % 3) * 0.05; v < 0.98; v += 0.13) {
          const u = vetaU(u0, amp, fase, v) + 0.035;
          c.beginPath(); c.moveTo(m.X(u), m.Y(v)); c.lineTo(m.X(u), m.Y(v + 0.035));
          c.strokeStyle = withA(MADERA.nudo, 0.4); c.lineWidth = 0.07; c.stroke();
        }
      }
    });

    // El nudo: el corazón oscuro y anillos alrededor, con la luz en el canto.
    const nx = m.X(NUDO_MADERA.u), ny = m.Y(NUDO_MADERA.v);
    const ng = c.createRadialGradient(nx, ny, 0.05, nx, ny, 0.9);
    ng.addColorStop(0, withA(MADERA.nudo, 0.75)); ng.addColorStop(1, withA(MADERA.nudo, 0));
    llenarElipse(c, nx, ny, 0.9, 0.65, ng);
    [[1.0, 0.32], [0.7, 0.42], [0.42, 0.6]].forEach(([r, a]) => {
      ellipsePath(c, nx, ny, r, r * 0.72);
      c.strokeStyle = withA(MADERA.nudo, a); c.lineWidth = 0.14; c.stroke();
      c.beginPath(); c.ellipse(nx, ny + 0.08, r, r * 0.72, 0, 0.15 * Math.PI, 0.85 * Math.PI);
      c.strokeStyle = withA(MADERA.claro, 0.25); c.lineWidth = 0.06; c.stroke();
    });

    // Barniz: un brillo suave que pasa POR ENCIMA de la veta.
    const gg = c.createRadialGradient(m.X(-0.3), m.Y(0.16), 0.2, m.X(-0.3), m.Y(0.16), m.rx * 0.8);
    gg.addColorStop(0, withA('#fff4e0', 0.30));
    gg.addColorStop(1, withA('#fff4e0', 0));
    c.fillStyle = gg; c.fillRect(m.cx - m.rx, m.top, m.rx * 2, m.h);

    // Lianas, hojas y motas: lo que brilla va último, sin velos encima.
    LIANAS_MADERA.forEach(t => trazoLiana(c, fina ? 0.32 : 0.38, () => trazarTramos(c, m, t)));
    if (fina) {
      HOJAS_MADERA.forEach(([u, v, a, l]) =>
        hojaMaterial(c, m.X(u), m.Y(v), a, l, withA(MADERA.liana, 0.5), (f) => trazoLiana(c, 0.12, f)));
      ZARCILLOS_MADERA.forEach(([u, v, r, sentido]) =>
        trazoLiana(c, 0.12, () => trazarEspiral(c, m.X(u), m.Y(v), r, 1.4, sentido)));
      MOTAS_MADERA.forEach(([u, v]) => {
        c.save(); c.shadowColor = MADERA.liana; c.shadowBlur = _k * 0.4;
        llenarElipse(c, m.X(u), m.Y(v), 0.13, 0.13, MADERA.lianaLuz);
        c.restore();
      });
    }
    c.restore();
    enPies(c, pose, (x, y, lado) => {
      for (let i = -1; i <= 1; i++) {
        c.beginPath(); c.moveTo(x - 1.2, y + i * 0.32); c.quadraticCurveTo(x, y + i * 0.32 + 0.2, x + 1.2, y + i * 0.32);
        c.strokeStyle = withA(MADERA.oscuro, 0.45); c.lineWidth = 0.1; c.stroke();
      }
      trazoLiana(c, 0.12, () => trazarEspiral(c, x + lado * 0.35, y + 0.15, 0.42, 1.2, lado));
    });
  }

  // ── Madera tallada: una sola pieza con relieves en espiral ──
  //
  // Un surco tallado, con la luz de siempre (arriba a la izquierda), tiene la
  // pared de arriba en sombra y la de abajo iluminada: por eso cada surco es
  // un canal oscuro con el labio claro corrido ABAJO a la derecha. Al revés
  // —el claro arriba— el ojo lo lee como un cordón pegado encima, y así se
  // veía antes: una madera oscura con dibujos marrones, sin un solo relieve.
  // La madera ahora es miel y no nogal, porque sobre un fondo casi negro
  // ninguna sombra tiene dónde caer.
  const TALLA = { surco: '#2a170a', hondo: '#160b04', filo: '#f3cf9a', veta: '#7a4c26' };
  const SURCOS_TALLA = [
    // Llamas que suben desde la panza por los costados.
    [[0.00, 1.02], [-0.25, 0.92], [-0.55, 0.96], [-0.70, 0.84], [-0.84, 0.72], [-0.80, 0.60], [-0.70, 0.62]],
    [[0.00, 1.02], [0.25, 0.92], [0.55, 0.96], [0.70, 0.84], [0.84, 0.72], [0.80, 0.60], [0.70, 0.62]],
    [[-0.18, 1.02], [-0.32, 0.90], [-0.50, 0.86], [-0.52, 0.76], [-0.54, 0.68], [-0.44, 0.66], [-0.42, 0.72]],
    [[0.18, 1.02], [0.32, 0.90], [0.50, 0.86], [0.52, 0.76], [0.54, 0.68], [0.44, 0.66], [0.42, 0.72]],
    // Costados altos, por fuera de los ojos.
    [[-0.92, 0.30], [-0.84, 0.38], [-0.82, 0.46], [-0.86, 0.54]],
    [[0.92, 0.30], [0.84, 0.38], [0.82, 0.46], [0.86, 0.54]],
  ];
  // Espirales: [u, v, radio, vueltas, sentido].
  const ESPIRALES_TALLA = [[-0.28, 0.82, 0.8, 1.7, 1], [0.28, 0.82, 0.8, 1.7, -1]];
  const HOJAS_TALLA = [
    [-0.66, 0.93, -2.6, 1.15], [0.66, 0.93, -0.55, 1.15], [-0.86, 0.66, -2.0, 0.85],
    [0.86, 0.66, -1.15, 0.85],
  ];
  // La guarda de la frente: talla de viruta, triángulos hundidos en fila.
  // [u del centro, v de la base, punta para arriba (1) o abajo (-1)]
  const VIRUTAS_TALLA = [];
  for (let i = -6; i <= 6; i++) VIRUTAS_TALLA.push([i * 0.09, 0.15, i % 2 ? -1 : 1]);

  function tallar(c, ancho, trazar) {
    c.save(); c.lineCap = 'round'; c.lineJoin = 'round';
    c.translate(0.13, 0.15); trazar();
    c.strokeStyle = withA(TALLA.filo, 0.9); c.lineWidth = ancho * 0.75; c.stroke();
    c.translate(-0.13, -0.15); trazar();
    c.strokeStyle = withA(TALLA.surco, 0.95); c.lineWidth = ancho; c.stroke();
    if (ancho >= 0.3) {
      c.translate(-0.03, -0.04); trazar();
      c.strokeStyle = withA(TALLA.hondo, 0.9); c.lineWidth = ancho * 0.4; c.stroke();
    }
    c.restore();
  }

  // Un triángulo de talla de viruta: tres facetas que bajan hacia el centro.
  // La que mira arriba a la izquierda recibe la luz, las otras dos no.
  function virutaTalla(c, x, y, lado, alto, sentido) {
    const p = [[x - lado / 2, y], [x + lado / 2, y], [x, y - alto * sentido]];
    const ox = x, oy = y - alto * sentido * 0.38;
    const tonos = sentido > 0
      ? [withA(TALLA.hondo, 0.75), withA(TALLA.surco, 0.55), withA(TALLA.filo, 0.55)]
      : [withA(TALLA.filo, 0.55), withA(TALLA.hondo, 0.75), withA(TALLA.surco, 0.55)];
    for (let i = 0; i < 3; i++) {
      const a = p[i], b = p[(i + 1) % 3];
      c.beginPath(); c.moveTo(a[0], a[1]); c.lineTo(b[0], b[1]); c.lineTo(ox, oy); c.closePath();
      c.fillStyle = tonos[i]; c.fill();
    }
  }

  function texturaTalla(c, P, pose) {
    const m = marcoMaterial(pose);
    const fina = texturaFina();
    c.save(); blobPath(c, m.cx, m.top, m.bot, m.rx); c.clip();

    // Veta de fondo: la talla manda, la veta sólo dice "madera".
    VETAS_MADERA.forEach(([u0, amp, fase], i) => {
      if (!fina && i % 2) return;
      c.beginPath();
      for (let v = -0.02; v <= 1.03; v += 0.05) {
        const u = (u0 + amp * Math.sin(v * 4 + fase)) * anchoMeridiano(v, 0.55);
        v <= -0.02 ? c.moveTo(m.X(u), m.Y(v)) : c.lineTo(m.X(u), m.Y(v));
      }
      c.strokeStyle = withA(TALLA.veta, i % 2 ? 0.22 : 0.36); c.lineWidth = i % 2 ? 0.08 : 0.16; c.stroke();
    });

    const ancho = fina ? 0.42 : 0.55;
    SURCOS_TALLA.forEach((t) => tallar(c, ancho, () => trazarTramos(c, m, t)));
    ESPIRALES_TALLA.forEach(([u, v, r, vueltas, sentido]) =>
      tallar(c, ancho * 0.85, () => trazarEspiral(c, m.X(u), m.Y(v), r, vueltas, sentido)));
    if (fina) {
      // La guarda de la frente, entre dos filetes.
      VIRUTAS_TALLA.forEach(([u, v, sentido]) => {
        const lado = 0.18 * m.rx * anchoMeridiano(v, 0.5), alto = 0.95;
        virutaTalla(c, m.X(u * anchoMeridiano(v, 0.5)), m.Y(v) - (sentido < 0 ? alto : 0), lado, alto, sentido);
      });
      [0.055, 0.165].forEach(v => tallar(c, 0.14, () => {
        c.beginPath();
        c.moveTo(m.X(-0.62), m.Y(v + 0.04));
        c.quadraticCurveTo(m.X(0), m.Y(v - 0.025), m.X(0.62), m.Y(v + 0.04));
      }));
      // Hojas hundidas: el fondo oscuro es lo que las separa del relieve.
      HOJAS_TALLA.forEach(([u, v, a, l]) =>
        hojaMaterial(c, m.X(u), m.Y(v), a, l, withA(TALLA.surco, 0.45), (f) => tallar(c, 0.18, f)));
      // Una cuenta en relieve en el corazón de cada espiral.
      ESPIRALES_TALLA.forEach(([u, v]) => {
        llenarElipse(c, m.X(u) + 0.07, m.Y(v) + 0.07, 0.24, 0.22, withA(TALLA.hondo, 0.7));
        llenarElipse(c, m.X(u), m.Y(v), 0.2, 0.18, '#c48a52');
        llenarElipse(c, m.X(u) - 0.06, m.Y(v) - 0.06, 0.08, 0.07, withA(TALLA.filo, 0.9));
      });
    }
    // Lustre de cera: la madera tallada brilla en los relieves, no en los surcos.
    const gg = c.createRadialGradient(m.X(-0.32), m.Y(0.18), 0.2, m.X(-0.32), m.Y(0.18), m.rx * 0.75);
    gg.addColorStop(0, withA('#fff1d6', 0.28));
    gg.addColorStop(1, withA('#fff1d6', 0));
    c.fillStyle = gg; c.fillRect(m.cx - m.rx, m.top, m.rx * 2, m.h);
    c.restore();
    enPies(c, pose, (x, y, lado) =>
      tallar(c, 0.18, () => trazarEspiral(c, x, y + 0.1, 0.55, 1.3, lado)));
  }

  // ── Gemas: un mosaico de piedras talladas, engarzadas en oro ──
  //
  // Lo que separa una gema de un vidrio pintado es cómo pasa la luz: el
  // borde se oscurece (la piedra es gruesa), la luz entra arriba a la
  // izquierda como un reflejo duro y SALE abajo a la derecha como un
  // resplandor del color de la piedra. La versión anterior tenía tonos
  // pastel y un brillo blando por piedra, y se leía como un acolchado.
  //
  // Las piedras son franjas que convergen como meridianos, partidas en filas
  // distintas por columna para que las juntas no formen una grilla. Cada una
  // lleva talla de pirámide: cuatro facetas hacia una mesa central.
  const GEMA = {
    amatista:   ['#e2c6ff', '#8d4fe0', '#33106b'],
    zafiro:     ['#b9d3ff', '#2f66e0', '#0b1f5e'],
    esmeralda:  ['#b4f7d2', '#16a862', '#053f24'],
    rubi:       ['#ffc0cc', '#de1f48', '#520515'],
    topacio:    ['#ffe6b0', '#f2a11c', '#6e3a04'],
    aguamarina: ['#d6fdff', '#36c6d9', '#0b4d58'],
  };
  const GEMAS = [
    { u0: -1.4, u1: -0.58, v0: -0.1, v1: 0.36, t: GEMA.amatista },
    { u0: -1.4, u1: -0.58, v0: 0.36, v1: 0.72, t: GEMA.zafiro },
    { u0: -1.4, u1: -0.58, v0: 0.72, v1: 1.1, t: GEMA.esmeralda },
    { u0: -0.58, u1: -0.2, v0: -0.1, v1: 0.62, t: GEMA.aguamarina },
    { u0: -0.58, u1: -0.2, v0: 0.62, v1: 1.1, t: GEMA.rubi },
    { u0: -0.2, u1: 0.2, v0: -0.1, v1: 0.24, t: GEMA.topacio },
    { u0: -0.2, u1: 0.2, v0: 0.24, v1: 0.68, t: GEMA.esmeralda },
    { u0: -0.2, u1: 0.2, v0: 0.68, v1: 1.1, t: GEMA.zafiro },
    { u0: 0.2, u1: 0.58, v0: -0.1, v1: 0.58, t: GEMA.aguamarina },
    { u0: 0.2, u1: 0.58, v0: 0.58, v1: 1.1, t: GEMA.amatista },
    { u0: 0.58, u1: 1.4, v0: -0.1, v1: 0.30, t: GEMA.esmeralda },
    { u0: 0.58, u1: 1.4, v0: 0.30, v1: 0.70, t: GEMA.rubi },
    { u0: 0.58, u1: 1.4, v0: 0.70, v1: 1.1, t: GEMA.topacio },
  ];
  const ORO = { trazo: '#d9b25f', luz: '#fff0bf', hondo: '#6b4a14' };
  const JUNTA_GEMA = '#1d1524';
  // El borde de una franja ondula apenas: recto parecía un gajo de naranja.
  const bordeGema = (u, v) => u + 0.04 * Math.sin(v * 7 + u * 10);
  const xGema = (m, u, v) => m.X(bordeGema(u, v) * anchoMeridiano(v, 0.3));
  function pathGema(c, m, g) {
    c.beginPath();
    for (let v = g.v0; v <= g.v1 + 1e-6; v += 0.05) {
      v === g.v0 ? c.moveTo(xGema(m, g.u0, v), m.Y(v)) : c.lineTo(xGema(m, g.u0, v), m.Y(v));
    }
    for (let v = g.v1; v >= g.v0 - 1e-6; v -= 0.05) c.lineTo(xGema(m, g.u1, v), m.Y(v));
    c.closePath();
  }
  // Las cuatro esquinas VISIBLES de la piedra: lo que cae fuera de la
  // silueta se recorta a su borde, así la talla converge donde se ve.
  function esquinasGema(m, g) {
    const v0 = Math.max(g.v0, 0.03), v1 = Math.min(g.v1, 0.97);
    const u0 = Math.max(g.u0, -0.97), u1 = Math.min(g.u1, 0.97);
    return {
      pts: [[u0, v0], [u1, v0], [u1, v1], [u0, v1]].map(([u, v]) => [xGema(m, u, v), m.Y(v)]),
      cu: (u0 + u1) / 2, cv: (v0 + v1) / 2,
    };
  }
  // Uniones donde se tocan tres piedras: un grano de oro tapa el cruce.
  const GRANOS_GEMA = [
    [-0.58, 0.36], [-0.58, 0.72], [-0.58, 0.62], [-0.2, 0.62], [-0.2, 0.24], [-0.2, 0.68],
    [0.2, 0.24], [0.2, 0.68], [0.2, 0.58], [0.58, 0.58], [0.58, 0.30], [0.58, 0.70],
  ];

  function texturaGemas(c, P, pose) {
    const m = marcoMaterial(pose);
    const fina = texturaFina();
    c.save(); blobPath(c, m.cx, m.top, m.bot, m.rx); c.clip();

    GEMAS.forEach((g, n) => {
      const { pts, cu, cv } = esquinasGema(m, g);
      const mx = m.X(cu * anchoMeridiano(cv, 0.3)), my = m.Y(cv);
      const tam = Math.max(Math.abs(pts[1][0] - pts[0][0]), pts[3][1] - pts[0][1]);
      c.save();
      pathGema(c, m, g);
      // Cuerpo: oscuro al borde, saturado al medio y el resplandor de salida
      // corrido abajo a la derecha.
      const lg = c.createRadialGradient(mx + tam * 0.12, my + tam * 0.16, 0.05, mx, my, tam * 0.75);
      lg.addColorStop(0, mixHex(g.t[1], g.t[0], 0.55));
      lg.addColorStop(0.45, g.t[1]);
      lg.addColorStop(1, g.t[2]);
      c.fillStyle = lg; c.fill();
      c.clip();
      if (fina) {
        // Talla de pirámide: cuatro facetas hacia una mesa al centro. La de
        // arriba y la de la izquierda reciben la luz; las otras dos no.
        const mesa = pts.map(([x, y]) => [mx + (x - mx) * 0.42, my + (y - my) * 0.42]);
        const tonos = [0.30, -0.18, -0.32, 0.12];
        for (let i = 0; i < 4; i++) {
          const j = (i + 1) % 4;
          c.beginPath();
          c.moveTo(pts[i][0], pts[i][1]); c.lineTo(pts[j][0], pts[j][1]);
          c.lineTo(mesa[j][0], mesa[j][1]); c.lineTo(mesa[i][0], mesa[i][1]);
          c.closePath();
          c.fillStyle = tonos[i] > 0 ? withA('#ffffff', tonos[i]) : withA(g.t[2], -tonos[i]);
          c.fill();
          c.strokeStyle = withA(g.t[0], 0.45); c.lineWidth = 0.05; c.stroke();
        }
        c.beginPath();
        mesa.forEach(([x, y], i) => i ? c.lineTo(x, y) : c.moveTo(x, y));
        c.closePath();
        c.fillStyle = withA(g.t[0], 0.28); c.fill();
      }
      // Sombra interna del engarce: la piedra es gruesa en el borde.
      pathGema(c, m, g);
      c.strokeStyle = withA(g.t[2], 0.6); c.lineWidth = 0.55; c.stroke();
      // El reflejo duro, arriba a la izquierda. En una de cada dos piedras:
      // repetido en todas se leía como un estampado de rayitas.
      if (n % 2 === 0) {
        const [ax, ay] = pts[0];
        c.translate(ax + (mx - ax) * 0.45, ay + (my - ay) * 0.4);
        c.rotate(-0.6);
        ellipsePath(c, 0, 0, Math.min(0.42, tam * 0.16), Math.min(0.13, tam * 0.05));
        c.fillStyle = withA('#ffffff', 0.85); c.fill();
      }
      c.restore();
    });

    volumenMaterial(c, m, 0.3);

    // Engarce: la junta oscura y encima el filete de oro con su canto de luz.
    GEMAS.forEach(g => {
      pathGema(c, m, g);
      c.strokeStyle = JUNTA_GEMA; c.lineWidth = fina ? 0.3 : 0.4; c.stroke();
    });
    if (fina) {
      GEMAS.forEach(g => {
        pathGema(c, m, g);
        c.strokeStyle = ORO.hondo; c.lineWidth = 0.16; c.stroke();
        c.strokeStyle = ORO.trazo; c.lineWidth = 0.1; c.stroke();
      });
      GRANOS_GEMA.forEach(([u, v]) => {
        const x = xGema(m, u, v), y = m.Y(v);
        llenarElipse(c, x, y, 0.2, 0.2, ORO.hondo);
        llenarElipse(c, x - 0.02, y - 0.02, 0.16, 0.16, ORO.trazo);
        llenarElipse(c, x - 0.06, y - 0.07, 0.07, 0.07, ORO.luz);
      });
      [[-0.84, 0.5, 0.42], [0.74, 0.44, 0.38], [-0.4, 0.86, 0.3], [0.42, 0.8, 0.34]]
        .forEach(([u, v, r]) => destello(c, m.X(u), m.Y(v), r, '#ffffff', 0.6));
    }
    c.restore();
    enPies(c, pose, (x, y, lado) => {
      c.beginPath(); c.moveTo(x - 1.2, y - 0.05); c.lineTo(x + 1.2, y - 0.05);
      c.strokeStyle = ORO.hondo; c.lineWidth = 0.2; c.stroke();
      c.strokeStyle = ORO.trazo; c.lineWidth = 0.12; c.stroke();
      llenarElipse(c, x + 0.35 * lado, y + 0.35, 0.3, 0.2, withA('#ffffff', 0.18));
      destello(c, x - 0.35 * lado, y + 0.3, 0.26, '#ffffff', 0.4);
    });
  }

  /* Ojo de diamante: un brillante visto de frente, con un engarce dorado.
     Las facetas son ocho triángulos alrededor de la mesa, alternando luz y
     sombra; con todas iguales se leía como un botón. */
  function ojoGema(c, P, e) {
    if (e.forma === 'shut' || e.forma === 'happy') {
      ranuraTallada(c, e, JUNTA_GEMA, ORO.trazo);
      return true;
    }
    if (FORMAS_DE_BOBERTO[e.forma]) return false;
    const k = aperturaOjo(e);
    c.save();
    c.translate(e.x - e.gx, e.y - e.gy);
    c.scale(1, k);
    ellipsePath(c, 0, 0, e.rx * 1.18, e.ry * 1.06);
    c.fillStyle = '#2a2234'; c.fill();
    c.strokeStyle = ORO.trazo; c.lineWidth = 0.24; c.stroke();
    c.clip();
    const gx = e.gx * 0.7, gy = e.gy * 0.7, R = 1.85;
    const g = c.createRadialGradient(gx - 0.5, gy - 0.6, 0.2, gx, gy, R);
    g.addColorStop(0, '#ffffff'); g.addColorStop(0.55, '#d6d2ee'); g.addColorStop(1, '#6d6894');
    llenarElipse(c, gx, gy, R, R * 1.05, g);
    const n = 8, mesa = 0.78;
    for (let i = 0; i < n; i++) {
      const a0 = (i / n) * Math.PI * 2 - Math.PI / 8, a1 = ((i + 1) / n) * Math.PI * 2 - Math.PI / 8;
      c.beginPath();
      c.moveTo(gx + Math.cos(a0) * mesa, gy + Math.sin(a0) * mesa);
      c.lineTo(gx + Math.cos(a0) * R, gy + Math.sin(a0) * R * 1.05);
      c.lineTo(gx + Math.cos(a1) * R, gy + Math.sin(a1) * R * 1.05);
      c.lineTo(gx + Math.cos(a1) * mesa, gy + Math.sin(a1) * mesa);
      c.closePath();
      c.fillStyle = i % 2 ? withA('#ffffff', 0.38) : withA('#3a3260', 0.30); c.fill();
      c.strokeStyle = withA('#ffffff', 0.55); c.lineWidth = 0.06; c.stroke();
    }
    c.beginPath();
    for (let i = 0; i < n; i++) {
      const a = (i / n) * Math.PI * 2 - Math.PI / 8;
      i ? c.lineTo(gx + Math.cos(a) * mesa, gy + Math.sin(a) * mesa) : c.moveTo(gx + Math.cos(a) * mesa, gy + Math.sin(a) * mesa);
    }
    c.closePath();
    c.fillStyle = withA('#f4f2ff', 0.75); c.fill();
    llenarElipse(c, gx - 0.55, gy - 0.65, 0.40, 0.34, withA('#ffffff', 0.95));
    llenarElipse(c, gx + 0.55, gy + 0.55, 0.15, 0.13, withA('#ffffff', 0.8));
    c.restore();
    return true;
  }

  // ── Piedra y musgo: canto rodado con musgo arriba y abajo ──
  const PIEDRA = { musgo: ['#3f6b25', '#5f8f3a', '#8fbf55'], grieta: '#2b2f33', flor: '#f4e6f0' };
  const OJO_PIEDRA = {
    fondo: '#121518', cuenca: '#4b5157', borde: '#202428',
    anillo: '#d3d9de', iris: '#3b444d', linea: '#8a96a1', pupila: '#07090b',
    ranura: '#23272b', filo: '#c6ccd2',
  };
  // Planos de roca: lascas más claras con el canto inferior oscuro.
  const LASCAS_PIEDRA = [
    [[-0.58, 0.64], [-0.30, 0.60], [-0.36, 0.76], [-0.60, 0.78]],
    [[0.40, 0.70], [0.66, 0.64], [0.72, 0.82], [0.46, 0.87]],
    [[-0.92, 0.30], [-0.78, 0.22], [-0.76, 0.44], [-0.94, 0.48]],
  ];
  // Liquen: anillos pálidos. [u, v, radio]
  const LIQUEN_PIEDRA = [[0.82, 0.46, 0.38], [-0.18, 0.80, 0.32], [0.18, 0.72, 0.2]];
  // Manchas de la roca: [u, v, radio en unidades, alfa (negativo = oscura)].
  const MANCHAS_PIEDRA = [
    [-0.6, 0.3, 3.2, 0.16], [0.55, 0.55, 2.6, -0.2], [-0.2, 0.85, 2.8, -0.16],
    [0.7, 0.15, 2.0, 0.12], [0.1, 0.7, 1.6, 0.1],
  ];
  const MOTAS_PIEDRA = motasFijas(11, 70, { u0: -1, u1: 1, v0: 0, v1: 1, r0: 0.07, r1: 0.18 });
  // Grietas: polilíneas que esquivan la cara.
  const GRIETAS_PIEDRA = [
    [[-1.0, 0.62], [-0.82, 0.66], [-0.72, 0.76], [-0.60, 0.78], [-0.52, 0.88]],
    [[1.0, 0.40], [0.86, 0.50], [0.80, 0.62], [0.66, 0.66]],
    [[0.30, 0.0], [0.24, 0.08], [0.30, 0.14]],
  ];
  // Matas de musgo: [u, v, radio de la mata, cantidad de bolitas].
  const MATAS_PIEDRA = [
    [-0.10, 0.02, 1.9, 26], [0.40, 0.05, 1.2, 14], [-0.50, 0.06, 1.0, 10],
    [-0.42, 0.93, 1.7, 24], [0.38, 0.94, 1.5, 22], [-0.93, 0.80, 0.9, 10], [0.92, 0.76, 0.8, 9],
  ];
  const BOLITAS_PIEDRA = MATAS_PIEDRA.map(([u, v, r, n], i) => {
    const out = [];
    let s = 31 + i * 97;
    const rnd = () => { s = (s * 16807) % 2147483647; return (s - 1) / 2147483646; };
    for (let j = 0; j < n; j++) {
      const a = rnd() * Math.PI * 2, d = Math.sqrt(rnd()) * r;
      out.push([Math.cos(a) * d, Math.sin(a) * d * 0.55, 0.22 + rnd() * 0.22, Math.floor(rnd() * 3)]);
    }
    return out;
  });

  function texturaPiedra(c, P, pose) {
    const m = marcoMaterial(pose);
    const fina = texturaFina();
    c.save(); blobPath(c, m.cx, m.top, m.bot, m.rx); c.clip();
    // Manchas grandes de la roca: zonas más claras y más oscuras. Con un
    // gris parejo y motas encima se leía como plástico salpicado.
    MANCHAS_PIEDRA.forEach(([u, v, r, a]) => {
      const g = c.createRadialGradient(m.X(u), m.Y(v), 0.1, m.X(u), m.Y(v), r);
      const col = a > 0 ? '#e9edf0' : '#1f2327';
      g.addColorStop(0, withA(col, Math.abs(a))); g.addColorStop(1, withA(col, 0));
      c.fillStyle = g; c.fillRect(m.X(u) - r, m.Y(v) - r, r * 2, r * 2);
    });
    MOTAS_PIEDRA.forEach(([u, v, r, tono], i) => {
      if (!fina && i % 2) return;
      llenarElipse(c, m.X(u), m.Y(v), r, r * 0.85, tono > 0.55 ? withA('#e8ecef', 0.55) : withA('#24282c', 0.55));
    });
    if (fina) {
      LASCAS_PIEDRA.forEach(l => {
        trazarPolilinea(c, m, l); c.closePath();
        c.fillStyle = withA('#ffffff', 0.12); c.fill();
        trazarPolilinea(c, m, l.slice(2).concat([l[0]]));
        c.strokeStyle = withA('#1c1f22', 0.35); c.lineWidth = 0.12; c.stroke();
      });
      LIQUEN_PIEDRA.forEach(([u, v, r]) => {
        llenarElipse(c, m.X(u), m.Y(v), r, r * 0.8, withA('#d4d98a', 0.22));
        ellipsePath(c, m.X(u), m.Y(v), r, r * 0.8);
        c.strokeStyle = withA('#d4d98a', 0.65); c.lineWidth = 0.08; c.stroke();
      });
    }
    // Grietas con el labio de abajo iluminado: así se hunden en la roca.
    GRIETAS_PIEDRA.forEach(g => {
      c.save(); c.lineCap = 'round'; c.lineJoin = 'round';
      c.translate(0.06, 0.09); trazarPolilinea(c, m, g);
      c.strokeStyle = withA('#e4e8eb', 0.45); c.lineWidth = 0.1; c.stroke();
      c.translate(-0.06, -0.09); trazarPolilinea(c, m, g);
      c.strokeStyle = withA(PIEDRA.grieta, 0.85); c.lineWidth = 0.17; c.stroke();
      c.restore();
    });
    // La sombra que el musgo deja sobre la piedra: sin ella las matas
    // flotaban encima como pegatinas.
    MATAS_PIEDRA.forEach(([u, v, r], i) => {
      if (!fina && i > 2) return;
      llenarElipse(c, m.X(u) + 0.12, m.Y(v) + 0.22, r * 1.02, r * 0.6, withA('#15191c', 0.35));
    });
    // Musgo: bolitas de tres verdes, las más oscuras primero (son las de abajo).
    [0, 1, 2].forEach(tono => {
      MATAS_PIEDRA.forEach(([u, v], i) => {
        BOLITAS_PIEDRA[i].forEach(([dx, dy, r, t], j) => {
          if (t !== tono || (!fina && j % 2)) return;
          llenarElipse(c, m.X(u) + dx, m.Y(v) + dy, r, r * 0.9, PIEDRA.musgo[t]);
          if (t === 2 && fina) llenarElipse(c, m.X(u) + dx - r * 0.3, m.Y(v) + dy - r * 0.3, r * 0.3, r * 0.3, withA('#ffffff', 0.35));
        });
      });
    });
    if (fina) {
      // Pelusa: hebras cortas que se paran sobre las bolitas más claras.
      c.save(); c.lineCap = 'round';
      c.strokeStyle = withA('#b9e07a', 0.8); c.lineWidth = 0.05;
      MATAS_PIEDRA.forEach(([u, v], i) => {
        BOLITAS_PIEDRA[i].forEach(([dx, dy, r, t], j) => {
          if (t !== 2 || j % 2) return;
          const x = m.X(u) + dx, y = m.Y(v) + dy - r * 0.7;
          c.beginPath();
          c.moveTo(x, y); c.lineTo(x - 0.08, y - 0.24);
          c.moveTo(x + 0.1, y + 0.04); c.lineTo(x + 0.2, y - 0.18);
          c.stroke();
        });
      });
      c.restore();
      // Dos brotes de helecho asomando del musgo de la coronilla.
      [[-0.22, 0.0, -0.35], [0.5, 0.03, 0.4]].forEach(([u, v, a]) => {
        const x = m.X(u), y = m.Y(v);
        c.save(); c.translate(x, y); c.rotate(a);
        c.beginPath(); c.moveTo(0, 0.2); c.quadraticCurveTo(0.08, -0.4, -0.05, -0.75);
        c.strokeStyle = PIEDRA.musgo[0]; c.lineWidth = 0.09; c.lineCap = 'round'; c.stroke();
        [[-0.25, 0.22], [-0.48, 0.18]].forEach(([yy, l]) => [-1, 1].forEach(sd => {
          llenarElipse(c, sd * l * 0.6, yy, l * 0.7, 0.07, PIEDRA.musgo[2], sd * 0.5);
        }));
        trazarEspiral(c, -0.05, -0.82, 0.14, 1.1, 1);
        c.strokeStyle = PIEDRA.musgo[1]; c.lineWidth = 0.07; c.stroke();
        c.restore();
      });
    }
    if (fina) {
      // Dos florcitas sobre el musgo: cinco pétalos y un centro.
      [[0.30, 0.04], [-0.32, 0.97]].forEach(([u, v]) => {
        for (let p = 0; p < 5; p++) {
          const a = p / 5 * Math.PI * 2;
          llenarElipse(c, m.X(u) + Math.cos(a) * 0.2, m.Y(v) + Math.sin(a) * 0.2, 0.15, 0.15, PIEDRA.flor);
        }
        llenarElipse(c, m.X(u), m.Y(v), 0.11, 0.11, '#f2c14e');
      });
    }
    c.restore();
    enPies(c, pose, (x, y, lado) => {
      [[-0.5, 0.25], [-0.15, 0.4], [0.25, 0.3], [0.6, 0.2]].forEach(([dx, dy], i) =>
        llenarElipse(c, x + dx * lado, y + dy, 0.28, 0.24, PIEDRA.musgo[i % 3]));
    });
  }

  // Un hongo chico detrás de la cabeza: va en 'back' para que el cuerpo le
  // tape el pie y parezca que brota de la piedra.
  function hongoPiedra(c, P, pose) {
    const m = marcoMaterial(pose);
    const x = m.X(-0.48), y = m.top + 1.1;
    c.save(); c.lineCap = 'round';
    c.beginPath(); c.moveTo(x, y); c.quadraticCurveTo(x - 0.35, y - 1.0, x - 0.2, y - 1.7);
    c.strokeStyle = '#ece3cf'; c.lineWidth = 0.45; c.stroke();
    c.beginPath();
    c.ellipse(x - 0.2, y - 1.75, 1.05, 0.62, -0.15, Math.PI, 0);
    c.closePath();
    const g = c.createLinearGradient(0, y - 2.4, 0, y - 1.7);
    g.addColorStop(0, '#e8b98a'); g.addColorStop(1, '#a8673a');
    c.fillStyle = g; c.fill();
    c.strokeStyle = '#6b3d1e'; c.lineWidth = 0.12; c.stroke();
    llenarElipse(c, x - 0.55, y - 2.05, 0.16, 0.12, withA('#fff4e4', 0.85));
    llenarElipse(c, x + 0.25, y - 1.98, 0.12, 0.09, withA('#fff4e4', 0.85));
    c.restore();
  }

  // ── Kintsugi: cerámica rota y reparada con oro ──
  //
  // Esmalte crema arriba y un baño índigo que chorrea desde abajo, pintado
  // con olas de seigaiha. Las grietas son el centro del skin y son de METAL:
  // un oro que no brilla hacia afuera sino que refleja, con un canto oscuro,
  // bandas de luz y un filo blanco. Con halo se leían como un tubo de neón.
  const KINTSUGI = { indigo: '#2c4876', indigoLuz: '#56789f', craquelado: '#8f8572', cobalto: '#2a4a8c' };
  // Ojos de esmalte índigo con el filete de oro: el mismo baño de la panza.
  const OJO_KINTSUGI = {
    fondo: '#0b1322', cuenca: '#33507a', borde: '#1d2c44',
    anillo: '#f3ecdf', iris: '#2b3f60', linea: '#e2b84e', pupila: '#060a12',
    ranura: '#2b3f60', filo: '#ffffff',
  };
  const GRIETAS_KINTSUGI = [
    [[-1.0, 0.28], [-0.84, 0.48], [-0.66, 0.64], [-0.44, 0.70], [-0.34, 0.86], [-0.40, 1.05]],
    [[-0.66, 0.64], [-0.80, 0.80], [-0.76, 0.98]],
    [[0.34, -0.05], [0.22, 0.09], [0.04, 0.15], [-0.14, 0.11], [-0.30, 0.15]],
    [[1.0, 0.52], [0.78, 0.66], [0.56, 0.76], [0.44, 0.94], [0.52, 1.05]],
    [[0.56, 0.76], [0.72, 0.90], [0.68, 1.05]],
    [[0.74, 0.0], [0.62, 0.14], [0.70, 0.22]],
  ].map((t, i) => quebrar(t, 3, 0.05, 101 + i * 13));
  // Ramitas: grietas finas que se desprenden de las grandes.
  const RAMITAS_KINTSUGI = [
    [[-0.44, 0.70], [-0.30, 0.66], [-0.22, 0.70]],
    [[-0.84, 0.48], [-0.94, 0.56]],
    [[0.78, 0.66], [0.88, 0.76]],
    [[0.44, 0.94], [0.30, 0.96]],
    [[0.04, 0.15], [0.10, 0.04]],
  ].map((t, i) => quebrar(t, 2, 0.04, 307 + i * 11));
  const CRAQUELADO_KINTSUGI = [
    [[-0.60, 0.10], [-0.46, 0.16], [-0.50, 0.24]], [[0.10, 0.70], [0.20, 0.76], [0.16, 0.84]],
    [[0.62, 0.36], [0.72, 0.42], [0.80, 0.40]], [[-0.20, 0.64], [-0.10, 0.70]],
    [[-0.86, 0.36], [-0.78, 0.30]], [[0.88, 0.16], [0.80, 0.24]],
  ];
  // Rama de ciruelo pintada en cobalto, con sus flores. [u, v, radio]
  const FLORES_KINTSUGI = [[-0.72, 0.20, 0.5], [-0.84, 0.48, 0.34], [0.66, 0.12, 0.42], [0.86, 0.38, 0.3]];
  const RAMAS_KINTSUGI = [
    [[-0.95, 0.62], [-0.88, 0.48], [-0.86, 0.34], [-0.72, 0.20]],
    [[-0.88, 0.48], [-0.80, 0.42]],
    [[0.98, 0.50], [0.90, 0.38], [0.80, 0.22], [0.66, 0.12]],
  ];
  // Pan de oro: donde dos grietas se cruzan, la pieza perdió un pedacito.
  const PAN_DE_ORO = [[-0.66, 0.64, 0.48], [0.56, 0.76, 0.44], [0.04, 0.15, 0.32]];
  function florCobalto(c, x, y, r) {
    for (let p = 0; p < 5; p++) {
      const a = p / 5 * Math.PI * 2 - Math.PI / 2;
      ellipsePath(c, x + Math.cos(a) * r * 0.5, y + Math.sin(a) * r * 0.5, r * 0.36, r * 0.36);
      c.fillStyle = withA(KINTSUGI.cobalto, 0.55); c.fill();
      c.strokeStyle = withA(KINTSUGI.cobalto, 0.9); c.lineWidth = 0.06; c.stroke();
    }
    llenarElipse(c, x, y, r * 0.22, r * 0.22, '#f3ecdf');
    llenarElipse(c, x, y, r * 0.12, r * 0.12, KINTSUGI.cobalto);
  }
  function chorreoKintsugi(c, m) {
    c.beginPath();
    c.moveTo(m.X(-1.1), m.Y(0.70));
    for (let u = -1.1; u <= 1.1; u += 0.05) {
      // Las gotas: un seno lento más picos redondos donde el esmalte chorreó.
      const gota = Math.max(0, Math.sin(u * 9.5 + 1)) ** 4 * 0.075;
      c.lineTo(m.X(u), m.Y(0.73 + 0.03 * Math.sin(u * 4) - gota));
    }
    c.lineTo(m.X(1.1), m.Y(1.1)); c.lineTo(m.X(-1.1), m.Y(1.1)); c.closePath();
  }
  // El oro de una grieta, en capas: canto oscuro, cuerpo con bandas de
  // reflejo y un filo de luz corrido hacia arriba a la izquierda.
  function oroKintsugi(c, m, ancho, trazar) {
    const g = c.createLinearGradient(m.X(-1), m.top, m.X(1), m.bot);
    [[0, '#f6d77a'], [0.18, '#b8862a'], [0.36, '#ffe9a6'], [0.55, '#a87824'], [0.75, '#f2cc66'], [1, '#8a6018']]
      .forEach(([t, col]) => g.addColorStop(t, col));
    c.save(); c.lineCap = 'round'; c.lineJoin = 'round';
    trazar(); c.strokeStyle = ORO.hondo; c.lineWidth = ancho * 1.5; c.stroke();
    trazar(); c.strokeStyle = g; c.lineWidth = ancho; c.stroke();
    if (texturaFina()) {
      c.translate(-0.04, -0.05); trazar();
      c.strokeStyle = withA(ORO.luz, 0.85); c.lineWidth = ancho * 0.3; c.stroke();
    }
    c.restore();
  }

  function texturaKintsugi(c, P, pose) {
    const m = marcoMaterial(pose);
    const fina = texturaFina();
    c.save(); blobPath(c, m.cx, m.top, m.bot, m.rx); c.clip();
    if (fina) {
      // Rama de ciruelo y flores en cobalto sobre el crema.
      c.save(); c.lineCap = 'round'; c.lineJoin = 'round';
      RAMAS_KINTSUGI.forEach(t => {
        trazarTramos(c, m, t.length === 4 ? t : [t[0], t[0], t[1], t[1]]);
        c.strokeStyle = withA(KINTSUGI.cobalto, 0.7); c.lineWidth = 0.16; c.stroke();
      });
      c.restore();
      FLORES_KINTSUGI.forEach(([u, v, r]) => florCobalto(c, m.X(u), m.Y(v), r));
      CRAQUELADO_KINTSUGI.forEach(t => {
        trazarPolilinea(c, m, t);
        c.strokeStyle = withA(KINTSUGI.craquelado, 0.4); c.lineWidth = 0.06; c.stroke();
      });
    }
    // El baño índigo, con el borde de esmalte más espeso (más oscuro) donde
    // se juntó al chorrear.
    chorreoKintsugi(c, m);
    const ig = c.createLinearGradient(0, m.Y(0.66), 0, m.bot);
    ig.addColorStop(0, KINTSUGI.indigoLuz); ig.addColorStop(1, KINTSUGI.indigo);
    c.fillStyle = ig; c.fill();
    c.save();
    c.clip();
    if (fina) {
      // Seigaiha: escamas de olas concéntricas, en filas corridas.
      c.lineWidth = 0.07;
      c.strokeStyle = withA('#b6cdec', 0.55);
      const R = 0.62;
      for (let fila = 0; fila < 4; fila++) {
        const y = m.Y(0.76) + fila * R * 0.55;
        for (let x = m.cx - m.rx - (fila % 2) * R; x <= m.cx + m.rx + R; x += R * 2) {
          ellipsePath(c, x, y, R, R);
          c.fillStyle = mixHex(KINTSUGI.indigoLuz, KINTSUGI.indigo, Math.min(1, fila * 0.3)); c.fill();
          [1, 0.66, 0.33].forEach(k => {
            c.beginPath(); c.arc(x, y, R * k, Math.PI, 0); c.stroke();
          });
        }
      }
    }
    c.restore();
    chorreoKintsugi(c, m);
    c.strokeStyle = withA('#1b2f52', 0.75); c.lineWidth = 0.18; c.stroke();
    volumenMaterial(c, m, 0.55);
    RAMITAS_KINTSUGI.forEach(t => oroKintsugi(c, m, fina ? 0.09 : 0.14, () => trazarPolilinea(c, m, t)));
    GRIETAS_KINTSUGI.forEach(t => oroKintsugi(c, m, fina ? 0.24 : 0.34, () => trazarPolilinea(c, m, t)));
    if (fina) {
      PAN_DE_ORO.forEach(([u, v, r], n) => {
        c.beginPath();
        for (let i = 0; i < 9; i++) {
          const a = i / 9 * Math.PI * 2, rr = r * (0.62 + 0.38 * (((i + n) * 37) % 7) / 6);
          const x = m.X(u) + Math.cos(a) * rr, y = m.Y(v) + Math.sin(a) * rr * 0.8;
          i ? c.lineTo(x, y) : c.moveTo(x, y);
        }
        c.closePath();
        const og = c.createLinearGradient(m.X(u) - r, m.Y(v) - r, m.X(u) + r, m.Y(v) + r);
        og.addColorStop(0, '#fff0bf'); og.addColorStop(0.45, '#e2b84e'); og.addColorStop(1, '#8a6018');
        c.fillStyle = og; c.fill();
        c.strokeStyle = ORO.hondo; c.lineWidth = 0.07; c.stroke();
        llenarElipse(c, m.X(u) - r * 0.3, m.Y(v) - r * 0.3, r * 0.22, r * 0.12, withA('#ffffff', 0.7), -0.5);
      });
    }
    c.restore();
    enPies(c, pose, (x, y, lado) =>
      oroKintsugi(c, m, 0.14, () => {
        c.beginPath(); c.moveTo(x - 0.7 * lado, y - 0.6); c.lineTo(x - 0.1 * lado, y);
        c.lineTo(x + 0.3 * lado, y - 0.15); c.lineTo(x + 0.6 * lado, y + 0.6);
      }));
  }

  // ── Obsidiana y lava: vidrio volcánico con grietas de magma ──
  //
  // El cuerpo es casi negro y muy brillante; lo que se lee de lejos son las
  // grietas encendidas y la panza al rojo, calentada desde abajo.
  //
  // Una grieta de magma no es una línea de un solo color: es una costura con
  // temperatura. Afuera el rojo oscuro de la roca que se calienta, adentro
  // el naranja, y en el centro un hilo amarillo casi blanco. Y se ensancha
  // abajo, que es donde está más caliente. Las de antes eran trazos rectos
  // de un mismo ancho y se leían como un cartel de neón.
  const LAVA = { magma: '#ff5a1a', brasa: '#ffb347', luz: '#fff1c4', hondo: '#3a0a00', roca: '#8a1c00' };
  const OJO_LAVA = {
    fondo: '#050404', cuenca: '#3a1a0c', borde: '#000000',
    anillo: '#ff9a3c', iris: '#4a1300', linea: '#ffd27a', pupila: '#000000',
    ranura: '#050404', filo: '#ff9a3c',
  };
  const GRIETAS_LAVA = [
    [[-1.0, 0.40], [-0.82, 0.58], [-0.70, 0.68], [-0.46, 0.72], [-0.38, 0.88], [-0.46, 1.05]],
    [[-0.70, 0.68], [-0.86, 0.84], [-0.80, 1.02]],
    [[1.0, 0.30], [0.84, 0.52], [0.70, 0.66], [0.50, 0.74], [0.40, 0.92], [0.30, 1.05]],
    [[0.50, 0.74], [0.70, 0.90], [0.64, 1.05]],
    [[0.30, -0.05], [0.16, 0.08], [-0.04, 0.12], [-0.24, 0.06]],
    [[-0.10, 0.84], [0.06, 0.92], [0.00, 1.05]],
    [[-0.38, 0.88], [-0.18, 0.86], [-0.10, 0.84]],
  ].map((t, i) => quebrar(t, 3, 0.06, 211 + i * 17));
  // Donde se juntan grietas, el magma hace un charco que brilla más.
  const CHARCOS_LAVA = [[-0.70, 0.68, 1.0], [0.50, 0.74, 0.9], [-0.38, 0.88, 0.8], [0.40, 0.92, 0.7]];
  // Planos de fractura concoidea: lascas del vidrio con el canto claro.
  const LASCAS_LAVA = [
    [[-0.92, 0.22], [-0.62, 0.06], [-0.40, 0.08], [-0.82, 0.40]],
    [[0.62, 0.08], [0.90, 0.24], [0.94, 0.42], [0.74, 0.30]],
    [[-0.20, 0.70], [0.16, 0.66], [0.26, 0.80], [-0.06, 0.82]],
  ];
  const BRASAS_LAVA = motasFijas(23, 14, { u0: -0.9, u1: 0.9, v0: 0.62, v1: 1, r0: 0.05, r1: 0.12 });

  // Una grieta por tramos: cada tramo con su ancho, más ancho abajo.
  function grietaLava(c, m, pts, ancho, color, blur) {
    c.save(); c.lineCap = 'round'; c.lineJoin = 'round';
    if (blur) { c.shadowColor = LAVA.magma; c.shadowBlur = _k * blur; }
    c.strokeStyle = color;
    for (let i = 1; i < pts.length; i++) {
      const v = (pts[i][1] + pts[i - 1][1]) / 2;
      c.beginPath();
      c.moveTo(m.X(pts[i - 1][0]), m.Y(pts[i - 1][1])); c.lineTo(m.X(pts[i][0]), m.Y(pts[i][1]));
      c.lineWidth = ancho * (0.55 + 0.6 * Math.max(0, Math.min(1, v)));
      c.stroke();
    }
    c.restore();
  }

  function texturaLava(c, P, pose) {
    const m = marcoMaterial(pose);
    const fina = texturaFina();
    c.save(); blobPath(c, m.cx, m.top, m.bot, m.rx); c.clip();
    // La panza al rojo: el magma calienta la piedra desde abajo.
    const calor = c.createRadialGradient(m.X(0), m.Y(1.15), 0.3, m.X(0), m.Y(1.0), m.rx * 1.1);
    calor.addColorStop(0, withA(LAVA.magma, 0.75));
    calor.addColorStop(0.45, withA(LAVA.roca, 0.30));
    calor.addColorStop(1, withA(LAVA.roca, 0));
    c.fillStyle = calor; c.fillRect(m.cx - m.rx, m.top, m.rx * 2, m.h);
    if (fina) {
      // Lascas: el vidrio volcánico se parte en planos lisos que reflejan.
      LASCAS_LAVA.forEach(l => {
        trazarPolilinea(c, m, l); c.closePath();
        const [u0, v0] = l[0], [u2, v2] = l[2];
        const g = c.createLinearGradient(m.X(u0), m.Y(v0), m.X(u2), m.Y(v2));
        g.addColorStop(0, withA('#ffffff', 0.14)); g.addColorStop(1, withA('#ffffff', 0));
        c.fillStyle = g; c.fill();
        trazarPolilinea(c, m, l.slice(0, 2));
        c.strokeStyle = withA('#ffffff', 0.35); c.lineWidth = 0.06; c.stroke();
      });
    }
    volumenMaterial(c, m, 0.22);
    // El reflejo duro del vidrio: una franja curva, nítida, arriba a la
    // izquierda. Es lo que dice "obsidiana" y no "carbón".
    c.beginPath();
    c.moveTo(m.X(-0.86), m.Y(0.42));
    c.quadraticCurveTo(m.X(-0.84), m.Y(0.08), m.X(-0.42), m.Y(0.03));
    c.quadraticCurveTo(m.X(-0.74), m.Y(0.14), m.X(-0.76), m.Y(0.44));
    c.closePath();
    c.fillStyle = withA('#ffffff', 0.5); c.fill();
    // Charcos: el resplandor por debajo de las grietas, donde se cruzan.
    c.save();
    c.globalCompositeOperation = 'lighter';
    CHARCOS_LAVA.forEach(([u, v, r]) => {
      const g = c.createRadialGradient(m.X(u), m.Y(v), 0.05, m.X(u), m.Y(v), r * 1.6);
      g.addColorStop(0, withA(LAVA.brasa, 0.55)); g.addColorStop(0.4, withA(LAVA.magma, 0.25));
      g.addColorStop(1, withA(LAVA.magma, 0));
      c.fillStyle = g; c.fillRect(m.X(u) - r * 2, m.Y(v) - r * 2, r * 4, r * 4);
    });
    c.restore();
    GRIETAS_LAVA.forEach(t => {
      grietaLava(c, m, t, fina ? 0.62 : 0.75, LAVA.hondo, 0);
      grietaLava(c, m, t, fina ? 0.4 : 0.5, '#d8360c', 0.9);
      grietaLava(c, m, t, fina ? 0.24 : 0.3, LAVA.magma, 0);
      if (fina) grietaLava(c, m, t, 0.1, LAVA.luz, 0);
    });
    if (fina) {
      CHARCOS_LAVA.forEach(([u, v, r]) => {
        llenarElipse(c, m.X(u), m.Y(v), r * 0.24, r * 0.2, LAVA.brasa);
        llenarElipse(c, m.X(u), m.Y(v), r * 0.12, r * 0.1, LAVA.luz);
      });
      BRASAS_LAVA.forEach(([u, v, r]) => {
        c.save(); c.shadowColor = LAVA.magma; c.shadowBlur = _k * 0.5;
        llenarElipse(c, m.X(u), m.Y(v), r, r, LAVA.brasa);
        c.restore();
      });
    }
    c.restore();
    enPies(c, pose, (x, y, lado) => {
      const g = c.createRadialGradient(x, y + 0.8, 0.1, x, y + 0.4, 1.2);
      g.addColorStop(0, withA(LAVA.magma, 0.7)); g.addColorStop(1, withA(LAVA.magma, 0));
      c.fillStyle = g; c.fillRect(x - 1.2, y - 1, 2.4, 2);
      const pie = { X: (u) => x + u * lado, Y: (v) => y + v };
      const t = [[-0.6, -0.5], [-0.25, -0.2], [0, 0.05], [0.2, 0.35], [0.35, 0.7]];
      grietaLava(c, pie, t, 0.3, '#d8360c', 0.6);
      grietaLava(c, pie, t, 0.12, LAVA.brasa, 0);
    });
  }

  // ── Escarcha: hielo translúcido con helechos de escarcha y burbujas ──
  //
  // Lo que lo separaba de "plástico celeste" era poco: un corte de luz y unos
  // copos sueltos. Ahora tiene las tres cosas que dicen hielo de un vistazo:
  // nieve apilada en la coronilla (lo único que se sale de la silueta), un
  // borde escarchado que se aclara hacia el canto, y planos de fractura
  // adentro, con las aristas que refractan.
  //
  // Los helechos son un árbol: cada rama larga larga dos pares de ramitas, y
  // cada ramita otros dos. Determinista, sin sorteo: el ángulo de cada hija
  // es el de la madre más o menos lo mismo.
  const ESCARCHA = { blanco: '#f4fcff', hondo: '#2f6f96', nieve: '#ffffff', nieveSombra: '#b9dcef' };
  const OJO_ESCARCHA = {
    fondo: '#0c2433', cuenca: '#3f86ad', borde: '#1d4a66',
    anillo: '#eefbff', iris: '#2a6a92', linea: '#bff0ff', pupila: '#06141e',
    ranura: '#21506e', filo: '#ffffff',
  };
  // Raíces de los helechos: [u, v, ángulo, largo en unidades]. Nacen en el
  // borde y crecen hacia adentro, sin llegar a la cara.
  const RAMAS_ESCARCHA = [
    [-0.98, 0.70, -0.5, 2.0], [-0.80, 0.98, -1.0, 1.8], [0.98, 0.66, -2.6, 2.0],
    [0.78, 0.98, -2.1, 1.8], [-0.30, 1.04, -1.4, 1.3], [0.30, 1.04, -1.8, 1.3],
  ];
  const COPOS_ESCARCHA = [[0.60, 0.84, 0.55], [-0.88, 0.36, 0.34], [0.86, 0.32, 0.3]];
  const BURBUJAS_ESCARCHA = motasFijas(41, 12, { u0: -0.7, u1: 0.7, v0: 0.62, v1: 0.95, r0: 0.08, r1: 0.2 });
  // Planos de fractura: polígonos en [u, v] con un lado que refracta.
  const FRACTURAS_ESCARCHA = [
    [[-0.62, 0.66], [-0.18, 0.74], [-0.30, 0.94], [-0.70, 0.86]],
    [[0.12, 0.72], [0.58, 0.64], [0.50, 0.86], [0.20, 0.92]],
  ];
  // El borde de abajo de la nieve: lomos redondos, más bajos a los costados.
  // [u, cuánto cuelga en v]
  const NIEVE_ESCARCHA = [[-0.64, 0.135], [-0.4, 0.1], [-0.14, 0.12], [0.12, 0.095], [0.38, 0.12], [0.64, 0.135]];

  function ramaEscarcha(c, x, y, ang, largo, nivel) {
    const x2 = x + Math.cos(ang) * largo, y2 = y + Math.sin(ang) * largo;
    c.moveTo(x, y); c.lineTo(x2, y2);
    if (nivel <= 0) return;
    [0.45, 0.75].forEach((t, i) => {
      const bx = x + (x2 - x) * t, by = y + (y2 - y) * t;
      [-1, 1].forEach(sg => ramaEscarcha(c, bx, by, ang + sg * 0.75, largo * (0.42 - i * 0.1), nivel - 1));
    });
  }
  function copoEscarcha(c, x, y, r) {
    c.beginPath();
    for (let i = 0; i < 6; i++) {
      const a = i / 6 * Math.PI * 2, ex = Math.cos(a), ey = Math.sin(a);
      c.moveTo(x, y); c.lineTo(x + ex * r, y + ey * r);
      [-1, 1].forEach(sg => {
        const bx = x + ex * r * 0.55, by = y + ey * r * 0.55;
        c.moveTo(bx, by); c.lineTo(bx + Math.cos(a + sg * 0.8) * r * 0.32, by + Math.sin(a + sg * 0.8) * r * 0.32);
      });
    }
  }
  // La nieve de la coronilla: se apoya sobre el borde de la silueta y lo
  // pasa un poco por arriba, con lomos que cuelgan hacia la frente.
  function nieveEscarcha(c, m) {
    // El lomo de arriba sigue la cabeza corrido hacia afuera, con bultos: un
    // contorno liso y parejo se leía como una tapa, no como nieve apilada.
    const th = m.h * 0.52, my = m.top + th, trx = m.rx * 0.94;
    const pts = NIEVE_ESCARCHA.map(([u, dv]) => [m.X(u), m.Y(dv)]);
    c.beginPath();
    c.moveTo(pts[0][0], pts[0][1]);
    for (let u = -0.66; u <= 0.661; u += 0.06) {
      const s = Math.max(-0.999, Math.min(0.999, u / 0.94));
      const fuera = 0.32 + 0.1 * Math.sin(u * 17 + 1.3) + 0.06 * Math.sin(u * 41);
      const x = m.cx + trx * s, y = my - th * Math.sqrt(1 - s * s);
      c.lineTo(x + s * fuera * 0.6, y - fuera);
    }
    c.lineTo(pts[pts.length - 1][0], pts[pts.length - 1][1]);
    for (let i = pts.length - 1; i > 0; i--) {
      const [x0, y0] = pts[i], [x1, y1] = pts[i - 1];
      c.quadraticCurveTo((x0 + x1) / 2, Math.max(y0, y1) + 0.6, x1, y1);
    }
    c.closePath();
  }

  function texturaEscarcha(c, P, pose) {
    const m = marcoMaterial(pose);
    const fina = texturaFina();
    c.save(); blobPath(c, m.cx, m.top, m.bot, m.rx); c.clip();
    // Profundidad: un núcleo más oscuro corrido abajo, como un bloque grueso.
    const hondo = c.createRadialGradient(m.X(0.15), m.Y(0.65), 0.3, m.X(0.15), m.Y(0.65), m.rx * 0.9);
    hondo.addColorStop(0, withA(ESCARCHA.hondo, 0.5)); hondo.addColorStop(1, withA(ESCARCHA.hondo, 0));
    c.fillStyle = hondo; c.fillRect(m.cx - m.rx, m.top, m.rx * 2, m.h);
    // Planos de fractura: una cara más clara y la arista que refracta.
    if (fina) {
      FRACTURAS_ESCARCHA.forEach(f => {
        trazarPolilinea(c, m, f); c.closePath();
        const [u0, v0] = f[0], [u2, v2] = f[2];
        const g = c.createLinearGradient(m.X(u0), m.Y(v0), m.X(u2), m.Y(v2));
        g.addColorStop(0, withA('#ffffff', 0.2)); g.addColorStop(1, withA('#ffffff', 0));
        c.fillStyle = g; c.fill();
        // Sólo la arista de arriba brilla: con las cuatro marcadas, cada plano
        // se leía como un papel pegado.
        trazarPolilinea(c, m, f.slice(0, 2));
        c.strokeStyle = withA('#ffffff', 0.7); c.lineWidth = 0.06; c.stroke();
      });
    }
    // Un corte de luz en diagonal: lo que dice "hielo" y no "plástico celeste".
    c.beginPath();
    c.moveTo(m.X(-0.95), m.Y(0.52)); c.lineTo(m.X(-0.2), m.Y(-0.05));
    c.lineTo(m.X(0.05), m.Y(-0.05)); c.lineTo(m.X(-0.95), m.Y(0.72)); c.closePath();
    c.fillStyle = withA('#ffffff', 0.22); c.fill();
    volumenMaterial(c, m, 0.45);
    // Borde escarchado: el hielo se blanquea hacia el canto, donde se congeló
    // primero. Va como trazo ancho y suave por dentro de la silueta.
    c.save();
    blobPath(c, m.cx, m.top, m.bot, m.rx);
    c.shadowColor = '#ffffff'; c.shadowBlur = _k * 0.8;
    c.strokeStyle = withA('#ffffff', 0.55); c.lineWidth = 0.7; c.stroke();
    c.restore();
    const nivel = fina ? 2 : 1;
    c.save();
    c.shadowColor = '#ffffff'; c.shadowBlur = _k * 0.3;
    c.beginPath();
    RAMAS_ESCARCHA.forEach(([u, v, a, l]) => ramaEscarcha(c, m.X(u), m.Y(v), a, l, nivel));
    c.strokeStyle = withA(ESCARCHA.blanco, 0.9); c.lineWidth = fina ? 0.09 : 0.14;
    c.lineCap = 'round'; c.stroke();
    if (fina) {
      COPOS_ESCARCHA.forEach(([u, v, r]) => {
        copoEscarcha(c, m.X(u), m.Y(v), r);
        c.strokeStyle = withA(ESCARCHA.blanco, 0.95); c.lineWidth = 0.09; c.stroke();
      });
    }
    c.restore();
    if (fina) {
      BURBUJAS_ESCARCHA.forEach(([u, v, r]) => {
        ellipsePath(c, m.X(u), m.Y(v), r, r);
        c.strokeStyle = withA('#ffffff', 0.5); c.lineWidth = 0.05; c.stroke();
        llenarElipse(c, m.X(u) - r * 0.35, m.Y(v) - r * 0.35, r * 0.25, r * 0.25, withA('#ffffff', 0.8));
      });
    }
    c.restore();

    // La nieve va FUERA del recorte: es lo único que pasa el borde.
    nieveEscarcha(c, m);
    const ng = c.createLinearGradient(0, m.top - 0.5, 0, m.Y(0.2));
    ng.addColorStop(0, ESCARCHA.nieve); ng.addColorStop(0.7, '#eef8ff'); ng.addColorStop(1, ESCARCHA.nieveSombra);
    c.fillStyle = ng; c.fill();
    c.strokeStyle = withA('#8fc2de', 0.55); c.lineWidth = 0.07; c.stroke();
    if (fina) {
      // Brillos de la nieve: puntitos que titilan como cristales.
      [[-0.42, 0.04, 0.2], [0.2, 0.0, 0.24], [0.5, 0.07, 0.16], [-0.1, 0.08, 0.12]].forEach(([u, v, r]) =>
        destello(c, m.X(u), m.Y(v), r, '#ffffff', 0.4));
      llenarElipse(c, m.X(-0.3), m.top + 0.05, 0.9, 0.18, withA('#ffffff', 0.85), -0.1);
    }
    enPies(c, pose, (x, y, lado) => {
      c.beginPath(); ramaEscarcha(c, x - 0.8 * lado, y + 0.6, -Math.PI / 2 + 0.6 * lado, 1.0, 1);
      c.strokeStyle = withA(ESCARCHA.blanco, 0.8); c.lineWidth = 0.07; c.stroke();
    });
  }

  // ── Nebulosa: un globo de vidrio con el cielo adentro ──
  //
  // Las nubes de gas se suman con 'lighter': donde se cruzan dos colores
  // aparece un tercero más claro, que es como se ven las fotos de nebulosas.
  // Pero el gas de una nebulosa no son manchas redondas: son jirones largos,
  // y lo que les da forma es el POLVO oscuro que los corta. Sin las bandas de
  // polvo, el cuerpo era un degradé violeta con puntitos.
  const NEBULOSA = { rosa: '#ff5fc8', cian: '#3fd0ff', violeta: '#8a5cff', polvo: '#07041a' };
  const OJO_NEBULOSA = {
    fondo: '#020108', cuenca: '#1d1550', borde: '#05031a',
    anillo: '#ffd6f5', iris: '#3b1f7a', linea: '#7ad8ff', pupila: '#05030f',
    ranura: '#05031a', filo: '#c9b8ff',
  };
  // Jirones: [u, v, largo y ancho relativos al ancho, giro, color, alfa]
  const NUBES_NEBULOSA = [
    [-0.55, 0.84, 0.95, 0.38, -0.5, 'rosa', 0.6], [0.5, 0.32, 0.8, 0.3, 0.9, 'cian', 0.45],
    [0.35, 0.86, 0.85, 0.32, 0.35, 'violeta', 0.65], [-0.7, 0.18, 0.7, 0.28, 1.2, 'violeta', 0.5],
    [0.0, 0.66, 0.6, 0.2, -0.2, 'rosa', 0.3], [-0.2, 0.95, 0.7, 0.18, 0.1, 'cian', 0.35],
    [0.75, 0.62, 0.5, 0.2, 1.4, 'rosa', 0.35],
  ];
  // Polvo: jirones oscuros que cruzan el gas. Mismo formato, sin color.
  const POLVO_NEBULOSA = [[-0.5, 0.9, 0.6, 0.06, -0.35, 0.4], [0.62, 0.7, 0.4, 0.05, 1.2, 0.35], [0.7, 0.2, 0.4, 0.05, 1.0, 0.3]];
  const ESTRELLAS_NEBULOSA = motasFijas(53, 70, { u0: -1, u1: 1, v0: 0, v1: 1, r0: 0.03, r1: 0.09 });
  const BRILLANTES_NEBULOSA = [[-0.80, 0.42, 0.42], [0.74, 0.5, 0.48], [0.22, 0.08, 0.32], [-0.30, 0.94, 0.3]];
  const COLOR_ESTRELLA = ['#ffffff', '#cfe6ff', '#ffe2c4', '#ffd0f0'];

  function jiron(c, m, u, v, largo, ancho, giro, color, a) {
    c.save();
    c.translate(m.X(u), m.Y(v)); c.rotate(giro); c.scale(1, ancho / largo);
    const r = m.rx * largo;
    const g = c.createRadialGradient(0, 0, 0.1, 0, 0, r);
    g.addColorStop(0, withA(color, a));
    g.addColorStop(0.5, withA(color, a * 0.4));
    g.addColorStop(1, withA(color, 0));
    c.fillStyle = g; c.fillRect(-r, -r, r * 2, r * 2);
    c.restore();
  }
  // Una estrella grande: núcleo, halo y la cruz de difracción.
  function estrellaGrande(c, x, y, r, color) {
    const g = c.createRadialGradient(x, y, 0, x, y, r * 2.2);
    g.addColorStop(0, withA(color, 0.6)); g.addColorStop(1, withA(color, 0));
    c.fillStyle = g; c.fillRect(x - r * 2.2, y - r * 2.2, r * 4.4, r * 4.4);
    destello(c, x, y, r * 1.6, color, 0.3);
    llenarElipse(c, x, y, r * 0.3, r * 0.3, '#ffffff');
  }

  function texturaNebulosa(c, P, pose) {
    const m = marcoMaterial(pose);
    const fina = texturaFina();
    c.save(); blobPath(c, m.cx, m.top, m.bot, m.rx); c.clip();
    c.save();
    c.globalCompositeOperation = 'lighter';
    NUBES_NEBULOSA.forEach(([u, v, l, w, giro, color, a], i) => {
      if (!fina && i > 3) return;
      jiron(c, m, u, v, l, w, giro, NEBULOSA[color], a);
    });
    c.restore();
    if (fina) POLVO_NEBULOSA.forEach(([u, v, l, w, giro, a]) => jiron(c, m, u, v, l, w, giro, NEBULOSA.polvo, a));
    ESTRELLAS_NEBULOSA.forEach(([u, v, r, tono], i) => {
      if (!fina && i % 3) return;
      llenarElipse(c, m.X(u), m.Y(v), r, r, withA(COLOR_ESTRELLA[i % 4], 0.35 + tono * 0.6));
    });
    if (fina) {
      // Una galaxia espiral chica y un planeta con anillo, abajo.
      const gx = m.X(-0.48), gy = m.Y(0.80);
      const halo = c.createRadialGradient(gx, gy, 0, gx, gy, 0.9);
      halo.addColorStop(0, withA('#f3d8ff', 0.45)); halo.addColorStop(1, withA('#f3d8ff', 0));
      c.fillStyle = halo; c.fillRect(gx - 1, gy - 1, 2, 2);
      [0, Math.PI].forEach(giro => {
        c.save(); c.translate(gx, gy); c.rotate(giro - 0.4); c.scale(1, 0.6);
        trazoBrillo(c, 0.1, () => trazarEspiral(c, 0, 0, 0.8, 0.9, 1), '#f3d8ff', null, 0.4);
        c.restore();
      });
      llenarElipse(c, gx, gy, 0.16, 0.11, '#ffffff');
      const px = m.X(0.52), py = m.Y(0.86);
      // El anillo se parte en dos: la mitad de atrás va antes que el planeta.
      const anillo = (a0, a1) => {
        c.beginPath(); c.ellipse(px, py, 0.95, 0.24, -0.35, a0, a1);
        c.strokeStyle = withA('#ffe9c4', 0.85); c.lineWidth = 0.1; c.stroke();
      };
      anillo(Math.PI, Math.PI * 2);
      const pg = c.createRadialGradient(px - 0.18, py - 0.2, 0.05, px, py, 0.5);
      pg.addColorStop(0, '#ffe2b0'); pg.addColorStop(0.6, '#e08a4a'); pg.addColorStop(1, '#7a2e16');
      llenarElipse(c, px, py, 0.48, 0.48, pg);
      c.save(); ellipsePath(c, px, py, 0.48, 0.48); c.clip();
      [-0.12, 0.1].forEach(dy => {
        c.beginPath(); c.ellipse(px, py + dy, 0.6, 0.08, -0.35, 0, Math.PI * 2);
        c.fillStyle = withA('#7a2e16', 0.35); c.fill();
      });
      c.restore();
      anillo(0, Math.PI);
      BRILLANTES_NEBULOSA.forEach(([u, v, r], i) =>
        estrellaGrande(c, m.X(u), m.Y(v), r, i % 2 ? '#cfe6ff' : '#ffd6f5'));
    }
    volumenMaterial(c, m, 0.35);
    c.restore();
    enPies(c, pose, (x, y) => {
      [[-0.5, 0.2, 0.07], [0.1, 0.45, 0.05], [0.45, 0.1, 0.06]].forEach(([dx, dy, r]) =>
        llenarElipse(c, x + dx, y + dy, r, r, '#ffffff'));
      destello(c, x - 0.1, y + 0.1, 0.25, '#f3d8ff', 0.4);
    });
  }

  // ── Celestial: un cielo nocturno limpio ──
  //
  // Simple como Nebulosa: el cuerpo ES el cielo, sin marco ni adornos. Lo
  // que lo separa de Nebulosa es la paleta (azul noche y una luz dorada
  // tenue, sin gas de colores) y lo que se ve en un cielo de verdad: la Vía
  // Láctea cruzando en diagonal con su grieta de polvo, una luna creciente,
  // constelaciones que se reconocen y una estrella fugaz.
  const CELESTIAL = { oro: '#f3d79a', linea: '#cfe3ff', luna: '#fff2cf' };
  const OJO_CELESTIAL = {
    fondo: '#000000', cuenca: '#0d1838', borde: '#050a1c',
    anillo: '#fff3d6', iris: '#1d3f8a', linea: '#f3d79a', pupila: '#02040c',
    ranura: '#050a1c', filo: '#9fb4ff',
  };
  const ESTRELLAS_CELESTIAL = motasFijas(67, 60, { u0: -1, u1: 1, v0: 0, v1: 1, r0: 0.025, r1: 0.08 });
  // La Vía Láctea: una banda de abajo a la izquierda hacia arriba a la
  // derecha, que pasa por debajo de los ojos. Sus estrellas se apiñan.
  const VIA_CELESTIAL = { u0: -1.0, v0: 0.98, u1: 1.0, v1: 0.5 };
  const POLVO_VIA = (() => {
    const out = [];
    let s = 89;
    const r = () => { s = (s * 16807) % 2147483647; return (s - 1) / 2147483646; };
    for (let i = 0; i < 120; i++) {
      const t = r(), d = (r() - 0.5) * 0.22 * (1 - Math.abs(t - 0.5));
      out.push([t, d, 0.02 + r() * 0.045, r()]);
    }
    return out;
  })();
  // Constelaciones: Casiopea (la W) a la izquierda y el Cinturón de Orión.
  const CONSTELACIONES_CELESTIAL = [
    [[-0.92, 0.6], [-0.78, 0.7], [-0.7, 0.62], [-0.58, 0.72], [-0.44, 0.64]],
    [[0.26, 0.92], [0.34, 0.89], [0.42, 0.86]],
  ];
  const DORADAS_CELESTIAL = [[0.22, 0.08, 0.36], [-0.62, 0.14, 0.28], [0.84, 0.44, 0.3]];

  function texturaCelestial(c, P, pose) {
    const m = marcoMaterial(pose);
    const fina = texturaFina();
    c.save(); blobPath(c, m.cx, m.top, m.bot, m.rx); c.clip();
    // Dos luces suaves, azul arriba y violácea abajo. El oro queda sólo en
    // los destellos: mezclado con el azul del cuerpo daba un gris sucio.
    [[0.40, 0.30, 0.9, '#7f9cff', 0.34], [-0.25, 0.95, 0.85, '#8a7cff', 0.26]].forEach(([u, v, r, col, a]) => {
      const g = c.createRadialGradient(m.X(u), m.Y(v), 0.1, m.X(u), m.Y(v), m.rx * r);
      g.addColorStop(0, withA(col, a)); g.addColorStop(1, withA(col, 0));
      c.fillStyle = g; c.fillRect(m.cx - m.rx, m.top, m.rx * 2, m.h);
    });
    // Vía Láctea: el resplandor de la banda y la grieta oscura por el medio.
    const V = VIA_CELESTIAL;
    const ax = m.X(V.u0), ay = m.Y(V.v0), bx = m.X(V.u1), by = m.Y(V.v1);
    const ang = Math.atan2(by - ay, bx - ax), largo = Math.hypot(bx - ax, by - ay);
    c.save();
    c.translate((ax + bx) / 2, (ay + by) / 2); c.rotate(ang);
    c.globalCompositeOperation = 'lighter';
    const banda = c.createLinearGradient(0, -1.4, 0, 1.4);
    banda.addColorStop(0, 'rgba(160,180,255,0)');
    banda.addColorStop(0.5, 'rgba(190,200,255,0.4)');
    banda.addColorStop(1, 'rgba(160,180,255,0)');
    c.fillStyle = banda; c.fillRect(-largo / 2, -1.4, largo, 2.8);
    c.globalCompositeOperation = 'source-over';
    if (fina) {
      c.beginPath();
      c.moveTo(-largo / 2, 0.05);
      c.bezierCurveTo(-largo / 6, -0.35, largo / 6, 0.4, largo / 2, -0.05);
      c.strokeStyle = 'rgba(6,10,30,0.22)'; c.lineWidth = 0.28; c.stroke();
    }
    c.restore();
    if (fina) {
      POLVO_VIA.forEach(([t, d, r, tono]) => {
        const x = ax + (bx - ax) * t - Math.sin(ang) * d * m.rx * 2;
        const y = ay + (by - ay) * t + Math.cos(ang) * d * m.rx * 2;
        llenarElipse(c, x, y, r, r, withA('#ffffff', 0.35 + tono * 0.5));
      });
    }
    ESTRELLAS_CELESTIAL.forEach(([u, v, r, tono], i) => {
      if (!fina && i % 3) return;
      llenarElipse(c, m.X(u), m.Y(v), r, r, withA('#ffffff', 0.3 + tono * 0.6));
    });
    if (fina) {
      CONSTELACIONES_CELESTIAL.forEach(pts => {
        trazarPolilinea(c, m, pts);
        c.strokeStyle = withA(CELESTIAL.linea, 0.45); c.lineWidth = 0.05; c.stroke();
        pts.forEach(([u, v], i) => destello(c, m.X(u), m.Y(v), i % 2 ? 0.2 : 0.3, '#ffffff', 0.5));
      });
      DORADAS_CELESTIAL.forEach(([u, v, r]) => destello(c, m.X(u), m.Y(v), r, CELESTIAL.oro, 0.7));
      // Luna creciente: un disco claro mordido por otro del color del cielo.
      const lx = m.X(0.6), ly = m.Y(0.76);
      c.save();
      c.shadowColor = CELESTIAL.luna; c.shadowBlur = _k * 0.6;
      c.beginPath();
      c.arc(lx, ly, 0.62, 0, Math.PI * 2);
      c.arc(lx + 0.28, ly - 0.16, 0.52, 0, Math.PI * 2, true);
      const lg = c.createLinearGradient(lx - 0.6, ly - 0.6, lx + 0.3, ly + 0.6);
      lg.addColorStop(0, '#fffaf0'); lg.addColorStop(1, CELESTIAL.oro);
      c.fillStyle = lg; c.fill('evenodd');
      c.restore();
      // Estrella fugaz arriba a la derecha, con la cola que se apaga.
      const sx = m.X(0.58), sy = m.Y(0.06);
      const cola = c.createLinearGradient(sx - 1.6, sy + 0.5, sx, sy);
      cola.addColorStop(0, 'rgba(255,255,255,0)'); cola.addColorStop(1, 'rgba(255,255,255,0.85)');
      c.beginPath(); c.moveTo(sx - 1.6, sy + 0.5); c.lineTo(sx, sy);
      c.strokeStyle = cola; c.lineWidth = 0.08; c.lineCap = 'round'; c.stroke();
      destello(c, sx, sy, 0.2, '#ffffff', 0.5);
    }
    volumenMaterial(c, m, 0.32);
    c.restore();
    enPies(c, pose, (x, y) => {
      [[-0.5, 0.2, 0.07], [0.15, 0.45, 0.05], [0.45, 0.1, 0.06]].forEach(([dx, dy, r]) =>
        llenarElipse(c, x + dx, y + dy, r, r, '#ffffff'));
    });
  }

  const CHAR_DEF = {
    madera: {
      pal: {
        base: '#8b5a33', lift: '#c7905c', deep: '#4e2c15', edge: '#2a170b', shadow: '#120a05',
        tint: MADERA.liana,
        armA: '#9a6a40', armB: '#5a3419',
        feetA: '#8a5a34', feetB: '#4a2a14',
      },
      head: texturaMadera,
      ojo: ojoAnillos(OJO_MADERA),
      // Una liana corta en cada brazo, en las coordenadas del muñón: sigue al
      // brazo cuando saluda.
      brazo(c, P, b) {
        if (!texturaFina()) return;
        trazoLiana(c, 0.14, () => {
          c.beginPath(); c.moveTo(-0.55 * b.side, 0.75);
          c.quadraticCurveTo(0.35 * b.side, 0.1, -0.1 * b.side, -0.7);
        });
      },
    },
    talla: {
      pal: {
        base: '#a8703c', lift: '#e2b27a', deep: '#5c3616', edge: '#2e1a0b', shadow: '#120a05',
        tint: '#f3cf9a',
        armA: '#b07a46', armB: '#5c3616',
        feetA: '#9c6636', feetB: '#4e2c10',
      },
      head: texturaTalla,
      ojo: ojoAnillos(OJO_MADERA),
      brazo(c, P, b) {
        if (!texturaFina()) return;
        tallar(c, 0.2, () => {
          c.beginPath(); c.arc(0.1 * b.side, 0.1, 0.55, 0, Math.PI * 1.5);
        });
      },
    },
    gemas: {
      pal: {
        base: '#8e5cc4', lift: '#cfaef3', deep: '#4b2a78', edge: JUNTA_GEMA, shadow: '#0e0a14',
        tint: '#c9b8ff',
        armA: '#a77fd6', armB: '#4b2a78',
        feetA: '#c8304f', feetB: '#520515',
      },
      head: texturaGemas,
      ojo: ojoGema,
      brazo(c, P, b) {
        if (!texturaFina()) return;
        trazoBrillo(c, 0.1, () => {
          c.beginPath(); c.moveTo(-0.5 * b.side, 0.8);
          c.quadraticCurveTo(0.3 * b.side, 0.2, 0, -0.6);
        }, ORO.trazo, null, 0.2);
      },
    },
    piedra: {
      pal: {
        base: '#80868c', lift: '#c0c6cb', deep: '#4a4f55', edge: '#2a2e33', shadow: '#0f1113',
        tint: '#9fd36a',
        armA: '#8c9298', armB: '#4f555b',
        feetA: '#6f757b', feetB: '#3d4247',
      },
      back: hongoPiedra,
      head: texturaPiedra,
      ojo: ojoAnillos(OJO_PIEDRA),
    },
    kintsugi: {
      pal: {
        base: '#ebe4d6', lift: '#ffffff', deep: '#b9ae99', edge: '#6e6455', shadow: '#1d1a15',
        tint: '#e2b84e',
        armA: '#efe9dd', armB: '#b9ae99',
        feetA: KINTSUGI.indigoLuz, feetB: KINTSUGI.indigo,
      },
      head: texturaKintsugi,
      ojo: ojoAnillos(OJO_KINTSUGI),
      brazo(c, P, b) {
        if (!texturaFina()) return;
        // El oro de la grieta lleva su degradé de reflejo; en el muñón, el
        // marco es el propio brazo.
        oroKintsugi(c, { X: (u) => u, top: -1, bot: 1 }, 0.14, () => {
          c.beginPath(); c.moveTo(-0.6 * b.side, -0.2);
          c.lineTo(0.05 * b.side, 0.25); c.lineTo(0.1 * b.side, 0.9);
        });
      },
    },
    lava: {
      pal: {
        base: '#1d1a22', lift: '#55505e', deep: '#09080b', edge: '#000000', shadow: '#000000',
        tint: LAVA.brasa,
        armA: '#2a2630', armB: '#0b0a0d',
        feetA: '#2a2630', feetB: '#0b0a0d',
      },
      // El calor se escapa por el borde: un halo naranja apenas visible.
      halo: { color: LAVA.magma, blur: 0.8 },
      head: texturaLava,
      ojo: ojoAnillos(OJO_LAVA),
      brazo(c, P, b) {
        if (!texturaFina()) return;
        trazoBrillo(c, 0.14, () => {
          c.beginPath(); c.moveTo(-0.6 * b.side, -0.4); c.lineTo(0, 0.1); c.lineTo(0.15 * b.side, 0.9);
        }, LAVA.magma, LAVA.luz, 0.6);
      },
    },
    escarcha: {
      pal: {
        base: '#a6dcf2', lift: '#effbff', deep: '#4f94b8', edge: '#2a5f80', shadow: '#0e2230',
        tint: '#bff0ff',
        armA: '#b8e6f7', armB: '#5d9fc2',
        feetA: '#a6dcf2', feetB: '#4f94b8',
      },
      halo: { color: '#bff0ff', blur: 0.6 },
      head: texturaEscarcha,
      ojo: ojoAnillos(OJO_ESCARCHA),
      brazo(c, P, b) {
        if (!texturaFina()) return;
        c.beginPath(); ramaEscarcha(c, -0.4 * b.side, 0.9, -Math.PI / 2 + 0.3 * b.side, 1.2, 1);
        c.strokeStyle = withA(ESCARCHA.blanco, 0.85); c.lineWidth = 0.07; c.stroke();
      },
    },
    nebulosa: {
      pal: {
        base: '#2a1f5c', lift: '#6a58c8', deep: '#0c0a24', edge: '#1f1650', shadow: '#020108',
        tint: '#ff7ad9',
        armA: '#3a2c7a', armB: '#120e34',
        feetA: '#3a2c7a', feetB: '#120e34',
      },
      halo: { color: NEBULOSA.violeta, blur: 0.35 },
      head: texturaNebulosa,
      ojo: ojoAnillos(OJO_NEBULOSA),
      brazo(c, P, b) {
        if (!texturaFina()) return;
        destello(c, 0.15 * b.side, 0.1, 0.4, '#ffffff', 0.5);
        llenarElipse(c, -0.35 * b.side, 0.6, 0.06, 0.06, '#ffffff');
      },
    },
    celestial: {
      pal: {
        base: '#16224e', lift: '#3d5aa6', deep: '#060b1f', edge: '#1a2a5e', shadow: '#000000',
        tint: CELESTIAL.oro,
        armA: '#1f2d62', armB: '#080e28',
        feetA: '#1f2d62', feetB: '#080e28',
      },
      halo: { color: '#9fb4ff', blur: 0.3 },
      head: texturaCelestial,
      ojo: ojoAnillos(OJO_CELESTIAL),
      brazo(c, P, b) {
        if (!texturaFina()) return;
        destello(c, 0.15 * b.side, 0.1, 0.32, '#ffffff', 0.5);
      },
    },
  };

  /*
   * El personaje del pintado en curso, o null.
   *
   * Se pregunta en cada capa en vez de pasarlo por parámetro porque las capas
   * ya tienen firma fija —drawEyes(c, P, pose) la usan también las vistas
   * previas— y porque `character` puede estar prestado por conVestuario
   * mientras dura un solo pintado.
   */
  function charActual() {
    return character ? (CHAR_DEF[character] || null) : null;
  }

  // Si el vestuario en curso lleva bata: el brazo del puntero va con manga.
  function llevaBata() {
    const ch = charActual();
    return ch ? !!ch.bata : hat === 'bata';
  }

  function drawAcc(c, st, layer) {
    if (layer === 'back') {
      const back = HATS_BACK[hat];
      if (back) back(c, accPal(st), st);
      return;
    }
    if (layer === 'over') {
      const over = HATS_OVER[hat];
      if (over) over(c, accPal(st), st);
      return;
    }
    const fn = HATS[hat];
    if (!fn) return;
    if (!!HATS_FRONT[hat] !== (layer === 'front')) return;
    fn(c, accPal(st), st);
  }

  // ── Props ─────────────────────────────────────────────────────────────
  //
  // Lo que Boberto sostiene o saca. En el motor viejo cada prop era un puñado
  // de filas de la grilla; acá es una función y se puede posicionar con
  // decimales, que es lo que permite que la taza no quede pegada al cuerpo.

  /**
   * La laptop, sobre las piernas, con las dos manos en el teclado.
   *
   * La versión anterior flotaba sin brazos, tapaba la boca y pintaba el
   * código con el color del tema (sobre un tema verde, verde sobre verde).
   * Ahora, con la receta de la guitarra, el skate y el jarro:
   *
   * - Colores FIJOS de aluminio con contorno oscuro y filos de luz: se lee
   *   sobre cualquier cuerpo y cualquier tema. La pantalla, el teclado y el
   *   trackpad miran a quien mira, como en una vidriera: si la tapa diera la
   *   espalda, lo único a la vista sería un rectángulo gris.
   * - Va baja, sobre las piernas (los pies se ocultan): el canto de la tapa
   *   queda debajo de la boca de todos los personajes, incluida la sonrisa
   *   abierta de los Dragon Ball en chibi, que baja hasta 12,8.
   * - Tipear sí es de dos manos: los brazos los pone el prop, por el hook
   *   `brazo` del personaje, y las manos se turnan. La tecla que pisa cada
   *   una se hunde y se oscurece delante de los dedos.
   * - La pantalla es una app de verdad, dibujada con rectángulos y sin
   *   fillText: un editor oscuro con la barra de la ventana, números de
   *   línea, sintaxis de colores, la línea activa y el cursor. El texto se
   *   escribe y se corre de un cuadro al otro (`linea`), así el tipeo se ve
   *   en la pantalla y no sólo en las manos.
   * - La luz de la pantalla le da en la cara y la panza (`brillo`), sólo
   *   sobre lo ya pintado ('source-atop'): no ilumina el aire.
   * - Por debajo de LAPTOP_DETALLE_PX el detalle sería polvo: quedan formas
   *   limpias (barras de código, teclado liso, trackpad), como el jarro sin
   *   las letras.
   *
   * Cuadros clave (propPhase): `tapa`, cuánto está abierta (0 cerrada, con
   * la calcomanía de la marca a la vista); `manos` [izq x, y, der x, y];
   * `tecla`, qué mano pisa (-1 o 1); `linea`, cuánto código hay escrito;
   * `alCuerpo`, si las manos siguen al cuerpo y no al teclado; `listo`, la
   * pantalla con el aviso verde.
   */
  const LAPTOP_COL = {
    contorno: '#151a21',
    aluLuz: '#f2f5f9', alu: '#cfd6df', aluSombra: '#95a0ae', canto: '#7b8694',
    bisel: '#171b22', camara: '#2c333e', lente: '#6fc3ff',
    pantalla: '#0d1420', barra: '#1a2432', pestana: '#26344b', margen: '#111a27',
    numero: '#3a4a60', activa: '#17243a', cursor: '#f4f8ff', estado: '#2f7de1',
    kw: '#ff79b0', fn: '#62c4ff', str: '#9fe07a', num: '#ffb45e', com: '#5b6b80', txt: '#d6dfeb',
    rojo: '#ff5f57', amarillo: '#febc2e', verde: '#28c840',
    ok: '#2fbf71', okOscuro: '#1b7a45',
    teclado: '#1c2129', tecla: '#3a414c', teclaLuz: '#56606e', teclaHundida: '#0b0e13',
    trackpad: '#bcc5d0', bisagra: '#5b6572', brillo: '#9fdcff',
  };
  const LAPTOP_DETALLE_PX = 6;
  // La tapa (de su canto de arriba a la bisagra) y la base, un trapecio visto
  // apenas desde arriba: más angosto atrás que adelante.
  const LAP = {
    x0: 4.4, x1: 11.6, tapa: 13.0, bisagra: 15.85,
    atras: 16.0, adelante: 17.7, canto: 18.05, medioAtras: 3.75, medioAdelante: 5.0,
  };
  const medioBase = (y) => LAP.medioAtras + (y - LAP.atras) / (LAP.adelante - LAP.atras) * (LAP.medioAdelante - LAP.medioAtras);
  // Filas del teclado: alto de tecla y cuántas teclas por fila. La última es
  // la de la barra espaciadora (dos teclas a cada lado).
  const TECLAS = { y0: 16.27, paso: 0.215, alto: 0.165, filas: [13, 12, 11] };
  /*
   * El archivo que se escribe: cada línea son tramos [color, ancho]. Un
   * editor de verdad tiene sangría, comentarios y líneas de largo distinto;
   * con todas iguales se leía como un código de barras.
   */
  const CODIGO = [
    [['kw', 1.0], ['txt', 0.5], ['fn', 1.3], ['txt', 0.3]],
    [['txt', 0.35], ['kw', 0.7], ['str', 1.7]],
    [['txt', 0.35], ['fn', 0.9], ['num', 0.45], ['txt', 0.5]],
    [['com', 2.7]],
    [['kw', 0.85], ['txt', 0.8], ['kw', 0.55], ['num', 0.4]],
    [['txt', 0.35], ['fn', 1.2], ['str', 1.05]],
    [['kw', 1.15], ['fn', 1.4]],
    [['txt', 0.35], ['num', 0.6], ['txt', 0.7], ['str', 0.9]],
  ];
  const LAPTOP = [
    { dy: 0,     tapa: 0,   manos: [4.4, 16.75, 11.6, 16.75] },                          // 0 cerrada
    { dy: 0,     tapa: 0.5, manos: [4.75, 14.5, 11.25, 14.5] },                          // 1 la abre
    { dy: 0,     tapa: 1,   manos: [5.0, 16.3, 11.0, 16.3], linea: 0, brillo: 0.6 },    // 2 abierta
    { dy: 0.1,   tapa: 1,   manos: [5.0, 16.42, 11.0, 16.2], tecla: -1, linea: 1, brillo: 0.6 }, // 3 tipea
    { dy: 0.1,   tapa: 1,   manos: [5.0, 16.2, 11.0, 16.42], tecla: 1,  linea: 2, brillo: 0.6 }, // 4 tipea
    { dy: 0.1,   tapa: 1,   manos: [5.0, 16.42, 11.0, 16.2], tecla: -1, linea: 3, brillo: 0.6 }, // 5 tipea
    { dy: 0.1,   tapa: 1,   manos: [5.0, 16.2, 11.0, 16.42], tecla: 1,  linea: 4, brillo: 0.6 }, // 6 tipea
    { dy: 0.25,  tapa: 1,   manos: [5.0, 16.3, 11.0, 16.3], linea: 4, brillo: 1 },      // 7 se acerca a leer
    { dy: -0.15, tapa: 1,   manos: [2.0, 6.6, 14.0, 6.6], alCuerpo: true, linea: 4, brillo: 0.6, listo: true }, // 8 listo: se estira
  ];
  function poseLaptop(i, cara) {
    return Object.assign({ prop: 'laptop', propPhase: i, hideFeet: true, armL: false, armR: false, bodyDY: LAPTOP[i].dy }, cara);
  }

  // La luz de la pantalla sobre la cara y la panza, sólo donde ya hay dibujo.
  function luzPantalla(c, k) {
    if (!(k > 0)) return;
    c.save();
    c.globalCompositeOperation = 'source-atop';
    const g = c.createRadialGradient(BODY.cx, LAP.tapa + 0.4, 0.5, BODY.cx, LAP.tapa + 0.4, 6.8);
    g.addColorStop(0, withA(LAPTOP_COL.brillo, 0.45 * k));
    g.addColorStop(1, withA(LAPTOP_COL.brillo, 0));
    c.fillStyle = g;
    c.fillRect(BODY.cx - 7, LAP.tapa - 6.6, 14, 7.2);
    c.restore();
  }

  // El trapecio de la base, con `d` de margen hacia adentro.
  function basePath(c, y0, y1, d) {
    c.beginPath();
    c.moveTo(BODY.cx - medioBase(y0) + d, y0);
    c.lineTo(BODY.cx + medioBase(y0) - d, y0);
    c.lineTo(BODY.cx + medioBase(y1) - d, y1);
    c.lineTo(BODY.cx - medioBase(y1) + d, y1);
    c.closePath();
  }

  // La pantalla, en el rectángulo [x0, y0, x1, y1].
  function pantallaLaptop(c, f, det, x0, y0, x1, y1) {
    const L = LAPTOP_COL;
    c.fillStyle = L.pantalla; c.fillRect(x0, y0, x1 - x0, y1 - y0);
    if (f.tapa < 1) {
      // Arrancando: la pantalla negra con una barra de progreso.
      const cx = (x0 + x1) / 2, cy = (y0 + y1) / 2;
      c.fillStyle = withA(L.txt, 0.25); c.fillRect(cx - 0.9, cy - 0.06, 1.8, 0.12);
      c.fillStyle = L.estado; c.fillRect(cx - 0.9, cy - 0.06, 0.8, 0.12);
      return;
    }
    // Barra de la ventana y barra de estado.
    const bh = 0.25, eh = 0.15;
    c.fillStyle = L.barra; c.fillRect(x0, y0, x1 - x0, bh);
    c.fillStyle = L.estado; c.fillRect(x0, y1 - eh, x1 - x0, eh);
    const top = y0 + bh, bot = y1 - eh;
    if (!det) {
      // Formas limpias: tres renglones de colores.
      [['kw', 2.6], ['fn', 3.6], ['str', 2.1]].forEach(([col, w], i) => {
        c.fillStyle = L[col]; c.fillRect(x0 + 0.35, top + 0.28 + i * 0.5, w, 0.24);
      });
    } else {
      // Los tres botones de la ventana y la pestaña del archivo.
      [L.rojo, L.amarillo, L.verde].forEach((col, i) => {
        ellipsePath(c, x0 + 0.2 + i * 0.2, y0 + bh / 2, 0.06, 0.06);
        c.fillStyle = col; c.fill();
      });
      c.fillStyle = L.pestana; c.fillRect(x0 + 0.85, y0 + 0.05, 1.35, bh - 0.05);
      c.fillStyle = withA(L.txt, 0.7); c.fillRect(x0 + 1.0, y0 + 0.11, 0.8, 0.07);
      // Margen con los números de línea.
      const mw = 0.42;
      c.fillStyle = L.margen; c.fillRect(x0, top, mw, bot - top);
      // Cuatro renglones a la vista. `linea` decide desde dónde se ve el
      // archivo y cuánto de la última línea ya está escrito.
      const ln = f.linea || 0;
      const ini = Math.floor(ln / 2), escrito = ln % 2 ? 0.85 : 0.45;
      for (let i = 0; i < 4; i++) {
        const y = top + 0.13 + i * 0.36;
        const activa = i === 3;
        if (activa) { c.fillStyle = L.activa; c.fillRect(x0 + mw, y - 0.07, x1 - x0 - mw, 0.32); }
        c.fillStyle = activa ? withA(L.txt, 0.75) : L.numero;
        c.fillRect(x0 + 0.12, y, 0.2, 0.17);
        const tramos = CODIGO[(ini + i) % CODIGO.length];
        const total = tramos.reduce((s, t) => s + t[1], 0);
        let x = x0 + mw + 0.2, falta = activa ? total * escrito : total;
        for (const [col, w] of tramos) {
          if (falta <= 0) break;
          const ww = Math.min(w, falta);
          c.fillStyle = L[col]; c.fillRect(x, y, Math.max(0.05, ww - 0.08), 0.17);
          x += w; falta -= w;
        }
        if (activa) {
          const xc = x0 + mw + 0.2 + total * escrito + 0.04;
          c.fillStyle = L.cursor; c.fillRect(xc, y - 0.04, 0.07, 0.25);
        }
      }
    }
    if (f.listo) {
      // El aviso verde: lo que dice que el trabajo quedó hecho.
      const cx = (x0 + x1) / 2, cy = (top + bot) / 2;
      roundRect(c, cx - 1.75, cy - 0.42, 3.5, 0.84, 0.16);
      c.fillStyle = L.ok; c.fill();
      c.lineWidth = 0.06; c.strokeStyle = L.okOscuro; c.stroke();
      c.beginPath();
      c.moveTo(cx - 1.35, cy + 0.02); c.lineTo(cx - 1.12, cy + 0.24); c.lineTo(cx - 0.72, cy - 0.22);
      c.lineWidth = 0.13; c.lineCap = 'round'; c.lineJoin = 'round'; c.strokeStyle = '#ffffff'; c.stroke();
      if (det) {
        c.fillStyle = withA('#ffffff', 0.9); c.fillRect(cx - 0.45, cy - 0.17, 1.9, 0.12);
        c.fillStyle = withA('#ffffff', 0.6); c.fillRect(cx - 0.45, cy + 0.06, 1.2, 0.1);
      }
    }
  }

  // La tapa abierta: el canto (su espesor), el aluminio, el vidrio negro del
  // bisel con la cámara y la pantalla. `tapa` < 1 la achica hacia la bisagra:
  // así se ve a medio abrir.
  function tapaLaptop(c, f, det) {
    const L = LAPTOP_COL, G = LAP;
    c.save();
    c.translate(0, G.bisagra);
    c.scale(1, f.tapa);
    c.translate(0, -G.bisagra);
    // El canto: el espesor de la tapa, visto apenas desde arriba.
    roundRect(c, G.x0 + 0.1, G.tapa - 0.2, G.x1 - G.x0 - 0.2, 0.4, 0.18);
    c.fillStyle = L.aluLuz; c.fill();
    c.lineWidth = 0.12; c.strokeStyle = L.contorno; c.stroke();
    // El aluminio, con la luz arriba.
    roundRect(c, G.x0, G.tapa, G.x1 - G.x0, G.bisagra - G.tapa, 0.3);
    const g = c.createLinearGradient(0, G.tapa, 0, G.bisagra);
    g.addColorStop(0, L.aluLuz); g.addColorStop(0.5, L.alu); g.addColorStop(1, L.aluSombra);
    c.fillStyle = g; c.fill();
    c.lineWidth = 0.18; c.strokeStyle = L.contorno; c.stroke();
    // El bisel negro, con un filo de luz arriba: el vidrio.
    const bx0 = G.x0 + 0.14, by0 = G.tapa + 0.12, bx1 = G.x1 - 0.14, by1 = G.bisagra - 0.12;
    roundRect(c, bx0, by0, bx1 - bx0, by1 - by0, 0.2);
    c.fillStyle = L.bisel; c.fill();
    if (det) {
      c.beginPath(); c.moveTo(bx0 + 0.25, by0 + 0.05); c.lineTo(bx1 - 0.25, by0 + 0.05);
      c.lineWidth = 0.05; c.strokeStyle = withA('#ffffff', 0.22); c.stroke();
      // La cámara, con su lente.
      ellipsePath(c, BODY.cx, by0 + 0.15, 0.075, 0.075);
      c.fillStyle = L.camara; c.fill();
      ellipsePath(c, BODY.cx + 0.02, by0 + 0.13, 0.03, 0.03);
      c.fillStyle = withA(L.lente, 0.8); c.fill();
    }
    pantallaLaptop(c, f, det, bx0 + 0.24, by0 + 0.3, bx1 - 0.24, by1 - 0.22);
    // Reflejo del vidrio: una diagonal tenue sobre la pantalla.
    if (det) {
      c.save();
      roundRect(c, bx0, by0, bx1 - bx0, by1 - by0, 0.2);
      c.clip();
      c.beginPath();
      c.moveTo(bx1 - 2.4, by0); c.lineTo(bx1 - 1.5, by0); c.lineTo(bx1 - 2.6, by1); c.lineTo(bx1 - 3.5, by1);
      c.closePath();
      c.fillStyle = withA('#ffffff', 0.05); c.fill();
      c.restore();
    }
    c.restore();
  }

  // La base: el canto de adelante, la cubierta de aluminio y, abierta, el
  // teclado y el trackpad; cerrada, la tapa con la calcomanía de la marca.
  function baseLaptop(c, f, det) {
    const L = LAPTOP_COL, G = LAP, cx = BODY.cx;
    const cerrada = f.tapa === 0;
    // El canto de adelante (el espesor; cerrada, el de las dos mitades).
    const mA = G.medioAdelante;
    c.beginPath();
    c.moveTo(cx - mA, G.adelante); c.lineTo(cx + mA, G.adelante);
    c.lineTo(cx + mA, G.canto - 0.2); c.quadraticCurveTo(cx + mA, G.canto, cx + mA - 0.25, G.canto);
    c.lineTo(cx - mA + 0.25, G.canto); c.quadraticCurveTo(cx - mA, G.canto, cx - mA, G.canto - 0.2);
    c.closePath();
    c.fillStyle = L.canto; c.fill();
    c.lineWidth = 0.16; c.strokeStyle = L.contorno; c.stroke();
    if (cerrada) {
      c.beginPath(); c.moveTo(cx - mA + 0.1, G.adelante + 0.17); c.lineTo(cx + mA - 0.1, G.adelante + 0.17);
      c.lineWidth = 0.05; c.strokeStyle = withA(L.contorno, 0.7); c.stroke();
    } else if (det) {
      // La muesca para abrirla, al medio del canto.
      c.beginPath(); c.ellipse(cx, G.adelante, 0.55, 0.12, 0, 0, Math.PI);
      c.fillStyle = withA(L.contorno, 0.45); c.fill();
    }
    // La cubierta: aluminio con la luz atrás, contorno y filo claro adelante.
    basePath(c, G.atras, G.adelante, 0);
    const g = c.createLinearGradient(0, G.atras, 0, G.adelante);
    g.addColorStop(0, L.aluLuz); g.addColorStop(1, L.alu);
    c.fillStyle = g; c.fill();
    c.lineWidth = 0.18; c.strokeStyle = L.contorno; c.stroke();
    c.beginPath(); c.moveTo(cx - mA + 0.15, G.adelante - 0.06); c.lineTo(cx + mA - 0.15, G.adelante - 0.06);
    c.lineWidth = 0.07; c.strokeStyle = withA('#ffffff', 0.75); c.stroke();

    if (cerrada) {
      // La calcomanía de la marca, la misma del jarro, vista desde arriba.
      const s = 0.8;
      c.save();
      c.translate(cx, (G.atras + G.adelante) / 2);
      c.scale(s, s * 0.6);
      c.translate(-MARCA.cx, -(MARCA.y0 + MARCA.y1) / 2);
      marcaDBA(c, _k * s);
      c.restore();
      return;
    }

    // El teclado: el hueco oscuro y las teclas.
    const t0 = TECLAS.y0 - 0.08, t1 = TECLAS.y0 + 3 * TECLAS.paso + TECLAS.alto + 0.08;
    basePath(c, t0, t1, 0.35);
    c.fillStyle = L.teclado; c.fill();
    if (det) {
      // La tecla pisada: la mano tapa el alto entero del teclado, así que se
      // ilumina la de al lado, hacia el centro, a la altura de la mano. Es
      // la que se ve bajar.
      const xPisada = f.tecla ? f.manos[f.tecla < 0 ? 0 : 2] - f.tecla * 0.95 : null;
      for (let r = 0; r < 4; r++) {
        const y = TECLAS.y0 + r * TECLAS.paso;
        const m = medioBase(y + TECLAS.alto / 2) - 0.45;
        // [x desde, ancho] de cada tecla de la fila.
        const teclas = [];
        if (r < 3) {
          const n = TECLAS.filas[r], w = (2 * m - (n - 1) * 0.05) / n;
          for (let i = 0; i < n; i++) teclas.push([cx - m + i * (w + 0.05), w]);
        } else {
          const w = (2 * m - 6 * 0.05) * 0.1;
          teclas.push([cx - m, w], [cx - m + w + 0.05, w]);
          teclas.push([cx - m + 2 * (w + 0.05), 2 * m - 4 * (w + 0.05) - 0.05]);
          teclas.push([cx + m - 2 * w - 0.05, w], [cx + m - w, w]);
        }
        for (const [x, w] of teclas) {
          const pisada = xPisada != null && r === 1 && xPisada >= x - 0.025 && xPisada < x + w + 0.025;
          c.fillStyle = pisada ? L.teclaHundida : L.tecla;
          c.fillRect(x, y + (pisada ? 0.03 : 0), w, TECLAS.alto);
          if (!pisada) { c.fillStyle = L.teclaLuz; c.fillRect(x, y, w, 0.035); continue; }
          // Hundida y oscura sola, sobre un teclado oscuro, no se veía: el
          // borde con la luz del teclado es lo que dice "la pisó".
          c.lineWidth = 0.04; c.strokeStyle = withA(L.lente, 0.85);
          c.strokeRect(x, y + 0.03, w, TECLAS.alto);
        }
      }
    } else {
      // Formas limpias: dos rayas sugieren las filas.
      c.lineWidth = 0.06; c.strokeStyle = withA(L.tecla, 0.9);
      [0.36, 0.64].forEach(k => {
        const y = t0 + (t1 - t0) * k;
        c.beginPath(); c.moveTo(cx - medioBase(y) + 0.5, y); c.lineTo(cx + medioBase(y) - 0.5, y); c.stroke();
      });
    }
    // El trackpad.
    const p0 = t1 + 0.1, p1 = G.adelante - 0.12;
    c.beginPath();
    c.moveTo(cx - 0.95, p0); c.lineTo(cx + 0.95, p0); c.lineTo(cx + 1.05, p1); c.lineTo(cx - 1.05, p1);
    c.closePath();
    c.fillStyle = L.trackpad; c.fill();
    c.lineWidth = 0.05; c.strokeStyle = L.aluSombra; c.stroke();
  }

  function propLaptop(c, P, pose) {
    const f = LAPTOP[pose.propPhase || 0] || LAPTOP[0];
    const L = LAPTOP_COL, G = LAP;
    const det = _k >= LAPTOP_DETALLE_PX;
    const dy = pose.bodyDY || 0;
    const T = llevaBata() ? _bataTela : null;
    const ch = charActual();

    luzPantalla(c, f.tapa === 1 ? f.brillo : 0);
    baseLaptop(c, f, det);
    if (f.tapa > 0) {
      // La bisagra: una franja oscura con sus dos cilindros.
      roundRect(c, G.x0 + 0.35, G.bisagra - 0.05, G.x1 - G.x0 - 0.7, 0.26, 0.1);
      c.fillStyle = L.bisagra; c.fill();
      c.lineWidth = 0.08; c.strokeStyle = L.contorno; c.stroke();
      if (det) {
        [G.x0 + 1.0, G.x1 - 1.0].forEach(x => {
          roundRect(c, x - 0.45, G.bisagra - 0.07, 0.9, 0.3, 0.13);
          c.fillStyle = L.aluSombra; c.fill();
          c.lineWidth = 0.06; c.strokeStyle = L.contorno; c.stroke();
        });
      }
      tapaLaptop(c, f, det);
    }

    // Los brazos por delante de la tapa (vienen de los costados del cuerpo
    // hasta el teclado) y las manos encima de las teclas.
    // Desde el hombro el muñón caía en diagonal sobre los costados de la
    // pantalla y tapaba el código: hacia el teclado sale de más abajo y más
    // afuera, así el antebrazo entra por el costado de la tapa.
    const m = f.manos, my = f.alCuerpo ? dy : 0;
    const hy = f.alCuerpo ? 10.4 : 13.2;
    brazoGuitarra(c, P, T, ch, -1, f.alCuerpo ? 2.9 : 1.7, hy + dy, m[0], m[1] + my);
    brazoGuitarra(c, P, T, ch, 1, f.alCuerpo ? 13.1 : 14.3, hy + dy, m[2], m[3] + my);
    manoGuitarra(c, P, m[0], m[1] + my);
    manoGuitarra(c, P, m[2], m[3] + my);
  }

  /**
   * La taza de café, con las manos que la sostienen.
   *
   * La versión anterior era un rectángulo gris claro flotando al costado, sin
   * manos, con el café del color de la utilería del tema, y para el sorbo sólo
   * subía: nunca llegaba a la boca y le tapaba medio ojo derecho. Ahora, con
   * la receta de la guitarra y el skate:
   *
   * - Un jarro enlozado de verdad: boca en elipse (se ve apenas desde arriba),
   *   cuerpo panzón, asa en C, borde oscuro de esmalte, el café adentro y la
   *   marca "DBA" en el frente (marcaDBA). Colores FIJOS con contorno oscuro
   *   y filete claro, como la guitarra: se lee sobre Charmander, Pikachu,
   *   Freezer o un Gengar de noche, y no cambia con el tema.
   * - La sostiene el personaje con UNA mano, la derecha, en el asa. El brazo
   *   va por el hook `brazo` del personaje (brazoGuitarra), así que lleva su
   *   manga o su guante. El izquierdo queda en reposo, como en `base`: con
   *   las dos manos alrededor del jarro se leía raro, un muñeco abrazando una
   *   taza, y nadie levanta un café así.
   * - En el sorbo el labio del jarro llega a la boca (bocaTaza) y el jarro se
   *   inclina hacia ella; el café adentro queda horizontal y se corre hacia
   *   ese labio, que es lo que dice "está tomando".
   * - Ningún cuadro con los ojos abiertos pisa el ojo derecho: el jarro va
   *   debajo y a la derecha, y sólo sube a la altura de la cara con los ojos
   *   cerrados (el sorbo) o por encima de la cabeza (el brindis).
   *
   * Coordenadas locales del jarro: origen en el centro de la boca, `y` hacia
   * el fondo. El jarro gira sobre ese centro.
   */
  const CAFE_COL = {
    contorno: '#161c24', filete: '#dfe8f1',
    esmalte: '#3f86c4', esmalteLuz: '#78b3e3', esmalteSombra: '#234f7c',
    borde: '#1d3553', interior: '#eef2f6', interiorSombra: '#b9c4d0',
    cafe: '#5b3822', crema: '#a4713f',
    // La etiqueta de la marca: crema con la luz de la izquierda, como el
    // esmalte, y las letras en el azul del borde.
    etiqueta: '#f7f1e4', etiquetaSombra: '#d8ccb6', letra: '#1d3553',
  };
  // Medio ancho de la boca, alto del cuerpo y medio ancho del fondo.
  // `k`: la escala con la que se lo pinta. Más chico, al lado de un cuerpo de
  // doce unidades se leía como un vaso de shot.
  const JARRO = { r: 1.25, ry: 0.38, h: 2.7, rf: 1.12, k: 1.3 };
  // Dónde agarra la mano, en coordenadas del jarro: el asa.
  const JARRO_ASA = [1.95, 1.25];

  /*
   * Cuadros clave (propPhase). `dy`: bodyDY del cuerpo. `taza`: [x, y, giro]
   * del centro de la boca del jarro en el lienzo, con el giro en radianes
   * (negativo la vuelca hacia la boca, a la izquierda de quien mira). `boca`,
   * en lugar de `taza`: el giro del sorbo, con el labio apoyado en la boca.
   * `alto`: el jarro por encima de la cabeza (el brindis); el hombro sube con
   * él y el jarro deja de acompañar a la boca.
   */
  const CAFE = [
    { dy: 0,     taza: [12.7, 10.1, 0] },               // 0 lo sostiene
    { dy: 0.1,   taza: [12.2, 10.8, 0.08] },            // 1 sopla
    { dy: 0.35,  taza: [11.3, 11.8, 0] },               // 2 lo huele
    { dy: -0.15, boca: -0.55 },                         // 3 sorbo
    { dy: -0.3,  boca: -0.75 },                         // 4 sorbo largo
    { dy: 0,     taza: [12.7, 10.3, 0] },               // 5 traga
    { dy: -0.1,  taza: [12.8, 10.4, 0.05] },            // 6 "ahh"
    // El brindis se estira apenas: con -0.4 el pelo de Rick tocaba el borde de
    // arriba a escala 2. El salto lo pone `plt-pop`, que mueve el canvas.
    { dy: -0.25, taza: [13.9, 3.0, 0.15], alto: true }, // 7 brindis
  ];
  // Sólo se apaga el brazo derecho, que lo dibuja propCoffee hasta el asa. El
  // izquierdo lo pinta drawArms como en cualquier pose, así que sale con la
  // manga de la bata, el guante del personaje o la muñequera de los Dragon
  // Ball sin que el café tenga que saber de ninguno. Hasta el brindis: con el
  // puño también en alto eran dos brazos levantando, justo lo que se sacó.
  function poseCafe(i, cara) {
    return Object.assign({ prop: 'coffee', propPhase: i, armR: false, bodyDY: CAFE[i].dy }, cara);
  }

  /*
   * La altura de la boca del personaje, para el sorbo y para bajar el jarro
   * con la cara. Boberto la tiene en 10,7; los personajes con los ojos más
   * abajo (los Dragon Ball en chibi, con `ojos.y` cerca de 10) también la
   * tienen más abajo, a unas dos unidades de los ojos. Nunca sube de 10,7:
   * un jarro más alto le taparía el ojo a quien tiene los ojos altos.
   */
  function bocaTaza(ch) {
    return ch && ch.ojos ? Math.max(10.7, ch.ojos.y + 2.0) : 10.7;
  }

  // Dónde queda el jarro en un cuadro: [x, y, giro] de la boca del jarro.
  function lugarTaza(f, ch) {
    const cara = bocaTaza(ch);
    if (f.boca == null) {
      const t = f.taza;
      // El brindis va por encima de la cabeza: no acompaña a la boca.
      return [t[0], f.alto ? t[1] : t[1] + cara - 10.7, t[2]];
    }
    // El labio izquierdo del jarro, apoyado apenas a la derecha del centro de
    // la boca: tapa esa mitad, como un labio contra el borde.
    const a = f.boca;
    const lx = BODY.cx + 0.55, ly = cara + f.dy + 0.15;
    const r = JARRO.r * JARRO.k;
    return [lx + r * Math.cos(a), ly + r * Math.sin(a), a];
  }

  // El cuerpo del jarro: de la boca al fondo, con el frente de la boca como
  // borde de arriba. Un solo trazo, para que el contorno no quede cruzado.
  function cuerpoJarroPath(c) {
    const J = JARRO, PI = Math.PI;
    c.beginPath();
    c.moveTo(-J.r, 0);
    c.lineTo(-J.rf, J.h - 0.3);
    c.ellipse(0, J.h - 0.3, J.rf, 0.3, 0, PI, 0, true);
    c.lineTo(J.r, 0);
    c.ellipse(0, 0, J.r, J.ry, 0, 0, PI, false);
    c.closePath();
  }

  /*
   * La marca "DBA" del frente del jarro, en coordenadas del jarro: gira y se
   * inclina con él porque se pinta en su mismo marco.
   *
   * - La etiqueta es un cilindro de base de datos visto de frente (tapa en
   *   elipse arriba, fondo curvo abajo), en crema con el contorno oscuro del
   *   jarro. Es el ícono de siempre de "base de datos", así que dice de quién
   *   es el jarro aun cuando las letras no entran.
   * - Las letras son trazos armados a mano, no fillText: la tipografía cambia
   *   con el tema y con el sistema, y una marca no puede cambiar de dibujo.
   *   Son condensadas y de esquinas redondeadas, una sola familia: la D y la B
   *   redondean a la derecha, la A arriba. Los trazos horizontales van más
   *   finos que los verticales para que los huecos de la B sigan abiertos.
   * - Por debajo de MARCA_LETRAS_PX las letras serían una mancha. Ahí queda
   *   sólo la etiqueta, limpia, que a esa escala se sigue leyendo como tal:
   *   tres manchas oscuras sobre crema se leían como suciedad, no como texto.
   */
  const MARCA = { cx: 0.05, w2: 0.92, e: 0.13, y0: 1.04, y1: 1.98, letraY: 1.25, letraH: 0.64,
    letraW: 0.46, sep: 0.1, tv: 0.14, th: 0.11 };
  // Medido renderizando coffeeHold con letras a escalas 4 a 8 del motor (5,2
  // a 10,4 px por unidad del jarro): a 4 son tres borrones, a 5 se lee
  // "DBII" (el hueco de la A se cierra), desde 6 (7,8 px) se lee "DBA". El
  // topbar, a 1,4–2 y aun con pantalla retina, queda siempre con la etiqueta.
  const MARCA_LETRAS_PX = 7.5;

  function marcaDBA(c, pxU) {
    const C = CAFE_COL, M = MARCA, PI = Math.PI;
    const x0 = M.cx - M.w2, x1 = M.cx + M.w2;

    // -- Etiqueta: el costado del cilindro, con la luz de la izquierda como
    //    el esmalte.
    c.beginPath();
    c.moveTo(x0, M.y0);
    c.lineTo(x0, M.y1);
    c.ellipse(M.cx, M.y1, M.w2, M.e, 0, PI, 0, true);
    c.lineTo(x1, M.y0);
    c.ellipse(M.cx, M.y0, M.w2, M.e, 0, 0, PI, true);
    c.closePath();
    const g = c.createLinearGradient(x0, 0, x1, 0);
    g.addColorStop(0, C.etiqueta); g.addColorStop(0.3, '#fffaf0'); g.addColorStop(1, C.etiquetaSombra);
    c.fillStyle = g; c.fill();
    c.lineWidth = 0.08; c.strokeStyle = C.contorno; c.stroke();
    // La tapa: la mitad de adelante de la elipse de arriba, más clara.
    ellipsePath(c, M.cx, M.y0, M.w2, M.e);
    c.fillStyle = '#fffdf8'; c.fill();
    c.beginPath();
    c.ellipse(M.cx, M.y0, M.w2, M.e, 0, 0, PI, false);
    c.lineWidth = 0.06; c.strokeStyle = withA(C.letra, 0.6); c.stroke();

    if (!(pxU >= MARCA_LETRAS_PX)) return;

    // -- Letras: D, B y A, centradas en la etiqueta.
    const w = M.letraW, h = M.letraH, tv = M.tv, th = M.th;
    const yT = M.letraY, yB = yT + h;
    const total = 3 * w + 2 * M.sep;
    let lx = M.cx - total / 2;
    c.fillStyle = C.letra;

    // D: asta, dos barras y el lado derecho redondeado; el hueco sigue la
    // misma curva, más cerrada.
    {
      const R = 0.2, Ri = Math.max(0.03, R - tv);
      c.beginPath();
      c.moveTo(lx, yT);
      c.lineTo(lx + w - R, yT);
      c.arcTo(lx + w, yT, lx + w, yT + R, R);
      c.lineTo(lx + w, yB - R);
      c.arcTo(lx + w, yB, lx + w - R, yB, R);
      c.lineTo(lx, yB);
      c.closePath();
      c.moveTo(lx + tv, yT + th);
      c.lineTo(lx + w - tv - Ri, yT + th);
      c.arcTo(lx + w - tv, yT + th, lx + w - tv, yT + th + Ri, Ri);
      c.lineTo(lx + w - tv, yB - th - Ri);
      c.arcTo(lx + w - tv, yB - th, lx + w - tv - Ri, yB - th, Ri);
      c.lineTo(lx + tv, yB - th);
      c.closePath();
      c.fill('evenodd');
    }
    lx += w + M.sep;

    // B: el panzón de arriba un poco más angosto que el de abajo, con la
    // cintura marcada; dos huecos.
    {
      const R = 0.16, wt = w - 0.04, m = yT + h * 0.47;
      c.beginPath();
      c.moveTo(lx, yT);
      c.lineTo(lx + wt - R, yT);
      c.arcTo(lx + wt, yT, lx + wt, yT + R, R);
      c.lineTo(lx + wt, m - 0.1);
      c.quadraticCurveTo(lx + wt, m, lx + w - 0.12, m);
      c.quadraticCurveTo(lx + w, m, lx + w, m + 0.12);
      c.lineTo(lx + w, yB - R);
      c.arcTo(lx + w, yB, lx + w - R, yB, R);
      c.lineTo(lx, yB);
      c.closePath();
      c.rect(lx + tv, yT + th, wt - 2 * tv, m - th / 2 - (yT + th));
      c.rect(lx + tv, m + th / 2, w - 2 * tv, yB - th - (m + th / 2));
      c.fill('evenodd');
    }
    lx += w + M.sep;

    // A: arco de hombros redondos, travesaño y el hueco de arriba.
    {
      const R = 0.2, Ri = Math.max(0.03, R - tv), ca = yT + h * 0.5, cb = ca + th;
      c.beginPath();
      c.moveTo(lx, yB);
      c.lineTo(lx, yT + R);
      c.arcTo(lx, yT, lx + R, yT, R);
      c.lineTo(lx + w - R, yT);
      c.arcTo(lx + w, yT, lx + w, yT + R, R);
      c.lineTo(lx + w, yB);
      c.lineTo(lx + w - tv, yB);
      c.lineTo(lx + w - tv, cb);
      c.lineTo(lx + tv, cb);
      c.lineTo(lx + tv, yB);
      c.closePath();
      c.moveTo(lx + tv, ca);
      c.lineTo(lx + tv, yT + th + Ri);
      c.arcTo(lx + tv, yT + th, lx + tv + Ri, yT + th, Ri);
      c.lineTo(lx + w - tv - Ri, yT + th);
      c.arcTo(lx + w - tv, yT + th, lx + w - tv, yT + th + Ri, Ri);
      c.lineTo(lx + w - tv, ca);
      c.closePath();
      c.fill('evenodd');
    }
  }

  // `pxU`: píxeles de dispositivo por unidad del jarro, para que la marca
  // decida cuánto detalle entra (ver marcaDBA). Sin dato, el detalle entero.
  function pintarJarro(c, a, pxU) {
    const C = CAFE_COL, J = JARRO;
    c.lineJoin = 'round'; c.lineCap = 'round';

    // -- Asa: va primero, así sus puntas quedan debajo del cuerpo.
    c.beginPath();
    c.moveTo(J.r - 0.1, 0.5);
    c.bezierCurveTo(J.r + 1.2, 0.3, J.r + 1.25, 2.15, J.rf - 0.02, 2.05);
    c.lineWidth = 0.66; c.strokeStyle = C.contorno; c.stroke();
    c.lineWidth = 0.36; c.strokeStyle = C.esmalte; c.stroke();

    // -- Cuerpo: esmalte con la luz de la izquierda, contorno oscuro y filete
    //    claro por dentro (el oscuro lo separa de un cuerpo claro, el filete
    //    de uno oscuro).
    cuerpoJarroPath(c);
    const g = c.createLinearGradient(-J.r, 0, J.r, 0);
    g.addColorStop(0, C.esmalte); g.addColorStop(0.28, C.esmalteLuz);
    g.addColorStop(0.55, C.esmalte); g.addColorStop(1, C.esmalteSombra);
    c.fillStyle = g; c.fill();
    c.lineWidth = 0.24; c.strokeStyle = C.contorno; c.stroke();
    c.save();
    c.clip();
    c.lineWidth = 0.16; c.strokeStyle = withA(C.filete, 0.45); c.stroke();
    // Brillo vertical del esmalte.
    c.beginPath();
    c.moveTo(-0.62, 0.55); c.lineTo(-0.55, J.h - 0.55);
    c.lineWidth = 0.22; c.strokeStyle = withA('#ffffff', 0.38); c.stroke();
    c.restore();

    // -- Estampa: la marca del equipo en el frente.
    marcaDBA(c, pxU);

    // -- Boca: el enlozado blanco de adentro y el café. El café queda
    //    horizontal aunque el jarro se incline: se corre hacia el labio que
    //    baja y se ensancha, que es lo que dice que el jarro se vuelca.
    ellipsePath(c, 0, 0, J.r, J.ry);
    const gi = c.createLinearGradient(0, -J.ry, 0, J.ry);
    gi.addColorStop(0, C.interiorSombra); gi.addColorStop(1, C.interior);
    c.fillStyle = gi; c.fill();
    c.save();
    c.clip();
    const k = Math.min(1, Math.abs(a) / 0.65);
    const cx = a < 0 ? -0.55 * k : 0, cy = 0.1 - 0.06 * k;
    c.save();
    c.translate(cx, cy);
    c.rotate(-a);
    ellipsePath(c, 0, 0, 1.02 + 0.3 * k, 0.3);
    c.fillStyle = C.cafe; c.fill();
    // La crema: un arco claro sobre el café.
    c.beginPath();
    c.ellipse(-0.1, -0.02, 0.62, 0.14, 0, Math.PI * 1.1, Math.PI * 1.9);
    c.lineWidth = 0.1; c.strokeStyle = withA(C.crema, 0.9); c.stroke();
    c.restore();
    c.restore();
    // El borde de esmalte oscuro, como en un jarro enlozado de verdad.
    ellipsePath(c, 0, 0, J.r, J.ry);
    c.lineWidth = 0.3; c.strokeStyle = C.contorno; c.stroke();
    c.lineWidth = 0.14; c.strokeStyle = C.borde; c.stroke();
  }

  function propCoffee(c, P, pose) {
    const f = CAFE[pose.propPhase || 0] || CAFE[0];
    const ch = charActual();
    const T = llevaBata() ? _bataTela : null;
    const dy = pose.bodyDY || 0;
    const [x, y, a] = lugarTaza(f, ch);
    const ca = Math.cos(a), sa = Math.sin(a);
    // De coordenadas del jarro a las del lienzo.
    const K = JARRO.k;
    const asa = [x + (JARRO_ASA[0] * ca - JARRO_ASA[1] * sa) * K, y + (JARRO_ASA[0] * sa + JARRO_ASA[1] * ca) * K];

    // El brazo va DETRÁS del jarro y la mano, encima: así agarra el asa.
    const hombroD = f.alto ? [13.1, 9.4 + dy] : [13.1, 10.4 + dy];
    brazoGuitarra(c, P, T, ch, 1, hombroD[0], hombroD[1], asa[0], asa[1]);

    c.save();
    c.translate(x, y);
    c.rotate(a);
    c.scale(K, K);
    pintarJarro(c, a, _k * K);
    c.restore();

    manoGuitarra(c, P, asa[0], asa[1]);
  }

  /*
   * Dónde tiene el jarro una pose de café, para quien se lo tenga que dar o
   * sacar de la mano (el corto de la landing lo trae desde la mesa).
   *
   * Sale de la MISMA tabla y del mismo cálculo que usa propCoffee, así que no
   * se puede separar del dibujo: el corto tenía esa posición copiada a mano y,
   * cuando se rehízo el jarro, el pase de la mesa a la mano quedó saltando.
   * También depende del personaje (bocaTaza baja el jarro con los ojos), cosa
   * que un número copiado no podía seguir.
   *
   * Las coordenadas son las del LIENZO (las de box(), con el aire incluido),
   * porque es lo que ve quien compone el canvas. `escala`: unidades de lienzo
   * por unidad del jarro. `alto`: del centro de la boca al fondo, en unidades
   * del jarro, para apoyarlo sobre una mesa.
   */
  function tazaDePose(poseName) {
    const pose = POSES[poseName];
    if (!pose || pose.prop !== 'coffee') return null;
    const f = CAFE[pose.propPhase || 0] || CAFE[0];
    const [x, y, giro] = lugarTaza(f, charActual());
    return { x: x + PAD.l, y: y + PAD.t, giro, escala: JARRO.k, alto: JARRO.h };
  }

  /**
   * El aro de burbujas, con la mano que lo sostiene y la burbuja que sopla.
   *
   * La versión anterior era un círculo del color de la utilería del tema que
   * aparecía de la nada al costado de la cara, crecía en tres saltos y se
   * rompía en cinco arcos. Ahora, con la receta de la guitarra y el café:
   *
   * - Un aro de verdad, de plástico de colores FIJOS con contorno oscuro: el
   *   palito, el aro (visto de costado, por eso es una elipse) y la película
   *   de jabón adentro cuando todavía no sopló. Lo sostiene UNA mano, la
   *   derecha, por el hook `brazo` del personaje; el otro brazo queda en
   *   reposo, pintado por el motor.
   * - Sopla con el aro delante de la boca y la burbuja crece PEGADA al aro,
   *   hacia afuera; después se suelta, flota hacia arriba y revienta. El aro
   *   sigue a la boca de cada personaje (bocaTaza, la misma que usa el café).
   * - La burbuja es de colores fijos: casi transparente, con el borde
   *   tornasolado (un degradé cónico de rosa, amarillo, verde agua y lila),
   *   la ventana de luz arriba a la izquierda y un filo oscuro muy suave, que
   *   es lo que la separa de un tema claro. Por debajo de BURBUJA_DETALLE_PX
   *   queda el borde y un solo brillo: a escala de topbar se sigue leyendo
   *   como burbuja.
   *
   * Cuadros clave (propPhase). `aro`: el centro del aro, con `y` relativa a
   * la boca cuando `alaBoca`; `giro`: inclinación del palito desde la
   * vertical (negativo, hacia la derecha), que también da hacia dónde sopla
   * (perpendicular al palito). `r`: la burbuja pegada al aro. `libre`: la
   * burbuja suelta, [x, y, r] en el lienzo. `pop`: dónde revienta.
   */
  const BURBUJA_COL = {
    contorno: '#26121d',
    plastico: '#e2508f', plasticoLuz: '#ff9cc8', plasticoSombra: '#992455',
    pelicula: '#d9f1ff', filo: '#1e2a40',
    // El tornasol, en orden alrededor del borde (cierra en el mismo color).
    iris: ['#ff8ad8', '#ffe07a', '#7ff0e6', '#8fb4ff', '#d69bff', '#ff8ad8'],
  };
  // El aro: radio a lo largo del palito y de costado. `palo`: hasta dónde
  // llega el palito desde el centro del aro; `agarre`: dónde lo toma la mano.
  const AROB = { rl: 1.2, rc: 0.5, palo: 4.1, agarre: 3.3 };
  const BURBUJA_DETALLE_PX = 5;

  const BURBUJA = [
    { aro: [14.3, 10.0], giro: -0.15, dy: 0 },                   // 0 saca el aro
    { aro: [9.5, -0.2], alaBoca: true, giro: -0.45, dy: 0.1 },   // 1 lo lleva a la boca
    { aro: [9.5, -0.2], alaBoca: true, giro: -0.35, dy: 0, r: 1.1 },      // 2 sopla (bubbleS)
    { aro: [9.5, -0.2], alaBoca: true, giro: -0.3, dy: -0.05, r: 1.85 },  // 3 crece (bubbleM)
    { aro: [9.5, -0.2], alaBoca: true, giro: -0.25, dy: -0.1, r: 2.7 },   // 4 enorme (bubbleL)
    { aro: [13.8, 11.4], giro: -0.25, dy: 0, libre: [13.2, 6.3, 2.3] },   // 5 se suelta
    { aro: [13.8, 11.4], giro: -0.25, dy: 0, libre: [13.9, 4.6, 2.25] },  // 6 sube
    { aro: [13.8, 11.4], giro: -0.25, dy: -0.1, pop: [13.9, 4.6] },       // 7 revienta (bubblePop)
  ];
  function poseBurbuja(i, cara) {
    return Object.assign({ prop: 'burbuja', propPhase: i, armR: false, bodyDY: BURBUJA[i].dy }, cara);
  }

  // El borde tornasolado: cónico si el navegador lo tiene; si no, lineal, que
  // pierde la vuelta completa pero conserva los colores.
  function irisBurbuja(c, x, y, r) {
    const C = BURBUJA_COL;
    const g = c.createConicGradient ? c.createConicGradient(-0.6, x, y) : c.createLinearGradient(x - r, y - r, x + r, y + r);
    C.iris.forEach((col, i) => g.addColorStop(i / (C.iris.length - 1), col));
    return g;
  }

  function pintarBurbuja(c, x, y, r, det) {
    const C = BURBUJA_COL, PI = Math.PI;
    // El cuerpo: casi transparente, con el canto más denso que el centro.
    ellipsePath(c, x, y, r, r);
    const g = c.createRadialGradient(x - r * 0.3, y - r * 0.35, r * 0.1, x, y, r);
    g.addColorStop(0, withA('#ffffff', 0.14));
    g.addColorStop(0.6, withA(C.pelicula, 0.08));
    g.addColorStop(1, withA(C.pelicula, 0.42));
    c.fillStyle = g; c.fill();
    // El filo oscuro, muy suave, por fuera del tornasol. Sin detalle (el
    // topbar) va más marcado, igual que el borde: si no, sobre el cuerpo la
    // burbuja se pierde.
    c.lineWidth = det ? 0.12 : 0.18; c.strokeStyle = withA(C.filo, det ? 0.35 : 0.55); c.stroke();
    // El borde tornasolado.
    c.save();
    c.globalAlpha = 0.92;
    ellipsePath(c, x, y, r - 0.13, r - 0.13);
    c.lineWidth = det ? 0.28 : 0.42; c.strokeStyle = irisBurbuja(c, x, y, r); c.stroke();
    if (det) {
      c.globalAlpha = 0.35;
      ellipsePath(c, x, y, r - 0.42, r - 0.42);
      c.lineWidth = 0.18; c.stroke();
    }
    c.restore();
    // La ventana de luz arriba a la izquierda, y el reflejo de abajo.
    c.lineCap = 'round';
    c.beginPath();
    c.arc(x, y, r * 0.7, PI * 1.12, PI * 1.42);
    c.lineWidth = Math.max(0.2, r * 0.1); c.strokeStyle = withA('#ffffff', 0.9); c.stroke();
    if (det) {
      ellipsePath(c, x + r * 0.05, y - r * 0.62, r * 0.07 + 0.04, r * 0.07 + 0.04);
      c.fillStyle = withA('#ffffff', 0.8); c.fill();
      c.beginPath();
      c.arc(x, y, r * 0.78, PI * 0.1, PI * 0.38);
      c.lineWidth = Math.max(0.1, r * 0.05); c.strokeStyle = withA('#ffffff', 0.35); c.stroke();
    }
  }

  // El estallido: el borde partido en arcos que se abren y gotitas afuera.
  function popBurbuja(c, x, y) {
    const C = BURBUJA_COL, R = 2.5;
    c.lineCap = 'round';
    for (let i = 0; i < 7; i++) {
      const a = (i / 7) * Math.PI * 2 + 0.2;
      c.beginPath();
      c.arc(x, y, R, a, a + 0.42);
      c.lineWidth = 0.34; c.strokeStyle = withA(C.filo, 0.3); c.stroke();
      c.lineWidth = 0.2; c.strokeStyle = C.iris[i % 5]; c.stroke();
      const b = a + 0.62;
      ellipsePath(c, x + Math.cos(b) * (R + 0.45), y + Math.sin(b) * (R + 0.45), 0.2, 0.2);
      c.fillStyle = withA(C.pelicula, 0.9); c.fill();
      c.lineWidth = 0.08; c.strokeStyle = withA(C.filo, 0.4); c.stroke();
    }
  }

  // El aro y el palito, en coordenadas del aro: origen en su centro y el
  // palito hacia +y. `pelicula`: la película de jabón adentro.
  function pintarAroBurbuja(c, pelicula) {
    const C = BURBUJA_COL, A = AROB;
    c.lineCap = 'round'; c.lineJoin = 'round';
    // El palito, del borde del aro a la punta.
    c.beginPath();
    c.moveTo(0, A.rl); c.lineTo(0, A.palo);
    c.lineWidth = 0.56; c.strokeStyle = C.contorno; c.stroke();
    c.lineWidth = 0.32; c.strokeStyle = C.plastico; c.stroke();
    c.beginPath();
    c.moveTo(-0.07, A.rl + 0.25); c.lineTo(-0.07, A.palo - 0.25);
    c.lineWidth = 0.09; c.strokeStyle = withA(C.plasticoLuz, 0.8); c.stroke();
    if (pelicula) {
      ellipsePath(c, 0, 0, A.rc, A.rl);
      c.fillStyle = withA(C.pelicula, 0.32); c.fill();
    }
    // El aro: contorno oscuro y plástico con la luz de la izquierda.
    ellipsePath(c, 0, 0, A.rc, A.rl);
    c.lineWidth = 0.56; c.strokeStyle = C.contorno; c.stroke();
    const g = c.createLinearGradient(-A.rc, -A.rl, A.rc, A.rl);
    g.addColorStop(0, C.plasticoLuz); g.addColorStop(0.5, C.plastico); g.addColorStop(1, C.plasticoSombra);
    c.lineWidth = 0.3; c.strokeStyle = g; c.stroke();
  }

  function propBurbuja(c, P, pose) {
    const f = BURBUJA[pose.propPhase || 0] || BURBUJA[0];
    const ch = charActual();
    const T = llevaBata() ? _bataTela : null;
    const dy = pose.bodyDY || 0;
    const det = _k >= BURBUJA_DETALLE_PX;
    const x = f.aro[0], y = (f.alaBoca ? bocaTaza(ch) + f.aro[1] : f.aro[1]) + dy;
    const sa = Math.sin(f.giro), ca = Math.cos(f.giro);
    const hx = x - AROB.agarre * sa, hy = y + AROB.agarre * ca;

    // El brazo va detrás del palito y la mano, encima: así lo agarra.
    brazoGuitarra(c, P, T, ch, 1, 13.1, 10.4 + dy, hx, hy);
    c.save();
    c.translate(x, y);
    c.rotate(f.giro);
    pintarAroBurbuja(c, !f.r);
    c.restore();
    manoGuitarra(c, P, hx, hy);

    // La burbuja pegada al aro crece hacia donde sopla (perpendicular al
    // palito), con el borde pasando por el aro.
    if (f.r) pintarBurbuja(c, x + (f.r - 0.3) * ca, y + (f.r - 0.3) * sa, f.r, det);
    if (f.libre) pintarBurbuja(c, f.libre[0], f.libre[1], f.libre[2], det);
    if (f.pop) popBurbuja(c, f.pop[0], f.pop[1]);
  }

  /**
   * La patineta, con el personaje arriba.
   *
   * La versión anterior era una barra con el degradé de la utilería y dos
   * puntos: el cuerpo flotaba dos unidades y media por encima, sin pies, y la
   * tabla tomaba el color del tema (naranja sobre un Boberto naranja). Ahora,
   * con la receta de la guitarra:
   *
   * - Colores FIJOS con contorno oscuro: lija gris oscura arriba, canto de
   *   maple, gráfica roja abajo, ejes de metal y ruedas de uretano claras. Se
   *   lee sobre cualquier cuerpo y cualquier tema.
   * - Se ve apenas desde arriba (VISTA_SKATE): asoma la lija y la rueda de
   *   atrás queda detrás de la tabla. De perfil puro una tabla es una raya.
   * - Tiene volumen de verdad (largo, ancho y espesor) y se proyecta girando
   *   sobre su eje largo. Eso es lo que permite el kickflip: los cuadros del
   *   giro muestran la gráfica, las ruedas arriba y la lija, en vez de una
   *   barra que se achica.
   * - Los pies (`pies` en la pose) quedan plantados en la lija y el cuerpo se
   *   agacha o salta sobre ellos; los brazos, abiertos para equilibrar, los
   *   dibuja brazosSkate por delante del cuerpo, como la guitarra.
   * - Todo queda adentro del lienzo: la tabla va por DETRÁS del cuerpo y las
   *   ruedas terminan 1,8 u antes del borde de abajo (antes, 0,7: a escala 1,4
   *   la rueda tocaba la última fila). El salto grande lo pone la clase CSS
   *   `plt-skatehop`, que mueve el canvas entero: el dibujo nunca sube más que
   *   el skate de siempre (bodyDY -1.2), así el pelo de los Dragon Ball, las
   *   orejas de Pikachu y los sombreros no se salen por arriba.
   */
  const SKATE_COL = {
    contorno: '#17120e', lija: '#2e3139', lijaGrano: '#565c69',
    canto: '#ecc88f', cantoSombra: '#b88a52', capa: '#9c6c3a',
    grafica: '#e0533d', graficaFranja: '#ffc24a',
    eje: '#c3c9d2', ejeSombra: '#7d8591',
    rueda: '#f4ecd6', ruedaSombra: '#c9b994', ruleman: '#5f6570',
  };
  // Medidas de la tabla, en unidades del personaje. `y`: el centro de la tabla
  // en reposo; `L`: medio largo; `t`: espesor; `w`: ancho; `cola`: cuánto sube
  // cada punta y en qué largo; `eje`: dónde van los ejes desde el centro;
  // `yEje`: el eje de las ruedas bajo el centro de la tabla; `r`: la rueda.
  const SK = { cx: 8, y: 15.85, L: 5.1, t: 0.44, w: 2.3, cola: [0.55, 1.35], eje: 3.2, yEje: 0.78, anchoEje: 1.0, r: 0.52 };
  // Cuánto se la ve desde arriba (radianes de giro sobre el eje largo).
  const VISTA_SKATE = -0.25;

  /*
   * Cuadros clave (propPhase). `dy`: bodyDY del cuerpo. `tabla`: [alza, cabeceo,
   * giro], alza en unidades (negativo es arriba), cabeceo en radianes
   * (negativo levanta la punta de adelante) y giro sobre el eje largo (el
   * kickflip). `rueda`: ángulo de la marca de las ruedas, para que ruede.
   * `pies` en el lienzo y `manos` [xI, yI, xD, yD] con el cuerpo quieto: se
   * les suma `dy`, para que acompañen al cuerpo cuando se agacha o salta.
   *
   * Con el cuerpo en -0.75 los pies apoyan en la lija sin que la tabla se
   * separe del piso; nada sube más que -1.2, el alto que ya se medía con
   * todos los personajes y accesorios.
   */
  const PIES_TABLA = [[5.7, 14.85], [10.4, 14.85]];
  const SKATE = [
    { dy: -0.75, tabla: [0, 0, 0],        rueda: 0,   pies: PIES_TABLA, manos: [0.9, 12.0, 15.1, 11.6] },  //  0 rueda A
    { dy: -0.75, tabla: [0, 0, 0],        rueda: 1.6, pies: PIES_TABLA, manos: [1.0, 11.6, 15.0, 12.0] },  //  1 rueda B
    { dy: -0.40, tabla: [0, 0, 0],        rueda: 0,   pies: PIES_TABLA, manos: [0.5, 10.2, 15.5, 10.4] },  //  2 se sube
    { dy: -0.50, tabla: [0, 0, 0],        rueda: 0.8, pies: [[2.7, 16.3], [10.4, 14.85]], manos: [1.9, 12.3, 15.2, 10.4] },  //  3 empuja
    { dy: -0.60, tabla: [0, 0, 0],        rueda: 2.4, pies: [[1.6, 15.7], [10.4, 14.85]], manos: [1.3, 12.8, 15.4, 10.0] },  //  4 patada atrás
    { dy: -0.30, tabla: [0, 0, 0],        rueda: 0,   pies: PIES_TABLA, manos: [1.4, 13.2, 14.6, 13.2] },  //  5 se agacha
    { dy: -1.20, tabla: [-0.5, -0.25, 0], rueda: 0.8, pies: [[4.9, 15.15], [10.5, 13.75]], manos: [0.3, 9.4, 15.7, 9.0] },  //  6 pop
    { dy: -1.20, tabla: [-0.05, 0, 1.35], rueda: 1.6, pies: [[5.3, 14.2], [10.7, 14.2]], manos: [0.2, 8.8, 15.8, 8.6] },  //  7 kickflip ¼
    { dy: -1.20, tabla: [0.3, 0, 2.9],    rueda: 2.4, pies: [[5.3, 14.2], [10.7, 14.2]], manos: [0.2, 8.6, 15.8, 8.8] },  //  8 kickflip ½
    { dy: -1.20, tabla: [0.2, 0.05, 4.5], rueda: 3.2, pies: [[5.3, 14.2], [10.7, 14.2]], manos: [0.3, 8.8, 15.7, 8.6] },  //  9 kickflip ¾
    { dy: -1.10, tabla: [-0.1, 0, 0],     rueda: 0,   pies: [[5.6, 14.55], [10.5, 14.55]], manos: [0.4, 9.6, 15.6, 9.6] },  // 10 la atrapa
    { dy: -0.30, tabla: [0, 0, 0],        rueda: 0.8, pies: PIES_TABLA, manos: [0.6, 11.2, 15.4, 11.2] },  // 11 cae
    { dy: -0.75, tabla: [0, 0, 0],        rueda: 1.6, pies: PIES_TABLA, manos: [1.5, 4.6, 14.5, 4.6] },    // 12 remate
  ];
  const cuadroSkate = (pose) => SKATE[pose.propPhase || 0] || SKATE[0];
  // La pose de un cuadro: el cuerpo y los pies salen de la tabla, la cara la
  // pone quien la declara. Los brazos del motor se apagan: los pone brazosSkate.
  function poseSkate(i, cara) {
    return Object.assign({ prop: 'skate', propPhase: i, armL: false, armR: false, bodyDY: SKATE[i].dy, pies: SKATE[i].pies }, cara);
  }

  // Cuánto sube cada punta en `u` (a lo largo de la tabla): plana en el medio
  // y curvada en las colas.
  function colaSkate(u) {
    const d = Math.abs(u) - (SK.L - SK.cola[1]);
    return d > 0 ? -SK.cola[0] * Math.pow(d / SK.cola[1], 2) : 0;
  }
  // Medio ancho de la tabla en `u`: las puntas son redondas vistas de frente.
  function anchoSkate(u) {
    const R = SK.w / 2, d = Math.abs(u) - (SK.L - R);
    return d > 0 ? R * Math.sqrt(Math.max(0, 1 - (d / R) * (d / R))) : R;
  }

  function propSkate(c, P, pose) {
    const f = cuadroSkate(pose);
    const C = SKATE_COL;
    const giro = f.tabla[2] + VISTA_SKATE;
    const cg = Math.cos(giro), sg = Math.sin(giro);
    // De la tabla (u a lo largo, y hacia la lija abajo, z hacia quien mira) a
    // la pantalla: el giro es sobre el eje largo, así que u no cambia.
    const Y = (y, z) => y * cg - z * sg;
    const prof = (y, z) => y * sg + z * cg;

    c.save();
    c.translate(SK.cx, SK.y + f.tabla[0]);
    c.rotate(f.tabla[1]);
    c.lineJoin = 'round'; c.lineCap = 'round';

    // Una cara de la tabla: la tira entre dos esquinas del corte transversal,
    // recorrida a lo largo. Todas se angostan en las puntas (anchoSkate), así
    // la nariz y la cola son redondas de frente y terminan en canto de perfil.
    const N = 24;
    const cara = (e1, e2) => {
      c.beginPath();
      for (let i = 0; i <= N; i++) {
        const u = -SK.L + (2 * SK.L * i) / N, k = anchoSkate(u) / (SK.w / 2);
        const y = e1[0] + colaSkate(u);
        const px = u, py = Y(y, e1[1] * k);
        i ? c.lineTo(px, py) : c.moveTo(px, py);
      }
      for (let i = N; i >= 0; i--) {
        const u = -SK.L + (2 * SK.L * i) / N, k = anchoSkate(u) / (SK.w / 2);
        c.lineTo(u, Y(e2[0] + colaSkate(u), e2[1] * k));
      }
      c.closePath();
    };

    const t = SK.t / 2, w = SK.w / 2;
    // Esquinas del corte: lija (arriba), canto de adelante, gráfica, canto de atrás.
    const A = [-t, -w], B = [-t, w], Cc = [t, w], D = [t, -w];
    const piezas = [];
    // Ejes y ruedas: cada rueda en su profundidad, para que la de atrás quede
    // tapada por la tabla y la de adelante por encima.
    [-1, 1].forEach(lado => {
      const u = lado * SK.eje, yb = t + colaSkate(u), ya = SK.yEje;
      piezas.push({ z: prof(ya, 0) - 0.01, pinta: () => {
        // Base y colgante del eje, en el plano de la tabla.
        c.beginPath();
        c.moveTo(u - 0.55, Y(yb, 0)); c.lineTo(u + 0.55, Y(yb, 0));
        c.lineTo(u + 0.26, Y(ya, 0)); c.lineTo(u - 0.26, Y(ya, 0));
        c.closePath();
        c.fillStyle = C.eje; c.fill();
        c.lineWidth = 0.1; c.strokeStyle = C.contorno; c.stroke();
        // El eje de las ruedas, de una a la otra.
        c.beginPath();
        c.moveTo(u, Y(ya, -SK.anchoEje)); c.lineTo(u, Y(ya, SK.anchoEje));
        c.lineWidth = 0.34; c.strokeStyle = C.contorno; c.stroke();
        c.lineWidth = 0.18; c.strokeStyle = C.ejeSombra; c.stroke();
      } });
      [-1, 1].forEach(zs => {
        const z = zs * SK.anchoEje;
        piezas.push({ z: prof(ya, z), pinta: () => {
          const x = u, y = Y(ya, z);
          ellipsePath(c, x, y, SK.r, SK.r);
          const g = c.createRadialGradient(x - 0.18, y - 0.2, 0.05, x, y, SK.r);
          g.addColorStop(0, C.rueda); g.addColorStop(1, C.ruedaSombra);
          c.fillStyle = g; c.fill();
          c.lineWidth = 0.13; c.strokeStyle = C.contorno; c.stroke();
          // Rulemán y una marca que gira: es lo que dice que la rueda rueda.
          ellipsePath(c, x, y, 0.17, 0.17);
          c.fillStyle = C.ruleman; c.fill();
          const a = f.rueda || 0;
          c.beginPath();
          c.moveTo(x + Math.cos(a) * 0.24, y + Math.sin(a) * 0.24);
          c.lineTo(x + Math.cos(a) * 0.44, y + Math.sin(a) * 0.44);
          c.lineWidth = 0.1; c.strokeStyle = C.ruleman; c.stroke();
        } });
      });
    });

    // La tabla: sólo las caras que miran hacia quien mira. Las visibles de un
    // bloque convexo no se pisan, así que no importa en qué orden van.
    piezas.push({ z: 0, pinta: () => {
      const caras = [
        { e: [A, B], n: [-1, 0], pinta: () => {
          c.fillStyle = C.lija; c.fill();
        } },
        { e: [Cc, D], n: [1, 0], pinta: () => {
          const g = c.createLinearGradient(-SK.L, 0, SK.L, 0);
          g.addColorStop(0, C.grafica); g.addColorStop(0.42, C.grafica);
          g.addColorStop(0.46, C.graficaFranja); g.addColorStop(0.54, C.graficaFranja);
          g.addColorStop(0.58, C.grafica); g.addColorStop(1, C.grafica);
          c.fillStyle = g; c.fill();
        } },
        { e: [B, Cc], n: [0, 1], pinta: () => { c.fillStyle = C.canto; c.fill(); } },
        { e: [D, A], n: [0, -1], pinta: () => { c.fillStyle = C.cantoSombra; c.fill(); } },
      ];
      caras.forEach(k => {
        if (prof(k.n[0], k.n[1]) <= 0.02) return;
        cara(k.e[0], k.e[1]);
        k.pinta();
        c.lineWidth = 0.13; c.strokeStyle = C.contorno; c.stroke();
      });
      // La capa del medio del terciado, en el canto que se ve.
      const zc = prof(0, w) > 0 ? w : -w;
      if (Math.abs(Y(t, zc) - Y(-t, zc)) > 0.25) {
        c.beginPath();
        for (let i = 0; i <= N; i++) {
          const u = -SK.L + 0.5 + ((2 * SK.L - 1) * i) / N;
          const py = Y(colaSkate(u), zc * anchoSkate(u) / w);
          i ? c.lineTo(u, py) : c.moveTo(u, py);
        }
        c.lineWidth = 0.07; c.strokeStyle = C.capa; c.stroke();
      }
      // El grano de la lija: dos puntos claros, si la lija se ve.
      if (prof(-1, 0) > 0.2) {
        c.fillStyle = C.lijaGrano;
        [[-2.4, 0.3], [1.6, -0.35], [3.3, 0.2]].forEach(([u, z]) => {
          ellipsePath(c, u, Y(-t, z), 0.12, 0.12); c.fill();
        });
      }
    } });

    piezas.sort((a, b) => a.z - b.z).forEach(p => p.pinta());
    c.restore();
  }

  /*
   * Los brazos del skate, abiertos para hacer equilibrio (o arriba en el
   * remate). Van por DELANTE del cuerpo, como los de la guitarra, y por el
   * mismo camino: el muñón con el hook `brazo` del personaje y la manga si
   * lleva la bata. La tabla, en cambio, va detrás: por eso esto es aparte.
   */
  function brazosSkate(c, P, pose) {
    const f = cuadroSkate(pose);
    const dy = f.dy, m = f.manos;
    const T = llevaBata() ? _bataTela : null;
    const ch = charActual();
    brazoGuitarra(c, P, T, ch, -1, 2.9, 10.4 + dy, m[0], m[1] + dy);
    brazoGuitarra(c, P, T, ch, 1, 13.1, 10.4 + dy, m[2], m[3] + dy);
    manoGuitarra(c, P, m[0], m[1] + dy);
    manoGuitarra(c, P, m[2], m[3] + dy);
  }

  /**
   * La guitarra criolla, con los dos brazos que la tocan.
   *
   * La versión anterior era una elipse del color del personaje sobre la cara:
   * tapaba la boca, se confundía con un cuerpo del mismo tono y nadie la
   * tocaba (el brazo derecho desaparecía y sólo se movía un punto). Ahora:
   *
   * - Va baja, a la altura de la panza, cruzada como se la sostiene: la caja a
   *   la izquierda de quien mira y el mástil subiendo hacia la derecha. Así la
   *   boca queda libre en todos los personajes.
   * - Los colores son FIJOS (madera, cuerdas claras, contorno oscuro) y no los
   *   de la utilería del tema: la utilería de Boberto sale de la paleta, y una
   *   guitarra naranja sobre un Boberto naranja es exactamente el problema.
   *   No va en UTILERIA_FIJA porque esa tabla sólo llega a los personajes (se
   *   mezcla en su pintor); acá tiene que valer igual para todos.
   * - Contorno oscuro con filete claro por dentro: el oscuro la separa de un
   *   cuerpo claro, el filete de uno oscuro (Gengar, los temas de noche).
   * - Los dos brazos los dibuja el prop, como el puntero: la mano izquierda del
   *   personaje pisa el mástil y la derecha rasguea sobre la boca de la caja.
   *   Van DELANTE del cuerpo y con contorno, porque un muñón del color del
   *   cuerpo apoyado sobre la panza desaparece.
   *
   * Coordenadas locales de la guitarra: x a lo largo del mástil desde el centro
   * de la caja grande, y perpendicular (+ hacia el piso). Rasguear es mover la
   * mano en y; cambiar de posición es mover la del mástil en x.
   */
  const GUITARRA_COL = {
    contorno: '#24160c', filete: '#f1e2c2',
    maderaLuz: '#f3c27a', madera: '#c9803c', maderaSombra: '#8a4a1c',
    boca: '#1b110a', puente: '#2b1a0e',
    mastil: '#4a2b15', diapason: '#2e1b0e', traste: '#d9d3c6', cejuela: '#f1e8d6',
    clavija: '#dcdfe4', cuerda: '#f6f1e7', pua: '#e0533d',
  };
  // Centro de la caja grande, ángulo del mástil y tamaño. Más arriba, la caja
  // chica pisa la sonrisa; más horizontal o más grande, el clavijero se acerca
  // al borde derecho del lienzo.
  const GTR = { x: 4.6, y: 14.6, a: -0.55, k: 1.12 };
  // Hombros: de dónde sale cada brazo (sólo dan la dirección del muñón).
  const GTR_HOMBRO_RASGUEO = [2.9, 10.6], GTR_HOMBRO_MASTIL = [13.4, 10.6];

  /*
   * Cuadros clave (propPhase). `traste`: dónde pisa la mano del mástil (x
   * local; menos es más agudo, más cerca de la caja). `rasgueo`: la mano de la
   * púa en coordenadas locales. `vibra`: cuánto se mueven las cuerdas.
   * `alCielo`: la mano de la púa en coordenadas del personaje, para el remate.
   */
  const GUITARRA = [
    { traste: 9.6, rasgueo: [1.9, -1.00], vibra: 0 },     // 0 sostiene, lista
    { traste: 9.6, rasgueo: [2.0,  1.05], vibra: 1 },     // 1 rasguido abajo
    { traste: 9.6, rasgueo: [1.9,  0.05], vibra: 0.6 },   // 2 a mitad de camino
    { traste: 9.6, rasgueo: [1.8, -1.05], vibra: 0.8 },   // 3 rasguido arriba
    { traste: 7.8, rasgueo: [2.1,  0.35], vibra: 0.5 },   // 4 solo: punteo
    { traste: 8.4, rasgueo: [2.0, -0.25], vibra: 0.5 },   // 5 solo: la mano sube
    { traste: 9.6, alCielo: [1.1, 5.0],   vibra: 1 },     // 6 remate, brazo arriba
  ];

  // Un muñón que sale de (sx, sy) y termina con la mano en (hx, hy). La mano
  // no la dibuja: va después, encima de la guitarra.
  function brazoGuitarra(c, P, T, ch, side, sx, sy, hx, hy) {
    const dx = hx - sx, dy = hy - sy, L = Math.hypot(dx, dy) || 1;
    const ux = dx / L, uy = dy / L;
    const bx = hx - ux * 1.26, by = hy - uy * 1.26;
    // La punta libre del muñón (y local positiva) apunta a la mano.
    const rot = Math.atan2(-ux, uy);
    if (T) { mangaBata(c, T, bx, by, rot, 1); return; }
    c.save();
    c.translate(bx, by); c.rotate(rot);
    ellipsePath(c, 0, 0, 1.00, 1.30);
    const g = c.createLinearGradient(-1, -1.3, 1, 1.3);
    g.addColorStop(0, P.armA); g.addColorStop(1, P.armB);
    c.fillStyle = g; c.fill();
    c.lineWidth = 0.16; c.strokeStyle = withA(P.edge, 0.7); c.stroke();
    // `puntero`: la mano la dibuja el prop (acá, sobre las cuerdas o el
    // mástil). `up: false` porque en este muñón la mano siempre está en +y.
    if (ch && ch.brazo) ch.brazo(c, P, { side: side, up: false, puntero: true });
    c.restore();
  }

  function manoGuitarra(c, P, x, y) {
    ellipsePath(c, x, y, 0.62, 0.62);
    const hg = c.createLinearGradient(x - 0.6, y - 0.6, x + 0.6, y + 0.6);
    hg.addColorStop(0, P.handA); hg.addColorStop(1, P.handB);
    c.fillStyle = hg; c.fill();
    c.lineWidth = 0.16; c.strokeStyle = withA(P.edge, 0.7); c.stroke();
  }

  // La caja en ocho: bordona grande, cintura y caja chica, en un solo trazo
  // para que el contorno no quede cruzado adentro.
  function cajaGuitarraPath(c) {
    const R1 = 2.2, R2 = 1.55, X2 = 2.45, PI = Math.PI;
    c.beginPath();
    c.arc(0, 0, R1, -0.28 * PI, 0.28 * PI, true);
    const bx = R1 * Math.cos(0.28 * PI), by = R1 * Math.sin(0.28 * PI);
    const ux = X2 + R2 * Math.cos(0.65 * PI), uy = R2 * Math.sin(0.65 * PI);
    c.quadraticCurveTo(1.55, 1.25, ux, uy);
    c.arc(X2, 0, R2, 0.65 * PI, -0.65 * PI, true);
    c.quadraticCurveTo(1.55, -1.25, bx, -by);
    c.closePath();
  }

  function propGuitar(c, P, pose) {
    const f = GUITARRA[pose.propPhase || 0] || GUITARRA[0];
    const G = GUITARRA_COL;
    const dy = pose.bodyDY || 0;
    const ox = GTR.x, oy = GTR.y + dy;
    const ca = Math.cos(GTR.a), sa = Math.sin(GTR.a);
    // De coordenadas de la guitarra a las del personaje.
    const aMundo = (x, y) => [ox + (x * ca - y * sa) * GTR.k, oy + (x * sa + y * ca) * GTR.k];
    const T = llevaBata() ? _bataTela : null;
    const ch = charActual();

    const mMastil = aMundo(f.traste, 0);
    const mPua = f.alCielo ? [f.alCielo[0], f.alCielo[1] + dy] : aMundo(f.rasgueo[0], f.rasgueo[1]);

    // Brazo del mástil: va DETRÁS del mástil; la mano, encima.
    brazoGuitarra(c, P, T, ch, 1, GTR_HOMBRO_MASTIL[0], GTR_HOMBRO_MASTIL[1] + dy, mMastil[0], mMastil[1]);

    c.save();
    c.translate(ox, oy);
    c.rotate(GTR.a);
    c.scale(GTR.k, GTR.k);
    c.lineCap = 'round';

    // -- Caja: madera con el centro más claro (sunburst al revés, el de las
    //    criollas), contorno oscuro y filete claro por dentro.
    cajaGuitarraPath(c);
    const g = c.createRadialGradient(1.0, -0.3, 0.3, 0.9, 0, 3.6);
    g.addColorStop(0, G.maderaLuz); g.addColorStop(0.55, G.madera); g.addColorStop(1, G.maderaSombra);
    c.fillStyle = g; c.fill();
    c.lineWidth = 0.50; c.strokeStyle = G.contorno; c.stroke();
    c.save();
    c.clip();
    c.lineWidth = 0.26; c.strokeStyle = G.filete; c.stroke();
    c.restore();

    // -- Boca con su roseta, y el puente con la selleta.
    ellipsePath(c, 2.3, 0, 0.84, 0.84);
    c.lineWidth = 0.16; c.strokeStyle = G.filete; c.stroke();
    ellipsePath(c, 2.3, 0, 0.62, 0.62);
    c.fillStyle = G.boca; c.fill();
    roundRect(c, -0.95, -0.85, 0.50, 1.70, 0.16);
    c.fillStyle = G.puente; c.fill();
    c.beginPath(); c.moveTo(-0.62, -0.62); c.lineTo(-0.62, 0.62);
    c.lineWidth = 0.10; c.strokeStyle = G.filete; c.stroke();

    // -- Mástil y diapasón, con trastes cada vez más juntos hacia la caja.
    roundRect(c, 3.6, -0.42, 7.2, 0.84, 0.18);
    c.fillStyle = G.mastil; c.fill();
    c.lineWidth = 0.16; c.strokeStyle = G.contorno; c.stroke();
    roundRect(c, 3.7, -0.30, 6.95, 0.60, 0.12);
    c.fillStyle = G.diapason; c.fill();
    c.strokeStyle = G.traste; c.lineWidth = 0.09;
    let xt = 10.6, paso = 0.95;
    c.beginPath();
    for (let i = 0; i < 7; i++) { xt -= paso; paso *= 0.93; c.moveTo(xt, -0.28); c.lineTo(xt, 0.28); }
    c.stroke();
    roundRect(c, 10.6, -0.36, 0.18, 0.72, 0.05);
    c.fillStyle = G.cejuela; c.fill();

    // -- Clavijero con tres clavijas por lado.
    for (const sd of [-1, 1]) {
      for (const xc of [11.2, 11.7, 12.2]) {
        ellipsePath(c, xc, sd * 0.72, 0.21, 0.21);
        c.fillStyle = G.clavija; c.fill();
        c.lineWidth = 0.07; c.strokeStyle = G.contorno; c.stroke();
      }
    }
    roundRect(c, 10.78, -0.55, 1.75, 1.10, 0.30);
    c.fillStyle = G.diapason; c.fill();
    c.lineWidth = 0.14; c.strokeStyle = G.contorno; c.stroke();

    // -- Cuerdas: tres alcanzan a esta escala. Cuando suenan se arquean y
    //    dejan una segunda copia tenue del otro lado: eso es lo que se lee
    //    como vibración en un cuadro quieto.
    const v = f.vibra || 0;
    [-0.18, 0, 0.18].forEach((yc, i) => {
      const off = v * 0.10 * (i % 2 ? -1 : 1);
      c.beginPath(); c.moveTo(-0.62, yc); c.quadraticCurveTo(2.3, yc + off, 10.62, yc);
      c.lineWidth = 0.08; c.strokeStyle = withA(G.cuerda, 0.9); c.stroke();
      if (v > 0) {
        c.beginPath(); c.moveTo(-0.62, yc); c.quadraticCurveTo(2.3, yc - off, 10.62, yc);
        c.lineWidth = 0.07; c.strokeStyle = withA(G.cuerda, 0.35); c.stroke();
      }
    });
    c.restore();

    // Brazo de la púa, encima de la caja. En el remate sale hacia arriba.
    const hombro = f.alCielo ? [GTR_HOMBRO_RASGUEO[0], 9.4 + dy] : [GTR_HOMBRO_RASGUEO[0], GTR_HOMBRO_RASGUEO[1] + dy];
    brazoGuitarra(c, P, T, ch, -1, hombro[0], hombro[1], mPua[0], mPua[1]);

    // Púa asomando de la mano hacia las cuerdas (o hacia el cielo).
    const pp = f.alCielo ? [mPua[0] - 0.15, mPua[1] - 0.72]
      : aMundo(f.rasgueo[0], f.rasgueo[1] + (f.rasgueo[1] > 0.2 ? -0.70 : 0.70));
    const px = pp[0], py = pp[1];
    c.beginPath();
    c.moveTo(px - 0.28, py - 0.18); c.lineTo(px + 0.28, py - 0.18); c.lineTo(px, py + 0.34);
    c.closePath();
    c.fillStyle = G.pua; c.fill();
    c.lineWidth = 0.08; c.strokeStyle = G.contorno; c.stroke();

    manoGuitarra(c, P, mPua[0], mPua[1]);
    manoGuitarra(c, P, mMastil[0], mMastil[1]);
  }

  /**
   * La lupa de detective, con la mano que la sostiene.
   *
   * La versión anterior era un aro del color de la utilería del tema flotando
   * al costado, sin mano, y con un vidrio que apenas aclaraba lo de atrás.
   * Ahora, con la receta de la guitarra, el skate y el café:
   *
   * - Una lupa de verdad, de colores FIJOS con contorno oscuro: aro de
   *   bronce, cuello y virola de metal, mango de madera oscura con su anillo
   *   en la punta, y un vidrio apenas celeste con su brillo. Se lee sobre
   *   cualquier cuerpo y cualquier tema.
   * - La sostiene UNA mano, la derecha, por el mango. El brazo va por el hook
   *   `brazo` del personaje (brazoGuitarra), así que lleva su manga o su
   *   guante; el otro brazo queda en reposo, pintado por el motor.
   * - El chiste: el vidrio AUMENTA. Lo que queda detrás (el ojo, el
   *   sombrero, la manga) se copia del propio lienzo agrandado y recortado al
   *   vidrio, así que cuando la lupa pasa por delante de un ojo se ve el ojo
   *   enorme, con la forma, la mirada y el parpadeo que tenga en ese cuadro.
   *   No se redibuja el ojo: se aumenta lo que ya está pintado, y por eso
   *   funciona igual con el ojo de Pikachu, la venda de Gojo o los lentes.
   *
   * Cuadros clave (propPhase). `ojo`: el vidrio centrado sobre ese ojo (1, el
   * de la derecha de quien mira; -1, el otro), siguiendo la mirada y la
   * altura de los ojos de cada personaje. `lupa`, en lugar de `ojo`: el
   * centro del vidrio en el lienzo. `giro`: la inclinación del mango, en
   * radianes desde la vertical (negativo, el mango hacia la derecha).
   * `hombro`: de dónde sale el brazo, cuando la mano queda tan cerca del
   * hombro de siempre que el muñón no tendría dirección. `k`: el tamaño de
   * la lupa; más grande es más cerca de quien mira (se acerca a ver).
   */
  const LUPA_COL = {
    contorno: '#1b140e',
    bronceLuz: '#f4dc8e', bronce: '#c99a3c', bronceSombra: '#7a5518',
    maderaLuz: '#9a5f33', madera: '#6b3b1c', maderaSombra: '#3a1d0b',
    vidrio: '#cfeaff', vidrioBorde: '#6fa6d6',
  };
  // Medidas en coordenadas de la lupa: origen en el centro del vidrio y el
  // mango hacia +y. `aro`: radio del aro; `vidrio`: radio de lo que aumenta.
  // `agarre`: dónde la toma la mano. `aumento`: cuánto agranda el vidrio; con
  // más, el ojo ya no entra y el vidrio se llena de negro.
  const LUPA = { aro: 2.35, vidrio: 2.12, virola: [2.68, 3.3], mango: [3.3, 5.8],
    agarre: 4.3, aumento: 1.38 };
  // Por debajo de esto (píxeles de pantalla por unidad) el anillo del mango,
  // la veta y el segundo brillo serían un píxel suelto: queda la silueta.
  const LUPA_DETALLE_PX = 5;

  const LUPA_CUADROS = [
    { lupa: [14.5, 11.3], giro: -0.12, dy: 0 },                    // 0 la saca
    { ojo: 1,  giro: -0.55, dy: 0 },                               // 1 mira por la lupa (magnify)
    { ojo: -1, giro: -0.95, dy: 0.05, hombro: [12.6, 12.2] },      // 2 la pasa al otro ojo (magnifyL)
    { lupa: [14.6, 8.1], giro: -0.2, dy: 0 },                      // 3 la lleva afuera (magnifyR)
    { ojo: 1,  giro: -0.5, dy: 0.35, k: 1.15 },                    // 4 se acerca a mirar de cerca
    { ojo: 1,  giro: -0.45, dy: -0.15 },                           // 5 ¡lo encontró! (magnifyFound)
  ];
  function poseLupa(i, cara) {
    return Object.assign({ prop: 'lupa', propPhase: i, armR: false, bodyDY: LUPA_CUADROS[i].dy }, cara);
  }

  // El centro del vidrio en un cuadro. Sobre un ojo, con la misma cuenta que
  // drawEyes: la mirada corre el ojo y el vidrio lo acompaña.
  function centroLupa(f, pose, ch) {
    const dy = pose.bodyDY || 0;
    if (f.lupa) return [f.lupa[0], f.lupa[1] + dy];
    const lugar = ch && ch.ojos;
    const gx = (pose.gaze ? pose.gaze[0] : 0) * 0.62;
    const gy = (pose.gaze ? pose.gaze[1] : 0) * 0.48;
    const ey = (lugar ? lugar.y : BODY.top + (BODY.bot - BODY.top) * 0.38) + dy + gy;
    return [BODY.cx + f.ojo * (lugar ? lugar.sep : 2.28) + gx, ey];
  }

  /*
   * Lo que se ve a través del vidrio: el lienzo mismo, agrandado alrededor del
   * centro y recortado al círculo. Sólo con un lienzo de verdad: un contexto
   * prestado (un proxy, uno sin canvas) no tiene de dónde copiar, y ahí queda
   * el vidrio limpio en lugar de romper el cuadro.
   */
  function aumentoLupa(c, x, y, K) {
    const src = c.canvas;
    const real = src && ((typeof HTMLCanvasElement !== 'undefined' && src instanceof HTMLCanvasElement) ||
      (typeof OffscreenCanvas !== 'undefined' && src instanceof OffscreenCanvas));
    if (!real || !c.getTransform) return;
    const m = c.getTransform();
    const px = m.a * x + m.c * y + m.e, py = m.b * x + m.d * y + m.f;
    const R = LUPA.vidrio * K * Math.hypot(m.a, m.b), s = R / LUPA.aumento;
    c.save();
    ellipsePath(c, x, y, LUPA.vidrio * K, LUPA.vidrio * K);
    c.clip();
    c.setTransform(1, 0, 0, 1, 0, 0);
    c.drawImage(src, px - s, py - s, s * 2, s * 2, px - R, py - R, R * 2, R * 2);
    c.restore();
  }

  // El mango, el cuello y la virola, en coordenadas de la lupa. Van antes que
  // el aro: así el cuello nace por debajo del bronce.
  function mangoLupa(c, det) {
    const C = LUPA_COL, L = LUPA, PI = Math.PI;
    c.lineJoin = 'round'; c.lineCap = 'round';
    // Cuello: del aro a la virola.
    c.beginPath();
    c.moveTo(0, L.aro); c.lineTo(0, L.virola[0] + 0.1);
    c.lineWidth = 0.58; c.strokeStyle = C.contorno; c.stroke();
    c.lineWidth = 0.3; c.strokeStyle = C.bronce; c.stroke();
    // Mango: se ensancha hacia la punta, que termina redonda.
    const [m0, m1] = L.mango;
    c.beginPath();
    c.moveTo(-0.4, m0);
    c.lineTo(-0.5, m1 - 0.45);
    c.ellipse(0, m1 - 0.45, 0.5, 0.45, 0, PI, 0, true);
    c.lineTo(0.4, m0);
    c.closePath();
    const g = c.createLinearGradient(-0.5, 0, 0.5, 0);
    g.addColorStop(0, C.maderaLuz); g.addColorStop(0.4, C.madera); g.addColorStop(1, C.maderaSombra);
    c.fillStyle = g; c.fill();
    c.lineWidth = 0.2; c.strokeStyle = C.contorno; c.stroke();
    if (det) {
      // La veta y el anillo de bronce de la punta.
      c.beginPath();
      c.moveTo(-0.2, m0 + 0.35); c.lineTo(-0.26, m1 - 0.6);
      c.lineWidth = 0.1; c.strokeStyle = withA('#ffffff', 0.22); c.stroke();
      c.beginPath();
      c.moveTo(-0.48, m1 - 0.75); c.lineTo(0.48, m1 - 0.75);
      c.lineWidth = 0.22; c.strokeStyle = C.contorno; c.stroke();
      c.lineWidth = 0.1; c.strokeStyle = C.bronce; c.stroke();
    }
    // Virola: el casquillo de metal entre el cuello y la madera.
    const [v0, v1] = L.virola;
    roundRect(c, -0.46, v0, 0.92, v1 - v0, 0.14);
    const gv = c.createLinearGradient(-0.46, 0, 0.46, 0);
    gv.addColorStop(0, C.bronceLuz); gv.addColorStop(0.45, C.bronce); gv.addColorStop(1, C.bronceSombra);
    c.fillStyle = gv; c.fill();
    c.lineWidth = 0.18; c.strokeStyle = C.contorno; c.stroke();
    if (det) {
      c.beginPath();
      c.moveTo(-0.4, (v0 + v1) / 2); c.lineTo(0.4, (v0 + v1) / 2);
      c.lineWidth = 0.07; c.strokeStyle = withA(C.contorno, 0.7); c.stroke();
    }
  }

  // El vidrio y el aro, en coordenadas de la lupa (sin girar: los brillos
  // quedan siempre arriba a la izquierda, que es de donde viene la luz).
  function vidrioLupa(c, det) {
    const C = LUPA_COL, L = LUPA, PI = Math.PI;
    // El tinte: casi transparente en el centro y más celeste contra el aro,
    // que es como se ve el canto de un vidrio grueso.
    ellipsePath(c, 0, 0, L.vidrio, L.vidrio);
    const g = c.createRadialGradient(-0.5, -0.6, 0.2, 0, 0, L.vidrio);
    g.addColorStop(0, withA('#ffffff', 0.16));
    g.addColorStop(0.65, withA(C.vidrio, 0.08));
    g.addColorStop(1, withA(C.vidrioBorde, 0.38));
    c.fillStyle = g; c.fill();
    // El brillo: un arco curvo arriba a la izquierda y, con detalle, un punto.
    c.beginPath();
    c.arc(0, 0, L.vidrio - 0.42, PI * 1.08, PI * 1.42);
    c.lineWidth = 0.3; c.lineCap = 'round'; c.strokeStyle = withA('#ffffff', 0.8); c.stroke();
    if (det) {
      ellipsePath(c, 0.62, -1.05, 0.15, 0.15);
      c.fillStyle = withA('#ffffff', 0.7); c.fill();
    }
    // El aro: contorno oscuro, bronce con la luz arriba a la izquierda y un
    // filo interno oscuro que lo despega del vidrio.
    c.beginPath();
    c.arc(0, 0, L.aro, 0, PI * 2);
    c.lineWidth = 0.62; c.strokeStyle = C.contorno; c.stroke();
    const ga = c.createLinearGradient(-L.aro, -L.aro, L.aro, L.aro);
    ga.addColorStop(0, C.bronceLuz); ga.addColorStop(0.5, C.bronce); ga.addColorStop(1, C.bronceSombra);
    c.lineWidth = 0.36; c.strokeStyle = ga; c.stroke();
    if (det) {
      c.beginPath();
      c.arc(0, 0, L.aro - 0.2, 0, PI * 2);
      c.lineWidth = 0.06; c.strokeStyle = withA(C.contorno, 0.6); c.stroke();
    }
  }

  function propLupa(c, P, pose) {
    const f = LUPA_CUADROS[pose.propPhase || 0] || LUPA_CUADROS[0];
    const ch = charActual();
    const T = llevaBata() ? _bataTela : null;
    const dy = pose.bodyDY || 0;
    const det = _k >= LUPA_DETALLE_PX;
    const [x, y] = centroLupa(f, pose, ch);
    const K = f.k || 1;
    const sa = Math.sin(f.giro), ca = Math.cos(f.giro);
    // La mano, sobre el mango: (0, agarre) girado por `giro`.
    const hx = x - LUPA.agarre * K * sa, hy = y + LUPA.agarre * K * ca;
    const hombro = f.hombro ? [f.hombro[0], f.hombro[1] + dy] : [13.1, 10.4 + dy];

    // El brazo va detrás del mango y la mano, encima: así lo agarra.
    brazoGuitarra(c, P, T, ch, 1, hombro[0], hombro[1], hx, hy);

    c.save();
    c.translate(x, y);
    c.rotate(f.giro);
    c.scale(K, K);
    mangoLupa(c, det);
    c.restore();

    // Primero el aumento (copia lo que ya está pintado detrás) y después el
    // vidrio y el aro encima.
    aumentoLupa(c, x, y, K);
    c.save();
    c.translate(x, y);
    c.scale(K, K);
    vidrioLupa(c, det);
    c.restore();

    manoGuitarra(c, P, hx, hy);
  }

  /**
   * La pesca del bug: la caña, el charco, el corcho y lo que pica.
   *
   * Con la receta de la guitarra, el skate y la lupa:
   *
   * - Colores FIJOS con contorno oscuro: caña de madera con el mango de
   *   corcho y el reel, tanza oscura con un filo claro (se lee sobre un tema
   *   claro y sobre uno de noche), corcho rojo y blanco, agua celeste. Nada
   *   sale de la paleta, así que se ve igual sobre cualquier cuerpo.
   * - La caña la sostienen las dos manos, por `brazoGuitarra`: lleva la
   *   manga de la bata y el guante o las marcas de cada personaje.
   * - El charco y el corcho van DETRÁS (al costado de los pies, en el piso);
   *   la caña, la tanza, el bug y los brazos van delante. Cuando la caña se
   *   echa hacia atrás para tirar, cruza por detrás de la cabeza: `atras`.
   * - El chiste es el bug: un bichito con patas que patalea colgado del
   *   anzuelo. Debug, literal.
   *
   * La caña es una curva de `base` (el mango) a `punta`, con `comba`
   * unidades de flexión: positiva, se dobla hacia abajo. Las manos se
   * apoyan sobre esa misma curva, así que siguen a la caña cuando se dobla.
   */
  const PESCA_COL = {
    contorno: '#1b140e',
    madera: '#b5793e', maderaLuz: '#e3b077', corcho: '#d9b27c', corchoSombra: '#a87d48',
    reel: '#9aa4b1', reelLuz: '#e4e9ef',
    tanza: '#2a2630', tanzaFilo: '#f4efe6',
    corchoRojo: '#e0412f', corchoBlanco: '#f6f3ec',
    agua: '#3f9bd6', aguaLuz: '#a8dcf7', aguaHondo: '#256fa8',
    bug: '#5fc251', bugSombra: '#3b8d34', bugCabeza: '#2a2433', bugPata: '#2a2433',
  };
  const PESCA_DETALLE_PX = 5;
  // El charco: en el piso, a la derecha de los pies y lejos del borde.
  // A escala de topbar el lienzo redondea a píxeles enteros y queda más
  // corto que la caja: el charco no pasa de 17.6 abajo ni de 16.9 al costado.
  const CHARCO = { x: 14.3, y: 16.95, rx: 2.5, ry: 0.62 };
  const CORCHO_AGUA = [15.0, 16.6];

  /*
   * Cuadros clave (propPhase). `manos`: dónde agarra cada mano, como fracción
   * del largo de la caña (izquierda de quien mira, derecha); o `manosXY` con
   * las dos manos en el lienzo. `tanza`: 'cuelga' (corcho colgando de la
   * punta), 'vuela' (el corcho en el aire), 'agua' (corcho flotando),
   * 'tensa' (tirante hacia el agua) o 'bug'. `corcho` y `bug`: dónde están.
   * `hunde`: cuánto se hunde el corcho; `ondas`: anillos en el agua.
   * `patas`: la fase del pataleo; `aplasta`: la mano derecha sobre el bug.
   */
  const PESCA = [
    { base: [9.4, 13.6], punta: [12.6, -0.4], comba: 0,    manos: [0.03, 0.2], tanza: 'cuelga', corcho: [12.7, 2.4] },    // 0 la saca
    { base: [13.6, 11.2], punta: [2.4, -0.5], comba: -0.3, atras: true, manosXY: [[11.6, 11.6], [13.7, 10.1]],
      tanza: 'cuelga', corcho: [2.2, 2.0] },                                                                            // 1 la echa atrás
    { base: [9.6, 13.4], punta: [16.3, 2.6], comba: -0.5, manos: [0.03, 0.2], tanza: 'vuela', corcho: [16.2, 9.6] },     // 2 tira
    { base: [9.6, 13.4], punta: [16.4, 1.6], comba: 0.3,  manos: [0.03, 0.2], tanza: 'agua', corcho: CORCHO_AGUA, ondas: 1 }, // 3 espera
    { base: [9.6, 13.4], punta: [16.4, 1.7], comba: 0.35, manos: [0.03, 0.2], tanza: 'agua', corcho: CORCHO_AGUA, hunde: 0.3, ondas: 2 }, // 4 tiembla
    { base: [9.6, 13.4], punta: [16.1, 3.4], comba: 1.1,  manos: [0.03, 0.2], tanza: 'tensa', corcho: CORCHO_AGUA, hunde: 1, ondas: 2 },  // 5 ¡pica!
    { base: [9.1, 13.8], punta: [15.0, 4.6], comba: 1.7,  manos: [0.03, 0.2], tanza: 'tensa', corcho: CORCHO_AGUA, hunde: 1, ondas: 3 },  // 6 forcejea
    { base: [9.6, 13.2], punta: [13.4, -0.3], comba: -0.4, manos: [0.03, 0.2], tanza: 'bug', bug: [15.6, 5.4], patas: 1 },              // 7 lo saca
    { base: [9.6, 13.2], punta: [14.9, 1.0], comba: 0.25, manos: [0.03, 0.2], tanza: 'bug', bug: [14.9, 8.2], patas: 0 },                // 8 lo mira
    { base: [9.6, 13.2], punta: [14.9, 1.0], comba: 0.25, manos: [0.03, 0.2], tanza: 'bug', bug: [15.05, 8.25], patas: 1 },              // 9 patalea
    { base: [9.6, 13.2], punta: [14.9, 1.0], comba: 0.25, manos: [0.03], aplasta: [14.9, 8.4], tanza: 'bug', bug: [14.9, 8.2], patas: 2 }, // 10 lo aplasta
    { base: [9.6, 13.2], punta: [14.9, 1.0], comba: 0.25, manos: [0.03, 0.2], tanza: 'cuelga', corcho: null },                         // 11 listo
  ];
  function posePesca(i, cara) {
    return Object.assign({ prop: 'pesca', propPhase: i, armL: false, armR: false }, cara);
  }

  // Un punto de la caña, a la fracción `t` de su largo.
  function puntoCana(f, t, dy) {
    const [bx, by] = f.base, [px, py] = f.punta;
    const L = Math.hypot(px - bx, py - by) || 1;
    const nx = -(py - by) / L, ny = (px - bx) / L;
    // La normal que apunta hacia abajo: así `comba` positiva es caer.
    const s = ny >= 0 ? 1 : -1;
    const cx = (bx + px) / 2 + nx * s * f.comba, cy = (by + py) / 2 + ny * s * f.comba;
    const u = 1 - t;
    return [u * u * bx + 2 * u * t * cx + t * t * px, u * u * by + 2 * u * t * cy + t * t * py + dy];
  }

  function canaPesca(c, f, dy, det) {
    const C = PESCA_COL;
    const p = t => puntoCana(f, t, dy);
    const trazo = (t0, t1) => {
      c.beginPath();
      const n = 12;
      for (let i = 0; i <= n; i++) {
        const q = p(t0 + (t1 - t0) * i / n);
        if (i) c.lineTo(q[0], q[1]); else c.moveTo(q[0], q[1]);
      }
    };
    c.lineCap = 'round'; c.lineJoin = 'round';
    // La caña se afina: tres tramos, cada uno más fino.
    [[0, 0.45, 0.5, 0.3], [0.45, 0.8, 0.4, 0.22], [0.8, 1, 0.3, 0.14]].forEach(([a, b, w, wi]) => {
      trazo(a, b); c.lineWidth = w; c.strokeStyle = C.contorno; c.stroke();
      trazo(a, b); c.lineWidth = wi; c.strokeStyle = C.madera; c.stroke();
    });
    // El mango de corcho y el reel, abajo.
    trazo(0, 0.2); c.lineWidth = 0.78; c.strokeStyle = C.contorno; c.stroke();
    trazo(0, 0.2); c.lineWidth = 0.52; c.strokeStyle = C.corcho; c.stroke();
    const r = p(0.11);
    elipseTinta(c, r[0] + 0.15, r[1] + 0.55, 0.5, 0.5, C.reel, 0.16);
    if (det) elipseTinta(c, r[0] + 0.05, r[1] + 0.45, 0.18, 0.18, C.reelLuz);
    return p(1);
  }

  // La tanza: oscura, con un filo claro debajo para los temas oscuros.
  function tanzaPesca(c, a, b, comba) {
    const camino = () => {
      c.beginPath();
      c.moveTo(a[0], a[1]);
      c.quadraticCurveTo((a[0] + b[0]) / 2 + (comba || 0), (a[1] + b[1]) / 2 + Math.abs(comba || 0) * 0.6, b[0], b[1]);
    };
    c.lineCap = 'round';
    camino(); c.lineWidth = 0.2; c.strokeStyle = withA(PESCA_COL.tanzaFilo, 0.55); c.stroke();
    camino(); c.lineWidth = 0.09; c.strokeStyle = PESCA_COL.tanza; c.stroke();
  }

  function corchoPesca(c, x, y) {
    const C = PESCA_COL;
    c.save();
    ellipsePath(c, x, y, 0.55, 0.62);
    c.clip();
    c.fillStyle = C.corchoBlanco; c.fillRect(x - 1, y - 1, 2, 2);
    c.fillStyle = C.corchoRojo; c.fillRect(x - 1, y - 1, 2, 1);
    c.restore();
    elipseTinta(c, x, y, 0.55, 0.62, null, 0.16);
    c.beginPath(); c.moveTo(x, y - 0.62); c.lineTo(x, y - 1.0);
    c.lineWidth = 0.14; c.strokeStyle = C.contorno; c.stroke();
  }

  /*
   * El bug colgado del anzuelo: caparazón verde con la raya del medio, cabeza
   * oscura con dos ojitos, antenas y seis patas que se alternan con `patas`.
   * `aplastado`: una mancha chata con las patas para los costados.
   */
  function bugPesca(c, x, y, patas, det, aplastado) {
    const C = PESCA_COL;
    c.lineCap = 'round';
    if (aplastado) {
      elipseTinta(c, x, y + 0.35, 1.25, 0.34, C.bug, 0.16);
      c.strokeStyle = C.bugPata; c.lineWidth = 0.12;
      c.beginPath();
      [-1, 1].forEach(s => { c.moveTo(x + s * 1.2, y + 0.3); c.lineTo(x + s * 1.65, y + 0.1); c.moveTo(x + s * 1.1, y + 0.5); c.lineTo(x + s * 1.6, y + 0.7); });
      c.stroke();
      return;
    }
    // El anzuelo, arriba, enganchado al lomo.
    c.beginPath();
    c.moveTo(x, y - 1.55); c.lineTo(x, y - 0.95);
    c.quadraticCurveTo(x, y - 0.55, x + 0.32, y - 0.75);
    c.lineWidth = 0.12; c.strokeStyle = '#8b939e'; c.stroke();
    // Patas: tres por lado; cada fase las abre en otro ángulo.
    const ab = [0.25, 0.65, -0.1][patas || 0];
    c.strokeStyle = C.bugPata; c.lineWidth = 0.14;
    c.beginPath();
    [-0.35, 0.15, 0.6].forEach((py, i) => {
      [-1, 1].forEach(s => {
        const a = ab * (i % 2 ? -1 : 1) * s;
        c.moveTo(x + s * 0.6, y + py);
        c.lineTo(x + s * (1.15 + 0.1 * i), y + py + 0.25 + a);
      });
    });
    c.stroke();
    // Cuerpo: caparazón con su raya; la cabeza abajo (cuelga del lomo).
    elipseTinta(c, x, y, 0.82, 0.98, C.bug, 0.16);
    c.save();
    ellipsePath(c, x, y, 0.82, 0.98);
    c.clip();
    elipseTinta(c, x + 0.35, y + 0.3, 0.7, 0.9, C.bugSombra);
    c.restore();
    elipseTinta(c, x, y, 0.82, 0.98, null, 0.16);
    c.beginPath(); c.moveTo(x, y - 0.95); c.lineTo(x, y + 0.6);
    c.lineWidth = 0.1; c.strokeStyle = C.contorno; c.stroke();
    elipseTinta(c, x, y + 1.05, 0.52, 0.42, C.bugCabeza, 0.14);
    if (det) {
      elipseTinta(c, x - 0.2, y + 1.05, 0.13, 0.13, '#ffffff');
      elipseTinta(c, x + 0.2, y + 1.05, 0.13, 0.13, '#ffffff');
      c.beginPath();
      c.moveTo(x - 0.2, y + 1.4); c.quadraticCurveTo(x - 0.5, y + 1.9, x - 0.75, y + 1.75);
      c.moveTo(x + 0.2, y + 1.4); c.quadraticCurveTo(x + 0.5, y + 1.9, x + 0.75, y + 1.75);
      c.lineWidth = 0.09; c.strokeStyle = C.bugPata; c.stroke();
    }
  }

  // Lo de atrás: el charco, sus ondas y el corcho flotando. Y la caña, cuando
  // se echa hacia atrás para tirar.
  function propPesca(c, P, pose) {
    const f = PESCA[pose.propPhase || 0] || PESCA[0];
    const C = PESCA_COL;
    const det = _k >= PESCA_DETALLE_PX;
    const H = CHARCO;
    ellipsePath(c, H.x, H.y, H.rx, H.ry);
    c.fillStyle = C.agua; c.fill();
    c.save();
    c.clip();
    elipseTinta(c, H.x + 0.7, H.y + 0.35, H.rx, H.ry * 0.8, C.aguaHondo);
    c.restore();
    c.beginPath();
    c.ellipse(H.x - 0.3, H.y - 0.1, H.rx * 0.62, H.ry * 0.45, 0, Math.PI * 1.1, Math.PI * 1.6);
    c.lineWidth = 0.14; c.lineCap = 'round'; c.strokeStyle = withA(C.aguaLuz, 0.9); c.stroke();
    ellipsePath(c, H.x, H.y, H.rx, H.ry);
    c.lineWidth = 0.2; c.strokeStyle = C.contorno; c.stroke();
    if (f.tanza === 'agua' || f.tanza === 'tensa') {
      const [x, y] = f.corcho;
      for (let i = 1; i <= (f.ondas || 0); i++) {
        c.beginPath();
        c.ellipse(x, y + 0.45, 0.45 + i * 0.42, 0.14 + i * 0.12, 0, 0, Math.PI * 2);
        c.lineWidth = 0.1; c.strokeStyle = withA(C.aguaLuz, 0.95 - i * 0.2); c.stroke();
      }
      if ((f.hunde || 0) < 1) corchoPesca(c, x, y + (f.hunde || 0) * 0.6);
    }
    if (f.atras) {
      const dy = pose.bodyDY || 0;
      const punta = canaPesca(c, f, dy, det);
      tanzaPesca(c, punta, [f.corcho[0], f.corcho[1] + dy - 1.0]);
      corchoPesca(c, f.corcho[0], f.corcho[1] + dy);
    }
  }

  // Lo de adelante: la caña, la tanza, el bug y los dos brazos.
  function propPescaDelante(c, P, pose) {
    const f = PESCA[pose.propPhase || 0] || PESCA[0];
    const dy = pose.bodyDY || 0;
    const det = _k >= PESCA_DETALLE_PX;
    const T = llevaBata() ? _bataTela : null;
    const ch = charActual();
    if (!f.atras) {
      const punta = canaPesca(c, f, dy, det);
      if (f.tanza === 'cuelga' && f.corcho) {
        tanzaPesca(c, punta, [f.corcho[0], f.corcho[1] + dy - 1.0]);
        corchoPesca(c, f.corcho[0], f.corcho[1] + dy);
      } else if (f.tanza === 'cuelga') {
        // Sin nada colgando: la tanza corta con el anzuelo vacío.
        tanzaPesca(c, punta, [punta[0] + 0.1, punta[1] + 3]);
      } else if (f.tanza === 'vuela') {
        tanzaPesca(c, punta, [f.corcho[0], f.corcho[1] - 1.0], -1.2);
        corchoPesca(c, f.corcho[0], f.corcho[1]);
      } else if (f.tanza === 'agua') {
        tanzaPesca(c, punta, [f.corcho[0], f.corcho[1] + (f.hunde || 0) * 0.6 - 1.0], 0.5);
      } else if (f.tanza === 'tensa') {
        tanzaPesca(c, punta, [f.corcho[0], f.corcho[1] + 0.3]);
      } else if (f.tanza === 'bug') {
        tanzaPesca(c, punta, [f.bug[0], f.bug[1] + dy - 1.55]);
        bugPesca(c, f.bug[0], f.bug[1] + dy, f.patas, det, !!f.aplasta);
      }
    }
    // Las manos: sobre la caña, o donde diga el cuadro.
    const manos = f.manosXY ? f.manosXY.map(m => [m[0], m[1] + dy]) : f.manos.map(t => puntoCana(f, t, dy));
    const izq = manos[0];
    const der = f.aplasta ? [f.aplasta[0], f.aplasta[1] + dy] : manos[1];
    brazoGuitarra(c, P, T, ch, -1, 2.9, 10.6 + dy, izq[0], izq[1]);
    const hombroDer = f.aplasta ? [12.4, 12.4 + dy] : [13.1, 10.4 + dy];
    brazoGuitarra(c, P, T, ch, 1, hombroDer[0], hombroDer[1], der[0], der[1]);
    manoGuitarra(c, P, izq[0], izq[1]);
    manoGuitarra(c, P, der[0], der[1]);
  }

  /**
   * Los colores del tema activo, leídos de los tokens de la página.
   *
   * El lienzo nunca leía CSS: el cuerpo de Boberto sale de su propia tabla
   * de paletas, y los personajes y la utilería tienen colores fijos a
   * propósito. El pintor y los malabares son la excepción, porque su chiste
   * ES el tema: cambiás de paleta y cambia el cuadro. Se lee en cada cuadro
   * (son pocos y sólo durante esos gestos) y no se guarda: así un acento
   * elegido a mano o un cambio de tema a mitad del gesto se ven en el acto.
   * Sin documento (o sin tokens) cae a la utilería por defecto.
   */
  const TEMA_DEFECTO = { fondo: '#12161d', accent: '#5b8cff', ok: '#2ecc8f', warn: '#f2b34a', crit: '#f05663', fuchsia: '#ec4899' };
  function coloresTema() {
    const root = typeof document !== 'undefined' && document.documentElement;
    if (!root || typeof getComputedStyle !== 'function') return Object.assign({ serie: serieTema(TEMA_DEFECTO) }, TEMA_DEFECTO);
    const cs = getComputedStyle(root);
    const v = (n, d) => (cs.getPropertyValue(n) || '').trim() || d;
    const D = TEMA_DEFECTO;
    const T = {
      fondo: v('--bg-elev-1', v('--bg', D.fondo)), accent: v('--accent', D.accent),
      ok: v('--ok', D.ok), warn: v('--warn', D.warn), crit: v('--crit', D.crit), fuchsia: v('--fuchsia', D.fuchsia),
    };
    T.serie = serieTema(T);
    return T;
  }

  /*
   * Cuatro colores del tema que se distingan entre sí. En algunos temas el
   * acento y ok son el mismo verde (forest), y dos discos o dos manchas
   * iguales no se leen como dos. Se toman en orden y se saltea el que quede
   * a menos de 70 de distancia RGB de uno ya elegido; si no alcanzan, se
   * completa con los que falten. Un color que no es hex se da por distinto.
   */
  function serieTema(T) {
    const rgb = h => (/^#[0-9a-f]{6}$/i.test(h) ? [16, 8, 0].map(d => (parseInt(h.slice(1), 16) >> d) & 255) : null);
    const cerca = (x, y) => {
      const a = rgb(x), b = rgb(y);
      return !!(a && b) && Math.hypot(a[0] - b[0], a[1] - b[1], a[2] - b[2]) < 70;
    };
    const cand = [T.accent, T.ok, T.warn, T.crit, T.fuchsia];
    const serie = [];
    cand.forEach(col => { if (serie.length < 4 && !serie.some(e => cerca(e, col))) serie.push(col); });
    cand.forEach(col => { if (serie.length < 4 && !serie.includes(col)) serie.push(col); });
    return serie;
  }

  /**
   * El pintor: caballete, paleta, pincel y un autorretrato.
   *
   * - El caballete va DETRÁS del cuerpo, a la derecha, y al principio se ve
   *   el cuadro de espaldas: el bastidor de madera con su cruz. Lo que pinta
   *   no se ve hasta el final.
   * - La paleta (mano izquierda) lleva cuatro manchas con los colores del
   *   tema, así que el tema se ve desde el primer cuadro.
   * - Remate: da vuelta el cuadro y es un retrato suyo pintado con la
   *   paleta del tema: fondo de superficie, cuerpo en el acento, mejillas en
   *   crítico, sol en aviso y piso en ok. Cambiás de tema y cambia el cuadro,
   *   que es la tesis de la suite.
   * - Sin boina: el motor no tiene "sin accesorio" (siempre lleva uno, o el
   *   pelo de un personaje) y una boina encima de otro sombrero se ve mal.
   *
   * Colores de madera, pincel y contorno FIJOS, como en la guitarra.
   */
  const PINTOR_COL = {
    contorno: '#1b140e',
    madera: '#b5793e', maderaLuz: '#e3b077', maderaSombra: '#7a4a1e',
    tela: '#efe4cb', telaSombra: '#d3c3a0',
    paleta: '#d9a868', paletaSombra: '#a87a3e',
    mango: '#c7432f', virola: '#c9ced6',
  };
  const PINTOR_DETALLE_PX = 5;
  // El cuadro: de espaldas es un trapecio (lo vemos de tres cuartos); de
  // frente, un rectángulo; de canto, una tabla finita.
  // Todo queda a más de una unidad del borde derecho (18): a escala de
  // topbar, el antialias de un trazo más cerca ya pinta la última columna.
  const CUADRO_ESPALDA = [[13.4, 3.3], [16.6, 2.9], [16.6, 10.5], [13.4, 10.2]];
  const CUADRO_FRENTE = { x: 12.6, y: 3.2, w: 4.0, h: 6.6 };

  /*
   * Cuadros clave. `pincel`: [x, y, ángulo] de la mano que lo sostiene (el
   * ángulo es hacia dónde apunta la punta: 0 arriba, negativo a la
   * izquierda). `cuadro`: 'espalda', 'canto' o 'frente'. `mancha`: la manchita
   * de pintura en la mejilla. `manoCuadro`: la mano que da vuelta el cuadro.
   */
  const PINTOR = [
    { pincel: [13.6, 9.4, 0.45],   cuadro: 'espalda' },                       // 0 se prepara
    { pincel: [15.2, 8.9, 0],      cuadro: 'espalda', mide: true },           // 1 mide con el pulgar
    { pincel: [5.9, 12.5, -1.75],  cuadro: 'espalda', moja: true },           // 2 moja el pincel
    { pincel: [14.3, 8.5, -0.5],   cuadro: 'espalda' },                       // 3 pinta (abajo)
    { pincel: [14.3, 6.6, -0.5],   cuadro: 'espalda' },                       // 4 pinta (arriba)
    { pincel: [14.3, 8.0, -0.85],  cuadro: 'espalda', mancha: 1 },            // 5 ¡se mancha!
    { manoCuadro: [14.9, 7.6],     cuadro: 'canto', mancha: 1 },              // 6 lo da vuelta
    { pincel: [12.9, 11.6, 0.55],  cuadro: 'frente', mancha: 1 },             // 7 lo muestra
    { pincel: [12.7, 12.2, 0.75],  cuadro: 'frente', mancha: 1 },             // 8 orgulloso
  ];
  function posePintor(i, cara) {
    return Object.assign({ prop: 'pintor', propPhase: i, armL: false, armR: false }, cara);
  }

  // El retrato: Boberto pintado con los colores del tema.
  function retratoPintor(c, x, y, w, h, T, det) {
    c.save();
    c.beginPath(); c.rect(x, y, w, h); c.clip();
    c.fillStyle = T.fondo; c.fillRect(x, y, w, h);
    elipseTinta(c, x + w * 0.78, y + h * 0.2, w * 0.13, w * 0.13, T.warn);
    c.fillStyle = T.serie[1]; c.fillRect(x, y + h * 0.8, w, h * 0.2);
    // El cuerpo: la misma silueta del motor, en chico.
    const cx = x + w * 0.46, top = y + h * 0.26, bot = y + h * 0.86, rx = w * 0.3;
    blobPath(c, cx, top, bot, rx);
    c.fillStyle = T.accent; c.fill();
    c.lineWidth = 0.12; c.strokeStyle = PINTOR_COL.contorno; c.stroke();
    const ey = top + (bot - top) * 0.38;
    elipseTinta(c, cx - rx * 0.36, ey, rx * 0.17, rx * 0.22, PINTOR_COL.contorno);
    elipseTinta(c, cx + rx * 0.36, ey, rx * 0.17, rx * 0.22, PINTOR_COL.contorno);
    if (det) {
      elipseTinta(c, cx - rx * 0.62, ey + rx * 0.42, rx * 0.16, rx * 0.1, T.crit);
      elipseTinta(c, cx + rx * 0.62, ey + rx * 0.42, rx * 0.16, rx * 0.1, T.crit);
    }
    c.beginPath();
    c.moveTo(cx - rx * 0.3, ey + rx * 0.5); c.quadraticCurveTo(cx, ey + rx * 0.85, cx + rx * 0.3, ey + rx * 0.5);
    c.lineWidth = 0.12; c.lineCap = 'round'; c.strokeStyle = PINTOR_COL.contorno; c.stroke();
    c.restore();
  }

  // El caballete y el cuadro, detrás del cuerpo.
  function propPintor(c, P, pose) {
    const f = PINTOR[pose.propPhase || 0] || PINTOR[0];
    const C = PINTOR_COL;
    const det = _k >= PINTOR_DETALLE_PX;
    c.lineCap = 'round'; c.lineJoin = 'round';
    // Las tres patas: la de atrás más fina.
    const pata = (x0, y0, x1, y1, w) => {
      c.beginPath(); c.moveTo(x0, y0); c.lineTo(x1, y1);
      c.lineWidth = w + 0.24; c.strokeStyle = C.contorno; c.stroke();
      c.lineWidth = w; c.strokeStyle = C.madera; c.stroke();
    };
    pata(15.0, 2.4, 15.1, 17.4, 0.22);
    pata(14.8, 2.2, 13.2, 17.5, 0.34);
    pata(14.9, 2.2, 16.6, 17.5, 0.34);
    // El apoyo del cuadro.
    pata(12.9, 10.75, 16.7, 10.75, 0.3);
    if (f.cuadro === 'espalda') {
      poliTinta(c, CUADRO_ESPALDA, C.tela);
      c.save();
      trazoPoli(c, CUADRO_ESPALDA);
      c.clip();
      // El bastidor: el marco por dentro y la cruz.
      const [a, b, d, e] = CUADRO_ESPALDA;
      c.strokeStyle = C.madera; c.lineWidth = 0.42;
      c.beginPath();
      c.moveTo(a[0], a[1]); c.lineTo(b[0], b[1]); c.lineTo(d[0], d[1]); c.lineTo(e[0], e[1]); c.closePath();
      c.moveTo((a[0] + b[0]) / 2, (a[1] + b[1]) / 2); c.lineTo((e[0] + d[0]) / 2, (e[1] + d[1]) / 2);
      c.moveTo(a[0], (a[1] + e[1]) / 2); c.lineTo(b[0], (b[1] + d[1]) / 2);
      c.stroke();
      c.restore();
      poliTinta(c, CUADRO_ESPALDA);
    } else if (f.cuadro === 'canto') {
      roundRect(c, 14.6, 2.9, 0.7, 7.8, 0.12);
      c.fillStyle = C.maderaLuz; c.fill();
      c.lineWidth = 0.22; c.strokeStyle = C.contorno; c.stroke();
    } else {
      const Q = CUADRO_FRENTE;
      retratoPintor(c, Q.x, Q.y, Q.w, Q.h, coloresTema(), det);
      // El marco, encima.
      c.beginPath(); c.rect(Q.x, Q.y, Q.w, Q.h);
      c.lineWidth = 0.6; c.strokeStyle = C.contorno; c.stroke();
      c.lineWidth = 0.34; c.strokeStyle = C.madera; c.stroke();
    }
  }

  // El pincel: mango, virola y la punta mojada en el acento del tema.
  function pincelPintor(c, x, y, ang, punta) {
    const sa = Math.sin(ang), ca = -Math.cos(ang);
    const C = PINTOR_COL;
    const p = (d) => [x + sa * d, y + ca * d];
    const linea = (d0, d1, w, col) => {
      const a = p(d0), b = p(d1);
      c.beginPath(); c.moveTo(a[0], a[1]); c.lineTo(b[0], b[1]);
      c.lineWidth = w; c.strokeStyle = col; c.stroke();
    };
    c.lineCap = 'round';
    linea(-1.3, 1.5, 0.46, C.contorno);
    linea(-1.3, 1.1, 0.26, C.mango);
    linea(1.1, 1.6, 0.3, C.virola);
    linea(1.6, 2.3, 0.4, C.contorno);
    linea(1.6, 2.2, 0.26, punta);
  }

  // La paleta de pintor, con el agujero del pulgar y las manchas del tema.
  function paletaPintor(c, x, y, T, det) {
    const C = PINTOR_COL;
    c.save();
    c.beginPath();
    c.ellipse(x, y, 2.0, 1.25, -0.15, 0, Math.PI * 2);
    c.moveTo(x + 0.95, y + 0.35);
    c.ellipse(x + 0.7, y + 0.35, 0.28, 0.22, 0, 0, Math.PI * 2, true);
    c.fillStyle = C.paleta; c.fill('evenodd');
    c.lineWidth = 0.2; c.strokeStyle = C.contorno; c.stroke();
    c.restore();
    [[-1.15, -0.25], [-0.4, -0.7], [0.45, -0.6], [-0.7, 0.45]].forEach(([dx, dy], i) => {
      const col = T.serie[i];
      elipseTinta(c, x + dx, y + dy, 0.36, 0.28, col, det ? 0.1 : false);
    });
  }

  // Lo de adelante: la paleta, el pincel, los brazos y la mancha.
  function propPintorDelante(c, P, pose) {
    const f = PINTOR[pose.propPhase || 0] || PINTOR[0];
    const dy = pose.bodyDY || 0;
    const det = _k >= PINTOR_DETALLE_PX;
    const T = coloresTema();
    const tela = llevaBata() ? _bataTela : null;
    const ch = charActual();
    // La mancha en la mejilla: se la dejó el pincel.
    if (f.mancha) elipseTinta(c, 11.1, 11.5 + dy, 0.5, 0.36, T.accent, det ? 0.1 : false, -0.3);
    // Mano izquierda con la paleta.
    const pal = [1.9, 12.9 + dy];
    brazoGuitarra(c, P, tela, ch, -1, 2.9, 10.6 + dy, 1.5, 12.5 + dy);
    paletaPintor(c, pal[0], pal[1], T, det);
    manoGuitarra(c, P, 1.5, 12.5 + dy);
    // Mano derecha: con el pincel o dando vuelta el cuadro.
    const m = f.pincel ? [f.pincel[0], f.pincel[1] + dy] : [f.manoCuadro[0], f.manoCuadro[1] + dy];
    const lejos = Math.hypot(m[0] - 13.1, m[1] - 10.4 - dy) > 1.6;
    const hombro = lejos ? [13.1, 10.4 + dy] : [11.6, 12.6 + dy];
    brazoGuitarra(c, P, tela, ch, 1, hombro[0], hombro[1], m[0], m[1]);
    if (f.pincel) pincelPintor(c, m[0], m[1], f.pincel[2], f.moja || pose.propPhase > 1 ? T.accent : PINTOR_COL.tela);
    manoGuitarra(c, P, m[0], m[1]);
    // El pulgar parado, para medir.
    if (f.mide) {
      c.beginPath(); c.moveTo(m[0] + 0.1, m[1] - 0.4); c.lineTo(m[0] + 0.15, m[1] - 1.05);
      c.lineCap = 'round';
      c.lineWidth = 0.5; c.strokeStyle = withA(P.edge, 0.7); c.stroke();
      c.lineWidth = 0.32; c.strokeStyle = P.handA; c.stroke();
    }
  }

  /**
   * Malabares con discos: los cilindros de base de datos, uno por color del
   * tema (los cuatro de serieTema; el que cae del cielo es el último).
   *
   * - La cascada no se dibuja a mano: cada cilindro sigue la X de la cascada
   *   de verdad (sale de adentro de una mano y cae afuera de la otra) y su
   *   lugar se calcula con el tiempo. Ocho fases por vuelta, que cierran el
   *   loop sin salto: la última empalma con la primera.
   * - Con tres, la vuelta es pareja; con cuatro, los desfasajes se achican y
   *   se nota el apuro.
   * - Remate: el cuarto no lo agarra, le rebota en la cabeza y se le queda
   *   arriba; ataja los otros (dos apilados en una mano) y hace la reverencia
   *   con el disco haciendo equilibrio.
   *
   * Contorno oscuro fijo y los dos anillos del icono de base de datos; el
   * color es el del tema (coloresTema, como el pintor).
   */
  const DISCO = { rx: 1.25, h: 2.3, ry: 0.46 };
  const MALAB_MANO_I = [3.4, 12.9], MALAB_MANO_D = [12.6, 12.9];
  // La X de la cascada: de adentro de una mano a afuera de la otra. El techo
  // del vuelo deja el disco entero (con su tinta) por debajo de las puntas.
  const MALAB_ALTO = 1.25;

  // Dónde está un disco en el tramo `s` (0..2) de su vuelta.
  function vueloDisco(s) {
    const ida = s < 1, u = ida ? s : s - 1;
    const x0 = ida ? 5.0 : 11.0, x1 = ida ? 12.6 : 3.4;
    const y0 = 12.5, y1 = 12.3;
    const x = x0 + (x1 - x0) * u;
    const y = y0 + (y1 - y0) * u - 4 * (y0 - MALAB_ALTO) * u * (1 - u);
    return [x, y, (ida ? 1 : -1) * u * Math.PI];
  }

  /*
   * Con cuatro no se cruzan: es la fuente, dos discos por mano y cada uno de
   * su lado. Tres cuartos de la vuelta en el aire (de adentro hacia afuera) y
   * el resto en la mano, que lo trae de vuelta hacia adentro: con la mitad en
   * la mano se juntaban dos por mano y se veía amontonado. Las dos manos van
   * desfasadas.
   */
  function fuenteDisco(s, lado) {
    const adentro = lado < 0 ? 5.6 : 10.4, afuera = lado < 0 ? 2.8 : 13.2;
    if (s >= 1.5) {
      const u = (s - 1.5) / 0.5;
      return [afuera + (adentro - afuera) * u, 12.3 + 0.35 * Math.sin(u * Math.PI), 0];
    }
    const u = s / 1.5;
    const y = 12.3 - 4 * (12.3 - MALAB_ALTO - 1.2) * u * (1 - u);
    return [adentro + (afuera - adentro) * u, y, lado * u * Math.PI];
  }

  // Una fase de la cascada (tres) o de la fuente (cuatro), con las manos
  // acompañando.
  function cascada(n, fase) {
    const tau = fase * 2 / 8;
    const discos = [];
    for (let k = 0; k < n; k++) {
      const [x, y, rot] = n === 4
        ? fuenteDisco((tau + (k >> 1) + (k & 1) * 0.5) % 2, k & 1 ? 1 : -1)
        : vueloDisco((tau + 2 * k / n) % 2);
      discos.push({ x, y, rot, col: k });
    }
    // La mano que acaba de tirar baja y la otra sube a recibir.
    const b = Math.sin(tau * Math.PI);
    return {
      discos,
      manos: [[MALAB_MANO_I[0] + 0.3 * b, MALAB_MANO_I[1] - 0.45 * b], [MALAB_MANO_D[0] - 0.3 * b, MALAB_MANO_D[1] + 0.45 * b]],
    };
  }

  const MALABARES = (() => {
    const L = [];
    const enMano = (m, col, alto) => ({ x: m[0], y: m[1] - 1.05 - (alto || 0) * DISCO.h, rot: 0, col });
    // 0 los saca: dos en la izquierda, apilados, y uno en la derecha.
    L.push({ discos: [enMano(MALAB_MANO_I, 0), enMano(MALAB_MANO_I, 1, 1), enMano(MALAB_MANO_D, 2)],
      manos: [MALAB_MANO_I, MALAB_MANO_D] });
    // 1..8 cascada de tres; 9..16 cascada de cuatro.
    for (let i = 0; i < 8; i++) L.push(cascada(3, i));
    for (let i = 0; i < 8; i++) L.push(cascada(4, i));
    // 17 el cuarto cae del cielo mientras sigue con tres.
    const cae = cascada(3, 0);
    L.push({ discos: cae.discos.concat([{ x: 8.4, y: 1.25, rot: 0.5, col: 3 }]), manos: cae.manos });
    // 18 ¡bonk! el cuarto le rebota en la cabeza; dos todavía en el aire.
    L.push({ discos: [{ x: 8.2, y: 1.55, rot: -0.35, col: 3, cabeza: 1 },
      { x: 4.6, y: 4.2, rot: 1.2, col: 0 }, { x: 11.8, y: 5.4, rot: -0.9, col: 1 }, enMano(MALAB_MANO_D, 2)],
      manos: [[3.6, 12.5], MALAB_MANO_D] });
    // 19 ataja: uno en la izquierda, dos apilados en la derecha, uno en la cabeza.
    const ataja = [{ x: 8, y: 2.25, rot: 0, col: 3, cabeza: 1 },
      enMano(MALAB_MANO_I, 0), enMano(MALAB_MANO_D, 2), enMano(MALAB_MANO_D, 1, 1)];
    L.push({ discos: ataja, manos: [MALAB_MANO_I, MALAB_MANO_D] });
    // 20 la reverencia: las manos abiertas a los costados con sus discos.
    const ai = [2.6, 13.6], ad = [13.4, 13.6];
    L.push({ discos: [{ x: 8, y: 2.25, rot: 0.08, col: 3, cabeza: 1 }, enMano(ai, 0), enMano(ad, 2), enMano(ad, 1, 1)],
      manos: [ai, ad] });
    return L;
  })();
  function poseMalabares(i, cara) {
    return Object.assign({ prop: 'malabares', propPhase: i, armL: false, armR: false }, cara);
  }

  function discoMalabares(c, x, y, rot, col, det) {
    const { rx, h, ry } = DISCO;
    c.save();
    c.translate(x, y); c.rotate(rot);
    const cuerpo = () => {
      c.beginPath();
      c.ellipse(0, -h / 2, rx, ry, 0, Math.PI, 0);
      c.lineTo(rx, h / 2);
      c.ellipse(0, h / 2, rx, ry, 0, 0, Math.PI);
      c.closePath();
    };
    cuerpo();
    c.fillStyle = col; c.fill();
    // Sombra de cel del lado derecho y la tapa más clara.
    c.save();
    c.clip();
    c.fillStyle = withA('#000000', 0.22); c.fillRect(rx * 0.35, -h, rx, h * 2);
    c.restore();
    ellipsePath(c, 0, -h / 2, rx, ry);
    c.fillStyle = col; c.fill();
    c.fillStyle = withA('#ffffff', 0.38); c.fill();
    c.lineWidth = 0.14; c.strokeStyle = PINTOR_COL.contorno; c.stroke();
    if (det) {
      // Los dos anillos del icono de base de datos.
      c.beginPath();
      c.ellipse(0, -h / 6, rx, ry, 0, 0, Math.PI);
      c.moveTo(rx, h / 6);
      c.ellipse(0, h / 6, rx, ry, 0, 0, Math.PI);
      c.lineWidth = 0.1; c.strokeStyle = withA(PINTOR_COL.contorno, 0.75); c.stroke();
    }
    cuerpo();
    c.lineWidth = 0.16; c.strokeStyle = PINTOR_COL.contorno; c.stroke();
    c.restore();
  }

  function propMalabares(c, P, pose) {
    const f = MALABARES[pose.propPhase || 0] || MALABARES[0];
    const dy = pose.bodyDY || 0;
    const det = _k >= PINTOR_DETALLE_PX;
    const T = coloresTema();
    const cols = T.serie;
    const tela = llevaBata() ? _bataTela : null;
    const ch = charActual();
    const [mi, md] = f.manos.map(m => [m[0], m[1] + dy]);
    brazoGuitarra(c, P, tela, ch, -1, 2.9, 10.6 + dy, mi[0], mi[1]);
    brazoGuitarra(c, P, tela, ch, 1, 13.1, 10.4 + dy, md[0], md[1]);
    // Los discos en la mano van detrás de la mano; los del aire, delante de
    // todo. El de la cabeza sigue al cuerpo cuando se inclina.
    f.discos.forEach(d => discoMalabares(c, d.x, d.y + ((d.cabeza || d.y > 9) ? dy : 0), d.rot, cols[d.col], det));
    manoGuitarra(c, P, mi[0], mi[1]);
    manoGuitarra(c, P, md[0], md[1]);
  }

  /**
   * El yoyó, con el brazo derecho que lo maneja.
   *
   * - Se lo ve de frente, como un disco: así rueda en el plano del dibujo
   *   cuando hace "el perrito". Las dos mitades llevan dos colores del tema
   *   (coloresTema, como los malabares) y la línea que las separa ES la marca
   *   del giro: rota de un cuadro al siguiente, y a escala de topbar se sigue
   *   leyendo, cosa que un logo chiquito no haría.
   * - El disco partido es simétrico a media vuelta, así que el giro avanza de a
   *   un octavo de vuelta: con pasos más grandes el ojo lo lee girando para el
   *   otro lado. Por lo mismo, el loop de "dormido" son cuatro cuadros: cuatro
   *   octavos son media vuelta y empalman sin salto.
   * - El hilo es claro con borde oscuro, como las cuerdas de la guitarra: el
   *   oscuro lo separa de un fondo claro y el claro de uno oscuro.
   * - Nudo y remate: rueda por el piso y se lo empieza a llevar, el hilo se
   *   tensa y el cuerpo se va para adelante; un tirón lo trae de vuelta a la
   *   mano y remata con un guiño y el otro brazo arriba.
   *
   * El eje y el brillo se apagan por debajo de YOYO_DETALLE_PX: a escala de
   * topbar son una mancha y tapan el corte de las mitades, que es lo que
   * cuenta el giro.
   */
  const YOYO_COL = { contorno: '#1b140e', hilo: '#f4ecd6', eje: '#d5dae1' };
  const YOYO_R = 1.15;
  const YOYO_DETALLE_PX = 5;
  // El yoyó apoyado: el borde de abajo a la altura de la suela de los pies.
  const YOYO_PISO = 15.25;
  // El hombro del que sale el brazo derecho (sólo da la dirección del muñón).
  const YOYO_HOMBRO = [13.1, 10.4];

  /*
   * Cuadros clave (propPhase). `mano`: la mano derecha con el cuerpo quieto (se
   * le suma `dy`). `yoyo`: dónde está el yoyó; sin él, lo tiene en la mano.
   * `piso`: el yoyó apoyado, que no sigue al cuerpo cuando se inclina. `giro`
   * lo pone la tabla, de a un octavo de vuelta, siempre hacia adelante: rodar
   * hacia la derecha es girar en el sentido del reloj. `zumba`: los arquitos
   * de velocidad alrededor del disco. `manoI`: la mano izquierda en alto, para
   * el remate; va por brazoGuitarra y no con el brazo del motor, que se
   * dibuja detrás del cuerpo y en Goku, Buu o Pikachu no asomaba.
   *
   * Todo queda a más de una unidad del borde derecho (18): el perrito termina
   * con el disco en 15,7 y su contorno en 16,95.
   */
  const YOYO = (() => {
    const O = Math.PI / 4;
    const L = [
      { mano: [12.2, 12.6], dy: 0 },                                  //  0 lo saca
      { mano: [14.6, 8.4], dy: -0.25 },                                //  1 apunta
      { mano: [15.0, 9.6], yoyo: [15.2, 11.6], dy: 0, zumba: 1 },      //  2 lo tira
      { mano: [15.0, 9.5], yoyo: [15.2, 13.4], dy: 0, zumba: 1 },      //  3 baja girando
    ];
    // 4..7 dormido al final del hilo, girando en el lugar.
    for (let i = 0; i < 4; i++) L.push({ mano: [15.0, 9.4], yoyo: [15.2, 14.6], dy: 0, zumba: 1 });
    // 8 toca el piso.
    L.push({ mano: [14.6, 10.4], yoyo: [14.5, YOYO_PISO], dy: 0.2, piso: 1, zumba: 1 });
    // 9..14 el perrito: rueda hacia adelante con el hilo tenso.
    [14.6, 14.85, 15.1, 15.35, 15.55, 15.7].forEach((x, i) =>
      L.push({ mano: [14.5 + 0.1 * i, 10.6 + 0.08 * i], yoyo: [x, YOYO_PISO], dy: 0.2, piso: 1, zumba: 1 }));
    L.push(
      // 15 se lo lleva: el hilo tira de la mano y el cuerpo se va adelante.
      { mano: [15.3, 11.5], yoyo: [15.7, YOYO_PISO], dy: 0.35, piso: 1, zumba: 1 },
      // 16 el tirón: la mano arriba y el yoyó saltando del piso.
      { mano: [14.4, 7.9], yoyo: [15.2, 12.2], dy: -0.25, zumba: 1 },
      // 17 sube por el hilo.
      { mano: [14.5, 8.1], yoyo: [14.8, 9.7], dy: -0.2, zumba: 1 },
      { mano: [14.6, 8.2], dy: -0.1 },                                 // 18 lo atrapa
      { mano: [13.4, 11.6], dy: 0, manoI: [1.5, 5.2] },                // 19 remate
    );
    // El giro corre durante todo el gesto: cada cuadro con yoyó en el aire
    // avanza un octavo de vuelta respecto del anterior.
    let g = 0;
    L.forEach(f => { if (f.yoyo) g += O; f.giro = g; });
    return L;
  })();
  function poseYoyo(i, cara) {
    const f = YOYO[i];
    const pose = { prop: 'yoyo', propPhase: i, armR: false, bodyDY: f.dy };
    if (f.manoI) pose.armL = false;
    return Object.assign(pose, cara);
  }

  // El disco de frente: dos mitades del tema cortadas por el giro, el eje y el
  // contorno oscuro. `zumba`: dos arquitos que lo siguen, sólo con detalle.
  function discoYoyo(c, x, y, giro, colA, colB, det, zumba) {
    const r = YOYO_R, C = YOYO_COL;
    c.save();
    c.translate(x, y);
    ellipsePath(c, 0, 0, r, r);
    c.fillStyle = colA; c.fill();
    c.save();
    c.clip();
    c.rotate(giro);
    c.fillStyle = colB; c.fillRect(-r, 0, 2 * r, r);
    c.rotate(-giro);
    // Sombra de cel abajo a la derecha: sin ella es una pastilla plana.
    ellipsePath(c, -0.22, -0.22, r, r);
    c.rect(r, -r, -2 * r, 2 * r);
    c.fillStyle = withA('#000000', 0.22); c.fill('evenodd');
    c.restore();
    // El eje: chico, para que no tape el corte de las mitades.
    ellipsePath(c, 0, 0, r * 0.22, r * 0.22);
    c.fillStyle = C.eje; c.fill();
    if (det) {
      // El contorno del eje y un brillo arriba a la izquierda.
      c.lineWidth = 0.07; c.strokeStyle = C.contorno; c.stroke();
      c.beginPath(); c.arc(0, 0, r * 0.74, -2.7, -1.9);
      c.lineCap = 'round';
      c.lineWidth = 0.12; c.strokeStyle = withA('#ffffff', 0.6); c.stroke();
    }
    ellipsePath(c, 0, 0, r, r);
    c.lineWidth = 0.16; c.strokeStyle = C.contorno; c.stroke();
    if (zumba && det) {
      // Los arquitos van un poco detrás del corte, como si lo persiguieran.
      c.lineCap = 'round';
      c.lineWidth = 0.1; c.strokeStyle = withA('#ffffff', 0.55);
      [0, Math.PI].forEach(a => {
        c.beginPath(); c.arc(0, 0, r + 0.22, giro + a - 1.2, giro + a - 0.4); c.stroke();
      });
    }
    c.restore();
  }

  // Las dos mitades: el primero de la serie y el que más se le aleja. Con un
  // índice fijo, en dragon salían salvia y arena, dos tonos del mismo valor, y
  // el corte que cuenta el giro no se veía. Un color que no es hex no compite.
  function mitadesYoyo(serie) {
    const rgb = h => (/^#[0-9a-f]{6}$/i.test(h) ? [16, 8, 0].map(d => (parseInt(h.slice(1), 16) >> d) & 255) : null);
    const a = rgb(serie[0]);
    let mejor = serie[1], dist = -1;
    serie.slice(1).forEach(col => {
      const b = rgb(col);
      const d = a && b ? Math.hypot(a[0] - b[0], a[1] - b[1], a[2] - b[2]) : 0;
      if (d > dist) { dist = d; mejor = col; }
    });
    return [serie[0], mejor];
  }

  function propYoyo(c, P, pose) {
    const f = YOYO[pose.propPhase || 0] || YOYO[0];
    const dy = pose.bodyDY || 0;
    const det = _k >= YOYO_DETALLE_PX;
    const T = coloresTema();
    const [colA, colB] = mitadesYoyo(T.serie);
    const tela = llevaBata() ? _bataTela : null;
    const ch = charActual();
    const m = [f.mano[0], f.mano[1] + dy];
    if (f.manoI) {
      brazoGuitarra(c, P, tela, ch, -1, 2.9, 10.4 + dy, f.manoI[0], f.manoI[1] + dy);
      manoGuitarra(c, P, f.manoI[0], f.manoI[1] + dy);
    }
    brazoGuitarra(c, P, tela, ch, 1, YOYO_HOMBRO[0], YOYO_HOMBRO[1] + dy, m[0], m[1]);
    if (f.yoyo) {
      // En el aire sigue a la mano; apoyado, se queda en el piso.
      const yx = f.yoyo[0], yy = f.yoyo[1] + (f.piso ? 0 : dy);
      // Apoyado lleva su sombrita: sin ella no se lee que rueda POR el piso.
      if (f.piso) {
        ellipsePath(c, yx, yy + YOYO_R - 0.05, YOYO_R * 1.05, 0.28);
        c.fillStyle = withA('#000000', 0.28); c.fill();
      }
      // El hilo, del dedo al eje: borde oscuro y alma clara.
      c.beginPath(); c.moveTo(m[0] + 0.1, m[1] + 0.25); c.lineTo(yx, yy);
      c.lineCap = 'round';
      c.lineWidth = 0.24; c.strokeStyle = withA(YOYO_COL.contorno, 0.85); c.stroke();
      c.lineWidth = 0.11; c.strokeStyle = YOYO_COL.hilo; c.stroke();
      discoYoyo(c, yx, yy, f.giro, colA, colB, det, f.zumba);
      manoGuitarra(c, P, m[0], m[1]);
    } else {
      // En la mano: el disco detrás de la mano, que lo agarra desde abajo.
      discoYoyo(c, m[0] + 0.15, m[1] - 0.6, f.giro, colA, colB, det, 0);
      manoGuitarra(c, P, m[0], m[1]);
    }
  }

  /**
   * Jueguito con la pelota, con los dos brazos abiertos para el equilibrio.
   *
   * - La pelota es la clásica: cuero blanco, parches oscuros y contorno
   *   oscuro, con colores FIJOS como el skate. Una pelota del tema sobre un
   *   cuerpo del mismo tono se pierde, y blanca con parches se reconoce a
   *   cualquier escala.
   * - Se la ve de frente y el giro es el dibujo de los parches rotando en el
   *   plano. El patrón se repite cada quinto de vuelta, así que avanza de a
   *   JUEG_GIRO (un cuarto de ese período) por cuadro: con pasos de más de
   *   medio período el ojo lo lee girando para el otro lado, como el yoyó.
   * - Los personajes no tienen piernas: el pie es el muñón de siempre, puesto
   *   en el lienzo con `pies`. El toque de pie lo levanta poco y adelante; el
   *   de rodilla, alto y pegado al cuerpo. Lo que distingue un toque del otro
   *   es la altura del contacto, que es lo que se lee a escala de topbar.
   * - El pie del motor se pinta DETRÁS del cuerpo, así que el levantado va lo
   *   bastante afuera para asomar: la panza a la altura de la rodilla llega a
   *   x 13,1 y el pie del toque alto va en 13,7.
   * - Cuando la pisa, la pelota va detrás de los pies (el prop está en
   *   PROPS_BEHIND) para que la suela quede ENCIMA; en el aire va delante del
   *   cuerpo (PROP_DELANTE), que es donde se la ve picar sobre la rodilla y el
   *   pecho.
   * - Nudo y remate: un toque con la punta le sale torcido y la pelota se va
   *   alto, casi al rincón del lienzo; la sigue con la vista, la baja con el
   *   pecho, la frena con la planta y remata con el brazo arriba.
   * - El tiro alto queda adentro del lienzo: la pelota llega al margen de
   *   arriba (su borde en -0,75, el lienzo empieza en -2: a escala 1,4 un
   *   píxel es 0,7 u y el suavizado del contorno ya lo alcanzaba con menos
   *   aire; lo mismo a la derecha) y el cuerpo no sube
   *   más que -0,25, el alto que ya aguantaba el pelo de Rick en el yoyó.
   *
   * Las costuras, el brillo y las marcas de impacto se apagan por debajo de
   * JUEG_DETALLE_PX: a escala de topbar son ruido y tapan los parches, que es
   * lo que cuenta el giro.
   */
  const JUEG_COL = {
    contorno: '#17120e', cuero: '#f7f4ec', cueroSombra: '#c3bfb5',
    parche: '#23252b', costura: '#8f8b82', estela: '#f4ecd6',
  };
  const JUEG_R = 1.35;
  const JUEG_DETALLE_PX = 5;
  // El piso: la suela de los pies del motor (BODY.bot + 0,55 + 0,8).
  const JUEG_PISO = 16.4;
  const JUEG_GIRO = (2 * Math.PI / 5) / 4;
  // Los hombros de los que salen los brazos (sólo dan la dirección del muñón).
  const JUEG_HOMBRO_I = [2.9, 10.4], JUEG_HOMBRO_D = [13.1, 10.4];
  // Pie de apoyo y pie que juega, en el lienzo.
  const JUEG_APOYO = [6.1, 15.6];
  const JUEG_PIE_ABAJO = [11.0, 15.35];
  const JUEG_PIE_PISA = [13.4, 13.35];
  const JUEG_PELOTA_PISO = [13.4, JUEG_PISO - JUEG_R];

  /*
   * Cuadros clave (propPhase). `dy`: bodyDY del cuerpo. `pie`: el pie que
   * juega (el de apoyo es JUEG_APOYO). `manos` [xI, yI, xD, yD] con el cuerpo
   * quieto: se les suma `dy`. `pelota`: su centro en el lienzo; no sigue al
   * cuerpo, está en el aire. `atras`: la pisa, va detrás de los pies.
   * `toque`: de qué lado le pega algo ('abajo', el pie o la rodilla;
   * 'izq', el pecho), para las marcas de impacto. `vel`: hacia dónde va
   * rápido, para la estela. El giro lo pone la tabla.
   */
  const JUEGUITO = (() => {
    const L = [
      // 0 la pisa, lista para arrancar.
      { dy: 0, pie: JUEG_PIE_PISA, manos: [1.8, 12.8, 15.9, 10.6], pelota: JUEG_PELOTA_PISO, atras: 1, quieta: 1 },
      // 1 la levanta con la punta.
      { dy: -0.1, pie: [12.3, 15.3], manos: [1.0, 11.4, 15.9, 11.0], pelota: [13.6, 12.6], vel: [0.1, -1] },
      // 2 sube.
      { dy: 0, pie: JUEG_PIE_ABAJO, manos: [0.8, 10.8, 15.9, 10.6], pelota: [13.6, 10.0], vel: [0, -1] },
      // 3 toque de pie.
      { dy: 0.1, pie: [13.0, 14.75], manos: [0.7, 10.6, 15.9, 10.0], pelota: [13.3, 12.95], toque: 'abajo' },
      // 4 en el aire.
      { dy: 0, pie: JUEG_PIE_ABAJO, manos: [0.8, 10.4, 15.9, 10.4], pelota: [13.2, 9.5] },
      // 5 toque de rodilla: el pie alto, saltito en el de apoyo.
      { dy: -0.2, pie: [13.7, 12.5], manos: [0.6, 10.0, 16.1, 11.6], pelota: [14.0, 10.65], toque: 'abajo' },
      // 6 en el aire.
      { dy: 0, pie: JUEG_PIE_ABAJO, manos: [0.8, 10.6, 15.9, 10.2], pelota: [13.4, 9.3] },
      // 7 toque de pie.
      { dy: 0.1, pie: [13.0, 14.75], manos: [0.7, 10.8, 15.9, 10.0], pelota: [13.3, 12.95], toque: 'abajo' },
      // 8 en el aire.
      { dy: 0, pie: JUEG_PIE_ABAJO, manos: [0.8, 10.4, 15.9, 10.4], pelota: [13.3, 9.5] },
      // 9 toque de rodilla.
      { dy: -0.2, pie: [13.7, 12.5], manos: [0.6, 10.0, 16.1, 11.6], pelota: [14.0, 10.65], toque: 'abajo' },
      // 10 en el aire, un poco más afuera: el toque que viene ya llega torcido.
      { dy: 0, pie: JUEG_PIE_ABAJO, manos: [0.8, 10.6, 16.0, 10.4], pelota: [13.9, 9.4] },
      // 11 la pifia: le pega con la punta y sale para arriba y afuera.
      { dy: 0.1, pie: [13.6, 14.5], manos: [1.0, 9.6, 15.2, 8.6], pelota: [14.9, 12.7], toque: 'abajo', vel: [0.3, -1] },
      // 12 se va alto.
      { dy: -0.1, pie: JUEG_PIE_ABAJO, manos: [1.2, 7.6, 14.8, 8.6], pelota: [15.3, 5.0], vel: [0.2, -1] },
      // 13 casi se escapa: el rincón del lienzo; se estira mirándola.
      { dy: -0.25, pie: [10.6, 15.5], manos: [1.0, 6.2, 14.4, 6.6], pelota: [15.4, 0.6] },
      // 14 baja; abre los brazos para hacerle lugar en el pecho.
      { dy: 0, pie: JUEG_PIE_ABAJO, manos: [0.6, 9.8, 15.9, 12.0], pelota: [15.1, 4.8], vel: [-0.15, 1] },
      // 15 la baja con el pecho: se hunde para amortiguarla.
      { dy: 0.3, pie: [10.6, 15.6], manos: [0.3, 10.4, 16.2, 12.6], pelota: [14.6, 9.6], toque: 'izq' },
      // 16 resbala del pecho.
      { dy: 0.1, pie: JUEG_PIE_ABAJO, manos: [0.6, 10.8, 16.0, 12.2], pelota: [14.6, 12.2], vel: [-0.1, 1] },
      // 17 llega al piso y el pie ya va a buscarla.
      { dy: 0, pie: [13.5, 12.4], manos: [0.8, 11.2, 15.9, 11.4], pelota: [13.4, 14.85] },
      // 18 la frena con la planta.
      { dy: 0.15, pie: JUEG_PIE_PISA, manos: [1.2, 11.8, 16.0, 10.4], pelota: JUEG_PELOTA_PISO, atras: 1, quieta: 1 },
      // 19 remate: la pisa y levanta el brazo.
      { dy: 0, pie: JUEG_PIE_PISA, manos: [1.5, 5.0, 15.9, 10.2], pelota: JUEG_PELOTA_PISO, atras: 1, quieta: 1 },
    ];
    // El giro corre mientras la pelota se mueve: cada cuadro que no la tiene
    // quieta avanza JUEG_GIRO respecto del anterior.
    let g = 0;
    L.forEach(f => { if (!f.quieta) g += JUEG_GIRO; f.giro = g; });
    return L;
  })();
  function poseJueguito(i, cara) {
    const f = JUEGUITO[i];
    return Object.assign({ prop: 'jueguito', propPhase: i, armL: false, armR: false, bodyDY: f.dy,
      pies: [JUEG_APOYO, f.pie] }, cara);
  }
  const cuadroJueguito = (pose) => JUEGUITO[pose.propPhase || 0] || JUEGUITO[0];

  // Un polígono regular de `n` lados, con un vértice hacia `a`.
  function poligonoPath(c, x, y, r, n, a) {
    c.beginPath();
    for (let i = 0; i < n; i++) {
      const t = a + (i * 2 * Math.PI) / n;
      i ? c.lineTo(x + Math.cos(t) * r, y + Math.sin(t) * r) : c.moveTo(x + Math.cos(t) * r, y + Math.sin(t) * r);
    }
    c.closePath();
  }

  // La pelota de frente: un parche al centro y cinco que asoman por el borde,
  // achatados por la curva. `giro` rota el dibujo, no la sombra ni el brillo:
  // la luz no gira con la pelota.
  function pelotaJueguito(c, x, y, giro, det) {
    const r = JUEG_R, C = JUEG_COL;
    c.save();
    c.translate(x, y);
    ellipsePath(c, 0, 0, r, r);
    const g = c.createRadialGradient(-0.3 * r, -0.35 * r, 0.1, 0, 0, r);
    g.addColorStop(0, C.cuero); g.addColorStop(0.7, C.cuero); g.addColorStop(1, C.cueroSombra);
    c.fillStyle = g; c.fill();
    c.save();
    c.clip();
    c.rotate(giro);
    const VERT = -Math.PI / 2;
    if (det) {
      // Las costuras: de cada vértice del parche del centro al de afuera.
      c.beginPath();
      for (let i = 0; i < 5; i++) {
        const t = VERT + (i * 2 * Math.PI) / 5;
        c.moveTo(Math.cos(t) * r * 0.36, Math.sin(t) * r * 0.36);
        c.lineTo(Math.cos(t) * r * 0.74, Math.sin(t) * r * 0.74);
      }
      c.lineWidth = 0.06; c.strokeStyle = C.costura; c.stroke();
    }
    poligonoPath(c, 0, 0, r * 0.36, 5, VERT);
    c.fillStyle = C.parche; c.fill();
    // Los de afuera, en la dirección de cada vértice del central: achatados
    // hacia el centro, como los ve quien mira una esfera de frente.
    for (let i = 0; i < 5; i++) {
      const t = VERT + (i * 2 * Math.PI) / 5;
      c.save();
      c.rotate(t);
      c.translate(r * 0.98, 0);
      c.scale(0.55, 1);
      poligonoPath(c, 0, 0, r * 0.4, 5, Math.PI);
      c.restore();
      c.fillStyle = C.parche; c.fill();
    }
    c.rotate(-giro);
    // Sombra de cel abajo a la derecha, fija: sin ella es un disco plano.
    ellipsePath(c, -0.2, -0.24, r, r);
    c.rect(r, -r, -2 * r, 2 * r);
    c.fillStyle = withA('#000000', 0.18); c.fill('evenodd');
    c.restore();
    if (det) {
      c.beginPath(); c.arc(0, 0, r * 0.72, -2.75, -2.0);
      c.lineCap = 'round';
      c.lineWidth = 0.12; c.strokeStyle = withA('#ffffff', 0.75); c.stroke();
    }
    ellipsePath(c, 0, 0, r, r);
    c.lineWidth = 0.15; c.strokeStyle = C.contorno; c.stroke();
    c.restore();
  }

  // La estela: tres rayitas detrás de la pelota, del lado contrario a `vel`.
  // Borde oscuro y alma clara, como el hilo del yoyó, para los dos fondos.
  function estelaJueguito(c, x, y, vel) {
    const L = Math.hypot(vel[0], vel[1]) || 1;
    const ux = -vel[0] / L, uy = -vel[1] / L;
    const tx = -uy, ty = ux;
    c.save();
    c.lineCap = 'round';
    [-0.55, 0, 0.55].forEach((o, i) => {
      const a = JUEG_R + 0.25 + (i === 1 ? 0 : 0.2), b = a + (i === 1 ? 1.1 : 0.7);
      c.beginPath();
      c.moveTo(x + ux * a + tx * o, y + uy * a + ty * o);
      c.lineTo(x + ux * b + tx * o, y + uy * b + ty * o);
      c.lineWidth = 0.22; c.strokeStyle = withA(JUEG_COL.contorno, 0.55); c.stroke();
      c.lineWidth = 0.1; c.strokeStyle = withA(JUEG_COL.estela, 0.9); c.stroke();
    });
    c.restore();
  }

  // Las marcas del impacto: dos rayitas a cada lado del punto de contacto,
  // abiertas hacia afuera.
  function impactoJueguito(c, x, y, lado) {
    const nx = lado === 'izq' ? -1 : 0, ny = lado === 'izq' ? 0 : 1;
    const tx = -ny, ty = nx;
    const px = x + nx * JUEG_R, py = y + ny * JUEG_R;
    c.save();
    c.lineCap = 'round';
    c.beginPath();
    [-1, 1].forEach(s => {
      c.moveTo(px + tx * s * 0.75 - nx * 0.05, py + ty * s * 0.75 - ny * 0.05);
      c.lineTo(px + tx * s * 1.25 - nx * 0.35, py + ty * s * 1.25 - ny * 0.35);
    });
    c.lineWidth = 0.2; c.strokeStyle = withA(JUEG_COL.contorno, 0.6); c.stroke();
    c.lineWidth = 0.09; c.strokeStyle = JUEG_COL.estela; c.stroke();
    c.restore();
  }

  // Detrás de todo: la sombra en el piso (más chica y más tenue cuanto más
  // alta va la pelota: es lo que cuenta la altura) y la pelota cuando la pisa.
  function propJueguitoAtras(c, P, pose) {
    const f = cuadroJueguito(pose);
    const [x, y] = f.pelota;
    const alto = Math.max(0, JUEG_PISO - JUEG_R - y);
    const k = Math.max(0.35, 1 - alto * 0.06);
    ellipsePath(c, x, JUEG_PISO - 0.05, JUEG_R * 1.05 * k, 0.26 * k);
    c.fillStyle = withA('#000000', 0.3 * k); c.fill();
    if (f.atras) pelotaJueguito(c, x, y, f.giro, _k >= JUEG_DETALLE_PX);
  }

  // Delante del cuerpo: los brazos abiertos y la pelota en el aire.
  function propJueguitoDelante(c, P, pose) {
    const f = cuadroJueguito(pose);
    const dy = f.dy, m = f.manos;
    const det = _k >= JUEG_DETALLE_PX;
    const tela = llevaBata() ? _bataTela : null;
    const ch = charActual();
    brazoGuitarra(c, P, tela, ch, -1, JUEG_HOMBRO_I[0], JUEG_HOMBRO_I[1] + dy, m[0], m[1] + dy);
    brazoGuitarra(c, P, tela, ch, 1, JUEG_HOMBRO_D[0], JUEG_HOMBRO_D[1] + dy, m[2], m[3] + dy);
    manoGuitarra(c, P, m[0], m[1] + dy);
    manoGuitarra(c, P, m[2], m[3] + dy);
    if (f.atras) return;
    const [x, y] = f.pelota;
    if (f.vel) estelaJueguito(c, x, y, f.vel);
    pelotaJueguito(c, x, y, f.giro, det);
    if (f.toque && det) impactoJueguito(c, x, y, f.toque);
  }

  /**
   * El mate: la calabaza con su bombilla, el termo y las dos manos.
   *
   * - El mate va en la mano IZQUIERDA y el termo en la derecha. Al revés, el
   *   termo cebando desde la izquierda quedaba cruzado sobre la cara; desde la
   *   derecha se inclina hacia afuera del cuerpo y se lee contra el fondo.
   * - Lo que dice "mate" y no "taza" es la silueta: panza redonda que se
   *   angosta hacia la boca, la virola de cuero, la yerba verde asomando en
   *   montañita y la bombilla de metal en diagonal. Colores fijos con
   *   contorno oscuro; sólo el cuerpo del termo toma el color del tema.
   * - La bombilla está inclinada hacia la boca y es rígida con el mate: para
   *   tomar, el mate se ubica de modo que la punta caiga en la boca del
   *   personaje (bocaTaza, la misma del café), así llega en todos.
   * - Nudo y remate: el primer sorbo quema, sopla, el segundo sale bien y el
   *   ruidito del mate vacío; las partículas las pone g_mate.
   *
   * El brillo de la bombilla y la costura de la virola se apagan por debajo
   * de MATE_DETALLE_PX: a escala de topbar son ruido sobre un objeto de tres
   * unidades.
   */
  const MATE_COL = {
    contorno: '#1c120a',
    calabaza: '#8a5428', calabazaLuz: '#b97a3f', calabazaSombra: '#4f2c12',
    cuero: '#3a2213', costura: '#e2c28e', interior: '#1f170e',
    yerba: '#7aa83a', yerbaLuz: '#a7cf5f', yerbaHumeda: '#4b7322',
    bombilla: '#c4ccd6', bombillaLuz: '#f5f8fb', bombillaSombra: '#7b8692',
    tapa: '#b9c1cb', tapaSombra: '#727c88', agua: '#d6eef8',
  };
  const MATE_DETALLE_PX = 5;
  // Escala del mate y del termo respecto de sus coordenadas locales.
  const MATE_K = 1.15, TERMO_K = 1.2;
  // Boca del mate (rx, ry), bombilla (de dónde sale, inclinación desde la
  // vertical hacia la izquierda y largo) y dónde lo agarra la mano izquierda.
  const MATE_BOCA = [0.78, 0.22];
  const BOMBILLA = { x: -0.2, y: 0.1, giro: 0.42, largo: 2.1 };
  const MATE_AGARRE = [-1.25, 1.35];
  // Largo del termo, del pico a la base, y dónde lo agarra la mano derecha.
  const TERMO_LARGO = 4.6, TERMO_AGARRE = 3.5;
  const MATE_HOMBRO_I = [2.9, 10.4], MATE_HOMBRO_D = [13.1, 10.4];
  // El termo apoyado en el piso, a la derecha: x de su eje y la suela.
  const TERMO_PISO = [15.4, 15.35];

  /*
   * Cuadros clave (propPhase). `mate`: [x, y, giro] del centro de la boca del
   * mate; `sigue` le suma la altura de la boca del personaje, para que la
   * bombilla no le pase por los ojos a quien los tiene bajos. `boca`, en
   * lugar de `mate`: el giro del sorbo, con la punta de la bombilla en la
   * boca. `termo`: [x, y, giro] del pico, con la mano derecha; `piso`: el
   * termo ya dejado, y el brazo derecho vuelve a ser el del motor.
   * `chorro`: el agua del pico al mate, en dos variantes que alternan.
   */
  const MATE = [
    { dy: 0,     mate: [4.6, 12.3, 0.1],   termo: [14.7, 9.0, 0] },             // 0 los saca
    { dy: 0,     mate: [10.6, 12.9, 0.05], termo: [12.4, 9.4, -0.8] },          // 1 se prepara
    { dy: 0.1,   mate: [11.2, 13.0, 0.1],  termo: [12.0, 11.5, -2.45], chorro: 1 },// 2 ceba
    { dy: 0.1,   mate: [11.2, 13.0, 0.1],  termo: [12.0, 11.5, -2.45], chorro: 2 },// 3 ceba
    { dy: 0,     mate: [11.6, 12.4, 0],    sigue: 1, piso: 1 },                 // 4 deja el termo
    { dy: -0.1,  boca: -0.5, piso: 1 },                                         // 5 primer sorbo
    { dy: -0.2,  mate: [12.4, 12.4, -0.1], sigue: 1, piso: 1 },                 // 6 ¡quema!
    { dy: 0,     mate: [11.9, 12.0, -0.2], sigue: 1, piso: 1 },                 // 7 sopla
    { dy: 0.05,  mate: [11.9, 12.1, -0.15], sigue: 1, piso: 1 },                // 8 sopla (entre soplidos)
    { dy: -0.1,  boca: -0.55, piso: 1 },                                        // 9 segundo sorbo
    { dy: -0.25, boca: -0.75, piso: 1 },                                        // 10 el ruidito
    { dy: 0.1,   mate: [11.8, 12.6, 0.05], sigue: 1, piso: 1 },                 // 11 "ahh"
  ];
  function poseMate(i, cara) {
    const f = MATE[i];
    const pose = { prop: 'mate', propPhase: i, armL: false, bodyDY: f.dy };
    if (!f.piso) pose.armR = false;
    return Object.assign(pose, cara);
  }

  // La punta de la bombilla en coordenadas del mate.
  function puntaBombilla() {
    const B = BOMBILLA;
    return [B.x - B.largo * Math.sin(B.giro), B.y - B.largo * Math.cos(B.giro)];
  }

  // Dónde queda el mate en un cuadro: [x, y, giro] del centro de su boca.
  function lugarMate(f, ch) {
    const cara = bocaTaza(ch);
    if (f.boca == null) {
      const m = f.mate;
      return [m[0], m[1] + (f.sigue ? cara - 10.7 : 0), m[2]];
    }
    // La punta de la bombilla, en la boca 'o' que ponen las poses del sorbo: el
    // mate se cuelga de ahí con el giro del cuadro.
    const a = f.boca, [px, py] = puntaBombilla();
    const ca = Math.cos(a), sa = Math.sin(a);
    const mx = BODY.cx + 0.15, my = cara + f.dy + 0.05;
    return [mx - (px * ca - py * sa) * MATE_K, my - (px * sa + py * ca) * MATE_K, a];
  }

  // La calabaza: de la boca al fondo, panza redonda y cuello angosto, en un
  // solo trazo, con el frente de la boca como borde de arriba.
  function calabazaPath(c) {
    const [rx, ry] = MATE_BOCA, PI = Math.PI;
    c.beginPath();
    c.moveTo(-rx, 0);
    c.lineTo(-0.88, 0.5);
    c.bezierCurveTo(-1.6, 1.0, -1.35, 2.8, 0, 2.8);
    c.bezierCurveTo(1.35, 2.8, 1.6, 1.0, 0.88, 0.5);
    c.lineTo(rx, 0);
    c.ellipse(0, 0, rx, ry, 0, 0, PI, false);
    c.closePath();
  }

  // El mate en sus coordenadas: calabaza, virola, yerba y bombilla.
  function pintarMate(c, det) {
    const C = MATE_COL, [rx, ry] = MATE_BOCA, PI = Math.PI;
    calabazaPath(c);
    const g = c.createLinearGradient(-1.4, 0, 1.4, 0);
    g.addColorStop(0, C.calabazaLuz); g.addColorStop(0.45, C.calabaza); g.addColorStop(1, C.calabazaSombra);
    c.fillStyle = g; c.fill();
    // La virola de cuero: una franja con el borde de abajo curvo, como el
    // de la boca, para que abrace a la calabaza en lugar de taparla.
    c.beginPath();
    c.ellipse(0, 0, rx, ry, 0, PI, 0, true);
    c.lineTo(0.9, 0.58);
    c.ellipse(0, 0.58, 0.9, 0.22, 0, 0, PI, false);
    c.closePath();
    c.fillStyle = C.cuero; c.fill();
    if (det) {
      c.beginPath();
      c.ellipse(0, 0.31, 0.84, 0.22, 0, 0.15 * PI, 0.85 * PI, false);
      c.setLineDash([0.12, 0.1]);
      c.lineWidth = 0.05; c.strokeStyle = C.costura; c.stroke();
      c.setLineDash([]);
      // Un brillo en la panza, arriba a la izquierda.
      c.beginPath(); c.arc(-0.2, 1.75, 0.95, -2.75, -2.05);
      c.lineCap = 'round';
      c.lineWidth = 0.14; c.strokeStyle = withA('#ffffff', 0.35); c.stroke();
    }
    calabazaPath(c);
    c.lineWidth = 0.13; c.strokeStyle = C.contorno; c.stroke();

    // La boca: adentro oscuro, la yerba mojada en el pozo y la montañita seca
    // a la derecha, lejos de la bombilla.
    ellipsePath(c, 0, 0, rx, ry);
    c.fillStyle = C.yerbaHumeda; c.fill();
    c.lineWidth = 0.1; c.strokeStyle = C.contorno; c.stroke();
    c.beginPath();
    c.moveTo(-0.15, 0.1);
    c.quadraticCurveTo(0.2, -0.75, 0.78, -0.02);
    c.quadraticCurveTo(0.42, 0.22, -0.15, 0.1);
    c.closePath();
    c.fillStyle = C.yerba; c.fill();
    c.lineWidth = 0.07; c.strokeStyle = C.contorno; c.stroke();
    if (det) {
      c.fillStyle = C.yerbaLuz;
      [[0.25, -0.22], [0.45, -0.1], [0.12, -0.05]].forEach(([x, y]) => { c.fillRect(x, y, 0.09, 0.07); });
    }

    // La bombilla: tubo de metal con contorno y, con detalle, el brillo. La
    // boquilla es la punta aplastada, un poco más ancha.
    const B = BOMBILLA, [tx, ty] = puntaBombilla();
    c.lineCap = 'round';
    c.beginPath(); c.moveTo(B.x, B.y); c.lineTo(tx, ty);
    c.lineWidth = 0.32; c.strokeStyle = C.contorno; c.stroke();
    c.lineWidth = 0.18; c.strokeStyle = C.bombilla; c.stroke();
    c.save();
    c.translate(tx, ty); c.rotate(-B.giro);
    ellipsePath(c, 0, 0.05, 0.17, 0.26);
    c.fillStyle = C.bombilla; c.fill();
    c.lineWidth = 0.07; c.strokeStyle = C.contorno; c.stroke();
    c.restore();
    if (det) {
      const ux = -Math.cos(B.giro) * 0.05, uy = Math.sin(B.giro) * 0.05;
      c.beginPath();
      c.moveTo(B.x + (tx - B.x) * 0.2 + ux, B.y + (ty - B.y) * 0.2 + uy);
      c.lineTo(B.x + (tx - B.x) * 0.85 + ux, B.y + (ty - B.y) * 0.85 + uy);
      c.lineWidth = 0.06; c.strokeStyle = C.bombillaLuz; c.stroke();
    }
  }

  // El termo en sus coordenadas: origen en el pico, `y` hacia la base. La
  // manija va detrás del cuerpo, a la derecha; el cuerpo, del color del tema.
  function pintarTermo(c, col, det) {
    const C = MATE_COL, L = TERMO_LARGO;
    c.lineCap = 'round';
    c.beginPath();
    c.moveTo(0.55, 1.4); c.lineTo(1.0, 1.6); c.lineTo(1.0, 3.5); c.lineTo(0.55, 3.7);
    c.lineWidth = 0.34; c.strokeStyle = C.contorno; c.stroke();
    c.lineWidth = 0.18; c.strokeStyle = C.tapaSombra; c.stroke();
    // Cuerpo: un cilindro con la luz a la izquierda y una raya de brillo.
    c.beginPath(); c.roundRect(-0.62, 0.9, 1.24, L - 1.05, 0.2);
    const g = c.createLinearGradient(-0.62, 0, 0.62, 0);
    g.addColorStop(0, withA('#ffffff', 0)); g.addColorStop(1, withA('#000000', 0.35));
    c.fillStyle = col; c.fill();
    c.fillStyle = g; c.fill();
    c.fillStyle = withA('#ffffff', det ? 0.45 : 0.3);
    c.fillRect(-0.38, 1.15, 0.14, L - 1.6);
    c.lineWidth = 0.12; c.strokeStyle = C.contorno; c.stroke();
    // Tapa con el pico cebador y el aro de la base, de metal.
    c.beginPath(); c.roundRect(-0.48, 0.05, 0.96, 0.9, 0.18);
    c.moveTo(-0.16, 0.08); c.lineTo(-0.12, -0.15); c.lineTo(0.12, -0.15); c.lineTo(0.16, 0.08);
    const t = c.createLinearGradient(-0.48, 0, 0.48, 0);
    t.addColorStop(0, C.tapa); t.addColorStop(1, C.tapaSombra);
    c.fillStyle = t; c.fill();
    c.lineWidth = 0.1; c.strokeStyle = C.contorno; c.stroke();
    c.beginPath(); c.roundRect(-0.64, L - 0.35, 1.28, 0.35, 0.1);
    c.fillStyle = t; c.fill();
    c.lineWidth = 0.1; c.strokeStyle = C.contorno; c.stroke();
  }

  function propMate(c, P, pose) {
    const f = MATE[pose.propPhase || 0] || MATE[0];
    const det = _k >= MATE_DETALLE_PX;
    const ch = charActual();
    const tela = llevaBata() ? _bataTela : null;
    const dy = pose.bodyDY || 0;
    const col = coloresTema().serie[0];
    const enMate = (x, y, a, lx, ly) => {
      const ca = Math.cos(a), sa = Math.sin(a);
      return [x + (lx * ca - ly * sa) * MATE_K, y + (lx * sa + ly * ca) * MATE_K];
    };

    if (f.piso) {
      c.save();
      c.translate(TERMO_PISO[0], TERMO_PISO[1] - TERMO_LARGO * TERMO_K);
      c.scale(TERMO_K, TERMO_K);
      pintarTermo(c, col, det);
      c.restore();
    }

    // El mate, con la mano izquierda: el muñón detrás y la mano encima.
    const [mx, my, ma] = lugarMate(f, ch);
    const agarre = enMate(mx, my, ma, MATE_AGARRE[0], MATE_AGARRE[1]);
    brazoGuitarra(c, P, tela, ch, -1, MATE_HOMBRO_I[0], MATE_HOMBRO_I[1] + dy, agarre[0], agarre[1]);
    c.save();
    c.translate(mx, my); c.rotate(ma); c.scale(MATE_K, MATE_K);
    pintarMate(c, det);
    c.restore();
    manoGuitarra(c, P, agarre[0], agarre[1]);

    if (f.termo) {
      const [tx, ty, ta] = f.termo;
      const gx = tx - Math.sin(ta) * TERMO_AGARRE * TERMO_K, gy = ty + Math.cos(ta) * TERMO_AGARRE * TERMO_K;
      brazoGuitarra(c, P, tela, ch, 1, MATE_HOMBRO_D[0], MATE_HOMBRO_D[1] + dy, gx, gy);
      c.save();
      c.translate(tx, ty); c.rotate(ta); c.scale(TERMO_K, TERMO_K);
      pintarTermo(c, col, det);
      c.restore();
      manoGuitarra(c, P, gx, gy);
      if (f.chorro) {
        // Del pico al pozo, junto a la bombilla, cayendo en curva. Claro con
        // borde oscuro, como el hilo del yoyó: se lee en temas claros y oscuros.
        const sx = tx + Math.sin(ta) * 0.15 * TERMO_K, sy = ty - Math.cos(ta) * 0.15 * TERMO_K;
        const [ex, ey] = enMate(mx, my, ma, -0.05, -0.02);
        const cx = ex + (f.chorro === 1 ? 0.15 : 0.35), cy = sy - 0.1;
        c.lineCap = 'round';
        c.beginPath(); c.moveTo(sx, sy); c.quadraticCurveTo(cx, cy, ex, ey);
        c.lineWidth = 0.36; c.strokeStyle = withA(MATE_COL.contorno, 0.55); c.stroke();
        c.lineWidth = 0.2; c.strokeStyle = MATE_COL.agua; c.stroke();
        if (det) {
          // Las salpicaduras en el pozo, cambiando de lado entre cuadros.
          const s = f.chorro === 1 ? 1 : -1;
          c.fillStyle = MATE_COL.agua;
          ellipsePath(c, ex + 0.35 * s, ey - 0.3, 0.1, 0.1); c.fill();
          ellipsePath(c, ex - 0.25 * s, ey - 0.45, 0.08, 0.08); c.fill();
        }
      }
    }
  }

  /**
   * El avión de papel: la hoja, sus dobleces y el avión en la mano.
   *
   * - El papel pasa por cuatro etapas que se leen de un cuadro al otro: hoja
   *   rayada, "casita" (las dos puntas de arriba dobladas al centro), flecha
   *   vista desde arriba y el avión de perfil con la punta a la derecha. Los
   *   dobleces son lo que dice "papel": cada solapa va un tono más oscuro.
   * - Colores fijos con contorno oscuro (un avión de papel es blanco en todos
   *   los temas); sólo la raya del margen toma el color del tema. Renglones,
   *   margen y la línea del pliegue se apagan por debajo de AVION_DETALLE_PX.
   * - Lo que sale volando del cuadro NO se dibuja acá: el lienzo lo cortaría
   *   contra el borde, y el chequeo de alfa no lo aceptaría. El vuelo del
   *   remate es una partícula (`plt-pt-avion`) que emite g_avion desde la
   *   mano. El primer lanzamiento sí es del lienzo, porque termina a sus pies.
   * - Brazos por brazoGuitarra, con la mano encima del papel.
   */
  const AVION_COL = {
    contorno: '#22262e',
    papel: '#f7f5ef', solapa: '#dcdde2', sombra: '#b9bdc6',
    pliegue: '#8f96a3', renglon: '#9cc3e6',
  };
  const AVION_DETALLE_PX = 5;
  // Escala de la hoja (y sus dobleces) y del avión armado.
  const HOJA_K = 1.3, AVION_K = 1.4;
  const AVION_HOMBRO_I = [2.9, 10.4], AVION_HOMBRO_D = [13.1, 10.4];
  // Dónde lo agarra la mano derecha (la quilla, abajo) y la izquierda cuando
  // lo tiene parado (la cola), en coordenadas del avión.
  const AVION_AGARRE = [-0.25, 0.5], AVION_COLA = [-1.25, 0.3];

  /*
   * Cuadros clave (propPhase). `papel`: [x, y, giro, etapa] del centro del
   * papel (0 hoja, 1 casita, 2 flecha, 3 avión); `sigue` le suma la altura de
   * la boca del personaje, como el mate. `dos`: lo sostienen las dos manos por
   * los costados; `der`: la mano derecha por la quilla; `cola`: además la
   * izquierda por la cola. `pellizco`: la mano derecha pellizca la punta.
   * `suelto`: el avión en el aire, sin mano. `mano`: la derecha vacía en ese
   * punto; `visera`: la mano de visera sobre los ojos. `ñata`: la punta
   * aplastada del aterrizaje. Sin `mano`/`der`/`pellizco`/`visera`, el brazo
   * derecho es el del motor.
   */
  const AVION = [
    { dy: 0,    papel: [8, 13.0, 0, 0], dos: 1 },                           // 0 saca la hoja
    { dy: 0.05, papel: [8, 13.0, 0, 1], dos: 1 },                           // 1 dobla las puntas
    { dy: 0.05, papel: [8, 12.9, 0, 2], dos: 1 },                           // 2 la flecha
    { dy: 0,    papel: [11.2, 12.4, -0.05, 3], der: 1 },                    // 3 el avión, en la mano
    { dy: -0.1, papel: [12.3, 11.0, -0.15, 3], der: 1, sigue: 1 },          // 4 lo mira
    { dy: -0.15, papel: [12.8, 4.6, -0.35, 3], der: 1 },                    // 5 apunta, brazo arriba
    { dy: -0.05, papel: [13.6, 7.4, 0.12, 3], suelto: 1, mano: [14.9, 10.0] },// 6 lo lanza
    { dy: 0,    papel: [14.0, 12.4, 1.25, 3], suelto: 1 },                  // 7 en picada
    { dy: 0.1,  papel: [11.7, 14.55, 0.6, 3], suelto: 1, nata: 1 },         // 8 de punta, a los pies
    { dy: 0.3,  papel: [12.2, 14.1, 0.3, 3], der: 1, nata: 1 },             // 9 lo levanta
    { dy: 0,    papel: [10.2, 13.1, -1.5, 3], cola: 1, pellizco: [11.1, 10.75], nata: 1, sigue: 1 }, // 10 arregla la punta
    { dy: 0,    papel: [10.2, 13.1, -1.5, 3], cola: 1, pellizco: [10.95, 10.35], sigue: 1 },       // 11 arregla la punta
    { dy: -0.05, papel: [10.2, 13.1, -1.5, 3], cola: 1, sigue: 1 },         // 12 le sopla la punta
    { dy: -0.15, papel: [12.8, 4.6, -0.35, 3], der: 1 },                    // 13 apunta otra vez
    { dy: -0.2, mano: [14.6, 8.4] },                                        // 14 lo lanza: es partícula
    { dy: -0.1, visera: 1 },                                                // 15 lo sigue con la mano de visera
    { dy: -0.25, mano: [14.5, 4.8] },                                       // 16 festeja, puño arriba
  ];
  function poseAvion(i, cara) {
    const f = AVION[i];
    const pose = { prop: 'avion', propPhase: i, bodyDY: f.dy };
    if (f.dos || f.cola) pose.armL = false;
    if (f.dos || f.der || f.cola || f.mano || f.visera) pose.armR = false;
    return Object.assign(pose, cara);
  }

  // Un polígono cerrado, con relleno y contorno.
  function poliAvion(c, pts, relleno, ancho) {
    c.beginPath();
    pts.forEach(([x, y], i) => (i ? c.lineTo(x, y) : c.moveTo(x, y)));
    c.closePath();
    c.fillStyle = relleno; c.fill();
    c.lineWidth = ancho; c.strokeStyle = AVION_COL.contorno; c.stroke();
  }
  function rayaAvion(c, x0, y0, x1, y1, col, ancho) {
    c.beginPath(); c.moveTo(x0, y0); c.lineTo(x1, y1);
    c.lineWidth = ancho; c.strokeStyle = col; c.stroke();
  }

  // El papel en sus coordenadas, según la etapa. El avión, de perfil: la
  // punta a la derecha, el ala arriba y la quilla abajo, más oscura.
  function pintarPapelAvion(c, etapa, nata, col, det) {
    const C = AVION_COL, W = 1.5, H = 1.9;
    c.lineJoin = 'round'; c.lineCap = 'round';
    if (etapa === 0) {
      poliAvion(c, [[-W, -H], [W, -H], [W, H], [-W, H]], C.papel, 0.13);
      if (det) {
        for (let y = -1.1; y < H - 0.3; y += 0.65) rayaAvion(c, -W + 0.15, y, W - 0.15, y, C.renglon, 0.07);
        rayaAvion(c, -W + 0.55, -H + 0.12, -W + 0.55, H - 0.12, col, 0.08);
      }
      return;
    }
    if (etapa === 1) {
      // Las puntas de arriba, dobladas hasta el centro: la hoja queda con
      // techo y las dos solapas se ven como triángulos encima.
      poliAvion(c, [[0, -H], [W, -0.4], [W, H], [-W, H], [-W, -0.4]], C.papel, 0.13);
      if (det) for (let y = 0.35; y < H - 0.3; y += 0.65) rayaAvion(c, -W + 0.15, y, W - 0.15, y, C.renglon, 0.07);
      poliAvion(c, [[0, -H], [-W, -0.4], [0, -0.4]], C.solapa, 0.1);
      poliAvion(c, [[0, -H], [W, -0.4], [0, -0.4]], C.solapa, 0.1);
      return;
    }
    if (etapa === 2) {
      // La flecha, vista desde arriba: las alas abiertas y la quilla al medio.
      poliAvion(c, [[0, -2.1], [1.35, 1.8], [0.3, 1.55], [0, 1.95], [-0.3, 1.55], [-1.35, 1.8]], C.papel, 0.13);
      poliAvion(c, [[0, -2.1], [0.3, 1.55], [0, 1.95], [-0.3, 1.55]], C.solapa, 0.09);
      if (det) rayaAvion(c, 0, -1.9, 0, 1.85, C.pliegue, 0.06);
      return;
    }
    // El avión. Con la punta aplastada, la nariz se corre atrás y queda una
    // pestañita doblada hacia abajo.
    const N = nata ? [1.55, 0.2] : [2.1, 0.05];
    poliAvion(c, [N, [-1.45, 0.15], [-0.9, 0.85]], C.sombra, 0.12);
    poliAvion(c, [N, [-1.75, -0.95], [-1.45, 0.15]], C.papel, 0.13);
    if (nata) poliAvion(c, [N, [1.95, 0.55], [1.4, 0.6]], C.solapa, 0.09);
    if (det) rayaAvion(c, N[0] - 0.25, N[1] + 0.02, -1.35, 0.15, C.pliegue, 0.06);
  }

  function propAvion(c, P, pose) {
    const f = AVION[pose.propPhase || 0] || AVION[0];
    const det = _k >= AVION_DETALLE_PX;
    const ch = charActual();
    const tela = llevaBata() ? _bataTela : null;
    const dy = pose.bodyDY || 0;
    const [hiX, hiY] = AVION_HOMBRO_I, [hdX, hdY] = AVION_HOMBRO_D;
    const manos = [];
    let papel = null;
    if (f.papel) {
      const [x, y0, a, etapa] = f.papel;
      const y = y0 + (f.sigue ? bocaTaza(ch) - 10.7 : 0);
      const ca = Math.cos(a), sa = Math.sin(a);
      const k = etapa === 3 ? AVION_K : HOJA_K;
      const en = (lx, ly) => [x + (lx * ca - ly * sa) * k, y + (lx * sa + ly * ca) * k];
      papel = { x, y, a, k, etapa, en };
      if (f.dos) { manos.push([-1, en(-1.55, 0.5)], [1, en(1.55, 0.5)]); }
      if (f.der) manos.push([1, en(AVION_AGARRE[0], AVION_AGARRE[1])]);
      if (f.cola) manos.push([-1, en(AVION_COLA[0], AVION_COLA[1])]);
    }
    if (f.pellizco) manos.push([1, [f.pellizco[0], f.pellizco[1] + (f.sigue ? bocaTaza(ch) - 10.7 : 0)]]);
    else if (f.cola) manos.push([1, papel.en(0.2, 0.75)]);
    if (f.mano) manos.push([1, f.mano]);
    // La mano de visera, sobre los ojos del personaje y no sobre los de
    // Boberto: a quien los tiene bajos le taparía la frente y no la vista.
    let visera = null;
    if (f.visera) {
      const ojo = ch && ch.ojos ? ch.ojos.y : alturaOjos({});
      visera = [BODY.cx + 2.6, ojo - 2.7 + dy];
    }

    manos.forEach(([lado, [hx, hy]]) => {
      const [sx, sy] = lado < 0 ? [hiX, hiY] : [hdX, hdY];
      brazoGuitarra(c, P, tela, ch, lado, sx, sy + dy, hx, hy);
    });
    if (visera) {
      // Aplanada y apenas inclinada: una mano redonda sobre el ojo se leía
      // como rascarse; plana es la visera de quien mira lejos.
      const [vx, vy] = visera;
      brazoGuitarra(c, P, tela, ch, 1, hdX, hdY + dy, vx + 0.6, vy + 0.2);
      c.save();
      c.translate(vx, vy); c.rotate(-0.12);
      ellipsePath(c, 0, 0, 1.25, 0.5);
      const hg = c.createLinearGradient(0, -0.5, 0, 0.5);
      hg.addColorStop(0, P.handA); hg.addColorStop(1, P.handB);
      c.fillStyle = hg; c.fill();
      c.lineWidth = 0.16; c.strokeStyle = withA(P.edge, 0.7); c.stroke();
      c.restore();
    }
    if (papel) {
      c.save();
      c.translate(papel.x, papel.y); c.rotate(papel.a); c.scale(papel.k, papel.k);
      pintarPapelAvion(c, papel.etapa, !!f.nata, coloresTema().serie[0], det);
      c.restore();
    }
    manos.forEach(([, [hx, hy]]) => manoGuitarra(c, P, hx, hy));
  }

  /**
   * El cubo Rubik: tres caras a la vista, sus 3x3 y las dos manos.
   *
   * - Es una proyección 3D de verdad (27 cubitos, ortográfica, en vista de
   *   tres cuartos), no un dibujo plano: lo que dice "Rubik" a media jugada es
   *   la capa girada con el plástico negro asomando en el corte, y eso sale
   *   solo de la geometría en lugar de inventar cada ángulo a mano.
   * - Colores clásicos fijos (blanco arriba, verde al frente, rojo a la
   *   derecha) con contorno oscuro: son los que hacen reconocible el objeto
   *   en cualquier tema. Los centros nunca se mueven, como en el cubo real.
   * - Las mezclas son tablas fijas, no sorteos: el mismo cuadro se ve igual
   *   cada vez que se pinta, y la vitrina y las hojas de verificación coinciden.
   * - Los cubitos se pintan de atrás para adelante; la capa que gira va
   *   siempre después del bloque quieto porque está más cerca de la cámara
   *   (arriba o a la derecha) y nunca queda tapada por él.
   *
   * La ranura entre stickers y el brillo se apagan por debajo de
   * RUBIK_DETALLE_PX: a escala de topbar el sticker mide un píxel y medio, y
   * la ranura sólo ensuciaría el color. Quedan las manchas de color, que con
   * el contorno y las tres caras igual se leen como el cubo.
   */
  const RUBIK_COL = {
    contorno: '#0c0d10', plastico: '#1b1c21',
    w: '#f4f4ee', y: '#ffd31f', r: '#d9262c', o: '#ff7b1a', b: '#1f5fd9', g: '#17a34a',
  };
  const RUBIK_DETALLE_PX = 5;
  // Unidades del motor por cubito: el cubo entero mide unas 4 de ancho.
  const RUBIK_K = 0.95;
  const RUBIK_HOMBRO_I = [2.9, 10.4], RUBIK_HOMBRO_D = [13.1, 10.4];
  // Vista fija: girado hacia la izquierda (se ven frente y lado derecho) y
  // mirado desde arriba (se ve la cara de arriba).
  const RUBIK_VISTA = (() => {
    const yaw = -0.62, pitch = 0.45;
    return { cy: Math.cos(yaw), sy: Math.sin(yaw), cp: Math.cos(pitch), sp: Math.sin(pitch) };
  })();
  // La luz viene de arriba a la izquierda: la cara de arriba queda clara y la
  // derecha en sombra, que es lo que separa las tres caras sin dibujar aristas.
  const RUBIK_LUZ = (() => {
    const v = [-0.35, 0.85, 0.4], n = Math.hypot(v[0], v[1], v[2]);
    return v.map(x => x / n);
  })();

  /*
   * Estados del cubo. Cada cara es una cadena de 9 colores por filas, vista de
   * frente: U (arriba, la fila 0 es la del fondo), F (frente), R (derecha, la
   * columna 0 es la que toca al frente), L y B. L y B sólo asoman cuando gira
   * una capa, así que comparten una mezcla para todos los desarmados. `casi`
   * es el resuelto con la capa de arriba corrida un cuarto: el último giro.
   */
  const RUBIK_L = 'oybgowryb', RUBIK_B = 'ybrwbgoyb';
  const RUBIK_EST = {
    A: { U: 'ywobwrgwb', F: 'rgoygwbgy', R: 'wbgorygrw', L: RUBIK_L, B: RUBIK_B },
    B: { U: 'bwyowgrwo', F: 'grwbgyogr', R: 'ygbrrwbro', L: RUBIK_L, B: RUBIK_B },
    C: { U: 'owrgwbywg', F: 'ybgrgowgb', R: 'rwyorbgry', L: RUBIK_L, B: RUBIK_B },
    D: { U: 'gwbywowrw', F: 'oggbgrygw', R: 'bryyrgwro', L: RUBIK_L, B: RUBIK_B },
    casi: { U: 'wwwwwwwww', F: 'rrrgggggg', R: 'bbbrrrrrr', L: 'gggoooooo', B: 'ooobbbbbb' },
    ok: { U: 'wwwwwwwww', F: 'ggggggggg', R: 'rrrrrrrrr', L: 'ooooooooo', B: 'bbbbbbbbb' },
  };

  /*
   * Cuadros clave (propPhase). `cubo`: [x, y, giro] del centro en el lienzo;
   * `sigue` le suma la altura de la boca, para no taparle la cara a quien la
   * tiene baja. `est`: el estado. `capa`: [eje, ángulo] de la capa a medio
   * girar ('y' la de arriba, 'x' la de la derecha). `manos`: dónde van las
   * dos manos en coordenadas del cubo proyectado (cubitos); `una`: lo levanta
   * sólo con la derecha y el brazo izquierdo vuelve a ser el del motor.
   */
  const RUBIK_MANOS = [[-2.05, 0.45], [2.05, 0.15]];
  const RUBIK_MANOS_Y = [[-2.05, 0.6], [1.75, -1.15]];
  const RUBIK_MANOS_X = [[-2.05, 0.45], [2.15, -0.45]];
  const RUBIK = [
    { dy: 0,    cubo: [10.4, 13.6, 0.25], sigue: 1, est: 'A' },                                  // 0 lo saca
    { dy: 0,    cubo: [8, 13.5, 0],      sigue: 1, est: 'A' },                                   // 1 lo mira
    { dy: 0.05, cubo: [8, 13.5, 0],      sigue: 1, est: 'A', capa: ['y', 0.55], manos: RUBIK_MANOS_Y },   // 2 gira
    { dy: 0,    cubo: [8, 13.5, 0.03],   sigue: 1, est: 'B' },                                   // 3
    { dy: 0.05, cubo: [8, 13.5, 0],      sigue: 1, est: 'B', capa: ['x', 0.6], manos: RUBIK_MANOS_X },    // 4 gira
    { dy: 0,    cubo: [8, 13.5, -0.03],  sigue: 1, est: 'C' },                                   // 5
    { dy: 0.05, cubo: [8, 13.5, 0],      sigue: 1, est: 'C', capa: ['y', -0.6], manos: RUBIK_MANOS_Y },   // 6 gira
    { dy: 0,    cubo: [8, 13.5, 0.03],   sigue: 1, est: 'D' },                                   // 7
    { dy: 0.1,  cubo: [8, 13.6, -0.07],  sigue: 1, est: 'D', capa: ['x', 0.78], manos: RUBIK_MANOS_X },   // 8 se traba
    { dy: 0.05, cubo: [8, 13.6, 0.04],   sigue: 1, est: 'D', capa: ['x', 0.72], manos: RUBIK_MANOS_X },   // 9 se traba (forcejeo)
    { dy: -0.05, cubo: [10.3, 12.1, -0.18], sigue: 1, est: 'D', capa: ['x', 0.78] },             // 10 lo mira de cerca
    { dy: 0,    cubo: [8, 13.6, 0],      sigue: 1, est: 'D', capa: ['x', 0.78] },                // 11 piensa
    { dy: 0,    cubo: [7.1, 13.3, -0.22], sigue: 1, est: 'D', capa: ['x', 0.78] },               // 12 lo sacude
    { dy: 0,    cubo: [8.9, 13.3, 0.22], sigue: 1, est: 'D', capa: ['x', 0.78] },                // 13 lo sacude
    { dy: 0.05, cubo: [8, 13.5, 0],      sigue: 1, est: 'casi', capa: ['y', 0.75], manos: RUBIK_MANOS_Y }, // 14 último giro
    { dy: 0,    cubo: [8, 13.5, 0],      sigue: 1, est: 'ok' },                                  // 15 ¡resuelto!
    { dy: -0.2, cubo: [13.6, 5.6, -0.12], est: 'ok', una: 1 },                                  // 16 lo muestra
    { dy: 0,    cubo: [8, 13.5, 0],      sigue: 1, est: 'ok' },                                  // 17 contento
  ];
  function poseRubik(i, cara) {
    const f = RUBIK[i];
    const pose = { prop: 'rubik', propPhase: i, armR: false, bodyDY: f.dy };
    if (!f.una) pose.armL = false;
    return Object.assign(pose, cara);
  }

  // Gira p alrededor del eje de la capa ('y' vertical, 'x' horizontal).
  function rubikGira(p, eje, a) {
    const ca = Math.cos(a), sa = Math.sin(a), [x, y, z] = p;
    if (eje === 'y') return [x * ca + z * sa, y, -x * sa + z * ca];
    return [x, y * ca - z * sa, y * sa + z * ca];
  }

  // Del espacio del cubo (cubitos, y para arriba) a la pantalla: [x, y, hondura].
  function rubikProy(p) {
    const V = RUBIK_VISTA, [x, y, z] = p;
    const x1 = x * V.cy + z * V.sy, z1 = -x * V.sy + z * V.cy;
    return [x1, -(y * V.cp - z1 * V.sp), y * V.sp + z1 * V.cp];
  }

  // El sticker que muestra la cara exterior de un cubito, o null si es interior.
  function rubikSticker(est, i, j, k, n) {
    const S = RUBIK_EST[est] || RUBIK_EST.A;
    if (n[2] === 1 && k === 1) return S.F[(1 - j) * 3 + i + 1];
    if (n[1] === 1 && j === 1) return S.U[(k + 1) * 3 + i + 1];
    if (n[0] === 1 && i === 1) return S.R[(1 - j) * 3 + 1 - k];
    if (n[0] === -1 && i === -1) return S.L[(1 - j) * 3 + k + 1];
    if (n[2] === -1 && k === -1) return S.B[(1 - j) * 3 + 1 - i];
    if (n[1] === -1 && j === -1) return 'y';
    return null;
  }

  // Envolvente convexa (cadena monótona) de puntos de pantalla.
  function rubikEnvolvente(pts) {
    const p = pts.slice().sort((a, b) => a[0] - b[0] || a[1] - b[1]);
    const cruz = (o, a, b) => (a[0] - o[0]) * (b[1] - o[1]) - (a[1] - o[1]) * (b[0] - o[0]);
    const lo = [], hi = [];
    for (const q of p) {
      while (lo.length >= 2 && cruz(lo[lo.length - 2], lo[lo.length - 1], q) <= 0) lo.pop();
      lo.push(q);
    }
    for (let i = p.length - 1; i >= 0; i--) {
      const q = p[i];
      while (hi.length >= 2 && cruz(hi[hi.length - 2], hi[hi.length - 1], q) <= 0) hi.pop();
      hi.push(q);
    }
    return lo.slice(0, -1).concat(hi.slice(0, -1));
  }

  const RUBIK_NORMALES = [[1, 0, 0], [-1, 0, 0], [0, 1, 0], [0, -1, 0], [0, 0, 1], [0, 0, -1]];

  // El cubo en sus coordenadas (cubitos, centrado en el origen).
  function pintarRubik(c, est, capa, det) {
    const C = RUBIK_COL;
    const enCapa = (i, j) => capa && (capa[0] === 'y' ? j === 1 : i === 1);
    const mueve = (p, i, j) => enCapa(i, j) ? rubikGira(p, capa[0], capa[1]) : p;
    const quieto = [], girando = [];
    for (let i = -1; i <= 1; i++) for (let j = -1; j <= 1; j++) for (let k = -1; k <= 1; k++) {
      const centro = rubikProy(mueve([i, j, k], i, j));
      (enCapa(i, j) ? girando : quieto).push({ i, j, k, hondo: centro[2] });
    }
    const inset = det ? 0.12 : 0.03;

    const grupo = cubitos => {
      cubitos.sort((a, b) => a.hondo - b.hondo);
      const esquinas = [];
      for (const q of cubitos) {
        for (const n of RUBIK_NORMALES) {
          const nv = mueve(n, q.i, q.j);
          if (rubikProy(nv)[2] <= 0.01) continue;
          // Dos ejes tangentes a la cara y sus cuatro esquinas.
          const ta = n[0] ? [0, 1, 0] : [1, 0, 0], tb = n[2] ? [0, 1, 0] : [0, 0, 1];
          const cara = [[-1, -1], [1, -1], [1, 1], [-1, 1]].map(([s, t]) => rubikProy(mueve([
            q.i + n[0] * 0.5 + (ta[0] * s + tb[0] * t) * 0.5,
            q.j + n[1] * 0.5 + (ta[1] * s + tb[1] * t) * 0.5,
            q.k + n[2] * 0.5 + (ta[2] * s + tb[2] * t) * 0.5,
          ], q.i, q.j)));
          const quad = (pts) => {
            c.beginPath(); c.moveTo(pts[0][0], pts[0][1]);
            for (let m = 1; m < 4; m++) c.lineTo(pts[m][0], pts[m][1]);
            c.closePath();
          };
          quad(cara);
          c.fillStyle = C.plastico; c.fill();
          const s = rubikSticker(est, q.i, q.j, q.k, n);
          const luz = Math.max(0, nv[0] * RUBIK_LUZ[0] + nv[1] * RUBIK_LUZ[1] + nv[2] * RUBIK_LUZ[2]);
          if (s) {
            const mx = (cara[0][0] + cara[2][0]) / 2, my = (cara[0][1] + cara[2][1]) / 2;
            const st = cara.map(([x, y]) => [x + (mx - x) * inset * 2, y + (my - y) * inset * 2]);
            quad(st);
            c.fillStyle = C[s]; c.fill();
            // La sombra según la cara: separa arriba, frente y lado sin aristas.
            c.fillStyle = withA('#000000', (1 - luz) * 0.38); c.fill();
            if (det) {
              // Un brillo en la esquina de cada sticker, del lado de la luz.
              const e = st[3], f2 = st[0], g2 = st[2];
              c.beginPath();
              c.moveTo(e[0] + (f2[0] - e[0]) * 0.2 + (g2[0] - e[0]) * 0.12, e[1] + (f2[1] - e[1]) * 0.2 + (g2[1] - e[1]) * 0.12);
              c.lineTo(e[0] + (f2[0] - e[0]) * 0.2 + (g2[0] - e[0]) * 0.42, e[1] + (f2[1] - e[1]) * 0.2 + (g2[1] - e[1]) * 0.42);
              c.lineCap = 'round';
              c.lineWidth = 0.09; c.strokeStyle = withA('#ffffff', 0.45 * luz + 0.1); c.stroke();
            }
          }
          for (const p of cara) esquinas.push(p);
        }
      }
      if (!esquinas.length) return;
      const env = rubikEnvolvente(esquinas);
      c.beginPath(); c.moveTo(env[0][0], env[0][1]);
      for (let m = 1; m < env.length; m++) c.lineTo(env[m][0], env[m][1]);
      c.closePath();
      c.lineJoin = 'round';
      c.lineWidth = 0.16; c.strokeStyle = C.contorno; c.stroke();
    };
    grupo(quieto);
    grupo(girando);
  }

  function propRubik(c, P, pose) {
    const f = RUBIK[pose.propPhase || 0] || RUBIK[0];
    const det = _k >= RUBIK_DETALLE_PX;
    const ch = charActual();
    const tela = llevaBata() ? _bataTela : null;
    const dy = pose.bodyDY || 0;
    const [x0, y0, a] = f.cubo;
    const x = x0, y = y0 + (f.sigue ? bocaTaza(ch) - 10.7 : 0);
    const ca = Math.cos(a), sa = Math.sin(a);
    const enCubo = ([lx, ly]) => [x + (lx * ca - ly * sa) * RUBIK_K, y + (lx * sa + ly * ca) * RUBIK_K];

    // Las manos: muñones detrás del cubo y la mano encima, como la guitarra.
    const manos = f.una
      ? [[1, RUBIK_HOMBRO_D, enCubo([0.4, 2.0])]]
      : (f.manos || RUBIK_MANOS).map((m, n) => [n ? 1 : -1, n ? RUBIK_HOMBRO_D : RUBIK_HOMBRO_I, enCubo(m)]);
    manos.forEach(([lado, h, [hx, hy]]) => brazoGuitarra(c, P, tela, ch, lado, h[0], h[1] + dy, hx, hy));
    c.save();
    c.translate(x, y); c.rotate(a); c.scale(RUBIK_K, RUBIK_K);
    pintarRubik(c, f.est, f.capa, det);
    c.restore();
    manos.forEach(([, , [hx, hy]]) => manoGuitarra(c, P, hx, hy));
  }

  /**
   * El celular de la selfie: el aparato, el brazo que lo sostiene y el flash.
   *
   * - Tres caras: `dorso` (lo que ve el público mientras él mira la pantalla:
   *   carcasa oscura con la cámara), `pantalla` (lo da vuelta para mostrar la
   *   foto) y nada más; un celular de canto a esta escala es una raya y no
   *   dice "celular".
   * - Carcasa y contorno con colores fijos, porque un celular es oscuro en
   *   todos los temas; la pantalla toma el acento del tema, así que la foto se
   *   repinta con la paleta como el termo o el yoyó.
   * - La foto es una mini cara del propio personaje (su color de cuerpo) con
   *   los ojos cerrados o abiertos: es el nudo y el remate del gesto. Se apaga
   *   por debajo de SELFIE_DETALLE_PX junto con la lente y el brillo; a escala
   *   de topbar la pantalla mide ocho píxeles y la historia la cuenta la cara
   *   de Boberto (y la gotita), no la foto.
   * - El celular levantado queda a la altura de la cabeza y al costado, nunca
   *   arriba: arriba choca con el pelo de Rick contra el borde del lienzo.
   * - El destello que encandila es del lienzo (un brillo en la lente) más una
   *   partícula `plt-pt-flash` en la capa de la mascota: local, nunca la
   *   página entera.
   * - Brazo por brazoGuitarra, con la mano encima del borde de abajo.
   */
  const SELFIE_COL = {
    contorno: '#0d0f14', carcasa: '#2b303b', borde: '#454c5a',
    lente: '#0a0b0f', aro: '#7b8494', ojoFoto: '#14161c',
  };
  const SELFIE_DETALLE_PX = 5;
  const SELFIE_W = 1.5, SELFIE_H = 2.5;
  // Escala del celular en la mano y cuando lo da vuelta para mostrar la foto:
  // mostrándolo lo acerca, y es el único cuadro donde importa la pantalla.
  const SELFIE_K = 1.3, SELFIE_K_FOTO = 1.9;
  const SELFIE_HOMBRO_I = [2.9, 10.4], SELFIE_HOMBRO_D = [13.1, 10.4];

  /*
   * Cuadros clave (propPhase). `tel`: [x, y, giro] del centro del celular;
   * `cara`: 'dorso' o 'pantalla'; `foto`: 'cerrados' | 'abiertos' en la
   * pantalla; `flash`: 1 destello pleno, 0.5 el suave; `pulgar`: la mano
   * izquierda arriba con el pulgar levantado. La mano derecha va siempre en
   * el borde de abajo del celular, así que el brazo sale de la tabla.
   */
  const SELFIE = [
    { dy: 0,     tel: [11.5, 12.3, 0.12],  cara: 'dorso' },                       // 0 lo saca
    { dy: -0.1,  tel: [14.6, 6.5, -0.12],  cara: 'dorso' },                       // 1 lo levanta
    { dy: -0.1,  tel: [14.3, 7.0, -0.28],  cara: 'dorso' },                       // 2 se acomoda
    { dy: -0.15, tel: [14.7, 6.2, -0.04],  cara: 'dorso' },                       // 3 se acomoda
    { dy: -0.2,  tel: [14.7, 6.2, -0.04],  cara: 'dorso', pulgar: 1 },            // 4 pone pose
    { dy: -0.2,  tel: [14.7, 6.2, -0.04],  cara: 'dorso', pulgar: 1, flash: 1 },  // 5 ¡flash!
    { dy: 0.05,  tel: [14.1, 7.9, -0.22],  cara: 'dorso' },                       // 6 encandilado
    { dy: 0.05,  tel: [11.4, 11.4, 0.08],  cara: 'dorso' },                       // 7 mira la foto
    { dy: 0.1,   tel: [12.5, 11.2, -0.06], cara: 'pantalla', foto: 'cerrados' },  // 8 salió con los ojos cerrados
    { dy: -0.1,  tel: [14.6, 6.5, -0.12],  cara: 'dorso' },                       // 9 la vuelve a sacar
    { dy: -0.2,  tel: [14.7, 6.2, -0.04],  cara: 'dorso', pulgar: 1 },            // 10 ojos bien abiertos
    { dy: -0.2,  tel: [14.7, 6.2, -0.04],  cara: 'dorso', pulgar: 1, flash: 0.5 },// 11 flash suave
    { dy: 0,     tel: [11.4, 11.4, 0.08],  cara: 'dorso' },                       // 12 mira la foto
    { dy: -0.1,  tel: [12.5, 11.0, -0.06], cara: 'pantalla', foto: 'abiertos' },  // 13 salió bien
  ];
  function poseSelfie(i, cara) {
    const f = SELFIE[i];
    const pose = { prop: 'selfie', propPhase: i, bodyDY: f.dy, armR: false };
    if (f.pulgar) pose.armL = false;
    return Object.assign(pose, cara);
  }

  // La mini cara de la foto, en coordenadas del celular: el color de cuerpo
  // del personaje en curso, para que la foto sea de él y no de Boberto.
  function fotoSelfie(c, P, foto) {
    const C = SELFIE_COL;
    ellipsePath(c, 0, 0.15, 0.5, 0.55);
    const g = c.createLinearGradient(0, -0.4, 0, 0.7);
    g.addColorStop(0, P.mid); g.addColorStop(1, P.deep);
    c.fillStyle = g; c.fill();
    c.lineWidth = 0.06; c.strokeStyle = C.contorno; c.stroke();
    c.lineCap = 'round';
    c.strokeStyle = C.ojoFoto; c.fillStyle = C.ojoFoto;
    if (foto === 'cerrados') {
      // Dos rayitas caídas y la boca chueca: la foto arruinada.
      c.lineWidth = 0.09;
      for (const s of [-1, 1]) {
        c.beginPath(); c.moveTo(s * 0.32, 0.02); c.lineTo(s * 0.1, 0.08); c.stroke();
      }
      c.lineWidth = 0.07;
      c.beginPath(); c.moveTo(-0.14, 0.4); c.lineTo(0.14, 0.36); c.stroke();
    } else {
      for (const s of [-1, 1]) {
        ellipsePath(c, s * 0.2, 0.02, 0.1, 0.13);
        c.fillStyle = '#ffffff'; c.fill();
        ellipsePath(c, s * 0.2, 0.04, 0.055, 0.07);
        c.fillStyle = C.ojoFoto; c.fill();
      }
      c.lineWidth = 0.08;
      c.beginPath(); c.arc(0, 0.28, 0.17, 0.15 * Math.PI, 0.85 * Math.PI); c.stroke();
    }
  }

  function pintarCelular(c, P, f, det) {
    const C = SELFIE_COL, W = SELFIE_W, H = SELFIE_H;
    roundRect(c, -W / 2, -H / 2, W, H, 0.32);
    c.fillStyle = C.carcasa; c.fill();
    c.lineWidth = 0.15; c.strokeStyle = C.contorno; c.stroke();
    if (f.cara === 'pantalla') {
      const T = coloresTema();
      roundRect(c, -W / 2 + 0.15, -H / 2 + 0.2, W - 0.3, H - 0.4, 0.14);
      const g = c.createLinearGradient(0, -H / 2, 0, H / 2);
      g.addColorStop(0, T.serie[0]); g.addColorStop(1, T.fondo);
      c.fillStyle = g; c.fill();
      if (det) {
        // La isla de la cámara frontal, arriba al medio.
        roundRect(c, -0.2, -H / 2 + 0.28, 0.4, 0.12, 0.06);
        c.fillStyle = C.contorno; c.fill();
        c.save(); c.translate(0, -0.2); fotoSelfie(c, P, f.foto); c.restore();
      }
      return;
    }
    // El dorso: el canto un tono más claro y la cámara arriba a la izquierda.
    if (det) {
      roundRect(c, -W / 2 + 0.12, -H / 2 + 0.12, W - 0.24, H - 0.24, 0.24);
      c.lineWidth = 0.06; c.strokeStyle = C.borde; c.stroke();
    }
    roundRect(c, -W / 2 + 0.2, -H / 2 + 0.2, 0.66, 0.66, 0.18);
    c.fillStyle = C.contorno; c.fill();
    ellipsePath(c, -W / 2 + 0.53, -H / 2 + 0.53, 0.2, 0.2);
    c.fillStyle = C.lente; c.fill();
    if (det) { c.lineWidth = 0.06; c.strokeStyle = C.aro; c.stroke(); }
    if (f.flash) {
      // El destello nace en la lente: un núcleo blanco y cuatro rayos. Es lo
      // que se ve en el cuadro; la ráfaga que se expande es la partícula.
      const fx = -W / 2 + 0.53, fy = -H / 2 + 0.53, r = 1.5 * f.flash;
      const g = c.createRadialGradient(fx, fy, 0, fx, fy, r);
      g.addColorStop(0, 'rgba(255,255,255,1)');
      g.addColorStop(0.35, 'rgba(255,253,235,0.85)');
      g.addColorStop(1, 'rgba(255,250,220,0)');
      c.fillStyle = g;
      ellipsePath(c, fx, fy, r, r); c.fill();
      c.fillStyle = '#ffffff';
      c.beginPath();
      const L = 2.1 * f.flash, w = 0.13;
      c.moveTo(fx, fy - L); c.lineTo(fx + w, fy - w); c.lineTo(fx + L, fy); c.lineTo(fx + w, fy + w);
      c.lineTo(fx, fy + L); c.lineTo(fx - w, fy + w); c.lineTo(fx - L, fy); c.lineTo(fx - w, fy - w);
      c.closePath(); c.fill();
    } else if (det) {
      ellipsePath(c, -W / 2 + 0.47, -H / 2 + 0.47, 0.06, 0.06);
      c.fillStyle = 'rgba(255,255,255,0.55)'; c.fill();
    }
  }

  // El pulgar arriba: el puño redondo de siempre con un dedo parado encima.
  function pulgarSelfie(c, P, x, y) {
    c.save();
    c.translate(x + 0.12, y - 0.62); c.rotate(0.15);
    ellipsePath(c, 0, 0, 0.24, 0.42);
    const hg = c.createLinearGradient(0, -0.4, 0, 0.4);
    hg.addColorStop(0, P.handA); hg.addColorStop(1, P.handB);
    c.fillStyle = hg; c.fill();
    c.lineWidth = 0.14; c.strokeStyle = withA(P.edge, 0.7); c.stroke();
    c.restore();
    manoGuitarra(c, P, x, y);
  }

  function propSelfie(c, P, pose) {
    const f = SELFIE[pose.propPhase || 0] || SELFIE[0];
    const det = _k >= SELFIE_DETALLE_PX;
    const ch = charActual();
    const tela = llevaBata() ? _bataTela : null;
    const dy = pose.bodyDY || 0;
    const [x, y, a] = f.tel;
    const ca = Math.cos(a), sa = Math.sin(a);
    const k = f.cara === 'pantalla' ? SELFIE_K_FOTO : SELFIE_K;
    const en = (lx, ly) => [x + (lx * ca - ly * sa) * k, y + (lx * sa + ly * ca) * k];
    // La mano agarra el borde de abajo, corrida hacia afuera: tapa la carcasa
    // y no la cámara ni la foto.
    const mano = en(0.35, SELFIE_H / 2 - 0.1);
    brazoGuitarra(c, P, tela, ch, 1, SELFIE_HOMBRO_D[0], SELFIE_HOMBRO_D[1] + dy, mano[0], mano[1]);
    let pulgar = null;
    if (f.pulgar) {
      pulgar = [2.0, 8.4 + dy];
      brazoGuitarra(c, P, tela, ch, -1, SELFIE_HOMBRO_I[0], SELFIE_HOMBRO_I[1] + dy, pulgar[0], pulgar[1]);
    }
    c.save();
    c.translate(x, y); c.rotate(a); c.scale(k, k);
    c.lineJoin = 'round';
    pintarCelular(c, P, f, det);
    c.restore();
    manoGuitarra(c, P, mano[0], mano[1]);
    if (pulgar) pulgarSelfie(c, P, pulgar[0], pulgar[1]);
  }

  /**
   * El puntero de profesor, con el brazo que lo sostiene.
   *
   * El brazo lo dibuja el prop y no drawArms (las poses `teach*` apagan el
   * derecho): el muñón de drawArms levantado apunta hacia la cabeza, que es
   * justo para donde NO tiene que ir un puntero, y además tiene que quedar
   * DELANTE del cuerpo para que la mano agarre el palo.
   *
   * Si lleva la bata, el brazo va con su manga (la tela la dejó el accesorio
   * en `_bataTela` en este mismo cuadro) y la mano asoma del puño.
   *
   * propPhase: 0 señala arriba, 1 golpecito (la punta baja un poco), 2 bajado,
   * apoyado hacia el piso, para cuando mira al público. Las puntas quedan a
   * más de media unidad del borde del lienzo: el chequeo de alfa en los bordes
   * tiene que seguir dando cero.
   */
  const PUNTERO = [
    { brazo: [13.95, 8.55, 0.50],  punta: [16.30, -1.10] },
    { brazo: [13.95, 8.80, 0.40],  punta: [15.95, -0.30] },
    { brazo: [13.80, 10.50, -0.22], punta: [16.75, 17.30] },
  ];
  function propPuntero(c, P, pose) {
    const f = PUNTERO[pose.propPhase || 0] || PUNTERO[0];
    const bx = f.brazo[0], by = f.brazo[1] + (pose.bodyDY || 0), rot = f.brazo[2];
    const arriba = f.punta[1] < by;
    // La mano va en la punta libre del brazo: arriba si señala, abajo si cuelga.
    const s = arriba ? -1.26 : 1.26;
    const mx = bx - s * Math.sin(rot), my = by + s * Math.cos(rot);
    const T = llevaBata() ? _bataTela : null;
    const ch = charActual();

    // Brazo.
    if (T) {
      mangaBata(c, T, bx, by, rot, arriba ? -1 : 1);
    } else {
      c.save();
      c.translate(bx, by); c.rotate(rot);
      ellipsePath(c, 0, 0, 1.00, 1.30);
      const g = c.createLinearGradient(-1, -1.3, 1, 1.3);
      g.addColorStop(0, P.armA); g.addColorStop(1, P.armB);
      c.fillStyle = g; c.fill();
      // `puntero`: la mano la dibuja este prop, agarrando el palo.
      if (ch && ch.brazo) ch.brazo(c, P, { side: 1, up: arriba, puntero: true });
      c.restore();
    }

    // Palo: de un poco detrás de la mano hasta la punta. Primero un trazo
    // oscuro más ancho, que es lo que lo separa de un fondo claro.
    const ux = f.punta[0] - mx, uy = f.punta[1] - my;
    const L = Math.hypot(ux, uy) || 1;
    const ax = mx - (ux / L) * 0.8, ay = my - (uy / L) * 0.8;
    c.save();
    c.lineCap = 'round';
    c.beginPath(); c.moveTo(ax, ay); c.lineTo(f.punta[0], f.punta[1]);
    c.lineWidth = 0.62; c.strokeStyle = 'rgba(20,22,28,0.45)'; c.stroke();
    const wg = c.createLinearGradient(ax, ay, f.punta[0], f.punta[1]);
    wg.addColorStop(0, '#6e4b2c'); wg.addColorStop(1, '#b08257');
    c.lineWidth = 0.40; c.strokeStyle = wg; c.stroke();
    // La punta, de otro color: es lo que el ojo sigue hasta la pizarra.
    const px = f.punta[0] - (ux / L) * 0.9, py = f.punta[1] - (uy / L) * 0.9;
    c.beginPath(); c.moveTo(px, py); c.lineTo(f.punta[0], f.punta[1]);
    c.lineWidth = 0.46; c.strokeStyle = '#e0533d'; c.stroke();
    c.restore();

    // Mano, encima del palo: lo agarra.
    ellipsePath(c, mx, my, 0.62, 0.62);
    const hg = c.createLinearGradient(mx - 0.6, my - 0.6, mx + 0.6, my + 0.6);
    hg.addColorStop(0, P.handA); hg.addColorStop(1, P.handB);
    c.fillStyle = hg; c.fill();
    c.lineWidth = 0.16; c.strokeStyle = withA(P.edge, 0.7); c.stroke();
  }

  /*
   * Mano abierta, con la palma hacia quien mira: la del saludo.
   *
   * La mano de siempre (manoGuitarra) es un círculo: agarrando un mástil o un
   * asa se lee como un puño, pero en alto se lee como una pelota. Lo que dice
   * "hola" son los dedos separados. Se pinta como una calcomanía: primero el
   * contorno de todas las piezas y después el relleno encima, así palma, dedos
   * y pulgar comparten UN contorno en vez de quedar seis óvalos encimados.
   *
   * Por debajo de MANO_DEDOS_PX (píxeles de pantalla por unidad) cada dedo
   * mediría un píxel y la mano sería un borrón: queda un mitón (palma, bloque
   * de dedos y pulgar), que a escala de topbar se sigue leyendo como una mano
   * abierta. `giro`: inclinación de la mano, con los dedos hacia -y. `pulgar`:
   * de qué lado sale el pulgar (-1, hacia el cuerpo, para la mano de la
   * derecha de quien mira).
   */
  const MANO_DEDOS_PX = 6;
  // Tamaño de la mano abierta: un poco más grande que la de siempre. A la
  // misma escala, en alto y contra el pelo o la cola de un personaje, se leía
  // como un detalle del dibujo y no como una mano.
  const MANO_ABIERTA_K = 1.2;
  function manoAbierta(c, P, x, y, giro, pulgar) {
    const det = _k * MANO_ABIERTA_K >= MANO_DEDOS_PX;
    // [x, y, rx, ry, giro] de cada pieza, en coordenadas de la mano.
    const piezas = [[0, 0, 0.64, 0.58, 0]];
    if (det) {
      piezas.push([-0.43, -0.72, 0.16, 0.34, -0.2], [-0.15, -0.84, 0.16, 0.37, -0.07],
                  [0.15, -0.83, 0.16, 0.36, 0.07], [0.43, -0.69, 0.16, 0.32, 0.2]);
    } else {
      piezas.push([0, -0.6, 0.57, 0.44, 0]);
    }
    piezas.push([pulgar * 0.62, -0.1, 0.17, 0.33, pulgar * 0.85]);
    c.save();
    c.translate(x, y);
    c.rotate(giro);
    c.scale(MANO_ABIERTA_K, MANO_ABIERTA_K);
    c.beginPath();
    for (const [px, py, rx, ry, r] of piezas) {
      c.moveTo(px + rx * Math.cos(r), py + rx * Math.sin(r));
      c.ellipse(px, py, rx, ry, r, 0, Math.PI * 2);
    }
    // El contorno, más oscuro que el borde del cuerpo: es lo que la separa de
    // un pelo o una cola del mismo color (la cola de Pikachu queda detrás).
    c.lineWidth = 0.3; c.strokeStyle = withA(mixHex(P.edge, '#120c0a', 0.4), 0.9); c.stroke();
    const g = c.createLinearGradient(-0.7, -1.2, 0.7, 0.6);
    g.addColorStop(0, P.handA); g.addColorStop(1, P.handB);
    c.fillStyle = g; c.fill();
    if (det) {
      // La línea de la palma: sin ella, de frente, la mano es una silueta.
      c.beginPath();
      c.moveTo(-0.3, 0.1); c.quadraticCurveTo(0, 0.3, 0.3, 0.06);
      c.lineWidth = 0.08; c.strokeStyle = withA(P.edge, 0.4); c.stroke();
    }
    c.restore();
  }

  // Dónde termina el antebrazo de una mano abierta: la base de la palma, que
  // gira con la mano. El muñón llega hasta ahí y la palma se pinta encima.
  function munecaDe(x, y, giro) {
    const r = 0.45 * MANO_ABIERTA_K;
    return [x - r * Math.sin(giro), y + r * Math.cos(giro)];
  }

  /**
   * El saludo, con la mano abierta en alto.
   *
   * La versión anterior subía el muñón de drawArms DETRÁS del cuerpo: asomaba
   * apenas por el costado de la cabeza (sobre Pikachu, del mismo amarillo, no
   * se veía) y `waveDown` era igual a `base`, así que el saludo se leía como un
   * parpadeo del brazo. Ahora, con la receta de la guitarra:
   *
   * - Una sola mano, la de la derecha de quien mira, ABIERTA y por DELANTE del
   *   cuerpo, a la altura de la cabeza y por fuera de la silueta: se recorta
   *   contra el fondo y no contra un cuerpo del mismo color. El brazo va por
   *   el hook `brazo` del personaje (brazoGuitarra), con su manga o su guante.
   * - El otro brazo queda en reposo, pintado por el motor: saludar no
   *   necesita las dos manos.
   * - La muñeca es la que saluda: la mano se inclina hacia adentro y hacia
   *   afuera (`giro`) y se corre apenas, que es lo que hace un saludo de lejos.
   *
   * `waveUp` y `waveDown` conservan su nombre: el corto de la landing los
   * alterna cada 0,2 s, y ahora esa alternancia es la muñeca yendo y viniendo.
   */
  const SALUDO = [
    { mano: [15.0, 8.4], giro: 0.5, dy: 0.1 },     // 0 la levanta
    { mano: [15.4, 5.4], giro: -0.3, dy: -0.05 },  // 1 hacia adentro (waveUp)
    { mano: [16.0, 5.7], giro: 0.42, dy: 0 },      // 2 hacia afuera (waveDown)
    { mano: [15.7, 5.3], giro: 0.05, dy: -0.1 },   // 3 la palma de frente
    { mano: [14.8, 9.2], giro: 0.7, dy: 0.05 },    // 4 la baja
  ];
  function poseSaludo(i, cara) {
    return Object.assign({ prop: 'saludo', propPhase: i, armR: false, bodyDY: SALUDO[i].dy }, cara);
  }
  function propSaludo(c, P, pose) {
    const f = SALUDO[pose.propPhase || 0] || SALUDO[0];
    const dy = pose.bodyDY || 0;
    const T = llevaBata() ? _bataTela : null;
    const hx = f.mano[0], hy = f.mano[1] + dy;
    const [mx, my] = munecaDe(hx, hy, f.giro);
    brazoGuitarra(c, P, T, charActual(), 1, 13.1, 10.4 + dy, mx, my);
    manoAbierta(c, P, hx, hy, f.giro, -1);
  }

  // El puño: la mano de siempre con los nudillos marcados. En alto, un
  // círculo liso se lee como una pelota; la línea de los nudillos lo cierra.
  function puno(c, P, x, y) {
    manoGuitarra(c, P, x, y);
    if (_k < MANO_DEDOS_PX) return;
    c.beginPath();
    c.moveTo(x - 0.4, y - 0.1); c.quadraticCurveTo(x, y - 0.34, x + 0.4, y - 0.1);
    c.lineWidth = 0.09; c.strokeStyle = withA(P.edge, 0.6); c.stroke();
  }

  /**
   * El festejo: se agacha, salta con los dos brazos arriba, cae y tira el
   * puño.
   *
   * La versión anterior era la cara de `happy` quieta, aplastada por una
   * clase en px (casi invisible en una peana) y con chispas de texto que
   * volaban hasta 78 px, fuera del riel de Buenas prácticas. Ahora, con la
   * receta de la guitarra:
   *
   * - Tabla FESTEJO: `manos` son las dos manos [izq x, y, der x, y] y los
   *   brazos los pone el prop (festejar sí es de dos brazos); `puno` es sólo
   *   la derecha, y el otro brazo queda en reposo, pintado por el motor.
   * - El dibujo nunca sube más que -0.25 (con -0.4 el pelo de Rick tocaba el
   *   borde de arriba a escala 2): el salto grande lo pone `plt-salto`, que
   *   mueve el canvas entero.
   */
  const FESTEJO = [
    { dy: 0.4,   manos: [2.3, 13.6, 13.7, 13.6] }, // 0 se agacha, puños abajo
    { dy: -0.25, manos: [1.2, 4.6, 14.8, 4.6] },   // 1 salta con los dos brazos arriba
    { dy: 0.3,   manos: [0.9, 9.0, 15.1, 9.0] },   // 2 cae, brazos abiertos
    { dy: -0.1,  puno: [14.4, 4.9] },              // 3 puño arriba
    { dy: 0.15,  puno: [14.0, 8.9] },              // 4 lo baja de un tirón: "¡sí!"
    { dy: -0.1,  manos: [1.6, 5.4, 14.4, 5.4] },   // 5 remate, bajo la lluvia de estrellas
  ];
  function poseFestejo(i, cara) {
    const f = FESTEJO[i];
    return Object.assign({ prop: 'festejo', propPhase: i, armR: false, bodyDY: f.dy }, f.manos ? { armL: false } : null, cara);
  }
  function propFestejo(c, P, pose) {
    const f = FESTEJO[pose.propPhase || 0] || FESTEJO[0];
    const dy = pose.bodyDY || 0;
    const T = llevaBata() ? _bataTela : null;
    const ch = charActual();
    if (f.manos) {
      const m = f.manos;
      brazoGuitarra(c, P, T, ch, -1, 2.9, 10.4 + dy, m[0], m[1] + dy);
      brazoGuitarra(c, P, T, ch, 1, 13.1, 10.4 + dy, m[2], m[3] + dy);
      puno(c, P, m[0], m[1] + dy);
      puno(c, P, m[2], m[3] + dy);
      return;
    }
    brazoGuitarra(c, P, T, ch, 1, 13.1, 10.4 + dy, f.puno[0], f.puno[1] + dy);
    puno(c, P, f.puno[0], f.puno[1] + dy);
  }

  /**
   * Pensar: la mano en el mentón y, al final, el índice arriba ("¡ajá!").
   *
   * La versión anterior era sólo la cara achinada mirando arriba, así que
   * como pose fija (Buenas prácticas usa `think` dieciocho veces) no se
   * distinguía de un parpadeo. Ahora la mano derecha cruza por delante y
   * apoya el puño debajo de la boca, con el índice subiendo por la mejilla:
   * el gesto de "a ver…" que se lee a cualquier tamaño.
   *
   * - El mentón sale de la boca de cada personaje (bocaTaza, la misma que
   *   usa el café): los Dragon Ball en chibi tienen la cara más abajo, y una
   *   mano a la altura de Boberto les taparía la boca.
   * - El índice va primero y el puño encima, así el dedo nace del puño.
   * - `dedo`: hacia dónde apunta el índice, en radianes desde la vertical
   *   (negativo, hacia la boca). Sube casi derecho por la mejilla: inclinado
   *   hacia la cara apuntaba a la boca y se leía como "shh". `mano` en lugar
   *   del mentón: la mano en alto del "¡ajá!".
   */
  const PIENSA = [
    { menton: 1.35, dy: 0,     dedo: 0.06 },   // 0 la mano en el mentón (think)
    { menton: 1.4,  dy: 0,     dedo: -0.1 },   // 1 el índice se arrima a la boca
    { menton: 1.3,  dy: 0.05,  dedo: 0.16 },   // 2 golpecito en la mejilla
    { mano: [14.4, 6.6], dy: -0.15, dedo: 0.08 }, // 3 ¡ajá!, el índice arriba
  ];
  function posePiensa(i, cara) {
    return Object.assign({ prop: 'piensa', propPhase: i, armR: false, bodyDY: PIENSA[i].dy }, cara);
  }
  function dedoIndice(c, P, x, y, a) {
    c.save();
    c.translate(x, y);
    c.rotate(a);
    // Largo: tiene que asomar bien por encima del puño, que tapa su base.
    roundRect(c, -0.19, -1.72, 0.38, 1.45, 0.19);
    c.lineWidth = 0.24; c.strokeStyle = withA(P.edge, 0.8); c.stroke();
    const g = c.createLinearGradient(-0.2, -1.7, 0.2, 0);
    g.addColorStop(0, P.handA); g.addColorStop(1, P.handB);
    c.fillStyle = g; c.fill();
    c.restore();
  }
  function propPiensa(c, P, pose) {
    const f = PIENSA[pose.propPhase || 0] || PIENSA[0];
    const dy = pose.bodyDY || 0;
    const T = llevaBata() ? _bataTela : null;
    const ch = charActual();
    const hx = f.mano ? f.mano[0] : BODY.cx + f.menton;
    const hy = (f.mano ? f.mano[1] : bocaTaza(ch) + 1.3) + dy;
    brazoGuitarra(c, P, T, ch, 1, 13.1, 10.4 + dy, hx, hy);
    dedoIndice(c, P, hx, hy, f.dedo);
    manoGuitarra(c, P, hx, hy);
  }

  /**
   * El baile: un pasito de disco con UNA mano.
   *
   * La versión anterior era la cara de `happy` mirando a un lado y al otro,
   * con los muñones de reposo y un vaivén en px (casi invisible en una
   * peana). Ahora, con la receta de la guitarra:
   *
   * - La mano derecha hace los pasos: el índice arriba en diagonal ("Fiebre
   *   del sábado por la noche"), el índice abajo cruzando el cuerpo y el puño
   *   que bombea. El brazo va por el hook `brazo` del personaje
   *   (brazoGuitarra), con su manga o su guante; el otro queda en reposo,
   *   pintado por el motor.
   * - Los pies se turnan: en cada paso uno se levanta (`pie`, -1 el de la
   *   izquierda de quien mira, 1 el otro), y el cuerpo baja en el golpe y
   *   sube en el contratiempo.
   * - La mirada sigue a la mano: arriba cuando señala arriba, abajo cuando
   *   señala abajo.
   *
   * Cuadros clave (propPhase). `mano`: el puño en el lienzo; `dedo`: hacia
   * dónde señala el índice, en radianes desde la vertical (sin `dedo`, el
   * puño cerrado). El cuerpo nunca sube más que -0.25: el balanceo grande lo
   * pone `plt-baila`, que mueve el canvas entero.
   */
  const BAILE = [
    { mano: [14.4, 12.6], dy: 0.15, pie: 0 },               // 0 agarra el ritmo, la mano en la cadera
    { mano: [14.5, 12.3], dy: -0.05, pie: 0 },              // 1 cabeceo
    { mano: [15.5, 4.5], dedo: 0.45, dy: -0.15, pie: -1 },  // 2 el índice arriba (danceR)
    { mano: [10.7, 13.6], dedo: -2.45, dy: 0.25, pie: 1 },  // 3 el índice abajo, cruzado (danceL)
    { mano: [14.9, 6.7], dy: -0.1, pie: 1 },                // 4 bombea: el puño arriba
    { mano: [14.4, 10.3], dy: 0.2, pie: -1 },               // 5 bombea: el puño abajo
    { mano: [15.1, 3.2], dedo: 0.2, dy: -0.25, pie: -1 },   // 6 remate: la pose final, bien arriba
  ];
  function poseBaile(i, cara) {
    const f = BAILE[i];
    const y = BODY.bot + f.dy + 0.55;
    // Los pies van fijados (`pies`) para que uno se levante del todo y el
    // otro quede plantado; las suelas y la tinta de cada personaje los siguen.
    const pies = [[BODY.cx - 2.05, y - (f.pie === -1 ? 0.6 : 0)], [BODY.cx + 2.05, y - (f.pie === 1 ? 0.6 : 0)]];
    return Object.assign({ prop: 'baile', propPhase: i, armR: false, bodyDY: f.dy, pies }, cara);
  }
  function propBaile(c, P, pose) {
    const f = BAILE[pose.propPhase || 0] || BAILE[0];
    const dy = pose.bodyDY || 0;
    const T = llevaBata() ? _bataTela : null;
    const hx = f.mano[0], hy = f.mano[1] + dy;
    brazoGuitarra(c, P, T, charActual(), 1, 13.1, 10.4 + dy, hx, hy);
    if (f.dedo != null) { dedoIndice(c, P, hx, hy, f.dedo); manoGuitarra(c, P, hx, hy); }
    else puno(c, P, hx, hy);
  }

  /**
   * El estornudo: "ah… ah… ¡ah-CHÍS!", en el codo, como corresponde.
   *
   * La versión anterior eran dos cuadros (la cara achinada estirada y la de
   * ojos cerrados con la boca abierta) con un culatazo en px y cuatro
   * soplidos de texto que salían hasta 27 px por la izquierda del riel de
   * Buenas prácticas. Ahora, con la receta de la guitarra:
   *
   * - Tres inspiraciones que van en aumento: la cabeza atrás, los ojos que
   *   se achinan, el cuerpo que se estira y se angosta (`rx`), y el brazo
   *   que empieza a subir en la última.
   * - El estornudo va al CODO: el brazo derecho cruza delante de la boca. Los
   *   tramos hasta el codo y el medio del antebrazo van sin el hook del
   *   personaje (pondría un puño o una muñequera a mitad de camino) y el
   *   último, con el hook (brazoGuitarra), así que lleva su manga o su
   *   guante; con la bata de Rick, todos los tramos son de tela.
   * - Después se frota la nariz con el puño, de un lado al otro, y sonríe
   *   medio avergonzado.
   *
   * Cuadros clave (propPhase). `mano`: el puño, con la `y` relativa a la
   * boca del personaje (bocaTaza): los Dragon Ball en chibi tienen la cara
   * más abajo. `codo`: el brazo cruzado delante de la boca. Sin `mano`, el
   * brazo queda en reposo, pintado por el motor. El cuerpo nunca sube más
   * que -0.3: el estirón grande lo pone `plt-inhala`, que mueve el canvas.
   */
  const ESTORNUDO = [
    { dy: -0.1, rx: 0.98 },                              // 0 ah…
    { dy: -0.2, rx: 0.96 },                              // 1 ah… (sneezeUp)
    { dy: -0.3, rx: 0.95, mano: [11.7, -2.3] },          // 2 ¡ah…! el brazo empieza a subir
    { dy: 0.3,  rx: 1.06, codo: true, mano: [5.7, -0.1] }, // 3 ¡CHÍS!, en el codo (sneezeOut)
    { dy: 0.12, rx: 1.02, codo: true, mano: [5.9, -0.25] }, // 4 se queda un instante en el codo
    { dy: 0,    mano: [9.0, -1.05] },                    // 5 se frota la nariz
    { dy: 0.05, mano: [7.2, -1.15] },                    // 6 hacia el otro lado
  ];
  function poseEstornudo(i, cara) {
    const f = ESTORNUDO[i];
    return Object.assign({ prop: 'estornudo', propPhase: i, bodyDY: f.dy, bodyRX: f.rx || 1 },
      f.mano ? { armR: false } : null, cara);
  }
  // El tramo del brazo que va del hombro al codo: el mismo muñón, sin hook.
  function brazoTramo(c, P, T, x, y, rot) {
    if (T) { mangaBata(c, T, x, y, rot, 0); return; }
    c.save();
    c.translate(x, y); c.rotate(rot);
    ellipsePath(c, 0, 0, 1.0, 1.3);
    const g = c.createLinearGradient(-1, -1.3, 1, 1.3);
    g.addColorStop(0, P.armA); g.addColorStop(1, P.armB);
    c.fillStyle = g; c.fill();
    c.lineWidth = 0.16; c.strokeStyle = withA(P.edge, 0.7); c.stroke();
    c.restore();
  }
  function propEstornudo(c, P, pose) {
    const f = ESTORNUDO[pose.propPhase || 0] || ESTORNUDO[0];
    if (!f.mano) return;
    const dy = pose.bodyDY || 0;
    const ch = charActual();
    const T = llevaBata() ? _bataTela : null;
    const hx = f.mano[0], hy = bocaTaza(ch) + f.mano[1] + dy;
    const hombro = [13.1, 10.4 + dy];
    if (f.codo) {
      // El codo queda delante de la mejilla derecha; desde ahí el antebrazo
      // cruza la boca hasta el puño. Un muñón mide 2,6 u, así que el brazo
      // va en tres: hombro-codo, el primer tramo del antebrazo y el último,
      // con el hook, que llega al puño.
      const ex = 10.4, ey = hy + 0.35;
      const giro = (ax, ay, bx, by) => Math.atan2(-(bx - ax), by - ay);
      brazoTramo(c, P, T, (hombro[0] + ex) / 2, (hombro[1] + ey) / 2, giro(hombro[0], hombro[1], ex, ey));
      const L = Math.hypot(hx - ex, hy - ey) || 1;
      brazoTramo(c, P, T, ex + (hx - ex) / L * 1.3, ey + (hy - ey) / L * 1.3, giro(ex, ey, hx, hy));
      brazoGuitarra(c, P, T, ch, 1, ex, ey, hx, hy);
    } else {
      brazoGuitarra(c, P, T, ch, 1, hombro[0], hombro[1], hx, hy);
    }
    puno(c, P, hx, hy);
  }

  /**
   * La siesta: bosteza, cabecea y se duerme parado contra una almohada.
   *
   * La versión anterior era la cara con los ojos cerrados y una burbuja de
   * moco fija, con una respiración en px y tres "z" de texto que volaban
   * hasta 38 px, fuera del riel de Buenas prácticas. Ahora, con la receta de
   * la guitarra:
   *
   * - Bosteza tapándose la boca con la mano (por el hook `brazo` del
   *   personaje, con su manga o su guante) y cabecea.
   * - Se duerme con la cabeza apoyada en una almohada que sostiene con la
   *   misma mano: colores FIJOS con contorno oscuro (funda clara, costura y
   *   sombra), así se lee sobre cualquier cuerpo y cualquier tema. El
   *   ladeo hacia la almohada lo pone `plt-siesta`, que mueve el canvas.
   * - La burbuja de moco crece y se achica con la respiración y, en el
   *   remate, revienta: eso es lo que lo despierta.
   *
   * Cuadros clave (propPhase). `mano`: la mano en la boca, con la `y`
   * relativa a la boca del personaje (bocaTaza); `almohada`: la almohada en
   * la mano; `moco`: el radio de la burbuja; `pop`: la burbuja reventada.
   * Sin `mano` ni `almohada`, el brazo queda en reposo, pintado por el motor.
   */
  const SIESTA_COL = {
    contorno: '#1d2433',
    // Celeste y no blanca: blanca se leía como un papel o un pañuelo.
    funda: '#bcd3ff', fundaLuz: '#e8f0ff', fundaSombra: '#7f9fd6', costura: '#5a78b8', estrella: '#fff3b0',
    moco: '#cfe8ff', mocoBorde: '#5d7fa6',
  };
  const SIESTA = [
    { dy: -0.15, mano: [9.3, -0.1] },                    // 0 bosteza, la mano en la boca
    { dy: 0.2 },                                         // 1 cabecea
    { dy: 0.35, almohada: true, moco: 0.5 },             // 2 dormido (sleep)
    { dy: 0.4,  almohada: true, moco: 1.05 },            // 3 ronca: la burbuja crece
    { dy: 0.3,  almohada: true, pop: true },             // 4 la burbuja revienta
  ];
  // La almohada: centro, giro y dónde la toma la mano. De pie contra la
  // mejilla derecha, casi vertical.
  const ALMOHADA = { x: 14.1, y: 8.1, giro: 0.6, mano: [15.8, 10.9], hombro: [13.0, 12.4] };
  function poseSiesta(i, cara) {
    const f = SIESTA[i];
    return Object.assign({ prop: 'siesta', propPhase: i, bodyDY: f.dy },
      (f.mano || f.almohada) ? { armR: false } : null, cara);
  }

  // La funda en coordenadas propias: esquinas en punta y lados inflados.
  function pintarAlmohada(c, det) {
    const C = SIESTA_COL, w = 2.6, h = 1.65;
    c.beginPath();
    c.moveTo(-w, -h);
    c.quadraticCurveTo(0, -h - 0.6, w, -h);
    c.quadraticCurveTo(w + 0.5, 0, w, h);
    c.quadraticCurveTo(0, h + 0.6, -w, h);
    c.quadraticCurveTo(-w - 0.5, 0, -w, -h);
    c.closePath();
    // Inflada: la luz en el centro y la sombra contra los bordes.
    const g = c.createRadialGradient(-0.3, -0.3, 0.2, 0, 0, w + 0.4);
    g.addColorStop(0, C.fundaLuz); g.addColorStop(0.5, C.funda); g.addColorStop(1, C.fundaSombra);
    c.fillStyle = g; c.fill();
    c.lineWidth = 0.22; c.lineJoin = 'round'; c.strokeStyle = C.contorno; c.stroke();
    if (det) {
      // La costura y el pliegue del medio.
      c.save();
      c.setLineDash([0.22, 0.18]);
      c.beginPath();
      c.moveTo(-w + 0.4, -h + 0.25); c.quadraticCurveTo(0, -h - 0.15, w - 0.4, -h + 0.25);
      c.lineWidth = 0.07; c.strokeStyle = C.costura; c.stroke();
      c.restore();
      c.beginPath();
      c.moveTo(-0.9, 0.25); c.quadraticCurveTo(0, 0.65, 0.95, 0.2);
      c.lineWidth = 0.09; c.strokeStyle = withA(C.costura, 0.7); c.stroke();
      // Unas estrellitas estampadas: dicen "para dormir" de un vistazo.
      [[-1.4, -0.55], [1.2, -0.7], [-0.2, 0.95], [1.6, 0.75]].forEach(([x, y]) => {
        c.beginPath();
        for (let i = 0; i < 10; i++) {
          const a = -Math.PI / 2 + i * Math.PI / 5, r = i % 2 ? 0.1 : 0.24;
          c.lineTo(x + Math.cos(a) * r, y + Math.sin(a) * r);
        }
        c.closePath();
        c.fillStyle = C.estrella; c.fill();
      });
    }
  }

  // La burbuja de moco, saliendo de debajo del ojo derecho hacia afuera.
  function pintarMoco(c, x, y, r, pop) {
    const C = SIESTA_COL;
    if (pop) {
      c.lineCap = 'round';
      for (let i = 0; i < 5; i++) {
        const a = (i / 5) * Math.PI * 2 + 0.4;
        c.beginPath();
        c.arc(x, y, 1.1, a, a + 0.6);
        c.lineWidth = 0.22; c.strokeStyle = C.mocoBorde; c.stroke();
      }
      return;
    }
    ellipsePath(c, x, y, r, r);
    const g = c.createRadialGradient(x - r * 0.35, y - r * 0.4, r * 0.05, x, y, r);
    g.addColorStop(0, withA('#ffffff', 0.7));
    g.addColorStop(1, withA(C.moco, 0.4));
    c.fillStyle = g; c.fill();
    c.lineWidth = 0.12; c.strokeStyle = withA(C.mocoBorde, 0.75); c.stroke();
    ellipsePath(c, x - r * 0.35, y - r * 0.38, r * 0.2, r * 0.14);
    c.fillStyle = withA('#ffffff', 0.9); c.fill();
  }

  function propSiesta(c, P, pose) {
    const f = SIESTA[pose.propPhase || 0] || SIESTA[0];
    const dy = pose.bodyDY || 0;
    const ch = charActual();
    const T = llevaBata() ? _bataTela : null;
    const boca = bocaTaza(ch);
    if (f.mano) {
      const hx = f.mano[0], hy = boca + f.mano[1] + dy;
      brazoGuitarra(c, P, T, ch, 1, 13.1, 10.4 + dy, hx, hy);
      puno(c, P, hx, hy);
    }
    if (f.almohada) {
      const A = ALMOHADA;
      const hx = A.mano[0], hy = A.mano[1] + dy;
      brazoGuitarra(c, P, T, ch, 1, A.hombro[0], A.hombro[1] + dy, hx, hy);
      c.save();
      c.translate(A.x, A.y + dy);
      c.rotate(A.giro);
      pintarAlmohada(c, _k >= 5);
      c.restore();
      manoGuitarra(c, P, hx, hy);
    }
    // La burbuja sale de la nariz, entre los ojos y la boca.
    if (f.moco || f.pop) {
      const r = f.moco || 0;
      pintarMoco(c, BODY.cx + 1.0 + r * 0.75, boca - 1.0 + dy + r * 0.2, r, f.pop);
    }
  }

  /**
   * El mareo: ojos en espiral, la mano en la cabeza y estrellas dando
   * vueltas alrededor.
   *
   * La versión anterior eran los ojos en espiral quietos con cinco "★" de
   * texto que salían disparadas hasta 26 px (fuera del riel de Buenas
   * prácticas) y un tambaleo en px. Ahora, con la receta de la guitarra:
   *
   * - Las estrellas las dibuja el prop, en una órbita alrededor de la
   *   cabeza: la mitad de atrás va DETRÁS del cuerpo (PROPS_BEHIND) y la de
   *   adelante, delante (PROP_DELANTE), así que pasan por detrás de la
   *   cabeza y vuelven por delante de la frente. Colores fijos con contorno
   *   oscuro, más chicas y más tenues cuando van por atrás.
   * - La mano derecha en la sien (por el hook `brazo` del personaje, con su
   *   manga o su guante), por delante de todo.
   * - Las espirales de los ojos giran de un cuadro al otro (`giroEspiral`).
   *
   * Cuadros clave (propPhase). `giro`: dónde va la órbita, en radianes;
   * `k`: su tamaño (crece al entrar). Sin `mano`, el brazo queda en reposo.
   */
  const MAREO_COL = { estrella: '#ffd23f', estrellaLuz: '#fff3b0', contorno: '#4a3300', estela: '#fff3b0' };
  // La órbita: centro (x, y relativo al tope del cuerpo) y radios.
  const ORBITA = { x: 8, y: 1.4, rx: 5.1, ry: 1.4, n: 3 };
  const MAREO = [
    { giro: 0,   k: 0.6, dy: 0.15 },             // 0 se marea: aparecen las estrellas
    { giro: 0.8, k: 1, dy: 0, mano: true },       // 1 la mano en la sien (dizzy)
    { giro: 1.6, k: 1, dy: 0.05, mano: true },    // 2
    { giro: 2.4, k: 1, dy: 0.1, mano: true },     // 3
    { giro: 3.2, k: 1, dy: 0.05, mano: true },    // 4
  ];
  function poseMareo(i, cara) {
    const f = MAREO[i];
    return Object.assign({ prop: 'mareo', propPhase: i, bodyDY: f.dy, giroEspiral: i * 1.1 },
      f.mano ? { armR: false } : null, cara);
  }

  function estrellaMareo(c, x, y, r, a, det) {
    const C = MAREO_COL;
    c.beginPath();
    for (let i = 0; i < 10; i++) {
      const t = a - Math.PI / 2 + i * Math.PI / 5, rr = i % 2 ? r * 0.45 : r;
      c.lineTo(x + Math.cos(t) * rr, y + Math.sin(t) * rr);
    }
    c.closePath();
    const g = c.createLinearGradient(x - r, y - r, x + r, y + r);
    g.addColorStop(0, C.estrellaLuz); g.addColorStop(0.55, C.estrella);
    c.fillStyle = g; c.fill();
    c.lineWidth = det ? 0.12 : 0.16; c.lineJoin = 'round'; c.strokeStyle = C.contorno; c.stroke();
  }

  // `delante`: la mitad de la órbita que pasa por delante de la cabeza.
  function orbitaMareo(c, pose, delante) {
    const f = MAREO[pose.propPhase || 0] || MAREO[0];
    const O = ORBITA, k = f.k;
    const cx = O.x, cy = BODY.top + O.y + (pose.bodyDY || 0);
    const rx = O.rx * k, ry = O.ry * k;
    const det = _k >= 5;
    // La estela de la órbita: media elipse punteada, la de su lado.
    c.save();
    c.setLineDash([0.35, 0.3]);
    c.beginPath();
    c.ellipse(cx, cy, rx, ry, 0, delante ? 0 : Math.PI, delante ? Math.PI : Math.PI * 2);
    c.lineWidth = 0.1; c.strokeStyle = withA(MAREO_COL.estela, delante ? 0.55 : 0.3); c.stroke();
    c.restore();
    for (let i = 0; i < O.n; i++) {
      const a = f.giro + i * Math.PI * 2 / O.n;
      const sa = Math.sin(a);
      if ((sa >= 0) !== delante) continue;
      // Más grande y más opaca adelante: es lo que da la profundidad.
      const r = (0.92 + 0.25 * sa) * k;
      c.save();
      c.globalAlpha = delante ? 1 : 0.7;
      estrellaMareo(c, cx + Math.cos(a) * rx, cy + sa * ry, r, a * 0.7, det);
      c.restore();
    }
  }
  function propMareoAtras(c, P, pose) { orbitaMareo(c, pose, false); }
  function propMareoDelante(c, P, pose) {
    const f = MAREO[pose.propPhase || 0] || MAREO[0];
    if (f.mano) {
      const dy = pose.bodyDY || 0;
      const T = llevaBata() ? _bataTela : null;
      const hx = 13.5, hy = 5.9 + dy;
      brazoGuitarra(c, P, T, charActual(), 1, 13.1, 10.6 + dy, hx, hy);
      manoGuitarra(c, P, hx, hy);
    }
    orbitaMareo(c, pose, true);
  }

  /**
   * Enamorado: ojos de corazón, la mano en el pecho y el corazón que late.
   *
   * La versión anterior eran los ojos de corazón con el cuerpo entero
   * latiendo al 113 % y cinco corazones de texto (uno era un emoji de color)
   * que volaban hasta 38 px, fuera del riel de Buenas prácticas. Ahora, con
   * la receta de la guitarra:
   *
   * - Un corazón dibujado que asoma del pecho y late (`latido`: su tamaño en
   *   cada cuadro). Colores FIJOS con contorno oscuro y un brillo: se lee
   *   sobre Charmander o Freezer igual que sobre Boberto.
   * - La mano derecha en el pecho, al lado del corazón (por el hook `brazo`
   *   del personaje, con su manga o su guante); el otro brazo queda en
   *   reposo, pintado por el motor.
   *
   * Cuadros clave (propPhase). Sin `mano`, el brazo queda en reposo; con
   * `latido` en 0 todavía no asoma el corazón.
   */
  const AMOR_COL = { corazon: '#e8394d', corazonLuz: '#ff8a99', corazonSombra: '#a3162b', contorno: '#3a0a12' };
  const AMOR = [
    { dy: -0.1, latido: 0 },                  // 0 se enamora: los ojos se vuelven corazones
    { dy: 0,    latido: 1.25, mano: true },   // 1 la mano en el pecho (love)
    { dy: -0.05, latido: 1.65, mano: true },  // 2 el corazón late
    { dy: 0.2,  latido: 1.3,  mano: true },   // 3 suspira
  ];
  function poseAmor(i, cara) {
    const f = AMOR[i];
    return Object.assign({ prop: 'amor', propPhase: i, bodyDY: f.dy }, f.mano ? { armR: false } : null, cara);
  }
  // Un corazón de alto ~2 a escala 1, con la punta abajo.
  function corazonPath(c, x, y, k) {
    c.beginPath();
    c.moveTo(x, y + 0.95 * k);
    c.bezierCurveTo(x - 1.35 * k, y - 0.1 * k, x - 0.6 * k, y - 1.2 * k, x, y - 0.35 * k);
    c.bezierCurveTo(x + 0.6 * k, y - 1.2 * k, x + 1.35 * k, y - 0.1 * k, x, y + 0.95 * k);
    c.closePath();
  }
  function propAmor(c, P, pose) {
    const f = AMOR[pose.propPhase || 0] || AMOR[0];
    const dy = pose.bodyDY || 0;
    const C = AMOR_COL;
    if (f.latido) {
      const x = BODY.cx - 0.5, y = 12.6 + dy, k = f.latido;
      corazonPath(c, x, y, k);
      const g = c.createLinearGradient(x - k, y - k, x + k, y + k);
      g.addColorStop(0, C.corazonLuz); g.addColorStop(0.45, C.corazon); g.addColorStop(1, C.corazonSombra);
      c.fillStyle = g; c.fill();
      c.lineWidth = 0.2; c.lineJoin = 'round'; c.strokeStyle = C.contorno; c.stroke();
      ellipsePath(c, x - 0.48 * k, y - 0.42 * k, 0.22 * k, 0.15 * k);
      c.fillStyle = withA('#ffffff', 0.8); c.fill();
    }
    if (f.mano) {
      const T = llevaBata() ? _bataTela : null;
      const hx = 11.6, hy = 12.4 + dy;
      brazoGuitarra(c, P, T, charActual(), 1, 13.1, 10.4 + dy, hx, hy);
      manoGuitarra(c, P, hx, hy);
    }
  }

  /**
   * Asiente: un "sí" seguro, con el pulgar arriba al final.
   *
   * La versión anterior alternaba `lookU` y `base` tres veces: los ojos
   * subían y bajaban, pero la cabeza no, así que se leía como un parpadeo
   * raro y no como un sí. Ahora la cabeza baja de verdad (bodyDY y la mirada
   * al piso, con `plt-asiente` empujando el canvas) y el remate es la mano
   * derecha con el pulgar arriba (por el hook `brazo` del personaje).
   *
   * Cuadros clave (propPhase). `pulgar`: la mano en alto; sin ella el brazo
   * queda en reposo, pintado por el motor. `lookU`, que usaba antes, es una
   * pose compartida y no se toca.
   */
  const ASIENTE = [
    { dy: -0.1 },                        // 0 la cabeza arriba
    { dy: 0.25 },                        // 1 la cabeza abajo: "sí"
    { dy: -0.05, pulgar: [14.7, 9.6] },  // 2 el pulgar arriba
  ];
  function poseAsiente(i, cara) {
    const f = ASIENTE[i];
    return Object.assign({ prop: 'asiente', propPhase: i, bodyDY: f.dy }, f.pulgar ? { armR: false } : null, cara);
  }
  function propAsiente(c, P, pose) {
    const f = ASIENTE[pose.propPhase || 0] || ASIENTE[0];
    if (!f.pulgar) return;
    const dy = pose.bodyDY || 0;
    const T = llevaBata() ? _bataTela : null;
    const hx = f.pulgar[0], hy = f.pulgar[1] + dy;
    brazoGuitarra(c, P, T, charActual(), 1, 13.1, 10.6 + dy, hx, hy);
    // El pulgar va primero y el puño encima, así nace del puño (como el
    // índice de pensar). Más corto y más grueso que un índice.
    c.save();
    c.translate(hx - 0.2, hy - 0.15);
    c.rotate(-0.12);
    roundRect(c, -0.24, -1.3, 0.48, 1.1, 0.24);
    c.lineWidth = 0.24; c.strokeStyle = withA(P.edge, 0.8); c.stroke();
    const g = c.createLinearGradient(-0.24, -1.3, 0.24, 0);
    g.addColorStop(0, P.handA); g.addColorStop(1, P.handB);
    c.fillStyle = g; c.fill();
    c.restore();
    puno(c, P, hx, hy);
  }

  /**
   * Curiosidad: ladea la cabeza, mira a un lado y al otro, se rasca la
   * cabeza y cae en la cuenta ("¡oh!").
   *
   * La versión anterior eran `lookU`, `lookR` y `lookL` seguidos con un "?"
   * de texto que se iba 42 px a la derecha, fuera del riel de Buenas
   * prácticas. Esas tres son poses compartidas (el corto, el seguimiento del
   * cursor) y no se tocan: el gesto tiene las suyas.
   *
   * Cuadros clave (propPhase). `rasca`: la mano derecha en la cabeza (por el
   * hook `brazo` del personaje, con su manga o su guante), por delante del
   * pelo o del sombrero; sin ella el brazo queda en reposo, pintado por el
   * motor. El ladeo lo pone `plt-curioso`, que mueve el canvas.
   */
  const CURIOSO = [
    { dy: 0 },                           // 0 mira a un lado
    { dy: 0.05 },                        // 1 mira al otro
    { dy: 0, rasca: [13.1, 4.5] },       // 2 se rasca la cabeza
    { dy: 0.05, rasca: [12.5, 3.9] },    // 3 se rasca, la mano un poco más adentro
    { dy: -0.15 },                       // 4 ¡oh!
  ];
  function poseCurioso(i, cara) {
    const f = CURIOSO[i];
    return Object.assign({ prop: 'curioso', propPhase: i, bodyDY: f.dy }, f.rasca ? { armR: false } : null, cara);
  }
  function propCurioso(c, P, pose) {
    const f = CURIOSO[pose.propPhase || 0] || CURIOSO[0];
    if (!f.rasca) return;
    const dy = pose.bodyDY || 0;
    const T = llevaBata() ? _bataTela : null;
    const hx = f.rasca[0], hy = f.rasca[1] + dy;
    brazoGuitarra(c, P, T, charActual(), 1, 13.6, 9.6 + dy, hx, hy);
    puno(c, P, hx, hy);
  }

  /**
   * Levita: meditación, con los brazos abiertos y las palmas arriba.
   *
   * La versión anterior era `happy` quieto con un saltito en px y seis
   * destellos de texto que volaban hasta 36 px, fuera del riel de Buenas
   * prácticas. Ahora es una postura de meditación:
   *
   * - Los pies recogidos debajo del cuerpo (`pies`, juntos y un poco más
   *   arriba), los ojos cerrados y una sonrisa.
   * - LOS DOS brazos abiertos a los costados con la mano abierta y los dedos
   *   arriba: acá los dos brazos son la postura. Van por el hook `brazo` del
   *   personaje (brazoGuitarra), con su manga o su guante, y por delante del
   *   cuerpo (PROP_DELANTE).
   * - Debajo, un resplandor que respira (`brillo`), de color fijo: se dibuja
   *   detrás del cuerpo (PROPS_BEHIND). La flotación la pone `plt-levita`,
   *   que mueve el canvas.
   *
   * Cuadros clave (propPhase). `manos`: [izq x, y, der x, y]; `giro`: la
   * inclinación de las manos (los dedos hacia afuera); `recoge`: los pies
   * recogidos.
   */
  const LEVITA_COL = { brillo: '#ffe9a8', brilloBorde: '#9fd8ff' };
  const LEVITA = [
    { dy: 0.2,  manos: [2.6, 11.6, 13.4, 11.6], giro: 0.5,  recoge: true, brillo: 0 },    // 0 se acomoda
    { dy: 0,    manos: [1.5, 8.3, 14.5, 8.3],   giro: 0.25, recoge: true, brillo: 0.65 }, // 1 om
    { dy: -0.1, manos: [1.4, 7.8, 14.6, 7.8],   giro: 0.2,  recoge: true, brillo: 1 },    // 2 inspira
    { dy: 0.25, manos: [2.3, 11.7, 13.7, 11.7], giro: 0.55, recoge: false, brillo: 0 },   // 3 aterriza
  ];
  function poseLevita(i, cara) {
    const f = LEVITA[i];
    const extra = { prop: 'levita', propPhase: i, armL: false, armR: false, bodyDY: f.dy };
    if (f.recoge) {
      const y = BODY.bot + f.dy + 0.25;
      extra.pies = [[BODY.cx - 1.25, y], [BODY.cx + 1.25, y]];
    }
    return Object.assign(extra, cara);
  }
  // El resplandor de abajo, detrás del cuerpo.
  function propLevitaAtras(c, P, pose) {
    const f = LEVITA[pose.propPhase || 0] || LEVITA[0];
    if (!f.brillo) return;
    const C = LEVITA_COL, x = BODY.cx, y = BODY.bot + 1.3 + (pose.bodyDY || 0);
    const rx = 4.2 + f.brillo * 1.2;
    c.save();
    c.translate(x, y);
    c.scale(1, 0.24);
    const g = c.createRadialGradient(0, 0, 0.3, 0, 0, rx);
    g.addColorStop(0, withA(C.brillo, 0.75 * f.brillo));
    g.addColorStop(0.6, withA(C.brillo, 0.3 * f.brillo));
    g.addColorStop(1, withA(C.brilloBorde, 0));
    ellipsePath(c, 0, 0, rx, rx);
    c.fillStyle = g; c.fill();
    c.restore();
  }
  // Los dos brazos abiertos, por delante del cuerpo.
  function propLevitaDelante(c, P, pose) {
    const f = LEVITA[pose.propPhase || 0] || LEVITA[0];
    const dy = pose.bodyDY || 0;
    const T = llevaBata() ? _bataTela : null;
    const ch = charActual();
    const m = f.manos;
    // La de la izquierda de quien mira inclina los dedos hacia afuera
    // (giro negativo) y lleva el pulgar hacia el cuerpo (1); la otra, al revés.
    const [lx, ly] = munecaDe(m[0], m[1] + dy, -f.giro);
    const [rx, ry] = munecaDe(m[2], m[3] + dy, f.giro);
    brazoGuitarra(c, P, T, ch, -1, 2.9, 10.4 + dy, lx, ly);
    brazoGuitarra(c, P, T, ch, 1, 13.1, 10.4 + dy, rx, ry);
    manoAbierta(c, P, m[0], m[1] + dy, -f.giro, 1);
    manoAbierta(c, P, m[2], m[3] + dy, f.giro, -1);
  }

  /**
   * El escaneo: un haz de holograma que recorre el cuerpo dentro de un marco
   * de visor, y un visto bueno al final.
   *
   * La versión anterior era una banda del color del tema que bajaba en cinco
   * cuadros, dos veces, recortada a la silueta de siempre: no seguía a
   * `bodyRX` (el pepinillo, los personajes anchos) ni a `bodyDY`, y la piel
   * plana de los Dragon Ball la tapaba, así que tenían que volver a
   * pintarla. Ahora, con la receta de la guitarra:
   *
   * - Colores FIJOS, verde de terminal con un núcleo claro y contorno
   *   oscuro: se lee sobre cualquier cuerpo y cualquier tema.
   * - Se pinta como prop, por delante de todo (también del pelo y de la
   *   piel de los Dragon Ball), y la parte que tiñe va recortada a la
   *   silueta del cuadro, con su `bodyRX` y su `bodyDY`.
   * - Cuatro esquinas de visor enmarcan el cuerpo (`marco`: su tamaño, crece
   *   al entrar). El haz (`haz`: de 0 arriba a 1 abajo) cruza el cuerpo y
   *   sobresale apenas por los costados; lo que ya barrió queda con una
   *   rejilla tenue (`barrido`: hasta dónde, y para qué lado).
   * - Remate: el marco completo y un visto bueno en un círculo.
   */
  const ESCANEO_COL = { haz: '#3cf0a8', nucleo: '#eafff6', contorno: '#0b2a20' };
  const ESCANEO = [
    { marco: 0.55 },                                // 0 aparece el visor
    { marco: 1, haz: 0.1,  barrido: [0, 0.1] },     // 1 scan0
    { marco: 1, haz: 0.3,  barrido: [0, 0.3] },     // 2 scan1
    { marco: 1, haz: 0.5,  barrido: [0, 0.5] },     // 3 scan2
    { marco: 1, haz: 0.7,  barrido: [0, 0.7] },     // 4 scan3
    { marco: 1, haz: 0.9,  barrido: [0, 0.9] },     // 5 scan4
    { marco: 1, ok: true,  barrido: [0, 1] },       // 6 listo
  ];
  function poseEscaneo(i, cara) {
    return Object.assign({ prop: 'escaneo', propPhase: i }, cara);
  }
  function propEscaneo(c, P, pose) {
    const f = ESCANEO[pose.propPhase || 0] || ESCANEO[0];
    const C = ESCANEO_COL;
    const dy = pose.bodyDY || 0;
    const rx = BODY.rx * (pose.bodyRX || 1);
    const top = BODY.top + dy, bot = BODY.bot + dy, h = bot - top, cx = BODY.cx;
    const det = _k >= 5;
    c.save();
    c.lineCap = 'round'; c.lineJoin = 'round';

    // Lo ya barrido: un velo con rejilla, recortado a la silueta.
    if (f.barrido) {
      const y0 = top + h * f.barrido[0], y1 = top + h * f.barrido[1];
      c.save();
      blobPath(c, cx, top, bot, rx);
      c.clip();
      c.fillStyle = withA(C.haz, 0.12);
      c.fillRect(cx - rx, y0, rx * 2, y1 - y0);
      if (det) {
        c.beginPath();
        for (let y = y0 + 0.45; y < y1; y += 0.6) { c.moveTo(cx - rx, y); c.lineTo(cx + rx, y); }
        c.lineWidth = 0.06; c.strokeStyle = withA(C.haz, 0.35); c.stroke();
      }
      c.restore();
    }

    // El haz: un resplandor recortado al cuerpo y una línea que sobresale.
    if (f.haz != null) {
      const y = top + h * f.haz;
      c.save();
      blobPath(c, cx, top, bot, rx);
      c.clip();
      const g = c.createLinearGradient(0, y - 1.2, 0, y + 1.2);
      g.addColorStop(0, withA(C.haz, 0));
      g.addColorStop(0.5, withA(C.haz, 0.55));
      g.addColorStop(1, withA(C.haz, 0));
      c.fillStyle = g;
      c.fillRect(cx - rx, y - 1.2, rx * 2, 2.4);
      c.restore();
      const x0 = cx - rx - 0.5, x1 = cx + rx + 0.5;
      c.beginPath(); c.moveTo(x0, y); c.lineTo(x1, y);
      c.lineWidth = 0.42; c.strokeStyle = withA(C.contorno, 0.55); c.stroke();
      c.lineWidth = 0.24; c.strokeStyle = C.haz; c.stroke();
      c.lineWidth = 0.08; c.strokeStyle = C.nucleo; c.stroke();
      // Las puntas del haz: dos marcas cortas, como en un visor.
      c.beginPath();
      c.moveTo(x0, y - 0.35); c.lineTo(x0, y + 0.35);
      c.moveTo(x1, y - 0.35); c.lineTo(x1, y + 0.35);
      c.lineWidth = 0.18; c.strokeStyle = C.haz; c.stroke();
    }

    // Las cuatro esquinas del visor.
    const mx = rx + 0.75, my0 = top - 0.55, my1 = bot + 0.45, L = 1.3 * f.marco;
    const esquinas = [[cx - mx, my0, 1, 1], [cx + mx, my0, -1, 1], [cx - mx, my1, 1, -1], [cx + mx, my1, -1, -1]];
    c.beginPath();
    for (const [x, y, sx, sy] of esquinas) {
      c.moveTo(x, y + sy * L); c.lineTo(x, y); c.lineTo(x + sx * L, y);
    }
    c.lineWidth = 0.42; c.strokeStyle = withA(C.contorno, 0.6); c.stroke();
    c.lineWidth = 0.22; c.strokeStyle = C.haz; c.stroke();

    // El visto bueno, arriba a la derecha del marco.
    if (f.ok) {
      const bx = cx + rx - 0.3, by = top + 0.7;
      ellipsePath(c, bx, by, 1.05, 1.05);
      c.fillStyle = C.haz; c.fill();
      c.lineWidth = 0.2; c.strokeStyle = C.contorno; c.stroke();
      c.beginPath();
      c.moveTo(bx - 0.5, by + 0.02); c.lineTo(bx - 0.12, by + 0.42); c.lineTo(bx + 0.52, by - 0.38);
      c.lineWidth = 0.26; c.strokeStyle = C.contorno; c.stroke();
    }
    c.restore();
  }

  // ── Poses ─────────────────────────────────────────────────────────────
  //
  // Los NOMBRES son los mismos que los frames del motor pixel, y eso es
  // deliberado: los veinte y pico de gestos (g_wave, g_coffee, g_skate…) llaman
  // a drawF('waveUp'), drawF('coffeeSip')… y no se tocaron. Lo que cambió es
  // qué hay del otro lado del nombre: antes una matriz de píxeles, ahora un
  // puñado de parámetros que el pintor interpreta.
  //
  // Por eso una pose nueva ya no cuesta redibujar 288 celdas: cuesta una línea.
  const POSES = {
    base:        { eyes: 'open' },
    blink:       { eyes: 'shut' },
    wink:        { eyes: 'wink' },
    lookL:       { eyes: 'open', gaze: [-1, 0] },
    lookR:       { eyes: 'open', gaze: [1, 0] },
    lookU:       { eyes: 'open', gaze: [0, -1] },
    happy:       { eyes: 'happy', mouth: 'smile' },
    happyBlink:  { eyes: 'shut',  mouth: 'smile' },
    // ── Pensar (propPiensa), según la tabla PIENSA ──
    // `think` es el nombre público de siempre: Buenas prácticas lo usa como
    // pose fija, así que tiene que leerse sola (la mano en el mentón).
    think:       posePiensa(0, { eyes: 'squint', mouth: 'line', gaze: [-0.7, -0.8] }),
    piensaMira:  posePiensa(1, { eyes: 'open',   mouth: 'line', gaze: [-0.25, -1] }),
    piensaMira2: posePiensa(2, { eyes: 'squint', mouth: 'line', gaze: [0.45, -0.95] }),
    piensaAja:   posePiensa(3, { eyes: 'wide',   mouth: 'o',    gaze: [0.5, -0.6] }),

    walk1:       { eyes: 'open', feet: 1 },
    walk2:       { eyes: 'open', feet: 2 },

    // ── Saludo (propSaludo), según la tabla SALUDO ──
    // `waveUp` y `waveDown` son los nombres públicos de siempre (el corto de la
    // landing los alterna): la muñeca hacia adentro y hacia afuera.
    waveUp:      poseSaludo(1, { eyes: 'happy', mouth: 'smile' }),
    waveDown:    poseSaludo(2, { eyes: 'happy', mouth: 'smile' }),
    saludoSube:  poseSaludo(0, { eyes: 'wide',  mouth: 'smile' }),
    saludoPalma: poseSaludo(3, { eyes: 'happy', mouth: 'o' }),
    // Remate: baja la mano con un guiño.
    saludoBaja:  poseSaludo(4, { eyes: 'wink',  mouth: 'smile' }),

    // ── Festejo (propFestejo), según la tabla FESTEJO ──
    festejaAgacha: poseFestejo(0, { eyes: 'shut',  mouth: 'smile' }),
    festejaSalto:  poseFestejo(1, { eyes: 'happy', mouth: 'o' }),
    festejaCae:    poseFestejo(2, { eyes: 'happy', mouth: 'smile' }),
    festejaPuno:   poseFestejo(3, { eyes: 'happy', mouth: 'o' }),
    festejaPuno2:  poseFestejo(4, { eyes: 'shut',  mouth: 'smile' }),
    festejaRemate: poseFestejo(5, { eyes: 'happy', mouth: 'smile', gaze: [0, -0.6] }),

    // ── Laptop (propLaptop), según la tabla LAPTOP ──
    // `laptopOpen`, `laptopType1` y `laptopType2` son los nombres públicos de
    // siempre: Buenas prácticas usa los dos de tipeo como pose fija y el
    // consejo del topbar los alterna mientras escribe.
    laptopCierra:    poseLaptop(0, { eyes: 'open',  mouth: 'smile', gaze: [0, 0.8] }),
    laptopAbre:      poseLaptop(1, { eyes: 'wide',  mouth: 'o',     gaze: [0, 0.7] }),
    laptopOpen:      poseLaptop(2, { eyes: 'open',  gaze: [0, 0.6] }),
    laptopType1:     poseLaptop(3, { eyes: 'squint', gaze: [-0.2, 0.7] }),
    laptopType2:     poseLaptop(4, { eyes: 'squint', gaze: [0.2, 0.7] }),
    laptopTipea3:    poseLaptop(5, { eyes: 'squint', gaze: [-0.1, 0.7] }),
    laptopTipea4:    poseLaptop(6, { eyes: 'squint', gaze: [0.3, 0.7] }),
    // Se acerca a leer: la luz de la pantalla le da más fuerte en la cara.
    laptopLee:       poseLaptop(7, { eyes: 'open',  gaze: [0.2, 0.9] }),
    // Remate: se estira con las manos arriba y el aviso verde en la pantalla.
    laptopListo:     poseLaptop(8, { eyes: 'happy', mouth: 'smile' }),
    laptopCierraFin: poseLaptop(0, { eyes: 'happy', mouth: 'smile' }),

    // ── Café (propCoffee), según la tabla CAFE ──
    // `coffeeHold` y `coffeeSip` son los nombres públicos de siempre: los usa
    // Buenas prácticas de DBMON como pose fija y el corto de la landing.
    coffeeHold:  poseCafe(0, { eyes: 'open',  mouth: 'smile', gaze: [0.55, 0.55] }),
    cafeSopla:   poseCafe(1, { eyes: 'open',  mouth: 'o',     gaze: [0.6, 0.7] }),
    cafeSopla2:  poseCafe(1, { eyes: 'open',  mouth: 'line',  gaze: [0.6, 0.7] }),
    // Lo huele: se inclina sobre el jarro con los ojos cerrados.
    cafeHuele:   poseCafe(2, { eyes: 'shut',  mouth: 'smile', gaze: [0.2, 0.4] }),
    // El sorbo: ojos cerrados y la cabeza hacia atrás. Sin `mouth`: la boca
    // queda detrás del labio del jarro.
    coffeeSip:   poseCafe(3, { eyes: 'shut',  gaze: [0.1, -0.5] }),
    cafeSorbo:   poseCafe(4, { eyes: 'shut',  gaze: [0.1, -0.8] }),
    cafeTraga:   poseCafe(5, { eyes: 'shut',  mouth: 'smile' }),
    cafeAhh:     poseCafe(6, { eyes: 'happy', mouth: 'o' }),
    // Remate: el jarro en alto como en un brindis, con pilas.
    cafeRemate:  poseCafe(7, { eyes: 'wide',  mouth: 'smile' }),

    // ── Baile (propBaile), según la tabla BAILE ──
    // `danceR` y `danceL` conservan su nombre: el índice arriba y el índice
    // abajo, los dos pasos que se alternan.
    bailaEntra:    poseBaile(0, { eyes: 'open',  mouth: 'smile', gaze: [0.3, 0.4] }),
    bailaCabecea:  poseBaile(1, { eyes: 'happy', mouth: 'smile' }),
    danceR:        poseBaile(2, { eyes: 'open',  mouth: 'o', gaze: [0.9, -0.9] }),
    danceL:        poseBaile(3, { eyes: 'open',  mouth: 'smile', gaze: [-0.4, 0.9] }),
    bailaPuno:     poseBaile(4, { eyes: 'happy', mouth: 'o' }),
    bailaPuno2:    poseBaile(5, { eyes: 'happy', mouth: 'smile' }),
    // Remate: la pose final, mirando a la mano en alto.
    bailaRemate:   poseBaile(6, { eyes: 'wide',  mouth: 'smile', gaze: [0.8, -1] }),

    // ── Siesta (propSiesta), según la tabla SIESTA ──
    // `sleep` conserva su nombre: dormido contra la almohada. `squint` y
    // `startle`, que también usa el gesto, son poses compartidas (Buenas
    // prácticas, el corto) y no se tocan.
    siestaBosteza:  poseSiesta(0, { eyes: 'shut',  mouth: 'o' }),
    siestaCabecea:  poseSiesta(1, { eyes: 'squint', gaze: [0.2, 0.8] }),
    sleep:          poseSiesta(2, { eyes: 'shut' }),
    siestaRonca:    poseSiesta(3, { eyes: 'shut', mouth: 'o' }),
    siestaRevienta: poseSiesta(4, { eyes: 'wide', mouth: 'o', gaze: [0.4, 0] }),

    // ── Mareo (propMareoAtras + propMareoDelante), según la tabla MAREO ──
    // `dizzy` conserva su nombre: la mano en la sien y las estrellas girando.
    mareoEntra:   poseMareo(0, { eyes: 'spiral', mouth: 'o' }),
    dizzy:        poseMareo(1, { eyes: 'spiral', mouth: 'line' }),
    mareo2:       poseMareo(2, { eyes: 'spiral', mouth: 'line' }),
    mareo3:       poseMareo(3, { eyes: 'spiral', mouth: 'o' }),
    mareo4:       poseMareo(4, { eyes: 'spiral', mouth: 'line' }),
    // Se lo sacude de encima: los ojos apretados, ya sin estrellas.
    mareoSacude:  { eyes: 'shut', mouth: 'line' },

    // ── Enamorado (propAmor), según la tabla AMOR ──
    // `love` conserva su nombre: los ojos de corazón y la mano en el pecho.
    amorEntra:    poseAmor(0, { eyes: 'heart', mouth: 'o' }),
    love:         poseAmor(1, { eyes: 'heart', mouth: 'smile' }),
    amorLate:     poseAmor(2, { eyes: 'heart', mouth: 'smile' }),
    amorSuspira:  poseAmor(3, { eyes: 'happy', mouth: 'o' }),

    // ── Asiente (propAsiente), según la tabla ASIENTE ──
    asienteArriba: poseAsiente(0, { eyes: 'open',  mouth: 'smile', gaze: [0, -0.3] }),
    asienteAbajo:  poseAsiente(1, { eyes: 'happy', mouth: 'smile', gaze: [0, 0.9] }),
    asientePulgar: poseAsiente(2, { eyes: 'wink',  mouth: 'smile', gaze: [0.3, 0] }),

    // ── Curiosidad (propCurioso), según la tabla CURIOSO ──
    curiosoMira:   poseCurioso(0, { eyes: 'open',   gaze: [0.9, -0.4] }),
    curiosoMira2:  poseCurioso(1, { eyes: 'open',   gaze: [-0.9, -0.3] }),
    curiosoRasca:  poseCurioso(2, { eyes: 'squint', mouth: 'line', gaze: [0.3, -0.9] }),
    curiosoRasca2: poseCurioso(3, { eyes: 'squint', mouth: 'line', gaze: [0.3, -0.9] }),
    curiosoOh:     poseCurioso(4, { eyes: 'wide',   mouth: 'o', gaze: [0.5, -0.1] }),

    // ── Levita (propLevitaAtras + propLevitaDelante), según la tabla LEVITA ──
    levitaSienta:   poseLevita(0, { eyes: 'shut',  mouth: 'smile' }),
    levitaOm:       poseLevita(1, { eyes: 'shut',  mouth: 'smile' }),
    levitaInspira:  poseLevita(2, { eyes: 'shut',  mouth: 'o' }),
    // Abre un ojo, a ver si está flotando de verdad.
    levitaEspia:    poseLevita(1, { eyes: 'wink',  mouth: 'smile', gaze: [0.5, 0.6] }),
    levitaAterriza: poseLevita(3, { eyes: 'happy', mouth: 'smile' }),

    // ── Estornudo (propEstornudo), según la tabla ESTORNUDO ──
    // `sneezeUp` y `sneezeOut` conservan su nombre: la inspiración y el
    // estornudo en el codo.
    estornudoAh:     poseEstornudo(0, { eyes: 'squint', mouth: 'o', gaze: [0, -0.6] }),
    sneezeUp:        poseEstornudo(1, { eyes: 'squint', mouth: 'o', gaze: [0, -1] }),
    estornudoAh3:    poseEstornudo(2, { eyes: 'shut',   mouth: 'o', gaze: [0, -1] }),
    sneezeOut:       poseEstornudo(3, { eyes: 'shut',   mouth: 'o' }),
    estornudoCodo:   poseEstornudo(4, { eyes: 'squint', mouth: 'line', gaze: [-0.3, 0.3] }),
    estornudoFrota:  poseEstornudo(5, { eyes: 'shut',   mouth: 'line' }),
    estornudoFrota2: poseEstornudo(6, { eyes: 'shut',   mouth: 'line' }),
    // Remate: la sonrisa medio avergonzada, con el brazo ya en reposo.
    estornudoPerdon: { eyes: 'happy', mouth: 'smile', gaze: [0.3, 0.4] },

    // ── Burbuja (propBurbuja), según la tabla BURBUJA ──
    // `bubbleS`, `bubbleM`, `bubbleL` y `bubblePop` conservan su nombre: la
    // burbuja que crece en tres tamaños y el estallido.
    burbujaSaca:   poseBurbuja(0, { eyes: 'open', mouth: 'smile', gaze: [0.8, 0.3] }),
    burbujaSopla:  poseBurbuja(1, { eyes: 'open', mouth: 'o', gaze: [0.5, 0.3] }),
    bubbleS:       poseBurbuja(2, { eyes: 'open', mouth: 'o', gaze: [0.6, 0.1] }),
    bubbleM:       poseBurbuja(3, { eyes: 'open', mouth: 'o', gaze: [0.7, -0.1] }),
    bubbleL:       poseBurbuja(4, { eyes: 'wide', mouth: 'o', gaze: [0.8, -0.3] }),
    // Suelta: la sigue con la mirada mientras sube.
    burbujaSuelta: poseBurbuja(5, { eyes: 'open', mouth: 'smile', gaze: [0.8, -0.6] }),
    burbujaSube:   poseBurbuja(6, { eyes: 'open', mouth: 'smile', gaze: [0.9, -0.9] }),
    bubblePop:     poseBurbuja(7, { eyes: 'wide', mouth: 'o', gaze: [0.9, -0.9] }),

    // ── Skate (propSkate + brazosSkate), según la tabla SKATE ──
    // `skate1` y `skate2` son los nombres públicos de siempre: los dos cuadros
    // de la rodada.
    skate1:        poseSkate(0,  { eyes: 'open', gaze: [0.7, 0.2] }),
    skate2:        poseSkate(1,  { eyes: 'open', gaze: [0.7, 0.2] }),
    skateSube:     poseSkate(2,  { eyes: 'wide', mouth: 'o', gaze: [0.3, 0.8] }),
    skateEmpuja:   poseSkate(3,  { eyes: 'open', mouth: 'line', gaze: [0.8, 0.1] }),
    skateEmpuja2:  poseSkate(4,  { eyes: 'happy', mouth: 'smile' }),
    skateAgacha:   poseSkate(5,  { eyes: 'open', mouth: 'line', gaze: [0.5, 0.9] }),
    skatePop:      poseSkate(6,  { eyes: 'wide', mouth: 'o', gaze: [0.5, 0.4] }),
    // El kickflip: mira la tabla girar debajo de los pies.
    skateFlip1:    poseSkate(7,  { eyes: 'wide', mouth: 'o', gaze: [0, 1] }),
    skateFlip2:    poseSkate(8,  { eyes: 'wide', mouth: 'o', gaze: [0, 1] }),
    skateFlip3:    poseSkate(9,  { eyes: 'wide', mouth: 'o', gaze: [0, 1] }),
    skateAtrapa:   poseSkate(10, { eyes: 'open', gaze: [0, 1] }),
    skateAterriza: poseSkate(11, { eyes: 'happy', mouth: 'smile' }),
    skateRemate:   poseSkate(12, { eyes: 'happy', mouth: 'o' }),

    // ── Guitarra (propGuitar): los dos brazos los dibuja el prop ──
    // `guitar1` y `guitar2` son los nombres públicos de siempre (listPoses,
    // renderPose): se mantienen como sostener y rasguido abajo.
    guitar1:        { eyes: 'open',  gaze: [0.6, 0.7], prop: 'guitar', propPhase: 0, armL: false, armR: false },
    guitar2:        { eyes: 'happy', mouth: 'smile', prop: 'guitar', propPhase: 1, armL: false, armR: false },
    guitarMid:      { eyes: 'happy', mouth: 'smile', prop: 'guitar', propPhase: 2, armL: false, armR: false },
    guitarUp:       { eyes: 'happy', mouth: 'smile', prop: 'guitar', propPhase: 3, armL: false, armR: false },
    // El solo: cabeza gacha, ojos cerrados, cabeceando con la púa.
    guitarSolo:     { eyes: 'shut',  mouth: 'smile', gaze: [0, 0.9], prop: 'guitar', propPhase: 4, armL: false, armR: false },
    guitarSolo2:    { eyes: 'shut',  mouth: 'o',     gaze: [0.4, 1], prop: 'guitar', propPhase: 5, armL: false, armR: false },
    guitarRemate:   { eyes: 'happy', mouth: 'o', prop: 'guitar', propPhase: 6, armL: false, armR: false },

    // ── Escaneo (propEscaneo), según la tabla ESCANEO ──
    // `scan0` a `scan4` conservan su nombre: el haz bajando por el cuerpo,
    // con la mirada siguiéndolo.
    escaneoVisor: poseEscaneo(0, { eyes: 'squint' }),
    scan0:        poseEscaneo(1, { eyes: 'open', gaze: [0, -0.7] }),
    scan1:        poseEscaneo(2, { eyes: 'open', gaze: [0, -0.3] }),
    scan2:        poseEscaneo(3, { eyes: 'open', gaze: [0, 0.1] }),
    scan3:        poseEscaneo(4, { eyes: 'open', gaze: [0, 0.5] }),
    scan4:        poseEscaneo(5, { eyes: 'open', gaze: [0, 0.9] }),
    escaneoListo: poseEscaneo(6, { eyes: 'happy', mouth: 'smile' }),

    // ── Poses de los gestos nuevos ──
    squint:      { eyes: 'squint' },
    startle:     { eyes: 'wide',  mouth: 'o', bodyDY: -0.25 },
    // ── Lupa (propLupa), según la tabla LUPA_CUADROS ──
    // `magnify`, `magnifyL`, `magnifyR` y `magnifyFound` son los nombres
    // públicos de siempre: Buenas prácticas usa `magnify` y `magnifyFound`
    // como pose fija y el corto de la landing recorre los cuatro.
    lupaSaca:     poseLupa(0, { eyes: 'open', gaze: [0.8, 0.6] }),
    // Mira por la lupa: el ojo de atrás se ve enorme a través del vidrio.
    magnify:      poseLupa(1, { eyes: 'open', gaze: [0.15, 0] }),
    lupaParpadea: poseLupa(1, { eyes: 'shut', gaze: [0.15, 0] }),
    magnifyL:     poseLupa(2, { eyes: 'open', mouth: 'line', gaze: [-1, 0] }),
    magnifyR:     poseLupa(3, { eyes: 'open', mouth: 'line', gaze: [1, 0] }),
    // Se acerca: el ojo entrecerrado, todavía más grande detrás del vidrio.
    lupaAcerca:   poseLupa(4, { eyes: 'squint', gaze: [0.15, 0.3] }),
    // Remate: el ojo, enorme y bien abierto detrás del vidrio.
    magnifyFound: poseLupa(5, { eyes: 'wide', mouth: 'o', gaze: [0.1, -0.1] }),
    malabSaca:     poseMalabares(0,  { eyes: 'open', mouth: 'smile', gaze: [0, 0.4] }),
    malab3_0: poseMalabares(1, { eyes: 'open', mouth: 'smile', gaze: [0.3, -0.8] }),
    malab3_1: poseMalabares(2, { eyes: 'open', mouth: 'smile', gaze: [0.5, -0.9] }),
    malab3_2: poseMalabares(3, { eyes: 'open', mouth: 'smile', gaze: [0.1, -0.9] }),
    malab3_3: poseMalabares(4, { eyes: 'open', mouth: 'smile', gaze: [-0.3, -0.8] }),
    malab3_4: poseMalabares(5, { eyes: 'open', mouth: 'smile', gaze: [-0.5, -0.9] }),
    malab3_5: poseMalabares(6, { eyes: 'open', mouth: 'smile', gaze: [-0.1, -0.9] }),
    malab3_6: poseMalabares(7, { eyes: 'open', mouth: 'smile', gaze: [0.3, -0.8] }),
    malab3_7: poseMalabares(8, { eyes: 'open', mouth: 'smile', gaze: [0.5, -0.9] }),
    malab4_0: poseMalabares(9,  { eyes: 'wide', mouth: 'o', gaze: [0.4, -0.9] }),
    malab4_1: poseMalabares(10, { eyes: 'wide', mouth: 'o', gaze: [-0.2, -0.9] }),
    malab4_2: poseMalabares(11, { eyes: 'wide', mouth: 'o', gaze: [-0.5, -0.9] }),
    malab4_3: poseMalabares(12, { eyes: 'wide', mouth: 'o', gaze: [0.2, -0.9] }),
    malab4_4: poseMalabares(13, { eyes: 'wide', mouth: 'o', gaze: [0.5, -0.9] }),
    malab4_5: poseMalabares(14, { eyes: 'wide', mouth: 'o', gaze: [-0.2, -0.9] }),
    malab4_6: poseMalabares(15, { eyes: 'wide', mouth: 'o', gaze: [-0.5, -0.9] }),
    malab4_7: poseMalabares(16, { eyes: 'wide', mouth: 'o', gaze: [0.2, -0.9] }),
    malabCae:      poseMalabares(17, { eyes: 'wide', mouth: 'o', gaze: [0.1, -1] }),
    malabBonk:     poseMalabares(18, { eyes: 'shut', mouth: 'o', bodyDY: 0.3 }),
    malabAtaja:    poseMalabares(19, { eyes: 'open', mouth: 'smile', gaze: [0, -0.7] }),
    malabReverencia: poseMalabares(20, { eyes: 'happy', mouth: 'smile', bodyDY: 0.7 }),
    pintorSaca:    posePintor(0, { eyes: 'open', mouth: 'smile', gaze: [0.7, -0.3] }),
    pintorMide:    posePintor(1, { eyes: 'wink', gaze: [0.9, -0.2] }),
    pintorMoja:    posePintor(2, { eyes: 'open', gaze: [-0.8, 0.8] }),
    pintorPinta:   posePintor(3, { eyes: 'squint', mouth: 'o', gaze: [0.9, 0.1] }),
    pintorPinta2:  posePintor(4, { eyes: 'squint', mouth: 'o', gaze: [0.9, -0.4] }),
    pintorMancha:  posePintor(5, { eyes: 'wide', mouth: 'o', gaze: [0.4, 0.3] }),
    pintorGira:    posePintor(6, { eyes: 'open', mouth: 'smile', gaze: [0.8, -0.2] }),
    pintorMuestra: posePintor(7, { eyes: 'happy', mouth: 'smile' }),
    pintorOrgullo: posePintor(8, { eyes: 'open', mouth: 'smile', gaze: [0.2, 0] }),
    pescaSaca:    posePesca(0,  { eyes: 'open', gaze: [0.5, -0.4] }),
    pescaAtras:   posePesca(1,  { eyes: 'open', mouth: 'o', gaze: [-0.4, -0.6] }),
    pescaTira:    posePesca(2,  { eyes: 'open', mouth: 'smile', gaze: [0.9, 0.2] }),
    pescaEspera:  posePesca(3,  { eyes: 'open', gaze: [0.85, 0.9] }),
    pescaEspera2: posePesca(3,  { eyes: 'shut', gaze: [0.85, 0.9] }),
    pescaTiembla: posePesca(4,  { eyes: 'wide', gaze: [0.9, 0.95] }),
    pescaPica:    posePesca(5,  { eyes: 'wide', mouth: 'o', gaze: [0.9, 0.9] }),
    pescaForceja: posePesca(6,  { eyes: 'squint', mouth: 'o', gaze: [0.7, 0.6] }),
    pescaSale:    posePesca(7,  { eyes: 'wide', mouth: 'smile', gaze: [0.8, -0.6] }),
    pescaMira:    posePesca(8,  { eyes: 'squint', gaze: [0.95, -0.15] }),
    pescaPatalea: posePesca(9,  { eyes: 'wide', mouth: 'o', gaze: [0.95, -0.1] }),
    pescaAplasta: posePesca(10, { eyes: 'shut', mouth: 'smile', gaze: [0.6, 0] }),
    pescaListo:   posePesca(11, { eyes: 'happy', mouth: 'smile' }),
    // ── Yoyó (propYoyo), según la tabla YOYO ──
    yoyoSaca:     poseYoyo(0,  { eyes: 'open', mouth: 'smile', gaze: [0.6, 0.6] }),
    yoyoApunta:   poseYoyo(1,  { eyes: 'squint', mouth: 'line', gaze: [0.7, -0.4] }),
    yoyoTira:     poseYoyo(2,  { eyes: 'open', mouth: 'o', gaze: [0.8, 0.5] }),
    yoyoBaja:     poseYoyo(3,  { eyes: 'open', mouth: 'smile', gaze: [0.8, 0.8] }),
    yoyoDuerme0:  poseYoyo(4,  { eyes: 'open', mouth: 'smile', gaze: [0.8, 0.95] }),
    yoyoDuerme1:  poseYoyo(5,  { eyes: 'open', mouth: 'smile', gaze: [0.8, 0.95] }),
    yoyoDuerme2:  poseYoyo(6,  { eyes: 'open', mouth: 'smile', gaze: [0.8, 0.95] }),
    yoyoDuerme3:  poseYoyo(7,  { eyes: 'open', mouth: 'smile', gaze: [0.8, 0.95] }),
    yoyoPiso:     poseYoyo(8,  { eyes: 'wide', mouth: 'smile', gaze: [0.7, 1] }),
    yoyoPerro0:   poseYoyo(9,  { eyes: 'happy', mouth: 'smile' }),
    yoyoPerro1:   poseYoyo(10, { eyes: 'happy', mouth: 'smile' }),
    yoyoPerro2:   poseYoyo(11, { eyes: 'open', mouth: 'smile', gaze: [0.9, 0.9] }),
    yoyoPerro3:   poseYoyo(12, { eyes: 'open', mouth: 'smile', gaze: [0.95, 0.9] }),
    yoyoPerro4:   poseYoyo(13, { eyes: 'open', mouth: 'o', gaze: [0.95, 0.85] }),
    yoyoPerro5:   poseYoyo(14, { eyes: 'wide', mouth: 'o', gaze: [1, 0.85] }),
    yoyoTenso:    poseYoyo(15, { eyes: 'wide', mouth: 'o', gaze: [1, 0.8] }),
    yoyoTiron:    poseYoyo(16, { eyes: 'squint', mouth: 'line', gaze: [0.6, -0.3] }),
    yoyoSube:     poseYoyo(17, { eyes: 'open', mouth: 'smile', gaze: [0.7, -0.2] }),
    yoyoAtrapa:   poseYoyo(18, { eyes: 'shut', mouth: 'smile' }),
    yoyoRemate:   poseYoyo(19, { eyes: 'wink', mouth: 'smile' }),

    // ── Jueguito (propJueguitoAtras / propJueguitoDelante), según JUEGUITO ──
    juegPisa:     poseJueguito(0,  { eyes: 'open', mouth: 'smile', gaze: [0.7, 0.9] }),
    juegLevanta:  poseJueguito(1,  { eyes: 'open', mouth: 'line', gaze: [0.8, 0.6] }),
    juegSube:     poseJueguito(2,  { eyes: 'open', mouth: 'smile', gaze: [0.8, 0.3] }),
    juegPie0:     poseJueguito(3,  { eyes: 'squint', mouth: 'line', gaze: [0.7, 0.7] }),
    juegAire0:    poseJueguito(4,  { eyes: 'open', mouth: 'smile', gaze: [0.8, 0.2] }),
    juegRodilla0: poseJueguito(5,  { eyes: 'squint', mouth: 'line', gaze: [0.8, 0.5] }),
    juegAire1:    poseJueguito(6,  { eyes: 'open', mouth: 'smile', gaze: [0.8, 0.2] }),
    juegPie1:     poseJueguito(7,  { eyes: 'squint', mouth: 'line', gaze: [0.7, 0.7] }),
    juegAire2:    poseJueguito(8,  { eyes: 'open', mouth: 'smile', gaze: [0.8, 0.2] }),
    juegRodilla1: poseJueguito(9,  { eyes: 'squint', mouth: 'line', gaze: [0.8, 0.5] }),
    juegAire3:    poseJueguito(10, { eyes: 'open', mouth: 'smile', gaze: [0.9, 0.2] }),
    juegPifia:    poseJueguito(11, { eyes: 'wide', mouth: 'o', gaze: [0.9, 0.6] }),
    juegAlto:     poseJueguito(12, { eyes: 'wide', mouth: 'o', gaze: [0.9, -0.6] }),
    juegCima:     poseJueguito(13, { eyes: 'wide', mouth: 'o', gaze: [1, -1] }),
    juegBaja:     poseJueguito(14, { eyes: 'open', mouth: 'line', gaze: [0.9, -0.5] }),
    juegPecho:    poseJueguito(15, { eyes: 'squint', mouth: 'line', gaze: [0.9, 0.4] }),
    juegResbala:  poseJueguito(16, { eyes: 'open', mouth: 'smile', gaze: [0.8, 0.8] }),
    juegCae:      poseJueguito(17, { eyes: 'open', mouth: 'smile', gaze: [0.7, 1] }),
    juegPlanta:   poseJueguito(18, { eyes: 'happy', mouth: 'smile' }),
    juegRemate:   poseJueguito(19, { eyes: 'wink', mouth: 'smile' }),
    // ── Mate (propMate), según la tabla MATE ──
    mateSaca:     poseMate(0,  { eyes: 'open', mouth: 'smile', gaze: [0.2, 0.7] }),
    matePrepara:  poseMate(1,  { eyes: 'open', mouth: 'line', gaze: [0.7, 0.8] }),
    mateCeba0:    poseMate(2,  { eyes: 'squint', mouth: 'line', gaze: [0.8, 0.9] }),
    mateCeba1:    poseMate(3,  { eyes: 'squint', mouth: 'line', gaze: [0.8, 0.9] }),
    mateDeja:     poseMate(4,  { eyes: 'open', mouth: 'smile', gaze: [0.7, 0.7] }),
    mateSorbo:    poseMate(5,  { eyes: 'shut', mouth: 'o', gaze: [0.1, -0.5] }),
    mateQuema:    poseMate(6,  { eyes: 'wide', mouth: 'o', gaze: [0.8, 0.5] }),
    mateSopla0:   poseMate(7,  { eyes: 'open', mouth: 'o', gaze: [0.8, 0.7] }),
    mateSopla1:   poseMate(8,  { eyes: 'open', mouth: 'line', gaze: [0.8, 0.7] }),
    mateSorbo2:   poseMate(9,  { eyes: 'shut', mouth: 'o', gaze: [0.1, -0.5] }),
    mateRuido:    poseMate(10, { eyes: 'happy', mouth: 'o', gaze: [0.1, -0.8] }),
    mateAhh:      poseMate(11, { eyes: 'happy', mouth: 'smile' }),
    // ── Avión de papel (propAvion), según la tabla AVION ──
    avionHoja:    poseAvion(0,  { eyes: 'open', mouth: 'smile', gaze: [0, 0.9] }),
    avionDobla0:  poseAvion(1,  { eyes: 'squint', mouth: 'line', gaze: [0, 0.9] }),
    avionDobla1:  poseAvion(2,  { eyes: 'squint', mouth: 'line', gaze: [0, 0.9] }),
    avionListo:   poseAvion(3,  { eyes: 'open', mouth: 'smile', gaze: [0.7, 0.8] }),
    avionMira:    poseAvion(4,  { eyes: 'open', mouth: 'smile', gaze: [0.9, 0.2] }),
    avionApunta:  poseAvion(5,  { eyes: 'wink', mouth: 'line', gaze: [1, -0.3] }),
    avionLanza:   poseAvion(6,  { eyes: 'open', mouth: 'o', gaze: [1, 0] }),
    avionPicada:  poseAvion(7,  { eyes: 'wide', mouth: 'o', gaze: [0.9, 0.9] }),
    avionSuelo:   poseAvion(8,  { eyes: 'squint', mouth: 'line', gaze: [0.6, 1] }),
    avionLevanta: poseAvion(9,  { eyes: 'open', mouth: 'line', gaze: [0.7, 1] }),
    avionArregla0: poseAvion(10, { eyes: 'squint', mouth: 'line', gaze: [0.5, 0.4] }),
    avionArregla1: poseAvion(11, { eyes: 'squint', mouth: 'line', gaze: [0.5, 0.3] }),
    avionSopla:   poseAvion(12, { eyes: 'open', mouth: 'o', gaze: [0.5, 0.3] }),
    avionApunta2: poseAvion(13, { eyes: 'wink', mouth: 'smile', gaze: [1, -0.3] }),
    avionVuela:   poseAvion(14, { eyes: 'wide', mouth: 'o', gaze: [1, -0.4] }),
    avionVisera:  poseAvion(15, { eyes: 'open', mouth: 'smile', gaze: [1, -0.8] }),
    avionFesteja: poseAvion(16, { eyes: 'happy', mouth: 'smile' }),

    // ── Cubo Rubik (propRubik), según la tabla RUBIK ──
    rubikSaca:     poseRubik(0,  { eyes: 'open', mouth: 'smile', gaze: [0.5, 0.9] }),
    rubikMira:     poseRubik(1,  { eyes: 'open', mouth: 'line', gaze: [0, 1] }),
    rubikGira0:    poseRubik(2,  { eyes: 'squint', mouth: 'line', gaze: [0, 1] }),
    rubikGira1:    poseRubik(3,  { eyes: 'squint', mouth: 'line', gaze: [0, 1] }),
    rubikGira2:    poseRubik(4,  { eyes: 'squint', mouth: 'line', gaze: [0.2, 1] }),
    rubikGira3:    poseRubik(5,  { eyes: 'squint', mouth: 'line', gaze: [0, 1] }),
    rubikGira4:    poseRubik(6,  { eyes: 'squint', mouth: 'line', gaze: [0.2, 1] }),
    rubikGira5:    poseRubik(7,  { eyes: 'squint', mouth: 'line', gaze: [0, 1] }),
    rubikTraba0:   poseRubik(8,  { eyes: 'squint', mouth: 'line', gaze: [0.3, 1] }),
    rubikTraba1:   poseRubik(9,  { eyes: 'shut', mouth: 'line', gaze: [0.3, 1] }),
    rubikCerca:    poseRubik(10, { eyes: 'wide', mouth: 'line', gaze: [0.8, 0.7] }),
    rubikPiensa:   poseRubik(11, { eyes: 'open', mouth: 'line', gaze: [0.4, -1] }),
    rubikSacude0:  poseRubik(12, { eyes: 'squint', mouth: 'o', gaze: [-0.2, 1] }),
    rubikSacude1:  poseRubik(13, { eyes: 'squint', mouth: 'o', gaze: [0.3, 1] }),
    rubikUltimo:   poseRubik(14, { eyes: 'open', mouth: 'line', gaze: [0, 1] }),
    rubikResuelto: poseRubik(15, { eyes: 'wide', mouth: 'o', gaze: [0, 1] }),
    rubikMuestra:  poseRubik(16, { eyes: 'happy', mouth: 'smile', gaze: [0.8, -0.6] }),
    rubikContento: poseRubik(17, { eyes: 'happy', mouth: 'smile' }),

    // ── Selfie (propSelfie), según la tabla SELFIE ──
    selfieSaca:     poseSelfie(0,  { eyes: 'open', mouth: 'smile', gaze: [0.6, 0.9] }),
    selfieLevanta:  poseSelfie(1,  { eyes: 'open', mouth: 'smile', gaze: [1, -0.5] }),
    selfieAcomoda0: poseSelfie(2,  { eyes: 'squint', mouth: 'line', gaze: [1, -0.3] }),
    selfieAcomoda1: poseSelfie(3,  { eyes: 'squint', mouth: 'line', gaze: [1, -0.6] }),
    selfiePose:     poseSelfie(4,  { eyes: 'open', mouth: 'smile', gaze: [1, -0.6] }),
    selfieFlash:    poseSelfie(5,  { eyes: 'shut', mouth: 'smile', gaze: [1, -0.6] }),
    selfieEncandila: poseSelfie(6, { eyes: 'spiral', mouth: 'o' }),
    selfieMira:     poseSelfie(7,  { eyes: 'open', mouth: 'line', gaze: [0.7, 0.9] }),
    selfieMuestra:  poseSelfie(8,  { eyes: 'squint', mouth: 'line', gaze: [0, 0.2] }),
    selfieSuspira:  poseSelfie(8,  { eyes: 'shut', mouth: 'o', gaze: [0, 0.2] }),
    selfieOtra:     poseSelfie(9,  { eyes: 'open', mouth: 'line', gaze: [1, -0.5] }),
    selfieOjazos:   poseSelfie(10, { eyes: 'wide', mouth: 'smile', gaze: [1, -0.6] }),
    selfieFlash2:   poseSelfie(11, { eyes: 'wide', mouth: 'smile', gaze: [1, -0.6] }),
    selfieMira2:    poseSelfie(12, { eyes: 'open', mouth: 'smile', gaze: [0.7, 0.9] }),
    selfieConforme: poseSelfie(13, { eyes: 'happy', mouth: 'smile' }),

    // ── Clase: el puntero (propPuntero) ──
    // Las usa la guía de Buenas prácticas de DBMON para dar clase frente a una
    // pizarra que dibuja la página: la pizarra queda ARRIBA del personaje, así
    // que señala hacia arriba. `armR: false` porque el brazo del puntero lo
    // dibuja el prop.
    teachPoint:  { eyes: 'open',  mouth: 'o', gaze: [0.35, -1], prop: 'puntero', propPhase: 0, armR: false },
    teachTap:    { eyes: 'open',  gaze: [0.35, -1], prop: 'puntero', propPhase: 1, armR: false },
    teachNod:    { eyes: 'happy', mouth: 'smile', gaze: [0, 0.7], prop: 'puntero', propPhase: 2, armR: false },
    teachTalk:   { eyes: 'open',  mouth: 'o', prop: 'puntero', propPhase: 2, armR: false },
    teachSmile:  { eyes: 'happy', mouth: 'smile', prop: 'puntero', propPhase: 2, armR: false },
    teachBlink:  { eyes: 'shut',  mouth: 'smile', prop: 'puntero', propPhase: 2, armR: false },
  };

  const PROPS_BEHIND = { pintor: 1, pesca: 1, skate: 1, mareo: 1, levita: 1, jueguito: 1 };
  const PROP_FN = { selfie: propSelfie, rubik: propRubik, avion: propAvion, mate: propMate, jueguito: propJueguitoAtras, yoyo: propYoyo, malabares: propMalabares, pintor: propPintor, pesca: propPesca, laptop: propLaptop, coffee: propCoffee, skate: propSkate, guitar: propGuitar, lupa: propLupa, puntero: propPuntero,
    saludo: propSaludo, festejo: propFestejo, piensa: propPiensa, burbuja: propBurbuja,
    baile: propBaile, estornudo: propEstornudo, siesta: propSiesta, mareo: propMareoAtras,
    amor: propAmor, asiente: propAsiente, curioso: propCurioso, levita: propLevitaAtras,
    escaneo: propEscaneo };
  // Lo que un prop de atrás pone ADELANTE del cuerpo: la tabla va detrás y los
  // brazos del skate, delante (encima de la bata y de las capas del personaje).
  const PROP_DELANTE = { pintor: propPintorDelante, pesca: propPescaDelante, skate: brazosSkate, mareo: propMareoDelante, levita: propLevitaDelante,
    jueguito: propJueguitoDelante };

  // ── Pintor ────────────────────────────────────────────────────────────

  /**
   * Ajusta el tamaño real del canvas a la escala pedida.
   *
   * El backing store se multiplica por devicePixelRatio y el tamaño CSS no:
   * con curvas y gradientes, dibujar a 1× en una pantalla 2× se ve mordido en
   * los bordes. El sprite viejo no lo necesitaba porque los píxeles eran el
   * punto; acá son el enemigo.
   */
  function fitCanvas(cnv, s) {
    const dpr = Math.min(window.devicePixelRatio || 1, 3);
    const w = Math.round(BOX.W * s * dpr), h = Math.round(BOX.H * s * dpr);
    if (cnv.width !== w || cnv.height !== h) { cnv.width = w; cnv.height = h; }
    // Sólo se escribe cuando cambió: esto corre en cada cuadro y asignar
    // style.width igual igual dispara un recálculo de estilos al pedo.
    const cw = (BOX.W * s) + 'px', chh = (BOX.H * s) + 'px';
    if (cnv.style.width !== cw)  cnv.style.width  = cw;
    if (cnv.style.height !== chh) cnv.style.height = chh;
    return dpr;
  }

  /**
   * Pinta una pose en un contexto a `s` píxeles por unidad.
   *
   * Mantiene la firma del pintor viejo (contexto, qué, escala, estado de FX)
   * para que el topbar, la preview del panel y el avatar del chatbot sigan
   * compartiendo un solo camino de dibujo. Si cada uno pintara por su lado,
   * un ajuste al personaje habría que hacerlo en tres lugares — que es
   * exactamente cómo empezó este proyecto.
   */
  function paintFrame(c, poseName, s, st) {
    const basePose = POSES[poseName] || POSES.base;
    const ch = charActual();
    // Un personaje puede tener otra silueta (el pepinillo es más angosto): se
    // deriva por cuadro sin tocar las poses compartidas.
    const pose = (ch && ch.bodyRX)
      ? Object.assign({}, basePose, { bodyRX: (basePose.bodyRX || 1) * ch.bodyRX })
      : basePose;
    // Un personaje reemplaza al accesorio. Un material no: es la misma
    // silueta, así que el accesorio se dibuja igual. La textura va primero
    // (recortada al cuerpo) y el accesorio después, para que el gorro no
    // quede debajo de la veta. Detrás es al revés: las alas quedan al fondo
    // y lo que el material cuelga atrás (el hongo de la piedra) delante de
    // ellas, los dos detrás del cuerpo.
    const viste = !ch || esMateria(character);
    const capa = (nombre) => {
      if (viste && nombre === 'back') drawAcc(c, st, nombre);
      if (ch) {
        const f = ch[nombre];
        if (f) f(c, P, pose);
      }
      if (viste && nombre !== 'back') drawAcc(c, st, nombre);
    };
    const P = vecPal(st);
    const dpr = Math.min(window.devicePixelRatio || 1, 3);
    const k = s * dpr;
    _k = k;
    _poseActual = pose;

    // Re-ajusta el canvas antes de pintar. Es en CADA cuadro y no sólo al
    // crearlo porque el zoom del navegador cambia devicePixelRatio sin disparar
    // ningún evento propio: con el tamaño congelado, un Ctrl++ deja el dibujo
    // escalado por el navegador y el personaje sale borroso o mordido.
    // fitCanvas sólo escribe width/height cuando cambiaron, así que en el caso
    // normal no cuesta nada.
    if (c.canvas) fitCanvas(c.canvas, s);

    c.setTransform(1, 0, 0, 1, 0, 0);
    c.clearRect(0, 0, BOX.W * k, BOX.H * k);
    // El origen se corre al hueco del personaje. De acá en adelante todo el
    // dibujo usa las MISMAS coordenadas de siempre y no sabe que hay margen.
    c.setTransform(k, 0, 0, k, PAD.l * k, PAD.t * k);
    c.lineJoin = 'round';

    capa('back');                   // lo que cuelga detrás (las katanas)
    if (pose.prop && PROPS_BEHIND[pose.prop]) PROP_FN[pose.prop](c, P, pose);

    drawFeet(c, P, pose);
    drawArms(c, P, pose);

    // El cuerpo lleva el halo del glow de cuerpo. Va acá y no como filtro CSS
    // del canvas porque el filtro tiñe TAMBIÉN los props y el accesorio.
    // Un personaje no lleva el glow elegido: sólo el suyo propio, si tiene.
    c.save();
    if (ch) {
      if (ch.halo) {
        c.shadowColor = ch.halo.color;
        c.shadowBlur  = k * ch.halo.blur;
      }
    } else if (fxBody.length > 0 && _bodyM > 0.02) {
      c.shadowColor = bodyGlowColor();
      c.shadowBlur  = k * 1.5 * _bodyM;
    }
    drawBody(c, P, pose);
    c.restore();

    capa('head');                   // accesorio sobre la cabeza
    drawEyes(c, P, pose);
    drawMouth(c, P, pose);
    capa('front');                  // accesorio sobre la cara (lentes, parche)
    capa('over');                   // vidrio del casco, portilla: va sobre todo

    if (pose.prop && !PROPS_BEHIND[pose.prop]) PROP_FN[pose.prop](c, P, pose);
    if (pose.prop && PROP_DELANTE[pose.prop]) PROP_DELANTE[pose.prop](c, P, pose);

    c.setTransform(1, 0, 0, 1, 0, 0);
  }

  // ── Renderer ──────────────────────────────────────────────────────────
  let canvas, ctx, bubble;

  // ── Resolución del color del neón (glow) por target ───────────────────
  // Prioridad: efecto RGB activo → matiz que cicla; si no, color elegido a
  // mano; si 'auto', el derivado del color base.
  function bodyGlowColor() {
    if (_fxHas(fxBody, 'rgb')) return hslToHex(_bodyHue, 90, 62);
    if (glowBody && glowBody !== 'auto') return glowBody;
    return pal.glow;
  }
  function bodyGlowColorA() {
    if (_fxHas(fxBody, 'rgb')) return hslToHex(_bodyHue, 90, 62) + '88';
    if (glowBody && glowBody !== 'auto') return glowBody + '66';
    return pal.glowA;
  }
  // Ajusta el brillo (lightness) de un color hex en `dl` puntos.
  function adjustL(hex, dl) {
    if (!dl) return hex;
    const { h, s, l } = hexToHsl(hex);
    return hslToHex(h, s, _clamp(l + dl, 4, 96));
  }
  function accGlowColor() {
    if (_fxHas(fxAcc, 'rgb')) return hslToHex(_accHue, 95, 60);
    // `glowAcc` siempre vale 'auto' desde que el accesorio no tiene selector de
    // color; la rama queda por si alguna vez vuelve, pero hoy no se alcanza.
    if (glowAcc && glowAcc !== 'auto') return glowAcc;
    // Auto: versión brillante del color del accesorio. Para accesorios oscuros
    // o grises (ej. auriculares #2a2c34) el bulbo derivado es apagado y el glow
    // no se ve; acá lo subimos a un tinte claro y visible.
    const { h, s } = hexToHsl(_hatMat || HAT_DARK);
    return hslToHex(h, _clamp(s + 25, 0, 100), 74);
  }

  /**
   * Estado de FX del instante: cuánto hue rotar, el pulso de brillo y el color
   * del halo. Lee las variables vivas que avanza el ticker, así el topbar, la
   * preview y el avatar del chatbot coinciden cuadro a cuadro.
   *
   * Antes esto devolvía MAPAS de hex→hex, porque el pintor de píxeles sólo
   * podía recolorear comparando el color de cada celda. El pintor vectorial
   * conoce el rol de lo que está dibujando, así que alcanza con los parámetros.
   */
  function buildFxStyle() {
    // ¿Hay respiración (no-rgb) en el accesorio? Hoy NUNCA: el accesorio solo
    // admite 'rgb' (ver FX_ACC_PERMITIDOS), asi que accDL queda en 0. Se
    // conserva el calculo y no se borra porque es lo que habria que reactivar
    // si algun dia vuelven los otros efectos al accesorio.
    const accBreathe = fxAcc.length > 0 && fxAcc.some(e => e !== 'rgb');
    return {
      bodyHue: _fxHas(fxBody, 'rgb') ? _bodyHue : 0,
      accRGB:  _fxHas(fxAcc, 'rgb'),
      accDL:   accBreathe ? _clamp((_accM - 0.7) * 52, -28, 34) : 0,
      accGlow: accGlowColor(),
    };
  }

  let _curFrame = 'base';
  // Escala a la que se pinta el canvas principal. Es SC en el topbar y la que
  // pida la vitrina cuando el personaje se muda a otro contenedor. Antes estaba
  // clavada en SC: enganchado a una escena grande, seguía dibujándose de 32 px.
  let _stageScale = SC;
  function drawF(name) {
    if (!ctx) return;
    _curFrame = (name && POSES[name]) ? name : 'base';
    paintFrame(ctx, _curFrame, _stageScale, buildFxStyle());
    // try: pintarReflejo lee el estado del dock, que se declara más abajo en
    // el archivo; un pintado durante la evaluación no puede romper el motor.
    try { pintarReflejo(); } catch (e) {}
  }

  /*
   * El reflejo de Boberto en el piso del dock: un lienzo propio, colgado de la
   * línea del piso, al que se le copia cada cuadro dado vuelta.
   *
   * Era un -webkit-box-reflect sobre el canvas, y eso lo ataba a las
   * animaciones CSS del canvas: con la respiración (translateY + scaleY) y los
   * saltos de los gestos, el reflejo subía y bajaba con él, y una mancha que
   * se mueve debajo de la línea todo el tiempo molesta ("se mueve para arriba
   * y para abajo constantemente"). Un reflejo es del PISO: acá copia el dibujo
   * —la pose, el gesto, el vestuario— pero no el movimiento de la caja, porque
   * vive en un elemento que no se anima. Copiar un lienzo de 90 px por cuadro
   * es un drawImage; ningún costo que se note.
   *
   * Sólo en el dock: prestado a una vitrina no hay piso.
   */
  function pintarReflejo() {
    if (!_dock || _staged || !canvas || wrap.parentNode !== _dock) return;
    const r = _dock.querySelector(':scope > .plt-dock-piso > .plt-dock-reflejo');
    if (!r) return;
    if (r.width !== canvas.width || r.height !== canvas.height) { r.width = canvas.width; r.height = canvas.height; }
    if (r.style.width !== canvas.style.width) r.style.width = canvas.style.width;
    if (r.style.height !== canvas.style.height) r.style.height = canvas.style.height;
    const c = r.getContext('2d');
    if (!c) return;
    c.setTransform(1, 0, 0, 1, 0, 0);
    c.clearRect(0, 0, r.width, r.height);
    // Espejo vertical; y horizontal también si el wrap está dado vuelta (.fl),
    // porque ese giro es CSS del wrap y el reflejo no lo hereda.
    const fl = wrap.classList.contains('fl');
    c.setTransform(fl ? -1 : 1, 0, 0, -1, fl ? r.width : 0, r.height);
    c.drawImage(canvas, 0, 0);
    c.setTransform(1, 0, 0, 1, 0, 0);
  }

  // ── Ticker de efectos de glow ──────────────────────────────────────────
  // Anima multiplicadores, matices RGB y el drop-shadow del canvas mientras
  // haya algún efecto activo. Se apaga solo cuando no queda ninguno.
  let _fxRAF = null;
  function fxAnyActive() { return fxBody.length > 0 || fxAcc.length > 0; }
  /**
   * Cuanto agrandar el halo segun el tamano al que se dibuja Boberto.
   *
   * El halo eran 4 px FIJOS. En el topbar, sobre un personaje de 36 px de alto,
   * eso es un 11% y se ve; en una peana de 148 px es un 2,7% y es invisible. De
   * ahi la sensacion de que el color del brillo no hacia nada: si cambiaba, pero
   * sobre un halo tan fino que no se percibia.
   *
   * El factor se acota en 2,5 y no crece libre: el efecto 'halo' ya llega a 26 px
   * de blur por si mismo, y multiplicarlo por cuatro convierte el aura en un
   * manchon que se come al personaje.
   */
  function escalaHalo(escala) {
    return _clamp((escala || SC) / SC, 1, 2.5);
  }
  function staticBodyFilter(escala) {
    // Un personaje no lleva el brillo elegido para el cuerpo. El filtro es del
    // CANVAS entero, así que hay que apagarlo acá: el halo propio de un
    // personaje (Gojo) lo pinta paintFrame, que es lo único que viaja también
    // a renderPose y a las vistas previas.
    if (charActual()) return 'none';
    return `drop-shadow(0 0 ${(4 * escalaHalo(escala)).toFixed(1)}px ${bodyGlowColorA()})`;
  }
  /**
   * El drop-shadow del cuerpo para un multiplicador `m`.
   *
   * El rango de blur es lo que hace que los efectos se distingan, y estaba
   * demasiado angosto: `2 + 7*m` con m entre 0.4 y 1.35 daba SIEMPRE entre 5 y
   * 11 px. Cuatro efectos con nombres bien distintos y cuatro halos
   * practicamente iguales. Ahora va de ~2 a ~26 px.
   *
   * Y arriba de m = 1.15 se suma una segunda capa mas ancha y tenue: es lo que
   * convierte un borde brillante en un HALO de verdad, que es justamente lo que
   * el efecto 'halo' promete y no cumplia.
   */
  function bodyFilter(m, escala) {
    if (m == null || charActual()) return staticBodyFilter(escala);
    const col = bodyGlowColor();
    const k = escalaHalo(escala);
    // Numero y no el string ya formateado: `blur` salia de un toFixed() y la
    // segunda capa lo multiplicaba, o sea multiplicaba un string. Andaba por
    // coercion de JS, que es exactamente el tipo de cosa que se rompe sola.
    const blur = (1.5 + 11.5 * m) * k;
    let f = `drop-shadow(0 0 ${blur.toFixed(1)}px ${col})`;
    if (m > 1.15) f += ` drop-shadow(0 0 ${(blur * 1.9).toFixed(1)}px ${col}55)`;
    return f;
  }
  function fxStep(ts) {
    if (!fxAnyActive() || !enabled) {
      _fxRAF = null;
      _bodyM = 1; _accM = 1;
      if (canvas) canvas.style.filter = staticBodyFilter();
      if (ctx && !isBusy && !paused) drawF(_curFrame);
      paintPreview(null);
      pintarEspejosFx(null);
      return;
    }
    if (_fxHas(fxBody, 'rgb')) _bodyHue = (ts * 0.06) % 360;
    if (_fxHas(fxAcc, 'rgb'))  _accHue  = (ts * 0.06) % 360;
    const mb = fxMul(fxBody, ts);
    const ma = fxMul(fxAcc, ts);
    _bodyM = mb == null ? 1 : mb;
    _accM  = ma == null ? 1 : ma;
    if (canvas) canvas.style.filter = bodyFilter(mb);
    // No redibujar encima de un gesto en curso ni durante hover (paused).
    if (ctx && !isBusy && !paused) drawF(_curFrame);
    paintPreview(mb);
    // Las peanas y el avatar del chat tambien. Faltaba, y era el lugar donde
    // mas se nota: ahi Boberto se ve grande, asi que es donde uno mira el
    // brillo — y era justo donde no pasaba nada.
    pintarEspejosFx(mb);
    _fxRAF = requestAnimationFrame(fxStep);
  }
  function startFxTicker() {
    if (_fxRAF || !fxAnyActive()) return;
    _fxRAF = requestAnimationFrame(fxStep);
  }
  /**
   * Aplica el estado de FX del instante a todos los espejos vivos.
   *
   * Se declara antes de _espejos a proposito: esto corre dentro del ticker, o
   * sea siempre despues de que el archivo termino de evaluarse, asi que la
   * referencia ya existe. Se lo llama con `null` cuando no hay efectos.
   */
  function pintarEspejosFx(mb) {
    if (!_espejos || !_espejos.size) return;
    // El filtro se calcula POR ESPEJO y no una vez para todos: el halo escala
    // con el tamano al que se dibuja el personaje, y un espejo de peana mide
    // cuatro veces el del topbar.
    _espejos.forEach((esp) => {
      if (!esp.activo || !esp.cnv) return;
      esp.cnv.style.filter = filtroEspejo(esp, mb);
      // Y se redibuja: el cuerpo y el accesorio leen el FX del instante
      // (buildFxStyle), asi que sin repintar el pulso del accesorio y el ciclo
      // RGB se quedarian congelados aunque el halo lata.
      //
      // Con la pose que el espejo tiene PUESTA, no con 'base' ni con la mirada:
      // el bucle puede estar a mitad de un gesto y repintar otra cosa se lo
      // comeria. `dibujarEn` ya anota la pose cuando dibuja una mirada, asi que
      // `esp.pose` es siempre lo que de verdad hay en pantalla.
      dibujarEn(esp, esp.pose || 'base');
    });
  }

  // Refresca el glow estático (color elegido) cuando no hay efectos animando.
  function refreshStaticGlow() {
    if (_fxRAF) return; // el ticker ya redibuja
    if (canvas) canvas.style.filter = staticBodyFilter();
    if (ctx && !isBusy && !paused) drawF(_curFrame);
    paintPreview(null);
    pintarEspejosFx(null);
  }

  // ── Preview animada (canvas externo del panel de tweaks) ───────────────
  let _prevCnv = null, _prevCtx = null, _prevScale = SC;
  // Vestuario fijo de la preview (ver vestuarioDe). null = el elegido.
  let _prevVest = null;
  // Pinta el frame base en la preview con el estado de FX vivo. `mb` es el
  // multiplicador de respiración del cuerpo (null = glow estático del cuerpo).
  function paintPreview(mb) {
    if (!_prevCnv || !_prevCtx) return;
    conVestuario(_prevVest, () => {
      paintFrame(_prevCtx, 'base', _prevScale, buildFxStyle());
      // Por el MISMO camino que el topbar y las peanas, con la escala de la
      // preview: tenia su propia copia de la formula, con los coeficientes viejos,
      // asi que mostraba un halo distinto del real.
      _prevCnv.style.filter = bodyFilter(mb, _prevScale);
    });
  }

  // ── Partículas livianas (spans DOM sobre el wrap, theme-aware) ──
  const PARTICLE_MAX = 14;

  /**
   * Emite una partícula dentro de `host`.
   *
   * Las coordenadas que traen las coreografías están en el espacio del TOPBAR
   * (un cuadro de BOX.W×BOX.H a SC px por unidad) y no se reescriben: quien
   * hospeda partículas a otra escala escala su capa entera (ver `capaFx`). Eso
   * es lo que permite que todas las coreografías corran tal cual sobre
   * una peana de 148 px sin tocar un solo número.
   *
   * El tope es POR HOST y se lleva en el propio elemento. Era una única cuenta
   * global, y con el topbar más varias peanas emitiendo a la vez catorce se
   * agotaban entre todos: la primera peana en emitir dejaba a las demás mudas.
   */
  function emitirEn(host, kind, opts) {
    if (!host) return;
    if ((host._pltPt || 0) >= PARTICLE_MAX) return;
    const s = document.createElement('span');
    s.className = 'plt-particle plt-pt-' + kind;
    s.textContent = (opts && opts.text) || '';
    const startX = (opts && opts.x != null) ? opts.x : (MW * SC) / 2;
    s.style.left = startX + 'px';
    s.style.top  = ((opts && opts.y != null) ? opts.y : 4) + 'px';
    s.style.setProperty('--pt-dx', ((opts && opts.dx) || 0) + 'px');
    s.style.setProperty('--pt-dy', ((opts && opts.dy != null) ? opts.dy : 4) + 'px');
    if (opts && opts.rot != null) s.style.setProperty('--pt-rot', opts.rot + 'deg');
    if (opts && opts.scale != null) s.style.setProperty('--pt-scale', opts.scale);
    host.appendChild(s);
    host._pltPt = (host._pltPt || 0) + 1;
    const dur = (opts && opts.dur) || 1100;
    s.style.setProperty('--pt-dur', dur + 'ms');
    requestAnimationFrame(() => s.classList.add('go'));
    setTimeout(() => { s.remove(); host._pltPt = Math.max(0, (host._pltPt || 1) - 1); }, dur);
  }

  /**
   * Ráfaga: `n` partículas escalonadas. `host` puede ser una función para que
   * se resuelva en el momento de emitir —la capa de un espejo se reubica cuando
   * cambia la escala, así que capturarla al arrancar la ráfaga la dejaría
   * emitiendo en la posición vieja.
   */
  function burstEn(host, kind, n, optsFn) {
    for (let i = 0; i < n; i++) {
      setTimeout(() => emitirEn(typeof host === 'function' ? host() : host, kind, optsFn ? optsFn(i) : {}), i * 110);
    }
  }

  // El topbar: mismo camino, con su propia condición de apagado. `enabled` es la
  // mascota del topbar, y no vale para un espejo —una peana la pide explícitamente
  // la app que la monta, igual que la vitrina.
  function emitParticle(kind, opts) {
    if (!wrap || !enabled) return;
    emitirEn(hostPropio(), kind, opts);
  }
  function burstParticles(kind, n, optsFn) {
    if (!wrap || !enabled) return;
    burstEn(hostPropio, kind, n, optsFn);
  }

  /*
   * Dónde emite el Boberto propio cuando está en el DOCK.
   *
   * Las coreografías emiten a SC; el dock lo dibuja a la escala de su slot
   * (2,4 colapsado, 4 o más expandido). Emitiendo directo en el wrap, una nota
   * que sale "de la guitarra" salía de donde estaría la guitarra a escala 2:
   * a escala 4 eso es medio personaje de distancia. Es el mismo problema que
   * ya tenían los espejos, y se resuelve igual —con su capa escalada
   * (capaFx)— en vez de una segunda cuenta.
   *
   * Sólo en el dock: en una vitrina el wrap sigue emitiendo como siempre, que
   * es lo que la landing y el estudio ya tienen revisado a ojo.
   */
  const _espPropio = { activo: true, cnv: null, escala: SC, capa: null, padreFijado: false };
  function hostPropio() {
    if (_staged || !canvas || _stageScale === SC || !_dock || wrap.parentNode !== _dock) return wrap;
    _espPropio.cnv = canvas;
    _espPropio.escala = _stageScale;
    return capaFx(_espPropio) || wrap;
  }

  // ── Preview (para el panel de tweaks) ──────────────────────────────────
  // Dibuja el frame base con la paleta actual en un canvas externo, a la
  // escala pedida. No depende del estado de la mascota en el topbar.
  // Registra el canvas de preview y lo deja animando con los efectos vivos.
  // Si hay efectos activos, el ticker se encarga de animar ambos (topbar +
  // preview); si no, pinta un cuadro estático.
  // `opciones` acepta {character} y {hat}, igual que renderPose: sirve para
  // mostrar un personaje antes de elegirlo. Sin opciones, el elegido.
  function renderPreview(cnv, scale, opciones) {
    if (!cnv) return;
    const s = scale || SC;
    _prevScale = s;
    _prevVest = vestuarioDe(opciones);
    if (_prevCnv !== cnv) {
      _prevCnv = cnv;
      _prevCtx = cnv.getContext('2d');
    }
    // Se redimensiona SIEMPRE, no sólo al enganchar el canvas: el panel puede
    // abrirse arrastrado a otra pantalla y ahí cambia el devicePixelRatio.
    fitCanvas(cnv, s);
    // Sincroniza la paleta y los colores del accesorio con el skin elegido
    // antes de pintar: la preview se abre sin pasar por el loop del topbar.
    pal = getPal(); resolveHatColors();
    // Se pinta SIEMPRE un cuadro acá, y recién después arranca el ticker.
    // Delegarle el primer cuadro al ticker deja la preview en blanco hasta el
    // primer requestAnimationFrame — y si algo lo demora (una pestaña de fondo,
    // un panel que no compone cuadros) queda en blanco y punto.
    // Cada multiplicador vuelve a 1 si su propio target no tiene efectos: si se
    // resetearan juntos, apagar el glow del cuerpo dejaría al accesorio
    // congelado en el valle de su último pulso.
    if (fxBody.length === 0) _bodyM = 1;
    if (fxAcc.length === 0)  _accM = 1;
    paintPreview(fxBody.length > 0 ? _bodyM : null);
    if (fxAnyActive()) startFxTicker();
  }
  // Detiene/desvincula la preview (al cerrar el panel).
  function stopPreview() { _prevCnv = null; _prevCtx = null; }

  // ── Utils ─────────────────────────────────────────────────────────────
  const wait = ms => new Promise(r => setTimeout(r, ms));
  const rand = (a, b) => Math.floor(Math.random() * (b - a + 1)) + a;

  // ── Hacia dónde mira ──────────────────────────────────────────────────
  /**
   * A que lado mira: 'fr' (derecha) o 'fl' (izquierda, espejado).
   *
   * Se hace con classList y NO con `wrap.className = 'fr'`, que es como estaba
   * y borraba cualquier OTRA clase del contenedor. Hay una que importa:
   * `plt-vitrina`, la que marca que esta prestado a una escena. Pisarla a mitad
   * de un gesto lo devolvia al estilo del dock dentro de la escena.
   *
   * Ya no hay posicion que mover: en el dock no camina (ver "Dock"). Lo que
   * habia aca —getBounds, moveTo, walkTo, las piernas— servia solo al paseo
   * por el topbar y se fue con el.
   */
  function mirarA(lado) {
    wrap.classList.remove('fl', 'fr');
    wrap.classList.add(lado === 'fl' ? 'fl' : 'fr');
  }

  /* == Coreografías ======================================================
   *
   * Las coreografías del personaje, parametrizadas sobre un
   * ACTOR: quién dibuja, en qué elemento van las clases CSS y dónde se cuelgan
   * las partículas.
   *
   * Existe así porque el personaje tiene que poder actuar en dos lados a la vez:
   * el Boberto del topbar y cada peana, que es un espejo con su propio canvas.
   * Antes esto estaba atado al singleton por variables libres, así que una peana
   * no podía correr un gesto —y de ahí que tuviera su propio repertorio escrito
   * a mano, con seis de veinticuatro animaciones. La tentación era copiar las
   * otras dieciocho al bucle del espejo; sería la tercera copia de la misma
   * coreografía y se separa sola.
   *
   * Los nombres que las coreografías ya usaban se SOMBREAN con los del actor, así
   * que el cuerpo de cada gesto quedó intacto: lo que cambió es a qué apuntan,
   * no lo que hacen.
   *
   * Ningún actor se desplaza: el del dock vive en un slot de la sidebar y un
   * espejo en una caja de 150 px. Por eso el skate hace el ollie y rueda en el
   * lugar. (Existía `desliza`, que lo hacía recorrer la zona de caminata del
   * topbar; se fue con el paseo.)
   * -------------------------------------------------------------------- */
  function coreografias(A) {
    const drawF          = A.draw;
    const canvas         = A.el;
    const emitParticle   = A.emit;
    const burstParticles = A.burst;
    const mirarA         = A.mirar || (() => {});

    // Reinicia una clase de animación del canvas y devuelve su seguro de
    // reloj, que la saca a los `ms` aunque el gesto no llegue a hacerlo. Para
    // una clase de una sola vez, el seguro ES su final: dura lo que su
    // animación y un poco más. Sin cuadros (pestaña de fondo) no corre la
    // animación, pero el reloj sí, así que la clase nunca queda puesta.
    const reinicia = (clase, ms) => {
      canvas.classList.remove(clase); void canvas.offsetWidth; canvas.classList.add(clase);
      return setTimeout(() => canvas.classList.remove(clase), ms);
    };

    // ── Gestos simples ────────────────────────────────────────────────────
    async function g_blink() {
      drawF('blink'); await wait(75); drawF('base');
    }
    async function g_doubleBlink() {
      drawF('blink'); await wait(70); drawF('base');
      await wait(rand(160, 280));
      drawF('blink'); await wait(70); drawF('base');
    }
    async function g_wink() {
      drawF('wink'); await wait(220); drawF('blink'); await wait(70); drawF('base');
    }
    async function g_lookL() {
      drawF('lookL'); await wait(330); drawF('blink'); await wait(70); drawF('base');
    }
    async function g_lookR() {
      drawF('lookR'); await wait(330); drawF('blink'); await wait(70); drawF('base');
    }
    async function g_happy() {
      drawF('happyBlink'); await wait(85); drawF('happy'); await wait(270); drawF('base');
    }

    /* ── Gesto: LAPTOP (abre la laptop, programa, lee y se estira) ────────
     *
     * Con la receta de la guitarra. Antes la laptop aparecía abierta, sin
     * brazos, y alternaba dos cuadros con fragmentos de código de texto que
     * se iban hasta 35 px. Ahora:
     *
     * - Entrada: la laptop cerrada sobre las piernas (con la calcomanía a la
     *   vista), la abre y la pantalla arranca.
     * - Desarrollo: dos ráfagas de tipeo con las manos turnándose y el
     *   código escribiéndose en la pantalla, separadas por una pausa en la
     *   que se acerca a leer con la luz de la pantalla en la cara. Mientras
     *   tipea se mece apenas (`plt-tipea`, la clase en loop del gesto).
     * - Remate: el aviso verde en la pantalla y se estira con las manos
     *   arriba; después la cierra.
     *
     * Las partículas son barritas de código (cajas, no texto) que suben de
     * la pantalla y se corren poco: dentro del riel de Buenas prácticas.
     */
    async function g_laptop() {
      if (!canvas) return;
      canvas.classList.remove('plt-idle');
      // Desde el canto de la tapa, que queda cerca de y = 29 en la capa.
      const bit = () => emitParticle('bit', {
        x: 20 + rand(-6, 6), y: 28, dx: rand(-8, 8), dy: -rand(9, 14), dur: 900, scale: [0.6, 1, 1.4][rand(0, 2)],
      });
      const tipeo = ['laptopType1', 'laptopType2', 'laptopTipea3', 'laptopTipea4'];
      let golpe = 0;
      const rafaga = async (n) => {
        for (let i = 0; i < n; i++, golpe++) {
          drawF(tipeo[golpe % tipeo.length]);
          if (golpe % 2 === 0) bit();
          await wait(rand(110, 150));
        }
      };
      let seguro = null;
      try {
        drawF('laptopCierra'); await wait(280);
        drawF('laptopAbre'); await wait(160);
        drawF('laptopOpen'); await wait(320);
        seguro = reinicia('plt-tipea', 2400);
        await rafaga(8);
        canvas.classList.remove('plt-tipea');
        clearTimeout(seguro);
        drawF('laptopLee'); await wait(560);
        seguro = reinicia('plt-tipea', 2000);
        await rafaga(6);
        canvas.classList.remove('plt-tipea');
        drawF('laptopListo');
        // Desde el aviso verde, que queda cerca de (20, 32) en la capa.
        emitParticle('chispa', { x: 24, y: 29, dx: 5, dy: -8, dur: 620, rot: 90 });
        await wait(620);
        drawF('laptopCierraFin'); await wait(260);
      } finally {
        clearTimeout(seguro);
        canvas.classList.remove('plt-tipea');
      }
      drawF('happyBlink'); await wait(85);
      drawF('happy');      await wait(360);
      drawF('blink');      await wait(70);
      drawF('base');
    }

    /* ── Gesto: BURBUJA (sopla una burbuja, la mira subir y revienta) ──────
     *
     * Con la receta de la guitarra. Antes la burbuja aparecía de la nada al
     * costado de la cara, crecía en tres saltos y se rompía, sin aro ni
     * soplido. Ahora:
     *
     * - Entrada: saca el aro y se lo lleva a la boca.
     * - Desarrollo: sopla y la burbuja crece pegada al aro en tres tiempos,
     *   con un par de burbujitas que se escapan; el cuerpo se infla y se
     *   desinfla con cada soplido (`plt-sopla`, la clase en loop del gesto).
     *   Después se suelta y la sigue con la mirada mientras sube.
     * - Remate: revienta, con un saltito de sorpresa (`plt-brinco`), un
     *   anillo de salpicadura y gotitas que se apagan.
     *
     * Las partículas se emiten en coordenadas de "mirando a la derecha" (por
     * eso el `mirarA('fr')`) y en px de la capa del topbar: el aro queda
     * cerca de (23, 25) y la burbuja revienta cerca de (32, 13). Los
     * recorridos son cortos, para que escalados en el riel de Buenas
     * prácticas queden dentro de la peana.
     */
    async function g_bubble() {
      mirarA('fr');
      if (!canvas) return;
      canvas.classList.remove('plt-idle');
      const burbujita = () => emitParticle('burbujita', {
        x: 26 + rand(-1, 1), y: 22, dx: rand(3, 6), dy: -rand(8, 11), dur: 950, scale: [0.8, 1, 1.3][rand(0, 2)],
      });
      let seguro = null, seguroBrinco = null;
      try {
        drawF('burbujaSaca'); await wait(260);
        drawF('burbujaSopla'); await wait(220);
        seguro = reinicia('plt-sopla', 2600);
        drawF('bubbleS'); burbujita(); await wait(380);
        drawF('bubbleM'); await wait(420);
        burbujita();
        drawF('bubbleL'); await wait(560);
        canvas.classList.remove('plt-sopla');
        drawF('burbujaSuelta'); await wait(380);
        drawF('burbujaSube'); await wait(420);
        // Revienta: el anillo y las gotitas salen del centro de la burbuja.
        seguroBrinco = reinicia('plt-brinco', 380);
        drawF('bubblePop');
        emitParticle('salpica', { x: 32, y: 13, dur: 450 });
        for (let i = 0; i < 6; i++) {
          const a = (i / 6) * Math.PI * 2 + 0.3, d = rand(6, 8);
          emitParticle('gota', { x: 32, y: 13, dx: Math.cos(a) * d, dy: Math.sin(a) * d + 2, dur: 560 });
        }
        await wait(480);
      } finally {
        clearTimeout(seguro);
        clearTimeout(seguroBrinco);
        canvas.classList.remove('plt-sopla', 'plt-brinco');
      }
      drawF('happyBlink'); await wait(85); drawF('happy'); await wait(280); drawF('base');
    }

    /* ── Gesto: CAFÉ (sopla, lo huele, tres sorbos y un brindis) ───────────
     *
     * Con la receta de la guitarra y el skate. Antes eran dos cuadros (el
     * jarro al costado y el mismo jarro un poco más arriba) con tres "~" de
     * texto que salían del borde del lienzo y no del jarro. Ahora:
     *
     * - Entrada: agarra el jarro con las dos manos y sopla dos veces; el
     *   soplido empuja el vapor hacia afuera.
     * - Desarrollo: se inclina a olerlo con los ojos cerrados y el vapor le
     *   sube a la cara; después tres sorbos, cada uno más corto, con el jarro
     *   volcado contra la boca y la cabeza hacia atrás.
     * - Remate: un "ahh" con una chispa de energía y el jarro en alto, como en
     *   un brindis, con un saltito (`plt-pop`).
     *
     * El vaivén es UNA clase en loop (`plt-cafecito`) para todo el gesto, con
     * seguro de reloj, igual que la guitarra. Las partículas se emiten en
     * coordenadas de "mirando a la derecha" (por eso el `mirarA('fr')`) y en
     * px de la capa del topbar: el jarro en reposo tiene la boca cerca de
     * (29, 24). Los recorridos son cortos para que en el riel de Buenas
     * prácticas, escalados por '--pt-kf', queden dentro de la peana.
     */
    async function g_coffee() {
      mirarA('fr');
      if (!canvas) return;
      canvas.classList.remove('plt-idle');
      // Una voluta de vapor desde la boca del jarro: sube, ondula y se apaga.
      const vapor = (x, y, deriva) => emitParticle('vapor', {
        x: x + rand(-1, 1), y: y, dx: deriva + rand(-2, 2), dy: -rand(12, 16), dur: 1150,
      });
      // El soplido: una bocanada chica desde la boca hacia el jarro.
      const soplido = () => emitParticle('polvo', { x: 22, y: 26, dx: rand(4, 6), dy: -1, dur: 420 });

      canvas.classList.remove('plt-cafecito'); void canvas.offsetWidth; canvas.classList.add('plt-cafecito');
      const seguro = setTimeout(() => canvas.classList.remove('plt-cafecito'), 7000);
      let seguroPop = null;
      try {
        // Lo agarra: el vapor ya está subiendo.
        drawF('coffeeHold'); vapor(29, 24, 0); await wait(300);
        // Sopla dos veces; el vapor se va para afuera.
        for (let i = 0; i < 2; i++) {
          drawF('cafeSopla'); soplido(); vapor(28, 25, 5); await wait(210);
          drawF('cafeSopla2'); await wait(170);
        }
        // Lo huele: se inclina y el vapor le sube a la cara.
        drawF('cafeHuele'); vapor(27, 27, -3); await wait(260);
        vapor(26, 27, -4); await wait(380);
        // Tres sorbos, cada uno más corto.
        const sorbos = [[240, 380, 260], [200, 320, 220], [180, 280, 200]];
        for (const [ms1, ms2, ms3] of sorbos) {
          drawF('coffeeSip'); await wait(ms1);
          drawF('cafeSorbo'); await wait(ms2);
          drawF('cafeTraga'); await wait(ms3);
        }
        // "Ahh", con una chispa de energía.
        drawF('cafeAhh');
        emitParticle('shine', { text: '✦', x: 31, y: 9, dx: 5, dy: -9, dur: 800, rot: 90 });
        emitParticle('shine', { text: '✧', x: 11, y: 11, dx: -4, dy: -8, dur: 800, rot: -90 });
        await wait(520);
        // Remate: el jarro en alto. El saltito reemplaza al vaivén.
        canvas.classList.remove('plt-cafecito');
        drawF('cafeRemate');
        canvas.classList.remove('plt-pop'); void canvas.offsetWidth; canvas.classList.add('plt-pop');
        seguroPop = setTimeout(() => canvas.classList.remove('plt-pop'), 400);
        await wait(620);
      } finally {
        clearTimeout(seguro);
        clearTimeout(seguroPop);
        canvas.classList.remove('plt-cafecito', 'plt-pop');
      }
      drawF('happyBlink'); await wait(85); drawF('happy'); await wait(260); drawF('base');
    }

    /* ── Gesto: SCAN (analiza el Sprint: visor, dos pasadas y visto bueno) ──
     *
     * Con la receta de la guitarra. Antes eran cinco cuadros de una banda
     * del color del tema, dos veces, y un parpadeo de `scan4` con `base`.
     * Ahora:
     *
     * - Entrada: entrecierra los ojos y aparece el visor (las esquinas
     *   crecen hasta enmarcar el cuerpo).
     * - Desarrollo: el haz baja y sube, con la mirada siguiéndolo y la
     *   rejilla marcando lo ya barrido; en cada extremo del haz se escapan
     *   barritas de datos. El cuerpo zumba apenas (`plt-escanea`, la clase en
     *   loop del gesto).
     * - Remate: el visto bueno en el marco, un saltito (`plt-brinco`) y una
     *   chispa.
     *
     * Las barritas son las de la laptop (`bit`, cajas monocromas) y salen
     * desde los costados del haz con recorridos cortos; la chispa sale del
     * visto bueno, cerca de (32, 12) en la capa del topbar. Coordenadas de
     * "mirando a la derecha", por eso el `mirarA('fr')`.
     */
    async function g_scan() {
      mirarA('fr');
      if (!canvas) return;
      canvas.classList.remove('plt-idle');
      // `y`: la altura del haz en la capa del topbar.
      const datos = (y) => {
        emitParticle('bit', { x: 4, y, dx: -2, dy: -3, dur: 520, scale: 0.8 });
        emitParticle('bit', { x: 36, y, dx: 2, dy: -3, dur: 520, scale: 0.8 });
      };
      const alto = [9.5, 14, 18.5, 23, 27.5];
      let seguro = null, seguroBrinco = null;
      try {
        drawF('escaneoVisor'); await wait(280);
        seguro = reinicia('plt-escanea', 3200);
        for (const orden of [[0, 1, 2, 3, 4], [4, 3, 2, 1, 0]]) {
          for (const i of orden) {
            drawF('scan' + i);
            if (i % 2 === 0) datos(alto[i]);
            await wait(120);
          }
          await wait(90);
        }
        canvas.classList.remove('plt-escanea');
        seguroBrinco = reinicia('plt-brinco', 380);
        drawF('escaneoListo');
        emitParticle('chispa', { x: 32, y: 11, dx: 3, dy: -5, dur: 560, rot: 90 });
        await wait(560);
      } finally {
        clearTimeout(seguro);
        clearTimeout(seguroBrinco);
        canvas.classList.remove('plt-escanea', 'plt-brinco');
      }
      drawF('happyBlink'); await wait(85); drawF('happy'); await wait(300); drawF('base');
    }

    /* ── Gesto: ASIENTE (sí, sí, sí y el pulgar arriba) ────────────────────
     *
     * Es una respuesta corta, del tamaño de un microgesto, así que la receta
     * va apretada:
     *
     * - Entrada y desarrollo: tres cabezazos firmes, la cabeza abajo en cada
     *   "sí" (`plt-asiente`, la clase en loop del gesto, al mismo ritmo).
     * - Remate: el pulgar arriba con un guiño y una chispa en la punta.
     *
     * Antes alternaba `lookU` y `base`: subían los ojos y no la cabeza.
     */
    async function g_nod() {
      mirarA('fr');
      if (!canvas) return;
      canvas.classList.remove('plt-idle');
      let seguro = null;
      try {
        seguro = reinicia('plt-asiente', 1400);
        for (let i = 0; i < 3; i++) {
          drawF('asienteArriba'); await wait(150);
          drawF('asienteAbajo');  await wait(170);
        }
        canvas.classList.remove('plt-asiente');
        // El pulgar queda cerca de (33, 18) en la capa del topbar.
        drawF('asientePulgar');
        emitParticle('chispa', { x: 33, y: 17, dx: 3, dy: -5, dur: 560, rot: 90 });
        await wait(480);
      } finally {
        clearTimeout(seguro);
        canvas.classList.remove('plt-asiente');
      }
      drawF('happy'); await wait(200); drawF('base');
    }

    /* ── Gesto: SALUDA (levanta la mano abierta y la agita) ───────────────
     *
     * Con la receta de la guitarra. Antes alternaba el muñón asomado detrás
     * de la cabeza con `base`, a 1,2 s y sin entrada ni remate. Ahora:
     *
     * - Entrada: un saltito (`plt-brinco`) mientras levanta la mano.
     * - Desarrollo: tres idas y vueltas de la muñeca, con el cuerpo meciéndose
     *   apenas (`plt-saludo`, una sola clase en loop, en porcentaje para que
     *   se lea igual en una peana) y la palma de frente al final.
     * - Remate: baja la mano con un guiño.
     *
     * Es el gesto con el que la guía de Buenas prácticas recibe al elegir
     * plataforma y el del login de JiraToolkit: lo primero que se ve.
     */
    async function g_wave() {
      if (!canvas) return;
      canvas.classList.remove('plt-idle');
      let seguroBrinco = null, seguro = null;
      try {
        seguroBrinco = reinicia('plt-brinco', 380);
        drawF('saludoSube'); await wait(190);
        seguro = reinicia('plt-saludo', 2400);
        for (let i = 0; i < 3; i++) {
          drawF('waveUp');   await wait(165);
          drawF('waveDown'); await wait(165);
        }
        drawF('saludoPalma'); await wait(230);
        canvas.classList.remove('plt-saludo');
        drawF('saludoBaja'); await wait(320);
      } finally {
        clearTimeout(seguroBrinco);
        clearTimeout(seguro);
        canvas.classList.remove('plt-brinco', 'plt-saludo');
      }
      drawF('happyBlink'); await wait(85); drawF('happy'); await wait(240); drawF('base');
    }

    /* ── Gesto: CURIOSIDAD (ladea la cabeza, se rasca y cae en la cuenta) ──
     *
     * Con la receta de la guitarra. Antes eran tres miradas seguidas con un
     * "?" de texto que se iba 42 px a la derecha, fuera del riel de Buenas
     * prácticas. Ahora:
     *
     * - Entrada: ladea la cabeza (`plt-curioso`, la clase en loop del gesto)
     *   y mira a un lado y al otro.
     * - Desarrollo: aparece el "?" sobre la cabeza y se rasca.
     * - Remate: "¡oh!", con los ojos bien abiertos, un saltito
     *   (`plt-brinco`) y una chispa.
     *
     * El "?" es un glifo monocromo que aparece de golpe, se sostiene y se
     * apaga casi en el lugar; se emite en coordenadas de "mirando a la
     * derecha" (por eso el `mirarA('fr')`), arriba de la mano que rasca.
     */
    async function g_curiosity() {
      mirarA('fr');
      if (!canvas) return;
      canvas.classList.remove('plt-idle');
      let seguro = null, seguroBrinco = null;
      try {
        seguro = reinicia('plt-curioso', 3000);
        drawF('curiosoMira'); await wait(320);
        drawF('curiosoMira2'); await wait(320);
        drawF('curiosoMira'); await wait(200);
        emitParticle('pregunta', { text: '?', x: 31, y: 2, dx: 1, dy: -2, dur: 1300 });
        for (let i = 0; i < 3; i++) {
          drawF('curiosoRasca'); await wait(150);
          drawF('curiosoRasca2'); await wait(150);
        }
        canvas.classList.remove('plt-curioso');
        seguroBrinco = reinicia('plt-brinco', 380);
        drawF('curiosoOh');
        emitParticle('chispa', { x: 30, y: 9, dx: 4, dy: -5, dur: 560, rot: 90 });
        await wait(480);
      } finally {
        clearTimeout(seguro);
        clearTimeout(seguroBrinco);
        canvas.classList.remove('plt-curioso', 'plt-brinco');
      }
      drawF('happyBlink'); await wait(85); drawF('happy'); await wait(220); drawF('base');
    }

    /* ── Gesto: PIENSA (la mano en el mentón, "…" y "¡ajá!") ─────────────
     *
     * Con la receta de la guitarra. Antes era la cara achinada con tres
     * puntos que salían volando hacia los costados (32 px cada uno). Ahora:
     *
     * - Entrada: se lleva la mano al mentón.
     * - Desarrollo: la mirada se va hacia arriba y vuelve, el índice golpea
     *   la mejilla, y los tres puntos aparecen de a uno, cada uno un poco más
     *   grande y más arriba, del lado al que mira. Duran distinto para
     *   apagarse juntos. El cuerpo se mece despacio (`plt-piensa`, la clase
     *   en loop del gesto).
     * - Remate: "¡ajá!", el índice arriba con un saltito (`plt-brinco`) y una
     *   chispa en la punta del dedo.
     *
     * Los puntos son círculos de color del tema (ver .plt-pt-dot) y se corren
     * apenas: quedan dentro del riel de Buenas prácticas.
     */
    async function g_think() {
      if (!canvas) return;
      canvas.classList.remove('plt-idle');
      // `n`: 0, 1 o 2. Hacia arriba a la izquierda, que es para donde mira.
      const punto = (n) => emitParticle('dot', {
        x: 15 - n * 4.5, y: 9 - n * 3, dx: -1, dy: -3, scale: 0.8 + n * 0.25, dur: 1500 - n * 240,
      });
      let seguro = null, seguroBrinco = null;
      try {
        seguro = reinicia('plt-piensa', 3200);
        drawF('think'); await wait(260);
        drawF('piensaMira'); punto(0); await wait(240);
        drawF('piensaMira2'); punto(1); await wait(240);
        drawF('think'); punto(2); await wait(260);
        drawF('piensaMira2'); await wait(200);
        drawF('think'); await wait(320);
        canvas.classList.remove('plt-piensa');
        // ¡Ajá! La punta del dedo queda cerca de (14.5, 4.6) del dibujo.
        seguroBrinco = reinicia('plt-brinco', 380);
        drawF('piensaAja');
        emitParticle('chispa', { x: 33, y: 13, dx: 4, dy: -7, dur: 600, rot: 90 });
        await wait(520);
      } finally {
        clearTimeout(seguro);
        clearTimeout(seguroBrinco);
        canvas.classList.remove('plt-piensa', 'plt-brinco');
      }
      drawF('happyBlink'); await wait(85); drawF('happy'); await wait(300); drawF('base');
    }

    /* ── Gesto: FESTEJA (se agacha, salta, tira el puño y llueven estrellas)
     *
     * Con la receta de la guitarra. Antes era `happy` quieto con un aplastado
     * en px y seis chispas de texto que volaban hasta 78 px. Ahora:
     *
     * - Entrada: se agacha con los puños abajo y salta con los dos brazos
     *   arriba (`plt-salto`, de una sola vez, mueve el canvas); de los puños
     *   saltan dos chispas.
     * - Desarrollo: cae con los brazos abiertos y tira el puño dos veces,
     *   rebotando de alegría (`plt-festejo`, la clase en loop del gesto).
     * - Remate: los dos brazos arriba bajo una lluvia de estrellas, que es lo
     *   que antes era el gesto suelto de "estrellas".
     *
     * Las partículas son cajas (estrellas y chispas recortadas con
     * clip-path), no glifos: escalan limpio en una peana y no dependen de la
     * tipografía. Salen en px de la capa del topbar y se corren poco hacia
     * los costados (16 px como mucho): escaladas en el riel de Buenas
     * prácticas, quedan dentro de la peana.
     */
    async function g_celebrate() {
      if (!canvas) return;
      canvas.classList.remove('plt-idle');
      const chispa = (x, y, dx) => emitParticle('chispa', { x, y, dx, dy: -rand(6, 9), dur: 620, rot: rand(-90, 90) });
      let seguroSalto = null, seguro = null;
      try {
        drawF('festejaAgacha'); await wait(170);
        seguroSalto = reinicia('plt-salto', 560);
        drawF('festejaSalto');
        // Desde los puños en alto: (1.2, 4.35) y (14.8, 4.35) del dibujo.
        chispa(7, 12, -6); chispa(33, 12, 6);
        await wait(290);
        drawF('festejaCae'); await wait(160);
        seguro = reinicia('plt-festejo', 2600);
        for (let i = 0; i < 2; i++) {
          drawF('festejaPuno');  await wait(150);
          drawF('festejaPuno2'); await wait(140);
        }
        // Remate: la lluvia de estrellas, desde arriba de la cabeza.
        drawF('festejaRemate');
        burstParticles('estrella', 7, (i) => ({
          x: 20 + (i % 2 ? 1 : -1) * rand(2, 12), y: rand(1, 6),
          dx: (i % 2 ? 1 : -1) * rand(3, 16), dy: rand(14, 22), dur: rand(900, 1150), rot: rand(-200, 200),
        }));
        await wait(720);
      } finally {
        clearTimeout(seguroSalto);
        clearTimeout(seguro);
        canvas.classList.remove('plt-salto', 'plt-festejo');
      }
      drawF('happyBlink'); await wait(85); drawF('happy'); await wait(260); drawF('base');
    }

    /* ── Gesto: LEVITA (medita, se eleva, flota y aterriza) ─────────────────
     *
     * Rehecho como meditación. Antes era `happy` quieto con un saltito en px
     * y seis destellos de texto que volaban fuera del riel de Buenas
     * prácticas. Ahora:
     *
     * - Entrada: se acomoda con los pies recogidos y abre los brazos, y se
     *   eleva.
     * - Desarrollo: flota y respira (`plt-levita`, la clase en loop del
     *   gesto, que también hace la subida), con el resplandor de abajo
     *   creciendo en cada inspiración y unas chispas que suben de él.
     * - Remate: abre un ojo, a ver si está flotando de verdad, y aterriza
     *   suave (`plt-aterriza`, de una sola vez).
     *
     * Las chispas son las cajas monocromas de `chispa`, emitidas desde el
     * resplandor (cerca de y = 36 en la capa del topbar) con recorridos
     * cortos: quedan dentro de la peana.
     */
    async function g_levitate() {
      mirarA('fr');
      if (!canvas) return;
      canvas.classList.remove('plt-idle');
      const chispa = () => emitParticle('chispa', {
        x: 20 + rand(-9, 9), y: 35, dx: rand(-2, 2), dy: -rand(4, 7), dur: 700, rot: rand(-90, 90),
      });
      let seguro = null, seguroAterriza = null;
      try {
        drawF('levitaSienta'); await wait(380);
        seguro = reinicia('plt-levita', 5600);
        drawF('levitaOm'); await wait(520);
        for (let i = 0; i < 3; i++) {
          drawF('levitaInspira'); chispa(); await wait(700);
          drawF('levitaOm'); chispa(); await wait(700);
        }
        drawF('levitaEspia'); await wait(560);
        canvas.classList.remove('plt-levita');
        seguroAterriza = reinicia('plt-aterriza', 560);
        drawF('levitaAterriza'); await wait(520);
      } finally {
        clearTimeout(seguro);
        clearTimeout(seguroAterriza);
        canvas.classList.remove('plt-levita', 'plt-aterriza');
      }
      drawF('happyBlink'); await wait(85); drawF('happy'); await wait(240); drawF('base');
    }

    /* ── Gesto: SKATE (se sube, rema, se manda un kickflip y festeja) ──────
     *
     * Con la receta de la guitarra. Antes alternaba dos cuadros casi iguales
     * con un ollie al entrar y otro al salir, y las líneas de velocidad se
     * borraban de golpe a opacidad plena. Ahora:
     *
     * - Entrada: se sube de un salto y rema dos veces (el pie de atrás baja al
     *   piso y patea, con polvo en la rueda de atrás).
     * - Rodada: una sola clase en loop (`plt-rueda`: un vaivén chico con el
     *   cuerpo inclinado hacia adelante) para todo el gesto, con estelas que
     *   se desvanecen de verdad.
     * - Truco: se agacha, pop con la cola, kickflip en tres cuadros (gráfica,
     *   ruedas arriba, lija), la atrapa y cae agachado levantando polvo. El
     *   salto lo pone `plt-skatehop`, que mueve el canvas y no el dibujo.
     * - Remate: los dos brazos arriba.
     *
     * El mismo gesto para el dock, la vitrina y los espejos: en los tres rueda
     * en el lugar, que es lo honesto en una caja sin pista.
     *
     * Todo se mide con `wait()` y no con requestAnimationFrame: en una pestaña
     * de fondo no llegan cuadros, y un gesto que espera uno no termina nunca
     * (ver CLAUDE.md › "Cómo verificar").
     *
     * Las partículas se emiten en coordenadas de "mirando a la derecha": en el
     * topbar el espejado de `#plt-wrap` las da vuelta junto con el personaje.
     * Las posiciones están elegidas para que, escaladas en una peana, queden
     * dentro del canvas: en el riel de Buenas prácticas hay 8 px a la izquierda.
     */
    async function g_skate() {
      if (!canvas) return;
      canvas.classList.remove('plt-idle');
      // Estela: una raya que sale de atrás del cuerpo y se apaga hacia atrás.
      const estela = () => emitParticle('dash', {
        x: rand(12, 15), y: rand(16, 30), dx: -rand(9, 13), dy: 0, dur: 480,
      });
      // Polvo en el piso, junto a una rueda: `x` en px de la capa, `hacia` es
      // para dónde se va.
      const polvo = (x, hacia) => emitParticle('polvo', {
        x: x + rand(-1, 1), y: 37, dx: hacia * rand(4, 7), dy: -rand(2, 4), dur: 620,
      });
      const RUEDA_ATRAS = 13.6, RUEDA_ADELANTE = 26.4;

      // Rueda `ms` alternando los dos cuadros, con una estela cada dos.
      const rodar = async (ms) => {
        const fin = Date.now() + ms;
        for (let i = 0; Date.now() < fin; i++) {
          drawF(i % 2 ? 'skate2' : 'skate1');
          if (i % 2 === 0) estela();
          await wait(110);
        }
      };
      const remar = async () => {
        drawF('skateEmpuja'); polvo(RUEDA_ATRAS - 5, -1); await wait(170);
        drawF('skateEmpuja2'); await wait(150);
        drawF('skate1'); estela(); await wait(150);
      };
      const pop = (clase, seg) => {
        canvas.classList.remove(clase); void canvas.offsetWidth; canvas.classList.add(clase);
        return setTimeout(() => canvas.classList.remove(clase), seg);
      };

      // Una sola clase de rodada para todo el gesto. El seguro de reloj la
      // saca aunque el gesto no llegue al final: una clase infinita que queda
      // puesta deja al personaje meciéndose para siempre.
      const seguro = pop('plt-rueda', 7000);
      let seguroSalto = null;
      try {
        // Entrada: se sube de un salto y arranca remando.
        drawF('skateSube'); await wait(260);
        await remar();
        await remar();
        // Rodada, una remada más y otra rodada.
        await rodar(880);
        await remar();
        await rodar(440);
        // Kickflip: se agacha, pop, la tabla gira bajo los pies, la atrapa y cae.
        drawF('skateAgacha'); await wait(200);
        seguroSalto = pop('plt-skatehop', 900);
        drawF('skatePop'); polvo(RUEDA_ATRAS - 4, -1); await wait(110);
        drawF('skateFlip1'); await wait(85);
        drawF('skateFlip2'); await wait(85);
        drawF('skateFlip3'); await wait(85);
        drawF('skateAtrapa'); await wait(120);
        drawF('skateAterriza');
        polvo(RUEDA_ATRAS, -1); polvo(RUEDA_ADELANTE, 1);
        await wait(230);
        clearTimeout(seguroSalto);
        canvas.classList.remove('plt-skatehop');
        await rodar(440);
        // Remate: los dos brazos arriba.
        drawF('skateRemate'); await wait(620);
      } finally {
        clearTimeout(seguro);
        clearTimeout(seguroSalto);
        canvas.classList.remove('plt-rueda', 'plt-skatehop');
      }
      drawF('happyBlink'); await wait(85); drawF('happy'); await wait(260); drawF('base');
    }

    /* ── Gesto: GUITARRA (saca la viola, rasguea, se tira un solo) ──────────
     *
     * Antes alternaba dos cuadros casi iguales y reiniciaba `plt-tilt` en cada
     * golpe: la animación de .78 s se cortaba a los 250 ms y el cuerpo daba
     * tirones en vez de moverse. Ahora el balanceo es UNA clase en loop
     * (`plt-rockea`) que dura toda la actuación, y lo que marca el ritmo es la
     * mano: un rasguido con acento (abajo, abajo-arriba, arriba-abajo-arriba),
     * un solo cabeceando y un remate con el brazo al cielo.
     *
     * Las notas salen sólo en los golpes hacia abajo, que son los que suenan
     * fuerte; con una por cuadro eran ruido. Glifos monocromos: dos de los cinco
     * de antes eran emoji de color y desentonaban con el color del tema.
     */
    async function g_guitar() {
      if (!canvas) return;
      canvas.classList.remove('plt-idle');
      const notas = ['♪', '♫', '♬'];
      const nota = () => emitParticle('note', {
        text: notas[rand(0, notas.length - 1)],
        // Desde la boca de la guitarra, hacia afuera por la izquierda. Poco
        // recorrido lateral: en la peana del riel, más de 22 px (44 en
        // pantalla) la saca por el borde.
        x: (MW * SC) / 2 - 3, y: 28,
        dx: -rand(8, 22), dy: -rand(22, 34), dur: 1000, rot: rand(-120, 120),
      });
      // Una sola clase para toda la actuación. El seguro de reloj la saca
      // aunque el gesto no llegue al final: una clase infinita que queda
      // puesta deja al personaje bamboleándose para siempre.
      canvas.classList.remove('plt-rockea'); void canvas.offsetWidth; canvas.classList.add('plt-rockea');
      const seguro = setTimeout(() => canvas.classList.remove('plt-rockea'), 6500);
      try {
        // La levanta y arma el primer acorde.
        drawF('guitar1'); await wait(380);
        drawF('guitar2'); nota(); await wait(220);
        drawF('guitar1'); await wait(160);
        // Rasguido: dos compases. D = abajo (suena), M = de paso, U = arriba.
        const compas = ['D', 'M', 'D', 'U', 'M', 'U', 'D', 'U'];
        const cuadro = { D: 'guitar2', M: 'guitarMid', U: 'guitarUp' };
        for (let vuelta = 0; vuelta < 2; vuelta++) {
          for (const golpe of compas) {
            drawF(cuadro[golpe]);
            if (golpe === 'D') nota();
            await wait(golpe === 'M' ? 130 : 150);
          }
        }
        // Solo: punteo con la cabeza gacha, cabeceando al ritmo.
        for (let i = 0; i < 6; i++) {
          drawF(i % 2 ? 'guitarSolo2' : 'guitarSolo');
          if (i % 2 === 0) nota();
          await wait(170);
        }
        // Remate: un golpe abajo y el brazo al cielo.
        drawF('guitar2'); nota(); await wait(160);
        drawF('guitarRemate'); await wait(520);
      } finally {
        clearTimeout(seguro);
        canvas.classList.remove('plt-rockea');
      }
      drawF('happyBlink'); await wait(85); drawF('happy'); await wait(260); drawF('base');
    }

    /* ── Gesto: BAILE (agarra el ritmo, se manda unos pasos y remata) ──────
     *
     * Con la receta de la guitarra. Antes alternaba dos cuadros mirando a un
     * lado y al otro, con un vaivén en px y notas que volaban 30 px hacia
     * los costados, fuera del riel de Buenas prácticas. Ahora:
     *
     * - Entrada: agarra el ritmo cabeceando, con la mano en la cadera.
     * - Desarrollo: pasos en el tiempo, uno cada 280 ms: el índice arriba y
     *   abajo dos veces, el puño que bombea y otra vez el índice. Los pies se
     *   turnan y la cadera se balancea (`plt-baila`, la clase en loop del
     *   gesto, con dos tiempos por ciclo).
     * - Remate: la pose final con la mano bien arriba y un saltito
     *   (`plt-pop`).
     *
     * Las notas son glifos monocromos (♪ ♫), salen de arriba de la cabeza a
     * un lado y al otro con el paso, y se corren poco: dentro de la peana.
     */
    async function g_dance() {
      mirarA('fr');
      if (!canvas) return;
      canvas.classList.remove('plt-idle');
      const notas = ['♪', '♫'];
      // `lado`: -1 o 1. Sale de arriba de la cabeza, cerca de y = 9.
      const nota = (lado) => emitParticle('note', {
        text: notas[rand(0, 1)], x: 20 + lado * rand(7, 11), y: 9,
        dx: lado * rand(3, 7), dy: -rand(7, 11), dur: 900, rot: lado * rand(20, 60),
      });
      const tiempo = 280;
      let seguro = null, seguroPop = null;
      try {
        seguro = reinicia('plt-baila', 6000);
        // Agarra el ritmo: cuatro cabeceos.
        for (let i = 0; i < 4; i++) {
          drawF(i % 2 ? 'bailaCabecea' : 'bailaEntra'); await wait(tiempo);
        }
        // Los pasos, en el tiempo, con una nota cada dos, de un lado y del otro.
        const pasos = ['danceR', 'danceL', 'danceR', 'danceL', 'bailaPuno', 'bailaPuno2', 'bailaPuno', 'bailaPuno2', 'danceR', 'danceL'];
        for (let i = 0; i < pasos.length; i++) {
          drawF(pasos[i]);
          if (i % 2 === 0) nota(i % 4 === 0 ? 1 : -1);
          await wait(tiempo);
        }
        canvas.classList.remove('plt-baila');
        seguroPop = reinicia('plt-pop', 400);
        drawF('bailaRemate');
        nota(1); nota(-1);
        await wait(600);
      } finally {
        clearTimeout(seguro);
        clearTimeout(seguroPop);
        canvas.classList.remove('plt-baila', 'plt-pop');
      }
      drawF('happyBlink'); await wait(85); drawF('happy'); await wait(260); drawF('base');
    }

    /* ── Gesto: SIESTA (bosteza, se duerme parado y lo despierta el moco) ──
     *
     * Con la receta de la guitarra. Antes eran dos cuadros (achinado y
     * dormido) con una respiración en px y tres "z" de texto que volaban
     * hasta 38 px, fuera del riel de Buenas prácticas. Ahora:
     *
     * - Entrada: bosteza tapándose la boca y cabecea dos veces.
     * - Desarrollo: se duerme apoyado en la almohada, ladeado y respirando
     *   hondo (`plt-siesta`, la clase en loop del gesto); la burbuja de moco
     *   crece con cada ronquido y las "z" suben en escalera, cada una más
     *   grande.
     * - Remate: la burbuja revienta y se despierta de golpe (`startle`, con
     *   un saltito: `plt-brinco`).
     *
     * Las "z" son cajas recortadas (clip-path), no texto: no cambian con la
     * tipografía y escalan limpio en una peana. Se emiten en coordenadas de
     * "mirando a la derecha" (por eso el `mirarA('fr')`), sobre la almohada.
     */
    async function g_sleep() {
      mirarA('fr');
      if (!canvas) return;
      canvas.classList.remove('plt-idle');
      // `n`: 0, 1 o 2, en escalera hacia arriba a la derecha.
      const zeta = (n) => emitParticle('zeta', {
        x: 26 + n * 3, y: 11 - n * 3.5, dx: 2, dy: -5, dur: 1250, scale: 0.7 + n * 0.3,
      });
      let seguro = null, seguroBrinco = null;
      try {
        drawF('siestaBosteza'); await wait(560);
        drawF('siestaCabecea'); await wait(320);
        drawF('squint'); await wait(220);
        drawF('siestaCabecea'); await wait(360);
        seguro = reinicia('plt-siesta', 4200);
        for (let i = 0; i < 3; i++) {
          drawF('sleep'); await wait(600);
          drawF('siestaRonca'); zeta(i); await wait(600);
        }
        canvas.classList.remove('plt-siesta');
        // La burbuja revienta y lo despierta.
        drawF('siestaRevienta');
        for (let i = 0; i < 3; i++) {
          emitParticle('gota', { x: 23, y: 22, dx: 3 + i * 2, dy: -3 + i * 3, dur: 420 });
        }
        await wait(160);
        seguroBrinco = reinicia('plt-brinco', 380);
        drawF('startle'); await wait(420);
      } finally {
        clearTimeout(seguro);
        clearTimeout(seguroBrinco);
        canvas.classList.remove('plt-siesta', 'plt-brinco');
      }
      drawF('blink'); await wait(80); drawF('base');
    }

    /* ── Gesto: MAREADO (se marea, se agarra la cabeza y se lo sacude) ────
     *
     * Con la receta de la guitarra. Antes eran los ojos en espiral quietos,
     * un tambaleo en px y cinco "★" de texto que salían disparadas fuera del
     * riel de Buenas prácticas. Ahora:
     *
     * - Entrada: se marea y aparecen las estrellas sobre la cabeza.
     * - Desarrollo: se agarra la sien mientras las estrellas dan dos vueltas
     *   alrededor de la cabeza (por detrás y por delante) y las espirales de
     *   los ojos giran; el cuerpo se tambalea (`plt-mareo`, la clase en loop
     *   del gesto).
     * - Remate: se lo sacude de encima (`plt-sacude`, de una sola vez) y las
     *   estrellas salen despedidas y se apagan.
     *
     * Las estrellas que salen despedidas son cajas recortadas (las de
     * `estrella`, monocromas), emitidas en coordenadas de "mirando a la
     * derecha" (por eso el `mirarA('fr')`) desde la órbita, que queda cerca
     * de y = 13 en la capa del topbar; se corren poco y quedan en la peana.
     */
    async function g_dizzy() {
      mirarA('fr');
      if (!canvas) return;
      canvas.classList.remove('plt-idle');
      const vueltas = ['dizzy', 'mareo2', 'mareo3', 'mareo4'];
      let seguro = null, seguroSacude = null;
      try {
        seguro = reinicia('plt-mareo', 4000);
        drawF('mareoEntra'); await wait(280);
        for (let i = 0; i < 8; i++) { drawF(vueltas[i % 4]); await wait(170); }
        drawF('mareoEntra'); await wait(200);
        for (let i = 0; i < 8; i++) { drawF(vueltas[i % 4]); await wait(170); }
        canvas.classList.remove('plt-mareo');
        seguroSacude = reinicia('plt-sacude', 500);
        drawF('mareoSacude');
        [[10, 13, -6], [20, 15, 1], [30, 13, 6]].forEach(([x, y, dx]) => emitParticle('estrella', {
          x, y, dx, dy: -rand(4, 7), dur: 650, rot: rand(-200, 200),
        }));
        await wait(480);
      } finally {
        clearTimeout(seguro);
        clearTimeout(seguroSacude);
        canvas.classList.remove('plt-mareo', 'plt-sacude');
      }
      drawF('blink'); await wait(90); drawF('happy'); await wait(260); drawF('base');
    }

    /* ── Gesto: ENAMORADO (se enamora, le late el corazón y suspira) ───────
     *
     * Con la receta de la guitarra. Antes eran los ojos de corazón con el
     * cuerpo latiendo al 113 % y cinco corazones de texto (uno, un emoji de
     * color) que volaban fuera del riel de Buenas prácticas. Ahora:
     *
     * - Entrada: los ojos se vuelven corazones.
     * - Desarrollo: se lleva la mano al pecho y el corazón late cuatro
     *   veces, "pum-pum" y descanso, con el cuerpo acompañando (`plt-latido`,
     *   la clase en loop del gesto, con el mismo período que el latido). En
     *   cada latido sube un corazoncito.
     * - Remate: un suspiro (`plt-suspiro`, de una sola vez).
     *
     * Los corazoncitos son cajas recortadas (clip-path) de un solo color, no
     * glifos: no hay emoji de color y escalan limpio en una peana. Salen de
     * arriba de la cabeza, a un lado y al otro, y suben poco.
     */
    async function g_love() {
      mirarA('fr');
      if (!canvas) return;
      canvas.classList.remove('plt-idle');
      const corazon = (i) => {
        const lado = i % 2 ? -1 : 1;
        emitParticle('corazon', {
          x: 20 + lado * rand(6, 11), y: 10, dx: lado * rand(2, 4), dy: -rand(7, 11),
          dur: 1100, scale: [0.8, 1, 1.25][i % 3],
        });
      };
      let seguro = null, seguroSuspiro = null;
      try {
        drawF('amorEntra'); await wait(320);
        seguro = reinicia('plt-latido', 4200);
        for (let i = 0; i < 4; i++) {
          drawF('amorLate'); corazon(i); await wait(140);
          drawF('love'); await wait(130);
          drawF('amorLate'); await wait(140);
          drawF('love'); await wait(440);
        }
        canvas.classList.remove('plt-latido');
        seguroSuspiro = reinicia('plt-suspiro', 850);
        drawF('amorSuspira'); await wait(800);
      } finally {
        clearTimeout(seguro);
        clearTimeout(seguroSuspiro);
        canvas.classList.remove('plt-latido', 'plt-suspiro');
      }
      drawF('happyBlink'); await wait(85); drawF('happy'); await wait(300); drawF('base');
    }

    /* ── Gesto: LUPA (saca la lupa, inspecciona y encuentra algo) ──────────
     *
     * Con la receta de la guitarra. Antes la lupa aparecía de la nada sobre
     * la cara, barría con tres cuadros sin mano y el "!" salía 12 px por
     * arriba del riel de Buenas prácticas. Ahora:
     *
     * - Entrada: saca la lupa de abajo, se la lleva al ojo y parpadea detrás
     *   del vidrio: el ojo enorme que parpadea es el chiste del gesto.
     * - Desarrollo: barre de un ojo al otro y hacia afuera, con la mirada
     *   siguiendo a la lupa, y se acerca a mirar de cerca con el ojo
     *   entrecerrado. El cuerpo se ladea despacio, buscando (`plt-lupa`, la
     *   clase en loop del gesto).
     * - Remate: ¡lo encontró! El ojo bien abierto detrás del vidrio, un
     *   saltito (`plt-brinco`) y un "!" que aparece de golpe arriba de la
     *   lupa y se apaga sin salir de la caja.
     *
     * El "!" se emite en coordenadas de "mirando a la derecha" (por eso el
     * `mirarA('fr')`) y en px de la capa del topbar: la lupa del remate
     * queda cerca de (25, 19), con el aro hasta y = 14.
     */
    async function g_magnify() {
      mirarA('fr');
      if (!canvas) return;
      canvas.classList.remove('plt-idle');
      let seguro = null, seguroBrinco = null;
      try {
        drawF('lupaSaca'); await wait(220);
        drawF('magnify'); await wait(380);
        drawF('lupaParpadea'); await wait(90);
        drawF('magnify'); await wait(260);
        seguro = reinicia('plt-lupa', 4000);
        // Barre de un ojo al otro y hacia afuera, y vuelve.
        for (const p of ['magnifyL', 'magnify', 'magnifyR', 'magnify', 'magnifyL']) {
          drawF(p); await wait(rand(300, 380));
        }
        drawF('lupaAcerca'); await wait(520);
        canvas.classList.remove('plt-lupa');
        seguroBrinco = reinicia('plt-brinco', 380);
        drawF('magnifyFound');
        emitParticle('aviso', { x: 31, y: 7, dx: 1, dy: -3, dur: 900 });
        await wait(640);
      } finally {
        clearTimeout(seguro);
        clearTimeout(seguroBrinco);
        canvas.classList.remove('plt-lupa', 'plt-brinco');
      }
      drawF('happyBlink'); await wait(85); drawF('happy'); await wait(300); drawF('base');
    }

    /* ── Gesto: ESTORNUDO (ah… ah… ¡ah-CHÍS!, en el codo) ─────────────────
     *
     * Con la receta de la guitarra. Antes eran dos cuadros con un culatazo
     * en px y cuatro soplidos de texto que salían hasta 27 px por la
     * izquierda del riel de Buenas prácticas. Ahora:
     *
     * - Entrada: tres inspiraciones que van en aumento, con un amague (vuelve
     *   a la primera antes de la última), el cuerpo estirándose en cada una
     *   (`plt-inhala`, la clase en loop del gesto) y el brazo que sube.
     * - Desarrollo: ¡CHÍS! en el codo, con el culatazo (`plt-achis`, de una
     *   sola vez) y una nube fina de gotitas que se apaga.
     * - Remate: se frota la nariz de un lado al otro y sonríe medio
     *   avergonzado.
     *
     * Las gotitas se emiten en coordenadas de "mirando a la derecha" (por eso
     * el `mirarA('fr')`) y en px de la capa del topbar, desde el puño del
     * codo, cerca de (15, 25); salen hacia la izquierda, que es para donde el
     * brazo no tapa, y se corren poco: quedan dentro de la peana.
     */
    async function g_sneeze() {
      mirarA('fr');
      if (!canvas) return;
      canvas.classList.remove('plt-idle');
      let seguro = null, seguroAchis = null;
      try {
        seguro = reinicia('plt-inhala', 2400);
        drawF('estornudoAh'); await wait(400);
        drawF('sneezeUp'); await wait(380);
        // El amague: parece que se le pasa…
        drawF('estornudoAh'); await wait(200);
        drawF('estornudoAh3'); await wait(360);
        canvas.classList.remove('plt-inhala');
        // ¡CHÍS!
        seguroAchis = reinicia('plt-achis', 460);
        drawF('sneezeOut');
        emitParticle('polvo', { x: 15, y: 25, dx: -5, dy: -1, dur: 520 });
        for (let i = 0; i < 7; i++) {
          emitParticle('gota', {
            x: 14 + rand(-1, 1), y: 24 + rand(-2, 2), dx: -rand(3, 8), dy: rand(-5, 4), dur: rand(480, 680),
          });
        }
        await wait(380);
        drawF('estornudoCodo'); await wait(300);
        // Se frota la nariz, de un lado al otro.
        for (let i = 0; i < 2; i++) {
          drawF('estornudoFrota'); await wait(150);
          drawF('estornudoFrota2'); await wait(150);
        }
        drawF('estornudoPerdon'); await wait(360);
      } finally {
        clearTimeout(seguro);
        clearTimeout(seguroAchis);
        canvas.classList.remove('plt-inhala', 'plt-achis');
      }
      drawF('blink'); await wait(80); drawF('happy'); await wait(220); drawF('base');
    }

    /* ── Gesto: PESCA (tira la caña, pica, forcejea y saca un bug) ─────────
     *
     * Con la receta de la guitarra. Entrada: saca la caña, la echa atrás y
     * tira; el corcho cae al charco con su salpicadura. Nudo: espera, el
     * corcho tiembla dos veces sin que pique y de golpe se hunde; forcejea
     * con la caña doblada. Remate: sale un bug, lo mira de cerca, el bug
     * patalea, lo aplasta y queda contento.
     *
     * Las partículas van en px de la capa del topbar ("mirando a la derecha",
     * por eso `mirarA('fr')`): una unidad del lienzo u es (u + 2) * 2 px. El
     * corcho en el agua cae cerca de (35, 38). Recorridos cortos, por la peana.
     */
    async function g_pesca() {
      mirarA('fr');
      if (!canvas) return;
      canvas.classList.remove('plt-idle');
      const agua = (n, fuerza) => {
        emitParticle('salpica', { x: 35, y: 38, dur: 450 });
        for (let i = 0; i < n; i++) {
          emitParticle('gota', { x: 35 + rand(-2, 2), y: 37, dx: rand(-6, 6), dy: -rand(4, 4 + fuerza), dur: 520 });
        }
      };
      // Tiembla con el tirón: una clase corta, con su seguro de reloj.
      let seguro = null;
      try {
        drawF('pescaSaca'); await wait(420);
        drawF('pescaAtras'); await wait(320);
        drawF('pescaTira'); await wait(170);
        drawF('pescaEspera'); agua(3, 4); await wait(520);
        // Dos amagues: el corcho tiembla y no pica.
        for (let i = 0; i < 2; i++) {
          drawF('pescaTiembla'); await wait(150);
          drawF('pescaEspera'); await wait(i ? 420 : 300);
        }
        drawF('pescaEspera2'); await wait(110);
        drawF('pescaEspera'); await wait(260);
        // ¡Pica! Y forcejea: tres tirones.
        drawF('pescaPica'); agua(4, 6); await wait(240);
        seguro = reinicia('plt-pop', 1400);
        for (let i = 0; i < 3; i++) {
          drawF('pescaForceja'); agua(2, 5); await wait(150);
          drawF('pescaPica'); await wait(130);
        }
        // Lo saca del agua de un tirón.
        drawF('pescaSale'); agua(6, 9); await wait(360);
        // Lo mira de cerca; el bug patalea.
        drawF('pescaMira'); await wait(400);
        for (let i = 0; i < 3; i++) {
          drawF('pescaPatalea'); await wait(120);
          drawF('pescaMira'); await wait(120);
        }
        // Lo aplasta.
        drawF('pescaAplasta');
        for (let i = 0; i < 4; i++) {
          const a = (i / 4) * Math.PI * 2 + 0.4;
          emitParticle('chispa', { x: 34, y: 20, dx: Math.cos(a) * 7, dy: Math.sin(a) * 7, dur: 500 });
        }
        await wait(420);
        drawF('pescaListo'); await wait(380);
      } finally {
        clearTimeout(seguro);
        canvas.classList.remove('plt-pop');
      }
      drawF('happyBlink'); await wait(85); drawF('happy'); await wait(260); drawF('base');
    }

    /* ── Gesto: PINTOR (mide, pinta, se mancha y muestra el retrato) ───────
     *
     * Entrada: paleta y pincel en mano, mide con el pulgar como un pintor de
     * verdad. Nudo: moja el pincel en la paleta y pinta de espaldas a quien
     * mira, con salpicaduras del color del tema; en el último trazo se mancha
     * la cara. Remate: da vuelta el cuadro y es su retrato, pintado con la
     * paleta del tema. Un saltito de orgullo y estrellas.
     *
     * Las salpicaduras son 'chispa', que ya toma el acento del tema: salen
     * del borde del cuadro, cerca de (30, 24) en px de la capa del topbar.
     */
    async function g_pintor() {
      mirarA('fr');
      if (!canvas) return;
      canvas.classList.remove('plt-idle');
      const salpica = () => emitParticle('chispa', {
        x: 31 + rand(-1, 1), y: 22 + rand(-3, 3), dx: rand(2, 6), dy: -rand(3, 7), dur: 450,
      });
      let seguro = null;
      try {
        drawF('pintorSaca'); await wait(460);
        drawF('pintorMide'); await wait(560);
        drawF('pintorSaca'); await wait(160);
        // Dos tandas: moja, y unos trazos.
        for (let tanda = 0; tanda < 2; tanda++) {
          drawF('pintorMoja'); await wait(300);
          for (let i = 0; i < 3; i++) {
            drawF('pintorPinta'); salpica(); await wait(150);
            drawF('pintorPinta2'); await wait(150);
          }
        }
        drawF('pintorMancha'); salpica(); salpica(); await wait(520);
        // Lo da vuelta.
        drawF('pintorGira'); await wait(200);
        seguro = reinicia('plt-pop', 900);
        drawF('pintorMuestra');
        for (let i = 0; i < 5; i++) {
          emitParticle('estrella', { x: 31 + rand(-4, 4), y: 10 + rand(-2, 2), dx: rand(-6, 6), dy: rand(8, 14), dur: 900 });
        }
        await wait(700);
        drawF('pintorOrgullo'); await wait(650);
      } finally {
        clearTimeout(seguro);
        canvas.classList.remove('plt-pop');
      }
      drawF('happyBlink'); await wait(85); drawF('happy'); await wait(260); drawF('base');
    }

    /* ── Gesto: MALABARES (tres discos, le cae un cuarto, bonk y reverencia) ─
     *
     * Entrada: saca tres discos y arranca la cascada, dos vueltas tranquilo.
     * Nudo: le cae un cuarto del cielo y sigue con cuatro, más rápido y
     * transpirando. Remate: uno le rebota en la cabeza y se le queda arriba,
     * ataja los otros y hace la reverencia con el disco en equilibrio.
     *
     * Las vueltas de la cascada son ocho cuadros que empalman sin salto: el
     * ritmo lo da sólo la espera entre cuadros. Las gotas de sudor salen de
     * la frente, cerca de (26, 12) en px de la capa del topbar.
     */
    async function g_malabares() {
      mirarA('fr');
      if (!canvas) return;
      canvas.classList.remove('plt-idle');
      const vuelta = async (n, ms, sudor) => {
        for (let i = 0; i < 8; i++) {
          drawF('malab' + n + '_' + i);
          if (sudor && i % 4 === 0) emitParticle('gota', { x: 27, y: 12, dx: rand(3, 6), dy: rand(3, 6), dur: 520 });
          await wait(ms);
        }
      };
      let seguro = null;
      try {
        drawF('malabSaca'); await wait(420);
        await vuelta(3, 95);
        await vuelta(3, 90);
        drawF('malabCae'); await wait(360);
        await vuelta(4, 80, true);
        await vuelta(4, 72, true);
        // ¡Bonk!
        seguro = reinicia('plt-pop', 900);
        drawF('malabBonk');
        for (let i = 0; i < 3; i++) {
          emitParticle('estrella', { x: 20 + rand(-5, 5), y: 6, dx: rand(-7, 7), dy: rand(4, 9), dur: 700 });
        }
        await wait(360);
        drawF('malabAtaja'); await wait(420);
        drawF('malabReverencia'); await wait(620);
        drawF('malabAtaja'); await wait(300);
      } finally {
        clearTimeout(seguro);
        canvas.classList.remove('plt-pop');
      }
      drawF('happyBlink'); await wait(85); drawF('happy'); await wait(260); drawF('base');
    }

    /* ── Gesto: YOYÓ (lo tira, duerme, hace el perrito y lo recupera) ───────
     *
     * Entrada: lo saca y hace un tiro corto de prueba, de ida y vuelta, para
     * calentar la muñeca. Desarrollo: apunta, lo tira en serio y el yoyó baja
     * girando y se queda "dormido" al final del hilo. Nudo: lo apoya en el
     * piso y hace el perrito, pero rueda de más, el hilo se tensa y se lo
     * empieza a llevar. Remate: un tirón, el yoyó sube por el hilo, lo
     * atrapa y guiña con el otro brazo arriba.
     *
     * El giro está en los cuadros (el corte de las mitades avanza de a un
     * octavo de vuelta) y el ritmo lo da la espera: el loop de dormido son
     * cuatro cuadros que empalman. Las partículas van en px de la capa del
     * topbar: una unidad u es (u + 2) * 2 px y el piso queda en y = 37. El
     * polvo y las rayas salen hacia atrás del yoyó, porque rueda hacia adelante.
     */
    async function g_yoyo() {
      mirarA('fr');
      if (!canvas) return;
      canvas.classList.remove('plt-idle');
      const px = u => (u + 2) * 2;
      // Lo que levanta el yoyó al rodar, junto a su borde de atrás.
      const rueda = (u) => {
        emitParticle('polvo', { x: px(u) - 2 + rand(-1, 1), y: 37, dx: -rand(3, 5), dy: -rand(1, 3), dur: 520 });
        emitParticle('dash', { x: px(u) - 3, y: 34 + rand(-1, 1), dx: -rand(6, 9), dy: 0, dur: 380 });
      };
      const PERRO = [14.6, 14.85, 15.1, 15.35, 15.55, 15.7];
      let seguro = null;
      try {
        drawF('yoyoSaca'); await wait(420);
        // El tiro de prueba: ida y vuelta.
        drawF('yoyoTira'); await wait(110);
        drawF('yoyoSube'); await wait(110);
        drawF('yoyoAtrapa'); await wait(240);
        drawF('yoyoApunta'); await wait(380);
        // El tiro de verdad: baja girando y se duerme al final del hilo.
        drawF('yoyoTira'); await wait(110);
        drawF('yoyoBaja'); await wait(110);
        for (let v = 0; v < 3; v++) {
          for (let i = 0; i < 4; i++) { drawF('yoyoDuerme' + i); await wait(85); }
        }
        // Lo apoya: el perrito.
        drawF('yoyoPiso'); rueda(14.5); await wait(200);
        for (let i = 0; i < PERRO.length; i++) {
          drawF('yoyoPerro' + i);
          if (i % 2 === 0) rueda(PERRO[i]);
          await wait(140);
        }
        // Se lo lleva: el hilo tira de la mano.
        drawF('yoyoTenso'); rueda(15.7); await wait(420);
        // El tirón, y vuelve a la mano.
        drawF('yoyoTiron'); await wait(110);
        drawF('yoyoSube'); await wait(110);
        seguro = reinicia('plt-pop', 900);
        drawF('yoyoAtrapa');
        emitParticle('shine', { text: '✦', x: 33, y: 13, dx: 4, dy: -8, dur: 700, rot: 90 });
        await wait(300);
        drawF('yoyoRemate');
        for (let i = 0; i < 3; i++) {
          emitParticle('estrella', { x: 20 + rand(-6, 6), y: 8 + rand(-2, 2), dx: rand(-6, 6), dy: rand(8, 12), dur: 900 });
        }
        await wait(700);
      } finally {
        clearTimeout(seguro);
        canvas.classList.remove('plt-pop');
      }
      drawF('happyBlink'); await wait(85); drawF('happy'); await wait(260); drawF('base');
    }

    /* ── Gesto: JUEGUITO (pie y rodilla, se le va alta, pecho y planta) ─────
     *
     * Entrada: la tiene pisada y la levanta con la punta. Desarrollo: tres
     * vueltas de pie y rodilla, con ritmo parejo: el pie corto, el vuelo más
     * largo. Nudo: la última vuelta se le va corriendo hacia afuera y el toque
     * le sale con la punta; la pelota se va alta, al rincón del lienzo, y se
     * estira mirándola con la boca abierta. Remate: la baja con el pecho, la
     * deja caer, la frena con la planta y levanta el brazo.
     *
     * La vuelta de pie y rodilla son cuatro cuadros que avanzan un período
     * entero del dibujo de la pelota (cuatro pasos de JUEG_GIRO), así que se
     * repite sin que los parches salten. Las partículas van en px de la capa
     * del topbar: una unidad u es (u + 2) * 2 px y el piso queda en y = 37.
     */
    async function g_jueguito() {
      mirarA('fr');
      if (!canvas) return;
      canvas.classList.remove('plt-idle');
      const px = u => (u + 2) * 2;
      const VUELTA = [['juegPie0', 110], ['juegAire0', 170], ['juegRodilla0', 110], ['juegAire1', 170]];
      let seguro = null;
      try {
        drawF('juegPisa'); await wait(480);
        drawF('juegLevanta'); await wait(120);
        drawF('juegSube'); await wait(150);
        for (let v = 0; v < 2; v++) {
          for (const [p, ms] of VUELTA) { drawF(p); await wait(ms); }
        }
        // La última vuelta, con la pelota yendo cada vez más afuera.
        drawF('juegPie1'); await wait(110);
        drawF('juegAire2'); await wait(170);
        drawF('juegRodilla1'); await wait(110);
        drawF('juegAire3'); await wait(190);
        // La pifia: se va alta.
        drawF('juegPifia');
        emitParticle('chispa', { x: px(14.9), y: px(14.0), dx: 4, dy: -6, dur: 520, rot: 45 });
        await wait(120);
        drawF('juegAlto');
        emitParticle('aviso', { x: px(4.5), y: 3, dx: -1, dy: -3, dur: 900 });
        await wait(130);
        drawF('juegCima'); await wait(460);
        drawF('juegBaja'); await wait(150);
        // El pecho la amortigua; resbala y cae.
        drawF('juegPecho'); await wait(380);
        drawF('juegResbala'); await wait(140);
        drawF('juegCae'); await wait(130);
        // La planta: la frena en seco.
        drawF('juegPlanta');
        emitParticle('polvo', { x: px(12.4), y: 37, dx: -rand(3, 5), dy: -rand(1, 3), dur: 520 });
        emitParticle('polvo', { x: px(14.6), y: 37, dx: rand(3, 5), dy: -rand(1, 3), dur: 520 });
        await wait(380);
        seguro = reinicia('plt-pop', 900);
        drawF('juegRemate');
        for (let i = 0; i < 3; i++) {
          emitParticle('estrella', { x: 10 + rand(-6, 6), y: 8 + rand(-2, 2), dx: rand(-6, 6), dy: rand(8, 12), dur: 900 });
        }
        await wait(850);
      } finally {
        clearTimeout(seguro);
        canvas.classList.remove('plt-pop');
      }
      drawF('happyBlink'); await wait(85); drawF('happy'); await wait(260); drawF('base');
    }

    /* ── Gesto: MATE (ceba, se quema, sopla, toma y hace ruidito) ─────────
     *
     * Entrada: saca el mate y el termo, y ceba con el termo inclinado; el
     * chorrito alterna entre dos cuadros y el vapor sube del mate. Desarrollo:
     * deja el termo en el piso y toma por la bombilla. Nudo: el primer sorbo
     * quema —ojos grandes, saltito, gota de sudor y una bocanada de vapor— y
     * sopla dos veces. Remate: el segundo sorbo sale bien, el ruidito del mate
     * vacío y el "ahh" con una sonrisa.
     *
     * Las partículas van en px de la capa del topbar: una unidad u es
     * (u + 2) * 2 px. El único salto es `plt-pop`, de una sola vez, con su
     * seguro de reloj.
     */
    async function g_mate() {
      mirarA('fr');
      if (!canvas) return;
      canvas.classList.remove('plt-idle');
      const px = u => (u + 2) * 2;
      const vapor = (u, v, deriva) => emitParticle('vapor', {
        x: px(u) + rand(-1, 1), y: px(v), dx: deriva + rand(-2, 2), dy: -rand(10, 14), dur: 1050,
      });
      let seguro = null;
      try {
        drawF('mateSaca'); await wait(450);
        drawF('matePrepara'); await wait(280);
        // Ceba: el chorrito alterna y el vapor sube de la boca del mate.
        for (let i = 0; i < 3; i++) {
          drawF('mateCeba0'); if (i !== 1) vapor(11.2, 12.6, -2); await wait(140);
          drawF('mateCeba1'); await wait(140);
        }
        drawF('mateDeja'); vapor(11.6, 12.0, 2); await wait(380);
        // El primer sorbo… y quema.
        drawF('mateSorbo'); await wait(420);
        seguro = reinicia('plt-pop', 600);
        drawF('mateQuema');
        emitParticle('gota', { x: px(11.6), y: px(5.2), dx: rand(3, 6), dy: rand(4, 7), dur: 560 });
        vapor(12.4, 12.0, 3); vapor(12.0, 12.2, -3);
        await wait(520);
        // Sopla dos veces: la bocanada va de la boca al mate.
        for (let i = 0; i < 2; i++) {
          drawF('mateSopla0');
          emitParticle('polvo', { x: px(9.0), y: px(10.8), dx: rand(5, 7), dy: rand(1, 2), dur: 420 });
          await wait(220);
          drawF('mateSopla1'); await wait(170);
        }
        // El segundo sale bien, y el ruidito del mate vacío.
        drawF('mateSorbo2'); await wait(480);
        drawF('mateRuido');
        for (let i = 0; i < 3; i++) {
          setTimeout(() => emitParticle('note', {
            text: '~', x: px(10.6) + i * 2, y: px(11.4), dx: rand(4, 9), dy: -rand(8, 14), dur: 700, rot: rand(-30, 30),
          }), i * 140);
        }
        await wait(650);
        drawF('mateAhh');
        emitParticle('shine', { text: '✦', x: px(13.5), y: px(4.5), dx: 4, dy: -8, dur: 800, rot: 90 });
        vapor(11.8, 12.2, 1);
        await wait(650);
      } finally {
        clearTimeout(seguro);
        canvas.classList.remove('plt-pop');
      }
      drawF('happyBlink'); await wait(85); drawF('happy'); await wait(260); drawF('base');
    }

    /* ── Gesto: AVIÓN DE PAPEL (dobla, se le cae, lo arregla y vuela) ────
     *
     * Entrada: saca una hoja rayada y la dobla en dos pasos (casita, flecha)
     * hasta que queda el avión en la mano. Desarrollo: lo mira, apunta con un
     * ojo cerrado y lo lanza. Nudo: el avión entra en picada y se clava de
     * punta a sus pies —cara de vergüenza y gotita—; lo levanta, le endereza
     * la punta y le sopla. Remate: el segundo sale de la mano como partícula
     * (`plt-pt-avion`), hace un rulo y se pierde fuera del cuadro con su
     * estela; lo sigue con la mano de visera y festeja.
     *
     * El vuelo del remate no es del lienzo porque sale del cuadro: el lienzo lo
     * cortaría en el borde. La partícula lleva el rulo y la estela en su propia
     * animación y se borra sola al terminar (emitirEn); en el topbar espejado
     * sale para el otro lado, como todo el gesto. Las coordenadas van en px de
     * la capa del topbar: una unidad u es (u + 2) * 2 px. El único salto es
     * `plt-pop`, de una sola vez, con su seguro de reloj.
     */
    async function g_avion() {
      mirarA('fr');
      if (!canvas) return;
      canvas.classList.remove('plt-idle');
      const px = u => (u + 2) * 2;
      const soplido = (u, v) => emitParticle('polvo', { x: px(u), y: px(v), dx: rand(5, 7), dy: -rand(1, 2), dur: 420 });
      let seguro = null;
      try {
        // La hoja y los dos dobleces.
        drawF('avionHoja'); await wait(420);
        drawF('avionDobla0'); await wait(300);
        drawF('avionDobla1'); await wait(300);
        drawF('avionListo'); await wait(280);
        drawF('avionMira'); await wait(380);
        // Apunta y lo lanza: cruza el cuadro y se clava de punta a sus pies.
        drawF('avionApunta'); await wait(400);
        drawF('avionLanza'); await wait(150);
        drawF('avionPicada'); await wait(170);
        drawF('avionSuelo');
        emitParticle('polvo', { x: px(13.3), y: px(16), dx: rand(3, 5), dy: -2, dur: 450 });
        emitParticle('gota', { x: px(11.4), y: px(5.0), dx: rand(3, 5), dy: rand(4, 6), dur: 600 });
        await wait(560);
        // Lo levanta, le endereza la punta y le sopla.
        drawF('avionLevanta'); await wait(300);
        for (let i = 0; i < 2; i++) {
          drawF('avionArregla0'); await wait(160);
          drawF('avionArregla1'); await wait(160);
        }
        drawF('avionSopla'); soplido(8.6, 10.9); await wait(190); soplido(8.6, 10.9); await wait(190);
        // El segundo sale bien: la partícula hace el rulo y se va del cuadro.
        drawF('avionApunta2'); await wait(360);
        drawF('avionVuela');
        emitParticle('avion', { x: px(15.0), y: px(7.6), dx: 104, dy: -38, dur: 1900 });
        await wait(220);
        drawF('avionVisera'); await wait(900);
        seguro = reinicia('plt-pop', 600);
        drawF('avionFesteja');
        for (let i = 0; i < 4; i++) {
          setTimeout(() => emitParticle('chispa', {
            x: px(14.5) + rand(-3, 3), y: px(4.6), dx: rand(-6, 8), dy: -rand(6, 12), dur: 650, rot: rand(60, 160),
          }), i * 90);
        }
        await wait(620);
      } finally {
        clearTimeout(seguro);
        canvas.classList.remove('plt-pop');
      }
      drawF('happyBlink'); await wait(85); drawF('happy'); await wait(260); drawF('base');
    }

    /* ── Gesto: CUBO RUBIK (gira cada vez más rápido, se traba y lo resuelve) ──
     *
     * Entrada: saca el cubo desarmado y lo mira. Desarrollo: gira capas con las
     * dos manos en tres vueltas, cada una más rápida que la anterior; en la
     * última salen rayitas de velocidad a los costados. Nudo: un giro queda a
     * medias y no entra —forcejeo, gota de sudor—, lo mira de cerca, piensa
     * con los ojos arriba y lo sacude. Remate: el último giro lo deja resuelto
     * (cada cara de un color, chispas), lo levanta para mostrarlo y festeja.
     *
     * La velocidad sale de acortar las esperas, no de cuadros nuevos: los seis
     * cuadros del giro son los mismos en las tres vueltas. Las partículas van en
     * px de la capa del topbar: una unidad u es (u + 2) * 2 px. El único salto
     * es `plt-pop`, de una sola vez, con su seguro de reloj.
     */
    async function g_rubik() {
      mirarA('fr');
      if (!canvas) return;
      canvas.classList.remove('plt-idle');
      const px = u => (u + 2) * 2;
      const rayitas = () => {
        emitParticle('dash', { x: px(5.6), y: px(13.0) + rand(-2, 2), dx: -rand(7, 10), dy: 0, dur: 320 });
        emitParticle('dash', { x: px(10.4), y: px(13.0) + rand(-2, 2), dx: rand(7, 10), dy: 0, dur: 320 });
      };
      const GIROS = ['rubikGira0', 'rubikGira1', 'rubikGira2', 'rubikGira3', 'rubikGira4', 'rubikGira5'];
      let seguro = null;
      try {
        drawF('rubikSaca'); await wait(380);
        drawF('rubikMira'); await wait(380);
        // Tres vueltas de giros, cada una más rápida.
        for (const espera of [170, 110, 70]) {
          for (let i = 0; i < GIROS.length; i++) {
            drawF(GIROS[i]);
            if (espera < 100 && i % 2 === 0) rayitas();
            await wait(espera);
          }
        }
        // Se traba: la capa queda a medias y no entra.
        drawF('rubikTraba0');
        emitParticle('gota', { x: px(11.6), y: px(5.2), dx: rand(3, 6), dy: rand(4, 7), dur: 600 });
        await wait(160);
        for (let i = 0; i < 2; i++) {
          drawF('rubikTraba1'); await wait(110);
          drawF('rubikTraba0'); await wait(110);
        }
        drawF('rubikCerca'); await wait(420);
        drawF('rubikPiensa');
        emitParticle('pregunta', { text: '?', x: px(12.6), y: px(2.4), dx: 1, dy: -2, dur: 900 });
        await wait(520);
        // Lo sacude.
        for (let i = 0; i < 3; i++) {
          drawF('rubikSacude0'); await wait(80);
          drawF('rubikSacude1'); await wait(80);
        }
        // El último giro y queda resuelto.
        drawF('rubikUltimo'); await wait(170);
        drawF('rubikResuelto');
        for (let i = 0; i < 3; i++) {
          setTimeout(() => emitParticle('chispa', {
            x: px(8) + rand(-6, 6), y: px(11.2), dx: rand(-6, 6), dy: -rand(6, 10), dur: 600, rot: rand(60, 160),
          }), i * 90);
        }
        await wait(420);
        seguro = reinicia('plt-pop', 600);
        drawF('rubikMuestra');
        emitParticle('shine', { text: '✦', x: px(14.6), y: px(4.2), dx: 4, dy: -8, dur: 800, rot: 90 });
        await wait(650);
        drawF('rubikContento'); await wait(380);
      } finally {
        clearTimeout(seguro);
        canvas.classList.remove('plt-pop');
      }
      drawF('happyBlink'); await wait(85); drawF('happy'); await wait(260); drawF('base');
    }

    /* ── Gesto: SELFIE (el flash lo hace parpadear y la repite) ──
     *
     * Entrada: saca el celular, lo levanta con el brazo estirado al costado de
     * la cabeza y se acomoda buscando el ángulo. Desarrollo: pone pose con el
     * pulgar arriba y salta el flash —el destello en la lente más una ráfaga
     * blanca—, que lo encandila: ojos en espiral y estrellitas. Nudo: mira la
     * foto y la da vuelta hacia el público: salió con los ojos cerrados (cara
     * de resignación, gotita, suspiro). Remate: la vuelve a sacar con los ojos
     * bien abiertos a propósito, el flash suave ya no lo agarra, y muestra la
     * foto buena, conforme.
     *
     * La foto se lee en la pantalla desde SELFIE_DETALLE_PX para arriba; a
     * escala de topbar la cuenta la cara de él. El flash es partícula de la
     * capa de la mascota (`plt-pt-flash`), nunca la página entera, y se borra
     * sola (emitirEn). El único salto es `plt-pop`, de una sola vez, con su
     * seguro de reloj.
     */
    async function g_selfie() {
      mirarA('fr');
      if (!canvas) return;
      canvas.classList.remove('plt-idle');
      const px = u => (u + 2) * 2;
      // La lente del celular levantado (cuadros 4/5 y 10/11 de SELFIE).
      const lente = [px(14.4), px(5.3)];
      let seguro = null;
      try {
        // Lo saca, lo levanta y busca el ángulo.
        drawF('selfieSaca'); await wait(380);
        drawF('selfieLevanta'); await wait(340);
        for (let i = 0; i < 2; i++) {
          drawF('selfieAcomoda0'); await wait(220);
          drawF('selfieAcomoda1'); await wait(220);
        }
        // Pose, flash, y el flash le gana: parpadea y queda encandilado.
        drawF('selfiePose'); await wait(420);
        drawF('selfieFlash');
        emitParticle('flash', { x: lente[0], y: lente[1], dur: 380 });
        await wait(160);
        drawF('selfieEncandila');
        for (let i = 0; i < 3; i++) {
          setTimeout(() => emitParticle('estrella', {
            x: px(8) + (i - 1) * 9, y: px(2.6), dx: rand(-4, 4), dy: rand(4, 7), dur: 650, rot: rand(90, 200),
          }), i * 110);
        }
        await wait(560);
        // Mira la foto y la muestra: salió con los ojos cerrados.
        drawF('selfieMira'); await wait(420);
        drawF('selfieMuestra');
        emitParticle('gota', { x: px(4.2), y: px(5.2), dx: -rand(2, 4), dy: rand(4, 6), dur: 600 });
        await wait(680);
        drawF('selfieSuspira'); await wait(420);
        // La otra: ojos bien abiertos a propósito, y el flash suave no lo agarra.
        drawF('selfieOtra'); await wait(320);
        drawF('selfieOjazos'); await wait(480);
        drawF('selfieFlash2');
        emitParticle('flash', { x: lente[0], y: lente[1], dur: 300, scale: 0.6 });
        await wait(200);
        drawF('selfieMira2'); await wait(400);
        seguro = reinicia('plt-pop', 600);
        drawF('selfieConforme');
        for (let i = 0; i < 3; i++) {
          setTimeout(() => emitParticle('chispa', {
            x: px(13.4) + rand(-4, 4), y: px(7.6), dx: rand(-4, 8), dy: -rand(6, 10), dur: 600, rot: rand(60, 160),
          }), i * 90);
        }
        await wait(820);
      } finally {
        clearTimeout(seguro);
        canvas.classList.remove('plt-pop');
      }
      drawF('happyBlink'); await wait(85); drawF('happy'); await wait(260); drawF('base');
    }

    /* El pool de las paradas: las mismas que corría el topbar, en el mismo orden.
     * `skate` quedó afuera porque el pool se escribió cuando caminaba por el
     * topbar, donde ya se desplazaba solo. El dock y los espejos lo suman
     * aparte: parado quieto es la única forma que tiene de desplazarse. */
    const pool = [
      g_doubleBlink, g_lookL, g_lookR, g_wink,
      g_happy, g_laptop, g_bubble, g_coffee, g_scan,
      g_nod, g_wave, g_curiosity, g_think, g_celebrate,
      g_levitate, g_guitar,
      g_dance, g_sleep, g_dizzy, g_love, g_magnify, g_sneeze,
      g_pesca, g_pintor, g_malabares, g_yoyo, g_jueguito, g_mate, g_avion, g_rubik, g_selfie,
    ];

    /*
     * Cómo se elige el próximo gesto espontáneo (paradas del topbar, espejos y
     * vitrina: los tres pasan por acá).
     *
     * Antes era un sorteo parejo sobre el pool, así que la guitarra salía una
     * de cada 24 veces (4,2 %), igual que un parpadeo doble. Ahora cada gesto
     * tiene un peso —1 si no figura en PESO_GESTO— y la guitarra pesa 4.
     *
     * Subir el peso a secas la volvía repetitiva: con 4 podía salir dos veces
     * seguidas. Por eso los últimos RECIENTES gestos de ESTE actor quedan
     * afuera del sorteo; entre dos guitarras hay por lo menos cuatro gestos
     * distintos. Medido en simulación (un millón de sorteos, pool de 24):
     * guitarra 4,2 % → 10,3 %; cada uno de los otros 4,2 % → 3,9 %, así que
     * ninguno queda relegado. En el espejo (pool de 25, con skate) 4,0 % → 10,0 %.
     *
     * El historial es por actor porque cada peana y el topbar tienen su propia
     * rutina: que una peana haya tocado la guitarra no tiene por qué impedírsela
     * a la de al lado. Como RECIENTES es mayor que los tres gestos de una
     * parada, los de una misma parada siguen siendo distintos entre sí, que es
     * lo que garantizaba el barajado de antes.
     *
     * El skate y el café, rehechos con la misma receta, pesan lo mismo que la
     * guitarra. El skate sólo cuenta en los espejos: el topbar y la vitrina no
     * lo tienen en su pool (ver arriba). Con los tres en 4 (un millón de
     * sorteos): en topbar y vitrina guitarra 9,9 % y café 9,8 %, cada uno de
     * los otros 3,65 %; en el espejo guitarra, skate y café 9,2 % cada uno y
     * los otros 3,3 %.
     *
     * Con "estrellas" y "pulso" retirados el pool quedó en 22 (23 en el
     * espejo) y los pesos no cambiaron. Medido otra vez igual: en topbar y
     * vitrina guitarra y café 10,4 % y los otros 4,0 %; en el espejo los tres
     * de peso 4 9,7 % y los otros 3,5 %.
     *
     * La pesca, el pintor y los malabares (01/10/2026) son de la misma receta
     * y pesan lo mismo. Con el pool en 25 (26 en el espejo), medido igual: en
     * topbar y vitrina los cinco de peso 4 8,4 % cada uno y los otros 2,9 %;
     * en el espejo los seis de peso 4 7,9 % y los otros 2,6 %.
     *
     * El yoyó, el jueguito, el mate y el avión (01/10/2026) también pesan 4.
     * Con el pool en 29 (30 en el espejo), medido igual: en topbar y vitrina
     * los nueve de peso 4 6,5 % cada uno y los otros 2,1 %; en el espejo los
     * diez de peso 4 6,2 % y los otros 1,9 %. Cada gesto pesado sigue saliendo
     * casi el doble que en un sorteo parejo (3,4 %), así que no se diluyen
     * entre tantos.
     *
     * El cubo Rubik y la selfie (01/10/2026) también pesan 4. Con el pool en
     * 31 (32 en el espejo), medido igual: en topbar y vitrina los once de
     * peso 4 5,85 % cada uno y los otros 1,8 %; en el espejo los doce de peso
     * 4 5,55 % y los otros 1,7 %. Contra el sorteo parejo (3,2 %) cada gesto
     * pesado sigue saliendo casi el doble.
     */
    const PESO_GESTO = new Map([[g_guitar, 4], [g_skate, 4], [g_coffee, 4],
      [g_pesca, 4], [g_pintor, 4], [g_malabares, 4], [g_yoyo, 4], [g_jueguito, 4], [g_mate, 4], [g_avion, 4],
      [g_rubik, 4], [g_selfie, 4]]);
    const RECIENTES = 4;
    const recientes = [];
    function elegir(lista) {
      const todos = lista || pool;
      const libres = todos.filter(g => !recientes.includes(g));
      const cand = libres.length ? libres : todos;
      let total = 0;
      for (const g of cand) total += PESO_GESTO.get(g) || 1;
      let r = Math.random() * total;
      let elegido = cand[cand.length - 1];
      for (const g of cand) {
        r -= PESO_GESTO.get(g) || 1;
        if (r < 0) { elegido = g; break; }
      }
      recientes.push(elegido);
      if (recientes.length > RECIENTES) recientes.shift();
      return elegido;
    }

    return {
      pool: pool, elegir: elegir,
      blink: g_blink, doubleBlink: g_doubleBlink, wink: g_wink,
      lookL: g_lookL, lookR: g_lookR, happy: g_happy,
      laptop: g_laptop, bubble: g_bubble, coffee: g_coffee, scan: g_scan,
      nod: g_nod, wave: g_wave, curiosity: g_curiosity, think: g_think,
      celebrate: g_celebrate, levitate: g_levitate,
      skate: g_skate, guitar: g_guitar, dance: g_dance,
      sleep: g_sleep, dizzy: g_dizzy, love: g_love, magnify: g_magnify,
      sneeze: g_sneeze,
      pesca: g_pesca, pintor: g_pintor, malabares: g_malabares,
      yoyo: g_yoyo, jueguito: g_jueguito, mate: g_mate, avion: g_avion,
      rubik: g_rubik, selfie: g_selfie,
    };
  }

  /* El actor propio: el Boberto del dock, el mismo que se presta a una vitrina.
   *
   * Se ata PEREZOSAMENTE porque `canvas` se crea al final de este archivo: atado
   * al evaluarlo, todas sus coreografías habrían capturado `undefined` y
   * ninguna habría podido poner una clase CSS. */
  let _coreoPropia = null;
  function coreoPropia() {
    if (!_coreoPropia) {
      _coreoPropia = coreografias({
        draw: drawF, el: canvas, emit: emitParticle, burst: burstParticles,
        mirar: mirarA,
      });
    }
    return _coreoPropia;
  }

  let bubbleActive = false;
  let bubbleTimer  = null;

  function hideAdvice() {
    if (!bubble) return;
    bubble.classList.remove('show');
    bubbleActive = false;
  }

  /*
   * Dónde va la burbuja: al lado del personaje, en coordenadas de la VENTANA.
   *
   * La burbuja cuelga de <body> y es `position: fixed` (ver el CSS) porque el
   * dock vive dentro de una sidebar, y las sidebars recortan: un `overflow`
   * para el scroll de su navegación, o un `transform` de drawer en el
   * teléfono, que además convierte a `fixed` en relativo a ese ancestro. Colgada
   * del wrap, la burbuja quedaba cortada contra el borde del riel.
   *
   * Prefiere la derecha —la sidebar está a la izquierda y el contenido de la
   * app a la derecha— y se da vuelta sólo si no entra.
   */
  function ubicarBurbuja() {
    if (!bubble || !canvas) return;
    const r = canvas.getBoundingClientRect();
    const vw = document.documentElement.clientWidth || window.innerWidth;
    const vh = document.documentElement.clientHeight || window.innerHeight;
    const bw = bubble.offsetWidth || 240;
    const bh = bubble.offsetHeight || 30;
    const derecha = r.right + 12 + bw <= vw - 8 || r.left - 12 - bw < 8;
    const x = derecha ? r.right + 12 : r.left - 12 - bw;
    const y = Math.max(8, Math.min(vh - bh - 8, r.top + r.height / 2 - bh / 2));
    bubble.classList.toggle('plt-bubble-right', derecha);
    bubble.classList.toggle('plt-bubble-left', !derecha);
    bubble.style.left = Math.max(8, Math.round(x)) + 'px';
    bubble.style.top  = Math.round(y) + 'px';
  }

  /*
   * Un consejo con la laptop. Hoy no lo dispara nadie: el bucle del topbar lo
   * cambió por la laptop en silencio, y el dock heredó esa decisión. Se
   * conserva —con el interruptor "Consejos" de Ajustes— porque el corpus de
   * tips sigue declarado por cada app (BOBERTO_HOST.tips).
   */
  async function triggerAdvice(forcedText) {
    if (!enabled || !adviceEnabled || !bubble || isBusy) return;
    if (bubbleTimer) clearTimeout(bubbleTimer);

    const text = forcedText || CONSEJOS[rand(0, CONSEJOS.length - 1)];
    if (!text) return;

    bubble.innerHTML = '';
    bubbleActive = true;

    const promptSpan = document.createElement('span');
    promptSpan.className = 'plt-prompt';
    promptSpan.textContent = 'JTK> ';
    bubble.appendChild(promptSpan);

    const textContainer = document.createElement('span');
    bubble.appendChild(textContainer);

    const cursor = document.createElement('span');
    cursor.className = 'plt-cursor';
    bubble.appendChild(cursor);

    ubicarBurbuja();
    bubble.classList.add('show');

    let typingFinished = false;

    const animPromise = (async () => {
      if (isBusy) return;                 // un baile tomó el control: no dibujar
      drawF('laptopOpen');
      await wait(300);
      let frameToggle = false;
      while (bubbleActive && !typingFinished && !isBusy) {
        frameToggle = !frameToggle;
        drawF(frameToggle ? 'laptopType1' : 'laptopType2');
        await wait(120 + Math.random() * 80);
      }
      while (bubbleActive && !isBusy) {
        drawF(Math.random() < 0.3 ? (Math.random() < 0.5 ? 'laptopType1' : 'laptopType2') : 'laptopOpen');
        await wait(400);
      }
      if (isBusy) return;                 // no pisar la animación del baile con base/happy
      drawF('laptopOpen'); await wait(300);
      drawF('happyBlink'); await wait(80);
      drawF('happy');      await wait(400);
      drawF('base');
    })();

    const chars = Array.from(text);
    for (let idx = 0; idx < chars.length; idx++) {
      if (!bubbleActive || isBusy) break;
      const ch = chars[idx];
      const span = document.createElement('span');
      span.className = 'plt-char';
      if (ch === ' ') { span.innerHTML = '&nbsp;'; } else { span.textContent = ch; }
      textContainer.appendChild(span);
      // Crece mientras escribe: se reubica para que no se salga de la ventana.
      if (idx % 8 === 0) ubicarBurbuja();
      await wait(20);
    }

    typingFinished = true;
    ubicarBurbuja();

    const duration = Math.min(12000, Math.max(5000, text.length * 60 + 3000));
    await new Promise(resolve => {
      bubbleTimer = setTimeout(() => { hideAdvice(); resolve(); }, duration);
      const check = setInterval(() => {
        if (!bubbleActive) { clearInterval(check); resolve(); }
      }, 100);
    });

    await animPromise;
  }

  // ── Parpadeo idle ─────────────────────────────────────────────────────
  /*
   * Parpadeo del Boberto propio (dock o vitrina).
   *
   * `_blinkTimer` no es decorativo. Esto era una recursion de setTimeout que NO
   * guardaba su id, asi que cada llamada arrancaba una cadena nueva e
   * imparable. Con las peanas —que reajustan la escala en cada resize y
   * reintentan cuando todavia no hay layout— se apilaban decenas de cadenas y
   * el parpadeo se volvia un temblor. Ahora es idempotente: la segunda llamada
   * no hace nada.
   */
  let _blinkTimer = null;
  function startBlinkTimer() {
    if (_blinkTimer) return;
    function tick() {
      _blinkTimer = setTimeout(() => {
        if (enabled && !isBusy && !isNear) {
          drawF('blink');
          setTimeout(() => { if (!isBusy) drawF('base'); }, 75);
        }
        tick();
      }, rand(1000, 2200));
    }
    tick();
  }

  // ── Bucle del dock ────────────────────────────────────────────────────
  /*
   * Parado en la sidebar, cada tanto se manda un gesto. No camina: no hay
   * pista, y un personaje que se desplaza en un riel de 64 px se lee como un
   * error de layout.
   *
   * El intervalo es largo (9 a 16 s) porque la sidebar está siempre a la vista
   * mientras se trabaja: un gesto cada pocos segundos al costado de una tabla
   * es ruido. Con `prefers-reduced-motion` no hay gestos espontáneos —sólo
   * respira y parpadea—; tocarlo y probar gestos en el estudio sigue
   * funcionando, porque eso es pedirlo.
   *
   * Todo se espera con `wait()` (setTimeout): en una pestaña de fondo no hay
   * cuadros, y ahí además se saltea el gesto, porque nadie lo está mirando.
   */
  const REDUCIR_MOV = window.matchMedia ? window.matchMedia('(prefers-reduced-motion: reduce)') : { matches: false };
  let _bucleDock = false;
  async function bucleDock() {
    if (_bucleDock) return;
    _bucleDock = true;
    while (true) {
      await wait(rand(9000, 16000));
      // Respaldo del ResizeObserver, que entrega al final de un cuadro: sin
      // cuadros (pestaña de fondo) un colapso de la sidebar no llegaba nunca.
      // Va ANTES del chequeo de document.hidden: puesto después, justo en la
      // pestaña sin cuadros el respaldo no corría nunca, y Boberto se quedaba
      // con la escala que midió cuando la sidebar todavía estaba en transición.
      if (_dock && !_staged && wrap.parentNode === _dock) ajustarDock();
      if (!enabled || _staged || paused || isBusy || bubbleActive || document.hidden) continue;
      if (!_dock || wrap.parentNode !== _dock) continue;
      if (REDUCIR_MOV.matches) continue;

      const np = getPal();
      if (np !== pal) {
        pal = np; resolveHatColors();
        if (!_fxRAF) canvas.style.filter = staticBodyFilter();
      }

      // isBusy=true evita que el timer de parpadeo dibuje 'blink'/'base' ENCIMA
      // del gesto — eso causaba el bug de "saca/guarda" la laptop en bucle.
      // Con skate, como los espejos: el pool lo deja afuera porque se escribio
      // cuando caminaba, y parado en el dock es la unica forma de rodar.
      const T = coreoPropia();
      const g = T.elegir(T.pool.concat([T.skate]));
      isBusy = true;
      canvas.classList.remove('plt-idle');
      gestoEnDock(nombreGestoPropio(g));
      try { await g(); }
      catch (e) {}
      finally {
        isBusy = false;
        gestoEnDock('');
        if (!paused) { drawF('base'); canvas.classList.add('plt-idle'); }
      }
    }
  }

  // ── Hover sobre el dock ───────────────────────────────────────────────
  /*
   * Mira al cursor mientras lo tenés encima: arriba si estás sobre él, a un
   * costado si estás al lado. Escucha en el SLOT y no en el canvas porque el
   * slot es el botón: ocupa el ancho de la sidebar y el personaje no.
   */
  let hoverLeaveTimer = null;

  function onDockMove(e) {
    if (!enabled || _staged || isBusy || !canvas) return;
    const r = canvas.getBoundingClientRect();
    if (!r.width) return;
    // Centro medido sobre el propio rect: e.clientX vive en el espacio visual,
    // igual que el rect, así que el zoom del panel de Ajustes no lo corre.
    const dx = e.clientX - (r.left + r.width / 2);
    if (!isNear) {
      isNear = true; paused = true;
      clearTimeout(hoverLeaveTimer);
      canvas.classList.remove('plt-idle');
    }
    const quiere = Math.abs(dx) < r.width * 0.3 ? 'lookU' : (dx < 0 ? 'lookL' : 'lookR');
    if (_curFrame !== quiere) drawF(quiere);
  }

  function onDockLeave() {
    if (!isNear) return;
    isNear = false;
    clearTimeout(hoverLeaveTimer);
    hoverLeaveTimer = setTimeout(() => {
      paused = false;
      if (_staged || isBusy) return;
      drawF('blink');
      setTimeout(() => { if (!isBusy && !isNear) drawF('base'); }, 70);
      if (canvas) canvas.classList.add('plt-idle');
    }, 160);
  }

  // ── Un gesto al azar ──────────────────────────────────────────────────
  // Lo que hace al tocarlo en una vitrina (la landing), y el plan B del dock
  // cuando la página no cargó el estudio.
  const PICKS_CLICK = ['celebrate', 'wave', 'nod', 'think', 'curiosity', 'coffee', 'bubble',
                       'dance', 'love', 'dizzy', 'magnify', 'sneeze'];
  async function gestoAlAzar() {
    if (!enabled || isBusy || !canvas) return;
    isBusy = true; isNear = false; paused = true;
    canvas.classList.remove('plt-idle');
    hideAdvice();
    try {
      const T = coreoPropia();
      await T[PICKS_CLICK[Math.floor(Math.random() * PICKS_CLICK.length)]]();
    } catch (e) {
    } finally {
      paused = false; isBusy = false; drawF('base'); canvas.classList.add('plt-idle');
    }
  }

  // ── Click en el canvas ────────────────────────────────────────────────
  // En una vitrina, tocarlo festeja con un gesto. En el dock el click lo
  // atiende el slot (abre el estudio): acá no se hace nada, y sobre todo no se
  // llama a preventDefault, que le quitaría el foco al botón.
  function onCanvasClick(e) {
    if (!_staged) return;
    e.preventDefault();
    gestoAlAzar();
  }

  // ── CSS ───────────────────────────────────────────────────────────────
  const styleEl = document.createElement('style');
  styleEl.textContent = `
    /* El wrap es un bloque normal: en el dock y en una vitrina lo ubica su
       contenedor. Era absoluto y centrado en vertical cuando caminaba por el
       topbar; sin paseo no hay coordenada que escribirle.
       position: relative sigue haciendo falta porque las particulas de los
       gestos se cuelgan del wrap con position absolute. */
    #plt-wrap {
      position: relative;
      z-index: 1;
      line-height: 0;
      user-select: none;
      cursor: var(--cur-pointer, pointer);
    }
    #plt-wrap.fl { transform: scaleX(-1); }
    #plt-wrap.fr { transform: none; }
    /* Vitrina: prestado a una escena (la landing o el estudio). */
    #plt-wrap.plt-vitrina { z-index: 0; }

    /* ── El dock ──────────────────────────────────────────────────────────
       El slot lo pone cada app en su sidebar; lo que sigue es lo minimo para
       que funcione como boton en cualquiera de las tres. El ANCHO es de la
       app (el de su riel, expandido o colapsado): el motor lo mide y escala
       el canvas para entrar. El alto sale del canvas.

       Sin overflow hidden a proposito: los gestos suben por encima de la
       cabeza y las particulas salen despedidas, y un dock que recorta le
       cortaria la cabeza en cada festejo (ya paso con la peana).

       Esto es el PISO funcional. El recuadro con diseño (fondo, borde, rótulo,
       estado prestado) vive en shared/css/boberto-dock.css, que emite
       smg_boberto_estudio_tags() junto con el estudio: CSS de verdad y no un
       template literal, y cargado después de este style, así que le gana a
       igual especificidad. Sin esa hoja el slot sigue siendo un botón limpio. */
    [data-boberto-dock] {
      display: flex; flex-direction: column; align-items: center; justify-content: flex-end;
      box-sizing: border-box; min-width: 0; min-height: 44px;
      padding: 6px 4px;
      overflow: visible;
      cursor: var(--cur-pointer, pointer);
      border-radius: var(--r-md, 8px);
      -webkit-tap-highlight-color: transparent;
    }
    /* La lectura y el piso los crea el motor, pero sólo la hoja del dock los
       muestra: sin ella serían texto suelto en la sidebar. */
    [data-boberto-dock] > .plt-dock-rotulo,
    [data-boberto-dock] > .plt-dock-piso { display: none; }
    [data-boberto-dock]:focus { outline: none; }
    [data-boberto-dock]:focus-visible {
      outline: 2px solid var(--accent, #2684ff);
      outline-offset: 2px;
    }
    /* Mascota apagada desde Ajustes: el slot no ocupa lugar ni recibe foco
       (el motor le saca el tabindex). */
    [data-boberto-dock][data-boberto-estado="apagado"] { display: none; }
    /* Mientras el estudio lo tiene prestado, el slot queda vacio: se le
       conserva el alto para que la sidebar no salte debajo del modal. Sin
       manito ni resalte: no hay nada que tocar hasta que vuelva. */
    [data-boberto-dock][data-boberto-estado="prestado"] {
      min-height: var(--plt-dock-h, 44px);
      cursor: default;
    }

    /* ── La peana ──────────────────────────────────────────────────────────
       El mueble donde para Boberto cuando lo pide una app: modal, estado
       vacío, pantalla de login. Vive en el motor y no en cada app porque ya
       la usan las dos, y un mueble por app se separa solo (es exactamente el
       problema que esta suite existe para resolver).

       Los colores salen de los tokens semánticos, que son los que sobreviven
       un cambio de paleta. El acento entra por color-mix, así que la peana
       sigue al tema y al skin sin una línea por tema. */
    .plt-peana {
      position: relative;
      flex-shrink: 0;
      /* width + align-self para cuando la caja es un FLEX ITEM.

         Sin esto la peana medía CERO de ancho en cualquier contenedor flex, y
         peana() se negaba a montar — bien, pero Boberto no aparecia y sin un
         solo error. Paso en dos superficies de JiraToolkit: la tarjeta del
         anillo de Detalles (flex en columna con align-items:center, que ajusta
         al contenido, y el contenido estaba vacio) y el pie del modal de una
         tarea (flex en fila).

         Va en el MOTOR y no en cada app a proposito: es exactamente el tipo de
         cosa que la capa compartida tiene que resolver una vez para que la
         proxima app no la vuelva a descubrir. En un contenedor block no cambia
         nada (width:100% es lo mismo que auto), y donde la app declara
         flex-basis —el panel de IA del errorlog— ese gana sobre el width. */
      width: 100%; box-sizing: border-box; align-self: stretch;
      display: flex; align-items: flex-end; justify-content: center;
      /* SIN overflow:hidden, y es a proposito. Lo tenia y recortaba al
         personaje: para a ras del piso, asi que arriba le quedan diez pixeles,
         y sus gestos suben hasta trece — le cortaba la cabeza — mientras las
         particulas salen bastante mas alto todavia. El topbar nunca recorto y
         por eso ahi se veian bien. El halo y la linea de suelo no lo necesitan:
         los dos son pseudo-elementos con inset:0. */
      overflow: visible;
      /* SIN min-height acá. Tenía 'min-height: 0' y era un estorbo: este <style>
         se inyecta DESPUES del CSS de las apps, así que le ganaba a cualquier
         piso que una app quisiera poner. El panel de IA del errorlog se
         dimensiona por contenido, necesita un piso de 150px, y con el 0 del
         motor la peana medía 38 y Boberto no aparecía nunca. Las medidas de la
         caja son de la app; el motor sólo pone el mueble. */
    }
    /* Sin esta regla el atributo 'hidden' deja de funcionar en cualquier peana:
       el 'display: flex' de arriba pisa el '[hidden] { display: none }' que trae
       el navegador, y una peana "oculta" seguia ocupando su alto.

       (Sin comillas invertidas en este comentario a proposito: todo este bloque
       de CSS vive dentro de un template literal de JavaScript, y una comilla
       invertida acá lo termina en seco. Ya paso.)

       Y va aca, en el motor, porque este <style> se inyecta DESPUES del CSS de
       las apps: una app que quiera apagar su peana con 'display: none' a igual
       especificidad pierde contra '.plt-peana'. Por eso las apps la apagan con
       'height: 0' —otra propiedad, sin competencia— y el atributo lo resuelve
       esta regla, que gana por el selector de atributo. */
    .plt-peana[hidden] { display: none; }
    /* Mascota apagada en Ajustes: fuera de TODOS lados. La caja entera, con
       su halo y su piso, y no solo el canvas: un mueble vacio se lee como que
       Boberto no cargo. El avatar del chat no es una peana: se esconde sin
       soltar su lugar para no correr el resto de la burbuja. */
    html[data-boberto="off"] .plt-peana { display: none; }
    html[data-boberto="off"] .plt-espejo { visibility: hidden; }
    /* El halo va en un pseudo-elemento y no en el fondo del contenedor para no
       arrastrar el borde de quien la hospeda ni recortar el canvas. */
    .plt-peana::before {
      content: "";
      position: absolute; inset: 0;
      background:
        radial-gradient(58% 92% at 50% 108%,
          color-mix(in srgb, var(--accent, #2684ff) 22%, transparent) 0%,
          color-mix(in srgb, var(--accent, #2684ff) 7%, transparent) 42%,
          transparent 72%);
      pointer-events: none;
    }
    /* Línea de luz al pie: le da SUELO al personaje. Sin ella queda flotando
       sobre un degradado y se lee como un recorte pegado encima del panel. */
    .plt-peana::after {
      content: "";
      position: absolute; left: 50%; bottom: 16px;
      width: min(190px, 52%); height: 1px; transform: translateX(-50%);
      background: linear-gradient(90deg,
        transparent,
        color-mix(in srgb, var(--accent, #2684ff) 55%, transparent),
        transparent);
      pointer-events: none;
    }
    /* Variante con borde arriba, para cuando la peana cierra un panel (el pie de
       un cajón). Sin ella queda pegada al contenido de arriba sin separación. */
    .plt-peana.plt-peana-pie { border-top: 1px solid var(--border-subtle, rgba(255,255,255,.08)); }
    /* Variante sin halo, para peanas chicas metidas en una tarjeta ya coloreada. */
    .plt-peana.plt-peana-plana::before { display: none; }

    .plt-peana-stage {
      position: relative; z-index: 1;
      padding-bottom: 14px;
      display: flex; align-items: flex-end; justify-content: center;
    }
    /* El canvas propio de una peana. No es el del topbar: es un espejo, con su
       propio bucle de poses, justamente para que el del topbar no se mueva de
       ahi. Las animaciones CSS (plt-idle, plt-squash...) apuntan a
       'canvas.plt-*' sin '#plt-wrap' adelante, asi que le valen igual. */
    .plt-peana-cnv { display: block; }

    /* ── La capa de partículas de un espejo ───────────────────────────
       Las coreografías emiten partículas con coordenadas y tamaños de fuente
       pensados para el topbar. Un espejo de peana dibuja al personaje cuatro
       veces más grande, así que la capa mide el cuadro del TOPBAR y se escala
       entera con '--pt-k', centrada sobre el canvas.

       Escalar la caja y no cada número es lo que hace que esto no tenga
       mantenimiento: coordenadas, tamaños de fuente y hasta los 10px del anillo
       de 'plt-pt-salpica' acompañan solos. Escalar número por número habría sido
       una segunda copia de la coreografía, que se separa sola. */
    .plt-fx-capa {
      position: absolute;
      pointer-events: none;
      z-index: 90;
      transform: scale(var(--pt-k, 1));
      transform-origin: 50% 50%;
    }
    /* Sin image-rendering: el personaje ya no es una grilla de píxeles y
       'pixelated' le comía las curvas y los degradados. */
    #plt-wrap canvas { display: block; }

    /* Giro del moonwalk — la animación tiene prioridad sobre las reglas .fl/.fr */
    @keyframes plt-spin360 {
      from { transform: rotateY(0deg); }
      to   { transform: rotateY(360deg); }
    }
    canvas.plt-spin { animation: plt-spin360 .56s ease-in-out; }

    /* Respiración idle — bob + squash sutil, anclado a los pies */
    @keyframes plt-breathe {
      0%, 100% { transform: translateY(0) scaleY(1)    scaleX(1); }
      50%      { transform: translateY(-1px) scaleY(1.035) scaleX(0.985); }
    }
    canvas.plt-idle {
      transform-origin: bottom center;
      animation: plt-breathe 3.2s ease-in-out infinite;
    }

    /* Squash & stretch en aterrizajes (lo usa g_celebrate) */
    @keyframes plt-squash {
      0%   { transform: translateY(0)    scaleY(1)    scaleX(1); }
      20%  { transform: translateY(0)    scaleY(0.82) scaleX(1.15); }  /* anticipación */
      45%  { transform: translateY(-13px) scaleY(1.12) scaleX(0.9); }  /* estirón */
      70%  { transform: translateY(0)    scaleY(0.9)  scaleX(1.1); }   /* rebote */
      85%  { transform: translateY(0)    scaleY(1.04) scaleX(0.97); }
      100% { transform: translateY(0)    scaleY(1)    scaleX(1); }
    }
    canvas.plt-squash {
      transform-origin: bottom center;
      animation: plt-squash .62s ease-out;
    }

    /* La levitación (g_levitate): una sola clase con dos animaciones. La
       primera lo eleva (de 0 a -5 %) y la segunda, que arranca cuando ésa
       termina, lo deja flotando entre -5 % y -8 % con la respiración. En
       porcentaje: el de antes subía 10 px, que en una peana no se veía. */
    @keyframes plt-eleva {
      0%   { transform: translateY(0)   scaleY(.97); }
      100% { transform: translateY(-5%) scaleY(1); }
    }
    @keyframes plt-levita {
      0%, 100% { transform: translateY(-5%) scaleY(1); }
      50%      { transform: translateY(-8%) scaleY(1.02); }
    }
    canvas.plt-levita {
      transform-origin: bottom center;
      animation: plt-eleva .6s ease-out, plt-levita 1.4s ease-in-out .6s infinite;
    }

    /* El aterrizaje (remate de g_levitate), de una sola vez: baja desde la
       altura de la flotación y amortigua. */
    @keyframes plt-aterriza {
      0%   { transform: translateY(-5%) scaleY(1)   scaleX(1); }
      70%  { transform: translateY(0)   scaleY(.94) scaleX(1.05); }
      100% { transform: translateY(0)   scaleY(1)   scaleX(1); }
    }
    canvas.plt-aterriza {
      transform-origin: bottom center;
      animation: plt-aterriza .5s ease-in;
    }

    /* Pop elástico, de una sola vez: el remate de varios gestos (el brindis
       del café, la pose final del baile…). */
    @keyframes plt-pop {
      0%   { transform: scale(1); }
      35%  { transform: scale(1.13); }
      70%  { transform: scale(0.96); }
      100% { transform: scale(1); }
    }
    canvas.plt-pop {
      transform-origin: bottom center;
      animation: plt-pop .3s ease-out;
    }

    /* Balanceo del guitarrero (g_guitar): un cabeceo chico al pulso, en loop.
       Es una sola clase para toda la actuación; reiniciar una animación por
       golpe la corta antes de que termine y se lee como un tirón. */
    @keyframes plt-rockea {
      0%, 100% { transform: translateY(0)     rotate(0deg); }
      25%      { transform: translateY(-1px)  rotate(-3deg); }
      50%      { transform: translateY(0)     rotate(0deg); }
      75%      { transform: translateY(0.5px) rotate(2deg); }
    }
    canvas.plt-rockea {
      transform-origin: bottom center;
      animation: plt-rockea .6s ease-in-out infinite;
    }

    /* El balanceo del baile (g_dance): la cadera va de un lado al otro y el
       cuerpo baja en cada golpe, anclado a los pies. Un ciclo son dos
       tiempos (dos pasos de 280 ms). En loop y en porcentaje: el de antes
       se corría 2 px, que en una peana cuatro veces más grande no se veía. */
    @keyframes plt-baila {
      0%, 50%, 100% { transform: translateX(0)   translateY(0)     rotate(0deg)  scaleY(1); }
      12%           { transform: translateX(0)   translateY(1%)    rotate(0deg)  scaleY(.97); }
      25%           { transform: translateX(-1%) translateY(-1.5%) rotate(-4deg) scaleY(1.02); }
      62%           { transform: translateX(0)   translateY(1%)    rotate(0deg)  scaleY(.97); }
      75%           { transform: translateX(1%)  translateY(-1.5%) rotate(4deg)  scaleY(1.02); }
    }
    canvas.plt-baila {
      transform-origin: bottom center;
      animation: plt-baila .56s ease-in-out infinite;
    }

    /* La siesta (g_sleep): dormido contra la almohada, ladeado hacia ella, y
       respirando hondo (una respiración cada dos cuadros de 600 ms). En loop
       y en porcentaje: el de antes bajaba 1 px, que en una peana no se veía. */
    @keyframes plt-siesta {
      0%, 100% { transform: rotate(4deg) translateY(0)     scaleX(1)    scaleY(1); }
      50%      { transform: rotate(5deg) translateY(-0.6%) scaleX(.98) scaleY(1.035); }
    }
    canvas.plt-siesta {
      transform-origin: bottom center;
      animation: plt-siesta 1.2s ease-in-out infinite;
    }

    /* Tambaleo del mareo (g_dizzy): va de un lado al otro como quien no
       encuentra el piso. En loop y en porcentaje: el de antes se corría 2 px,
       que en una peana no se veía. */
    @keyframes plt-mareo {
      0%, 100% { transform: translateX(0)     rotate(0deg); }
      25%      { transform: translateX(-1.5%) rotate(-6deg); }
      50%      { transform: translateX(0)     rotate(1deg); }
      75%      { transform: translateX(1.5%)  rotate(5deg); }
    }
    canvas.plt-mareo {
      transform-origin: bottom center;
      animation: plt-mareo 1s ease-in-out infinite;
    }

    /* Se sacude el mareo de encima (remate de g_dizzy), de una sola vez:
       la cabeza va y viene rápido, cada vez menos. */
    @keyframes plt-sacude {
      0%, 100% { transform: rotate(0deg); }
      15%      { transform: rotate(-8deg); }
      35%      { transform: rotate(8deg); }
      55%      { transform: rotate(-6deg); }
      75%      { transform: rotate(4deg); }
    }
    canvas.plt-sacude {
      transform-origin: bottom center;
      animation: plt-sacude .45s ease-out;
    }

    /* Latido del enamorado (g_love): el cuerpo acompaña al corazón con un
       "pum-pum" y descansa. Más chico que el de antes (113 %): ahora late
       el corazón dibujado, y el cuerpo sólo lo acompaña. En loop. */
    @keyframes plt-latido {
      0%, 60%, 100% { transform: scale(1); }
      14%           { transform: scale(1.05); }
      28%           { transform: scale(1.01); }
      42%           { transform: scale(1.035); }
    }
    canvas.plt-latido {
      transform-origin: bottom center;
      animation: plt-latido .85s ease-out infinite;
    }

    /* El suspiro (remate de g_love), de una sola vez: toma aire, se
       infla un poco y lo suelta. */
    @keyframes plt-suspiro {
      0%, 100% { transform: translateY(0)    scaleX(1)    scaleY(1); }
      40%      { transform: translateY(-1%)  scaleX(.98)  scaleY(1.04); }
      75%      { transform: translateY(0.5%) scaleX(1.02) scaleY(.97); }
    }
    canvas.plt-suspiro {
      transform-origin: bottom center;
      animation: plt-suspiro .8s ease-in-out;
    }

    /* Las inspiraciones del estornudo (g_sneeze): en cada "ah…" el cuerpo se
       estira, se angosta y echa la cabeza atrás. En loop, una por
       inspiración, y en porcentaje para que se lea igual en una peana. */
    @keyframes plt-inhala {
      0%, 100% { transform: translateY(0)   rotate(0deg)  scaleX(1)   scaleY(1); }
      60%      { transform: translateY(-1%) rotate(-2deg) scaleX(.97) scaleY(1.04); }
    }
    canvas.plt-inhala {
      transform-origin: bottom center;
      animation: plt-inhala .42s ease-in-out infinite;
    }

    /* El estornudo (g_sneeze), de una sola vez: el cuerpo se dobla hacia el
       codo, rebota y vuelve. Es el culatazo de antes pasado a porcentaje: en
       px, en una peana cuatro veces más grande, apenas se movía. */
    @keyframes plt-achis {
      0%   { transform: translateY(0)    rotate(0deg)  scaleX(1)    scaleY(1); }
      15%  { transform: translateY(2%)   rotate(-5deg) scaleX(1.08) scaleY(.92); }
      45%  { transform: translateY(-1%)  rotate(2deg)  scaleX(.97)  scaleY(1.04); }
      75%  { transform: translateY(0)    rotate(-1deg) scaleX(1.01) scaleY(.99); }
      100% { transform: translateY(0)    rotate(0deg)  scaleX(1)    scaleY(1); }
    }
    canvas.plt-achis {
      transform-origin: bottom center;
      animation: plt-achis .42s ease-out;
    }

    /* Rodada del skate (g_skate): un vaivén chico con el cuerpo inclinado
       hacia adelante, en loop durante todo el gesto. En porcentaje del canvas
       y no en px: así se ve igual en el topbar que en una peana cuatro veces
       más grande. */
    @keyframes plt-rueda {
      0%, 100% { transform: translateY(0)     rotate(1.5deg); }
      50%      { transform: translateY(-1.2%) rotate(3deg); }
    }
    canvas.plt-rueda {
      transform-origin: bottom center;
      animation: plt-rueda .44s ease-in-out infinite;
    }

    /* Salto del kickflip (g_skate). Va DESPUÉS de plt-rueda a propósito:
       mientras está puesta, su animación gana y la rodada se retoma al
       sacarla. Mueve el canvas y no el dibujo, así el personaje no se sale
       del lienzo por arriba. */
    @keyframes plt-skatehop {
      0%   { transform: translateY(0)    rotate(0deg); }
      22%  { transform: translateY(-13%) rotate(-2deg); }
      50%  { transform: translateY(-17%) rotate(0deg); }
      80%  { transform: translateY(-7%)  rotate(1deg); }
      100% { transform: translateY(0)    rotate(0deg); }
    }
    canvas.plt-skatehop {
      transform-origin: bottom center;
      animation: plt-skatehop .52s cubic-bezier(.3, .7, .4, 1);
    }

    /* Vaivén del café (g_coffee): lento y chico, el de alguien que disfruta
       el primer café. En loop durante todo el gesto y en porcentaje, como la
       rodada, para que se vea igual en el topbar que en una peana. */
    @keyframes plt-cafecito {
      0%, 100% { transform: translateY(0)     rotate(0deg); }
      50%      { transform: translateY(-0.8%) rotate(-1.5deg); }
    }
    canvas.plt-cafecito {
      transform-origin: bottom center;
      animation: plt-cafecito 1.5s ease-in-out infinite;
    }

    /* Vaivén del saludo (g_wave): el cuerpo acompaña a la muñeca, una ida y
       vuelta cada dos sacudidas. En loop y en porcentaje, como la rodada. */
    @keyframes plt-saludo {
      0%, 50%, 100% { transform: translateY(0)     rotate(0deg); }
      25%           { transform: translateY(-0.5%) rotate(-2deg); }
      75%           { transform: translateY(-0.5%) rotate(2deg); }
    }
    canvas.plt-saludo {
      transform-origin: bottom center;
      animation: plt-saludo .66s ease-in-out infinite;
    }

    /* Rebote del festejo (g_celebrate): saltitos cortos mientras tira el
       puño. En loop y en porcentaje. */
    @keyframes plt-festejo {
      0%, 100% { transform: translateY(0)     scaleY(1); }
      50%      { transform: translateY(-2.5%) scaleY(1.02); }
    }
    canvas.plt-festejo {
      transform-origin: bottom center;
      animation: plt-festejo .34s ease-in-out infinite;
    }

    /* Vaivén de pensar (g_think): lento y chico, la cabeza que se ladea
       mientras piensa. En loop y en porcentaje. */
    @keyframes plt-piensa {
      0%, 100% { transform: translateY(0)     rotate(0deg); }
      50%      { transform: translateY(-0.4%) rotate(-2deg); }
    }
    canvas.plt-piensa {
      transform-origin: bottom center;
      animation: plt-piensa 1.4s ease-in-out infinite;
    }

    /* Mecida del tipeo (g_laptop): un cabeceo chico, rápido, de quien está
       concentrado escribiendo. En loop y en porcentaje. */
    @keyframes plt-tipea {
      0%, 100% { transform: translateY(0)     rotate(0deg); }
      50%      { transform: translateY(0.4%)  rotate(0.8deg); }
    }
    canvas.plt-tipea {
      transform-origin: bottom center;
      animation: plt-tipea .3s ease-in-out infinite;
    }

    /* Vaivén de la lupa (g_magnify): el cuerpo se ladea despacio de un lado
       al otro mientras busca, acompañando el barrido. En loop y en
       porcentaje. */
    @keyframes plt-lupa {
      0%, 100% { transform: translateX(0)     rotate(0deg); }
      25%      { transform: translateX(-0.8%) rotate(-2.5deg); }
      75%      { transform: translateX(0.8%)  rotate(2.5deg); }
    }
    canvas.plt-lupa {
      transform-origin: bottom center;
      animation: plt-lupa 1.3s ease-in-out infinite;
    }

    /* El soplido de la burbuja (g_bubble): el cuerpo se infla y se desinfla
       con cada soplido. En loop; es una escala, así que se ve igual en el
       topbar que en una peana. */
    @keyframes plt-sopla {
      0%, 100% { transform: scaleX(1)    scaleY(1); }
      50%      { transform: scaleX(1.03) scaleY(.975); }
    }
    canvas.plt-sopla {
      transform-origin: bottom center;
      animation: plt-sopla .7s ease-in-out infinite;
    }

    /* Los cabezazos de asiente (g_nod): baja en cada "sí" y vuelve, al ritmo
       de los cuadros (320 ms). En loop y en porcentaje. */
    @keyframes plt-asiente {
      0%, 100% { transform: translateY(0)    scaleY(1); }
      55%      { transform: translateY(1.5%) scaleY(.97); }
    }
    canvas.plt-asiente {
      transform-origin: bottom center;
      animation: plt-asiente .32s ease-in-out infinite;
    }

    /* El ladeo de la curiosidad (g_curiosity): la cabeza se inclina hacia un
       costado, como un perro que escucha, y vuelve despacio. En loop y en
       porcentaje. */
    @keyframes plt-curioso {
      0%, 100% { transform: translateX(0)    rotate(0deg); }
      50%      { transform: translateX(0.6%) rotate(6deg); }
    }
    canvas.plt-curioso {
      transform-origin: bottom center;
      animation: plt-curioso 1.2s ease-in-out infinite;
    }

    /* El zumbido del escaneo (g_scan): una vibración chica y rápida mientras
       el haz recorre el cuerpo. En loop y en porcentaje. */
    @keyframes plt-escanea {
      0%, 100% { transform: translateY(0)     scaleY(1); }
      50%      { transform: translateY(-0.5%) scaleY(1.01); }
    }
    canvas.plt-escanea {
      transform-origin: bottom center;
      animation: plt-escanea .24s ease-in-out infinite;
    }

    /* Saltito de una sola vez (entrada del saludo, el "¡ajá!" de pensar).
       Va DESPUÉS de las clases en loop a propósito: mientras está puesta, su
       animación gana, y el loop se retoma al sacarla. En porcentaje: en la
       peana salta lo mismo, a su escala. */
    @keyframes plt-brinco {
      0%   { transform: translateY(0)   scaleY(1)    scaleX(1); }
      35%  { transform: translateY(-6%) scaleY(1.03) scaleX(.98); }
      75%  { transform: translateY(0)   scaleY(.96)  scaleX(1.03); }
      100% { transform: translateY(0)   scaleY(1)    scaleX(1); }
    }
    canvas.plt-brinco {
      transform-origin: bottom center;
      animation: plt-brinco .34s ease-out;
    }

    /* El salto del festejo (g_celebrate), de una sola vez: despega del
       agachado, sube, cae y amortigua. Mueve el canvas y no el dibujo, como
       el kickflip, así el pelo y los sombreros no se salen del lienzo. */
    @keyframes plt-salto {
      0%   { transform: translateY(0)    scaleY(.94)  scaleX(1.05); }
      35%  { transform: translateY(-16%) scaleY(1.06) scaleX(.96); }
      60%  { transform: translateY(-11%) scaleY(1.02) scaleX(.99); }
      85%  { transform: translateY(0)    scaleY(.92)  scaleX(1.06); }
      100% { transform: translateY(0)    scaleY(1)    scaleX(1); }
    }
    canvas.plt-salto {
      transform-origin: bottom center;
      animation: plt-salto .5s cubic-bezier(.3, .7, .4, 1);
    }

    /* La burbuja cuelga de body y se ubica en coordenadas de la ventana (ver
       ubicarBurbuja): dentro de la sidebar la recortaria su overflow. Queda
       debajo del estudio (z 2147482000) y del panel de Ajustes. */
    #plt-bubble {
      position: fixed;
      top: 0; left: 0;
      transform: scale(0.9);
      opacity: 0;
      pointer-events: none;
      white-space: nowrap;
      max-width: 600px;
      overflow: hidden;
      text-overflow: ellipsis;
      padding: 6px 12px;
      background: var(--bg-elev-1, var(--s1, #0d1f3c));
      border: 1px solid var(--accent, #2684ff);
      border-radius: var(--r-sm, 6px);
      box-shadow: var(--shadow-md, 0 4px 16px rgba(0,0,0,.4)),
                  0 0 8px color-mix(in srgb, var(--accent, #2684ff) 30%, transparent);
      color: var(--text, var(--t1, #e8f4ff));
      font-size: 11px;
      line-height: 1.4;
      font-family: var(--font-mono, monospace);
      z-index: 2147481000;
      transition: opacity 0.2s ease,
                  transform 0.2s cubic-bezier(0.175, 0.885, 0.32, 1.275);
    }
    @media (prefers-reduced-motion: reduce) { #plt-bubble { transition: none; } }
    #plt-bubble.show {
      opacity: 1;
      transform: scale(1);
      pointer-events: auto;
    }

    #plt-bubble::after {
      content: "";
      position: absolute;
      top: 16px;
      margin-top: -5px;
      border: 5px solid transparent;
    }
    #plt-bubble::before {
      content: "";
      position: absolute;
      top: 16px;
      margin-top: -6px;
      border: 6px solid transparent;
    }
    #plt-bubble.plt-bubble-left::after  { left:100%; border-left-color: var(--bg-elev-1, var(--s1, #0d1f3c)); }
    #plt-bubble.plt-bubble-left::before { left:100%; border-left-color: var(--accent, #2684ff); }
    #plt-bubble.plt-bubble-right::after  { right:100%; border-right-color: var(--bg-elev-1, var(--s1, #0d1f3c)); }
    #plt-bubble.plt-bubble-right::before { right:100%; border-right-color: var(--accent, #2684ff); }

    .plt-prompt {
      color: var(--accent, #2684ff);
      font-weight: bold;
      margin-right: 4px;
    }
    .plt-cursor {
      display: inline-block;
      width: 6px;
      height: 11px;
      background-color: var(--text, var(--t1, #e8f4ff));
      margin-left: 2px;
      vertical-align: middle;
      animation: plt-blink 0.8s infinite steps(2, start);
    }
    @keyframes plt-blink { to { visibility: hidden; } }
    .plt-char { display: inline-block; }

    .plt-particle {
      position: absolute;
      pointer-events: none;
      font-family: var(--font-mono, monospace);
      font-size: 9px;
      line-height: 1;
      color: var(--accent, #2684ff);
      opacity: 0;
      transform: translate(-50%, 0) scale(var(--pt-kf, 1));
      transition: transform 1s ease-out, opacity 1s ease-out;
      z-index: 90;
      white-space: nowrap;
    }
    /* '--pt-kf' corrige la capa escalada de una peana (ver capaFx): achica el
       glifo y el recorrido para que crezcan hasta el doble del topbar y no
       hasta el cuádruple. En el topbar no está definida y vale 1, o sea que
       ahí todo queda exactamente como estaba. */
    .plt-particle.go { opacity: .9; transform: translate(calc(-50% + var(--pt-dx, 0px) * var(--pt-kf, 1)), calc(var(--pt-dy, 4px) * var(--pt-kf, 1))) scale(var(--pt-kf, 1)); }
    /* Los puntos de "pensando": círculos y no un punto de texto. Un "." en
       negrita es un cuadrado, y escalado en una peana se leía como un bloque
       blanco; un círculo sigue siendo un punto a cualquier tamaño. */
    .plt-pt-dot {
      /* '--pt-scale' los hace crecer de a uno, como los puntos de un globo
         de pensamiento; sin él miden 4 px, como siempre. */
      width: calc(4px * var(--pt-scale, 1)); height: calc(4px * var(--pt-scale, 1));
      border-radius: 50%;
      background: color-mix(in srgb, var(--accent, #2684ff) 55%, var(--text, #e8f4ff));
    }

    /* Los puntos de pensar y las barritas de la laptop. Los tipos de texto
       de antes entraban con una transición hasta .9 y se borraban del DOM a
       opacidad plena: no se desvanecían, desaparecían de golpe. Con una
       animación el final es opacidad 0, y llega un poco antes del borrado:
       la clase 'go' entra un cuadro después del temporizador que la saca. */
    @keyframes plt-drift {
      0%   { opacity: 0; transform: translate(-50%, 0) scale(var(--pt-kf, 1)); }
      18%  { opacity: .9; }
      60%  { opacity: .9; }
      92%  { opacity: 0; }
      100% { opacity: 0; transform: translate(calc(-50% + var(--pt-dx, 0px) * var(--pt-kf, 1)), calc(var(--pt-dy, 4px) * var(--pt-kf, 1))) scale(var(--pt-kf, 1)); }
    }
    .plt-pt-dot { transition: none; }
    .plt-pt-dot.go {
      opacity: 0;
      animation: plt-drift var(--pt-dur, 1.1s) ease-out forwards;
    }

    /* ── Glifos que suben girando y se apagan: el destello del café y las
       notas de la guitarra y el baile ── */
    @keyframes plt-rise {
      0%   { opacity: 0; transform: translate(-50%, 0) scale(calc(.35 * var(--pt-kf, 1))) rotate(0deg); }
      18%  { opacity: 1; }
      100% { opacity: 0; transform: translate(calc(-50% + var(--pt-dx, 0px) * var(--pt-kf, 1)), calc(var(--pt-dy, -28px) * var(--pt-kf, 1))) scale(calc(1.15 * var(--pt-kf, 1))) rotate(var(--pt-rot, 160deg)); }
    }
    .plt-pt-shine, .plt-pt-note {
      font-size: 12px;
      text-shadow: 0 0 6px currentColor;
      transition: none;
    }
    .plt-pt-shine {
      color: color-mix(in srgb, var(--accent, #2684ff) 60%, #ffffff);
    }
    .plt-pt-note {
      font-size: 14px;
      color: color-mix(in srgb, var(--accent, #2684ff) 70%, #ffffff);
    }
    .plt-pt-shine.go, .plt-pt-note.go {
      opacity: 0;
      animation: plt-rise var(--pt-dur, 1s) cubic-bezier(.22,.61,.36,1) forwards;
    }

    /* Líneas de velocidad del skate: una raya que se estira hacia atrás y se
       apaga. Antes era un glifo que llegaba a .7 por transición y se borraba
       del DOM ahí, a opacidad plena: desaparecía de golpe. Con la animación
       llega a 0 antes del borrado. Es una caja y no un carácter por lo mismo
       que los puntos de "pensando": escala limpio en una peana. */
    @keyframes plt-estela {
      0%   { opacity: 0;   transform: translate(-50%, 0) scale(calc(.6 * var(--pt-kf, 1)), var(--pt-kf, 1)); }
      20%  { opacity: .75; }
      88%  { opacity: 0; }
      100% { opacity: 0;   transform: translate(calc(-50% + var(--pt-dx, 0px) * var(--pt-kf, 1)), 0) scale(calc(1.3 * var(--pt-kf, 1)), var(--pt-kf, 1)); }
    }
    .plt-pt-dash {
      width: 9px; height: 2px;
      border-radius: 1px;
      background: color-mix(in srgb, var(--accent, #2684ff) 70%, #ffffff);
      box-shadow: 0 0 4px color-mix(in srgb, var(--accent, #2684ff) 60%, transparent);
      transition: none;
    }
    .plt-pt-dash.go {
      opacity: 0;
      animation: plt-estela var(--pt-dur, .5s) linear forwards;
    }
    /* Polvo del skate: una bocanada redonda en el piso, junto a la rueda o al
       pie que rema, que crece y se apaga. */
    @keyframes plt-polvo {
      0%   { opacity: 0;   transform: translate(-50%, -50%) scale(calc(.5 * var(--pt-kf, 1))); }
      18%  { opacity: .55; }
      85%  { opacity: 0; }
      100% { opacity: 0;   transform: translate(calc(-50% + var(--pt-dx, 0px) * var(--pt-kf, 1)), calc(-50% + var(--pt-dy, 0px) * var(--pt-kf, 1))) scale(calc(1.6 * var(--pt-kf, 1))); }
    }
    .plt-pt-polvo {
      width: 5px; height: 5px;
      border-radius: 50%;
      background: color-mix(in srgb, var(--text, #e8f4ff) 45%, #9aa3ae);
      transition: none;
    }
    .plt-pt-polvo.go {
      opacity: 0;
      animation: plt-polvo var(--pt-dur, .6s) ease-out forwards;
    }
    /* Vapor del café: una voluta en S (dos arcos finos, uno sobre el otro)
       que sube, se estira, ondula y se apaga. Antes era un "~" de texto que
       se borraba a opacidad plena. Es una caja con bordes y no un glifo por
       lo mismo que el polvo y los puntos: escala limpio en una peana. Color
       del texto del tema, lavado: vapor, no humo. */
    @keyframes plt-vapor {
      0%   { opacity: 0;   transform: translate(-50%, 0) scale(calc(.7 * var(--pt-kf, 1))); }
      22%  { opacity: .7; }
      50%  { opacity: .55; transform: translate(calc(-50% + var(--pt-dx, 0px) * .4 * var(--pt-kf, 1) - 1.5px * var(--pt-kf, 1)), calc(var(--pt-dy, -14px) * .5 * var(--pt-kf, 1))) scale(calc(.9 * var(--pt-kf, 1)), var(--pt-kf, 1)); }
      88%  { opacity: 0; }
      100% { opacity: 0;   transform: translate(calc(-50% + var(--pt-dx, 0px) * var(--pt-kf, 1)), calc(var(--pt-dy, -14px) * var(--pt-kf, 1))) scale(var(--pt-kf, 1), calc(1.35 * var(--pt-kf, 1))); }
    }
    .plt-pt-vapor {
      width: 5px; height: 10px;
      color: color-mix(in srgb, var(--text, #e8f4ff) 80%, transparent);
      transition: none;
    }
    .plt-pt-vapor::before, .plt-pt-vapor::after {
      content: ''; position: absolute;
      width: 3px; height: 5px;
      border: 0 solid currentColor;
    }
    .plt-pt-vapor::before { top: 0; left: 0; border-left-width: 1.2px; border-radius: 50% 0 0 50%; }
    .plt-pt-vapor::after  { top: 5px; left: 1.6px; border-right-width: 1.2px; border-radius: 0 50% 50% 0; }
    .plt-pt-vapor.go {
      opacity: 0;
      animation: plt-vapor var(--pt-dur, 1.15s) ease-out forwards;
    }
    /* Estrellas y chispas del festejo: cajas recortadas con clip-path y no
       glifos (un '✦' cambia de dibujo con la tipografía del tema y algunas
       fuentes lo pintan como emoji). Monocromas. La estrella cae girando,
       como lluvia; la
       chispa salta de un puño y se apaga. Las dos terminan en opacidad 0
       antes de que el temporizador las saque del DOM. */
    @keyframes plt-lluvia {
      0%   { opacity: 0;  transform: translate(-50%, 0) scale(calc(.5 * var(--pt-kf, 1))) rotate(0deg); }
      15%  { opacity: 1; }
      70%  { opacity: .85; }
      100% { opacity: 0;  transform: translate(calc(-50% + var(--pt-dx, 0px) * var(--pt-kf, 1)), calc(var(--pt-dy, 18px) * var(--pt-kf, 1))) scale(var(--pt-kf, 1)) rotate(var(--pt-rot, 160deg)); }
    }
    .plt-pt-estrella {
      width: 7px; height: 7px;
      background: color-mix(in srgb, #ffd166 82%, var(--accent, #2684ff));
      clip-path: polygon(50% 0%, 61% 35%, 98% 35%, 68% 57%, 79% 91%, 50% 70%, 21% 91%, 32% 57%, 2% 35%, 39% 35%);
      transition: none;
    }
    .plt-pt-estrella.go {
      opacity: 0;
      animation: plt-lluvia var(--pt-dur, 1s) cubic-bezier(.3,.5,.5,1) forwards;
    }
    @keyframes plt-chispazo {
      0%   { opacity: 0;  transform: translate(-50%, -50%) scale(calc(.4 * var(--pt-kf, 1))) rotate(0deg); }
      25%  { opacity: 1; }
      100% { opacity: 0;  transform: translate(calc(-50% + var(--pt-dx, 0px) * var(--pt-kf, 1)), calc(-50% + var(--pt-dy, -8px) * var(--pt-kf, 1))) scale(calc(1.1 * var(--pt-kf, 1))) rotate(var(--pt-rot, 90deg)); }
    }
    .plt-pt-chispa {
      width: 7px; height: 7px;
      background: color-mix(in srgb, var(--accent, #2684ff) 45%, #ffffff);
      clip-path: polygon(50% 0%, 62% 38%, 100% 50%, 62% 62%, 50% 100%, 38% 62%, 0% 50%, 38% 38%);
      transition: none;
    }
    .plt-pt-chispa.go {
      opacity: 0;
      animation: plt-chispazo var(--pt-dur, .6s) ease-out forwards;
    }
    /* Barritas de código de la laptop: una caja del color del tema, de largo
       variable ('--pt-scale'), en vez de fragmentos de texto como '</>'. Usan
       el mismo desvanecido que los puntos de pensar. */
    .plt-pt-bit {
      width: calc(6px * var(--pt-scale, 1)); height: 2px;
      border-radius: 1px;
      background: color-mix(in srgb, var(--accent, #2684ff) 70%, #ffffff);
      transition: none;
    }
    .plt-pt-bit.go {
      opacity: 0;
      animation: plt-drift var(--pt-dur, .9s) ease-out forwards;
    }
    /* El "!" de la lupa: una caja con la barra y el punto (los dos
       pseudoelementos), no un glifo, por lo mismo que las estrellas: escala
       limpio en una peana y no cambia de dibujo con la tipografía. Amarillo
       de aviso con un filo oscuro, para que se lea también sobre un tema
       claro. Aparece de golpe con un rebote, se sostiene y se apaga subiendo
       apenas. */
    @keyframes plt-aviso {
      0%   { opacity: 0; transform: translate(-50%, 0) scale(calc(.3 * var(--pt-kf, 1))); }
      16%  { opacity: 1; transform: translate(-50%, 0) scale(calc(1.25 * var(--pt-kf, 1))); }
      30%  { opacity: 1; transform: translate(-50%, 0) scale(var(--pt-kf, 1)); }
      70%  { opacity: 1; }
      92%  { opacity: 0; }
      100% { opacity: 0; transform: translate(calc(-50% + var(--pt-dx, 0px) * var(--pt-kf, 1)), calc(var(--pt-dy, -3px) * var(--pt-kf, 1))) scale(var(--pt-kf, 1)); }
    }
    .plt-pt-aviso {
      width: 4px; height: 12px;
      transition: none;
    }
    .plt-pt-aviso::before, .plt-pt-aviso::after {
      content: ''; position: absolute; left: 0;
      width: 4px;
      background: color-mix(in srgb, #ffd166 85%, var(--accent, #2684ff));
      box-shadow: 0 0 0 1px rgba(24, 16, 8, .55);
    }
    .plt-pt-aviso::before { top: 0; height: 8px; border-radius: 2px 2px 1.5px 1.5px; }
    .plt-pt-aviso::after  { bottom: 0; height: 3px; border-radius: 50%; }
    .plt-pt-aviso.go {
      opacity: 0;
      animation: plt-aviso var(--pt-dur, .9s) ease-out forwards;
    }
    /* El "?" de la curiosidad: un glifo de un solo color, con el mismo
       aparecer, sostenerse y apagarse que el "!" de la lupa. */
    .plt-pt-pregunta {
      font-size: 12px; font-weight: bold;
      color: color-mix(in srgb, var(--accent, #2684ff) 55%, #ffffff);
      text-shadow: 0 0 2px rgba(24, 16, 8, .6);
      transition: none;
    }
    .plt-pt-pregunta.go {
      opacity: 0;
      animation: plt-aviso var(--pt-dur, 1.3s) ease-out forwards;
    }
    /* La burbuja (g_bubble): burbujitas que se escapan del aro, el anillo de
       salpicadura y las gotitas del estallido. Cajas redondas y no glifos,
       como el polvo: escalan limpio en una peana. Del celeste de la película
       de jabón, con un toque del tema; todas terminan en opacidad 0 antes
       de que el temporizador las saque. */
    @keyframes plt-burbujita {
      0%   { opacity: 0;   transform: translate(-50%, -50%) scale(calc(.5 * var(--pt-scale, 1) * var(--pt-kf, 1))); }
      20%  { opacity: .9; }
      70%  { opacity: .6; }
      100% { opacity: 0;   transform: translate(calc(-50% + var(--pt-dx, 0px) * var(--pt-kf, 1)), calc(-50% + var(--pt-dy, -10px) * var(--pt-kf, 1))) scale(calc(var(--pt-scale, 1) * var(--pt-kf, 1))); }
    }
    .plt-pt-burbujita {
      width: 4px; height: 4px;
      border-radius: 50%;
      border: 1px solid color-mix(in srgb, #d9f1ff 80%, var(--accent, #2684ff));
      background: rgba(217, 241, 255, .14);
      transition: none;
    }
    .plt-pt-burbujita.go {
      opacity: 0;
      animation: plt-burbujita var(--pt-dur, .95s) ease-out forwards;
    }
    @keyframes plt-salpica {
      0%   { opacity: 0;  transform: translate(-50%, -50%) scale(calc(.4 * var(--pt-kf, 1))); }
      15%  { opacity: .9; }
      100% { opacity: 0;  transform: translate(-50%, -50%) scale(calc(1.5 * var(--pt-kf, 1))); }
    }
    .plt-pt-salpica {
      width: 10px; height: 10px;
      border-radius: 50%;
      border: 1.2px dashed color-mix(in srgb, #d9f1ff 80%, var(--accent, #2684ff));
      transition: none;
    }
    .plt-pt-salpica.go {
      opacity: 0;
      animation: plt-salpica var(--pt-dur, .45s) ease-out forwards;
    }
    @keyframes plt-gota {
      0%   { opacity: 0;  transform: translate(-50%, -50%) scale(var(--pt-kf, 1)); }
      15%  { opacity: .95; }
      100% { opacity: 0;  transform: translate(calc(-50% + var(--pt-dx, 0px) * var(--pt-kf, 1)), calc(-50% + var(--pt-dy, 0px) * var(--pt-kf, 1))) scale(calc(.6 * var(--pt-kf, 1))); }
    }
    .plt-pt-gota {
      width: 3px; height: 3px;
      border-radius: 50%;
      background: color-mix(in srgb, #d9f1ff 85%, var(--accent, #2684ff));
      transition: none;
    }
    .plt-pt-gota.go {
      opacity: 0;
      animation: plt-gota var(--pt-dur, .56s) ease-out forwards;
    }
    /* Las "z" de la siesta: una caja recortada con la forma de la letra, no
       un glifo, por lo mismo que las estrellas: escala limpio en una peana y
       no cambia con la tipografía. Del color del texto, lavado. Suben en
       escalera (cada una sale más arriba y más grande, ver g_sleep) y se
       apagan antes de que el temporizador las saque. */
    @keyframes plt-zeta {
      0%   { opacity: 0;   transform: translate(-50%, -50%) scale(calc(.5 * var(--pt-scale, 1) * var(--pt-kf, 1))) rotate(-8deg); }
      20%  { opacity: .9; }
      70%  { opacity: .75; }
      100% { opacity: 0;   transform: translate(calc(-50% + var(--pt-dx, 0px) * var(--pt-kf, 1)), calc(-50% + var(--pt-dy, -5px) * var(--pt-kf, 1))) scale(calc(var(--pt-scale, 1) * var(--pt-kf, 1))) rotate(6deg); }
    }
    .plt-pt-zeta {
      width: 6px; height: 6px;
      background: color-mix(in srgb, var(--text, #e8f4ff) 75%, #7fb0dd);
      clip-path: polygon(0 0, 100% 0, 100% 22%, 36% 78%, 100% 78%, 100% 100%, 0 100%, 0 78%, 64% 22%, 0 22%);
      transition: none;
    }
    .plt-pt-zeta.go {
      opacity: 0;
      animation: plt-zeta var(--pt-dur, 1.25s) ease-out forwards;
    }
    /* Los corazoncitos del enamorado: una caja recortada con forma de
       corazón, de un solo color (antes uno de los glifos era un emoji de
       color). Suben meciéndose y se apagan. */
    @keyframes plt-corazon {
      0%   { opacity: 0;  transform: translate(-50%, -50%) scale(calc(.4 * var(--pt-scale, 1) * var(--pt-kf, 1))) rotate(0deg); }
      18%  { opacity: .95; transform: translate(-50%, -50%) scale(calc(var(--pt-scale, 1) * var(--pt-kf, 1))) rotate(-8deg); }
      70%  { opacity: .8; }
      100% { opacity: 0;  transform: translate(calc(-50% + var(--pt-dx, 0px) * var(--pt-kf, 1)), calc(-50% + var(--pt-dy, -9px) * var(--pt-kf, 1))) scale(calc(var(--pt-scale, 1) * var(--pt-kf, 1))) rotate(8deg); }
    }
    .plt-pt-corazon {
      width: 7px; height: 6px;
      background: #ff5d8f;
      clip-path: polygon(50% 100%, 8% 58%, 0% 35%, 4% 15%, 18% 3%, 34% 2%, 50% 16%, 66% 2%, 82% 3%, 96% 15%, 100% 35%, 92% 58%);
      transition: none;
    }
    .plt-pt-corazon.go {
      opacity: 0;
      animation: plt-corazon var(--pt-dur, 1.1s) ease-out forwards;
    }
    /* El avión de papel del remate: sale de la mano, hace un rulo y se va
       fuera del cuadro. Es partícula y no lienzo porque el lienzo lo cortaría
       en el borde. El dibujo es el mismo perfil que pinta propAvion (ala
       blanca, quilla gris, contorno oscuro), en un SVG con colores fijos: un
       avión de papel es blanco en todos los temas. Mira a la derecha; en el
       topbar espejado lo da vuelta la capa, como al resto del gesto.
       El rulo es un círculo de radio 7 px muestreado cada 45°, con el giro
       tangente al recorrido (rotate negativo = la punta sube); el tramo final
       va a --pt-dx/--pt-dy achicándose, como si se alejara, y llega a
       opacidad 0 antes de que emitirEn lo saque del DOM: no queda nada
       infinito ni colgado. La estela es un ::before pegado a la cola, así
       gira con el avión y no hay que sincronizar dos partículas. */
    @keyframes plt-vuelo {
      0%   { opacity: 1;  transform: translate(-50%, -50%) scale(calc(.9 * var(--pt-kf, 1))) rotate(-12deg); }
      12%  { opacity: 1;  transform: translate(calc(-50% + 12px * var(--pt-kf, 1)), calc(-50% - 6px * var(--pt-kf, 1))) scale(calc(.9 * var(--pt-kf, 1))) rotate(-10deg); }
      22%  { transform: translate(calc(-50% + 24px * var(--pt-kf, 1)), calc(-50% - 9px * var(--pt-kf, 1))) scale(calc(.9 * var(--pt-kf, 1))) rotate(0deg); }
      27%  { transform: translate(calc(-50% + 29px * var(--pt-kf, 1)), calc(-50% - 11px * var(--pt-kf, 1))) scale(calc(.9 * var(--pt-kf, 1))) rotate(-45deg); }
      32%  { transform: translate(calc(-50% + 31px * var(--pt-kf, 1)), calc(-50% - 16px * var(--pt-kf, 1))) scale(calc(.9 * var(--pt-kf, 1))) rotate(-90deg); }
      37%  { transform: translate(calc(-50% + 29px * var(--pt-kf, 1)), calc(-50% - 21px * var(--pt-kf, 1))) scale(calc(.9 * var(--pt-kf, 1))) rotate(-135deg); }
      42%  { transform: translate(calc(-50% + 24px * var(--pt-kf, 1)), calc(-50% - 23px * var(--pt-kf, 1))) scale(calc(.9 * var(--pt-kf, 1))) rotate(-180deg); }
      47%  { transform: translate(calc(-50% + 19px * var(--pt-kf, 1)), calc(-50% - 21px * var(--pt-kf, 1))) scale(calc(.9 * var(--pt-kf, 1))) rotate(-225deg); }
      52%  { transform: translate(calc(-50% + 17px * var(--pt-kf, 1)), calc(-50% - 16px * var(--pt-kf, 1))) scale(calc(.9 * var(--pt-kf, 1))) rotate(-270deg); }
      57%  { transform: translate(calc(-50% + 19px * var(--pt-kf, 1)), calc(-50% - 11px * var(--pt-kf, 1))) scale(calc(.9 * var(--pt-kf, 1))) rotate(-315deg); }
      62%  { opacity: 1;  transform: translate(calc(-50% + 24px * var(--pt-kf, 1)), calc(-50% - 9px * var(--pt-kf, 1))) scale(calc(.9 * var(--pt-kf, 1))) rotate(-360deg); }
      90%  { opacity: .85; }
      100% { opacity: 0;  transform: translate(calc(-50% + var(--pt-dx, 100px) * var(--pt-kf, 1)), calc(-50% + var(--pt-dy, -36px) * var(--pt-kf, 1))) scale(calc(.5 * var(--pt-kf, 1))) rotate(-378deg); }
    }
    .plt-pt-avion {
      width: 12px; height: 8px;
      background: url("data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 24 16'%3E%3Cpath d='M23 7L4 8L8 14Z' fill='%23b9bdc6' stroke='%2322262e' stroke-width='1.6' stroke-linejoin='round'/%3E%3Cpath d='M23 7L1 1L4 8Z' fill='%23f7f5ef' stroke='%2322262e' stroke-width='1.6' stroke-linejoin='round'/%3E%3C/svg%3E") center / contain no-repeat;
      transition: none;
    }
    .plt-pt-avion::before {
      content: ''; position: absolute;
      right: 88%; top: 40%;
      width: 14px; height: 1.5px;
      border-radius: 1px;
      background: linear-gradient(to right, transparent, color-mix(in srgb, var(--accent, #2684ff) 65%, #ffffff));
      opacity: .8;
    }
    .plt-pt-avion.go {
      opacity: 1;
      animation: plt-vuelo var(--pt-dur, 1.9s) linear forwards;
    }
    /* El flash de la selfie: una ráfaga blanca que nace en la lente y se
       abre. Vive en la capa de la mascota y no tapa la página: es el destello
       del celular, no un fogonazo de pantalla. '--pt-scale' achica el flash
       suave del remate. Termina en opacidad 0 antes de que emitirEn lo saque
       del DOM: no queda nada infinito ni colgado. */
    @keyframes plt-flash {
      0%   { opacity: 0;  transform: translate(-50%, -50%) scale(calc(.2 * var(--pt-kf, 1))); }
      15%  { opacity: 1; }
      100% { opacity: 0;  transform: translate(-50%, -50%) scale(calc(2.6 * var(--pt-scale, 1) * var(--pt-kf, 1))); }
    }
    .plt-pt-flash {
      width: 12px; height: 12px;
      border-radius: 50%;
      background: radial-gradient(circle, #ffffff 0 28%, rgba(255, 252, 232, .8) 46%, rgba(255, 250, 220, 0) 72%);
      transition: none;
    }
    .plt-pt-flash.go {
      opacity: 0;
      animation: plt-flash var(--pt-dur, .38s) ease-out forwards;
    }
  `;
  document.head.appendChild(styleEl);

  // ── DOM ───────────────────────────────────────────────────────────────
  const wrap = document.createElement('div');
  wrap.id        = 'plt-wrap';
  wrap.className = 'fr';

  canvas = document.createElement('canvas');
  ctx = canvas.getContext('2d');
  fitCanvas(canvas, SC);
  wrap.appendChild(canvas);
  canvas.addEventListener('mousedown', onCanvasClick);

  // La burbuja NO va dentro del wrap: ver ubicarBurbuja(). Se cuelga de body
  // recién al arrancar, que es cuando body seguro existe.
  bubble = document.createElement('div');
  bubble.id = 'plt-bubble';
  bubble.addEventListener('mousedown', (e) => { e.stopPropagation(); hideAdvice(); });

  // Reaplica la paleta actual (skin o tema) y redibuja. Idempotente.
  function applySkin() {
    pal = getPal(); resolveHatColors();
    if (canvas && !_fxRAF) canvas.style.filter = staticBodyFilter();
    if (!isBusy) { drawF('blink'); setTimeout(() => { if (!isBusy) drawF('base'); }, 90); }
  }

  // ── Theme observer ────────────────────────────────────────────────────
  // Sólo afecta a la mascota cuando el skin está en 'auto' (sigue el tema).
  let lastTheme = document.documentElement.getAttribute('data-theme') || '';
  new MutationObserver(() => {
    const t = document.documentElement.getAttribute('data-theme') || '';
    if (t === lastTheme) return;
    lastTheme = t;
    if (skin === 'auto') applySkin();
  }).observe(document.documentElement, { attributes: true, attributeFilter: ['data-theme'] });

  /* ── Dock: el lugar de Boberto en la sidebar ───────────────────────────
   *
   * Lo declara quien renderiza: la app pone `<div data-boberto-dock></div>`
   * donde lo quiere (su sidebar) y el motor lo adopta. No se descubre con un
   * querySelector al arrancar, por dos razones medidas en esta suite:
   *
   *   - JiraToolkit pinta su sidebar desde JS, después de que Firebase
   *     confirma la sesión: cuando corre este archivo el slot todavía no
   *     existe.
   *   - Su renderSidebar() reescribe el innerHTML de la sidebar entera, así
   *     que el slot se DESTRUYE y se vuelve a crear con cada navegación. El
   *     wrap queda colgando de un nodo desconectado y Boberto desaparece.
   *
   * Por eso un MutationObserver que no se desconecta nunca: cada vez que
   * cambia el árbol, si el wrap no está en un slot vivo, busca el slot
   * actual y se muda. El costo es una comparación por mutación (el chequeo
   * caro, el querySelector, sólo corre cuando el slot se perdió).
   *
   * Si nunca aparece un slot, Boberto no se muestra. No hay plan B en el
   * topbar a propósito: el topbar es de la app, y un personaje que aparece
   * donde nadie lo declaró es la clase de cosa que se pisa con el layout.
   *
   * El slot se marca con `data-boberto-estado`: "visible", "apagado" (la
   * mascota está apagada en Ajustes) o "prestado" (está en el estudio).
   * Y con `data-boberto-fondo`: el paisaje elegido en el estudio (ver
   * FONDOS), que la hoja del dock usa para mostrar u ocultar su capa.
   */
  const DOCK_SEL = '[data-boberto-dock]';
  const DOCK_SC_MIN = 1.4;   // por debajo, los ojos dejan de leerse
  /* El techo era 3 porque las partículas, escritas a SC=2, quedaban chicas y
     corridas al lado de un personaje más grande. Desde que el dock emite en
     una capa escalada (hostPropio) eso dejó de ser cierto, y el techo es el
     del LUGAR: a 4 el personaje mide 80 px, un tercio largo de la sidebar
     expandida, que es lo que se pidió ("más grande") sin que el recuadro le
     coma la navegación. Una app puede pedir más con data-boberto-escala-max,
     hasta DOCK_SC_TOPE. El tope subió a 6 cuando el riel pasó a 280 px y el
     escenario ganó cielo: a 4.5 (90 px) el personaje quedaba chico dentro
     del lugar. A 6 mide 120 px, menos de la mitad del slot, y el riel
     colapsado no se entera porque ahí manda el ancho, no el techo. */
  const DOCK_SC_MAX = 4;
  const DOCK_SC_TOPE = 6;
  /* En el riel colapsado el ancho manda: a esta escala la cara ocupa el
     slot y la hoja recorta sólo los costados (las puntas de las alas). El
     alto del lienzo entra entero; el aire bajo los pies lo pone la hoja. */
  const DOCK_SC_BUSTO = 3.6;
  /* Por debajo de este ancho (en px, el del slot) el recuadro es "compacto":
     el riel colapsado de las tres apps deja un slot de 56. El umbral está
     lejos de los dos valores reales para que un ancho intermedio de la
     transición de colapso no lo haga titilar. */
  const DOCK_COMPACTO_PX = 120;
  let _dock = null;
  let _dockRO = null;

  /* ── Fondo del dock: el paisaje detrás de Boberto ───────────────────────
   *
   * Lo elige la persona en la pestaña "Fondo" del estudio y se guarda en
   * smg_boberto_fondo, como el resto de su look: es UNO para las tres apps,
   * porque el escenario de la sidebar es uno solo. Sin nada guardado no hay
   * fondo, que es como se veía antes de existir este eje.
   *
   * Tres cosas quedaron afuera a propósito porque el usuario ya las
   * rechazó en este mismo lugar: retícula de puntos, manchas de luz y
   * corchetes de esquina.
   *
   * El dibujo sale de generadores DETERMINISTAS (una semilla fija por fondo):
   * mismo dibujo en cada carga, en cada app y en la miniatura del estudio.
   * Con Math.random el paisaje cambiaría al recargar, y lo que se eligió en
   * la miniatura no sería lo que aparece en la sidebar.
   *
   * El SVG se arma acá, en el motor, y no en el estudio, porque lo necesitan
   * los dos: el dock para pintarlo y el estudio para las miniaturas. Ningún
   * dato de afuera entra en el markup: ids de catálogo y números propios.
   */
  const LS_FONDO = 'smg_boberto_fondo';
  const FONDO_NINGUNO = 'ninguno';
  /* Todas las escenas son láminas pintadas, con paleta propia. No salen de
   * los tokens del tema: un valle o una ciudad teñidos con el acento dejan
   * de ser esa escena. "premium" no es una etiqueta: sólo ensancha la máscara
   * del dock y de la miniatura para que el paisaje no se coma los costados.
   *
   * `grupo` arma las secciones del estudio. El personaje tapa el centro, así
   * que cada lámina deja ese tercio vacío. Cyberpunk y arcade son la familia
   * de neón; Neo Mirai es el grabado claro/medio/oscuro; Japonés es lámina
   * tradicional y Anime es cel-shading (aldea, cañón, claro); el resto es a
   * propósito otra cosa (afiche, noche quieta, trazo, interior). */
  const FONDOS = [
    { id: 'ninguno',     name: 'Sin fondo', desc: 'El escenario limpio: sólo el piso y su reflejo.' },
    { id: 'cyberpunk',   grupo: 'cyberpunk', name: 'Cyberpunk', premium: true, desc: 'Carteles en los edificios de los costados y la calle vacía en el medio.' },
    { id: 'datacenter',  grupo: 'cyberpunk', name: 'Datacenter en el horizonte', premium: true, desc: 'Racks a los dos lados de una calle vacía.' },
    { id: 'kame',        grupo: 'cyberpunk', name: 'Kame House', premium: true, desc: 'La casa a la izquierda, una palmera a la derecha y el mar en el medio.' },
    { id: 'cy-letreros', grupo: 'cyberpunk', name: 'Letreros', premium: true, desc: 'Un neón rosa a la izquierda y uno cian a la derecha, sobre la calle mojada.' },
    { id: 'cy-callejon', grupo: 'cyberpunk', name: 'Callejón', premium: true, desc: 'Carteles a la izquierda y una flecha de neón a la derecha.' },
    { id: 'arcade',      grupo: 'arcade', name: 'Arcade', premium: true, desc: 'Máquinas a los dos lados y el piso de neón libre en el centro.' },
    { id: 'anime',       grupo: 'arcade', name: 'Valle neón', premium: true, desc: 'Cordilleras de neón a los dos lados y el centro libre.' },
    { id: 'ar-sala',     grupo: 'arcade', name: 'Sala', premium: true, desc: 'Máquinas a los dos lados y el piso libre en el centro.' },
    { id: 'mirai-sol',   grupo: 'neomirai', name: 'Sol', premium: true, desc: 'Claro. El sol a la izquierda y pinos a la derecha.' },
    { id: 'mirai-monte', grupo: 'neomirai', name: 'Monte', premium: true, desc: 'Claro. El monte a la izquierda y las torres a la derecha.' },
    { id: 'mirai-puerto', grupo: 'neomirai', name: 'Puerto', premium: true, desc: 'Medio. Un muelle a la izquierda y torres a la derecha.' },
    { id: 'mirai-noche', grupo: 'neomirai', name: 'Noche', premium: true, desc: 'Oscuro. El sol y el monte a la izquierda, las torres a la derecha.' },
    { id: 'mirai-tinta', grupo: 'neomirai', name: 'Tinta', premium: true, desc: 'Oscuro. Monte y torres en línea de tinta, el centro vacío.' },
    { id: 'mirai-jardin', grupo: 'neomirai', name: 'Linterna', premium: true, desc: 'Un pino y una linterna a la izquierda, torres a la derecha.' },
    { id: 'mirai-lago',  grupo: 'neomirai', name: 'Lago', premium: true, desc: 'Un puente a la izquierda y un pino a la derecha, el agua vacía.' },
    { id: 'jp-torii',    grupo: 'japones', name: 'Torii', premium: true, desc: 'Un torii en el agua a la izquierda y un pino a la derecha.' },
    { id: 'jp-jardin',   grupo: 'japones', name: 'Jardín', premium: true, desc: 'El alero del templo a la izquierda y un arce a la derecha.' },
    { id: 'jp-sakura',   grupo: 'japones', name: 'Cerezos', premium: true, desc: 'Cerezos a los dos lados y la luna sobre el agua, en el centro.' },
    { id: 'jp-puente',   grupo: 'japones', name: 'Sauce', premium: true, desc: 'Un puente y un sauce a la izquierda, un farol y un arce a la derecha.' },
    { id: 'jp-volcan',   grupo: 'japones', name: 'Volcán', premium: true, desc: 'El cono nevado a la izquierda y pinos a la derecha.' },
    { id: 'an-noche',    grupo: 'shonen', name: 'Noche', premium: true, desc: 'Oscuro. Faroles a los dos lados y la luna sobre la calle vacía.' },
    { id: 'an-tormenta', grupo: 'shonen', name: 'Tormenta', premium: true, desc: 'Oscuro. Pinos a la izquierda, una cascada a la derecha y un rayo en el cielo.' },
    { id: 'an-muelle',   grupo: 'shonen', name: 'Muelle', premium: true, desc: 'Un barco a la izquierda y cajones a la derecha, el agua libre.' },
    { id: 'an-escalera', grupo: 'shonen', name: 'Escalera', premium: true, desc: 'Un portal y escalones a la izquierda, pinos a la derecha.' },
    { id: 'crepusculo',  grupo: 'paisaje', name: 'Crepúsculo', premium: true, desc: 'Sol a rayas a la izquierda y pinos a la derecha, en afiche plano.' },
    { id: 'nevado',      grupo: 'paisaje', name: 'Nevado', premium: true, desc: 'El pico a la izquierda y un cordón bajo a la derecha, en bandas planas.' },
    { id: 'pa-lagos',    grupo: 'paisaje', name: 'Lagos', premium: true, desc: 'Montes a los dos lados y el agua en bandas planas.' },
    { id: 'marea',       grupo: 'nocturno', name: 'Marea', premium: true, desc: 'La luna a la izquierda, un cabo a la derecha y el agua quieta en el medio.' },
    { id: 'faro',        grupo: 'nocturno', name: 'Faro', premium: true, desc: 'Anochecer, entre claro y oscuro. El faro a la izquierda y un cabo bajo a la derecha.' },
    { id: 'no-muelle',   grupo: 'nocturno', name: 'Embarcadero', premium: true, desc: 'Un muelle a la izquierda y un cabo a la derecha, el agua vacía.' },
    { id: 'topologia',   grupo: 'abstracto', name: 'Topología', premium: true, desc: 'Dos redes chicas, una a cada lado, y el centro vacío.' },
    { id: 'onda',        grupo: 'abstracto', name: 'Onda', premium: true, desc: 'Bandas curvas en los bordes, sobre un verde medio.' },
    { id: 'abs-sol',     grupo: 'abstracto', name: 'Sol', premium: true, desc: 'Arcos a la izquierda y un disco a la derecha, sobre arena.' },
    { id: 'abs-bloques', grupo: 'abstracto', name: 'Bloques', premium: true, desc: 'Rectángulos en los bordes, el centro índigo vacío.' },
    { id: 'abs-puntos',  grupo: 'abstracto', name: 'Puntos', premium: true, desc: 'Dos nubes de puntos, una a cada lado, sobre gris.' },
    { id: 'estante',     grupo: 'interior', name: 'Estante', premium: true, desc: 'Repisas en los bordes y la pared libre en el medio.' },
    { id: 'sillon',      grupo: 'interior', name: 'Sillón', premium: true, desc: 'Un sillón a la izquierda y una planta a la derecha, pared azul media.' },
    { id: 'escritorio',  grupo: 'interior', name: 'Escritorio', premium: true, desc: 'Libros a la izquierda y un reloj a la derecha, pared arena.' },
    { id: 'in-biblioteca', grupo: 'interior', name: 'Biblioteca', premium: true, desc: 'Libros a la izquierda, una lámpara y una planta a la derecha.' },
    { id: 'ce-constelaciones', grupo: 'celestial', name: 'Constelaciones', premium: true, desc: 'Cielo azul noche con una constelación a cada lado.' },
    { id: 'ce-luna',     grupo: 'celestial', name: 'Luna', premium: true, desc: 'Una luna creciente a la izquierda y nubes bajas.' },
    { id: 'ce-zodiaco',  grupo: 'celestial', name: 'Zodiaco', premium: true, desc: 'Oscuro. La rueda del zodiaco a la izquierda y constelaciones a la derecha.' },
    { id: 'ce-carta',    grupo: 'celestial', name: 'Carta estelar', premium: true, desc: 'Oscuro. Los doce signos sobre la eclíptica, constelaciones y una luna chica.' },
  ];
  const esFondo = (id) => FONDOS.some(f => f.id === id);
  /* Fondos retirados en la depuración del 14/10/2026 y el que los reemplaza.
   * Eran setenta láminas en cinco estilos que no conversaban, con
   * casi-duplicados (tres noches de cerezos con la misma luna), anime tan
   * saturado que a 236 px era ruido detrás del personaje y abstractos casi
   * negros que sobre la sidebar oscura no se veían. Quien tenía uno elegido
   * no se queda sin fondo: pasa al pariente más cercano que sigue. */
  const FONDO_RETIRADO = {
    'lluvia': 'cyberpunk', 'ar-espacio': 'arcade', 'mirai-arco': 'mirai-sol',
    'an-aldea': 'an-escalera', 'an-canon': 'an-tormenta', 'an-claro': 'an-escalera',
    'an-cumbre': 'an-escalera', 'an-techos': 'an-escalera', 'an-puente': 'an-escalera',
    'an-luna': 'jp-sakura', 'an-bruma': 'jp-sakura', 'an-cabana': 'jp-sakura',
    'an-terrazas': 'an-escalera', 'cordillera': 'nevado', 'pa-colinas': 'crepusculo',
    'luciernagas': 'marea', 'no-bosque': 'no-muelle', 'haz': 'topologia',
    'horizonte': 'topologia', 'orbita': 'abs-sol', 'eclipse': 'abs-sol',
    'abs-espiral': 'abs-puntos', 'abs-cruces': 'abs-bloques', 'lampara': 'sillon',
    'cortina': 'estante', 'in-ventana': 'in-biblioteca', 'ce-aurora': 'ce-constelaciones',
  };
  let fondo = FONDO_NINGUNO;
  try {
    let g = localStorage.getItem(LS_FONDO);
    if (FONDO_RETIRADO[g]) {
      g = FONDO_RETIRADO[g];
      localStorage.setItem(LS_FONDO, g);
    }
    if (esFondo(g)) fondo = g;
  } catch (e) { fondo = FONDO_NINGUNO; }

  /* ── Opacidad por fondo ──
   * Un valor POR FONDO (mapa id → %, 0 a 100) y no uno global: un paisaje de
   * líneas finas se quiere casi entero y uno ilustrado bastante más bajo, y
   * con un solo valor cambiar de fondo obligaba a volver a ajustarlo.
   *
   * Precedencia: lo que la persona eligió para ese fondo REEMPLAZA el default
   * del tema; sin valor elegido manda el tema (--plt-fondo-op de la hoja del
   * dock: 85 % en oscuro, 60 % sobre papel). El default se LEE de la hoja en
   * vez de copiarse acá: la lista de temas claros vive en un solo lugar.
   * Al dock llega como --plt-fondo-op-user (inline), así la hoja conserva su
   * multiplicador de "prestado" sobre el valor que gane. */
  const LS_FONDO_OP = 'smg_boberto_fondo_op';
  const FONDO_OP_DEFAULT = 85;
  const pctFondo = (v) => Math.max(0, Math.min(100, Math.round(Number(v))));
  // Sólo ids del catálogo y números: lo de storage puede venir de cualquier
  // versión, y un id viejo o un valor roto no tiene que llegar al CSS.
  function limpiarOps(m) {
    const out = {};
    if (!m || typeof m !== 'object') return out;
    Object.keys(m).forEach((k) => {
      if (k !== FONDO_NINGUNO && esFondo(k) && Number.isFinite(Number(m[k]))) out[k] = pctFondo(m[k]);
    });
    return out;
  }
  let fondoOps = {};
  try { fondoOps = limpiarOps(JSON.parse(localStorage.getItem(LS_FONDO_OP) || '{}')); } catch (e) { fondoOps = {}; }
  function guardarOps() {
    try {
      if (Object.keys(fondoOps).length) localStorage.setItem(LS_FONDO_OP, JSON.stringify(fondoOps));
      else localStorage.removeItem(LS_FONDO_OP);
    } catch (e) {}
  }
  // El default del tema, leído del slot vivo. Sin dock (la landing) queda el
  // número de fábrica: ahí no hay hoja que lo diga.
  function opDefault() {
    if (_dock) {
      const v = parseFloat(getComputedStyle(_dock).getPropertyValue('--plt-fondo-op'));
      if (Number.isFinite(v) && v >= 0 && v <= 1) return Math.round(v * 100);
    }
    return FONDO_OP_DEFAULT;
  }
  const opDe = (id) => (Object.prototype.hasOwnProperty.call(fondoOps, id) ? fondoOps[id] : opDefault());

  // El lienzo de todos los fondos es 16:9, igual que las láminas. El SVG
  // recorta con el piso anclado: si la caja es más baja que 16:9 se va el
  // cielo, no los costados. La caja del dock no puede ser más alta que 16:9
  // (boberto-dock.css): si lo fuera, este mismo recorte se comería los
  // costados, que es donde está la escena.
  const FW = 240, FH = 135;
  const r1 = (n) => Math.round(n * 10) / 10;

  const GENERADORES = {
    /* Siluetas de racks y edificios apoyadas en el piso, con pocas ventanas
       encendidas en el acento: menos de una de cada diez. */
    datacenter() { return fondoPlano('fondo-datacenter.jpg?4'); },
    /* La flota como red: nodos muy espaciados y unidos por líneas finas.
       Pocos y a mano (no una retícula): unos pocos llevan el acento. */
    topologia() { return fondoPlano('fondo-topologia.jpg?5'); },
    /* Verde medio: las bandas viven en los bordes. */
    onda() { return fondoPlano('fondo-onda.jpg?1'); },
    'abs-sol'() { return fondoPlano('fondo-abs-sol.jpg?1'); },
    'abs-bloques'() { return fondoPlano('fondo-abs-bloques.jpg?1'); },

    /* ── Ilustrados ─────────────────────────────────────────────────────────
     * Paleta propia (ver FONDOS). El personaje tapa el medio del escenario,
     * así que lo que identifica la escena son FORMAS GRANDES a los costados
     * (x 24-78 y x 162-216) y en el cielo: una casa de 7 px o una flor de
     * medio punto desaparecen, y el centro queda un manchón del color de
     * fondo. El cuadro quieto está completo; el movimiento es sólo un adorno
     * del estudio (boberto-estudio.css).
     * Los ids de gradientes llevan la serie del documento: dock, escenario y
     * miniatura conviven en la misma página. */

    /* Campo de cerezos pintado. Una lámina, no formas sueltas: los círculos
       se leían como un icono. */
    anime() { return fondoPlano('fondo-valle.jpg?4'); },

    /* Sala de juegos de noche: cielo violeta con estrellas de un píxel, una
       luna en píxeles, dos cordones de montañas escalonadas, invasores y
       monedas flotando, el piso de neón en perspectiva y, encima de todo,
       las líneas de barrido de un monitor de tubo. */
    arcade() { return fondoPlano('fondo-arcade.jpg?4'); },

    /* Ciudad de noche bajo la lluvia: dos capas de edificios con ventanas
       encendidas, bruma rosada que sube del suelo, carteles de neón en los
       edificios de los costados (los del centro los tapa Boberto), lluvia en
       diagonal y el piso mojado que repite cada cartel como un reflejo. */
    cyberpunk() { return fondoPlano('fondo-cyberpunk.jpg?4'); },

    /* Afiche plano: el sol a rayas queda a la izquierda y los pinos a la derecha. */
    crepusculo() { return fondoPlano('fondo-crepusculo.jpg?5'); },

    /* Pico a la izquierda, cordón bajo a la derecha, cielo en bandas planas. */
    nevado() { return fondoPlano('fondo-nevado.jpg?5'); },

    /* La isla pintada: la casa rosa, las palmeras y el agua. */
    kame() { return fondoPlano('fondo-kame.jpg?4'); },

    /* La luna a la izquierda, el cabo a la derecha, el agua vacía en el medio. */
    marea() { return fondoPlano('fondo-marea.jpg?5'); },

    /* Anochecer, no noche cerrada: el cielo queda entre el azul y el rosa. */
    faro() { return fondoPlano('fondo-faro.jpg?1'); },

    /* Las repisas tocan los bordes. El centro es pared. */
    estante() { return fondoPlano('fondo-estante.jpg?5'); },
    /* Dos interiores en tonos medios y distintos: azul y arena. */
    sillon() { return fondoPlano('fondo-sillon.jpg?1'); },
    escritorio() { return fondoPlano('fondo-escritorio.jpg?1'); },

    /* Neo Mirai: grabado. El sol, el monte o el portal van a un lado y las
       torres al otro. El centro es papel vacío. */
    'mirai-sol'() { return fondoPlano('fondo-mirai-sol.jpg?5'); },
    'mirai-monte'() { return fondoPlano('fondo-mirai-monte.jpg?5'); },
    'mirai-puerto'() { return fondoPlano('fondo-mirai-puerto.jpg?5'); },
    'mirai-noche'() { return fondoPlano('fondo-mirai-noche.jpg?5'); },
    'mirai-tinta'() { return fondoPlano('fondo-mirai-tinta.jpg?5'); },

    /* Japonés y anime pintado. Misma regla que el resto: lo que identifica
       la escena está en los costados, y el centro queda libre porque ahí
       se para Boberto. */
    'jp-torii'() { return fondoPlano('fondo-jp-torii.jpg?1'); },
    'jp-jardin'() { return fondoPlano('fondo-jp-jardin.jpg?1'); },
    'jp-sakura'() { return fondoPlano('fondo-jp-sakura.jpg?1'); },
    'an-noche'() { return fondoPlano('fondo-an-noche.jpg?1'); },
    'an-tormenta'() { return fondoPlano('fondo-an-tormenta.jpg?1'); },
    'an-muelle'() { return fondoPlano('fondo-an-muelle.jpg?1'); },
    'an-escalera'() { return fondoPlano('fondo-an-escalera.jpg?1'); },
    'cy-letreros'() { return fondoPlano('fondo-cy-letreros.jpg?1'); },
    'cy-callejon'() { return fondoPlano('fondo-cy-callejon.jpg?1'); },
    'ar-sala'() { return fondoPlano('fondo-ar-sala.jpg?1'); },
    'mirai-jardin'() { return fondoPlano('fondo-mirai-jardin.jpg?1'); },
    'mirai-lago'() { return fondoPlano('fondo-mirai-lago.jpg?1'); },
    'jp-puente'() { return fondoPlano('fondo-jp-puente.jpg?1'); },
    'jp-volcan'() { return fondoPlano('fondo-jp-volcan.jpg?1'); },
    'pa-lagos'() { return fondoPlano('fondo-pa-lagos.jpg?1'); },
    'no-muelle'() { return fondoPlano('fondo-no-muelle.jpg?1'); },
    'abs-puntos'() { return fondoPlano('fondo-abs-puntos.jpg?1'); },
    'in-biblioteca'() { return fondoPlano('fondo-in-biblioteca.jpg?1'); },
    'ce-constelaciones'() { return fondoPlano('fondo-ce-constelaciones.jpg?1'); },
    'ce-luna'() { return fondoPlano('fondo-ce-luna.jpg?1'); },
    'ce-zodiaco'() { return fondoPlano('fondo-ce-zodiaco.jpg?1'); },
    'ce-carta'() { return fondoPlano('fondo-ce-carta.jpg?1'); },
  };

  /* La lámina se resuelve contra el script, no contra la página: las apps
     cuelgan a distintas profundidades y un "../shared" fijo falla en la
     landing. El corte es el archivo boberto.js: boberto-estudio.js también
     contiene ese texto en el nombre. */
  let _assetBase = null;
  function assetFondo(archivo) {
    if (!_assetBase) {
      const s = [...document.scripts].find((el) => /\/boberto\.js(?:\?|$)/.test(el.src));
      _assetBase = s ? new URL('../assets/', s.src).href : '../shared/assets/';
    }
    return _assetBase + archivo;
  }
  function fondoPlano(archivo) {
    return '<image href="' + assetFondo(archivo) + '" x="0" y="0" width="' + FW + '" height="' + FH + '" preserveAspectRatio="xMidYMid slice"/>';
  }

  /** Markup del fondo `id`, o '' si es "Sin fondo" o un id fuera de catálogo. */
  function fondoSVG(id) {
    const gen = GENERADORES[id];
    if (!gen) return '';
    const premium = FONDOS.some(f => f.id === id && f.premium);
    return '<svg class="plt-fondo-svg pf-' + id + (premium ? ' pf-premium' : '') + '" viewBox="0 0 ' + FW + ' ' + FH
         + '" preserveAspectRatio="xMidYMax slice" aria-hidden="true" focusable="false">'
         + gen() + '</svg>';
  }

  /*
   * Pone el fondo elegido en el slot vivo: el atributo (para la hoja), la
   * opacidad elegida para ESE fondo y el dibujo en su capa. La capa se rearma
   * sólo si cambió el fondo, y no con cada estadoDock: corre en cada cambio
   * de estado. La opacidad sí se escribe siempre: es un atributo, no un SVG.
   */
  function aplicarFondo() {
    if (!_dock) return;
    if (_dock.getAttribute('data-boberto-fondo') !== fondo) _dock.setAttribute('data-boberto-fondo', fondo);
    // Sin valor elegido se SACA la propiedad (no se escribe el default): así
    // manda el de la hoja, que es distinto en los temas claros.
    if (Object.prototype.hasOwnProperty.call(fondoOps, fondo)) {
      _dock.style.setProperty('--plt-fondo-op-user', String(fondoOps[fondo] / 100));
    } else {
      _dock.style.removeProperty('--plt-fondo-op-user');
    }
    const capa = _dock.querySelector(':scope > .plt-dock-fondo');
    if (!capa || capa.getAttribute('data-fondo') === fondo) return;
    capa.setAttribute('data-fondo', fondo);
    capa.innerHTML = fondoSVG(fondo);
  }

  /* ── Borde del lugar ──
   * El marco de Boberto y del fondo, no de la lectura de abajo: esa sigue
   * siendo el pie. "Sin borde" es el default y no se guarda, igual que "Sin
   * fondo". En el riel colapsado la hoja lo esconde: a 55 px un marco se
   * come al personaje. */
  const LS_BORDE = 'smg_boberto_borde';
  const BORDE_NINGUNO = 'ninguno';
  const BORDES = [
    { id: 'ninguno', name: 'Sin borde', desc: 'El lugar como está: sin marco.' },
    { id: 'filete',  name: 'Filete',  desc: 'Una línea fina, quieta, del color del acento.' },
    { id: 'doble',   name: 'Doble',   desc: 'Dos líneas, una dentro de la otra.' },
    { id: 'visor',   name: 'Visor',   desc: 'Sólo las cuatro esquinas.' },
    { id: 'vidrio',  name: 'Vidrio',  desc: 'Un canto de vidrio, fijo, con un reflejo arriba.' },
    { id: 'neon',    name: 'Neón',    desc: 'Un trazo de neón que da la vuelta.' },
    { id: 'pulso',   name: 'Pulso',   desc: 'Un halo que respira, sin línea dura.' },
    { id: 'barrido', name: 'Barrido', desc: 'Una luz que recorre el perímetro.' },
    { id: 'estela',  name: 'Estela',  desc: 'La misma vuelta, con cola larga y la cabeza bien blanca.' },
    { id: 'gemelos', name: 'Gemelos', desc: 'Dos luces enfrentadas, recorriendo juntas.' },
    { id: 'chispa',  name: 'Chispa',  desc: 'Un punto corto, rápido, que da la vuelta.' },
    { id: 'flujo',   name: 'Flujo',   desc: 'El anillo encendido, con un brillo que se desplaza.' },
    { id: 'cruce',   name: 'Cruce',   desc: 'Dos recorridos a la vez, en sentido contrario.' },
    { id: 'arcade',  name: 'Arcade',  desc: 'Un bisel grueso, como la pantalla de un gabinete.' },
    { id: 'banda',   name: 'Banda',   desc: 'Una franja continua, en el filo de la imagen.' },
    { id: 'foto',    name: 'Foto',    desc: 'Dos líneas, como el passepartout de una lámina.' },
    { id: 'guion',   name: 'Guion',   desc: 'El perímetro en trazo discontinuo.' },
    { id: 'cine',    name: 'Cine',    desc: 'Sólo el canto de arriba y el de abajo.' },
    { id: 'esquina', name: 'Esquina', desc: 'Cuatro ángulos, pegados al filo.' },
  ];
  const esBorde = (id) => BORDES.some(b => b.id === id);
  let borde = BORDE_NINGUNO;
  try {
    const g = localStorage.getItem(LS_BORDE);
    if (esBorde(g)) borde = g;
  } catch (e) { borde = BORDE_NINGUNO; }

  function aplicarBorde() {
    if (!_dock) return;
    if (_dock.getAttribute('data-boberto-borde') !== borde) _dock.setAttribute('data-boberto-borde', borde);
    const marco = _dock.querySelector(':scope > .plt-borde-marco');
    if (!marco) return;
    if (borde === BORDE_NINGUNO) marco.removeAttribute('data-modelo');
    else marco.setAttribute('data-modelo', borde);
  }

  function estadoDock() {
    if (!_dock) return;
    const est = _staged ? 'prestado' : (enabled ? 'visible' : 'apagado');
    if (_dock.getAttribute('data-boberto-estado') !== est) _dock.setAttribute('data-boberto-estado', est);
    // Apagado no es un botón: fuera del orden de tabulación y del árbol
    // accesible, para que el lector de pantalla no anuncie un control oculto.
    // Prestado tampoco: el slot queda vacío mientras Boberto está en el
    // estudio, y un Tab que se detiene en un hueco que no
    // responde se lee como roto. Sigue en el árbol, pero deshabilitado: el
    // lugar existe y vuelve a ser botón en cuanto él regresa (goHome).
    _dock.setAttribute('tabindex', est === 'visible' ? '0' : '-1');
    if (est === 'apagado') _dock.setAttribute('aria-hidden', 'true');
    else _dock.removeAttribute('aria-hidden');
    if (est === 'prestado') _dock.setAttribute('aria-disabled', 'true');
    else _dock.removeAttribute('aria-disabled');
    lecturaDock();
    // Acá y no sólo en setFondo: un slot recreado por la app (JiraToolkit
    // rearma su sidebar) nace sin dibujo, y lo primero que corre es esto.
    aplicarFondo();
    aplicarBorde();
  }

  /*
   * El dato vivo de la lectura del dock, el lado derecho del renglón mono.
   *
   * Dice algo que es VERDAD ahora, no una etiqueta fija: el gesto que está
   * haciendo mientras dura, dónde está si se lo prestó al estudio, y si no
   * qué lleva puesto (el personaje, o el accesorio si no hay personaje). Es
   * la gramática de la línea de estado de DBMON: rótulo a la izquierda,
   * lectura a la derecha. Un "Personalizar" fijo era una etiqueta de tarjeta.
   */
  let _gestoDock = '';
  function lecturaDock() {
    if (!_dock) return;
    const dato = _dock.querySelector(':scope > .plt-dock-rotulo .plt-dock-dato');
    if (!dato) return;
    let txt;
    if (_staged) txt = 'En el estudio';
    else if (_gestoDock) txt = _gestoDock;
    else {
      const ch = character ? CHARACTERS.find(c => c.id === character) : null;
      const acc = ch ? null : HAT_PRESETS.find(h => h.id === hat);
      txt = ch ? ch.name : (acc ? acc.name : 'En línea');
    }
    if (dato.textContent !== txt) dato.textContent = txt;
    // La marca deja que la hoja encienda la lectura mientras dura el gesto.
    if (_gestoDock && !_staged) _dock.setAttribute('data-boberto-gesto', '');
    else _dock.removeAttribute('data-boberto-gesto');
  }

  /* El nombre de catálogo de una coreografía del actor propio, o '' si no es
     un gesto con nombre (parpadeos y miradas también salen del sorteo). */
  function nombreGestoPropio(fn) {
    const T = coreoPropia();
    const key = Object.keys(T).find(k => T[k] === fn);
    const g = key ? GESTOS.find(x => x.key === key) : null;
    return g ? g.name : '';
  }
  function gestoEnDock(nombre) {
    _gestoDock = nombre || '';
    lecturaDock();
  }

  /*
   * Escala para que el canvas entre en el ANCHO útil del slot.
   *
   * El slot mide lo que mide la sidebar —280 px expandida, 64 colapsada— y
   * eso es lo que manda: el personaje se achica para entrar en el riel
   * colapsado (64 px menos el padding dan unos 2,4 px por unidad, o sea un
   * canvas de 48 px) en vez de desbordarlo. Una app puede mover el techo con
   * `data-boberto-escala-max` en el propio slot —lo declara quien renderiza—,
   * hacia abajo libremente y hacia arriba hasta DOCK_SC_TOPE.
   */
  function escalaDock(slot) {
    const techo = Math.min(DOCK_SC_TOPE, parseFloat(slot.getAttribute('data-boberto-escala-max')) || DOCK_SC_MAX);
    /* El busto no sale del ancho: la hoja recorta. formaDock ya corrió. */
    if (slot.getAttribute('data-boberto-forma') === 'compacta') return Math.min(techo, DOCK_SC_BUSTO);
    const cs = getComputedStyle(slot);
    const util = slot.clientWidth - (parseFloat(cs.paddingLeft) || 0) - (parseFloat(cs.paddingRight) || 0);
    if (!(util > 0)) return 0;
    const s = Math.floor((util / BOX.W) * 10) / 10;
    return Math.max(DOCK_SC_MIN, Math.min(techo, s));
  }

  /*
   * La forma del recuadro: "amplia" (sidebar expandida, con rótulo) o
   * "compacta" (riel de 64). Se marca en el slot ANTES de medir, porque cada
   * forma tiene su padding y el ancho útil sale de ahí. La decide el ancho y
   * no una clase de la app: cada una colapsa con un selector distinto
   * (.collapsed, data-dbm-nav, data-db-nav) y el recuadro es uno solo.
   */
  function formaDock(slot) {
    const w = slot.clientWidth;
    if (!w) return;
    const forma = w < DOCK_COMPACTO_PX ? 'compacta' : 'amplia';
    if (slot.getAttribute('data-boberto-forma') !== forma) slot.setAttribute('data-boberto-forma', forma);
  }

  /*
   * Cuánto aire queda entre las suelas y el borde de abajo del lienzo, en
   * unidades de dibujo. MEDIDO y no calculado: se usaba PAD.b (una unidad),
   * pero los pies terminan bastante más arriba del borde del personaje, y la
   * línea del piso quedaba diez píxeles debajo de las suelas —Boberto
   * flotando sobre el horizonte, justo lo que el horizonte tenía que evitar.
   * Se pinta la pose base en un lienzo suelto y se busca la última fila
   * opaca, así un personaje o un accesorio que cuelgue más abajo mueve la
   * línea con él. Se guarda por vestuario: medir cuesta un pintado.
   */
  let _airePies = null, _airePiesClave = '';
  function airePies() {
    const clave = character + '|' + hat;
    if (_airePies !== null && _airePiesClave === clave) return _airePies;
    let u = PAD.b;
    try {
      const esc = 8;
      const cv = document.createElement('canvas');
      const cx = cv.getContext('2d');
      paintFrame(cx, 'base', esc, buildFxStyle());
      const W = cv.width, H = cv.height;
      const px = cx.getImageData(0, 0, W, H).data;
      let ultima = -1;
      for (let y = H - 1; y >= 0 && ultima < 0; y--) {
        // Umbral bajo a propósito: la suela termina en un borde suavizado, y
        // con un umbral alto la línea quedaba un par de px por encima de lo
        // último que se ve del pie.
        for (let x = 0; x < W; x++) if (px[(y * W + x) * 4 + 3] > 40) { ultima = y; break; }
      }
      if (ultima >= 0) u = (H - 1 - ultima) / (H / BOX.H);
    } catch (e) { /* sin lienzo medible queda la cuenta vieja */ }
    _airePies = u; _airePiesClave = clave;
    return u;
  }

  function ajustarDock() {
    if (!_dock || _staged || wrap.parentNode !== _dock) return;
    formaDock(_dock);
    const s = escalaDock(_dock);
    if (s && s !== _stageScale) {
      _stageScale = s;
      fitCanvas(canvas, s);
      drawF(_curFrame);
    }
    // El aire bajo los pies viaja a la hoja del dock: el piso sube eso para
    // quedar justo debajo de ellos a cualquier escala (50 px colapsado, 90
    // expandido), y el reflejo se corre lo mismo para nacer en la línea.
    // La unidad de dibujo en px (--plt-dock-u) va aparte: con ella la hoja
    // mide el largo del reflejo y la distancia a la lectura, que tienen que
    // crecer con el personaje y no con su aire.
    if (_stageScale) {
      _dock.style.setProperty('--plt-dock-aire', (airePies() * _stageScale).toFixed(2) + 'px');
      _dock.style.setProperty('--plt-dock-u', _stageScale + 'px');
      // El alto del personaje: el fondo sube eso desde el piso, más el aire
      // de arriba del slot, para llegar al filete y no pasarlo.
      _dock.style.setProperty('--plt-dock-lienzo-h', (BOX.H * _stageScale).toFixed(2) + 'px');
    }
    // El alto del slot CON Boberto adentro, medido: lo usa el estado
    // "prestado" para conservarlo cuando él se va al estudio. Se mide en vez
    // de calcularse porque el recuadro suma padding, borde y rótulo, y una
    // cuenta escrita acá se separaría sola de la hoja.
    const h = _dock.offsetHeight;
    if (h) _dock.style.setProperty('--plt-dock-h', h + 'px');
    // Dónde está la línea del piso, medida desde el borde de abajo del slot:
    // el fondo es hijo del SLOT (no del piso, ver prepararDock) y se apoya
    // ahí. Se mide por lo mismo que el alto: debajo de la línea están el
    // rótulo y el padding, y una cuenta escrita se separaría de la hoja.
    // clientHeight y offsetTop cuentan desde la misma caja (la de padding),
    // que es contra la que se resuelve el `bottom` de un absoluto.
    const piso = _dock.querySelector(':scope > .plt-dock-piso');
    if (piso && piso.offsetParent === _dock) {
      const b = _dock.clientHeight - piso.offsetTop;
      if (b > 0) _dock.style.setProperty('--plt-dock-piso-b', b + 'px');
    }
  }

  function onDockClick(e) {
    if (_staged || !enabled) return;
    e.preventDefault();
    abrirEstudio(e.currentTarget);
  }
  function onDockKey(e) {
    if (e.key !== 'Enter' && e.key !== ' ' && e.key !== 'Spacebar') return;
    if (_staged || !enabled) return;
    e.preventDefault();
    abrirEstudio(e.currentTarget);
  }

  // Decora un slot nuevo una sola vez: rol de botón, nombre accesible y sus
  // listeners. La marca vive en el nodo, así que un slot recreado por la app
  // se decora de nuevo y uno reusado no acumula listeners.
  function prepararDock(slot) {
    if (slot._bobDock) return;
    slot._bobDock = true;
    if (!slot.hasAttribute('role')) slot.setAttribute('role', 'button');
    if (!slot.hasAttribute('tabindex')) slot.setAttribute('tabindex', '0');
    if (!slot.hasAttribute('aria-label')) slot.setAttribute('aria-label', 'Personalizar a Boberto');
    if (!slot.hasAttribute('title')) slot.setAttribute('title', 'Personalizar a Boberto');
    slot.setAttribute('aria-haspopup', 'dialog');
    // La lectura ("BOBERTO ··· <dato>") y el piso donde se para. Son del
    // motor y no de cada app por la misma razón que el escenario: es uno solo
    // en las tres. aria-hidden porque el nombre accesible del botón ya es el
    // aria-label; leídos, se anunciarían dos veces. Los textos van con
    // textContent: nada de markup armado a mano.
    //
    // El lado derecho son DOS textos en la misma celda: el dato vivo (qué
    // lleva puesto, qué gesto está haciendo, dónde está) y la invitación
    // ("Personalizar"), que lo reemplaza al pasar el mouse o con el foco. Se
    // alternan por CSS para que el cambio no mueva el renglón.
    if (!slot.querySelector(':scope > .plt-dock-rotulo')) {
      const rot = document.createElement('span');
      rot.className = 'plt-dock-rotulo';
      rot.setAttribute('aria-hidden', 'true');
      const nom = document.createElement('span');
      nom.className = 'plt-dock-nombre';
      nom.textContent = 'Boberto';
      const lectura = document.createElement('span');
      lectura.className = 'plt-dock-lectura';
      const dato = document.createElement('span');
      dato.className = 'plt-dock-dato';
      const pista = document.createElement('span');
      pista.className = 'plt-dock-pista';
      pista.textContent = 'Personalizar';
      lectura.appendChild(dato); lectura.appendChild(pista);
      rot.appendChild(nom); rot.appendChild(lectura);
      // El piso: la línea de horizonte, con la barra neón del colofón adentro
      // (.plt-dock-barra recibe las MISMAS reglas, smg_edge_bar_css()). Son
      // dos elementos porque la barra necesita overflow:hidden para recortar
      // el realce, y la luz que derrama sobre el piso cuelga por fuera de
      // sus 2 px.
      const piso = document.createElement('span');
      piso.className = 'plt-dock-piso';
      piso.setAttribute('aria-hidden', 'true');
      // El fondo es hijo directo del SLOT, no del piso. Colgaba del piso
      // (bottom: 100%) y quedaba detrás de Boberto sólo mientras el piso no
      // tuviera z-index ni transform: con cualquiera de los dos el piso arma
      // su propio contexto de apilado, el fondo queda adentro y se pinta
      // ENCIMA del personaje. Como hijo del slot (que es isolation: isolate)
      // su z-index se compara directo contra el del wrap, y nada de lo que se
      // le haga al piso lo puede subir. Se apoya en la línea con
      // --plt-dock-piso-b, que mide ajustarDock. Nace vacío: lo llena
      // aplicarFondo.
      const capa = document.createElement('span');
      capa.className = 'plt-dock-fondo';
      capa.setAttribute('aria-hidden', 'true');
      slot.insertBefore(capa, slot.firstChild);
      // El marco es hijo del slot, encima de todo y sin eventos: el click
      // sigue abriendo el estudio. Nace sin modelo; aplicarBorde lo prende.
      if (!slot.querySelector(':scope > .plt-borde-marco')) {
        const marco = document.createElement('i');
        marco.className = 'plt-borde-marco';
        marco.setAttribute('aria-hidden', 'true');
        slot.appendChild(marco);
      }
      // El reflejo va ANTES que la barra: así la línea se pinta encima y el
      // reflejo nace debajo de ella (ver pintarReflejo).
      const reflejo = document.createElement('canvas');
      reflejo.className = 'plt-dock-reflejo';
      reflejo.setAttribute('aria-hidden', 'true');
      piso.appendChild(reflejo);
      const barra = document.createElement('i');
      barra.className = 'plt-dock-barra';
      piso.appendChild(barra);
      // Casi en fase con el colofón: los dos recorren en 7 s, y el del
      // colofón arrancó con la página. Un desfase negativo igual al tiempo
      // transcurrido pone este realce en el mismo punto del ciclo, así las
      // dos barras se leen como un mismo pulso y no como dos que compiten.
      piso.style.setProperty('--plt-piso-fase', -Math.round(performance.now() % 7000) + 'ms');
      slot.appendChild(rot); slot.appendChild(piso);
    }
    slot.addEventListener('click', onDockClick);
    slot.addEventListener('keydown', onDockKey);
    slot.addEventListener('mousemove', onDockMove, { passive: true });
    slot.addEventListener('mouseleave', onDockLeave, { passive: true });
  }

  /*
   * Pone el wrap en el slot vivo. Idempotente y barato cuando no hay nada que
   * hacer, porque corre con cada mutación del documento.
   */
  function adoptarDock() {
    if (_dock && _dock.isConnected && (_staged || wrap.parentNode === _dock)) return;
    const slot = document.querySelector(DOCK_SEL);
    if (!slot) { _dock = null; return; }
    if (slot !== _dock) {
      prepararDock(slot);
      _dock = slot;
      if (_dockRO) _dockRO.disconnect();
      if ('ResizeObserver' in window) {
        // El ancho cambia al colapsar la sidebar sin que la ventana se mueva:
        // ahí `resize` no dispara nunca.
        _dockRO = new ResizeObserver(ajustarDock);
        _dockRO.observe(slot);
      }
    }
    estadoDock();
    // Prestado al estudio: el wrap vuelve con goHome(), que mira _dock.
    if (_staged) return;
    slot.appendChild(wrap);
    wrap.style.display = enabled ? '' : 'none';
    _stageScale = 0;     // fuerza a remedir en el slot nuevo
    ajustarDock();
    if (!_stageScale) { _stageScale = SC; fitCanvas(canvas, SC); }
    drawF('base');
    canvas.classList.add('plt-idle');
  }

  /*
   * Abre el estudio (shared/js/boberto-estudio.js) con foco de vuelta en
   * `origen`. Si la página no cargó el módulo, tocarlo hace lo de siempre: un
   * gesto al azar. Un botón que no hace nada se lee como roto.
   */
  function abrirEstudio(origen) {
    const E = window.BOBERTO_ESTUDIO;
    if (E && typeof E.abrir === 'function') return E.abrir({ origen: origen || _dock || null }) !== false;
    gestoAlAzar();
    return false;
  }

  // ── Init ──────────────────────────────────────────────────────────────
  let _inited = false;
  function init() {
    if (_inited) return;
    _inited = true;
    if (document.body && !bubble.parentNode) document.body.appendChild(bubble);

    canvas.style.filter = staticBodyFilter();
    drawF('base');
    canvas.classList.add('plt-idle');
    startBlinkTimer();
    startFxTicker();

    adoptarDock();
    new MutationObserver(adoptarDock).observe(document.documentElement, { childList: true, subtree: true });
    bucleDock();

    // La burbuja está en coordenadas de la ventana: si la ventana cambia,
    // se la vuelve a ubicar junto al personaje.
    window.addEventListener('resize', () => { if (bubbleActive) ubicarBurbuja(); }, { passive: true });
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', init);
  } else {
    init();
  }

  /* == Espejos ==========================================================
   *
   * Un espejo es un canvas AJENO donde Boberto se dibuja con su propio bucle de
   * poses, sin tocar al del topbar. Existe porque el personaje tiene que poder
   * estar en dos lados a la vez: la mascota del topbar no se toca NUNCA, y cada
   * peana refleja una copia.
   *
   * Es lo contrario de la vitrina (`mountStage`), que MUDA el unico
   * `#plt-wrap`. La vitrina sigue existiendo para la landing, que no tiene
   * topbar del cual sacarlo y quiere la coreografia completa con particulas.
   *
   * Lo que un espejo hace: todas las coreografias, con sus particulas y
   * sus animaciones CSS — los keyframes apuntan a `canvas.plt-*`, sin
   * `#plt-wrap` adelante, asi que valen en cualquier canvas.
   *
   * Lo unico que NO hace es desplazarse: no camina, y el skate rueda en el lugar.
   * Eso es lo que lo hace seguro de meter en cualquier hueco de cualquier
   * pantalla, y es tambien la unica diferencia con el del topbar.
   */
  const _espejos = new Set();

  /*
   * El vestuario fijo que piden unas opciones {hat, character}, o null si no
   * piden ninguno (= el elegido por la persona).
   *
   * Hay pantallas que pintan a Boberto con otro vestuario que el elegido —las
   * miniaturas de la sección Skins de Ajustes— y ese vestuario no puede pisar
   * la preferencia de la persona: ni en localStorage ni en el topbar.
   *
   * Las reglas, y por qué:
   *
   * 1. `character`: un id del catálogo es ese personaje; '', 'none' o null es
   *    "sin personaje". undefined es "no lo digo".
   * 2. Un `hat` o un `character` retirado (rick, pikachu…) se traduce a su
   *    disfraz, o a ninguno: hay pantallas escritas antes de que se fueran.
   * 3. Un `hat` fijo SIN `character` significa sin personaje. El papel manda:
   *    un Boberto al que se le pide la bata tiene que salir con bata aunque
   *    la persona tenga elegido un material.
   * 4. Un id que no está en el catálogo se ignora: un vestuario mal escrito no
   *    puede dejar al personaje sin nada.
   */
  function vestuarioDe(op) {
    if (!op) return null;
    let ch;
    if (op.character === null || op.character === '' || op.character === 'none') ch = '';
    else if (esCharacter(op.character)) ch = op.character;
    const pedido = esRetirado(op.hat) ? PERSONAJE_RETIRADO[op.hat]
      : (ch === undefined && esRetirado(op.character)) ? PERSONAJE_RETIRADO[op.character] : op.hat;
    const h = (pedido && HAT_PRESETS.some(x => x.id === pedido)) ? pedido : null;
    if (ch === undefined && h) ch = '';
    if (ch === undefined && !h) return null;
    return { hat: h, character: ch === undefined ? null : ch };
  }

  /*
   * Pinta con OTRO vestuario (ver vestuarioDe) sin tocar el elegido.
   *
   * Se cambian `hat` y `character` SÓLO mientras dura el pintado, y se
   * devuelven aunque el pintor falle. `fn` puede devolver algo: lo usan los
   * filtros de los espejos, que dependen de si hay personaje.
   */
  function conVestuario(v, fn) {
    if (!v) return fn();
    const nuevoHat = v.hat || hat;
    const nuevoChar = v.character == null ? character : v.character;
    if (nuevoHat === hat && nuevoChar === character) return fn();
    const hatPropio = hat, charPropio = character;
    hat = nuevoHat; character = nuevoChar;
    try { resolveHatColors(); return fn(); }
    finally { hat = hatPropio; character = charPropio; resolveHatColors(); }
  }

  // El filtro de brillo de un espejo, con SU vestuario: una peana fija en
  // un accesorio lleva el brillo de la persona aunque el topbar sea un personaje.
  function filtroEspejo(esp, mb) {
    return conVestuario(esp.vest, () => (mb == null
      ? staticBodyFilter(esp.escala)
      : bodyFilter(mb, esp.escala)));
  }

  function crearEspejo(cnv, escala, opciones) {
    const op = opciones || {};
    const esp = {
      cnv: cnv, ctx: cnv.getContext('2d'), escala: escala || 2,
      // Vestuario fijo de este espejo (ver vestuarioDe). null = el elegido.
      vest: vestuarioDe(op),
      activo: true, hover: null, pose: 'base',
      // Un gesto en curso manda sobre la mirada al cursor y sobre el ticker de
      // FX, igual que `isBusy` en el topbar. Sin esta bandera, un mousemove a
      // mitad de un baile le pisaba la pose con 'lookR'.
      gesto: false,
      // Cada cuanto hace un gesto. `cada` estaba documentado en peana() y no lo
      // leia nadie, porque el bucle del espejo no tenia gestos que espaciar.
      cada: op.cada === 0 || op.cada === false ? 0 : (op.cada || 7000),
      capa: null,
    };
    fitCanvas(cnv, esp.escala);
    // La marca que usa el CSS para apagarlo con la mascota (el avatar del chat
    // no vive en una peana, así que no lo alcanza la regla de .plt-peana).
    cnv.classList.add('plt-espejo');
    // staticBodyFilter() y NO un drop-shadow escrito a mano: el de antes usaba
    // getPal().glowA, o sea el color del TEMA, e ignoraba tanto el color de
    // brillo elegido como el ciclo RGB. De ahi que en las peanas el brillo
    // fuera siempre del mismo color por mas que se cambiara.
    cnv.style.filter = filtroEspejo(esp, null);
    _espejos.add(esp);
    correrEspejo(esp);
    return esp;
  }

  function pararEspejo(esp) {
    if (!esp) return;
    esp.activo = false;
    _espejos.delete(esp);
    // La capa se va con el espejo. Queda en el DOM de la app que la hospeda, y
    // un espejo soltado y vuelto a montar acumularia una capa por vez.
    if (esp.capa && esp.capa.parentNode) esp.capa.remove();
    esp.capa = null;
  }

  /** Reajusta la escala de un espejo vivo. Idempotente. */
  function reescalarEspejo(esp, escala) {
    if (!esp || !esp.activo) return;
    esp.escala = escala || esp.escala;
    fitCanvas(esp.cnv, esp.escala);
    // El halo escala con el tamano, asi que un cambio de escala tambien tiene
    // que rehacer el filtro. Si no, un Boberto reescalado se queda con el halo
    // del tamano anterior.
    if (!_fxRAF) esp.cnv.style.filter = filtroEspejo(esp, null);
    dibujarEn(esp, esp.pose || 'base');
  }

  function dibujarEn(esp, pose) {
    if (!esp || !esp.activo || !esp.ctx) return;
    pal = getPal(); resolveHatColors();
    const p = (pose && POSES[pose]) ? pose : 'base';
    // Se recuerda la pose puesta porque el ticker de FX repinta este canvas
    // cuadro a cuadro, y necesita repetir LA MISMA. Sin esto lo repintaba
    // siempre en 'base' y le pisaba el gesto en curso: a mitad de la
    // animacion de la laptop, con un efecto de brillo activo, el personaje
    // volvia a la pose neutra sesenta veces por segundo.
    esp.pose = p;
    conVestuario(esp.vest, () => paintFrame(esp.ctx, p, esp.escala, buildFxStyle()));
  }

  /**
   * La capa donde un espejo cuelga sus particulas.
   *
   * Las coreografias emiten en el espacio del TOPBAR: un cuadro de BOX.W x BOX.H
   * a SC px por unidad. La capa mide exactamente ese cuadro y se escala entera
   * con `--pt-k`, centrada sobre el canvas. Asi ninguna coordenada ni tamano de
   * fuente de todas las coreografias se reescribe —y el dia que se toque
   * una, sigue habiendo un solo lugar donde vive.
   *
   * Se reubica en CADA emision y no solo al crearla: el canvas se recentra
   * cuando cambia la escala, y una peana reajusta escala en cada resize.
   */
  function capaFx(esp) {
    if (!esp || !esp.activo || !esp.cnv) return null;
    const cnv = esp.cnv, padre = cnv.parentNode;
    if (!padre) return null;
    // Sin ancestro posicionado las particulas se van al rincon de la pagina.
    // Se mide una sola vez por espejo: getComputedStyle en cada emision, con
    // varias peanas emitiendo a la vez, es un recalculo de estilos al pedo.
    if (!esp.padreFijado) {
      if (getComputedStyle(padre).position === 'static') padre.style.position = 'relative';
      esp.padreFijado = true;
    }
    let capa = esp.capa;
    if (!capa || !capa.isConnected) {
      capa = document.createElement('div');
      capa.className = 'plt-fx-capa';
      padre.appendChild(capa);
      esp.capa = capa;
    }
    const w = BOX.W * SC, h = BOX.H * SC;
    const k = esp.escala / SC;
    capa.style.width  = w + 'px';
    capa.style.height = h + 'px';
    capa.style.left = (cnv.offsetLeft + (BOX.W * esp.escala - w) / 2) + 'px';
    capa.style.top  = (cnv.offsetTop  + (BOX.H * esp.escala - h) / 2) + 'px';
    capa.style.setProperty('--pt-k', k);
    // Escalar la capa entera acompaña bien las POSICIONES (una nota sale de la
    // guitarra a cualquier tamaño), pero no los glifos ni los recorridos: a 4x
    // un punto de 13 px medía 52 y los de "pensando" salían volando del riel.
    // Glifo y recorrido crecen como mucho al doble; '--pt-kf' es la corrección
    // que aplica el CSS de las partículas sobre la escala de la capa.
    capa.style.setProperty('--pt-kf', k > 2 ? 2 / k : 1);
    return capa;
  }

  /** El actor de un espejo: dibuja en SU canvas y emite en SU capa. */
  function actorEspejo(esp) {
    return {
      draw: (pose) => dibujarEn(esp, pose),
      el: esp.cnv,
      emit: (kind, opts) => emitirEn(capaFx(esp), kind, opts),
      burst: (kind, n, optsFn) => burstEn(() => capaFx(esp), kind, n, optsFn),
      // No hay a donde mirar: el flip vive en `#plt-wrap`, y un espejo es un
      // canvas suelto cuyo transform se lo llevan las animaciones CSS.
      mirar: () => {},
    };
  }

  /**
   * El bucle de un espejo: el repertorio COMPLETO del personaje.
   *
   * Antes eran siete ramas escritas a mano —parpadeo, happy, un squash, think,
   * la laptop y una mirada alrededor— mientras el topbar corria veinticuatro
   * coreografias. O sea que en las peanas, que es donde Boberto se ve grande y es
   * donde uno mira, faltaban dieciocho animaciones que ya existian.
   *
   * Ahora elige del mismo catalogo, con su propio actor: cada espejo tiene su
   * juego de coreografias atado a SU canvas y a SU capa de particulas, asi que
   * varias peanas y el topbar pueden estar haciendo cosas distintas a la vez.
   *
   * El bucle es de setTimeout y no de requestAnimationFrame a proposito: en una
   * pestana de fondo no llegan cuadros, y un bucle que espera uno se congela.
   * Las animaciones CSS y las particulas si se congelan ahi —no hay nada que
   * hacer— pero las poses siguen, que es lo que sostiene al personaje vivo.
   */
  async function correrEspejo(esp) {
    const D = (pose) => dibujarEn(esp, pose);
    const G = coreografias(actorEspejo(esp));
    // El pool comun mas `skate`, que quedo afuera del pool porque el Boberto
    // del topbar ya caminaba solo. Parado quieto es la unica forma de rodar.
    const repertorio = G.pool.concat([G.skate]);

    D('base');
    await wait(rand(1000, 3000));

    while (esp.activo && esp.ctx) {
      // Un gesto o una pose pedidos desde afuera (peanaGesto / peanaPose)
      // mandan: el bucle espera a que terminen en vez de pisarlos con su
      // rutina. Adentro del bucle `gesto` nunca llega prendido a esta linea
      // (se prende y se apaga en la misma vuelta), asi que para las peanas que
      // nadie maneja esto no cambia nada.
      if (esp.gesto) { await wait(120); continue; }

      // El filtro lo mantiene fxStep() cuadro a cuadro cuando hay efectos
      // activos; aca solo se refresca el estatico, por si cambio el color
      // elegido mientras el bucle estaba esperando.
      if (!_fxRAF) esp.cnv.style.filter = filtroEspejo(esp, null);

      // La mirada manda sobre todo lo demas: con el cursor cerca, Boberto lo
      // sigue en vez de seguir con su rutina.
      if (esp.hover) { D(esp.hover); await wait(100); continue; }

      // Quieto por pedido de quien la monto (`cada: 0`): solo mira y parpadea.
      if (!esp.cada) {
        D(Math.random() < 0.5 ? 'blink' : 'wink'); await wait(90); D('base');
        await wait(rand(1600, 3400));
        continue;
      }

      const gesto = G.elegir(repertorio);
      esp.gesto = true;
      try {
        await gesto();
      } catch (e) {
        // Una coreografia que falla no puede matar el bucle: el espejo quedaria
        // congelado en la pose a medio camino para el resto de la sesion.
      } finally {
        esp.gesto = false;
        // Casi todas las coreografias arrancan sacando `plt-idle` —el balanceo
        // de reposo— y ninguna lo repone: eso lo hace quien las llama (el
        // bucle del dock, playGesture). Sin esto la primera animacion le apagaba el reposo al
        // espejo para el resto de la sesion.
        esp.cnv.classList.add('plt-idle');
      }
      if (!esp.activo) break;

      D('base');
      // Alrededor de `cada`, no exactamente: varias peanas en la misma pantalla
      // con el mismo periodo se sincronizan y se ve como una coreografia de
      // grupo, que no es la idea.
      await wait(rand(Math.round(esp.cada * 0.6), Math.round(esp.cada * 1.3)));
    }
  }

  /*
   * Redibuja todos los espejos. Lo llaman los eventos de piel y de efectos: sin
   * esto un espejo se queda con la paleta vieja hasta su proxima pose, que
   * puede ser dentro de siete segundos.
   */
  function refrescarEspejos() {
    // Con la pose puesta y no con 'base': un cambio de paleta a mitad de un
    // gesto le devolvia la pose neutra y se veia como un salto.
    _espejos.forEach((esp) => { if (esp.activo) dibujarEn(esp, esp.pose || 'base'); });
  }

  // == El avatar del chatbot es un espejo ================================
  // Se conserva la API publica (startChatAnimation / stopChatAnimation /
  // setChatHoverOverride) porque la usan chatbot.js y finder.js de JiraToolkit.
  let _espejoChat = null;

  function startChatAnimation(cnv, scale) {
    if (!cnv) return;
    if (_espejoChat && _espejoChat.cnv === cnv && _espejoChat.activo) {
      reescalarEspejo(_espejoChat, scale || 2);
      return;
    }
    pararEspejo(_espejoChat);
    _espejoChat = crearEspejo(cnv, scale || 2);
  }

  function stopChatAnimation() {
    pararEspejo(_espejoChat);
    _espejoChat = null;
  }

  function setChatHoverOverride(frame) {
    if (!_espejoChat) return;
    _espejoChat.hover = frame || null;
    dibujarEn(_espejoChat, frame || 'base');
  }

  /* ── Vitrina: Boberto fuera del dock ───────────────────────────────────
   *
   * La landing y el estudio quieren mostrarlo en grande y dejar disparar sus
   * gestos. La tentación era escribirles una animación aparte; sería la
   * cuarta copia de algo que ya existe.
   *
   * En vez de eso se MUDA el mismo Boberto. Se puede porque `wrap` y `canvas`
   * se crean al evaluar el archivo y sólo se cuelgan de un slot cuando la app
   * declara uno: en una página sin dock quedan vivos y sueltos. Enganchándolos
   * a otro contenedor, los veintipico de gestos, las partículas y las
   * animaciones CSS funcionan tal cual, sin una línea nueva de coreografía.
   *
   * La clase `plt-vitrina` marca el préstamo; al soltarlo, goHome() lo
   * devuelve al slot VIVO, que no tiene por qué ser el mismo nodo del que
   * salió (JiraToolkit recrea su sidebar).
   */

  // Catálogo público de gestos. El id es el que se le pasa a playGesture(); el
  // nombre y el ícono los muestra quien arme la interfaz.
  //
  // `key` nombra la coreografía, no la ejecuta: la misma entrada sirve para el
  // topbar y para un espejo, que tienen su propio juego de coreografías atado a
  // su propio canvas. Antes esto guardaba la función del singleton, que es
  // justamente lo que hacía imposible correr un gesto en otro lado.
  const GESTOS = [
    { id: 'saluda',    name: 'Saluda',     icon: '👋', key: 'wave' },
    { id: 'baila',     name: 'Baila',      icon: '🕺', key: 'dance' },
    { id: 'festeja',   name: 'Festeja',    icon: '🎉', key: 'celebrate' },
    { id: 'enamorado', name: 'Enamorado',  icon: '❤️', key: 'love' },
    { id: 'mareado',   name: 'Mareado',    icon: '💫', key: 'dizzy' },
    { id: 'duerme',    name: 'Siesta',     icon: '😴', key: 'sleep' },
    { id: 'estornuda', name: 'Estornuda',  icon: '🤧', key: 'sneeze' },
    { id: 'lupa',      name: 'Investiga',  icon: '🔍', key: 'magnify' },
    { id: 'cafe',      name: 'Café',       icon: '☕', key: 'coffee' },
    { id: 'laptop',    name: 'Programa',   icon: '💻', key: 'laptop' },
    { id: 'guitarra',  name: 'Solo',       icon: '🎸', key: 'guitar' },
    { id: 'skate',     name: 'Skate',      icon: '🛹', key: 'skate' },
    { id: 'levita',    name: 'Levita',     icon: '✨', key: 'levitate' },
    { id: 'escanea',   name: 'Analiza',    icon: '📡', key: 'scan' },
    { id: 'piensa',    name: 'Piensa',     icon: '💭', key: 'think' },
    { id: 'burbuja',   name: 'Burbuja',    icon: '🫧', key: 'bubble' },
    { id: 'pesca',     name: 'Pesca',      icon: '🎣', key: 'pesca' },
    { id: 'pintor',    name: 'Pintor',     icon: '🎨', key: 'pintor' },
    { id: 'malabares', name: 'Malabares',  icon: '🤹', key: 'malabares' },
    { id: 'yoyo',      name: 'Yoyó',       icon: '🪀', key: 'yoyo' },
    { id: 'jueguito',  name: 'Jueguito',   icon: '⚽', key: 'jueguito' },
    { id: 'mate',      name: 'Mate',       icon: '🧉', key: 'mate' },
    { id: 'avion',     name: 'Avión',      icon: '✈️', key: 'avion' },
    { id: 'rubik',     name: 'Rubik',      icon: '🧊', key: 'rubik' },
    { id: 'selfie',    name: 'Selfie',     icon: '🤳', key: 'selfie' },
  ];

  let _vitrinaAuto = null;
  // Ya no se anota "de dónde vino": la casa es siempre el dock VIVO (_dock).
  // Anotar el nodo de salida lo dejaba sin camino de vuelta cuando la app
  // recreaba la sidebar con el estudio abierto.

  /**
   * Engancha a Boberto dentro de `host` y lo deja andando.
   *
   * `enabled` se fuerza en memoria y NO se persiste: quien pone la vitrina en
   * su página la está pidiendo, pero eso no puede prenderle la mascota en el
   * dock de las otras apps a quien la había apagado.
   */
  /**
   * Pila de reclamos de la vitrina.
   *
   * Las PEANAS ya no la usan: desde que cada una dibuja su propio canvas con un
   * espejo, no hay recurso unico que pelear y pueden coexistir. La pila quedo
   * para la vitrina, que sigue mudando el wrap, y se conserva porque es lo que
   * garantiza que una vitrina efimera devuelva la mascota al lugar correcto (al
   * reclamo de abajo si habia otro, al dock si no).
   *
   * Es LIFO: manda el reclamo más nuevo, y al soltarlo Boberto baja al que
   * quedó abajo. Sólo cuando no queda ninguno vuelve al dock.
   */
  const _stage = [];

  /**
   * ¿Este elemento tiene una caja dibujada?
   *
   * Se mide con getClientRects() y NO con offsetParent, que es la trampa obvia
   * y está mal: `offsetParent` vale null para cualquier elemento
   * `position: fixed`, así que una peana fija —un modal propio, un panel
   * flotante— quedaba rechazada como si estuviera oculta. getClientRects()
   * devuelve cero rectángulos sólo cuando el elemento de verdad no se dibuja
   * (display:none, o un ancestro con display:none), que es lo que importa acá:
   * mudar a Boberto a un contenedor sin caja lo hace desaparecer de toda la app.
   */
  function tieneCaja(el) {
    if (!el || !el.isConnected) return false;
    // Se pregunta si el elemento se DIBUJA, no si tiene área. Son cosas
    // distintas y confundirlas cuesta caro: el escenario de una peana está
    // vacío hasta que Boberto entra, así que mide cero de ancho, y exigiéndole
    // área el primer montaje siempre fallaba. Quien decide si la caja da el
    // tamaño es peana(), que mide la caja de la APP — la que tiene el alto.
    return el.getClientRects().length > 0;
  }

  function _stageIndex(host) {
    for (let i = _stage.length - 1; i >= 0; i--) if (_stage[i].host === host) return i;
    return -1;
  }

  /**
   * Reclama la vitrina para `host`. Idempotente: volver a llamar con el mismo
   * host actualiza su escala en vez de apilar otro reclamo — la landing llama a
   * esto en cada `resize`, y apilando crecería para siempre.
   */
  function claimStage(host, scale, opciones) {
    if (!host || !canvas) return false;
    const i = _stageIndex(host);
    if (i !== -1) _stage.splice(i, 1);
    _stage.push({ host: host, scale: scale || SC, opts: opciones || {} });
    return applyStage();
  }

  /**
   * Suelta el reclamo de `host`. Sin argumento suelta TODOS — es la salida de
   * emergencia y la que usa unmountStage() para volver al dock de una.
   */
  function releaseStage(host) {
    if (host === undefined) _stage.length = 0;
    else {
      const i = _stageIndex(host);
      if (i === -1) return false;
      _stage.splice(i, 1);
    }
    return _stage.length ? applyStage() : goHome();
  }

  /**
   * Pone a Boberto en el reclamo de arriba de la pila.
   *
   * Descarta de paso los reclamos cuyo contenedor ya no tiene caja: un modal que
   * se cerró sacándose una clase, sin llamar a releaseStage(), dejaría al
   * personaje atrapado en un `display:none` para el resto de la sesión. Esto lo
   * cura solo en el próximo reclamo o suelte.
   */
  /*
   * Reintento del reclamo de vitrina.
   *
   * Hace falta porque "el host todavia no midio" y "el host ya no existe" son
   * cosas distintas, y confundirlas costaba la vitrina entera: la landing
   * reclama en cuanto tiene el nodo, que puede ser antes de que el layout
   * resuelva su ancho, y descartar ahi el reclamo lo tiraba a la basura.
   *
   * Con techo (~6 s): un host conectado que nunca va a medir —escondido por una
   * media query a proposito— no puede dejar un timer girando para siempre.
   */
  let _stageTimer = null, _stageTiros = 0;
  function reintentarStage() {
    if (_stageTimer || _stageTiros >= 40) return;
    _stageTimer = setTimeout(() => {
      _stageTimer = null;
      _stageTiros++;
      applyStage();
    }, 150);
  }

  function applyStage() {
    // Se descartan SOLO los reclamos cuyo host ya no esta en el documento —un
    // contenedor destruido— y los que tienen otro reclamo debajo al que ceder,
    // que es lo que hace que un modal cerrado devuelva la mascota a la vista.
    //
    // Un host conectado, sin caja y UNICO se conserva: todavia puede medir.
    while (_stage.length) {
      const top = _stage[_stage.length - 1];
      if (!top.host.isConnected) { _stage.pop(); continue; }
      if (tieneCaja(top.host)) break;
      if (_stage.length > 1) { _stage.pop(); continue; }
      reintentarStage();
      return false;
    }
    if (!_stage.length) return goHome();

    _stageTiros = 0;
    const cur = _stage[_stage.length - 1];
    const op = cur.opts;

    enabled = true;
    _staged = true;
    isNear = false;
    hideAdvice();
    wrap.classList.add('plt-vitrina');
    wrap.style.display = '';
    if (wrap.parentNode !== cur.host) cur.host.appendChild(wrap);
    estadoDock();

    _stageScale = cur.scale;
    fitCanvas(canvas, cur.scale);
    canvas.style.filter = staticBodyFilter();
    paused = false;
    drawF('base');
    canvas.classList.add('plt-idle');
    startFxTicker();
    startBlinkTimer();

    // Un gesto suelto cada tanto, para que no sea una estatua esperando clicks.
    // El bucle del dock está cedido mientras `_staged` esté puesto.
    if (_vitrinaAuto) clearInterval(_vitrinaAuto);
    if (op.autoplay !== false) {
      _vitrinaAuto = setInterval(() => {
        if (isBusy || paused || document.hidden) return;
        const g = coreoPropia().elegir();
        isBusy = true;
        canvas.classList.remove('plt-idle');
        Promise.resolve(g()).catch(() => {}).then(() => {
          isBusy = false;
          if (!paused) { drawF('base'); canvas.classList.add('plt-idle'); }
        });
      }, op.cada || 7000);
    }
    return true;
  }

  // Nombre histórico. La landing lo usa y no le interesa la pila: su vitrina es
  // permanente y es el único reclamo de esa página.
  const mountStage = claimStage;

  // ── Peana: el mueble, armado y reclamado de una ────────────────────────
  const PEANA_MIN_SC = 4;
  const PEANA_MAX_SC = 9;

  /**
   * Arma la peana dentro de `caja` y para a Boberto ahí. Devuelve si lo logró.
   * Con la mascota apagada devuelve true sin montar nada: la caja queda
   * reservada, el CSS la esconde y se monta sola al prenderla.
   *
   * Existe para que una app no tenga que repetir el markup, el CSS ni el cálculo
   * de escala: se le pasa un contenedor vacío y listo. Es idempotente, así que
   * también sirve como "reajustá la escala" después de un resize.
   *
   * La escala se DERIVA del alto real de la caja en vez de ir clavada: el
   * personaje mide 18 unidades, así que a 8 px por unidad son 144, y en una caja
   * más baja esa escala le cortaría la cabeza. De este modo el alto se decide en
   * un solo lugar —el CSS de quien la hospeda— y el motor lo sigue.
   *
   * opciones:
   *   pie       — borde arriba, para cuando la peana cierra un panel
   *   plana     — sin halo, para peanas dentro de una tarjeta ya coloreada
   *   escalaMax — techo, si la caja es alta pero no querés un Boberto gigante
   *   cada      — cada cuántos ms hace un gesto suelto (0/false para quieto)
   *   hat       — vestuario fijo (un id de listHats). Se pinta con ese
   *               accesorio sin cambiar el elegido y, si no se pasa
   *               `character`, sin personaje (ver vestuarioDe)
   *   character — personaje fijo (un id de listCharacters, o '' para
   *               ninguno), sin cambiar el elegido
   */
  /*
   * Espejo activo por caja de peana.
   *
   * Un Map y no un WeakMap: hay que RECORRERLAS en cada mousemove para decidir
   * la mirada, y un WeakMap no es iterable. No filtra memoria porque
   * soltarPeana() borra la entrada, y esa llamada ya es obligatoria por otros
   * motivos.
   */
  const _peanas = new Map();
  /*
   * Peanas pedidas (o dormidas) con la mascota apagada, con sus opciones.
   *
   * Apagada, la caja no se dibuja (html[data-boberto="off"]), así que no hay
   * nada que medir ni dónde pararse. La caja queda anotada acá y se monta
   * cuando la vuelven a prender: sin esto, prenderla desde Ajustes dejaba
   * vacías todas las peanas que la app había pedido mientras estaba apagada.
   */
  const _peanasEnEspera = new Map();

  function peana(caja, opciones) {
    if (!caja) return false;
    const op = opciones || {};

    caja.classList.add('plt-peana');
    if (op.pie)   caja.classList.add('plt-peana-pie');
    if (op.plana) caja.classList.add('plt-peana-plana');

    let stage = caja.querySelector(':scope > .plt-peana-stage');
    if (!stage) {
      stage = document.createElement('div');
      stage.className = 'plt-peana-stage';
      caja.appendChild(stage);
    }

    // Después de poner la clase y no antes: con ella, el CSS del motor apaga
    // la caja entera (halo y piso incluidos) en vez de dejar el hueco.
    //
    // Y true, no false: para las apps, false es "no entra", y lo que hacen
    // con eso es ocultar la caja (novedades, JiraToolkit). Una caja oculta por
    // la app no se puede medir, así que al prender la mascota no había dónde
    // montarla. Esconderla es trabajo de este CSS, no de cada app.
    if (!prefEncendido()) { _peanasEnEspera.set(caja, op); return true; }

    // Sin caja dibujada no hay peana. Se pregunta por la caja de la APP, que es
    // la que tiene el alto: el escenario esta vacio hasta que entra el canvas.
    if (!tieneCaja(caja)) return false;

    // El aire de abajo se lee del propio elemento y no se repite aca: si el CSS
    // lo cambia, la escala se recalcula sola.
    const aire = parseFloat(getComputedStyle(stage).paddingBottom) || 0;
    const libreY = caja.clientHeight - aire;
    const libreX = caja.clientWidth;

    // Se miden las DOS dimensiones, no solo el alto.
    //
    // El ancho importa porque una app puede apagar su peana angostandola — el
    // panel de IA del errorlog la apaga con `flex-basis: 0`, no con
    // `display: none`, para no pelear la cascada con el CSS del motor — y ahi
    // hay alto de sobra y cero lugar donde pararse.
    //
    // Y de paso, en una caja angosta pero visible se ACHICA en vez de
    // desbordarse: manda la dimension mas apretada.
    if (libreY < BOX.H * PEANA_MIN_SC || libreX < BOX.W * PEANA_MIN_SC) return false;

    const techo = Math.min(PEANA_MAX_SC, op.escalaMax || PEANA_MAX_SC);
    const cabe = Math.min(Math.floor(libreY / BOX.H), Math.floor(libreX / BOX.W));
    const esc = Math.max(PEANA_MIN_SC, Math.min(techo, cabe));

    /*
     * Canvas PROPIO, y no el del topbar.
     *
     * Antes esto reclamaba la vitrina, que MUDA el unico `#plt-wrap`: mientras
     * una peana estuviera abierta, el topbar se quedaba sin mascota. Con un
     * espejo hay dos dibujos del mismo personaje —misma piel, mismo gorro,
     * mismos efectos, porque los lee de la misma configuracion— y el del topbar
     * no se toca nunca.
     */
    let esp = _peanas.get(caja);
    _peanasEnEspera.delete(caja);
    if (esp && esp.activo) { esp.vest = vestuarioDe(op); esp.op = op; reescalarEspejo(esp, esc); return true; }

    let cnv = stage.querySelector(':scope > canvas');
    if (!cnv) {
      cnv = document.createElement('canvas');
      cnv.className = 'plt-peana-cnv plt-idle';
      stage.appendChild(cnv);
    }
    esp = crearEspejo(cnv, esc, { cada: op.cada, hat: op.hat, character: op.character });
    // Las opciones se guardan para volver a montarla igual después de un
    // apagado (ver sincronizarApagado).
    esp.op = op;
    _peanas.set(caja, esp);
    seguirCursor(caja, esp);
    return true;
  }

  /** Suelta la peana de `caja`. Contraparte obligatoria de peana(). */
  function soltarPeana(caja) {
    if (!caja) return false;
    // También si estaba esperando: una caja soltada no se monta al prender.
    const enEspera = _peanasEnEspera.delete(caja);
    const esp = _peanas.get(caja);
    if (!esp) return enEspera;
    pararEspejo(esp);
    _peanas.delete(caja);
    return true;
  }

  /*
   * Apagada en Ajustes, la mascota se va de TODOS lados, no sólo del dock.
   *
   * Las peanas (modales, login, guía, novedades) y el avatar del chat son
   * espejos con canvas propio, y ninguno miraba `enabled`: destildar "Mostrar
   * a Boberto" lo sacaba de la sidebar y lo dejaba en todo lo demás.
   *
   * La preferencia se publica en <html data-boberto="off">. Con eso el CSS del
   * motor apaga peanas y espejos, y una app puede colgar lo suyo del mismo
   * atributo (JiraToolkit, el dibujo de sus estados vacíos). Se lee de storage
   * y no de `enabled`: la vitrina fuerza `enabled` en memoria para mostrarse,
   * y eso no es lo que eligió la persona.
   */
  function prefEncendido() {
    try { return localStorage.getItem(LS_KEY) !== 'false'; } catch (e) { return true; }
  }
  function publicarApagado() {
    const h = document.documentElement;
    if (prefEncendido()) h.removeAttribute('data-boberto');
    else h.setAttribute('data-boberto', 'off');
  }
  function sincronizarApagado() {
    publicarApagado();
    if (prefEncendido()) {
      // Ya sin el atributo, las cajas se dibujan y se pueden medir. La que
      // todavía no entra (un modal cerrado) sigue esperando: la app la vuelve
      // a pedir al abrirlo, y ahí se monta. La que ya no está en el DOM se va.
      Array.from(_peanasEnEspera).forEach(([caja, op]) => {
        if (!caja.isConnected) _peanasEnEspera.delete(caja);
        else peana(caja, op);
      });
    } else {
      // Se duermen en vez de seguir dibujando en una caja que no se ve.
      _peanas.forEach((esp, caja) => {
        _peanasEnEspera.set(caja, esp.op || {});
        pararEspejo(esp);
      });
      _peanas.clear();
    }
  }

  /*
   * Manejar una peana desde la app: un gesto del catalogo o una pose sostenida.
   *
   * playGesture() actua sobre el topbar, y hay pantallas que necesitan que
   * reaccione la peana que tienen al lado (la guia de Buenas practicas festeja
   * un checklist completo en SU Boberto, no en el del topbar). Las dos corren
   * con el actor del espejo —su canvas, su capa de particulas—, asi que no hay
   * una sola linea de dibujo nueva: es el mismo repertorio de siempre.
   *
   * Devuelven false si la peana no esta montada o ya esta en medio de otro
   * gesto: la app decide que hacer (casi siempre, nada).
   *
   * Una POSE sostenida es blanda: otro pedido (pose o gesto) la reemplaza en
   * vez de rebotar. Existe por la guia de Buenas practicas, que da clase
   * encadenando poses cortas: si esas poses bloquearan como un gesto, el
   * festejo de un quiz bien respondido llegaria tarde o se perderia. Un gesto
   * sigue siendo duro: nada lo corta a la mitad.
   *
   * `turno` es lo que hace seguro el reemplazo: la pose reemplazada sigue
   * esperando su setTimeout, y cuando vence ya no es la ultima, asi que no
   * devuelve al personaje a la pose neutra encima de lo que vino despues.
   */
  function correrEnPeana(esp, fn, blanda) {
    const turno = (esp.turno || 0) + 1;
    esp.turno = turno;
    esp.gesto = true;
    esp.blanda = !!blanda;
    esp.cnv.classList.remove('plt-idle');
    Promise.resolve().then(fn).catch(() => {}).then(() => {
      if (esp.turno !== turno) return;
      esp.gesto = false;
      esp.blanda = false;
      if (!esp.activo) return;
      esp.cnv.classList.add('plt-idle');
      dibujarEn(esp, esp.hover || 'base');
    });
    return true;
  }

  function peanaGesto(caja, id) {
    const esp = _peanas.get(caja);
    const g = GESTOS.find(x => x.id === id);
    if (!esp || !esp.activo || (esp.gesto && !esp.blanda) || !g) return false;
    const fn = coreografias(actorEspejo(esp))[g.key];
    if (!fn) return false;
    return correrEnPeana(esp, fn);
  }

  /**
   * Sostiene una pose de POSES durante `ms` (1600 por defecto) y vuelve a la
   * rutina. Es blanda: otra pose o un gesto la reemplazan (ver correrEnPeana).
   */
  function peanaPose(caja, pose, ms) {
    const esp = _peanas.get(caja);
    if (!esp || !esp.activo || (esp.gesto && !esp.blanda) || !POSES[pose]) return false;
    return correrEnPeana(esp, async () => { dibujarEn(esp, pose); await wait(ms || 1600); }, true);
  }

  /*
   * Los ojos siguen al cursor cuando esta cerca de la peana.
   *
   * "Cerca" es un margen alrededor de la caja y no la caja misma: si tuviera que
   * estar encima, con una peana de 150px el gesto casi nunca pasaria, y lo que
   * lo hace lindo es que te mire cuando pasas al lado.
   *
   * El listener es UNO para todo el documento y se registra una sola vez: uno
   * por peana significaria varios listeners de mousemove compitiendo, y eso en
   * una tabla grande se paga.
   */
  const MARGEN_MIRADA = 140;
  let _mirarListo = false;

  function seguirCursor() {
    if (_mirarListo) return;
    _mirarListo = true;
    document.addEventListener('mousemove', (e) => {
      _peanas.forEach((esp, caja) => {
        if (!esp.activo) return;

        let quiere = null;
        const r = caja.getBoundingClientRect();
        if (r.width && r.height) {
          const cerca = e.clientX > r.left - MARGEN_MIRADA && e.clientX < r.right + MARGEN_MIRADA
                     && e.clientY > r.top  - MARGEN_MIRADA && e.clientY < r.bottom + MARGEN_MIRADA;
          if (cerca) {
            // El canvas y no la caja: la caja puede ser mucho mas ancha que el
            // personaje (el panel de detalle mide 1200px) y mirando su centro
            // Boberto quedaria bizqueando hacia el medio de la pantalla.
            const rc = esp.cnv.getBoundingClientRect();
            const dx = e.clientX - (rc.left + rc.right) / 2;
            const dy = e.clientY - (rc.top + rc.bottom) / 2;
            // Arriba gana sobre los costados SOLO cuando esta claramente
            // arriba: con el cursor en diagonal, mirar de costado se lee mejor.
            if (dy < -rc.height * 0.55 && Math.abs(dx) < rc.width) quiere = 'lookU';
            else if (Math.abs(dx) < rc.width * 0.35)               quiere = 'base';
            else                                                    quiere = dx > 0 ? 'lookR' : 'lookL';
          }
        }

        // Se repinta solo cuando la mirada CAMBIA. Dos razones, y las dos
        // importan: mousemove llega decenas de veces por segundo y repintar en
        // cada una es gastar por nada; y sin comparar, al alejarse el cursor la
        // pose de mirada quedaba congelada hasta que el bucle volviera a su
        // tope, o sea hasta siete segundos mirando a un cursor que ya no esta.
        if (quiere === esp.hover) return;
        esp.hover = quiere;
        // Se anota la mirada pero NO se dibuja mientras corre un gesto: el bucle
        // la va a atender en cuanto termine. Sin esto, pasar el cursor al lado
        // de una peana a mitad de un baile le pisaba la pose con 'lookR'.
        if (esp.gesto) return;
        dibujarEn(esp, quiere || 'base');
      });
    }, { passive: true });
  }
  /**
   * Devuelve a Boberto a su casa: el slot del dock que esté vivo AHORA.
   *
   * Sólo lo llama la pila, y sólo cuando NO queda ningún reclamo: mientras haya
   * una vitrina abierta, soltar otra lo baja a esa y no a su casa.
   *
   * No vuelve "al nodo del que salió" porque ese nodo puede no existir más:
   * JiraToolkit reescribe su sidebar entera, y con el estudio abierto el slot
   * original ya se recreó. Sin dock (la landing) se lo descuelga de la escena,
   * que es lo honesto: no tiene otra casa en esa página.
   *
   * `enabled` se relee de storage: la vitrina lo fuerza en memoria para poder
   * mostrarse aunque la mascota esté apagada, y ese permiso muere con ella.
   * Quien la tenía apagada la recupera apagada.
   */
  function goHome() {
    if (!_staged) return false;
    if (_vitrinaAuto) { clearInterval(_vitrinaAuto); _vitrinaAuto = null; }
    _staged = false;
    wrap.classList.remove('plt-vitrina');
    enabled = localStorage.getItem(LS_KEY) !== 'false';
    paused = false;
    isBusy = false;
    _stageScale = SC;
    if (canvas) fitCanvas(canvas, SC);
    // Lo cuelga del slot vivo y lo reescala a su ancho.
    adoptarDock();
    if (!_dock && wrap.parentNode) wrap.parentNode.removeChild(wrap);
    estadoDock();
    wrap.style.display = enabled ? '' : 'none';
    drawF('base');
    if (canvas) {
      canvas.classList.add('plt-idle');
      canvas.style.filter = staticBodyFilter();
    }
    return true;
  }

  /** Corre un gesto del catálogo. Devuelve si lo arrancó. */
  function playGesture(id) {
    const g = GESTOS.find(x => x.id === id);
    const fn = g && coreoPropia()[g.key];
    if (!fn || !canvas || isBusy) return false;
    isBusy = true;
    paused = true;
    hideAdvice();
    canvas.classList.remove('plt-idle');
    // Sólo se anuncia en la lectura si el gesto corre EN el dock: prestado al
    // estudio, la lectura dice dónde está, no qué hace.
    if (!_staged) gestoEnDock(g.name);
    Promise.resolve(fn()).catch(() => {}).then(() => {
      gestoEnDock('');
      paused = false;
      isBusy = false;
      drawF('base');
      canvas.classList.add('plt-idle');
    });
    return true;
  }

  // ── API pública ───────────────────────────────────────────────────────
  window.BOBERTO = {
    isEnabled: () => enabled,
    setEnabled(val) {
      enabled = !!val;
      localStorage.setItem(LS_KEY, String(enabled));
      // Peanas y espejos, no sólo el dock.
      sincronizarApagado();
      // Prestado a una vitrina sigue a la vista: la vitrina lo pidió, y al
      // volver al dock goHome() relee la preferencia.
      if (wrap && !_staged) wrap.style.display = enabled ? '' : 'none';
      if (!_staged) estadoDock();
      if (enabled) { paused = false; isBusy = false; drawF('base'); startFxTicker(); }
      document.dispatchEvent(new CustomEvent('boberto-changed', { detail: { enabled } }));
    },
    isAdviceEnabled: () => adviceEnabled,
    setAdviceEnabled(val) {
      adviceEnabled = !!val;
      localStorage.setItem('smg_mascota_advice', String(adviceEnabled));
      if (!adviceEnabled) hideAdvice();
      document.dispatchEvent(new CustomEvent('boberto-advice-changed', { detail: { adviceEnabled } }));
    },
    // ── Skins ──
    listSkins: () => SKIN_PRESETS.slice(),
    getSkin: () => skin,
    getSkinColor: () => skinColor,
    setSkin(id) {
      skin = id || 'auto';
      try { localStorage.setItem(LS_SKIN, skin); } catch (e) {}
      applySkin();
      document.dispatchEvent(new CustomEvent('boberto-skin-changed', { detail: { skin, skinColor } }));
    },
    setSkinColor(hex) {
      skinColor = hex;
      skin = 'custom';
      try { localStorage.setItem(LS_SKIN_COLOR, skinColor); localStorage.setItem(LS_SKIN, skin); } catch (e) {}
      applySkin();
      document.dispatchEvent(new CustomEvent('boberto-skin-changed', { detail: { skin, skinColor } }));
    },
    // ── Ojos ──
    listEyes: () => EYE_PRESETS.slice(),
    getEyeColor: () => eyeColor,
    setEyeColor(hex) {
      eyeColor = hex || 'auto';
      try { localStorage.setItem(LS_EYE, eyeColor); } catch (e) {}
      applySkin();
      document.dispatchEvent(new CustomEvent('boberto-skin-changed', { detail: { skin, skinColor, eyeColor } }));
    },
    // ── Accesorios de cabeza ──
    listHats: () => HAT_PRESETS.slice(),
    listHatColors:  () => HAT_COLOR_PRESETS.slice(),
    listHatColors2: () => HAT_DETAIL_PRESETS.slice(),
    getHat: () => hat,
    setHat(id) {
      // Un panel viejo en caché puede pedir un personaje retirado: recibe su
      // disfraz, o el accesorio de siempre. Guardar el id dejaría uno fuera
      // de catálogo.
      if (esRetirado(id)) id = PERSONAJE_RETIRADO[id];
      hat = id || HAT_DEFAULT;
      try { localStorage.setItem(LS_HAT, hat); } catch (e) {}
      applySkin();
      document.dispatchEvent(new CustomEvent('boberto-skin-changed', { detail: { skin, skinColor, eyeColor, hat } }));
    },
    // ── Personajes (en la interfaz, "Skin") ──
    listCharacters: () => CHARACTERS.map(x => Object.assign({}, x)),
    /** '' si no hay personaje. */
    getCharacter: () => character,
    /*
     * null, '' o 'none' lo sacan y vuelve el accesorio que estaba guardado
     * debajo. Un id desconocido se ignora: no puede sacarle el personaje a
     * nadie por un typo de quien llama.
     *
     * Dispara `boberto-skin-changed` además del evento propio porque todo lo
     * que ya redibuja a Boberto (espejos, vistas previas, la vitrina) escucha
     * ese, y un personaje le cambia la piel entera.
     */
    setCharacter(id) {
      if (esRetirado(id)) {
        this.setCharacter(null);
        if (PERSONAJE_RETIRADO[id]) this.setHat(PERSONAJE_RETIRADO[id]);
        return;
      }
      const nuevo = (id == null || id === '' || id === 'none') ? '' : id;
      if (nuevo && !esCharacter(nuevo)) return;
      character = nuevo;
      try { localStorage.setItem(LS_CHARACTER, character || 'none'); } catch (e) {}
      applySkin();
      document.dispatchEvent(new CustomEvent('boberto-skin-changed', { detail: { skin, skinColor, eyeColor, hat, character } }));
      document.dispatchEvent(new CustomEvent('boberto-character-changed', { detail: { character } }));
    },
    // ── Color del accesorio ──
    // Son dos ejes: el material y el detalle. `hatColor2` en 'auto' significa
    // "el color natural de cada accesorio", que es como se veía siempre.
    getHatColor: () => hatColor,
    setHatColor(hex) {
      hatColor = hex || HAT_DARK;
      try { localStorage.setItem(LS_HAT_COLOR, hatColor); } catch (e) {}
      applySkin();
      document.dispatchEvent(new CustomEvent('boberto-skin-changed', { detail: { skin, skinColor, eyeColor, hat, hatColor } }));
    },
    getHatColor2: () => hatColor2,
    setHatColor2(hex) {
      hatColor2 = hex || 'auto';
      try { localStorage.setItem(LS_HAT_COLOR2, hatColor2); } catch (e) {}
      applySkin();
      document.dispatchEvent(new CustomEvent('boberto-skin-changed', { detail: { skin, skinColor, eyeColor, hat, hatColor, hatColor2 } }));
    },

    // ── Efectos de glow (respiración) ──
    /*
     * Los efectos que admite un target. SIN argumento devuelve todos, que es
     * lo que esperaban los llamadores viejos.
     *
     * El filtro vive ACA y no en cada interfaz a proposito: hay dos que
     * ofrecen esta lista —el panel de Ajustes y la vitrina de la landing— y
     * ya se habian separado una vez. La landing seguia mostrando los cinco
     * para el accesorio cuando el motor solo acepta 'rgb', asi que cuatro de
     * esos botones se apretaban y no hacian nada. Preguntandole al motor, el
     * dia que vuelvan alcanza con cambiar FX_ACC_PERMITIDOS.
     */
    listFxEffects: (target) => (target === 'acc'
      ? FX_EFFECTS.filter(e => FX_ACC_PERMITIDOS.includes(e.id))
      : FX_EFFECTS.slice()),
    getFx: (target) => (target === 'acc' ? fxAcc.slice() : fxBody.slice()),
    isFxOn: (target, id) => (target === 'acc' ? fxAcc : fxBody).includes(id),
    setFx(target, ids) {
      let list = (Array.isArray(ids) ? ids : []).filter(id => _fxIds.includes(id));
      // El accesorio solo admite 'rgb'. Se filtra ACA y no solo en el panel: la
      // API es publica, y una lista que el motor acepta pero no puede mostrar es
      // un estado que nadie puede volver a apagar.
      if (target === 'acc') list = list.filter(id => FX_ACC_PERMITIDOS.includes(id));
      if (target === 'acc') { fxAcc = list; try { localStorage.setItem(LS_FX_ACC, JSON.stringify(fxAcc)); } catch (e) {} }
      else { fxBody = list; try { localStorage.setItem(LS_FX_BODY, JSON.stringify(fxBody)); } catch (e) {} }
      if (fxAnyActive()) startFxTicker();
      document.dispatchEvent(new CustomEvent('boberto-fx-changed', { detail: { target, fxBody, fxAcc } }));
    },
    toggleFx(target, id) {
      const cur = (target === 'acc' ? fxAcc : fxBody).slice();
      const i = cur.indexOf(id);
      if (i >= 0) cur.splice(i, 1); else cur.push(id);
      this.setFx(target, cur);
    },
    // ── Color del neón (glow) por target ──
    getGlow: (target) => (target === 'acc' ? glowAcc : glowBody),
    setGlow(target, hex) {
      const v = hex || 'auto';
      // El accesorio no tiene color de brillo elegible: su glow se deriva del
      // color del accesorio. Se ignora en silencio en vez de guardarlo, para que
      // no quede un valor aplicandose sin control que lo cambie.
      if (target === 'acc') { glowAcc = 'auto'; try { localStorage.removeItem(LS_GLOW_ACC); } catch (e) {} }
      else { glowBody = v; try { localStorage.setItem(LS_GLOW_BODY, glowBody); } catch (e) {} }
      refreshStaticGlow();
      document.dispatchEvent(new CustomEvent('boberto-fx-changed', { detail: { target, glowBody, glowAcc } }));
    },
    listGlowColors: () => GLOW_PRESETS.slice(),
    // ── Fondo del dock (el paisaje detrás de él en la sidebar) ──
    listFondos: () => FONDOS.map(x => Object.assign({}, x)),
    getFondo: () => fondo,
    /* Un id fuera de catálogo se ignora, como en setCharacter. "Sin fondo"
     * BORRA la clave en vez de guardarla: es el default, y así un fondo
     * que se agregue como default algún día le llega a quien nunca eligió. */
    setFondo(id) {
      const nuevo = id || FONDO_NINGUNO;
      if (!esFondo(nuevo)) return;
      fondo = nuevo;
      try {
        if (fondo === FONDO_NINGUNO) localStorage.removeItem(LS_FONDO);
        else localStorage.setItem(LS_FONDO, fondo);
      } catch (e) {}
      aplicarFondo();
      document.dispatchEvent(new CustomEvent('boberto-fondo-changed', { detail: { fondo, opacidad: opDe(fondo) } }));
    },
    /* Opacidad (0-100) del fondo `id`, o del puesto si no se pasa. Sin valor
     * elegido devuelve el default del tema (ver opDefault), que es lo que se
     * está viendo; "Sin fondo" no tiene opacidad y devuelve 0. */
    getFondoOpacidad(id) {
      const k = id || fondo;
      if (k === FONDO_NINGUNO || !esFondo(k)) return 0;
      return opDe(k);
    },
    /* Guarda la opacidad de UN fondo (no hace falta que sea el puesto: el
     * estudio podría ajustar uno que se está previsualizando). Se acota a
     * 0-100 y se redondea. Emite el mismo `boberto-fondo-changed` que
     * setFondo, con { fondo, opacidad, id }: `fondo` es el puesto y
     * `opacidad` la del puesto; `id` dice cuál se tocó. Un evento y no dos
     * porque quien escucha (el estudio) repinta lo mismo en los dos casos. */
    setFondoOpacidad(id, pct) {
      if (id === FONDO_NINGUNO || !esFondo(id) || !Number.isFinite(Number(pct))) return;
      fondoOps[id] = pctFondo(pct);
      guardarOps();
      aplicarFondo();
      document.dispatchEvent(new CustomEvent('boberto-fondo-changed', { detail: { fondo, opacidad: opDe(fondo), id } }));
    },
    /* El mapa entero, para que el estudio pueda tomar una foto al abrir y
     * reponerla con "Restablecer". setFondoOpacidades(null) lo vacía (todos
     * vuelven al default del tema): es lo que hace resetLook. */
    getFondoOpacidades: () => Object.assign({}, fondoOps),
    setFondoOpacidades(mapa) {
      fondoOps = limpiarOps(mapa);
      guardarOps();
      aplicarFondo();
      document.dispatchEvent(new CustomEvent('boberto-fondo-changed', { detail: { fondo, opacidad: opDe(fondo) } }));
    },
    // El dibujo de un fondo, para las miniaturas del estudio: el mismo que
    // pinta el dock, no una copia.
    fondoSVG,
    /* Valores de fábrica de todo lo que se elige en el estudio. Vivía en el
     * "Restablecer todo" de Ajustes; se mudó con el resto de su look, y los
     * defaults son los del motor (HAT_DEFAULT, HAT_DARK), no una copia. */
    resetLook() {
      this.setCharacter(null);
      this.setSkin('auto');
      this.setEyeColor('auto');
      this.setHat(HAT_DEFAULT);
      this.setHatColor(HAT_DARK);
      this.setHatColor2('auto');
      this.setFx('body', []);
      this.setFx('acc', []);
      this.setGlow('body', 'auto');
      // Las opacidades también: "de fábrica" es sin fondo Y sin ajustes
      // guardados, así el próximo fondo que se elija arranca en el default.
      this.setFondoOpacidades(null);
      this.setFondo(FONDO_NINGUNO);
      this.setBorde(BORDE_NINGUNO);
    },
    listBordes: () => BORDES.map(x => Object.assign({}, x)),
    getBorde: () => borde,
    setBorde(id) {
      const nuevo = id || BORDE_NINGUNO;
      if (!esBorde(nuevo)) return;
      borde = nuevo;
      try {
        if (borde === BORDE_NINGUNO) localStorage.removeItem(LS_BORDE);
        else localStorage.setItem(LS_BORDE, borde);
      } catch (e) {}
      aplicarBorde();
      document.dispatchEvent(new CustomEvent('boberto-borde-changed', { detail: { borde } }));
    },
    // ── Vitrina (mostrarlo fuera del dock y disparar sus gestos) ──
    mountStage,
    claimStage,
    releaseStage,
    // Alias histórico: soltar TODOS los reclamos y volver al dock.
    unmountStage: () => releaseStage(),
    // ── Dock (el slot [data-boberto-dock] de la sidebar) ──
    // Abre el estudio como si lo hubieran tocado; el foco vuelve a `origen`
    // (por defecto, el dock). Devuelve si lo abrió.
    abrirEstudio: (origen) => abrirEstudio(origen),
    // El slot donde vive ahora, o null si la página no declaró ninguno.
    dock: () => (_dock && _dock.isConnected ? _dock : null),
    isStaged: () => _staged,
    stageDepth: () => _stage.length,
    // ── Peana (el mueble listo para usar) ──
    peana,
    soltarPeana,
    peanaGesto,
    peanaPose,
    listPoses: () => Object.keys(POSES),
    listGestures: () => GESTOS.map(g => ({ id: g.id, name: g.name, icon: g.icon })),
    playGesture,
    isBusy: () => isBusy,
    /* La medida del lienzo, para quien tenga que muestrearlo.
     * La usa el intro de la landing, que rasteriza al personaje y lo parte en
     * celdas: con el 16x18 escrito a mano allá, agrandar la caja acá le habría
     * deformado la silueta en silencio. Se pregunta, no se copia. */
    box: () => ({ W: BOX.W, H: BOX.H }),
    /* El jarro del café, para que otro lienzo lo sostenga fuera de las poses
     * (el corto de la landing lo tiene en la mesa y se lo pasa a la mano).
     * tazaEn: dónde queda en una pose de café (ver tazaDePose); `opciones`
     * como en renderPose. pintarTaza: lo pinta en sus coordenadas propias,
     * origen en el centro de la boca; quien llama traslada, gira y escala.
     * Se pinta con el mismo código que el de las poses: si no, al pasar de
     * la mesa a la mano cambiaría de jarro. */
    tazaEn: (poseName, opciones) => conVestuario(vestuarioDe(opciones), () => tazaDePose(poseName)),
    pintarTaza(c, giro) {
      // El detalle de la marca sale de la escala real de quien llama (su
      // transformación ya trae el traslado, el giro y la escala del jarro):
      // así decide igual que en la pose a igual tamaño en pantalla.
      const m = typeof c.getTransform === 'function' ? c.getTransform() : null;
      const pxU = m && isFinite(m.a) ? Math.hypot(m.a, m.b) : Infinity;
      c.save();
      pintarJarro(c, giro || 0, pxU);
      c.restore();
    },
    // ── Preview ──
    renderPreview,
    stopPreview,
    /* opciones.hat / opciones.character: vestuario fijo para este cuadro,
     * sin cambiar el elegido (ver vestuarioDe). Lo usan las miniaturas de la
     * sección Skins de Ajustes. */
    renderPose(cnv, poseName, scale, opciones) {
      if (!cnv) return;
      const s = scale || SC;
      fitCanvas(cnv, s);
      pal = getPal(); resolveHatColors();
      conVestuario(vestuarioDe(opciones), () => paintFrame(cnv.getContext('2d'), poseName || 'base', s, buildFxStyle()));
    },
    // ── Animación en Chatbot ──
    startChatAnimation,
    stopChatAnimation,
    setChatHoverOverride,
  };

  // ── DOM listener de skin ──
  // Los espejos (peanas y avatar del chat) se redibujan con la piel y con los
  // efectos: cada uno tiene su propio canvas y su propio bucle, asi que nadie
  // mas los va a repintar.
  document.addEventListener('boberto-skin-changed', refrescarEspejos);
  // La lectura del dock nombra lo que lleva puesto: personaje y accesorio
  // viajan en este mismo evento (setCharacter también lo dispara).
  // Y un personaje nuevo puede pararse distinto: se vuelve a medir el piso.
  document.addEventListener('boberto-skin-changed', () => {
    lecturaDock();
    if (_dock && !_staged && wrap.parentNode === _dock) ajustarDock();
  });
  document.addEventListener('boberto-fx-changed', refrescarEspejos);

})();
