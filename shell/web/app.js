// Glass desktop UI. Talks to the host (Desktop3D.Shell) through WebView2 messages:
//   host -> page: config, theme, layout, status, telemetry, toast
//   page -> host: ready, launch {name}, start, search, log {text}
import { createCore } from './core3d.js';

const $ = (id) => document.getElementById(id);
const host = window.chrome && window.chrome.webview;
const send = (msg) => { if (host) host.postMessage(msg); };

addEventListener('error', (e) => send({ type: 'log', text: `${e.message} @ ${e.filename}:${e.lineno}` }));
addEventListener('unhandledrejection', (e) => send({ type: 'log', text: 'promise: ' + (e.reason && e.reason.message || e.reason) }));

const ICON = {
  eye: 'M2 12s3.5-7 10-7 10 7 10 7-3.5 7-10 7S2 12 2 12z M12 9a3 3 0 1 0 0 6a3 3 0 1 0 0-6z',
  ship: 'M12 2l7 17-7-4-7 4z M12 15v6',
  plane: 'M21 16v-2l-8-5V3.5a1.5 1.5 0 0 0-3 0V9l-8 5v2l8-2.5V19l-2 1.5V22l3.5-1 3.5 1v-1.5L13 19v-5.5z',
  planet: 'M12 6a6 6 0 1 0 0 12a6 6 0 1 0 0-12z M2.5 17C4.5 19.5 20.5 12.5 21.5 7',
  code: 'M16 18l6-6-6-6 M8 6l-6 6 6 6',
  folder: 'M3 7a2 2 0 0 1 2-2h4l2 2h8a2 2 0 0 1 2 2v8a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z',
  globe: 'M12 2a10 10 0 1 0 0 20a10 10 0 1 0 0-20z M2 12h20 M12 2c3 3 4.5 6.5 4.5 10S15 19 12 22c-3-3-4.5-6.5-4.5-10S9 5 12 2z',
  sliders: 'M4 6h9 M17 6h3 M15 4v4 M4 12h3 M11 12h9 M9 10v4 M4 18h11 M19 18h1 M17 16v4',
  terminal: 'M4 17l6-5-6-5 M12 19h8',
  play: 'M3 7a3 3 0 0 1 3-3h12a3 3 0 0 1 3 3v10a3 3 0 0 1-3 3H6a3 3 0 0 1-3-3z M10 9l5 3-5 3z',
  jet: 'M12 2l1.4 6.2L21 13v2l-7.4-2.1-.6 5.1 3 2.2V22L12 20.6 8 22v-1.8l3-2.2-.6-5.1L3 15v-2l7.6-4.8z',
  vr: 'M3 9a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2v6a2 2 0 0 1-2 2h-4.2l-1.8-2.6h-2l-1.8 2.6H5a2 2 0 0 1-2-2z M7.5 12h1 M15.5 12h1',
  game: 'M6 8h12a4 4 0 0 1 4 4v2a3 3 0 0 1-5.4 1.8L15 14H9l-1.6 1.8A3 3 0 0 1 2 14v-2a4 4 0 0 1 4-4z M7 10.5v3 M5.5 12h3 M15.5 11h.01 M17.5 13h.01',
  app: 'M4 4h7v7H4z M13 4h7v7h-7z M4 13h7v7H4z M13 13h7v7h-7z',
};
const ICON_CHOICES = ['app', 'game', 'jet', 'plane', 'ship', 'planet', 'vr', 'globe', 'play', 'code', 'terminal', 'folder', 'sliders', 'eye'];

function guessIcon(name, id) {
  const s = `${name} ${id}`.toLowerCase();
  if (/pimax|steamvr|oculus|quest|openxr|virtual desktop|\bvr\b|mixed reality/.test(s)) return 'vr';
  if (/\bdcs\b|falcon|il-2|war thunder|ace combat|vtol/.test(s)) return 'jet';
  if (/flight|limitless|x-plane|xplane|aerofly|msfs/.test(s)) return 'plane';
  if (/elite|star citizen|no man|edcopilot/.test(s)) return 'ship';
  if (/starfield|space|planet|kerbal/.test(s)) return 'planet';
  if (/chrome|edge|firefox|browser|opera|brave/.test(s)) return 'globe';
  if (/youtube|vlc|video|media|netflix|plex|spotify|music|player/.test(s)) return 'play';
  if (/code|studio|notepad|python|git|cursor/.test(s)) return 'code';
  if (/terminal|powershell|command prompt/.test(s)) return 'terminal';
  if (/explorer|files|folder/.test(s)) return 'folder';
  if (/settings|control panel|afterburner|hwmonitor|monitor|tweak|config|tool/.test(s)) return 'sliders';
  if (/steam|game|launcher|epic|ubisoft|ea app|gog|xbox/.test(s)) return 'game';
  return 'app';
}
const svg = (name) => `<svg viewBox="0 0 24 24" aria-hidden="true"><path d="${ICON[name] || ICON.app}"></path></svg>`;

const state = {
  apps: [],
  shell: { clock_24h: false, decks: ['Start', 'Games', 'Dev', 'Tools'], start_deck: 'Start' },
  deck: null,
  layout: null,
  W: 1920,
  z: 1,
};

let core;
try { core = createCore($('gl')); }
catch (err) {
  send({ type: 'log', text: '3D core unavailable: ' + err.message });
  core = { setCore() {}, setColors() {}, setActivity() {}, setBrightness() {}, setIdle() {}, setThrottled() {}, pointer() {}, pulse() {} };
}

// ---------- layout: the 1920x1080 mockup scaled to the work area (taskbar excluded) ----------
function applyLayout() {
  let wx = 0, wy = 0, ww = innerWidth, wh = innerHeight;
  const m = state.layout;
  if (m && m.win && m.win.w > 0) {
    const r = innerWidth / m.win.w; // CSS px per physical px
    wx = m.work.x * r; wy = m.work.y * r; ww = m.work.w * r; wh = m.work.h * r;
  }
  const z = wh / 1080;
  const W = ww / z;
  Object.assign($('frame').style, { left: wx + 'px', top: wy + 'px', width: ww + 'px', height: wh + 'px' });
  const stage = $('stage');
  stage.style.zoom = z;
  stage.style.width = W + 'px';
  state.z = z; state.W = W;

  const cx = wx + (W / 2) * z, cy = wy + 440 * z;
  const root = document.documentElement.style;
  root.setProperty('--core-x', cx + 'px');
  root.setProperty('--core-y', cy + 'px');
  root.setProperty('--grid', 64 * z + 'px');
  core.setCore(cx, cy, z);
  renderNodes(false);
}
addEventListener('resize', applyLayout);

// ---------- app nodes on the arc ----------
const MAX_APPS_PER_DECK = 18;
const decksOf = (a) => a.decks || (a.deck ? [a.deck] : []);

const ICON_SIZES = [
  { key: 'small', label: 'Small', tile: 48 },
  { key: 'medium', label: 'Medium', tile: 66 },
];

function currentIconSize() {
  const k = state.shell.icon_size === 'large' ? 'medium' : (state.shell.icon_size || 'small');
  return ICON_SIZES.find((s) => s.key === k) || ICON_SIZES[0];
}

function applyIconSize(key, redraw = false) {
  const mapped = key === 'large' ? 'medium' : key;
  const item = ICON_SIZES.find((s) => s.key === mapped) || ICON_SIZES[0];
  state.shell.icon_size = item.key;
  document.body.dataset.iconSize = item.key;
  if (redraw) renderNodes(false);
}

function computeArcSlots(n, cx) {
  const maxOuterRx = Math.min(590, Math.max(430, cx - 460 - 56));
  if (n <= 10) {
    const rx = Math.min(maxOuterRx, n > 8 ? 520 + (n - 8) * 24 : 520);
    const ry = n > 8 ? 300 + (n - 8) * 14 : 300;
    return Array.from({ length: n }, (_, i) => ({
      deg: n === 1 ? 90 : 200 - i * (220 / (n - 1)),
      rx,
      ry,
    }));
  }
  const outerCount = Math.ceil((n + 1) / 2);
  const innerCount = n - outerCount;
  const rxOuter = maxOuterRx, ryOuter = 348;
  const rxInner = Math.max(305, rxOuter - 175), ryInner = 236;
  const step = 224 / (outerCount - 1);
  const gapOffset = outerCount - 1 > innerCount ? 0.5 : 0;
  const slots = [];
  for (let j = 0; j < outerCount; j++) slots.push({ deg: 202 - j * step, rx: rxOuter, ry: ryOuter });
  for (let k = 0; k < innerCount; k++) slots.push({ deg: 202 - (k + 0.5 + gapOffset) * step, rx: rxInner, ry: ryInner });
  return slots;
}

