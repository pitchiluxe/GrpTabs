// GrpTabs site: the hero demo (one looping, narrated timeline) and the version
// label on the download line. No framework; text goes in with textContent only.

const desk = document.getElementById('desk');
const caption = document.querySelector('.caption');
const pauseBtn = document.querySelector('.replay');
const reduce = window.matchMedia('(prefers-reduced-motion: reduce)').matches;

const el = {
  note: desk.querySelector('[data-win="note"]'),
  term: desk.querySelector('[data-win="term"]'),
  noteTabs: desk.querySelector('[data-tabs="note"]'),
  termTabs: desk.querySelector('[data-tabs="term"]'),
  ghost: desk.querySelector('.ghost'),
  keys: desk.querySelector('.keys'),
  cursor: desk.querySelector('.cursor'),
};

// Window frames, in percent of the desk.
const HOME = { note: { l: 6, t: 22, w: 40, h: 62 }, term: { l: 54, t: 26, w: 40, h: 62 } };
const TAB_H = 6.6;
const TITLES = { note: 'groceries.txt - Notepad', term: 'Windows PowerShell' };

let playing = !reduce;
let visible = true;
let runId = 0;

const pct = (v) => `${v}%`;

function place(win, f) {
  Object.assign(win.style, { left: pct(f.l), top: pct(f.t), width: pct(f.w), height: pct(f.h) });
}

function placeTabs(tabs, f) {
  Object.assign(tabs.style, { left: pct(f.l + 1.2), top: pct(f.t - TAB_H) });
}

/** A tab bar showing these windows, with `active` highlighted. */
function setTabs(tabs, ids, active) {
  tabs.replaceChildren(...ids.map((id) => {
    const t = document.createElement('span');
    t.className = `tab${id === active ? ' on' : ''}`;
    t.dataset.id = id;
    const ico = document.createElement('i');
    ico.className = `ico ico-${id}`;
    t.append(ico, TITLES[id]);
    return t;
  }));
}

/** Where an element sits, in percent of the desk (its centre, or a point inside it). */
function at(node, fx = 0.5, fy = 0.5) {
  const d = desk.getBoundingClientRect();
  const r = node.getBoundingClientRect();
  return { x: ((r.left + r.width * fx - d.left) / d.width) * 100, y: ((r.top + r.height * fy - d.top) / d.height) * 100 };
}

function cursorTo(p) {
  Object.assign(el.cursor.style, { left: pct(p.x), top: pct(p.y) });
}

function ghostTo(p) {
  Object.assign(el.ghost.style, { left: pct(p.x), top: pct(p.y) });
}

function say(text) {
  caption.textContent = text;
}

/** Wait, but stop if the demo restarts; hold while paused or scrolled away. */
async function wait(ms, id) {
  let left = ms;
  while (left > 0) {
    if (id !== runId) throw new Error('restart');
    await new Promise((r) => setTimeout(r, 50));
    if (playing && visible) left -= 50;
  }
  if (id !== runId) throw new Error('restart');
}

function reset() {
  place(el.note, HOME.note);
  place(el.term, HOME.term);
  placeTabs(el.noteTabs, HOME.note);
  placeTabs(el.termTabs, HOME.term);
  setTabs(el.noteTabs, ['note'], 'note');
  setTabs(el.termTabs, ['term'], 'term');
  el.note.classList.remove('hidden');
  el.term.classList.remove('hidden', 'target');
  el.noteTabs.classList.remove('show');
  el.termTabs.classList.remove('show');
  el.ghost.classList.remove('show');
  el.keys.classList.remove('show');
  el.note.style.zIndex = 2;
  el.term.style.zIndex = 1;
  cursorTo({ x: 50, y: 94 });
}

/** The grouped state: both windows in the PowerShell frame, `active` showing. */
function showGroup(active) {
  const f = HOME.term;
  place(el.note, f);
  placeTabs(el.termTabs, f);
  setTabs(el.termTabs, ['term', 'note'], active);
  el.termTabs.classList.add('show');
  el.noteTabs.classList.remove('show');
  el.note.classList.toggle('hidden', active !== 'note');
  el.term.classList.toggle('hidden', active !== 'term');
  el.note.style.zIndex = active === 'note' ? 2 : 1;
  el.term.style.zIndex = active === 'term' ? 2 : 1;
}

