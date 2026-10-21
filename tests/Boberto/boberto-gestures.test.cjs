const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

// Same headless canvas as the accessory tests: records every call and checks
// that numbers stay finite and save/restore stay balanced.
function fixture() {
  const store = new Map([['smg_boberto_migrated', '1']]);
  const calls = [];
  let depth = 0;
  const ctx = new Proxy({}, {
    get(target, key) {
      if (key in target) return target[key];
      if (['createLinearGradient', 'createRadialGradient', 'createConicGradient'].includes(key)) return () => ({ addColorStop() {} });
      if (key === 'measureText') return () => ({ width: 10 });
      if (key === 'getTransform') return () => ({ a: 1, b: 0, c: 0, d: 1, e: 0, f: 0 });
      return (...args) => {
        for (const arg of args) if (typeof arg === 'number') assert.ok(Number.isFinite(arg), `${String(key)} received ${arg}`);
        if (key === 'save') depth++;
        if (key === 'restore') assert.ok(--depth >= 0, 'balanced canvas state');
        calls.push([key, ...args]);
      };
    },
    set(target, key, value) { target[key] = value; calls.push([key, value]); return true; },
  });
  function element() {
    return { style: { setProperty() {}, removeProperty() {} }, dataset: {}, classList: { add() {}, remove() {}, toggle() {}, contains() { return false; } },
      appendChild() {}, remove() {}, setAttribute() {}, removeAttribute() {}, getAttribute() { return null; }, addEventListener() {},
      querySelector() { return null; }, querySelectorAll() { return []; }, getContext() { ctx.canvas = this; return ctx; },
      getBoundingClientRect() { return { left: 0, top: 0, width: 100, height: 100 }; } };
  }
  const document = { readyState: 'loading', documentElement: element(), body: element(), head: element(),
    createElement: element, addEventListener() {}, dispatchEvent() {}, querySelector() { return null; }, querySelectorAll() { return []; }, getElementById() { return null; } };
  const window = { addEventListener() {}, matchMedia: () => ({ matches: false, addEventListener() {} }), devicePixelRatio: 1 };
  const storage = { getItem: key => store.get(key) ?? null, setItem: (key, value) => store.set(key, String(value)), removeItem: key => store.delete(key) };
  const observer = class { observe() {} disconnect() {} };
  const context = vm.createContext({ window, document, localStorage: storage, console, MutationObserver: observer, ResizeObserver: observer,
    IntersectionObserver: observer, CustomEvent: class {}, requestAnimationFrame: () => 1, cancelAnimationFrame() {},
    setTimeout: () => 1, clearTimeout() {}, setInterval: () => 1, clearInterval() {}, performance: { now: () => 0 },
    getComputedStyle: () => ({ getPropertyValue: () => '', filter: 'none' }), innerWidth: 1280, innerHeight: 800 });
  vm.runInContext(fs.readFileSync(path.join(__dirname, '../../src/AseIsql.Desktop/Assets/Boberto/boberto.js'), 'utf8'), context);
  const api = window.BOBERTO;
  function render(pose, opts = {}) {
    calls.length = 0;
    api.renderPose(element(), pose, 3, Object.assign({ hat: 'limpio', character: '' }, opts));
    assert.equal(depth, 0, 'render restores canvas state');
    return JSON.stringify(calls);
  }
  return { api, render };
}

const gestures = {
  pesas: ['pesasAgarra', 'pesasTira', 'pesasPanza', 'pesasTiembla1', 'pesasTiembla2', 'pesasEnvion', 'pesasArriba', 'pesasSuelta'],
  magia: ['magiaMuestra', 'magiaGolpe', 'magiaFloreo', 'magiaGolpe2', 'magiaEspia', 'magiaSale', 'magiaTada'],
  jardin: ['jardinSaca', 'jardinRiega', 'jardinBrota', 'jardinMira', 'jardinHojas', 'jardinPimpollo', 'jardinEspera', 'jardinCanta', 'jardinFlor'],
  guardia: ['guardiaSuena', 'guardiaSalta', 'guardiaAtiende', 'guardiaEscucha', 'guardiaPreocupa', 'guardiaEntiende', 'guardiaAsiente', 'guardiaListo', 'guardiaCorta'],
  barrilete: ['barrileteSuelta', 'barrileteSube', 'barrileteAlto1', 'barrileteAlto2', 'barrileteCae', 'barrileteTira', 'barrileteVuelve', 'barrileteRulo1', 'barrileteRulo2'],
};

for (const [id, poses] of Object.entries(gestures)) {
  test(`${id} is in the public catalog and every keyframe draws its own frame`, () => {
    const f = fixture();
    assert.equal(f.api.listGestures().filter(g => g.id === id).length, 1);
    const all = f.api.listPoses();
    const frames = poses.map(p => { assert.ok(all.includes(p), `${p} is a public pose`); return f.render(p); });
    assert.equal(new Set(frames).size, frames.length, 'no two keyframes render the same');
    assert.notEqual(frames[0], f.render('base'), 'the prop shows up');
  });

  test(`${id} keyframes render under accessories, outfits and materials`, () => {
    const f = fixture();
    for (const p of poses) {
      f.render(p, { hat: 'caballero' });
      f.render(p, { hat: 'bata' });
      f.render(p, { hat: 'limpio', character: 'vitral' });
    }
  });
}
