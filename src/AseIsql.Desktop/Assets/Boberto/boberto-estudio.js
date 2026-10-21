/* ── Estudio de Boberto — capa compartida de SMG_APPS ───────────────────────
 *
 * El menú para personalizar a Boberto y probar sus gestos, en cuatro
 * apartados: Gestos; Apariencia (skins, cuerpo, ojos, efectos); Accesorio
 * (modelo, color, detalle) y Fondo.
 *
 * Es el ÚNICO lugar donde se lo configura. El panel de Ajustes tenía su
 * propia sección (vista previa, colores, brillo…) y quedó sólo con el
 * interruptor de encendido: dos menús para lo mismo se separan solos, y acá
 * se lo ve probándose cada cosa. Lo que Ajustes tenía y esto no —el "Auto"
 * del cuerpo y de los ojos, los colores a elección, el color del brillo y
 * los valores de fábrica— se mudó acá antes de borrarlo.
 *
 * Nació en la landing (vitrinaBoberto, en assets/landing.js) y se mudó acá
 * cuando las apps también lo necesitaron: al tocar a Boberto en la sidebar se
 * abre este mismo menú en un modal. Dos copias del motor de pestañas se
 * separan solas —ya pasó con la lista de efectos del accesorio—, así que hay
 * UNA y la consumen las dos.
 *
 * Dos piezas:
 *
 *   BOBERTO_ESTUDIO.montar(op)  — arma pestañas y paneles dentro de
 *     contenedores que pone quien llama, y presta al Boberto real a su escena
 *     (BOBERTO.claimStage). La usa la landing con su propio layout.
 *
 *   BOBERTO_ESTUDIO.abrir(op)   — el modal "estudio": arma su propio cuadro
 *     (escena grande + panel) y llama a montar() adentro. Lo abre el dock de
 *     la sidebar (shared/js/boberto.js).
 *
 * Lo que diferencia a una de otra es `persistir`. Los setters del motor ya
 * guardan en localStorage (smg_boberto_*). En la landing eso es un problema:
 * mirar una página no puede cambiarte el Boberto de las tres apps, así que se
 * anota el estado de entrada y se repone al salir. En el estudio de una app
 * es exactamente lo pedido: lo que elegís queda, y "Restablecer" vuelve a lo
 * que había al abrir.
 *
 * El menú se arma desde lo que publica el motor —gestos, accesorios,
 * paletas—: escrito a mano, cada accesorio nuevo obligaría a acordarse de
 * este archivo.
 *
 * Los estilos viven en shared/css/boberto-estudio.css, con los tokens
 * semánticos de la suite: se repinta con el tema como todo lo demás.
 * ------------------------------------------------------------------------- */