function renderNodes(animate) {
  const box = $('nodes');
  box.textContent = '';
  const list = state.apps.filter((a) => decksOf(a).includes(state.deck)).slice(0, MAX_APPS_PER_DECK);
  const n = list.length;
  const cx = state.W / 2, cy = 440;
  const slots = computeArcSlots(n, cx);
  const lineGap = currentIconSize().tile / 2 + 18;
  let d = '';
  list.forEach((a, i) => {
    const { deg, rx, ry } = slots[i];
    const rad = (deg * Math.PI) / 180;
    const x = cx + rx * Math.cos(rad), y = cy - ry * Math.sin(rad);
    const dx = x - cx, dy = y - cy, dist = Math.hypot(dx, dy), ux = dx / dist, uy = dy / dist;
    const end = Math.max(212, dist - lineGap);
    d += `M${(cx + ux * 206).toFixed(1)} ${(cy + uy * 206).toFixed(1)} L${(cx + ux * end).toFixed(1)} ${(cy + uy * end).toFixed(1)} `;

    const b = document.createElement('button');
    b.type = 'button';
    b.className = 'node' + (a.ready ? '' : ' not-ready');
    b.style.left = x + 'px';
    b.style.top = y + 'px';
    // Curve tiles subtly inward around the 3D centerpiece.
    const arcRy = (-Math.cos(rad) * 6).toFixed(1) + 'deg';
    b.style.setProperty('--arc-ry', arcRy);
    b.title = a.ready ? `Open ${a.name}` : `${a.name}: ${a.note || 'not available'}`;
    b.innerHTML = `<span class="tile">${svg(a.icon)}</span><span class="label"></span>`;
    b.querySelector('.label').textContent = a.name;
    const tile = b.querySelector('.tile');
    b.addEventListener('pointermove', (e) => {
      const r = tile.getBoundingClientRect();
      if (!r.width || !r.height) return;
      const px = Math.max(0, Math.min(1, (e.clientX - r.left) / r.width));
      const py = Math.max(0, Math.min(1, (e.clientY - r.top) / r.height));
      tile.style.setProperty('--rx', ((0.5 - py) * 14).toFixed(1) + 'deg');
      tile.style.setProperty('--ry', ((px - 0.5) * 14).toFixed(1) + 'deg');
      tile.style.setProperty('--gx', (px * 100).toFixed(0) + '%');
      tile.style.setProperty('--gy', (py * 100).toFixed(0) + '%');
    });
    b.addEventListener('pointerleave', () => {
      tile.style.removeProperty('--rx');
      tile.style.removeProperty('--ry');
      tile.style.removeProperty('--gx');
      tile.style.removeProperty('--gy');
    });
    b.addEventListener('click', () => launch(a));
    b.addEventListener('contextmenu', (e) => {
      e.preventDefault();
      openTileMenu(x + 36, y - 36, [{ text: `Remove from ${state.deck} deck`, act: () => send({ type: 'remove_app', name: a.name, deck: state.deck }) }]);
    });
    box.appendChild(b);
    if (animate) setTimeout(() => b.classList.add('in'), 40 + i * 45);
    else b.classList.add('in');
  });
  $('linkPath').setAttribute('d', d);
  updateCoreName();
}

// The centrepiece's name: shell.json "core_label" if set, otherwise the active deck.
function updateCoreName() {
  const label = (state.shell.core_label || state.deck || '').toString().trim();
  $('coreName').textContent = label.toUpperCase();
}

const lastLaunched = new Map();
function launch(a) {
  if (!a.ready) { toast(`${a.name}: ${a.note || 'not available yet'}`); return; }
  const now = performance.now();
  if (now - (lastLaunched.get(a.name) || 0) < 1500) return; // guard against double-click / double-pinch
  lastLaunched.set(a.name, now);
  core.pulse();
  toast(a.heavy ? `Starting ${a.name}. The 3D desktop steps aside while you play.` : `Opening ${a.name}…`);
  send({ type: 'launch', name: a.name });
}

// ---------- decks ----------
const MAX_DECKS = 8;
let pendingDeckSelect = null;

function renderDecks() {
  const row = $('deckRow');
  row.textContent = '';
  row.style.setProperty('--cols', String(Math.min(4, Math.max(1, state.shell.decks.length))));
  for (const name of state.shell.decks) {
    const b = document.createElement('button');
    b.type = 'button';
    b.textContent = name;
    b.title = `${name} deck (right-click to rename or remove)`;
    b.setAttribute('aria-pressed', String(name === state.deck));
    b.addEventListener('click', () => setDeck(name));
    b.addEventListener('contextmenu', (e) => {
      e.preventDefault();
      const r = b.getBoundingClientRect(), s = $('stage').getBoundingClientRect();
      const items = [{ text: `Rename "${name}"…`, act: () => openDeckModal('rename', name) }];
      if (state.shell.decks.length > 1) {
        items.push({ text: `Remove "${name}" deck`, act: () => send({ type: 'remove_deck', name }) });
      }
      openTileMenu((r.left - s.left) / state.z, (r.top - s.top) / state.z - 88, items);
    });
    row.appendChild(b);
  }
  const btn = $('addDeckBtn');
  if (btn) btn.title = state.shell.decks.length >= MAX_DECKS ? `Maximum ${MAX_DECKS} decks` : 'Create a new deck';
}

function setDeck(name) {
  if (name === state.deck) return;
  state.deck = name;
  renderDecks();
  renderNodes(true);
}

// ---------- "+ DECK" / rename deck dialog ----------
const deckDlg = { mode: 'add', oldName: '' };

function openDeckModal(mode, oldName = '') {
  closeTileMenu();
  if (mode === 'add' && state.shell.decks.length >= MAX_DECKS) {
    toast(`Maximum of ${MAX_DECKS} decks reached (right-click a deck to rename or remove one).`);
    return;
  }
  deckDlg.mode = mode;
  deckDlg.oldName = oldName;
  $('deckTitle').textContent = mode === 'rename' ? `Rename "${oldName}"` : 'Add a deck';
  $('deckConfirm').textContent = mode === 'rename' ? 'Save name' : 'Add deck';
  $('deckHint').textContent = `${state.shell.decks.length} / ${MAX_DECKS} DECKS`;
  $('deckName').value = mode === 'rename' ? oldName : '';
  $('deckConfirm').disabled = !$('deckName').value.trim();
  $('deckScrim').hidden = false;
  $('deckModal').hidden = false;
  send({ type: 'focus_input', id: 'deckName' });
  setTimeout(() => { $('deckName').focus(); $('deckName').select(); }, 60);
}

function closeDeckModal() {
  $('deckScrim').hidden = true;
  $('deckModal').hidden = true;
}

function confirmDeckModal() {
  const name = $('deckName').value.trim();
  if (!name) return;
  if (deckDlg.mode === 'rename') {
    if (name === deckDlg.oldName) { closeDeckModal(); return; }
    send({ type: 'rename_deck', oldName: deckDlg.oldName, newName: name });
  } else {
    send({ type: 'add_deck', name });
  }
  closeDeckModal();
}

$('addDeckBtn').addEventListener('click', () => openDeckModal('add'));
$('deckClose').addEventListener('click', closeDeckModal);
$('deckCancel').addEventListener('click', closeDeckModal);
$('deckScrim').addEventListener('click', closeDeckModal);
$('deckConfirm').addEventListener('click', confirmDeckModal);
$('deckName').addEventListener('input', () => { $('deckConfirm').disabled = !$('deckName').value.trim(); });
$('deckName').addEventListener('pointerdown', () => send({ type: 'focus_input', id: 'deckName' }));
$('deckName').addEventListener('keydown', (e) => {
  if (e.key === 'Enter') confirmDeckModal();
});

// ---------- dock ----------
function renderPins() {
  const box = $('pins');
  box.textContent = '';
  for (const a of state.apps.filter((x) => x.pinned)) {
    const b = document.createElement('button');
    b.type = 'button';
    b.className = 'pin' + (a.ready ? '' : ' not-ready');
    b.setAttribute('aria-label', a.name);
    b.title = a.ready ? a.name : `${a.name}: ${a.note || 'not available'}`;
    b.innerHTML = svg(a.icon);
    b.addEventListener('click', () => launch(a));
    b.addEventListener('contextmenu', (e) => {
      e.preventDefault();
      const r = b.getBoundingClientRect(), s = $('stage').getBoundingClientRect();
      openTileMenu((r.left - s.left) / state.z, (r.top - s.top) / state.z - 64, [{ text: `Unpin ${a.name} from the dock`, act: () => send({ type: 'unpin_app', name: a.name }) }]);
    });
    box.appendChild(b);
  }
}