async function press(id) {
  el.cursor.classList.add('down');
  await wait(140, id);
  el.cursor.classList.remove('down');
}

async function loop() {
  const id = ++runId;
  try {
    for (;;) {
      reset();
      say('Point at a window and its tab appears.');
      await wait(700, id);
      cursorTo({ x: HOME.note.l + 20, y: HOME.note.t + 34 });
      await wait(950, id);
      el.noteTabs.classList.add('show');
      await wait(1100, id);

      say('Drag that tab onto another window.');
      const grab = at(el.noteTabs.firstElementChild, 0.4, 0.5);
      cursorTo(grab);
      await wait(950, id);
      el.cursor.classList.add('down');
      ghostTo(grab);
      el.ghost.classList.add('show');
      await wait(250, id);
      const drop = { x: HOME.term.l + 18, y: HOME.term.t + 2.5 };
      cursorTo(drop);
      ghostTo(drop);
      el.term.classList.add('target');
      await wait(1150, id);

      say('Let go. Both windows now share one frame and one tab bar.');
      el.cursor.classList.remove('down');
      el.ghost.classList.remove('show');
      el.term.classList.remove('target');
      showGroup('note');
      await wait(2000, id);

      say('Click a tab to switch windows.');
      cursorTo(at(el.termTabs.querySelector('[data-id="term"]'), 0.4, 0.5));
      await wait(1000, id);
      await press(id);
      showGroup('term');
      await wait(1600, id);

      say('Or switch with the keyboard.');
      cursorTo({ x: 88, y: 92 });
      el.keys.classList.add('show');
      await wait(900, id);
      showGroup('note');
      await wait(1400, id);
      el.keys.classList.remove('show');

      say('Drag a tab away and that window steps out of the group.');
      const out = at(el.termTabs.querySelector('[data-id="note"]'), 0.4, 0.5);
      cursorTo(out);
      await wait(1000, id);
      el.cursor.classList.add('down');
      ghostTo(out);
      el.ghost.classList.add('show');
      await wait(250, id);
      cursorTo({ x: 22, y: 30 });
      ghostTo({ x: 22, y: 30 });
      await wait(1150, id);
      el.cursor.classList.remove('down');
      el.ghost.classList.remove('show');
      place(el.note, HOME.note);
      placeTabs(el.noteTabs, HOME.note);
      setTabs(el.termTabs, ['term'], 'term');
      el.termTabs.classList.remove('show');
      el.noteTabs.classList.add('show');
      el.term.classList.remove('hidden');
      el.note.classList.remove('hidden');
      el.note.style.zIndex = 2;
      await wait(2400, id);
    }
  } catch {
    /* restarted */
  }
}

function still() {
  // Reduced motion: one clear frame of the result instead of the animation.
  reset();
  el.cursor.style.display = 'none';
  showGroup('note');
  say('Two windows grouped: click a tab to switch, drag it away to split.');
}

pauseBtn.addEventListener('click', () => {
  if (reduce && !playing) {
    playing = true;
    el.cursor.style.display = '';
    pauseBtn.textContent = 'Pause demo';
    pauseBtn.setAttribute('aria-pressed', 'false');
    loop();
    return;
  }
  playing = !playing;
  pauseBtn.textContent = playing ? 'Pause demo' : 'Play demo';
  pauseBtn.setAttribute('aria-pressed', String(!playing));
});

new IntersectionObserver((entries) => { visible = entries[0].isIntersecting; }, { threshold: 0.2 }).observe(desk);

if (reduce) {
  still();
  pauseBtn.textContent = 'Play demo';
} else {
  loop();
}

// The download line shows the latest version from GitHub; the links already point at the latest installer.
fetch('https://api.github.com/repos/pitchiluxe/GrpTabs/releases/latest')
  .then((r) => (r.ok ? r.json() : null))
  .then((rel) => {
    if (!rel || !rel.tag_name) return;
    const v = rel.tag_name.replace(/^v/, '');
    const setup = (rel.assets || []).find((a) => /Setup\.exe$/i.test(a.name));
    const size = setup ? ` (${Math.round(setup.size / 1048576)} MB)` : '';
    document.querySelectorAll('.js-version').forEach((n) => { n.textContent = `Version ${v}${size}, free for Windows 10 and 11.`; });
    document.querySelectorAll('.js-shot-ver').forEach((n) => { n.textContent = `v${v}`; });
  })
  .catch(() => {});