(function () {
  'use strict';

  // Miembro por miembro: un boberto.js viejo en caché existe pero no tiene
  // ni claimStage ni las paletas.
  const NECESARIOS = ['mountStage', 'releaseStage', 'playGesture', 'listGestures', 'listHats',
                      'listSkins', 'listEyes', 'listHatColors', 'listHatColors2',
                      'listFxEffects', 'toggleFx', 'isFxOn', 'getFx', 'setFx'];

  function motor() {
    const B = window.BOBERTO;
    if (!B || NECESARIOS.some((m) => typeof B[m] !== 'function')) return null;
    return B;
  }

  // Escala por defecto de la escena: la de la landing, medida por ANCHO.
  // window.innerWidth puede valer 0 (pasa en un panel todavía sin tamaño) y
  // ahí Boberto quedaba diminuto para siempre; el contenedor es, además, lo
  // que de verdad tiene que albergarlo.
  /* Medidas en px de CSS del propio elemento (clientWidth), no en px de
   * pantalla (getBoundingClientRect). La suite escala la interfaz con `zoom`
   * en el body, y con el zoom estandarizado de Chromium el rectángulo viene ya
   * multiplicado: al 150 % la escena "medía" 420 px cuando su caja era de
   * 280, y el lienzo elegido para 420 se aplastaba contra el max-width. */
  function cajaDe(el) {
    const r = el.getBoundingClientRect();
    return { width: el.clientWidth || r.width, height: el.clientHeight || r.height };
  }

  function escalaPorAncho(escena) {
    const w = cajaDe(escena).width;
    if (!w) return 20;                    // sin medida útil: la grande, y los reintentos corrigen
    return w < 380 ? 11 : w < 620 ? 15 : 20;
  }

  /**
   * Arma el estudio dentro de los contenedores de `op` y devuelve un manejador,
   * o null si el motor no cargó (quien llama decide qué mostrar sin él).
   *
   * op:
   *   escena     — dónde se presta el Boberto real (vacío; lo llena el motor)
   *   tabs       — contenedor de las pestañas (role=tablist lo pone el módulo)
   *   paneles    — contenedor de los paneles
   *   volver     — botón opcional que repone el estado de entrada
   *   persistir  — false (default): repone el estado de entrada en pagehide.
   *                true: lo elegido queda guardado.
   *   escala     — fn(escena) → px por unidad. Default: la de la landing.
   *   excluir    — ids de grupos que no van (la landing saca 'fondo': no
   *                tiene sidebar, así que no hay dónde verlo).
   *   fabrica    — botón opcional que lo deja con los valores de fábrica.
   *   rotulo     — elemento opcional donde se escribe lo que tiene puesto.
   *   fondo      — capa opcional DETRÁS de la escena donde se pinta el fondo
   *                puesto (o el que se está probando) con su opacidad.
   *   igualarAlto — false si el contenedor de paneles tiene alto propio y
   *                scrollea por dentro (el modal). Default: se iguala al más alto.
   *
   * Manejador: { desmontar(), restablecer(), mostrar(id), tocado() }
   * mostrar() acepta el id de un eje ('ojos') o de un apartado ('apariencia').
   */
  function montar(op) {
    op = op || {};
    const B = motor();
    const escena = op.escena, tabs = op.tabs, paneles = op.paneles;
    if (!B || !escena || !tabs || !paneles) return null;

    const persistir = op.persistir === true;
    const escalaDe = typeof op.escala === 'function' ? op.escala : escalaPorAncho;
    // Lo que hay que desarmar al desmontar: listeners, observadores y timers.
    const limpiar = [];
    const escuchar = (blanco, ev, fn, opciones) => {
      blanco.addEventListener(ev, fn, opciones);
      limpiar.push(() => blanco.removeEventListener(ev, fn, opciones));
    };
    const observar = (fn, el) => {
      if (typeof ResizeObserver !== 'function') return;
      const ro = new ResizeObserver(fn);
      ro.observe(el);
      limpiar.push(() => ro.disconnect());
    };
    let vivo = true;

    // ── Estado de entrada, para poder devolverlo tal cual ──
    const inicial = {
      hat: B.getHat(), skin: B.getSkin(), skinColor: B.getSkinColor(),
      eye: B.getEyeColor(), hatColor: B.getHatColor(),
      hatColor2: typeof B.getHatColor2 === 'function' ? B.getHatColor2() : null,
      fxBody: B.getFx('body'), fxAcc: B.getFx('acc'),
      glowBody: typeof B.getGlow === 'function' ? B.getGlow('body') : null,
      fondo: typeof B.getFondo === 'function' ? B.getFondo() : null,
      borde: typeof B.getBorde === 'function' ? B.getBorde() : null,
      // El mapa de opacidades entero, no sólo la del fondo puesto: en el
      // estudio se puede ajustar uno, cambiar a otro y ajustar ése también.
      fondoOps: typeof B.getFondoOpacidades === 'function' ? B.getFondoOpacidades() : null,
      // null = motor sin personajes (un boberto.js viejo en caché): no se repone.
      character: typeof B.getCharacter === 'function' ? B.getCharacter() : null,
    };
    let tocado = false;

    // ── Escena ──
    let escalaPuesta = 0;
    function ajustarEscala() {
      if (!vivo) return;
      const e = escalaDe(escena);
      if (e === escalaPuesta) return;
      // La marca se pone DESPUÉS y sólo si funcionó. Estaba antes de la llamada,
      // y el día que mountStage() empezó a poder fallar —cuando se le agregó la
      // guarda de "el contenedor tiene que tener caja"— eso dejó la vitrina
      // vacía para siempre: el primer intento caía antes de que la escena
      // midiera, la marca quedaba puesta igual, y el `if` de arriba bloqueaba
      // todos los reintentos.
      //
      // Sin gestos sueltos (`autoplay: false`): esta vitrina es para probarlo, y
      // un gesto que arranca solo mientras se elige otro se confunde con la
      // respuesta al click. Acá sólo hace lo que se le pide; parpadear y
      // respirar siguen, porque eso es estar vivo, no un gesto.
      if (B.mountStage(escena, e, { autoplay: false })) {
        escalaPuesta = e;
        // La unidad del dibujo, publicada para el CSS de la escena: el piso y
        // la sombra se ubican respecto de los pies, y los pies están a una
        // unidad del borde del lienzo (el aire de abajo de BOX).
        escena.style.setProperty('--bes-u', e + 'px');
      }
    }
    ajustarEscala();
    // El ResizeObserver entrega al final de un frame, así que en una pestaña de
    // fondo no llega nunca; y si la primera medición cayó antes de que el
    // layout resolviera, la escena se quedaba con la escala de reserva. Los
    // otros tres no dependen de que se compongan cuadros.
    observar(ajustarEscala, escena);
    escuchar(window, 'resize', ajustarEscala, { passive: true });
    escuchar(window, 'load', ajustarEscala);
    const t300 = setTimeout(ajustarEscala, 300);
    limpiar.push(() => clearTimeout(t300));

    /* ── Menú ──
     * Cada grupo declara de dónde saca sus opciones, cómo se ve cada una, qué
     * hace al elegirla y cuál está puesta. Con eso, agregar un eje nuevo es una
     * entrada más y no otra función igual a las anteriores.
     *
     * `apartado` dice bajo qué botón de la navegación vive (ver APARTADOS) y
     * `corto` es su rótulo DENTRO de ese apartado: "Color del accesorio" se lee
     * entero en una pestaña suelta, pero bajo "Accesorio" alcanza con "Color".
     * `forma` elige la grilla: 'icono' (emoji arriba del nombre), 'muestra'
     * (un círculo de color), 'interruptor' (se prende y apaga, no se elige) o
     * 'tarjeta' (miniatura del fondo). Es la forma de la OPCIÓN, no del eje:
     * un color se elige mirando el color, un gesto leyendo qué hace. */
    const sw = (hex) => {
      const i = document.createElement('i');
      i.className = 'bes-tono';
      i.setAttribute('aria-hidden', 'true');
      i.style.setProperty('--tono', hex);
      return i;
    };
    const ico = (txt) => {
      const i = document.createElement('i');
      i.className = 'bes-ico';
      i.setAttribute('aria-hidden', 'true');
      i.textContent = txt;
      return i;
    };

    // Un color a elección: <input type=color> encima del chip, invisible. El
    // valor tiene que ser #rrggbb, y lo guardado puede ser 'auto' o 'custom'.
    const hex6 = (v) => (/^#[0-9a-f]{6}$/i.test(String(v || '')) ? String(v).toLowerCase() : '#888888');
    const enLista = (lista, v) => lista.some((x) => String(x.id).toLowerCase() === String(v || '').toLowerCase());
    /* Miniatura de un fondo: el MISMO SVG que pinta el dock (BOBERTO.fondoSVG)
     * sobre una franja con su línea de piso. Una ilustración aparte para el
     * menú se separaría del dibujo real en el primer retoque. */
    const miniatura = (id) => {
      const m = document.createElement('span');
      m.className = 'bes-fondo-min';
      m.setAttribute('aria-hidden', 'true');
      const capa = document.createElement('span');
      capa.className = 'bes-fondo-capa';
      capa.innerHTML = B.fondoSVG(id);   // markup del motor, sin datos de afuera
      m.appendChild(capa);
      const piso = document.createElement('i');
      piso.className = 'bes-fondo-piso';
      m.appendChild(piso);
      return m;
    };

    /* ── El fondo en el escenario ──
     * op.fondo es la capa del escenario que queda DETRÁS de Boberto (la pone
     * quien arma la escena: el modal sí, la landing no tiene). Muestra el
     * fondo puesto, con su opacidad, y mientras el puntero o el foco están
     * sobre una tarjeta, ESA tarjeta: así se prueba un fondo sin elegirlo.
     * La prueba no toca nada guardado; al salir vuelve al puesto.
     * El puntero y el foco se llevan aparte: se puede estar con el foco en
     * una tarjeta (la que se acaba de elegir) y pasar el mouse por otra, y al
     * irse el mouse tiene que volver la del foco, no la guardada. */
    const capaEscena = op.fondo || null;
    const FONDO_SIN = 'ninguno';   // el id de "Sin fondo" del catálogo del motor
    const opacidadDe = (id) => (typeof B.getFondoOpacidad === 'function' ? B.getFondoOpacidad(id) : 85);
    let previaPuntero = null, previaFoco = null, pintado = null;
    function pintarEscena() {
      if (!capaEscena || !vivo || typeof B.getFondo !== 'function') return;
      // Quien excluyó el eje no pidió fondo: la capa queda vacía.
      if (!GRUPOS.some((g) => g.id === 'fondo')) return;
      const puesto = B.getFondo();
      const id = previaPuntero || previaFoco || puesto;
      // El SVG se rearma sólo si cambió el fondo: el slider sólo cambia la
      // opacidad, y rearmarlo en cada paso del arrastre reiniciaría las
      // animaciones de los ilustrados.
      if (pintado !== id) {
        capaEscena.innerHTML = B.fondoSVG(id);   // markup del motor, sin datos de afuera
        capaEscena.setAttribute('data-fondo', id);
        pintado = id;
      }
      capaEscena.style.setProperty('--bes-fondo-op', String(opacidadDe(id) / 100));
      capaEscena.toggleAttribute('data-previa', id !== puesto);
    }
    const previa = (tipo, id) => {
      if (tipo === 'puntero') previaPuntero = id; else previaFoco = id;
      pintarEscena();
    };

    /* El marco del escenario. La prueba (puntero o foco) no se guarda; al
     * salir vuelve el puesto. Sin elemento (la landing no lo pasa) no hay
     * nada que pintar: el grupo ni siquiera se monta ahí. */
    const marcoEscena = op.marco || null;
    let bordePuntero = null, bordeFoco = null;
    function pintarMarco() {
      if (!marcoEscena || !vivo || typeof B.getBorde !== 'function') return;
      const id = bordePuntero || bordeFoco || B.getBorde();
      if (!id || id === 'ninguno') marcoEscena.removeAttribute('data-modelo');
      else marcoEscena.setAttribute('data-modelo', id);
    }
    const previaBorde = (tipo, id) => {
      if (tipo === 'puntero') bordePuntero = id; else bordeFoco = id;
      pintarMarco();
    };
    const muestraBorde = (id) => {
      const m = document.createElement('span');
      m.className = 'bes-borde-min';
      m.setAttribute('aria-hidden', 'true');
      if (id && id !== 'ninguno') {
        const marco = document.createElement('i');
        marco.className = 'plt-borde-marco';
        marco.setAttribute('data-modelo', id);
        m.appendChild(marco);
      }
      return m;
    };

    /* ── La opacidad del fondo ──
     * Una barra para el fondo PUESTO, arriba de las tarjetas: así queda a la
     * vista al lado del escenario, que es donde se ve el efecto. Un <input
     * type=range> nativo: flechas, Re Pág/Av Pág, Inicio y Fin ya funcionan
     * con el teclado, y el lector de pantalla lo anuncia como control
     * deslizante sin ARIA a mano. Con "Sin fondo" se deshabilita (no hay
     * nada que atenuar) en vez de esconderse: si desapareciera, la grilla de
     * abajo saltaría al elegir. */
    const opa = {};
    function cabezaOpacidad() {
      const fila = crear('div', 'bes-opacidad');
      const idInp = pref + 'opacidad';
      const lab = crear('label', 'bes-opacidad-rotulo');
      lab.htmlFor = idInp;
      lab.append(crear('span', 'bes-opacidad-k', 'Opacidad'), crear('span', 'bes-opacidad-de'));
      const inp = document.createElement('input');
      inp.type = 'range';
      inp.id = idInp;
      inp.className = 'bes-opacidad-input';
      inp.min = '0'; inp.max = '100'; inp.step = '1';
      const val = document.createElement('output');
      val.className = 'bes-opacidad-valor';
      val.htmlFor = idInp;
      val.setAttribute('aria-hidden', 'true');   // el valor ya lo anuncia el slider (aria-valuetext)
      inp.addEventListener('input', () => {
        const f = B.getFondo();
        if (f === FONDO_SIN) return;
        // Se pinta acá y no se espera al evento del motor: el número y la
        // barra tienen que seguir al dedo en el mismo cuadro.
        mostrarValor(Number(inp.value));
        B.setFondoOpacidad(f, Number(inp.value));
        tocado = true;
      });
      fila.append(lab, inp, val);
      Object.assign(opa, { fila: fila, inp: inp, val: val, de: lab.lastChild });
      return fila;
    }
    function mostrarValor(v) {
      opa.val.textContent = v + ' %';
      opa.inp.setAttribute('aria-valuetext', v + ' %');
      opa.inp.style.setProperty('--p', v + '%');
    }
    function sincronizarOpacidad() {
      if (!opa.inp) return;
      const f = B.getFondo();
      const sin = f === FONDO_SIN;
      opa.inp.disabled = sin;
      opa.fila.dataset.off = sin ? '1' : '0';
      opa.de.textContent = sin ? 'Sin fondo' : (nombreEn(B.listFondos(), f) || f);
      if (sin) {
        opa.val.textContent = '—';
        opa.inp.removeAttribute('aria-valuetext');
        opa.inp.style.setProperty('--p', '0%');
        return;
      }
      const v = opacidadDe(f);
      // No se le pisa el valor a quien lo está arrastrando.
      if (document.activeElement !== opa.inp) opa.inp.value = String(v);
      mostrarValor(v);
    }

    /* El catálogo trae `grupo`. Sin fondo no tiene: es la salida, no un estilo,
     * y va solo arriba para que no quede mezclado con una familia. */
    const seccionFondo = (titulo, pred) => ({
      titulo: titulo,
      forma: 'tarjeta',
      items: () => B.listFondos().filter(pred),
      adorno: (it) => miniatura(it.id),
      hacer: (it) => B.setFondo(it.id),
      activo: (it) => String(B.getFondo() || '').toLowerCase() === String(it.id).toLowerCase(),
      previa: previa,
    });

    // Los de serie Materiales no son otro personaje: el estudio los saca de
    // Skins y el motor les deja el accesorio. La serie la declara el catálogo.
    const esMateria = (id) => {
      if (!id || typeof B.listCharacters !== 'function') return false;
      const x = B.listCharacters().find((c) => c.id === id);
      return !!(x && x.serie === 'Materiales');
    };

    const GRUPOS = [
      {
        id: 'gestos', tab: 'Gestos', apartado: 'gestos', forma: 'icono',
        // Los gestos no tienen "elegido": son acciones. Por eso no marcan.
        accion: true,
        items: () => B.listGestures(),
        adorno: (it) => ico(it.icon),
        hacer: (it) => B.playGesture(it.id),
      },
      {
        /* Los personajes de franquicia se retiraron (ver PERSONAJE_RETIRADO en
         * el motor): lo que queda en el registro de personajes son los
         * materiales. Un material es la misma silueta, así que el accesorio se
         * queda; `vestir: true` (el cuerpo) sí se apaga con uno puesto (ver
         * sincronizar). Los disfraces viven en Accesorio. */
        id: 'material', tab: 'Material', apartado: 'apariencia', forma: 'icono',
        requiere: 'listCharacters',
        items: () => [{ id: '', name: 'Sin material', icon: '∅' }].concat(
          B.listCharacters().filter((c) => c.serie === 'Materiales')),
        adorno: (it) => ico(it.icon),
        hacer: (it) => {
          if (!it.id) {
            if (esMateria(B.getCharacter())) B.setCharacter(null);
            return;
          }
          B.setCharacter(it.id);
        },
        // Sin material puesto queda marcado «Sin material».
        puesto: () => (esMateria(B.getCharacter()) ? B.getCharacter() : ''),
      },
      {
        id: 'accesorio', tab: 'Accesorio', corto: 'Modelo', apartado: 'accesorio', forma: 'icono', vestir: 'accesorio',
        items: () => B.listHats(),
        adorno: (it) => ico(it.icon),
        hacer: (it) => B.setHat(it.id),
        puesto: () => B.getHat(),
      },
      {
        id: 'cuerpo', tab: 'Cuerpo', apartado: 'apariencia', forma: 'muestra', vestir: true,
        // 'auto' (seguir el tema) no es un skin del catálogo: se agrega acá
        // porque es el default, y sin él no había cómo volver a él.
        items: () => [{ id: 'auto', name: 'Auto', desc: 'Sigue el color del tema' }].concat(B.listSkins()),
        adorno: (it) => (it.id === 'auto' ? ico('A') : sw(it.color)),
        hacer: (it) => B.setSkin(it.id),
        puesto: () => B.getSkin(),
        // setSkinColor deja el skin en 'custom': ése es el "puesto" del libre.
        libre: {
          etiqueta: 'Color del cuerpo a elección',
          valor: () => B.getSkinColor(),
          puesto: () => B.getSkin() === 'custom',
          poner: (hex) => B.setSkinColor(hex),
        },
      },
      {
        id: 'ojos', tab: 'Ojos', apartado: 'apariencia', forma: 'muestra', vestir: true,
        items: () => [{ id: 'auto', name: 'Auto', desc: 'Según el color del cuerpo' }].concat(B.listEyes()),
        adorno: (it) => (it.id === 'auto' ? ico('A') : sw(it.id)),
        hacer: (it) => B.setEyeColor(it.id),
        puesto: () => B.getEyeColor(),
        libre: {
          etiqueta: 'Color de ojos a elección',
          valor: () => B.getEyeColor(),
          puesto: () => B.getEyeColor() !== 'auto' && !enLista(B.listEyes(), B.getEyeColor()),
          poner: (hex) => B.setEyeColor(hex),
        },
      },
      {
        id: 'color', tab: 'Color del accesorio', corto: 'Color', apartado: 'accesorio', forma: 'muestra', vestir: 'accesorio',
        items: () => B.listHatColors(),
        // "Natural" muestra el color de fábrica del accesorio puesto, no un
        // gris fijo; sincronizar() lo repinta cuando cambia el accesorio.
        adorno: (it) => {
          const t = sw(it.muestra || it.id);
          if (it.muestra) t.dataset.natural = '1';
          return t;
        },
        hacer: (it) => B.setHatColor(it.id),
        puesto: () => B.getHatColor(),
        libre: {
          etiqueta: 'Color del accesorio a elección',
          valor: () => B.getHatColor(),
          puesto: () => !enLista(B.listHatColors(), B.getHatColor()),
          poner: (hex) => B.setHatColor(hex),
        },
      },
      {
        id: 'detalle', tab: 'Detalle del accesorio', corto: 'Detalle', apartado: 'accesorio', forma: 'muestra', vestir: 'accesorio',
        items: () => B.listHatColors2(),
        adorno: (it) => (it.id === 'auto' ? ico('A') : sw(it.id)),
        hacer: (it) => B.setHatColor2(it.id),
        puesto: () => (typeof B.getHatColor2 === 'function' ? B.getHatColor2() : 'auto'),
        libre: typeof B.setHatColor2 === 'function' ? {
          etiqueta: 'Color del detalle a elección',
          valor: () => B.getHatColor2(),
          puesto: () => !enLista(B.listHatColors2(), B.getHatColor2()),
          poner: (hex) => B.setHatColor2(hex),
        } : null,
      },
      {
        id: 'efectos', tab: 'Efectos', apartado: 'apariencia',
        nota: 'El glow del cuerpo lo trae el skin o el material. Con un material, el del accesorio sigue.',
        bloqueoNota: 'piel',
        /* Dos filas independientes en una sola pestaña: el cuerpo y el
         * accesorio tienen sus propios efectos y hay que poder verlos juntos.
         * Son INTERRUPTORES, no una elección excluyente — se combinan, y por
         * eso cada uno lleva su propio estado en vez de marcarse uno solo.
         * El del cuerpo se apaga con skin y con material; el del accesorio,
         * sólo con un skin. */
        secciones: [
          { titulo: 'Glow del cuerpo',    target: 'body', bloqueo: 'piel' },
          { titulo: 'Glow del accesorio', target: 'acc',  bloqueo: 'accesorio' },
        ].map((sec) => ({
          bloqueo: sec.bloqueo,
          titulo: sec.titulo,
          forma: 'interruptor',
          // Por TARGET: el accesorio sólo admite RGB, y sin el argumento la
          // vitrina ofrecía los cinco. Los otros cuatro se apretaban y no
          // pasaba nada, porque el motor los filtra igual.
          items: () => B.listFxEffects(sec.target),
          adorno: (it) => ico(it.id === 'rgb' ? '🌈' : '◍'),
          hacer: (it) => B.toggleFx(sec.target, it.id),
          activo: (it) => B.isFxOn(sec.target, it.id),
        })).concat(typeof B.listGlowColors === 'function' && typeof B.getGlow === 'function' ? [{
          /* El color del brillo del cuerpo. Éste SÍ es una elección (uno a
           * la vez), no un interruptor. El accesorio no tiene: su brillo sale
           * del color del accesorio (ver setGlow en el motor). */
          titulo: 'Color del glow del cuerpo',
          bloqueo: 'piel',
          forma: 'muestra',
          items: () => B.listGlowColors(),
          adorno: (it) => (it.id === 'auto' ? ico('A') : sw(it.id)),
          hacer: (it) => B.setGlow('body', it.id),
          activo: (it) => String(B.getGlow('body') || 'auto').toLowerCase() === String(it.id).toLowerCase(),
          libre: {
            etiqueta: 'Color del glow a elección',
            valor: () => B.getGlow('body'),
            puesto: () => !enLista(B.listGlowColors(), B.getGlow('body')),
            poner: (hex) => B.setGlow('body', hex),
          },
        }] : []),
      },
      {
        /* El paisaje detrás de él en la sidebar. No es de `vestir`: con un
         * skin puesto el fondo se sigue viendo, es del lugar y no de él.
         * Tarjetas y no chips: un fondo se elige por cómo se ve, y en un
         * círculo de 17 px no se ve nada. */
        id: 'fondo', tab: 'Fondo', apartado: 'fondo',
        requiere: 'listFondos',
        cabeza: typeof B.setFondoOpacidad === 'function' ? cabezaOpacidad : null,
        secciones: [
          seccionFondo('', (it) => !it.grupo),
          seccionFondo('Cyberpunk', (it) => it.grupo === 'cyberpunk'),
          seccionFondo('Arcade', (it) => it.grupo === 'arcade'),
          seccionFondo('Neo Mirai', (it) => it.grupo === 'neomirai'),
          seccionFondo('Japonés', (it) => it.grupo === 'japones'),
          seccionFondo('Anime', (it) => it.grupo === 'shonen'),
          seccionFondo('Paisaje', (it) => it.grupo === 'paisaje'),
          seccionFondo('Nocturno', (it) => it.grupo === 'nocturno'),
          seccionFondo('Abstracto', (it) => it.grupo === 'abstracto'),
          seccionFondo('Interior', (it) => it.grupo === 'interior'),
          seccionFondo('Celestial', (it) => it.grupo === 'celestial'),
        ],
      },
      {
        /* El marco del lugar, no del personaje: con un skin puesto se sigue
         * viendo. Tarjetas, porque un borde se elige mirándolo. */
        id: 'borde', tab: 'Bordes', apartado: 'borde', forma: 'tarjeta',
        requiere: 'listBordes',
        items: () => B.listBordes(),
        adorno: (it) => muestraBorde(it.id),
        hacer: (it) => B.setBorde(it.id),
        puesto: () => B.getBorde(),
        previa: previaBorde,
      },
    ].filter((g) => (!g.requiere || typeof B[g.requiere] === 'function')
                    && !(Array.isArray(op.excluir) && op.excluir.includes(g.id)));

    const paneleDe = {};
    // Cada botón se guarda junto a la función que dice si está puesto: hay dos
    // clases de estado —"cuál está elegido" (un color) y "está prendido" (un
    // efecto)— y con una sola forma no entran las dos.
    const botonesDe = {};
    // Los chips de color a elección: se marcan y se repintan aparte, porque su
    // estado es un color y no un id del catálogo.
    const libres = [];

    // Nota de los grupos que un skin deja sin efecto. Una por panel, creada al
    // armarlo; sincronizar() decide si se ve.
    const notaDe = {};
    let conSkinPuesto = null;
    let anchoMedido = -1;

    function sincronizar() {
      if (!vivo) return;
      rotular();
      sincronizarOpacidad();
      pintarEscena();
      pintarMarco();
      Object.keys(botonesDe).forEach((id) => {
        botonesDe[id].forEach((e) => {
          if (!e.estado) return;
          e.b.setAttribute('aria-pressed', e.estado() ? 'true' : 'false');
        });
      });
      const natural = B.listHatColors().find((c) => c.muestra);
      if (natural) {
        Object.keys(paneleDe).forEach((id) => {
          paneleDe[id].querySelectorAll('.bes-tono[data-natural]').forEach((t) => t.style.setProperty('--tono', natural.muestra));
        });
      }
      libres.forEach((l) => {
        const puesto = !!l.libre.puesto();
        l.lab.dataset.puesto = puesto ? '1' : '0';
        // Sin elegido propio, la muestra queda neutra: pintarla con el color
        // de un preset diría que ése es "el tuyo".
        const v = hex6(l.libre.valor());
        l.tono.style.setProperty('--tono', puesto ? v : 'transparent');
        // No se le pisa el valor a quien lo está arrastrando.
        if (document.activeElement !== l.inp && puesto) l.inp.value = v;
      });
      /* Con un skin puesto, elegir un color no cambia nada en la escena: el
       * motor lo guarda y lo ignora. Un chip que no hace nada se lee como un
       * error, así que esas filas se apagan (inert: sin foco ni clic).
       * 'piel' se apaga con skin y con material (el cuerpo ya no es el
       * elegido). 'accesorio' sólo con un skin: el material lo deja puesto.
       * El modo y no un booleano, porque pasar de Gojo a madera tiene que
       * volver a abrir el accesorio. */
      const chId = typeof B.getCharacter === 'function' ? B.getCharacter() : '';
      const modo = !chId ? '' : (esMateria(chId) ? 'materia' : 'skin');
      if (modo === conSkinPuesto) return;
      conSkinPuesto = modo;
      const filaBloqueada = (bloqueo) => {
        if (!bloqueo || !modo) return false;
        if (bloqueo === 'accesorio') return modo === 'skin';
        return true;
      };
      GRUPOS.forEach((g) => {
        if (notaDe[g.id]) notaDe[g.id].hidden = !filaBloqueada(notaDe[g.id].dataset.bloqueo);
        paneleDe[g.id].querySelectorAll('.bes-fila[data-bloqueo]').forEach((f) => {
          const bloq = filaBloqueada(f.dataset.bloqueo);
          f.toggleAttribute('inert', bloq);
          f.dataset.bloq = bloq ? '1' : '0';
        });
      });
      // La nota suma una línea al panel más alto: se vuelve a igualar.
      if (anchoMedido >= 0) igualarAlto(true);
    }

    /* ── El rótulo de la escena ──
     * Una línea que nombra lo que tiene puesto. Las grillas muestran el nombre
     * de cada opción en dos renglones como mucho, y un "Goku Super Saiyajin 4"
     * cortado no dice cuál quedó elegido; acá se lee entero. Es opcional:
     * lo pone quien arma la escena (el modal sí; la landing no lo necesita). */
    const rotulo = op.rotulo || null;
    let rotuloK = null, rotuloV = null;
    if (rotulo) {
      rotuloK = crear('span', 'bes-rotulo-k');
      rotuloV = crear('span', 'bes-rotulo-v');
      rotulo.append(rotuloK, rotuloV);
    }
    const nombreEn = (lista, id) => {
      const x = lista.find((i) => String(i.id).toLowerCase() === String(id || '').toLowerCase());
      return x ? x.name : '';
    };
    function rotular() {
      if (!rotulo) return;
      const ch = typeof B.getCharacter === 'function' ? B.getCharacter() : null;
      let k, v;
      if (ch && esMateria(ch)) {
        k = 'Material';
        v = [nombreEn(B.listCharacters(), ch), nombreEn(B.listHats(), B.getHat())].filter(Boolean).join(' · ');
      } else if (ch) {
        k = 'Skin';
        v = nombreEn(B.listCharacters(), ch) || ch;
      } else {
        const sk = B.getSkin();
        const cuerpo = sk === 'auto' ? 'Color del tema'
          : sk === 'custom' ? 'Color propio'
          : nombreEn(B.listSkins(), sk);
        k = 'Look';
        v = [nombreEn(B.listHats(), B.getHat()), cuerpo].filter(Boolean).join(' · ');
      }
      rotuloK.textContent = k;
      rotuloV.textContent = v;
      rotulo.title = k + ': ' + v;
    }

    // Un grupo simple es un grupo de UNA sección sin título.
    const seccionesDe = (g) => g.secciones || [{
      items: g.items, adorno: g.adorno, hacer: g.hacer, libre: g.libre, forma: g.forma,
      insignia: g.insignia, previa: g.previa,
      activo: g.accion ? null : (it) => String(g.puesto() || '').toLowerCase() === String(it.id).toLowerCase(),
    }];

    /* ── La navegación: apartados y ejes ─────────────────────────────────────
     * Eran nueve pestañas planas, y nueve no entran en una fila: envolvían en
     * dos renglones desparejos y el ojo no encontraba dónde estaba parado. Se
     * agrupan en apartados que se leen como lo que son —qué hace, cómo se ve,
     * qué tiene puesto, dónde está— y cada apartado muestra sus ejes en una
     * segunda fila, siempre de un renglón.
     *
     * El agrupamiento es una tabla y no un `if`: un eje nuevo declara su
     * `apartado` y aparece solo. Un apartado sin ejes (la landing excluye el
     * fondo) desaparece; uno con un solo eje no muestra segunda fila de
     * pestañas, porque una pestaña sola no elige nada.
     *
     * "Detalle" va con el accesorio y no con la apariencia: es su segundo
     * color (setHatColor2). Y el fondo tiene apartado propio porque no es de
     * él sino del lugar: con un skin puesto se sigue viendo. */
    const svg = (d) => '<svg viewBox="0 0 24 24" width="15" height="15" fill="none" stroke="currentColor" stroke-width="1.7" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false">' + d + '</svg>';
    const APARTADOS = [
      { id: 'gestos', nombre: 'Gestos', ayuda: 'Tocá un gesto y Boberto lo hace en la escena.',
        icono: svg('<path d="M12 3v3M12 18v3M3 12h3M18 12h3M5.6 5.6l2.1 2.1M16.3 16.3l2.1 2.1M5.6 18.4l2.1-2.1M16.3 7.7l2.1-2.1"/>') },
      { id: 'apariencia', nombre: 'Apariencia',
        icono: svg('<circle cx="12" cy="8" r="4"/><path d="M4.5 20.5a7.5 7.5 0 0 1 15 0"/>') },
      { id: 'accesorio', nombre: 'Accesorio',
        icono: svg('<path d="M3 18.5h18"/><path d="M6 18.5c0-6 2.4-11 6-11s6 5 6 11"/><path d="M9.5 13.5h5"/>') },
      { id: 'fondo', nombre: 'Fondo', ayuda: 'El paisaje que tiene detrás en la sidebar de las apps.',
        icono: svg('<path d="M3 19l6-8 4 5 3-4 5 7H3z"/><circle cx="17" cy="6.5" r="1.6"/>') },
      { id: 'borde', nombre: 'Bordes', ayuda: 'El marco del lugar donde está, en la sidebar y en esta vista.',
        icono: svg('<rect x="4" y="4" width="16" height="16" rx="1.5"/>') },
    ];
    // Un eje con un apartado que la tabla no conoce no se pierde: arma el suyo.
    GRUPOS.forEach((g) => {
      if (!APARTADOS.some((a) => a.id === g.apartado)) {
        APARTADOS.push({ id: g.apartado || g.id, nombre: g.tab, icono: '' });
        g.apartado = g.apartado || g.id;
      }
    });
    const apartados = APARTADOS
      .map((a) => Object.assign({}, a, { ejes: GRUPOS.filter((g) => g.apartado === a.id) }))
      .filter((a) => a.ejes.length);

    // Ids únicos por montaje: la landing y el estudio pueden convivir en la
    // misma página, y aria-controls necesita ids que no choquen.
    const pref = 'bes' + (++montar._n) + '-';
    tabs.classList.add('bes-tabs');
    paneles.classList.add('bes-paneles');
    /* El contenedor deja de ser el tablist: ahora adentro hay DOS (apartados y
     * ejes), y un tablist dentro de otro no es un patrón que un lector de
     * pantalla sepa anunciar. La etiqueta que traía pasa a la fila principal. */
    const etiqueta = tabs.getAttribute('aria-label') || 'Personalizar a Boberto';
    tabs.removeAttribute('role');
    tabs.removeAttribute('aria-label');
    const navApartados = crear('div', 'bes-nav-apartados');
    navApartados.setAttribute('role', 'tablist');
    navApartados.setAttribute('aria-label', etiqueta);
    // El ancho de cada segmento sale de cuántos apartados hay, no de su texto:
    // un control segmentado con botones de anchos distintos se lee torcido.
    navApartados.style.setProperty('--bes-n', String(apartados.length));
    const navEjes = crear('div', 'bes-nav-ejes');
    tabs.append(navApartados, navEjes);

    const tabDe = (id, clase, texto) => {
      const t = crear('button', 'bes-tab ' + clase);
      t.type = 'button';
      t.id = id;
      t.setAttribute('role', 'tab');
      t.setAttribute('aria-selected', 'false');
      t.tabIndex = -1;
      if (texto) t.appendChild(crear('span', 'bes-tab-txt', texto));
      return t;
    };

    apartados.forEach((a) => {
      const t = tabDe(pref + 'ap-' + a.id, 'bes-tab--apartado', '');
      if (a.icono) t.insertAdjacentHTML('beforeend', a.icono);   // markup propio, sin datos de afuera
      t.appendChild(crear('span', 'bes-tab-txt', a.nombre));
      t.addEventListener('click', () => mostrar(a.id));
      navApartados.appendChild(t);
      a._tab = t;
      a.eje = a.ejes[0].id;
      a.ejes.forEach((g) => { g._ap = a; });

      /* La segunda fila existe SIEMPRE, también cuando hay un solo eje: ahí es
       * una línea de ayuda en vez de pestañas. Si apareciera y desapareciera,
       * la grilla de abajo saltaría 36 px cada vez que se cambia de apartado
       * — y en la landing lo que salta es la sección entera. */
      if (a.ejes.length > 1) {
        const fila = crear('div', 'bes-subtabs');
        fila.setAttribute('role', 'tablist');
        fila.setAttribute('aria-label', a.nombre);
        a.ejes.forEach((g) => {
          const st = tabDe(pref + 'tab-' + g.id, 'bes-tab--eje', g.corto || g.tab);
          st.title = g.tab;
          st.setAttribute('aria-controls', pref + 'pane-' + g.id);
          st.addEventListener('click', () => mostrar(g.id));
          fila.appendChild(st);
          g._tab = st;
        });
        a._fila = fila;
      } else {
        a._fila = crear('p', 'bes-nav-ayuda', a.ayuda || a.ejes[0].tab);
      }
      a._fila.hidden = true;
      navEjes.appendChild(a._fila);
    });

    GRUPOS.forEach((g) => {
      const pane = document.createElement('div');
      pane.className = 'bes-pane';
      pane.id = pref + 'pane-' + g.id;
      pane.setAttribute('role', 'tabpanel');
      // Lo nombra su pestaña de eje; si el apartado tiene uno solo, el apartado.
      pane.setAttribute('aria-labelledby', (g._tab || g._ap._tab).id);
      pane.hidden = true;
      botonesDe[g.id] = [];
      if (g.vestir || g.nota) {
        const nota = document.createElement('p');
        nota.className = 'bes-nota';
        nota.hidden = true;
        const bloqueo = g.bloqueoNota || (g.vestir === 'accesorio' ? 'accesorio' : 'piel');
        nota.dataset.bloqueo = bloqueo;
        nota.textContent = g.nota || (bloqueo === 'accesorio'
          ? 'Con un skin puesto el accesorio queda guardado. Un material sí lo deja puesto.'
          : 'Con un skin o un material, el cuerpo trae sus propios colores.');
        pane.appendChild(nota);
        notaDe[g.id] = nota;
      }
      // Un control propio del eje, arriba de su grilla (la opacidad del fondo).
      if (g.cabeza) pane.appendChild(g.cabeza());
      seccionesDe(g).forEach((sec) => {
        const forma = sec.forma || 'icono';
        if (sec.titulo) {
          const h = document.createElement('p');
          h.className = 'bes-seccion';
          h.textContent = sec.titulo;
          pane.appendChild(h);
        }
        const fila = document.createElement('div');
        fila.className = 'bes-fila bes-fila--' + forma;
        const bloqueo = sec.bloqueo || (g.vestir === 'accesorio' ? 'accesorio' : (g.vestir ? 'piel' : ''));
        if (bloqueo) fila.dataset.bloqueo = bloqueo;
        sec.items().forEach((it) => {
          const b = document.createElement('button');
          b.type = 'button';
          b.className = 'bes-chip bes-chip--' + forma;
          // aria-pressed y no una clase: el estado es "esto está puesto", y un
          // lector de pantalla tiene que poder saberlo sin ver el color.
          if (sec.activo) b.setAttribute('aria-pressed', 'false');
          b.appendChild(sec.adorno(it));
          b.appendChild(crear('span', 'bes-chip-nombre', it.name));
          // Después del nombre y no antes: el lector de pantalla dice "Arcade,
          // Premium" y no al revés. Dónde se ve lo decide la hoja.
          const ins = sec.insignia ? sec.insignia(it) : '';
          if (ins) b.appendChild(crear('span', 'bes-insignia', ins));
          // Probar sin elegir: puntero encima o foco de teclado pintan esta
          // opción en el escenario; al irse, vuelve lo que había.
          if (sec.previa) {
            b.addEventListener('pointerenter', () => sec.previa('puntero', it.id));
            b.addEventListener('pointerleave', () => sec.previa('puntero', null));
            b.addEventListener('focus', () => sec.previa('foco', it.id));
            b.addEventListener('blur', () => sec.previa('foco', null));
          }
          // Un interruptor muestra su estado también con forma (la pastilla de
          // la esquina), no sólo con color: prendido y apagado se distinguen
          // en escala de grises.
          if (forma === 'interruptor') b.appendChild(crear('i', 'bes-pip'));
          b.title = it.desc || it.name;
          b.addEventListener('click', () => {
            sec.hacer(it);
            if (sec.activo) { tocado = true; sincronizar(); }
          });
          fila.appendChild(b);
          botonesDe[g.id].push({ b: b, estado: sec.activo ? () => sec.activo(it) : null });
        });
        /* El color a elección, al final de la fila. Un <label> con el input
         * adentro y no un botón que lo abra: el selector nativo se abre con
         * el click real sobre el input, y showPicker() no está en todos los
         * navegadores de la oficina. */
        if (sec.libre) {
          const lab = document.createElement('label');
          lab.className = 'bes-chip bes-chip--' + forma + ' bes-chip-libre';
          lab.title = sec.libre.etiqueta;
          const tono = sw('transparent');
          const n = crear('span', 'bes-chip-nombre', 'Otro');
          const inp = document.createElement('input');
          inp.type = 'color';
          inp.className = 'bes-libre-input';
          inp.setAttribute('aria-label', sec.libre.etiqueta);
          inp.value = hex6(sec.libre.valor());
          inp.addEventListener('input', () => {
            sec.libre.poner(inp.value);
            tocado = true;
            sincronizar();
          });
          lab.append(tono, n, inp);
          fila.appendChild(lab);
          libres.push({ lab: lab, tono: tono, inp: inp, libre: sec.libre });
        }
        pane.appendChild(fila);
      });
      paneles.appendChild(pane);
      paneleDe[g.id] = pane;
    });

    /* Acepta el id de un eje ('ojos') o de un apartado ('apariencia'). Con un
     * apartado, vuelve al eje que se estaba mirando ahí: ir a Gestos y volver
     * no tiene que tirarte de "Ojos" a "Skins". Cuando un id es las dos cosas
     * ('gestos', 'fondo'), gana el eje, que es lo más preciso. */
    function mostrar(id) {
      let g = GRUPOS.find((x) => x.id === id) || null;
      const a = g ? g._ap : apartados.find((x) => x.id === id);
      if (!a) return;
      if (!g) g = a.ejes.find((x) => x.id === a.eje) || a.ejes[0];
      a.eje = g.id;
      apartados.forEach((x) => {
        const activo = x === a;
        x._tab.setAttribute('aria-selected', activo ? 'true' : 'false');
        x._tab.tabIndex = activo ? 0 : -1;
        x._tab.setAttribute('aria-controls', pref + 'pane-' + x.eje);
        x._fila.hidden = !activo;
      });
      GRUPOS.forEach((x) => {
        const puesto = x.id === x._ap.eje;
        if (x._tab) {
          x._tab.setAttribute('aria-selected', puesto ? 'true' : 'false');
          x._tab.tabIndex = puesto ? 0 : -1;
        }
        paneleDe[x.id].hidden = x !== g;
      });
      // Una tarjeta que se esconde con su panel no avisa que el puntero o el
      // foco se fueron: sin esto, el escenario se quedaba mostrando la prueba.
      if (previaPuntero || previaFoco) { previaPuntero = previaFoco = null; pintarEscena(); }
      if (bordePuntero || bordeFoco) { bordePuntero = bordeFoco = null; pintarMarco(); }
      // Donde los paneles scrollean por dentro (el modal), un eje nuevo arranca
      // arriba: heredar el scroll del anterior deja la grilla a mitad de camino.
      if (paneles.scrollTop) paneles.scrollTop = 0;
    }
    if (apartados.length) mostrar(apartados[0].id);

    /* Flechas, Inicio y Fin: el patrón de tabs de ARIA, dentro de la fila en
     * la que está el foco. Cada fila deja una sola pestaña en el orden de
     * tabulación, así que Tab va de apartados a ejes y de ejes a la grilla,
     * y las flechas recorren cada fila. Activación automática: mover el foco
     * es elegir, porque cambiar de panel no cuesta nada. */
    escuchar(tabs, 'keydown', (e) => {
      const t = e.target && e.target.closest ? e.target.closest('[role="tab"]') : null;
      if (!t || !tabs.contains(t)) return;
      const lista = Array.prototype.slice.call(t.parentNode.querySelectorAll('[role="tab"]'));
      const i = lista.indexOf(t);
      let j = -1;
      if (e.key === 'ArrowRight' || e.key === 'ArrowDown') j = (i + 1) % lista.length;
      else if (e.key === 'ArrowLeft' || e.key === 'ArrowUp') j = (i - 1 + lista.length) % lista.length;
      else if (e.key === 'Home') j = 0;
      else if (e.key === 'End') j = lista.length - 1;
      if (j < 0) return;
      e.preventDefault();
      lista[j].click();
      lista[j].focus();
    });

    /* ── Todas las pestañas miden lo mismo ──────────────────────────────────
     * Cada eje tiene su propia cantidad de opciones: "Gestos" son veinticinco y
     * entran en cinco filas, "Accesorio" son treinta y uno y entran en nueve.
     * Con el alto libre, cambiar de pestaña estiraba o encogía el panel — y en
     * la landing el panel es la mitad de una sección a sangre, así que lo que
     * saltaba era la sección entera.
     *
     * El alto se fija en el MÁS ALTO, y se MIDE en vez de escribirse. Un número
     * a mano acá queda corto el día que se agrega un accesorio, que es el único
     * día en que a alguien le importa — y quedar corto no se ve como un número
     * viejo, se ve como que el panel volvió a saltar.
     *
     * Se destapan todos para medir porque un panel oculto mide cero: `hidden` es
     * `display: none`, no una opacidad. Se los devuelve a su estado en el mismo
     * cuadro, así que nadie ve el destape.
     *
     * Y se vuelve a medir cuando cambia el ancho o cuando terminan de cargar las
     * fuentes: las dos cosas cambian cuántos chips entran por fila, o sea el
     * alto del más alto.
     *
     * Donde el contenedor ya tiene alto propio y scrollea por dentro (el
     * modal: `igualarAlto: false`), esto sobra y además estorba: un min-height
     * del panel más alto dejaría a los cortos con un hueco para scrollear. */
    const igualar = op.igualarAlto !== false;
    function igualarAlto(forzar) {
      if (!vivo || !igualar) return;
      /* El ANCHO es lo que decide cuántos chips entran por fila, o sea el alto.
       * Comparar contra el último medido hace dos cosas: evita remedir por nada,
       * y —esto es lo importante— corta el bucle del observador. `igualarAlto`
       * escribe `minHeight`, que cambia el tamaño del elemento observado: sin
       * este corte, cada medición dispararía la siguiente para siempre. */
      const w = paneles.getBoundingClientRect().width;
      if (!forzar && w === anchoMedido) return;
      anchoMedido = w;

      const antes = GRUPOS.map((g) => paneleDe[g.id].hidden);
      GRUPOS.forEach((g) => { paneleDe[g.id].hidden = false; });
      let alto = 0;
      GRUPOS.forEach((g) => {
        alto = Math.max(alto, paneleDe[g.id].getBoundingClientRect().height);
      });
      GRUPOS.forEach((g, i) => { paneleDe[g.id].hidden = antes[i]; });
      // Sin medida útil no se toca nada: dejar un min-height en 0 sería peor que
      // el salto, porque recorta el panel más alto.
      if (alto > 0) paneles.style.minHeight = Math.ceil(alto) + 'px';
    }
    igualarAlto(true);
    /* El ResizeObserver es el disparador PRINCIPAL y el evento `resize` el
     * respaldo, no al revés. La caja puede cambiar de ancho sin que la ventana
     * se mueva —aparece una barra de scroll, cambia una fuente, el contenedor se
     * reacomoda— y ahí `resize` no dispara nunca. Es la misma razón por la que
     * la escena de al lado ya usa uno. */
    observar(() => igualarAlto(), paneles);
    escuchar(window, 'resize', () => igualarAlto(), { passive: true });
    // Éstos fuerzan: el ancho puede no haber cambiado y las letras sí.
    escuchar(window, 'load', () => igualarAlto(true));
    if (document.fonts && document.fonts.ready) {
      document.fonts.ready.then(() => igualarAlto(true)).catch(() => {});
    }

    sincronizar();
    // El skin y los efectos también se cambian desde el panel de Ajustes, en
    // esta misma página: sin esto el menú seguiría marcando (y bloqueando) lo
    // de antes.
    escuchar(document, 'boberto-skin-changed', sincronizar);
    escuchar(document, 'boberto-fx-changed', sincronizar);
    escuchar(document, 'boberto-fondo-changed', sincronizar);
    escuchar(document, 'boberto-borde-changed', sincronizar);
    const hayFondo = GRUPOS.some((g) => g.id === 'fondo');
    const hayBorde = GRUPOS.some((g) => g.id === 'borde');

    function restablecer() {
      if (!tocado) return;
      B.setSkin(inicial.skin);
      if (inicial.skinColor) B.setSkinColor(inicial.skinColor);
      // setSkinColor deja el skin en 'custom', así que el skin se repone DESPUÉS.
      B.setSkin(inicial.skin);
      B.setEyeColor(inicial.eye);
      B.setHatColor(inicial.hatColor);
      if (inicial.hatColor2 !== null && typeof B.setHatColor2 === 'function') B.setHatColor2(inicial.hatColor2);
      B.setFx('body', inicial.fxBody);
      B.setFx('acc', inicial.fxAcc);
      if (inicial.glowBody !== null) B.setGlow('body', inicial.glowBody);
      // Sólo si la pestaña está: quien la excluyó (la landing) no lo tocó, y
      // reponerlo igual escribiría en storage sin motivo.
      if (hayFondo && inicial.fondoOps !== null) B.setFondoOpacidades(inicial.fondoOps);
      if (hayFondo && inicial.fondo !== null) B.setFondo(inicial.fondo);
      if (hayBorde && inicial.borde !== null && typeof B.setBorde === 'function') B.setBorde(inicial.borde);
      B.setHat(inicial.hat);
      // Al final: el personaje no toca lo de arriba, y así queda el que estaba.
      if (inicial.character !== null) B.setCharacter(inicial.character);
      tocado = false;
      sincronizar();
    }

    if (op.volver) escuchar(op.volver, 'click', restablecer);
    // De fábrica: los defaults los tiene el motor (resetLook). Cuenta como
    // tocado, así "Restablecer" puede deshacerlo.
    if (op.fabrica && typeof B.resetLook === 'function') {
      escuchar(op.fabrica, 'click', () => { B.resetLook(); tocado = true; sincronizar(); });
    } else if (op.fabrica) {
      op.fabrica.hidden = true;
    }
    // `pagehide` y no `beforeunload`: este último no dispara cuando la página
    // entra al bfcache, y ahí te ibas con el accesorio de prueba puesto.
    if (!persistir) escuchar(window, 'pagehide', restablecer);

    function desmontar() {
      if (!vivo) return;
      // Sin persistir, irse es lo mismo que salir de la página: se repone.
      if (!persistir) restablecer();
      vivo = false;
      limpiar.forEach((fn) => { try { fn(); } catch (e) {} });
      limpiar.length = 0;
      // Devuelve a Boberto a su casa (el dock) o al reclamo que haya debajo.
      B.releaseStage(escena);
      tabs.textContent = '';
      paneles.textContent = '';
      if (rotulo) rotulo.textContent = '';
      if (capaEscena) {
        capaEscena.textContent = '';
        capaEscena.removeAttribute('data-fondo');
        capaEscena.removeAttribute('data-previa');
        capaEscena.style.removeProperty('--bes-fondo-op');
      }
      escena.style.removeProperty('--bes-u');
      paneles.style.minHeight = '';
    }

    return {
      desmontar: desmontar,
      restablecer: restablecer,
      mostrar: mostrar,
      tocado: () => tocado,
    };
  }
  montar._n = 0;

  /* ══ El modal ═══════════════════════════════════════════════════════════
   *
   * Lo abre el dock de la sidebar. Escena a la izquierda, menú a la derecha;
   * en un teléfono, casi pantalla completa y el menú debajo de la escena.
   *
   * La escena PRESTA al Boberto real (claimStage), no pone una peana. La peana
   * es un espejo: otro canvas con su propio bucle, y sus gestos se piden por
   * otra API (peanaGesto). Con ella habría dos Bobertos a la vez —el del dock
   * debajo del velo y el del estudio— y el menú tendría que saber a cuál le
   * habla. Prestado, el menú es exactamente el de la landing (playGesture
   * sobre el único personaje), con partículas y animaciones CSS completas; al
   * cerrar, releaseStage lo devuelve al slot vivo de la sidebar. Que el dock
   * quede vacío mientras tanto no se ve: está debajo del velo.
   *
   * z-index 2147482000: arriba de cualquier cosa de una app (topbar, drawers,
   * novedades) y DEBAJO del panel de Ajustes compartido (2147483000+). No se
   * pueden pisar: el velo tapa el engranaje y el foco no sale del cuadro, así
   * que con el estudio abierto no hay cómo abrir Ajustes.
   */
  let _abierto = null;

  // Escala de la escena del modal: la más grande de la escalera de la landing
  // que entre en el ANCHO y en el ALTO. En el modal el alto es lo escaso (el
  // cuadro mide como mucho 700 px) y medir sólo el ancho le cortaba la cabeza.
  function escalaModal(escena) {
    const B = window.BOBERTO;
    const box = (B && typeof B.box === 'function') ? B.box() : { W: 20, H: 21 };
    const r = cajaDe(escena);
    if (!r.width || !r.height) return 11;
    // El alto útil descuenta el piso (abajo) y el rótulo: el lienzo se apoya
    // sobre la línea del piso, no en el borde del escenario.
    const cabe = (e) => box.W * e <= r.width * 0.86 && box.H * e <= r.height * 0.76;
    // Escalones más finos que los de la landing: acá el escenario cambia de
    // tamaño con la ventana, y saltar de 20 a 15 dejaba un Boberto chico en
    // un escenario que tenía lugar para uno bastante más grande.
    const escalera = [24, 22, 20, 18, 16, 14, 12, 10, 8, 6];
    for (let i = 0; i < escalera.length; i++) if (cabe(escalera[i])) return escalera[i];
    return 5;
  }

  function crear(tag, clase, texto) {
    const el = document.createElement(tag);
    if (clase) el.className = clase;
    if (texto) el.textContent = texto;
    return el;
  }

  const FOCO_SEL = 'button:not([disabled]), [href], input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])';
  function enfocables(raiz) {
    return Array.prototype.filter.call(raiz.querySelectorAll(FOCO_SEL), (el) =>
      el.getClientRects().length > 0 && !el.closest('[inert]') && el.tabIndex >= 0);
  }

  /**
   * Abre el estudio. Devuelve si quedó abierto.
   *
   * op.origen — a quién se le devuelve el foco al cerrar (el dock).
   */
  function abrir(op) {
    if (_abierto) return true;
    op = op || {};
    if (!motor() || !document.body) return false;

    const raiz = crear('div', 'bes-modal');
    const velo = crear('div', 'bes-velo');
    velo.setAttribute('aria-hidden', 'true');
    const dialogo = crear('div', 'bes-dialogo');
    dialogo.setAttribute('role', 'dialog');
    dialogo.setAttribute('aria-modal', 'true');
    dialogo.setAttribute('aria-labelledby', 'bes-titulo');
    dialogo.setAttribute('aria-describedby', 'bes-bajada');

    const cab = crear('header', 'bes-cab');
    const titulos = crear('div', 'bes-titulos');
    const h = crear('h2', 'bes-titulo', 'Estudio de Boberto');
    h.id = 'bes-titulo';
    // Una sola línea: la explicación larga de qué se guarda vive en el pie,
    // al lado de los botones a los que se refiere.
    const bajada = crear('p', 'bes-bajada', 'Vestilo, probá sus gestos y elegí su fondo. Lo lleva puesto en todas las apps.');
    bajada.id = 'bes-bajada';
    titulos.append(h, bajada);
    const x = crear('button', 'bes-x');
    x.type = 'button';
    x.setAttribute('aria-label', 'Cerrar el estudio');
    x.title = 'Cerrar (Esc)';
    x.innerHTML = '<svg viewBox="0 0 24 24" width="16" height="16" fill="none" stroke="currentColor" stroke-width="2" aria-hidden="true"><path d="M6 6l12 12M18 6L6 18"/></svg>';
    cab.append(titulos);
    /* El camino de vuelta a Personalización. Desde ahí se llega acá ("Abrir
     * el estudio"), y sin este botón cerrar el estudio dejaba a la persona en
     * la app, sin el panel del que venía. Los dos modales no se apilan: el
     * panel va por encima (z-index), así que se cierra éste y se abre aquél. */
    const T = window.DBMON_TWEAKS;
    if (T && typeof T.open === 'function') {
      const ir = crear('button', 'bes-ir');
      ir.type = 'button';
      ir.title = 'Paleta, tipografía, interfaz y efectos de la suite';
      ir.innerHTML = '<svg viewBox="0 0 24 24" width="15" height="15" fill="none" stroke="currentColor" stroke-width="1.7" stroke-linecap="round" aria-hidden="true"><path d="M4 7h10M18 7h2M4 17h4M12 17h8"/><circle cx="16" cy="7" r="2"/><circle cx="10" cy="17" r="2"/></svg>';
      ir.appendChild(crear('span', 'bes-ir-txt', 'Personalización'));
      ir.addEventListener('click', () => { cerrar(); T.open(); });
      cab.appendChild(ir);
    }
    cab.appendChild(x);

    const cuerpo = crear('div', 'bes-cuerpo');
    /* El escenario va en capas, con el orden ESCRITO y no librado al orden de
     * los nodos: fondo (0) → luz y piso (1) → Boberto (2) → marcas y rótulo
     * (3). `isolation` le da contexto de apilado propio, así ningún z-index de
     * acá compite con los de la app de atrás.
     *
     * La capa de fondo la pinta montar() (op.fondo): el fondo puesto, con su
     * opacidad, o el de la tarjeta que se está probando. Existe DETRÁS de
     * Boberto desde que nace el escenario; agregarla después, encima, es como
     * el fondo podía terminar tapándolo en la sidebar cuando dependía de que
     * nadie le pusiera z-index al piso. */
    const escenario = crear('div', 'bes-escenario');
    const capaFondo = crear('div', 'bes-escena-fondo');
    capaFondo.setAttribute('aria-hidden', 'true');
    const luz = crear('div', 'bes-escena-luz');
    luz.setAttribute('aria-hidden', 'true');
    // La escena queda VACÍA a propósito: adentro se muda el Boberto de verdad.
    const escena = crear('div', 'bes-escena');
    const marcas = crear('div', 'bes-escena-marcas');
    marcas.setAttribute('aria-hidden', 'true');
    const rotulo = crear('p', 'bes-rotulo');
    // El marco cierra el lugar (personaje + fondo) por arriba del piso. Sin
    // modelo no se ve: "Sin borde" es el default.
    const marco = crear('i', 'plt-borde-marco');
    marco.setAttribute('aria-hidden', 'true');
    escenario.append(capaFondo, luz, escena, marcas, rotulo, marco);

    const panel = crear('div', 'bes-panel');
    const tabs = crear('div');
    const paneles = crear('div');
    /* El pie dice UNA cosa en una línea. Lo que hace cada botón va en su
     * `title`: escrito como párrafo al lado, envolvía en tres renglones y se
     * cortaba contra el borde del cuadro. */
    const pie = crear('div', 'bes-pie');
    const nota = crear('p', 'bes-pie-nota');
    // Dos largos del mismo aviso: el CSS elige según el ancho del editor (al
    // 150 % de escala no entra el largo al lado de los dos botones, y cortado
    // con puntos suspensivos no se entiende).
    nota.append(crear('i', 'bes-pie-punto'),
      crear('span', 'bes-pie-largo', 'Se guarda automáticamente'),
      crear('span', 'bes-pie-corto', 'Se guarda solo'));
    const acciones = crear('div', 'bes-pie-acciones');
    const fabrica = crear('button', 'bes-boton bes-boton--suave', 'De fábrica');
    fabrica.type = 'button';
    fabrica.title = 'Lo deja como vino: sin skin, con el accesorio y los colores de fábrica, sin efectos, sin fondo y sin borde';
    const restablecer = crear('button', 'bes-boton', 'Restablecer');
    restablecer.type = 'button';
    restablecer.title = 'Lo deja como estaba cuando abriste el estudio';
    acciones.append(fabrica, restablecer);
    pie.append(nota, acciones);
    panel.append(tabs, paneles, pie);
    cuerpo.append(escenario, panel);
    dialogo.append(cab, cuerpo);
    raiz.append(velo, dialogo);

    // El scroll de la página se traba mientras el estudio está abierto: con la
    // rueda sobre el velo, scrollear la tabla de atrás se lee como que el
    // modal no es modal. Se guarda el valor anterior para no pisar el de la app.
    const html = document.documentElement;
    const overflowPrevio = html.style.overflow;
    html.style.overflow = 'hidden';

    document.body.appendChild(raiz);

    const manejador = montar({
      escena: escena, tabs: tabs, paneles: paneles, volver: restablecer,
      fabrica: fabrica, persistir: true, escala: escalaModal, marco: marco,
      rotulo: rotulo, igualarAlto: false, fondo: capaFondo,
    });
    if (!manejador) {
      raiz.remove();
      html.style.overflow = overflowPrevio;
      return false;
    }

    function onKey(e) {
      if (e.key === 'Escape') { e.preventDefault(); e.stopPropagation(); cerrar(); return; }
      if (e.key !== 'Tab') return;
      // Trampa de foco: el aria-modal sólo se lo dice al lector de pantalla; el
      // teclado igual se iba al contenido de atrás.
      const lista = enfocables(dialogo);
      if (!lista.length) { e.preventDefault(); return; }
      const primero = lista[0], ultimo = lista[lista.length - 1];
      if (e.shiftKey && (document.activeElement === primero || !dialogo.contains(document.activeElement))) {
        e.preventDefault(); ultimo.focus();
      } else if (!e.shiftKey && (document.activeElement === ultimo || !dialogo.contains(document.activeElement))) {
        e.preventDefault(); primero.focus();
      }
    }
    // Si el foco se escapa igual (un click en el velo, un script de la app),
    // vuelve al cuadro.
    function onFocusIn(e) {
      if (!dialogo.contains(e.target)) {
        const lista = enfocables(dialogo);
        if (lista.length) lista[0].focus();
      }
    }
    document.addEventListener('keydown', onKey, true);
    document.addEventListener('focusin', onFocusIn);
    velo.addEventListener('click', cerrar);
    x.addEventListener('click', cerrar);

    _abierto = {
      raiz: raiz, manejador: manejador, origen: op.origen || null,
      overflowPrevio: overflowPrevio,
      soltar: () => {
        document.removeEventListener('keydown', onKey, true);
        document.removeEventListener('focusin', onFocusIn);
      },
    };

    // Foco en la pestaña elegida: es lo primero que se usa, y anunciarla dice
    // dónde se está.
    const sel = tabs.querySelector('[aria-selected="true"]');
    (sel || x).focus();

    document.dispatchEvent(new CustomEvent('boberto-estudio-abierto'));
    return true;
  }

  function cerrar() {
    const a = _abierto;
    if (!a) return false;
    _abierto = null;
    a.soltar();
    // Primero se suelta la escena (Boberto vuelve al dock) y DESPUÉS se saca el
    // cuadro: al revés, releaseStage encontraba el host desconectado y la pila
    // lo descartaba antes de poder devolverlo.
    a.manejador.desmontar();
    a.raiz.remove();
    document.documentElement.style.overflow = a.overflowPrevio;
    // El foco vuelve a quien abrió. Si ese nodo ya no existe (JiraToolkit
    // recreó la sidebar), al dock vivo.
    const B = window.BOBERTO;
    const destino = (a.origen && a.origen.isConnected) ? a.origen
      : (B && typeof B.dock === 'function' ? B.dock() : null);
    if (destino && typeof destino.focus === 'function') destino.focus();
    document.dispatchEvent(new CustomEvent('boberto-estudio-cerrado'));
    return true;
  }

  window.BOBERTO_ESTUDIO = {
    montar: montar,
    abrir: abrir,
    cerrar: cerrar,
    abierto: () => !!_abierto,
  };
})();