// ---------- right-click menu for tiles and dock pins ----------
function openTileMenu(x, y, items) {
  const m = $('tileMenu');
  m.textContent = '';
  for (const it of items) {
    const b = document.createElement('button');
    b.type = 'button';
    b.setAttribute('role', 'menuitem');
    b.textContent = it.text;
    b.addEventListener('click', () => { closeTileMenu(); it.act(); });
    m.append(b);
  }
  const clampedX = Math.max(16, Math.min(state.W - 260, x));
  const clampedY = Math.max(16, Math.min(1080 - 72, y));
  m.style.left = clampedX + 'px';
  m.style.top = clampedY + 'px';
  m.hidden = false;
}
function closeTileMenu() { $('tileMenu').hidden = true; }
document.addEventListener('pointerdown', (e) => {
  if (!$('tileMenu').hidden && !e.target.closest('#tileMenu')) closeTileMenu();
  if (!e.target.closest('input')) send({ type: 'desktop_focus' });
});

// ---------- "+ ADD APP": pick from Windows' All-apps list ----------
const add = { items: [], selected: null, icon: 'app', decks: new Set() };

function countAppsOnDeck(deckName) {
  return state.apps.filter((a) => decksOf(a).some((d) => d.toLowerCase() === deckName.toLowerCase())).length;
}

function openAdd() {
  closeTileMenu();
  add.selected = null;
  add.icon = 'app';
  const startFree = countAppsOnDeck(state.deck) < MAX_APPS_PER_DECK
    ? state.deck
    : state.shell.decks.find((d) => countAppsOnDeck(d) < MAX_APPS_PER_DECK);
  add.decks = startFree ? new Set([startFree]) : new Set();
  $('addSearch').value = '';
  $('addName').value = '';
  $('addPin').checked = false;
  $('addConfirm').disabled = true;
  $('addList').innerHTML = '<div class="list-msg">Loading your apps…</div>';
  renderAddOptions();
  $('addScrim').hidden = false;
  $('addModal').hidden = false;
  send({ type: 'list_start_apps' });
  send({ type: 'focus_input', id: 'addSearch' });
  setTimeout(() => $('addSearch').focus(), 60);
}

function closeAdd() {
  $('addScrim').hidden = true;
  $('addModal').hidden = true;
}

function renderAddList() {
  const box = $('addList');
  const q = $('addSearch').value.trim().toLowerCase();
  const list = add.items.filter((i) => !q || i.name.toLowerCase().includes(q));
  box.textContent = '';
  if (!list.length) {
    box.innerHTML = `<div class="list-msg">${add.items.length ? 'No apps match that.' : 'No apps found.'}</div>`;
    return;
  }
  for (const it of list) {
    const b = document.createElement('button');
    b.type = 'button';
    b.className = 'app-row';
    b.dataset.appId = it.id;
    b.setAttribute('role', 'option');
    b.setAttribute('aria-selected', String(add.selected === it));
    const n = document.createElement('span');
    n.textContent = it.name;
    const k = document.createElement('span');
    k.className = 'kind';
    k.textContent = it.kind;
    b.append(n, k);
    b.addEventListener('click', () => selectApp(it));
    b.addEventListener('dblclick', () => { selectApp(it); confirmAdd(); });
    box.append(b);
  }
}

function selectApp(it) {
  add.selected = it;
  add.icon = guessIcon(it.name, it.id);
  $('addName').value = it.name.length > 40 ? it.name.slice(0, 40) : it.name;
  $('addConfirm').disabled = false;
  // Toggle aria-selected in place so the list doesn't rebuild and jump scroll position.
  for (const row of $('addList').children) {
    if (row.dataset) row.setAttribute('aria-selected', String(row.dataset.appId === it.id));
  }
  renderAddOptions();
}

function renderAddOptions() {
  $('addOptions').classList.toggle('dim', !add.selected);
  const decks = $('addDecks');
  decks.textContent = '';
  for (const d of state.shell.decks) {
    const count = countAppsOnDeck(d);
    const existingApp = add.selected && state.apps.find((a) => a.name.toLowerCase() === add.selected.name.toLowerCase());
    const alreadyOn = existingApp ? decksOf(existingApp).some((x) => x.toLowerCase() === d.toLowerCase()) : false;
    const full = count >= MAX_APPS_PER_DECK && !alreadyOn;
    if (full && add.decks.has(d)) add.decks.delete(d);
    const c = document.createElement('button');
    c.type = 'button';
    c.className = 'chip';
    c.textContent = full ? `${d} (${MAX_APPS_PER_DECK}/${MAX_APPS_PER_DECK})` : d;
    c.disabled = full;
    c.title = full ? `${d} deck is full (${MAX_APPS_PER_DECK} apps max) — pick another deck or click + DECK` : `${count}/${MAX_APPS_PER_DECK} apps on ${d}`;
    c.setAttribute('aria-pressed', String(add.decks.has(d)));
    c.addEventListener('click', () => {
      if (full) return;
      if (add.decks.has(d)) add.decks.delete(d); else add.decks.add(d);
      renderAddOptions();
    });
    decks.append(c);
  }
  const icons = $('addIcons');
  icons.textContent = '';
  for (const name of ICON_CHOICES) {
    const b = document.createElement('button');
    b.type = 'button';
    b.className = 'icon-choice';
    b.setAttribute('role', 'radio');
    b.setAttribute('aria-checked', String(add.icon === name));
    b.setAttribute('aria-label', name);
    b.innerHTML = svg(name);
    b.addEventListener('click', () => { add.icon = name; renderAddOptions(); });
    icons.append(b);
  }
  $('addConfirm').disabled = !add.selected || add.decks.size === 0;
}

function confirmAdd() {
  if (!add.selected || add.decks.size === 0) return;
  send({
    type: 'add_app',
    id: add.selected.id,
    name: $('addName').value.trim() || add.selected.name,
    icon: add.icon,
    decks: [...add.decks],
    pinned: $('addPin').checked,
  });
  closeAdd();
}

$('addAppBtn').addEventListener('click', openAdd);
$('addClose').addEventListener('click', closeAdd);
$('addCancel').addEventListener('click', closeAdd);
$('addScrim').addEventListener('click', closeAdd);
$('addConfirm').addEventListener('click', confirmAdd);
$('addSearch').addEventListener('input', renderAddList);
$('addSearch').addEventListener('search', renderAddList);
$('addSearch').addEventListener('pointerdown', () => send({ type: 'focus_input', id: 'addSearch' }));
$('addName').addEventListener('pointerdown', () => send({ type: 'focus_input', id: 'addName' }));
$('addSearch').addEventListener('keydown', (e) => {
  if (e.key === 'Enter') {
    const q = $('addSearch').value.trim().toLowerCase();
    const first = add.items.find((i) => !q || i.name.toLowerCase().includes(q));
    if (first) selectApp(first);
  }
});
$('gameModeBtn').addEventListener('click', () => {
  toast('Switching to Game Mode… (Ctrl+Alt+G or shortcut brings the 3D desktop back)');
  send({ type: 'enter_game_mode' });
});
$('stockWinBtn').addEventListener('click', () => {
  toast('Switching to stock Windows…');
  send({ type: 'restore_stock_windows' });
});

// ---------- Windows power menu (Lock, Sleep, Restart, Shut down with 2-click safety) ----------
let powerArmed = null;
const POWER_ITEMS = [
  { key: 'lock', label: 'Lock PC', needsConfirm: false, danger: false },
  { key: 'sleep', label: 'Sleep', needsConfirm: false, danger: false },
  { key: 'restart', label: 'Restart Windows', needsConfirm: true, danger: true },
  { key: 'shutdown', label: 'Shut Down Windows', needsConfirm: true, danger: true },
];

function renderPowerMenu() {
  const menu = $('powerMenu');
  menu.textContent = '';
  for (const it of POWER_ITEMS) {
    const b = document.createElement('button');
    b.type = 'button';
    b.setAttribute('role', 'menuitem');
    const armed = powerArmed === it.key;
    b.className = 'power-opt' + (it.danger ? ' danger' : '') + (armed ? ' confirm' : '');
    b.textContent = armed ? `Confirm ${it.label}?` : it.label;
    b.addEventListener('click', () => {
      if (it.needsConfirm && !armed) {
        powerArmed = it.key;
        renderPowerMenu();
        return;
      }
      closePowerMenu();
      send({ type: 'power_action', action: it.key });
    });
    menu.append(b);
  }
}

