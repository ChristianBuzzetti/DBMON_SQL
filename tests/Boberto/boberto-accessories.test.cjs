const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const accessories = ['caballero', 'steampunk', 'faraon', 'vaquero', 'guardian',
  'bombero', 'detective', 'mosquetero', 'bufon', 'marinero',
  'pintor', 'mariachi', 'ninja', 'gaitero', 'polar',
  'astronauta', 'buzo', 'helice', 'bufanda'];
const upgraded = ['santa', 'copa', 'chef', 'corona', 'pirata', 'vikingo', 'gladiador', 'mago', 'arquero'];

function fixture(engine, saved = {}) {
  const store = new Map(Object.entries({ smg_boberto_migrated: '1', ...saved }));
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
  vm.runInContext(fs.readFileSync(path.join(__dirname, '../../src/AseIsql.Desktop/Assets/Boberto', engine), 'utf8'), context);
  const api = window.BOBERTO;
  function render(hat, pose = 'base') {
    calls.length = 0;
    api.renderPose(element(), pose, 3, { hat, character: '' });
    assert.equal(depth, 0, 'render restores canvas state');
    return JSON.stringify(calls);
  }
  function renderAs(character, pose = 'base') {
    calls.length = 0;
    api.renderPose(element(), pose, 3, { hat: 'limpio', character });
    assert.equal(depth, 0, 'render restores canvas state');
    return JSON.stringify(calls);
  }
  return { api, store, render, renderAs, context };
}

for (const id of accessories) {
    test(`${id} is selectable, distinct and survives reload`, () => {
      const f = fixture('boberto.js');
      assert.equal(f.api.listHats().filter(hat => hat.id === id).length, 1);
      assert.notEqual(f.render(id), f.render('auriculares'), 'must not use the unknown-accessory fallback');
      f.api.setHat(id);
      assert.equal(f.api.getHat(), id);
      assert.equal(fixture('boberto.js', Object.fromEntries(f.store)).api.getHat(), id);
    });
}

test('smooth ensembles render every pose without changing the saved outfit', () => {
  const f = fixture('boberto.js');
  f.api.setHat('samurai');
  const before = JSON.stringify([...f.store]);
  for (const id of accessories) for (const pose of f.api.listPoses()) f.render(id, pose);
  assert.equal(f.api.getHat(), 'samurai');
  assert.equal(JSON.stringify([...f.store]), before);
});

test('each smooth ensemble honors both accessory colors', () => {
  for (const id of accessories) {
    const f = fixture('boberto.js');
    const natural = f.render(id);
    f.api.setHatColor('#703e91');
    const primary = f.render(id);
    assert.notEqual(primary, natural, `${id}: primary material changes`);
    f.api.setHatColor2('#24b7a3');
    assert.notEqual(f.render(id), primary, `${id}: detail material changes`);
  }
});

test('upgraded classics wear their outfit in every pose', () => {
  const f = fixture('boberto.js');
  for (const id of upgraded) {
    const base = f.render(id);
    assert.notEqual(base, f.render('limpio'), `${id}: draws something`);
    for (const pose of f.api.listPoses()) f.render(id, pose);
  }
});

test('items beside a hand step aside while a prop takes that hand', () => {
  const f = fixture('boberto.js');
  // teachPoint hands the right arm to the board pointer; laptopOpen takes both.
  const lupa = '"ellipse",15.7,8.6', escudo = '"moveTo",-0.6,8.2', arco = '"moveTo",0,-8.6';
  assert.ok(f.render('detective').includes(lupa), 'magnifier shown when the hand is free');
  assert.ok(!f.render('detective', 'teachPoint').includes(lupa), 'magnifier hidden while pointing');
  assert.ok(f.render('caballero').includes(escudo), 'shield shown at rest');
  assert.ok(!f.render('caballero', 'laptopOpen').includes(escudo), 'shield hidden while typing');
  assert.ok(f.render('caballero', 'teachPoint').includes(escudo), 'the other hand keeps its shield');
  assert.ok(f.render('arquero').includes(arco), 'bow shown at rest');
  assert.ok(!f.render('arquero', 'laptopOpen').includes(arco), 'bow and its hand hidden while typing');
});

const materials = ['bronce', 'marmol', 'vitral', 'circuito', 'ambar'];
for (const id of materials) {
  test(`${id} material is listed, persists and renders every pose`, () => {
    const f = fixture('boberto.js');
    assert.equal(f.api.listCharacters().filter(ch => ch.id === id).length, 1);
    f.api.setCharacter(id);
    assert.equal(f.api.getCharacter(), id);
    assert.equal(fixture('boberto.js', Object.fromEntries(f.store)).api.getCharacter(), id);
    for (const pose of f.api.listPoses()) f.renderAs(id, pose);
    assert.notEqual(f.renderAs(id), f.renderAs('madera'), 'has its own texture');
  });
}

test('the default accessory color is called Natural and shows the piece own color', () => {
  const f = fixture('boberto.js');
  f.api.setHat('caballero');
  const natural = f.api.listHatColors()[0];
  assert.equal(natural.name, 'Natural');
  assert.equal(natural.id, '#2a2c34', 'keeps the stored value, so saved choices stay put');
  assert.equal(natural.muestra, '#8095ad');
  f.api.setHat('auriculares');
  assert.equal(f.api.listHatColors()[0].muestra, '#2a2c34', 'pieces without their own color fall back to graphite');
  assert.ok(!f.api.listHatColors().some(c => c.name === 'Grafito'));
});

test('desktop adapter translates the expanded catalogs without changing identifiers', () => {
  const f = fixture('boberto.js');
  const catalogs = ['listHats', 'listCharacters', 'listGestures'];
  const before = Object.fromEntries(catalogs.map(method => [method, Array.from(f.api[method](), item => item.id)]));
  f.context.Node = { TEXT_NODE: 3, ELEMENT_NODE: 1 };
  vm.runInContext(fs.readFileSync(path.join(__dirname, '../../src/AseIsql.Desktop/Assets/Boberto/english.js'), 'utf8'), f.context);
  for (const method of catalogs) assert.deepEqual(Array.from(f.api[method](), item => item.id), before[method]);
  const names = new Map(catalogs.flatMap(method => Array.from(f.api[method](), item => [item.id, item.name])));
  for (const [id, name] of Object.entries({ caballero: 'Medieval knight', guardian: 'Forest guardian',
    gaitero: 'Scottish piper', polar: 'Polar expedition', bronce: 'Bronze and patina', marmol: 'Marble',
    vitral: 'Stained glass', circuito: 'Circuit', ambar: 'Amber', pesas: 'Weightlifting', magia: 'Magic',
    jardin: 'Garden', guardia: 'On call', barrilete: 'Kite' })) assert.equal(names.get(id), name, id);
});
