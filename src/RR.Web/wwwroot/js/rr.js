// Rotation Raider browser presentation layer.
//
// Owns the requestAnimationFrame loop, input capture (with event timestamps)
// and all drawing. Game logic lives in C# (RR.Sim / RR.Client); every frame we
// hand it the inputs captured since the last frame and draw the state it
// returns. In the Unity port this file is replaced by RR.Game.
(function () {
  'use strict';

  const RR = (window.RR = window.RR || {});
  const TAU = Math.PI * 2;
  const MS_PER_TICK = 1000 / 60;
  const EMOJI = '"Apple Color Emoji","Segoe UI Emoji","Noto Color Emoji",sans-serif';
  const UI_FONT = 'system-ui,-apple-system,"Segoe UI",Roboto,sans-serif';

  // Must match RR.Client.ClientInput.
  const IN = { S0: 0, S1: 1, S2: 2, S3: 3, SWAP_DOWN: 4, SWAP_UP: 5, MAJOR: 6, MINOR: 7, TRINKET: 8, BLOCK: 9, DODGE: 10, END: 11 };

  const COLORS = {
    block: '#f5c542',
    dodge: '#ff4d6d',
    none: '#9aa4b2',
    perfect: '#7ef9ff',
    text: '#f4f1ea',
    dim: '#8b8d98',
    panel: 'rgba(10,10,16,0.72)',
  };

  const clamp = (v, a, b) => Math.max(a, Math.min(b, v));
  const lerp = (a, b, t) => a + (b - a) * t;
  const easeOut = (t) => 1 - Math.pow(1 - t, 3);

  // ---------------------------------------------------------------- storage

  RR.storage = {
    get(key) {
      try { return window.localStorage.getItem(key); } catch (e) { return null; }
    },
    set(key, value) {
      try { window.localStorage.setItem(key, value); } catch (e) { /* best effort */ }
    },
  };

  RR.download = function (name, text) {
    const blob = new Blob([text], { type: 'application/json' });
    const a = document.createElement('a');
    a.href = URL.createObjectURL(blob);
    a.download = name;
    document.body.appendChild(a);
    a.click();
    setTimeout(() => { URL.revokeObjectURL(a.href); a.remove(); }, 1000);
  };

  RR.fullscreen = async function () {
    try {
      if (!document.fullscreenElement) await document.documentElement.requestFullscreen();
      if (screen.orientation && screen.orientation.lock) await screen.orientation.lock('landscape');
    } catch (e) { /* not supported (iOS Safari): ignore */ }
  };

  // ------------------------------------------------------------------ audio

  const Sfx = {
    ctx: null,
    on: true,
    ensure() {
      if (!this.on) return null;
      if (!this.ctx) {
        try { this.ctx = new (window.AudioContext || window.webkitAudioContext)(); } catch (e) { return null; }
      }
      if (this.ctx.state === 'suspended') this.ctx.resume();
      return this.ctx;
    },
    tone(freq, dur, type, vol, slideTo, delay) {
      const ctx = this.ensure();
      if (!ctx) return;
      const t0 = ctx.currentTime + (delay || 0);
      const osc = ctx.createOscillator();
      const g = ctx.createGain();
      osc.type = type || 'sine';
      osc.frequency.setValueAtTime(freq, t0);
      if (slideTo) osc.frequency.exponentialRampToValueAtTime(slideTo, t0 + dur);
      g.gain.setValueAtTime(vol || 0.1, t0);
      g.gain.exponentialRampToValueAtTime(0.0001, t0 + dur);
      osc.connect(g).connect(ctx.destination);
      osc.start(t0);
      osc.stop(t0 + dur + 0.02);
    },
    noise(dur, vol, freq) {
      const ctx = this.ensure();
      if (!ctx) return;
      const len = Math.floor(ctx.sampleRate * dur);
      const buf = ctx.createBuffer(1, len, ctx.sampleRate);
      const d = buf.getChannelData(0);
      for (let i = 0; i < len; i++) d[i] = (Math.random() * 2 - 1) * (1 - i / len);
      const src = ctx.createBufferSource();
      src.buffer = buf;
      const f = ctx.createBiquadFilter();
      f.type = 'lowpass';
      f.frequency.value = freq || 1200;
      const g = ctx.createGain();
      g.gain.value = vol || 0.2;
      src.connect(f).connect(g).connect(ctx.destination);
      src.start();
    },
    play(name) {
      if (!this.on) return;
      switch (name) {
        case 'hit': this.noise(0.08, 0.12, 1800); break;
        case 'crit': this.noise(0.12, 0.2, 3000); this.tone(880, 0.08, 'square', 0.04); break;
        case 'perfect': this.tone(1320, 0.18, 'triangle', 0.12); this.tone(1980, 0.22, 'sine', 0.08, null, 0.04); break;
        case 'avoid': this.tone(520, 0.1, 'triangle', 0.08); break;
        case 'hurt': this.noise(0.25, 0.35, 500); this.tone(110, 0.25, 'sawtooth', 0.08, 60); break;
        case 'proc': this.tone(988, 0.08, 'sine', 0.08); this.tone(1318, 0.12, 'sine', 0.07, null, 0.06); break;
        case 'teleBlock': this.tone(330, 0.12, 'square', 0.04); break;
        case 'teleDodge': this.tone(660, 0.07, 'square', 0.04); this.tone(660, 0.07, 'square', 0.04, null, 0.1); break;
        case 'teleNone': this.tone(140, 0.4, 'sawtooth', 0.05, 90); break;
        case 'fail': this.tone(180, 0.06, 'square', 0.03); break;
        case 'flow': this.tone(784, 0.15, 'triangle', 0.1); this.tone(1046, 0.15, 'triangle', 0.1, null, 0.08); this.tone(1568, 0.3, 'triangle', 0.1, null, 0.16); break;
        case 'phase': this.tone(90, 0.8, 'sawtooth', 0.12, 45); break;
        case 'win': [523, 659, 784, 1046].forEach((f, i) => this.tone(f, 0.3, 'triangle', 0.1, null, i * 0.12)); break;
        case 'lose': [392, 330, 262].forEach((f, i) => this.tone(f, 0.4, 'sawtooth', 0.06, null, i * 0.18)); break;
        case 'tick': this.tone(1000, 0.04, 'square', 0.05); break;
        case 'go': this.tone(1400, 0.15, 'square', 0.06); break;
      }
    },
  };

  // ------------------------------------------------------------------ fight

  const F = {
    running: false,
    canvas: null,
    ctx: null,
    bridge: null,
    screen: null,
    init: null,
    f: null,
    queue: [],
    raf: 0,
    paused: false,
    w: 0,
    h: 0,
    dpr: 1,
    L: null,
    lastNow: 0,
    sceneT: 0,
    hitstopUntil: 0,
    shake: { until: 0, mag: 0 },
    flash: { until: 0, color: '#fff', dur: 1 },
    floaters: [],
    banners: [],
    toasts: [],
    pAnim: null,
    bAnim: null,
    bossFlashUntil: 0,
    defPress: { block: 0, dodge: 0 },
    pointers: new Map(),
    pressedCodes: new Map(),
    countdownStart: 0,
    started: false,
    endInfo: null,
    lastCountBeat: -1,
  };

  function now() { return performance.now(); }

  function eventTime(e) {
    // Event timestamps share performance.now()'s clock in current browsers.
    const t = e.timeStamp;
    const n = now();
    return t > 0 && Math.abs(n - t) < 5000 ? t : n;
  }

  function push(code, t) {
    F.queue.push(code, t);
  }

  RR.fight = {
    start(canvasId, bridge, screenRef, initJson) {
      RR.fight.stop();
      F.canvas = document.getElementById(canvasId);
      F.ctx = F.canvas.getContext('2d');
      F.bridge = bridge;
      F.screen = screenRef;
      F.init = JSON.parse(initJson);
      Sfx.on = !!F.init.settings.sound;
      Sfx.ensure();
      Object.assign(F, {
        running: true, paused: false, queue: [], f: null, floaters: [], banners: [], toasts: [],
        pAnim: null, bAnim: null, started: false, endInfo: null, lastCountBeat: -1,
        hitstopUntil: 0, sceneT: 0, lastNow: now(), countdownStart: now(),
      });
      F.pointers.clear();
      F.pressedCodes.clear();
      F.coarse = window.matchMedia && window.matchMedia('(pointer: coarse)').matches;
      resize();
      window.addEventListener('resize', resize);
      window.addEventListener('keydown', onKeyDown, true);
      window.addEventListener('keyup', onKeyUp, true);
      document.addEventListener('visibilitychange', onVisibility);
      F.canvas.addEventListener('pointerdown', onPointerDown);
      F.canvas.addEventListener('pointerup', onPointerUp);
      F.canvas.addEventListener('pointercancel', onPointerUp);
      F.canvas.addEventListener('contextmenu', prevent);
      F.raf = requestAnimationFrame(loop);
    },

    stop() {
      if (!F.running) return;
      F.running = false;
      cancelAnimationFrame(F.raf);
      window.removeEventListener('resize', resize);
      window.removeEventListener('keydown', onKeyDown, true);
      window.removeEventListener('keyup', onKeyUp, true);
      document.removeEventListener('visibilitychange', onVisibility);
      if (F.canvas) {
        F.canvas.removeEventListener('pointerdown', onPointerDown);
        F.canvas.removeEventListener('pointerup', onPointerUp);
        F.canvas.removeEventListener('pointercancel', onPointerUp);
        F.canvas.removeEventListener('contextmenu', prevent);
      }
    },

    pause() {
      if (!F.running || F.paused || !F.started || (F.f && F.f.ended)) return;
      F.paused = true;
      F.bridge.invokeMethod('Pause', now());
      F.screen.invokeMethodAsync('OnPaused');
    },

    resume() {
      if (!F.running || !F.paused) return;
      F.paused = false;
      F.bridge.invokeMethod('Resume', now());
    },

    sendInput(code) {
      push(code, now());
    },
  };

  function prevent(e) { e.preventDefault(); }

  function onVisibility() {
    if (document.hidden) RR.fight.pause();
  }

  function resize() {
    const dpr = Math.min(window.devicePixelRatio || 1, 2);
    const w = window.innerWidth;
    const h = window.innerHeight;
    F.dpr = dpr;
    F.w = w;
    F.h = h;
    F.canvas.width = Math.round(w * dpr);
    F.canvas.height = Math.round(h * dpr);
    F.canvas.style.width = w + 'px';
    F.canvas.style.height = h + 'px';
    F.L = layout(w, h);
  }

  // ---------------------------------------------------------------- layout

  function layout(w, h) {
    const m = Math.min(w, h);
    const pad = Math.max(10, m * 0.03);
    const a = clamp(m * 0.075, 22, 46);
    const C = { x: w - pad - a * 0.95, y: h - pad - a * 0.95 };
    const deg = (d) => (d * Math.PI) / 180;
    const ring = (radius, angles, r) =>
      angles.map((d) => ({ x: C.x + radius * Math.cos(deg(d)), y: C.y - radius * Math.sin(deg(d)), r }));

    const D = a * 1.45;
    const block = { x: pad + D, y: h - pad - D, r: D };
    const dodge = { x: block.x + 2.25 * D * Math.cos(deg(52)), y: block.y - 2.25 * D * Math.sin(deg(52)), r: D * 0.92 };
    const S = clamp(h * 0.2, 60, 230);

    return {
      w, h, m, pad, a, S,
      swap: { x: C.x, y: C.y, r: a * 0.8 },
      slots: ring(a * 4.35, [180, 150, 120, 90], a),
      cds: ring(a * 2.2, [178, 134, 90], a * 0.62),
      block,
      dodge,
      pauseBtn: { x: pad + 16, y: pad + 16, r: 16 },
      groundY: h * 0.66,
      playerX: w * 0.3,
      bossX: w * 0.63,
      tele: { x: w * 0.47, y: h * 0.38, R: clamp(m * 0.055, 18, 44) },
      font: (px) => Math.round(clamp(px * (m / 420), px * 0.75, px * 2.1)),
    };
  }

  function hitRegions() {
    const L = F.L;
    const regs = [];
    L.slots.forEach((s, i) => regs.push({ code: IN.S0 + i, ...s }));
    regs.push({ code: IN.SWAP_DOWN, release: IN.SWAP_UP, ...L.swap });
    regs.push({ code: IN.MAJOR, ...L.cds[0] });
    regs.push({ code: IN.MINOR, ...L.cds[1] });
    regs.push({ code: IN.TRINKET, ...L.cds[2] });
    regs.push({ code: IN.BLOCK, ...L.block });
    regs.push({ code: IN.DODGE, ...L.dodge });
    regs.push({ code: 'pause', ...L.pauseBtn, r: 26 });
    return regs;
  }

  // ----------------------------------------------------------------- input

  function onPointerDown(e) {
    e.preventDefault();
    Sfx.ensure();
    if (F.paused) return;
    const rect = F.canvas.getBoundingClientRect();
    const x = e.clientX - rect.left;
    const y = e.clientY - rect.top;
    let best = null;
    let bestD = Infinity;
    for (const r of hitRegions()) {
      const d = Math.hypot(x - r.x, y - r.y);
      if (d <= r.r * 1.18 && d / r.r < bestD) {
        best = r;
        bestD = d / r.r;
      }
    }
    if (!best) return;
    if (best.code === 'pause') {
      RR.fight.pause();
      return;
    }
    try { F.canvas.setPointerCapture(e.pointerId); } catch (err) { /* ignore */ }
    F.pointers.set(e.pointerId, best);
    press(best.code, eventTime(e));
  }

  function onPointerUp(e) {
    const r = F.pointers.get(e.pointerId);
    if (!r) return;
    F.pointers.delete(e.pointerId);
    release(r.code, eventTime(e));
  }

  const KEYS = {
    Digit1: IN.S0, Digit2: IN.S1, Digit3: IN.S2, Digit4: IN.S3,
    Numpad1: IN.S0, Numpad2: IN.S1, Numpad3: IN.S2, Numpad4: IN.S3,
    KeyQ: IN.SWAP_DOWN, KeyE: IN.MAJOR, KeyR: IN.MINOR, KeyT: IN.TRINKET,
    Space: IN.BLOCK, KeyJ: IN.BLOCK, ShiftLeft: IN.DODGE, ShiftRight: IN.DODGE, KeyK: IN.DODGE,
  };
  const KEY_LABELS = { 0: '1', 1: '2', 2: '3', 3: '4', 4: 'Q', 6: 'E', 7: 'R', 8: 'T', 9: 'Space', 10: 'Shift' };

  function onKeyDown(e) {
    if (e.code === 'Escape') {
      e.preventDefault();
      if (F.paused) return;
      RR.fight.pause();
      return;
    }
    const code = KEYS[e.code];
    if (code === undefined) return;
    e.preventDefault();
    if (e.repeat || F.paused) return;
    press(code, eventTime(e));
  }

  function onKeyUp(e) {
    const code = KEYS[e.code];
    if (code === undefined) return;
    e.preventDefault();
    release(code, eventTime(e));
  }

  function press(code, t) {
    F.pressedCodes.set(code, now());
    if (code === IN.BLOCK) F.defPress.block = now();
    if (code === IN.DODGE) F.defPress.dodge = now();
    if (!F.started) return;
    push(code, t);
  }

  function release(code, t) {
    F.pressedCodes.delete(code);
    if (code === IN.SWAP_DOWN) push(IN.SWAP_UP, t);
  }

  // ------------------------------------------------------------------ loop

  function loop(t) {
    if (!F.running) return;
    F.raf = requestAnimationFrame(loop);

    const dt = Math.min(100, t - F.lastNow);
    F.lastNow = t;
    if (!F.paused && t >= F.hitstopUntil) F.sceneT += dt;

    if (!F.started) {
      const beat = Math.floor((t - F.countdownStart) / 600);
      if (beat !== F.lastCountBeat && beat < 3) {
        F.lastCountBeat = beat;
        Sfx.play('tick');
      }
      if (t - F.countdownStart >= 1800) {
        F.started = true;
        F.bridge.invokeMethod('Start', t);
        Sfx.play('go');
        banner('FIGHT!', COLORS.text, 700, 1.4);
      }
    }

    if (!F.paused) {
      const inputs = F.queue;
      F.queue = [];
      let json;
      try {
        json = F.bridge.invokeMethod('Frame', t, inputs);
      } catch (err) {
        console.error(err);
        RR.fight.stop();
        return;
      }
      const frame = JSON.parse(json);
      F.f = frame;
      for (const ev of frame.ev) onEvent(ev, t);
    }

    draw(t);
  }

  // ---------------------------------------------------------------- events

  function abilityName(id) {
    const a = F.init.abilities[id];
    if (a) return a.name;
    const au = F.init.auras[id];
    return au ? au.name : id;
  }

  function onEvent(ev, t) {
    const L = F.L;
    switch (ev.k) {
      case 'dmg': {
        const x = L.bossX + (Math.random() - 0.5) * L.S * 0.9;
        const y = L.groundY - L.S * 1.55 - Math.random() * L.S * 0.3;
        F.floaters.push({
          text: ev.a.toLocaleString() + (ev.c ? '!' : ''),
          x, y,
          born: t,
          life: ev.c ? 1100 : 850,
          color: ev.p ? '#e0707a' : ev.c ? '#ffd24a' : '#ffffff',
          size: ev.c ? 30 : ev.p ? 15 : 20,
        });
        if (!ev.p) {
          F.bossFlashUntil = t + 70;
          Sfx.play(ev.c ? 'crit' : 'hit');
        }
        break;
      }
      case 'use': {
        const a = F.init.abilities[ev.id] || {};
        const anim = a.anim || '';
        const type = anim === 'roar' || anim === 'drink' ? 'buff' : 'attack';
        F.pAnim = { type, start: F.sceneT, dur: type === 'attack' ? 260 : 400, color: a.color || '#fff' };
        if (a.cost >= 80 || ev.id === 'recklessness') shake(t, 120, 4);
        if (type === 'buff') banner(a.name, a.color || COLORS.text, 900, 0.8, 'player');
        break;
      }
      case 'fail': {
        const reasons = {
          NotEnoughResource: 'Not enough ' + F.init.res.name,
          OnCooldown: 'On cooldown',
          OnGlobalCooldown: 'Not ready',
          MissingAura: 'Needs a perfect Block',
          Staggered: 'Staggered!',
          TargetHpTooHigh: 'Target HP too high',
        };
        const msg = reasons[ev.r];
        if (msg) toast(msg);
        Sfx.play('fail');
        break;
      }
      case 'def':
        F.pAnim = { type: ev.d, start: F.sceneT, dur: ev.d === 'dodge' ? 380 : 280 };
        break;
      case 'tele':
        Sfx.play(ev.c === 'block' ? 'teleBlock' : ev.c === 'dodge' ? 'teleDodge' : 'teleNone');
        break;
      case 'res': {
        F.bAnim = { type: 'strike', start: F.sceneT, dur: 260 };
        if (ev.o === 'perfect') {
          F.hitstopUntil = t + 75;
          flash(t, '#ffffff', 110, 0.35);
          banner('PERFECT', COLORS.perfect, 650, 1.25, 'tele');
          sparks(L.playerX + L.S * 0.35, L.groundY - L.S * 0.8, COLORS.perfect, t);
          Sfx.play('perfect');
        } else if (ev.o === 'avoided') {
          banner(ev.id && F.f && teleCounter(ev) === 'dodge' ? 'DODGED' : 'AVOIDED', COLORS.text, 550, 0.9, 'tele');
          Sfx.play('avoid');
        } else if (ev.o === 'hit') {
          shake(t, 260, 11);
          flash(t, '#ff2030', 220, 0.3);
          banner('HIT  −' + ev.dmg.toLocaleString(), '#ff5a64', 700, 0.95, 'tele');
          F.pAnim = { type: 'hurt', start: F.sceneT, dur: 420 };
          Sfx.play('hurt');
        } else {
          shake(t, 400, 7);
          flash(t, '#c8d0ff', 300, 0.25);
        }
        break;
      }
      case 'proc': {
        const name = F.init.procs[ev.id] || ev.id;
        banner(name + '!', '#ffd24a', 900, 0.75, 'player');
        Sfx.play('proc');
        break;
      }
      case 'flow':
        banner('FLOW!', '#48e6c8', 1100, 1.5, 'center');
        flash(t, '#48e6c8', 260, 0.25);
        Sfx.play('flow');
        break;
      case 'phase':
        banner('PHASE ' + (ev.p + 1), '#ff7a5a', 1400, 1.6, 'center');
        shake(t, 500, 9);
        Sfx.play('phase');
        break;
      case 'end': {
        const text = ev.r === 'Killed' ? 'VICTORY' : ev.r === 'Enraged' ? 'ENRAGED' : ev.r === 'DummyComplete' ? 'TIME!' : 'FIGHT OVER';
        F.endInfo = { text, victory: ev.v, reason: ev.r, t };
        banner(text, ev.v ? '#ffd24a' : '#ff4d4d', 2400, 2.0, 'center');
        Sfx.play(ev.v ? 'win' : 'lose');
        break;
      }
    }
  }

  function teleCounter(ev) {
    const tg = F.f && F.f.tg.find((x) => x.id === ev.id);
    return tg ? tg.c : '';
  }

  function shake(t, dur, mag) {
    if (t + dur > F.shake.until || mag > F.shake.mag) F.shake = { until: t + dur, mag, dur };
  }

  function flash(t, color, dur, alpha) {
    F.flash = { until: t + dur, color, dur, alpha };
  }

  function banner(text, color, life, scale, where) {
    F.banners.push({ text, color, life, scale: scale || 1, where: where || 'center', born: now() });
  }

  function toast(text) {
    F.toasts = F.toasts.filter((x) => x.text !== text);
    F.toasts.push({ text, born: now(), life: 900 });
  }

  function sparks(x, y, color, t) {
    for (let i = 0; i < 14; i++) {
      const a = Math.random() * TAU;
      const sp = 80 + Math.random() * 160;
      F.floaters.push({ spark: true, x, y, vx: Math.cos(a) * sp, vy: Math.sin(a) * sp, born: t, life: 380, color });
    }
  }

  // ------------------------------------------------------------------ draw

  function draw(t) {
    const ctx = F.ctx;
    const L = F.L;
    const f = F.f;
    ctx.setTransform(F.dpr, 0, 0, F.dpr, 0, 0);

    let sx = 0;
    let sy = 0;
    if (t < F.shake.until) {
      const k = (F.shake.until - t) / (F.shake.dur || 200);
      sx = (Math.random() - 0.5) * 2 * F.shake.mag * k;
      sy = (Math.random() - 0.5) * 2 * F.shake.mag * k;
    }

    drawBackground(ctx, L);
    ctx.save();
    ctx.translate(sx, sy);
    if (f) {
      drawBoss(ctx, L, f, t);
      drawPlayer(ctx, L, f, t);
      drawTelegraphs(ctx, L, f);
      drawFloaters(ctx, t);
    }
    ctx.restore();

    if (f) {
      drawHud(ctx, L, f, t);
      drawButtons(ctx, L, f, t);
    }
    drawBanners(ctx, L, t);
    drawToasts(ctx, L, t);

    if (t < F.flash.until) {
      const k = (F.flash.until - t) / F.flash.dur;
      ctx.globalAlpha = k * (F.flash.alpha || 0.3);
      ctx.fillStyle = F.flash.color;
      ctx.fillRect(0, 0, L.w, L.h);
      ctx.globalAlpha = 1;
    }

    if (!F.started) drawCountdown(ctx, L, t);
    if (L.h > L.w) drawRotateHint(ctx, L);
    if (F.paused) {
      ctx.fillStyle = 'rgba(0,0,0,0.45)';
      ctx.fillRect(0, 0, L.w, L.h);
    }
  }

  function drawBackground(ctx, L) {
    const g = ctx.createLinearGradient(0, 0, 0, L.h);
    g.addColorStop(0, '#1b1626');
    g.addColorStop(0.55, '#2a1f2b');
    g.addColorStop(1, '#120d12');
    ctx.fillStyle = g;
    ctx.fillRect(0, 0, L.w, L.h);

    // distant pillars
    ctx.fillStyle = 'rgba(255,255,255,0.03)';
    for (let i = 0; i < 7; i++) {
      const x = (i + 0.5) * (L.w / 7);
      ctx.fillRect(x - L.w * 0.018, L.groundY - L.h * 0.5, L.w * 0.036, L.h * 0.5);
    }

    const fg = ctx.createLinearGradient(0, L.groundY, 0, L.h);
    fg.addColorStop(0, '#3a2b2a');
    fg.addColorStop(1, '#140e0e');
    ctx.fillStyle = fg;
    ctx.fillRect(0, L.groundY, L.w, L.h - L.groundY);
    ctx.strokeStyle = 'rgba(255,200,150,0.15)';
    ctx.lineWidth = 2;
    ctx.beginPath();
    ctx.moveTo(0, L.groundY);
    ctx.lineTo(L.w, L.groundY);
    ctx.stroke();

    const v = ctx.createRadialGradient(L.w / 2, L.h * 0.5, L.m * 0.3, L.w / 2, L.h * 0.5, Math.max(L.w, L.h) * 0.75);
    v.addColorStop(0, 'rgba(0,0,0,0)');
    v.addColorStop(1, 'rgba(0,0,0,0.55)');
    ctx.fillStyle = v;
    ctx.fillRect(0, 0, L.w, L.h);
  }

  function shadow(ctx, x, y, w) {
    ctx.fillStyle = 'rgba(0,0,0,0.4)';
    ctx.beginPath();
    ctx.ellipse(x, y + 4, w, w * 0.18, 0, 0, TAU);
    ctx.fill();
  }

  function activeTelegraph(f) {
    for (const tg of f.tg) if (!tg.done) return tg;
    return null;
  }

  function drawPlayer(ctx, L, f, t) {
    const S = L.S;
    let x = L.playerX;
    let y = L.groundY;
    let rot = 0;
    let axeRot = -0.6;
    let tint = null;
    let guard = 0;

    const a = F.pAnim;
    if (a) {
      const p = clamp((F.sceneT - a.start) / a.dur, 0, 1);
      const s = Math.sin(p * Math.PI);
      switch (a.type) {
        case 'attack':
          x += s * S * 0.45;
          axeRot = lerp(-1.6, 1.3, easeOut(p));
          break;
        case 'buff':
          y -= s * S * 0.12;
          axeRot = -2.2 * s - 0.6;
          tint = a.color;
          break;
        case 'block':
          guard = s;
          axeRot = -1.57;
          break;
        case 'dodge':
          x -= s * S * 0.55;
          y -= s * S * 0.35;
          rot = -s * 0.5;
          break;
        case 'hurt':
          x -= s * S * 0.25;
          rot = -s * 0.2;
          tint = '#ff3040';
          break;
      }
      if (p >= 1) F.pAnim = null;
    }

    const staggered = f.tick < f.stagger;
    if (staggered) x += Math.sin(F.sceneT / 30) * 2;
    const bob = Math.sin(F.sceneT / 380) * S * 0.015;

    shadow(ctx, x, L.groundY, S * 0.32);
    ctx.save();
    ctx.translate(x, y + bob);
    ctx.rotate(rot);

    // legs
    ctx.fillStyle = '#2b2522';
    ctx.fillRect(-S * 0.14, -S * 0.42, S * 0.11, S * 0.42);
    ctx.fillRect(S * 0.03, -S * 0.42, S * 0.11, S * 0.42);
    // torso
    ctx.fillStyle = '#7a1f1f';
    roundRect(ctx, -S * 0.2, -S * 0.95, S * 0.4, S * 0.58, S * 0.08);
    ctx.fill();
    ctx.fillStyle = '#5b5f66';
    roundRect(ctx, -S * 0.22, -S * 0.98, S * 0.44, S * 0.14, S * 0.05);
    ctx.fill();
    // head + helm
    ctx.fillStyle = '#d8a684';
    ctx.beginPath();
    ctx.arc(0, -S * 1.08, S * 0.13, 0, TAU);
    ctx.fill();
    ctx.fillStyle = '#6d727a';
    ctx.beginPath();
    ctx.arc(0, -S * 1.12, S * 0.14, Math.PI, TAU);
    ctx.fill();
    ctx.fillRect(-S * 0.15, -S * 1.13, S * 0.3, S * 0.04);
    // horns
    ctx.strokeStyle = '#e8dcc0';
    ctx.lineWidth = S * 0.03;
    ctx.beginPath();
    ctx.moveTo(-S * 0.12, -S * 1.18);
    ctx.quadraticCurveTo(-S * 0.25, -S * 1.25, -S * 0.22, -S * 1.38);
    ctx.moveTo(S * 0.12, -S * 1.18);
    ctx.quadraticCurveTo(S * 0.25, -S * 1.25, S * 0.22, -S * 1.38);
    ctx.stroke();

    // axe
    ctx.save();
    ctx.translate(S * 0.16, -S * 0.75);
    ctx.rotate(axeRot);
    ctx.strokeStyle = '#6b4a2b';
    ctx.lineWidth = S * 0.05;
    ctx.beginPath();
    ctx.moveTo(0, S * 0.15);
    ctx.lineTo(0, -S * 0.7);
    ctx.stroke();
    ctx.fillStyle = '#c9ced6';
    ctx.beginPath();
    ctx.moveTo(0, -S * 0.68);
    ctx.quadraticCurveTo(S * 0.34, -S * 0.72, S * 0.3, -S * 0.42);
    ctx.lineTo(0, -S * 0.5);
    ctx.closePath();
    ctx.fill();
    ctx.restore();

    if (tint) {
      ctx.globalCompositeOperation = 'source-atop';
      ctx.globalAlpha = 0.35;
      ctx.fillStyle = tint;
      ctx.fillRect(-S * 0.5, -S * 1.5, S, S * 1.6);
      ctx.globalAlpha = 1;
      ctx.globalCompositeOperation = 'source-over';
    }
    ctx.restore();

    if (guard > 0) {
      ctx.strokeStyle = COLORS.block;
      ctx.globalAlpha = guard;
      ctx.lineWidth = 5;
      ctx.beginPath();
      ctx.arc(x + S * 0.2, y - S * 0.7, S * 0.55, -0.9, 0.9);
      ctx.stroke();
      ctx.globalAlpha = 1;
    }

    // aura glow while big cooldowns run
    if (f.pa.some((a2) => a2.id === 'recklessness' || a2.id === 'flow_burst')) {
      const flow = f.pa.some((a2) => a2.id === 'flow_burst');
      ctx.globalAlpha = 0.18 + Math.sin(F.sceneT / 120) * 0.06;
      ctx.fillStyle = flow ? '#48e6c8' : '#ff5a2a';
      ctx.beginPath();
      ctx.ellipse(x, y - S * 0.7, S * 0.45, S * 0.85, 0, 0, TAU);
      ctx.fill();
      ctx.globalAlpha = 1;
    }

    if (staggered) {
      ctx.font = `800 ${L.font(14)}px ${UI_FONT}`;
      ctx.textAlign = 'center';
      ctx.fillStyle = '#ff6b6b';
      ctx.fillText('STAGGERED', x, y - S * 1.55);
    }
  }

  function drawBoss(ctx, L, f, t) {
    const S = L.S;
    const dummy = F.init.boss.dummy;
    let x = L.bossX;
    const y = L.groundY;
    const tg = activeTelegraph(f);
    let wind = 0;
    let lean = 0;
    let glow = null;

    if (tg) {
      const total = (tg.imp - tg.s) * MS_PER_TICK;
      const p = clamp((f.ms - tg.s * MS_PER_TICK) / total, 0, 1);
      wind = p;
      lean = -p * 0.18;
      glow = COLORS[tg.c] || COLORS.none;
    }
    let strike = 0;
    if (F.bAnim) {
      const p = clamp((F.sceneT - F.bAnim.start) / F.bAnim.dur, 0, 1);
      strike = Math.sin(p * Math.PI);
      if (p >= 1) F.bAnim = null;
    }
    x -= strike * S * 0.35;

    let fall = 0;
    let alpha = 1;
    if (F.endInfo && F.endInfo.reason === 'Killed') {
      const p = clamp((t - F.endInfo.t) / 900, 0, 1);
      fall = easeOut(p) * 1.45;
      alpha = 1 - p * 0.4;
    }

    shadow(ctx, x, y, S * (dummy ? 0.3 : 0.6));
    ctx.save();
    ctx.globalAlpha = alpha;
    ctx.translate(x, y);
    ctx.rotate(fall + lean - strike * 0.12);

    if (glow) {
      ctx.globalAlpha = 0.15 + wind * 0.45;
      const g = ctx.createRadialGradient(0, -S * 0.9, S * 0.1, 0, -S * 0.9, S * 1.25);
      g.addColorStop(0, glow);
      g.addColorStop(1, 'rgba(0,0,0,0)');
      ctx.fillStyle = g;
      ctx.beginPath();
      ctx.arc(0, -S * 0.9, S * 1.25, 0, TAU);
      ctx.fill();
      ctx.globalAlpha = alpha;
    }

    if (dummy) drawDummy(ctx, S, t);
    else drawBrute(ctx, S, wind, strike, f);

    if (t < F.bossFlashUntil) {
      ctx.globalCompositeOperation = 'source-atop';
      ctx.globalAlpha = 0.45;
      ctx.fillStyle = '#fff';
      ctx.fillRect(-S * 1.4, -S * 2.4, S * 2.8, S * 2.5);
      ctx.globalCompositeOperation = 'source-over';
    }
    ctx.restore();

    if (F.endInfo && F.endInfo.reason === 'Enraged') {
      ctx.fillStyle = 'rgba(255,0,0,0.12)';
      ctx.fillRect(0, 0, L.w, L.h);
    }
  }

  function drawDummy(ctx, S, t) {
    const wob = t < F.bossFlashUntil + 150 ? Math.sin(t / 25) * 0.04 : 0;
    ctx.rotate(wob);
    ctx.fillStyle = '#6b4a2b';
    ctx.fillRect(-S * 0.06, -S * 1.2, S * 0.12, S * 1.2);
    ctx.fillRect(-S * 0.45, -S * 0.95, S * 0.9, S * 0.1);
    ctx.fillStyle = '#c9a85c';
    roundRect(ctx, -S * 0.25, -S * 1.0, S * 0.5, S * 0.6, S * 0.1);
    ctx.fill();
    ctx.beginPath();
    ctx.arc(0, -S * 1.32, S * 0.2, 0, TAU);
    ctx.fill();
    ctx.strokeStyle = '#8a1d1d';
    ctx.lineWidth = S * 0.025;
    ctx.beginPath();
    ctx.arc(0, -S * 0.72, S * 0.14, 0, TAU);
    ctx.arc(0, -S * 0.72, S * 0.06, 0, TAU);
    ctx.stroke();
  }

  function drawBrute(ctx, S, wind, strike, f) {
    const skin = f.phase > 0 ? '#d9c3c0' : '#c9d1c8';
    const dark = '#7d8a83';
    // legs
    ctx.fillStyle = '#3a3330';
    roundRect(ctx, -S * 0.42, -S * 0.55, S * 0.3, S * 0.55, S * 0.08);
    ctx.fill();
    roundRect(ctx, S * 0.12, -S * 0.55, S * 0.3, S * 0.55, S * 0.08);
    ctx.fill();
    // body
    ctx.fillStyle = skin;
    ctx.beginPath();
    ctx.ellipse(0, -S * 1.05, S * 0.62, S * 0.62, 0, 0, TAU);
    ctx.fill();
    ctx.fillStyle = '#4a3b33';
    ctx.fillRect(-S * 0.55, -S * 0.65, S * 1.1, S * 0.18);
    // back arm (raises during wind-up)
    ctx.save();
    ctx.translate(S * 0.35, -S * 1.35);
    ctx.rotate(-0.4 - wind * 1.9 + strike * 2.4);
    ctx.fillStyle = dark;
    roundRect(ctx, -S * 0.13, 0, S * 0.26, S * 0.85, S * 0.12);
    ctx.fill();
    ctx.fillStyle = skin;
    ctx.beginPath();
    ctx.arc(0, S * 0.88, S * 0.19, 0, TAU);
    ctx.fill();
    ctx.restore();
    // head
    ctx.fillStyle = skin;
    ctx.beginPath();
    ctx.arc(-S * 0.18, -S * 1.68, S * 0.24, 0, TAU);
    ctx.fill();
    // eyes
    ctx.fillStyle = f.phase > 0 ? '#ff3b3b' : '#ffb347';
    ctx.beginPath();
    ctx.arc(-S * 0.28, -S * 1.72, S * 0.04, 0, TAU);
    ctx.arc(-S * 0.14, -S * 1.72, S * 0.04, 0, TAU);
    ctx.fill();
    // front arm (toward the player)
    ctx.save();
    ctx.translate(-S * 0.45, -S * 1.3);
    ctx.rotate(0.35 + wind * 0.5 - strike * 1.1);
    ctx.fillStyle = skin;
    roundRect(ctx, -S * 0.14, 0, S * 0.28, S * 0.9, S * 0.13);
    ctx.fill();
    ctx.beginPath();
    ctx.arc(0, S * 0.92, S * 0.2, 0, TAU);
    ctx.fill();
    ctx.restore();
  }

  function drawTelegraphs(ctx, L, f) {
    const T = L.tele;
    for (const tg of f.tg) {
      if (tg.done) continue;
      const color = COLORS[tg.c] || COLORS.none;
      const total = (tg.imp - tg.s) * MS_PER_TICK;
      const left = tg.imp * MS_PER_TICK - f.ms;
      const p = clamp(1 - left / total, 0, 1);
      const R = T.R;
      const R0 = R * 3.2;
      const radiusAt = (msBefore) => R + (R0 - R) * clamp(msBefore / total, 0, 1);

      // timing bands: normal (avoid) and perfect
      if (tg.c !== 'none') {
        ctx.fillStyle = hexA(color, 0.14);
        ring(ctx, T.x, T.y, R, radiusAt(tg.nw * MS_PER_TICK));
        ctx.fillStyle = hexA(COLORS.perfect, 0.32);
        ring(ctx, T.x, T.y, R, radiusAt(tg.pw * MS_PER_TICK));
      }

      // target
      ctx.fillStyle = hexA(color, 0.25 + p * 0.35);
      ctx.beginPath();
      ctx.arc(T.x, T.y, R, 0, TAU);
      ctx.fill();
      ctx.strokeStyle = color;
      ctx.lineWidth = 3;
      ctx.stroke();

      // closing ring
      const r = Math.max(R, radiusAt(left));
      ctx.strokeStyle = left <= tg.pw * MS_PER_TICK ? COLORS.perfect : color;
      ctx.lineWidth = 4 + p * 3;
      ctx.beginPath();
      ctx.arc(T.x, T.y, r, 0, TAU);
      ctx.stroke();

      ctx.textAlign = 'center';
      ctx.textBaseline = 'middle';
      ctx.font = `${Math.round(R * 0.9)}px ${EMOJI}`;
      ctx.fillStyle = '#fff';
      ctx.fillText(tg.c === 'block' ? '🛡️' : tg.c === 'dodge' ? '💨' : '💀', T.x, T.y + 1);

      ctx.font = `800 ${L.font(15)}px ${UI_FONT}`;
      ctx.fillStyle = color;
      const label = tg.c === 'block' ? 'BLOCK' : tg.c === 'dodge' ? 'DODGE' : 'BRACE';
      ctx.fillText(label, T.x, T.y + R0 + L.font(12));
      ctx.font = `600 ${L.font(13)}px ${UI_FONT}`;
      ctx.fillStyle = COLORS.text;
      ctx.fillText(tg.name, T.x, T.y - R0 - L.font(10));
      ctx.textBaseline = 'alphabetic';
    }
  }

  function ring(ctx, x, y, r1, r2) {
    ctx.beginPath();
    ctx.arc(x, y, r2, 0, TAU);
    ctx.arc(x, y, r1, 0, TAU, true);
    ctx.fill();
  }

  function drawFloaters(ctx, t) {
    F.floaters = F.floaters.filter((fl) => t - fl.born < fl.life);
    ctx.textAlign = 'center';
    for (const fl of F.floaters) {
      const p = (t - fl.born) / fl.life;
      if (fl.spark) {
        const s = (t - fl.born) / 1000;
        ctx.globalAlpha = 1 - p;
        ctx.fillStyle = fl.color;
        ctx.beginPath();
        ctx.arc(fl.x + fl.vx * s, fl.y + fl.vy * s, 3, 0, TAU);
        ctx.fill();
        continue;
      }
      const pop = p < 0.12 ? 1 + (0.12 - p) * 4 : 1;
      ctx.globalAlpha = p > 0.7 ? 1 - (p - 0.7) / 0.3 : 1;
      ctx.font = `800 ${Math.round(F.L.font(fl.size) * pop)}px ${UI_FONT}`;
      ctx.lineWidth = 3;
      ctx.strokeStyle = 'rgba(0,0,0,0.7)';
      const y = fl.y - p * 55;
      ctx.strokeText(fl.text, fl.x, y);
      ctx.fillStyle = fl.color;
      ctx.fillText(fl.text, fl.x, y);
    }
    ctx.globalAlpha = 1;
  }

  // ------------------------------------------------------------------- HUD

  function fmtTime(ms) {
    const s = Math.max(0, Math.ceil(ms / 1000));
    return Math.floor(s / 60) + ':' + String(s % 60).padStart(2, '0');
  }

  function drawHud(ctx, L, f, t) {
    const fs = L.font;
    const pad = L.pad;

    // Player frame (left), DPS meter (right), boss bar in between.
    const px = pad + 40;
    const pw = clamp(L.w * 0.2, 110, 280);
    const left = px + pw + 18;
    const right = L.w - pad - fs(78);
    const bw = Math.min(right - left, 620);
    const bx = clamp((L.w - bw) / 2, left, right - bw);
    const by = pad + fs(18);
    const bh = fs(16);
    ctx.textAlign = 'left';
    ctx.font = `700 ${fs(14)}px ${UI_FONT}`;
    ctx.fillStyle = COLORS.text;
    ctx.fillText(F.init.boss.name, bx, by - fs(5));
    ctx.textAlign = 'right';
    if (f.enrage > 0) {
      const left = f.enrage * MS_PER_TICK - f.ms;
      ctx.fillStyle = left < 20000 ? '#ff5a5a' : COLORS.text;
      ctx.fillText((F.init.boss.dummy ? '⏱ ' : 'Enrage ') + fmtTime(left), bx + bw, by - fs(5));
    }
    ctx.fillStyle = 'rgba(0,0,0,0.6)';
    roundRect(ctx, bx, by, bw, bh, 4);
    ctx.fill();
    const hpPct = f.maxHp > 0 ? f.hp / f.maxHp : 0;
    const hg = ctx.createLinearGradient(bx, 0, bx + bw, 0);
    hg.addColorStop(0, '#8e1b1b');
    hg.addColorStop(1, '#d4442a');
    ctx.fillStyle = hg;
    roundRect(ctx, bx, by, bw * hpPct, bh, 4);
    ctx.fill();
    ctx.strokeStyle = 'rgba(255,255,255,0.7)';
    ctx.lineWidth = 2;
    for (const mark of F.init.boss.phaseMarks) {
      const mx = bx + (bw * mark) / 100;
      ctx.beginPath();
      ctx.moveTo(mx, by - 2);
      ctx.lineTo(mx, by + bh + 2);
      ctx.stroke();
    }
    ctx.textAlign = 'center';
    ctx.font = `700 ${fs(11)}px ${UI_FONT}`;
    ctx.fillStyle = '#fff';
    ctx.fillText(
      F.init.boss.dummy ? f.dmg.toLocaleString() + ' dmg' : (hpPct * 100).toFixed(1) + '%  ·  ' + f.hp.toLocaleString(),
      bx + bw / 2,
      by + bh * 0.78
    );
    drawAuraRow(ctx, f.ba, bx, by + bh + 6, fs(22), f, 'left');

    // Player frame
    const py = pad + fs(18);
    ctx.textAlign = 'left';
    ctx.font = `700 ${fs(13)}px ${UI_FONT}`;
    ctx.fillStyle = COLORS.text;
    ctx.fillText(F.init.res.name, px, py - fs(5));
    ctx.fillStyle = 'rgba(0,0,0,0.6)';
    roundRect(ctx, px, py, pw, fs(14), 4);
    ctx.fill();
    ctx.fillStyle = F.init.res.color || '#e0452b';
    roundRect(ctx, px, py, (pw * f.res) / Math.max(1, f.resMax), fs(14), 4);
    ctx.fill();
    ctx.strokeStyle = 'rgba(255,255,255,0.25)';
    ctx.lineWidth = 1;
    ctx.beginPath();
    const m80 = px + pw * 0.8;
    ctx.moveTo(m80, py);
    ctx.lineTo(m80, py + fs(14));
    ctx.stroke();
    ctx.textAlign = 'center';
    ctx.font = `700 ${fs(11)}px ${UI_FONT}`;
    ctx.fillStyle = '#fff';
    ctx.fillText(String(f.res), px + pw / 2, py + fs(11));

    // flow pips, right-aligned above the resource bar
    if (!F.init.boss.dummy) {
      for (let i = 0; i < f.flowMax; i++) {
        ctx.fillStyle = i < f.flow ? '#48e6c8' : 'rgba(255,255,255,0.15)';
        ctx.beginPath();
        ctx.arc(px + pw - 4 - (f.flowMax - 1 - i) * 11, py - fs(9), 3.5, 0, TAU);
        ctx.fill();
      }
    }
    drawAuraRow(ctx, f.pa, px, py + fs(14) + 6, fs(22), f, 'left');

    // DPS meter
    ctx.textAlign = 'right';
    ctx.font = `800 ${fs(20)}px ${UI_FONT}`;
    ctx.fillStyle = '#ffd24a';
    ctx.fillText(f.dps.toLocaleString(), L.w - pad, pad + fs(18));
    ctx.font = `600 ${fs(11)}px ${UI_FONT}`;
    ctx.fillStyle = COLORS.dim;
    ctx.fillText('DPS · ' + fmtTime(f.ms), L.w - pad, pad + fs(32));

    // pause button
    const pb = L.pauseBtn;
    ctx.fillStyle = 'rgba(255,255,255,0.12)';
    ctx.beginPath();
    ctx.arc(pb.x, pb.y, pb.r, 0, TAU);
    ctx.fill();
    ctx.fillStyle = COLORS.text;
    ctx.fillRect(pb.x - 5, pb.y - 6, 3.5, 12);
    ctx.fillRect(pb.x + 1.5, pb.y - 6, 3.5, 12);
  }

  function drawAuraRow(ctx, auras, x, y, size, f) {
    let i = 0;
    for (const a of auras) {
      const ax = x + i * (size + 4);
      ctx.fillStyle = 'rgba(0,0,0,0.55)';
      roundRect(ctx, ax, y, size, size, 4);
      ctx.fill();
      ctx.strokeStyle = a.color || '#888';
      ctx.lineWidth = 2;
      ctx.stroke();
      if (a.e > 0) {
        const total = (a.e - a.s) * MS_PER_TICK;
        const left = a.e * MS_PER_TICK - f.ms;
        const k = clamp(left / total, 0, 1);
        ctx.fillStyle = 'rgba(0,0,0,0.5)';
        ctx.fillRect(ax, y, size, size * (1 - k));
        ctx.font = `700 ${Math.round(size * 0.38)}px ${UI_FONT}`;
        ctx.textAlign = 'center';
        ctx.fillStyle = '#fff';
        ctx.fillText(Math.max(0, Math.ceil(left / 1000)), ax + size / 2, y + size + size * 0.42);
      }
      ctx.textAlign = 'center';
      ctx.textBaseline = 'middle';
      ctx.font = `${Math.round(size * 0.6)}px ${EMOJI}`;
      ctx.fillText(a.icon || '•', ax + size / 2, y + size / 2 + 1);
      ctx.textBaseline = 'alphabetic';
      if (a.st > 1) {
        ctx.font = `800 ${Math.round(size * 0.4)}px ${UI_FONT}`;
        ctx.textAlign = 'right';
        ctx.fillStyle = '#fff';
        ctx.fillText(a.st, ax + size - 1, y + size - 2);
      }
      i++;
    }
  }

  // --------------------------------------------------------------- buttons

  function drawButtons(ctx, L, f, t) {
    const keys = F.init.settings.showKeys && !F.coarse;
    const tick = f.ms / MS_PER_TICK;

    f.bar.forEach((b, i) => drawAbility(ctx, L.slots[i], b, f, t, tick, keys ? KEY_LABELS[i] : null, true));
    f.cds.forEach((b, i) => drawAbility(ctx, L.cds[i], b, f, t, tick, keys ? KEY_LABELS[6 + i] : null, false));

    // swap
    const s = L.swap;
    const held = f.barB;
    if (f.swapGlow) glowRing(ctx, s, t, '#ffd24a');
    ctx.fillStyle = held ? '#3d3360' : '#232330';
    ctx.beginPath();
    ctx.arc(s.x, s.y, s.r, 0, TAU);
    ctx.fill();
    ctx.strokeStyle = held ? '#a99cff' : 'rgba(255,255,255,0.35)';
    ctx.lineWidth = 2;
    ctx.stroke();
    ctx.textAlign = 'center';
    ctx.textBaseline = 'middle';
    ctx.font = `800 ${Math.round(s.r * 0.62)}px ${UI_FONT}`;
    ctx.fillStyle = '#fff';
    ctx.fillText(held ? 'B' : 'A', s.x, s.y - s.r * 0.12);
    ctx.font = `600 ${Math.round(s.r * 0.3)}px ${UI_FONT}`;
    ctx.fillStyle = COLORS.dim;
    ctx.fillText('SWAP', s.x, s.y + s.r * 0.45);
    ctx.textBaseline = 'alphabetic';
    if (keys) keyHint(ctx, s, 'Q');

    // defense
    const tg = activeTelegraph(f);
    let hint = null;
    if (tg && tg.c !== 'none') {
      const left = tg.imp * MS_PER_TICK - f.ms;
      if (left <= tg.nw * MS_PER_TICK + 250) hint = tg.c;
    }
    drawDefense(ctx, L.block, 'block', '🛡️', 'BLOCK', hint === 'block', t, keys ? 'Space' : null);
    drawDefense(ctx, L.dodge, 'dodge', '💨', 'DODGE', hint === 'dodge', t, keys ? 'Shift' : null);
  }

  function glowRing(ctx, b, t, color) {
    const pulse = 0.5 + 0.5 * Math.sin(t / 110);
    ctx.save();
    ctx.shadowColor = color;
    ctx.shadowBlur = 18 + pulse * 10;
    ctx.strokeStyle = color;
    ctx.lineWidth = 3 + pulse * 2;
    ctx.beginPath();
    ctx.arc(b.x, b.y, b.r + 4, 0, TAU);
    ctx.stroke();
    ctx.restore();
  }

  function keyHint(ctx, b, label) {
    ctx.font = `700 ${Math.max(9, Math.round(b.r * 0.28))}px ${UI_FONT}`;
    ctx.textAlign = 'center';
    const w = ctx.measureText(label).width + 6;
    const x = b.x + b.r * 0.72;
    const y = b.y - b.r * 0.72;
    ctx.fillStyle = 'rgba(0,0,0,0.75)';
    roundRect(ctx, x - w / 2, y - 8, w, 15, 3);
    ctx.fill();
    ctx.fillStyle = '#ddd';
    ctx.fillText(label, x, y + 3);
  }

  function drawAbility(ctx, pos, b, f, t, tick, key, showName) {
    if (!b) return;
    const a = F.init.abilities[b.id] || {};
    const r = pos.r;

    if (b.glow) glowRing(ctx, pos, t, '#ffd24a');

    ctx.fillStyle = '#1d1b24';
    ctx.beginPath();
    ctx.arc(pos.x, pos.y, r, 0, TAU);
    ctx.fill();
    ctx.fillStyle = hexA(a.color || '#666', b.ok ? 0.55 : 0.18);
    ctx.beginPath();
    ctx.arc(pos.x, pos.y, r * 0.92, 0, TAU);
    ctx.fill();

    ctx.textAlign = 'center';
    ctx.textBaseline = 'middle';
    ctx.globalAlpha = b.ok ? 1 : 0.4;
    ctx.font = `${Math.round(r * 0.85)}px ${EMOJI}`;
    ctx.fillText(a.icon || '?', pos.x, pos.y + 1);
    ctx.globalAlpha = 1;
    ctx.textBaseline = 'alphabetic';

    // cooldown sweep (charges)
    let cdLeft = 0;
    if (b.ch === 0 && b.re > 0) {
      const total = b.re - b.rs;
      const k = clamp((b.re - tick) / total, 0, 1);
      cdLeft = (b.re - tick) * MS_PER_TICK;
      sweep(ctx, pos.x, pos.y, r * 0.92, k, 'rgba(0,0,0,0.68)');
    } else if (a.onGcd && tick < f.gcdE) {
      const k = clamp((f.gcdE - tick) / Math.max(1, f.gcdE - f.gcdS), 0, 1);
      sweep(ctx, pos.x, pos.y, r * 0.92, k, 'rgba(0,0,0,0.45)');
    }
    if (b.ch < b.mx && b.ch > 0 && b.re > 0) {
      // recharging an extra charge: thin arc
      const k = clamp(1 - (b.re - tick) / (b.re - b.rs), 0, 1);
      ctx.strokeStyle = 'rgba(255,255,255,0.75)';
      ctx.lineWidth = 3;
      ctx.beginPath();
      ctx.arc(pos.x, pos.y, r - 2, -Math.PI / 2, -Math.PI / 2 + TAU * k);
      ctx.stroke();
    }

    if (cdLeft > 0) {
      ctx.font = `800 ${Math.round(r * 0.5)}px ${UI_FONT}`;
      ctx.textAlign = 'center';
      ctx.lineWidth = 3;
      ctx.strokeStyle = 'rgba(0,0,0,0.8)';
      const txt = cdLeft >= 10000 ? String(Math.ceil(cdLeft / 1000)) : (cdLeft / 1000).toFixed(1);
      ctx.strokeText(txt, pos.x, pos.y + r * 0.18);
      ctx.fillStyle = '#fff';
      ctx.fillText(txt, pos.x, pos.y + r * 0.18);
    }

    ctx.strokeStyle = b.q ? '#ffffff' : hexA(a.color || '#888', 0.9);
    ctx.lineWidth = b.q ? 3.5 : 2;
    ctx.beginPath();
    ctx.arc(pos.x, pos.y, r, 0, TAU);
    ctx.stroke();

    if (b.mx > 1) {
      ctx.font = `800 ${Math.round(r * 0.36)}px ${UI_FONT}`;
      ctx.textAlign = 'right';
      ctx.fillStyle = '#fff';
      ctx.fillText(String(b.ch), pos.x + r * 0.85, pos.y + r * 0.9);
    }
    if (a.cost > 0) {
      ctx.font = `700 ${Math.max(9, Math.round(r * 0.27))}px ${UI_FONT}`;
      ctx.textAlign = 'left';
      ctx.fillStyle = b.ok ? '#ffb199' : '#ff5a5a';
      ctx.fillText(String(a.cost), pos.x - r * 0.9, pos.y + r * 0.9);
    }
    if (showName && F.L.m >= 520) {
      ctx.font = `600 ${Math.max(9, Math.round(r * 0.26))}px ${UI_FONT}`;
      ctx.textAlign = 'center';
      ctx.fillStyle = 'rgba(255,255,255,0.75)';
      ctx.fillText(a.name || b.id, pos.x, pos.y - r - 5);
    }
    if (key) keyHint(ctx, pos, key);
  }

  function drawDefense(ctx, b, type, icon, label, hint, t, key) {
    const color = COLORS[type];
    const since = now() - F.defPress[type];
    const pressed = since < 140;
    if (hint) glowRing(ctx, b, t, color);
    ctx.fillStyle = pressed ? hexA(color, 0.55) : '#1d1b24';
    ctx.beginPath();
    ctx.arc(b.x, b.y, b.r * (pressed ? 0.95 : 1), 0, TAU);
    ctx.fill();
    ctx.strokeStyle = color;
    ctx.lineWidth = 3;
    ctx.stroke();
    ctx.textAlign = 'center';
    ctx.textBaseline = 'middle';
    ctx.font = `${Math.round(b.r * 0.62)}px ${EMOJI}`;
    ctx.fillText(icon, b.x, b.y - b.r * 0.1);
    ctx.font = `800 ${Math.round(b.r * 0.24)}px ${UI_FONT}`;
    ctx.fillStyle = color;
    ctx.fillText(label, b.x, b.y + b.r * 0.55);
    ctx.textBaseline = 'alphabetic';
    if (key) keyHint(ctx, b, key);
  }

  function sweep(ctx, x, y, r, k, color) {
    if (k <= 0) return;
    ctx.fillStyle = color;
    ctx.beginPath();
    ctx.moveTo(x, y);
    ctx.arc(x, y, r, -Math.PI / 2, -Math.PI / 2 + TAU * k);
    ctx.closePath();
    ctx.fill();
  }

  // ------------------------------------------------------- banners & toasts

  function drawBanners(ctx, L, t) {
    const n = now();
    F.banners = F.banners.filter((b) => n - b.born < b.life);
    const slots = { center: 0, tele: 0, player: 0 };
    for (const b of F.banners) {
      const p = (n - b.born) / b.life;
      const pop = p < 0.15 ? 0.6 + (p / 0.15) * 0.4 : 1;
      const idx = slots[b.where]++;
      let x = L.w / 2;
      let y = L.h * 0.3;
      if (b.where === 'tele') {
        x = L.tele.x;
        y = L.tele.y + L.font(8) + idx * L.font(26);
      } else if (b.where === 'player') {
        x = L.playerX;
        y = L.groundY - L.S * 1.75 - idx * L.font(18) - p * 20;
      } else {
        y = L.h * 0.3 + idx * L.font(30);
      }
      ctx.globalAlpha = p > 0.75 ? 1 - (p - 0.75) / 0.25 : 1;
      ctx.font = `900 ${Math.round(L.font(22) * b.scale * pop)}px ${UI_FONT}`;
      ctx.textAlign = 'center';
      ctx.lineWidth = 4;
      ctx.strokeStyle = 'rgba(0,0,0,0.75)';
      ctx.strokeText(b.text, x, y);
      ctx.fillStyle = b.color;
      ctx.fillText(b.text, x, y);
    }
    ctx.globalAlpha = 1;
  }

  function drawToasts(ctx, L, t) {
    const n = now();
    F.toasts = F.toasts.filter((x) => n - x.born < x.life);
    const s = L.slots[1];
    F.toasts.forEach((x, i) => {
      const p = (n - x.born) / x.life;
      ctx.globalAlpha = p > 0.6 ? 1 - (p - 0.6) / 0.4 : 1;
      ctx.font = `700 ${L.font(13)}px ${UI_FONT}`;
      ctx.textAlign = 'center';
      ctx.fillStyle = '#ffb3b3';
      ctx.fillText(x.text, s.x - L.a * 1.6, s.y - L.a * 2.6 - i * L.font(16));
    });
    ctx.globalAlpha = 1;
  }

  function drawCountdown(ctx, L, t) {
    const k = (t - F.countdownStart) / 600;
    const n = 3 - Math.floor(k);
    if (n < 1) return;
    const p = k % 1;
    ctx.fillStyle = 'rgba(0,0,0,0.35)';
    ctx.fillRect(0, 0, L.w, L.h);
    ctx.globalAlpha = 1 - p * 0.6;
    ctx.font = `900 ${Math.round(L.font(64) * (1.3 - p * 0.3))}px ${UI_FONT}`;
    ctx.textAlign = 'center';
    ctx.fillStyle = '#fff';
    ctx.fillText(String(n), L.w / 2, L.h * 0.42);
    ctx.globalAlpha = 1;
    ctx.font = `600 ${L.font(14)}px ${UI_FONT}`;
    ctx.fillStyle = COLORS.dim;
    ctx.fillText(F.init.boss.name + (F.init.boss.title ? ' — ' + F.init.boss.title : ''), L.w / 2, L.h * 0.52);
  }

  function drawRotateHint(ctx, L) {
    ctx.font = `600 ${L.font(12)}px ${UI_FONT}`;
    ctx.textAlign = 'center';
    ctx.fillStyle = '#ffd24a';
    ctx.fillText('↻ Rotate to landscape for the best layout', L.w / 2, L.h * 0.5);
  }

  // ------------------------------------------------------------- helpers

  function roundRect(ctx, x, y, w, h, r) {
    w = Math.max(0, w);
    r = Math.min(r, w / 2, h / 2);
    ctx.beginPath();
    ctx.moveTo(x + r, y);
    ctx.arcTo(x + w, y, x + w, y + h, r);
    ctx.arcTo(x + w, y + h, x, y + h, r);
    ctx.arcTo(x, y + h, x, y, r);
    ctx.arcTo(x, y, x + w, y, r);
    ctx.closePath();
  }

  function hexA(hex, a) {
    if (!hex || hex[0] !== '#') return `rgba(136,136,136,${a})`;
    const h = hex.length === 4 ? hex.replace(/#(.)(.)(.)/, '#$1$1$2$2$3$3') : hex;
    const n = parseInt(h.slice(1, 7), 16);
    return `rgba(${(n >> 16) & 255},${(n >> 8) & 255},${n & 255},${a})`;
  }

  // ------------------------------------------------------------ calibration

  // Metronome calibration: tap on each beat; the median lateness of your taps
  // (after the count-in) becomes the input offset.
  const Cal = { running: false, beats: [], taps: [], ref: null, pad: null, timer: 0, onKey: null, onTap: null, ctx: null };

  RR.calibrate = {
    start(padId, dotnetRef, count, intervalMs) {
      RR.calibrate.stop();
      const ctx = Sfx.ensure() || new (window.AudioContext || window.webkitAudioContext)();
      Cal.ctx = ctx;
      Cal.ref = dotnetRef;
      Cal.pad = document.getElementById(padId);
      Cal.running = true;
      Cal.taps = [];
      Cal.beats = [];

      const startCtx = ctx.currentTime + 0.6;
      for (let i = 0; i < count; i++) {
        const when = startCtx + (i * intervalMs) / 1000;
        const osc = ctx.createOscillator();
        const g = ctx.createGain();
        osc.frequency.value = i < 4 ? 660 : 1000;
        g.gain.setValueAtTime(0.25, when);
        g.gain.exponentialRampToValueAtTime(0.0001, when + 0.05);
        osc.connect(g).connect(ctx.destination);
        osc.start(when);
        osc.stop(when + 0.06);
        Cal.beats.push(when);
      }

      const toPerf = (ctxTime) => {
        // Map audio-clock time to performance.now() using the output timestamp
        // (accounts for output latency where the browser reports it).
        if (ctx.getOutputTimestamp) {
          const ts = ctx.getOutputTimestamp();
          if (ts && ts.performanceTime) return ts.performanceTime + (ctxTime - ts.contextTime) * 1000;
        }
        return now() + (ctxTime - ctx.currentTime - (ctx.outputLatency || 0)) * 1000;
      };

      const dot = Cal.pad.querySelector('.calib-dot');
      const flashLoop = () => {
        if (!Cal.running) return;
        const t = now();
        const near = Cal.beats.some((b) => Math.abs(toPerf(b) - t) < 60);
        if (dot) dot.classList.toggle('on', near);
        Cal.timer = requestAnimationFrame(flashLoop);
      };
      Cal.timer = requestAnimationFrame(flashLoop);

      const tap = (time) => {
        if (!Cal.running) return;
        Cal.taps.push(time);
        Cal.pad.classList.add('tapped');
        setTimeout(() => Cal.pad && Cal.pad.classList.remove('tapped'), 80);
      };
      Cal.onTap = (e) => { e.preventDefault(); tap(eventTime(e)); };
      Cal.onKey = (e) => {
        if (e.code !== 'Space' || e.repeat) return;
        e.preventDefault();
        tap(eventTime(e));
      };
      Cal.pad.addEventListener('pointerdown', Cal.onTap);
      window.addEventListener('keydown', Cal.onKey, true);

      const endAt = (count * intervalMs) + 1200;
      setTimeout(() => {
        if (!Cal.running) return;
        const beatsPerf = Cal.beats.slice(4).map(toPerf);
        const offsets = [];
        for (const tapT of Cal.taps) {
          let best = null;
          for (const b of beatsPerf) if (best === null || Math.abs(tapT - b) < Math.abs(tapT - best)) best = b;
          if (best !== null && Math.abs(tapT - best) < intervalMs / 2) offsets.push(tapT - best);
        }
        offsets.sort((a, b) => a - b);
        const median = offsets.length ? offsets[Math.floor(offsets.length / 2)] : 0;
        const ref = Cal.ref;
        RR.calibrate.stop();
        ref.invokeMethodAsync('OnCalibrated', median, offsets.length);
      }, endAt);
    },

    stop() {
      Cal.running = false;
      cancelAnimationFrame(Cal.timer);
      if (Cal.pad && Cal.onTap) Cal.pad.removeEventListener('pointerdown', Cal.onTap);
      if (Cal.onKey) window.removeEventListener('keydown', Cal.onKey, true);
      Cal.onTap = null;
      Cal.onKey = null;
    },
  };
})();