function openPowerMenu() {
  closeThemeMenu();
  closeTileMenu();
  powerArmed = null;
  renderPowerMenu();
  $('powerMenu').hidden = false;
  $('powerBtn').setAttribute('aria-expanded', 'true');
}

function closePowerMenu() {
  powerArmed = null;
  $('powerMenu').hidden = true;
  $('powerBtn').setAttribute('aria-expanded', 'false');
}

$('powerBtn').addEventListener('click', () => ($('powerMenu').hidden ? openPowerMenu() : closePowerMenu()));
document.addEventListener('pointerdown', (e) => {
  if (!$('powerMenu').hidden && !e.target.closest('.power-wrap')) closePowerMenu();
});

// ---------- clock + calendar ----------
const cal = { year: new Date().getFullYear(), month: new Date().getMonth(), todayKey: '' };

function renderCalendar() {
  const now = new Date();
  const y = cal.year, m = cal.month;
  const label = new Date(y, m, 1).toLocaleDateString('en-US', { month: 'long', year: 'numeric' }).toUpperCase();
  $('calMonth').textContent = label;
  const grid = $('calDays');
  grid.textContent = '';

  const firstDow = new Date(y, m, 1).getDay();
  const daysInMonth = new Date(y, m + 1, 0).getDate();
  const prevDays = new Date(y, m, 0).getDate();
  const totalCells = firstDow + daysInMonth <= 35 ? 35 : 42;
  const isThisMonth = y === now.getFullYear() && m === now.getMonth();

  for (let i = 0; i < totalCells; i++) {
    const cell = document.createElement('span');
    cell.className = 'cal-day';
    if (i < firstDow) {
      cell.classList.add('other');
      cell.textContent = String(prevDays - firstDow + i + 1);
    } else if (i >= firstDow + daysInMonth) {
      cell.classList.add('other');
      cell.textContent = String(i - (firstDow + daysInMonth) + 1);
    } else {
      const day = i - firstDow + 1;
      cell.textContent = String(day);
      if (isThisMonth && day === now.getDate()) cell.classList.add('today');
    }
    grid.appendChild(cell);
  }
}

$('calPrev').addEventListener('click', () => {
  cal.month--;
  if (cal.month < 0) { cal.month = 11; cal.year--; }
  renderCalendar();
});
$('calNext').addEventListener('click', () => {
  cal.month++;
  if (cal.month > 11) { cal.month = 0; cal.year++; }
  renderCalendar();
});
$('calToday').addEventListener('click', () => {
  const now = new Date();
  cal.year = now.getFullYear();
  cal.month = now.getMonth();
  renderCalendar();
});

function tickClock() {
  const now = new Date();
  const h24 = !!state.shell.clock_24h;
  let h = now.getHours();
  const m = String(now.getMinutes()).padStart(2, '0');
  let ampm = '';
  if (!h24) { ampm = h >= 12 ? 'PM' : 'AM'; h = h % 12 || 12; }
  const hh = h24 ? String(h).padStart(2, '0') : String(h);
  $('time').textContent = `${hh}:${m}`;
  $('ampm').textContent = ampm;
  const weekday = now.toLocaleDateString('en-US', { weekday: 'short' }).toUpperCase();
  const month = now.toLocaleDateString('en-US', { month: 'short' }).toUpperCase();
  $('date').textContent = `${weekday} · ${now.getDate()} ${month} ${now.getFullYear()}`;
  $('dockTime').textContent = `${hh}:${m}${ampm ? ' ' + ampm : ''}`;

  const key = `${now.getFullYear()}-${now.getMonth()}-${now.getDate()}`;
  if (key !== cal.todayKey) {
    cal.todayKey = key;
    cal.year = now.getFullYear();
    cal.month = now.getMonth();
    renderCalendar();
  }
}
tickClock();
setTimeout(() => { tickClock(); setInterval(tickClock, 1000); }, 1000 - (Date.now() % 1000));

// ---------- telemetry (universal Windows counters: CPU, RAM, live drives) ----------
const pct = (v) => (v == null ? null : Math.max(0, Math.min(100, Math.round(v))));
let activeMeters = [];
let meterEls = [];

function buildMeters(hybrid) {
  const specs = hybrid
    ? [
        { label: 'P-cores', val: (t) => t.p, sub: (t) => `${t.pCount} THREADS · ${pct(t.p)}%` },
        { label: 'E-cores', val: (t) => t.e, sub: (t) => `${t.eCount} CORES · ${pct(t.e)}%` },
        { label: 'RAM', val: (t) => (t.ramTotal ? (t.ramUsed / t.ramTotal) * 100 : null), sub: (t) => (t.ramTotal ? `${t.ramUsed.toFixed(1)} / ${Math.round(t.ramTotal)} GB` : '—') },
      ]
    : [
        { label: 'CPU', val: (t) => t.cpu, sub: (t) => (t.cpu == null ? '—' : `${t.threads || ''} THREADS · ${pct(t.cpu)}%`.trim()) },
        { label: 'RAM', val: (t) => (t.ramTotal ? (t.ramUsed / t.ramTotal) * 100 : null), sub: (t) => (t.ramTotal ? `${t.ramUsed.toFixed(1)} / ${Math.round(t.ramTotal)} GB` : '—') },
      ];
  activeMeters = specs;
  const box = $('meters');
  box.textContent = '';
  meterEls = specs.map((m) => {
    const el = document.createElement('div');
    el.className = 'meter';
    el.innerHTML = `<div class="meter-top"><span></span><span class="meter-sub">—</span></div><div class="bar"><i></i></div>`;
    el.querySelector('span').textContent = m.label;
    box.appendChild(el);
    return { sub: el.querySelector('.meter-sub'), bar: el.querySelector('.bar > i') };
  });
}
buildMeters(false);

function formatSpace(gb) {
  if (gb >= 1000) return (gb / 1024).toFixed(1) + ' TB';
  if (gb >= 10) return Math.round(gb) + ' GB';
  return gb.toFixed(1) + ' GB';
}

let lastDrivesSig = '';
function renderDrives(list) {
  const drives = Array.isArray(list) ? list : [];
  const sig = JSON.stringify(drives);
  if (sig === lastDrivesSig) return;
  lastDrivesSig = sig;
  const wrap = $('drivesWrap');
  const grid = $('drives');
  grid.textContent = '';
  wrap.hidden = drives.length === 0;
  for (const d of drives) {
    const item = document.createElement('div');
    item.className = 'drive-item';
    item.title = `${d.name} — ${formatSpace(d.freeGb)} free of ${formatSpace(d.totalGb)}`;
    const top = document.createElement('div');
    top.className = 'drive-top';
    const nm = document.createElement('span');
    nm.className = 'mono drive-name';
    nm.textContent = d.name;
    const fr = document.createElement('span');
    fr.className = 'mono drive-free';
    fr.textContent = formatSpace(d.freeGb);
    top.append(nm, fr);
    const bar = document.createElement('div');
    bar.className = 'bar';
    const fill = document.createElement('i');
    fill.style.width = `${Math.max(0, Math.min(100, d.usedPct || 0))}%`;
    bar.append(fill);
    item.append(top, bar);
    grid.append(item);
  }
}

let lastTelemetry = 0;
let lastHybrid = null;
function showTelemetry(t) {
  lastTelemetry = performance.now();
  $('liveTag').textContent = 'LIVE';
  $('liveTag').classList.remove('stale');
  const hybrid = (t.eCount || 0) > 0;
  if (hybrid !== lastHybrid) {
    lastHybrid = hybrid;
    buildMeters(hybrid);
  }
  activeMeters.forEach((m, i) => {
    const v = pct(m.val(t));
    meterEls[i].sub.textContent = m.sub(t);
    meterEls[i].bar.style.width = (v == null ? 0 : v) + '%';
  });
  renderDrives(t.drives);
  // The centrepiece: live CPU & RAM load under its name, and its rings turn faster when the PC is busy.
  const cpu = t.cpu != null ? pct(t.cpu) : Math.round(t.p || 0);
  const ramPct = t.ramTotal ? pct((t.ramUsed / t.ramTotal) * 100) : null;
  $('coreState').textContent = ramPct == null ? `CPU ${cpu}%` : `CPU ${cpu}% · RAM ${ramPct}%`;
  core.setActivity((cpu || 0) / 100);
}
setInterval(() => {
  if (performance.now() - lastTelemetry > 5000) {
    $('liveTag').textContent = host ? 'WAITING' : 'NO HOST';
    $('liveTag').classList.add('stale');
  }
}, 2000);

// ---------- status ----------
function showStatus() { }

// ---------- theme & live color slider ----------
let baseTheme = {};

function hslToRgb(h, s, l) {
  h = ((h % 360) + 360) % 360;
  s = Math.max(0, Math.min(100, s)) / 100;
  l = Math.max(0, Math.min(100, l)) / 100;
  const c = (1 - Math.abs(2 * l - 1)) * s;
  const x = c * (1 - Math.abs(((h / 60) % 2) - 1));
  const m = l - c / 2;
  let r = 0, g = 0, b = 0;
  if (h < 60) [r, g, b] = [c, x, 0];
  else if (h < 120) [r, g, b] = [x, c, 0];
  else if (h < 180) [r, g, b] = [0, c, x];
  else if (h < 240) [r, g, b] = [0, x, c];
  else if (h < 300) [r, g, b] = [x, 0, c];
  else [r, g, b] = [c, 0, x];
  return [Math.round((r + m) * 255), Math.round((g + m) * 255), Math.round((b + m) * 255)];
}
const rgbHex = ([r, g, b]) => '#' + [r, g, b].map((v) => v.toString(16).padStart(2, '0')).join('');
const rgbaCss = ([r, g, b], a) => `rgba(${r},${g},${b},${a})`;

function colorNameFor(hue, sat) {
  if (sat < 14) return 'Silver';
  const h = ((hue % 360) + 360) % 360;
  if (h < 14 || h >= 345) return 'Crimson';
  if (h < 34) return 'Orange';
  if (h < 54) return 'Amber';
  if (h < 72) return 'Gold';
  if (h < 110) return 'Lime';
  if (h < 158) return 'Emerald';
  if (h < 185) return 'Teal';
  if (h < 212) return 'Cyan';
  if (h < 236) return 'Azure';
  if (h < 262) return 'Cobalt';
  if (h < 292) return 'Violet';
  if (h < 322) return 'Magenta';
  return 'Rose';
}

function buildCustomTheme(base, hue, sat) {
  const accentRgb = hslToRgb(hue, sat, sat < 15 ? 84 : 66);
  const bgGlowRgb = hslToRgb(hue, Math.min(95, sat), sat < 15 ? 78 : 50);
  const glassRgb = hslToRgb(hue, Math.round(sat * 0.78), 68);
  const borderRgb = hslToRgb(hue, Math.round(sat * 0.85), 76);
  const bg1 = rgbHex(hslToRgb(hue, Math.round(sat * 0.68), sat < 15 ? 17 : 14));
  const bg2 = rgbHex(hslToRgb(hue, Math.round(sat * 0.65), sat < 15 ? 8 : 7));
  const bg3 = rgbHex(hslToRgb(hue, Math.round(sat * 0.55), 3));
  return Object.assign({}, base, {
    label: colorNameFor(hue, sat),
    bg_gradient: [bg1, bg2, bg3],
    bg_glow: rgbaCss(bgGlowRgb, sat < 15 ? 0.12 : 0.28),
    text: rgbHex(hslToRgb(hue, Math.min(sat, 48), 95)),
    muted: rgbHex(hslToRgb(hue, Math.min(sat, 34), 68)),
    accent: rgbHex(accentRgb),
    accent_soft: rgbaCss(accentRgb, 0.16),
    glass_fill: rgbaCss(glassRgb, sat < 15 ? 0.06 : 0.08),
    glass_fill_strong: rgbaCss(glassRgb, sat < 15 ? 0.12 : 0.16),
    glass_border: rgbaCss(borderRgb, sat < 15 ? 0.20 : 0.26),
    glow: `0 0 24px ${rgbaCss(accentRgb, sat < 15 ? 0.30 : 0.46)}`,
    orb_core: rgbHex(hslToRgb(hue, Math.min(sat, 75), 94)),
  });
}

function applyTheme(t, labelOverride) {
  const s = document.documentElement.style;
  const set = (k, v) => { if (v) s.setProperty(k, v); };
  const bg = t.bg_gradient || [];
  set('--bg1', bg[0]); set('--bg2', bg[1]); set('--bg3', bg[2]);
  set('--bg-glow', t.bg_glow); set('--text', t.text); set('--muted', t.muted);
  set('--accent', t.accent); set('--accent-soft', t.accent_soft);
  set('--glass', t.glass_fill); set('--glass-strong', t.glass_fill_strong);
  set('--border', t.glass_border); set('--glow', t.glow);
  const lbl = labelOverride != null ? String(labelOverride).trim() : '';
  $('themeLabel').textContent = lbl ? 'THEME · ' + lbl.toUpperCase() : 'THEME';
  core.setColors(t.accent, t.orb_core || t.text);
}

function applyEffectiveTheme() {
  const tc = state.shell.theme_color;
  if (tc && typeof tc.hue === 'number') {
    const sat = typeof tc.sat === 'number' ? tc.sat : 85;
    const prof = (state.shell.active_theme_profile || '').trim();
    const custom = buildCustomTheme(baseTheme, tc.hue, sat);
    applyTheme(custom, prof || custom.label);
  } else if (baseTheme && baseTheme.accent) {
    applyTheme(baseTheme, '');
  }
}

const COLOR_PRESETS = [
  { label: 'Cyan', hue: 200, sat: 95 },
  { label: 'Azure', hue: 222, sat: 92 },
  { label: 'Emerald', hue: 152, sat: 84 },
  { label: 'Amber', hue: 36, sat: 95 },
  { label: 'Crimson', hue: 352, sat: 90 },
  { label: 'Violet', hue: 274, sat: 88 },
  { label: 'Magenta', hue: 318, sat: 88 },
  { label: 'Silver', hue: 215, sat: 4 },
];

// ---------- theme picker (click "THEME" above the calendar) ----------
const themes = { list: [], active: '', previous: '' };

function currentColorValues() {
  const tc = state.shell.theme_color;
  const hue = tc && typeof tc.hue === 'number' ? tc.hue : 215;
  const sat = tc && typeof tc.sat === 'number' ? tc.sat : 4;
  return { hue, sat };
}

function renderThemeMenu() {
  const menu = $('themeMenu');
  menu.textContent = '';
  renderColorSection(menu);
  renderSavedThemesSection(menu);
  renderIconSizeSection(menu);
  renderBackgroundSection(menu);
}

function renderColorSection(menu) {
  const head = document.createElement('div');
  head.className = 'menu-head';
  head.textContent = 'THEME COLOR';
  menu.append(head);

  const tc = state.shell.theme_color;
  let { hue, sat } = currentColorValues();

  const box = document.createElement('div');
  box.className = 'color-box';

  const top = document.createElement('div');
  top.className = 'color-head';
  const meta = document.createElement('span');
  meta.className = 'color-meta';
  const chip = document.createElement('span');
  chip.className = 'color-chip';
  const nameEl = document.createElement('span');
  meta.append(chip, nameEl);

  const actions = document.createElement('span');
  actions.className = 'color-actions';

  const saveBtn = document.createElement('button');
  saveBtn.type = 'button';
  saveBtn.className = 'mono color-save';
  saveBtn.textContent = '+ SAVE';
  saveBtn.title = 'Save these colors as a reusable theme profile';
  saveBtn.addEventListener('click', () => {
    closeThemeMenu();
    openSaveThemeModal(hue, sat);
  });

  const resetBtn = document.createElement('button');
  resetBtn.type = 'button';
  resetBtn.className = 'mono color-reset';
  resetBtn.textContent = 'RESET';
  resetBtn.title = 'Return to the default neutral dark theme';
  resetBtn.hidden = !(tc && typeof tc.hue === 'number');
  resetBtn.addEventListener('click', () => {
    state.shell.theme_color = null;
    state.shell.active_theme_profile = null;
    applyEffectiveTheme();
    send({ type: 'set_theme_color', reset: true });
    renderThemeMenu();
  });
  actions.append(saveBtn, resetBtn);
  top.append(meta, actions);

  const hueRange = document.createElement('input');
  hueRange.type = 'range';
  hueRange.className = 'hue-slider';
  hueRange.min = '0';
  hueRange.max = '359';
  hueRange.step = '1';
  hueRange.value = String(hue);
  hueRange.setAttribute('aria-label', 'Theme color hue');

  const satHead = document.createElement('div');
  satHead.className = 'color-sub';
  const satLbl = document.createElement('span');
  satLbl.textContent = 'Intensity';
  const satVal = document.createElement('span');
  satVal.className = 'mono';
  satHead.append(satLbl, satVal);

  const satRange = document.createElement('input');
  satRange.type = 'range';
  satRange.className = 'sat-slider';
  satRange.min = '0';
  satRange.max = '100';
  satRange.step = '1';
  satRange.value = String(sat);
  satRange.setAttribute('aria-label', 'Theme color intensity');

  const swatches = document.createElement('div');
  swatches.className = 'color-swatches';
  for (const p of COLOR_PRESETS) {
    const dot = document.createElement('button');
    dot.type = 'button';
    dot.className = 'color-dot';
    dot.title = p.label;
    dot.setAttribute('aria-label', p.label);
    dot.style.background = rgbHex(hslToRgb(p.hue, p.sat, p.sat < 15 ? 82 : 62));
    dot.addEventListener('click', () => {
      hue = p.hue; sat = p.sat;
      hueRange.value = String(hue);
      satRange.value = String(sat);
      preview();
      send({ type: 'set_theme_color', hue, sat });
    });
    swatches.append(dot);
  }

  function syncUi() {
    const hex = rgbHex(hslToRgb(hue, sat, sat < 15 ? 84 : 66));
    chip.style.background = hex;
    nameEl.textContent = (state.shell.active_theme_profile || '').trim() || (state.shell.theme_color ? colorNameFor(hue, sat) : 'Default');
    satVal.textContent = sat + '%';
    satRange.style.background = `linear-gradient(90deg, hsl(${hue}, 0%, 72%) 0%, hsl(${hue}, 100%, 62%) 100%)`;
  }

  function preview() {
    state.shell.theme_color = { hue, sat };
    state.shell.active_theme_profile = null;
    resetBtn.hidden = false;
    syncUi();
    applyEffectiveTheme();
  }

  syncUi();
  hueRange.addEventListener('input', () => { hue = Number(hueRange.value); preview(); });
  hueRange.addEventListener('change', () => send({ type: 'set_theme_color', hue, sat }));
  satRange.addEventListener('input', () => { sat = Number(satRange.value); preview(); });
  satRange.addEventListener('change', () => send({ type: 'set_theme_color', hue, sat }));

  box.append(top, hueRange, satHead, satRange, swatches);
  menu.append(box);
}

function renderSavedThemesSection(menu) {
  const head = document.createElement('div');
  head.className = 'menu-head';
  head.textContent = 'SAVED THEMES';
  menu.append(head);

  const saved = Array.isArray(state.shell.saved_themes) ? state.shell.saved_themes : [];
  const activeProf = (state.shell.active_theme_profile || '').trim().toLowerCase();
  const isDefault = !(state.shell.theme_color && typeof state.shell.theme_color.hue === 'number');

  // Always offer Default (clean neutral dark glass) so you can return in one click.
  const defBtn = document.createElement('button');
  defBtn.type = 'button';
  defBtn.className = 'theme-opt';
  defBtn.setAttribute('role', 'menuitemradio');
  defBtn.setAttribute('aria-checked', String(isDefault));
  const defSw = document.createElement('span');
  defSw.className = 'swatch';
  defSw.style.background = '#D9DEE6';
  const defName = document.createElement('span');
  defName.className = 'name';
  defName.textContent = 'Default';
  defBtn.append(defSw, defName);
  if (isDefault) {
    const cur = document.createElement('span');
    cur.className = 'mono current';
    cur.textContent = 'ON';
    defBtn.append(cur);
  }
  defBtn.addEventListener('click', () => {
    state.shell.theme_color = null;
    state.shell.active_theme_profile = null;
    applyEffectiveTheme();
    send({ type: 'set_theme_color', reset: true });
    renderThemeMenu();
  });
  menu.append(defBtn);

  if (!saved.length) {
    const empty = document.createElement('div');
    empty.className = 'saved-empty';
    empty.textContent = 'Dial in a color above and click + SAVE to store profiles here.';
    menu.append(empty);
    return;
  }

  for (const p of saved) {
    if (!p || !p.name || typeof p.hue !== 'number') continue;
    const pSat = typeof p.sat === 'number' ? p.sat : 85;
    const row = document.createElement('div');
    row.className = 'bg-row';

    const checked = !isDefault && activeProf === String(p.name).toLowerCase();
    const b = document.createElement('button');
    b.type = 'button';
    b.className = 'theme-opt';
    b.setAttribute('role', 'menuitemradio');
    b.setAttribute('aria-checked', String(checked));
    const sw = document.createElement('span');
    sw.className = 'swatch';
    sw.style.background = rgbHex(hslToRgb(p.hue, pSat, pSat < 15 ? 82 : 64));
    const name = document.createElement('span');
    name.className = 'name';
    name.textContent = p.name;
    b.append(sw, name);
    if (checked) {
      const cur = document.createElement('span');
      cur.className = 'mono current';
      cur.textContent = 'ON';
      b.append(cur);
    }
    b.addEventListener('click', () => {
      state.shell.theme_color = { hue: p.hue, sat: pSat };
      state.shell.active_theme_profile = p.name;
      applyEffectiveTheme();
      send({ type: 'set_theme_color', hue: p.hue, sat: pSat, profile: p.name });
      renderThemeMenu();
    });

    const del = document.createElement('button');
    del.type = 'button';
    del.className = 'bg-del';
    del.title = `Delete saved theme "${p.name}"`;
    del.setAttribute('aria-label', `Delete ${p.name}`);
    del.innerHTML = '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M6 6l12 12 M18 6L6 18"></path></svg>';
    del.addEventListener('click', () => {
      state.shell.saved_themes = saved.filter((x) => x !== p);
      if (checked) state.shell.active_theme_profile = null;
      applyEffectiveTheme();
      send({ type: 'delete_theme_profile', name: p.name });
      renderThemeMenu();
    });

    row.append(b, del);
    menu.append(row);
  }
}

// ---------- "+ SAVE" theme profile dialog ----------
const saveThemeDlg = { hue: 200, sat: 95 };

function openSaveThemeModal(hue, sat) {
  saveThemeDlg.hue = hue;
  saveThemeDlg.sat = sat;
  const hex = rgbHex(hslToRgb(hue, sat, sat < 15 ? 84 : 66));
  const autoName = colorNameFor(hue, sat);
  $('saveThemeChip').style.background = hex;
  $('saveThemeDesc').textContent = `${autoName.toUpperCase()} · ${sat}%`;
  const input = $('saveThemeName');
  input.value = (state.shell.active_theme_profile || '').trim() || autoName;
  $('saveThemeConfirm').disabled = !input.value.trim();
  $('saveThemeScrim').hidden = false;
  $('saveThemeModal').hidden = false;
  send({ type: 'focus_input', id: 'saveThemeName' });
  setTimeout(() => { input.focus(); input.select(); }, 60);
}

function closeSaveThemeModal() {
  $('saveThemeScrim').hidden = true;
  $('saveThemeModal').hidden = true;
}

function confirmSaveThemeModal() {
  const name = $('saveThemeName').value.trim();
  if (!name) return;
  const { hue, sat } = saveThemeDlg;
  const list = Array.isArray(state.shell.saved_themes) ? state.shell.saved_themes.slice() : [];
  const idx = list.findIndex((p) => p && String(p.name).toLowerCase() === name.toLowerCase());
  if (idx >= 0) list[idx] = { name, hue, sat };
  else list.push({ name, hue, sat });
  state.shell.saved_themes = list;
  state.shell.theme_color = { hue, sat };
  state.shell.active_theme_profile = name;
  applyEffectiveTheme();
  send({ type: 'save_theme_profile', name, hue, sat });
  closeSaveThemeModal();
}

$('saveThemeClose').addEventListener('click', closeSaveThemeModal);
$('saveThemeCancel').addEventListener('click', closeSaveThemeModal);
$('saveThemeScrim').addEventListener('click', closeSaveThemeModal);
$('saveThemeConfirm').addEventListener('click', confirmSaveThemeModal);
$('saveThemeName').addEventListener('input', () => { $('saveThemeConfirm').disabled = !$('saveThemeName').value.trim(); });
$('saveThemeName').addEventListener('pointerdown', () => send({ type: 'focus_input', id: 'saveThemeName' }));
$('saveThemeName').addEventListener('keydown', (e) => {
  if (e.key === 'Enter') confirmSaveThemeModal();
});

function renderIconSizeSection(menu) {
  const head = document.createElement('div');
  head.className = 'menu-head';
  head.textContent = 'ICONS';
  menu.append(head);

  const box = document.createElement('div');
  box.className = 'fit-box';
  const top = document.createElement('div');
  top.className = 'fit-head';
  const lbl = document.createElement('span');
  lbl.textContent = 'Size';
  const curIdx = Math.max(0, ICON_SIZES.findIndex((s) => s.key === currentIconSize().key));
  const val = document.createElement('span');
  val.className = 'mono';
  val.textContent = ICON_SIZES[curIdx].label;
  top.append(lbl, val);

  const range = document.createElement('input');
  range.type = 'range';
  range.min = '0';
  range.max = String(ICON_SIZES.length - 1);
  range.step = '1';
  range.value = String(curIdx);
  range.setAttribute('aria-label', 'Icon size');

  const ticks = document.createElement('div');
  ticks.className = 'fit-ticks';
  const btns = ICON_SIZES.map((s, idx) => {
    const tb = document.createElement('button');
    tb.type = 'button';
    tb.textContent = s.label;
    tb.setAttribute('aria-pressed', String(idx === curIdx));
    tb.addEventListener('click', () => {
      range.value = String(idx);
      update(idx);
      send({ type: 'set_icon_size', value: s.key });
    });
    ticks.append(tb);
    return tb;
  });

  function update(idx) {
    const item = ICON_SIZES[idx] || ICON_SIZES[0];
    val.textContent = item.label;
    btns.forEach((b, i) => b.setAttribute('aria-pressed', String(i === idx)));
    applyIconSize(item.key, true);
  }

  range.addEventListener('input', () => update(Number(range.value)));
  range.addEventListener('change', () => {
    const item = ICON_SIZES[Number(range.value)] || ICON_SIZES[0];
    send({ type: 'set_icon_size', value: item.key });
  });

  box.append(top, range, ticks);
  menu.append(box);
}

// ---------- background pictures (same menu) ----------
const BG_FITS = [
  { key: 'fill', label: 'Fill' },
  { key: 'fit', label: 'Fit' },
  { key: 'stretch', label: 'Stretch' },
  { key: 'center', label: 'Center' },
];
const bgs = { items: [], active: null, dim: 0.45, fit: 'fill', shown: '' };
const mediaUrl = (file) => `https://media.desktop3d.local/${encodeURIComponent(file)}`;

function applyBgFit(fit) {
  const mode = BG_FITS.some((f) => f.key === fit) ? fit : 'fill';
  bgs.fit = mode;
  const el = $('bg');
  el.classList.remove('fit-fill', 'fit-fit', 'fit-stretch', 'fit-center');
  el.classList.add('fit-' + mode);
}

function bgOption(label, thumbStyle, checked, onPick) {
  const b = document.createElement('button');
  b.type = 'button';
  b.className = 'theme-opt';
  b.setAttribute('role', 'menuitemradio');
  b.setAttribute('aria-checked', String(checked));
  const t = document.createElement('span');
  t.className = 'thumb' + (thumbStyle ? '' : ' none');
  if (thumbStyle) t.style.backgroundImage = thumbStyle;
  const n = document.createElement('span');
  n.className = 'name';
  n.textContent = label;
  b.append(t, n);
  if (checked) {
    const cur = document.createElement('span');
    cur.className = 'mono current';
    cur.textContent = 'ON';
    b.append(cur);
  }
  b.addEventListener('click', onPick);
  return b;
}

let bgDropdownOpen = false;

function renderBackgroundSection(menu) {
  const head = document.createElement('div');
  head.className = 'menu-head';
  head.textContent = 'BACKGROUND';
  menu.append(head);

  // Compact dropdown selector row (+ inline delete for active picture)
  const selectRow = document.createElement('div');
  selectRow.className = 'bg-row';
  const selectBtn = document.createElement('button');
  selectBtn.type = 'button';
  selectBtn.className = 'bg-select-btn';
  selectBtn.setAttribute('aria-expanded', String(bgDropdownOpen));
  selectBtn.title = bgs.items.length ? `Choose from ${bgs.items.length} saved background(s)` : 'Choose background';

  const curThumb = document.createElement('span');
  curThumb.className = 'thumb' + (bgs.active ? '' : ' none');
  if (bgs.active) curThumb.style.backgroundImage = `url("${mediaUrl(bgs.active)}")`;
  const curName = document.createElement('span');
  curName.className = 'name';
  curName.textContent = bgs.active ? bgs.active.replace(/\.[^.]+$/, '') : 'Theme colours';
  const chev = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
  chev.setAttribute('viewBox', '0 0 24 24');
  chev.setAttribute('aria-hidden', 'true');
  chev.setAttribute('class', 'chev');
  chev.innerHTML = '<path d="M6 9l6 6 6-6"></path>';
  selectBtn.append(curThumb, curName, chev);

  const drop = document.createElement('div');
  drop.className = 'bg-dropdown';
  drop.hidden = !bgDropdownOpen;

  selectBtn.addEventListener('click', () => {
    bgDropdownOpen = !bgDropdownOpen;
    drop.hidden = !bgDropdownOpen;
    selectBtn.setAttribute('aria-expanded', String(bgDropdownOpen));
  });
  selectRow.append(selectBtn);

  if (bgs.active) {
    const activeFile = bgs.active;
    const delActive = document.createElement('button');
    delActive.type = 'button';
    delActive.className = 'bg-del';
    delActive.title = `Remove ${activeFile} from the list (your original picture is not touched)`;
    delActive.setAttribute('aria-label', `Remove ${activeFile}`);
    delActive.innerHTML = '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M6 6l12 12 M18 6L6 18"></path></svg>';
    delActive.addEventListener('click', () => send({ type: 'remove_background', file: activeFile }));
    selectRow.append(delActive);
  }
  menu.append(selectRow);

  drop.append(bgOption('Theme colours', '', !bgs.active, () => {
    bgDropdownOpen = false;
    send({ type: 'set_background', file: null });
  }));
  for (const f of bgs.items) {
    const row = document.createElement('div');
    row.className = 'bg-row';
    row.append(bgOption(f.replace(/\.[^.]+$/, ''), `url("${mediaUrl(f)}")`, f === bgs.active, () => {
      bgDropdownOpen = false;
      send({ type: 'set_background', file: f });
    }));
    const del = document.createElement('button');
    del.type = 'button';
    del.className = 'bg-del';
    del.title = `Remove ${f} from the list (your original picture is not touched)`;
    del.setAttribute('aria-label', `Remove ${f}`);
    del.innerHTML = '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M6 6l12 12 M18 6L6 18"></path></svg>';
    del.addEventListener('click', () => send({ type: 'remove_background', file: f }));
    row.append(del);
    drop.append(row);
  }
  menu.append(drop);

  const pick = document.createElement('button');
  pick.type = 'button';
  pick.className = 'theme-opt';
  pick.textContent = '+ Choose a picture…';
  pick.addEventListener('click', () => { closeThemeMenu(); send({ type: 'pick_background' }); });
  menu.append(pick);

  const wp = document.createElement('button');
  wp.type = 'button';
  wp.className = 'theme-opt';
  wp.textContent = '+ Use my Windows wallpaper';
  wp.addEventListener('click', () => send({ type: 'use_windows_wallpaper' }));
  menu.append(wp);

  if (bgs.active) {
    const row = document.createElement('label');
    row.className = 'dim-row';
    const lbl = document.createElement('span');
    lbl.textContent = 'Dim';
    const range = document.createElement('input');
    range.type = 'range';
    range.min = '0';
    range.max = '80';
    range.step = '5';
    range.value = String(Math.round(bgs.dim * 100));
    const val = document.createElement('span');
    val.className = 'mono';
    val.textContent = range.value + '%';
    range.addEventListener('input', () => {
      val.textContent = range.value + '%';
      document.documentElement.style.setProperty('--bg-dim', range.value + '%'); // live preview
    });
    range.addEventListener('change', () => send({ type: 'set_bg_dim', value: Number(range.value) / 100 }));
    row.append(lbl, range, val);
    menu.append(row);

    // Windows-style fit slider: Fill (0) · Fit (1) · Stretch (2) · Center (3)
    const fitBox = document.createElement('div');
    fitBox.className = 'fit-box';
    const fitHead = document.createElement('div');
    fitHead.className = 'fit-head';
    const fitLbl = document.createElement('span');
    fitLbl.textContent = 'Fit';
    const curIdx = Math.max(0, BG_FITS.findIndex((f) => f.key === bgs.fit));
    const fitVal = document.createElement('span');
    fitVal.className = 'mono';
    fitVal.textContent = BG_FITS[curIdx].label;
    fitHead.append(fitLbl, fitVal);

    const fitRange = document.createElement('input');
    fitRange.type = 'range';
    fitRange.min = '0';
    fitRange.max = String(BG_FITS.length - 1);
    fitRange.step = '1';
    fitRange.value = String(curIdx);
    fitRange.setAttribute('aria-label', 'Wallpaper fit mode');

    const ticks = document.createElement('div');
    ticks.className = 'fit-ticks';
    const tickBtns = BG_FITS.map((f, idx) => {
      const tb = document.createElement('button');
      tb.type = 'button';
      tb.textContent = f.label;
      tb.setAttribute('aria-pressed', String(idx === curIdx));
      tb.addEventListener('click', () => {
        fitRange.value = String(idx);
        updateFitUi(idx);
        send({ type: 'set_bg_fit', value: f.key });
      });
      ticks.append(tb);
      return tb;
    });

    function updateFitUi(idx) {
      const item = BG_FITS[idx] || BG_FITS[0];
      fitVal.textContent = item.label;
      tickBtns.forEach((b, i) => b.setAttribute('aria-pressed', String(i === idx)));
      applyBgFit(item.key); // instant live preview
    }

    fitRange.addEventListener('input', () => updateFitUi(Number(fitRange.value)));
    fitRange.addEventListener('change', () => {
      const item = BG_FITS[Number(fitRange.value)] || BG_FITS[0];
      send({ type: 'set_bg_fit', value: item.key });
    });

    fitBox.append(fitHead, fitRange, ticks);
    menu.append(fitBox);
  }
}

function applyBackground(bg) {
  const file = bg && bg.image ? bg.image : null;
  const dim = Math.max(0, Math.min(0.9, bg && typeof bg.dim === 'number' ? bg.dim : 0.45));
  bgs.active = file;
  bgs.dim = dim;
  document.documentElement.style.setProperty('--bg-dim', Math.round(dim * 100) + '%');
  applyBgFit(bg && bg.fit ? bg.fit : bgs.fit);
  const url = file ? mediaUrl(file) : '';
  if (url === bgs.shown) return;
  bgs.shown = url;
  const el = $('bg');
  el.style.opacity = '0';
  if (!url) { document.body.classList.remove('has-bg'); return; }
  const img = new Image();
  img.onload = () => {
    if (bgs.shown !== url) return;
    el.style.backgroundImage = `url("${url}")`;
    document.body.classList.add('has-bg');
    el.style.opacity = '1';
  };
  img.onerror = () => send({ type: 'log', text: 'background picture failed to load: ' + file });
  img.src = url;
}

function openThemeMenu() {
  send({ type: 'list_backgrounds' });
  renderThemeMenu();
  $('themeMenu').hidden = false;
  $('themeBtn').setAttribute('aria-expanded', 'true');
}

function closeThemeMenu() {
  bgDropdownOpen = false;
  $('themeMenu').hidden = true;
  $('themeBtn').setAttribute('aria-expanded', 'false');
}

$('themeBtn').addEventListener('click', () => ($('themeMenu').hidden ? openThemeMenu() : closeThemeMenu()));
document.addEventListener('pointerdown', (e) => {
  if (!$('themeMenu').hidden && !e.target.closest('.theme-wrap')) closeThemeMenu();
});
document.addEventListener('keydown', (e) => {
  if (e.key !== 'Escape') return;
  closeThemeMenu();
  closePowerMenu();
  closeTileMenu();
  if (!$('addModal').hidden) closeAdd();
  if (!$('deckModal').hidden) closeDeckModal();
  if (!$('saveThemeModal').hidden) closeSaveThemeModal();
});

// ---------- toast ----------
let toastTimer = 0;
function toast(text) {
  const el = $('toast');
  el.textContent = text;
  el.classList.add('show');
  clearTimeout(toastTimer);
  toastTimer = setTimeout(() => el.classList.remove('show'), 3500);
}

// ---------- activity: full frame rate while you're using it, 15 fps after 30 idle seconds ----------
let lastActivity = performance.now();
addEventListener('pointermove', (e) => {
  lastActivity = performance.now();
  core.setIdle(false);
  const nx = e.clientX / innerWidth, ny = e.clientY / innerHeight;
  core.pointer(nx, ny);
  const disc = $('coreDisc');
  if (disc) {
    disc.style.setProperty('--core-rx', ((0.5 - ny) * 14 + 4).toFixed(1) + 'deg');
    disc.style.setProperty('--core-ry', ((nx - 0.5) * 16).toFixed(1) + 'deg');
  }
}, { passive: true });
setInterval(() => { if (performance.now() - lastActivity > 30000) core.setIdle(true); }, 5000);

// ---------- host messages ----------
let lastAppsSig = '', lastDecksSig = '';
function onConfig(msg) {
  const nextApps = Array.isArray(msg.apps) ? msg.apps : [];
  const nextShell = Object.assign({}, state.shell, msg.shell || {});
  const appsSig = JSON.stringify(nextApps);
  const decksSig = JSON.stringify(nextShell.decks || []);
  const appsChanged = appsSig !== lastAppsSig;
  const decksChanged = decksSig !== lastDecksSig;
  lastAppsSig = appsSig;
  lastDecksSig = decksSig;

  state.apps = nextApps;
  state.shell = nextShell;
  state.shell.theme_color = (msg.shell && msg.shell.theme_color && typeof msg.shell.theme_color.hue === 'number') ? msg.shell.theme_color : null;
  state.shell.active_theme_profile = (msg.shell && typeof msg.shell.active_theme_profile === 'string' && msg.shell.active_theme_profile.trim()) ? msg.shell.active_theme_profile.trim() : null;
  state.shell.saved_themes = (msg.shell && Array.isArray(msg.shell.saved_themes)) ? msg.shell.saved_themes : [];
  const prevDeck = state.deck;
  if (pendingDeckSelect && state.shell.decks.includes(pendingDeckSelect)) {
    state.deck = pendingDeckSelect;
    pendingDeckSelect = null;
  }
  if (!state.shell.decks.includes(state.deck)) state.deck = state.shell.start_deck || state.shell.decks[0];
  applyEffectiveTheme();
  applyBackground(state.shell.background);
  applyIconSize(state.shell.icon_size || 'small', false);
  core.setBrightness(state.shell.core_brightness ?? 0.5);
  showStatus();
  if (decksChanged || state.deck !== prevDeck) renderDecks();
  if (appsChanged) renderPins();
  if (appsChanged || decksChanged || state.deck !== prevDeck) renderNodes(true);
  else { renderNodes(false); updateCoreName(); }
  tickClock();
}

if (host) {
  host.addEventListener('message', (e) => {
    const m = e.data || {};
    switch (m.type) {
      case 'config': onConfig(m); break;
      case 'theme':
        baseTheme = m.theme || {};
        themes.list = Array.isArray(m.list) ? m.list : [];
        themes.active = m.name || '';
        themes.previous = m.previous || '';
        applyEffectiveTheme();
        if (!$('themeMenu').hidden) renderThemeMenu();
        break;
      case 'layout': state.layout = m; applyLayout(); break;
      case 'status': showStatus(m); break;
      case 'telemetry': showTelemetry(m); break;
      case 'toast': toast(m.text); break;
      case 'backgrounds':
        bgs.items = Array.isArray(m.items) ? m.items : [];
        applyBackground({ image: m.active, dim: m.dim, fit: m.fit });
        if (!$('themeMenu').hidden) renderThemeMenu();
        break;
      case 'start_apps':
        add.items = Array.isArray(m.items) ? m.items : [];
        if (!$('addModal').hidden) {
          renderAddList();
          if (document.activeElement !== $('addSearch') && document.activeElement !== $('addName')) {
            send({ type: 'focus_input', id: 'addSearch' });
          }
        }
        break;
      case 'focus_element':
        if (m.id && $(m.id)) $(m.id).focus();
        break;
      case 'deck_select':
        if (m.name) {
          pendingDeckSelect = m.name;
          if (state.shell.decks.includes(m.name)) { setDeck(m.name); pendingDeckSelect = null; }
        }
        break;
    }
  });
} else {
  // Opened in a normal browser for design work: show sample apps, no live data.
  onConfig({ apps: [
    { name: 'Files', decks: ['Command'], icon: 'folder', ready: true, pinned: true },
    { name: 'Browser', decks: ['Command'], icon: 'globe', ready: true, pinned: true },
    { name: 'YouTube', decks: ['Command'], icon: 'play', ready: true },
    { name: 'Settings', decks: ['Command'], icon: 'sliders', ready: true, pinned: true },
    { name: 'Elite Dangerous', decks: ['Command', 'Flight'], icon: 'ship', ready: false, note: 'needs Game Mode' },
  ] });
}

applyLayout();
showStatus();
send({ type: 'ready' });
