// The desktop's 3D centerpiece orbital graphic: a clean, minimalist 3D luminous orbit around
// the beveled glass center medallion — a primary sculpted track carrying a glowing energy dot
// with a fading comet trail, a subtle counter-tilted outer orbit with a pinpoint satellite, and
// soft background starfield particles. Speeds up gently with live CPU/GPU activity and pulses on launch.
import * as THREE from './vendor/three.module.min.js';

export function createCore(canvas) {
  const reduced = matchMedia('(prefers-reduced-motion: reduce)').matches;
  const renderer = new THREE.WebGLRenderer({ canvas, antialias: true, alpha: true, premultipliedAlpha: true });
  renderer.setClearColor(0x000000, 0);
  // Keep pixel ratio at 1 on high-res displays so the full-screen WebGL canvas stays lightweight on GPU bandwidth.
  const dpr = 1;
  renderer.setPixelRatio(dpr);

  const scene = new THREE.Scene();
  const FOV = 30;
  const camera = new THREE.PerspectiveCamera(FOV, 1, 1, 20000);
  let W = 1, H = 1;

  // ---- colours (lerped toward targets so theme changes crossfade) ----
  const accent = new THREE.Color('#5CC8FF'), accentTarget = accent.clone();
  const coreCol = new THREE.Color('#E8F7FF'), coreTarget = coreCol.clone();
  const tinted = [];
  const coreTinted = [];

  function radialTexture(size, stops) {
    const c = document.createElement('canvas');
    c.width = c.height = size;
    const g = c.getContext('2d');
    const grad = g.createRadialGradient(size / 2, size / 2, 0, size / 2, size / 2, size / 2);
    for (const [o, a] of stops) grad.addColorStop(o, `rgba(255,255,255,${a})`);
    g.fillStyle = grad;
    g.fillRect(0, 0, size, size);
    const t = new THREE.CanvasTexture(c);
    t.colorSpace = THREE.SRGBColorSpace;
    return t;
  }

  function makeMat(opacity, additive = true, useCore = false) {
    const m = new THREE.MeshBasicMaterial({
      color: (useCore ? coreCol : accent).clone(),
      transparent: true, opacity, depthWrite: false, side: THREE.DoubleSide,
      blending: additive ? THREE.AdditiveBlending : THREE.NormalBlending,
      premultipliedAlpha: true,
    });
    (useCore ? coreTinted : tinted).push(m);
    return m;
  }

  const core = new THREE.Group();
  scene.add(core);

  // Soft ambient halo behind the 3D glass medallion
  const glowMat = new THREE.MeshBasicMaterial({
    map: radialTexture(128, [[0, 0.72], [0.35, 0.30], [0.7, 0.08], [1, 0]]),
    color: accent.clone(), transparent: true, opacity: 0.24, depthWrite: false,
    blending: THREE.AdditiveBlending, premultipliedAlpha: true,
  });
  tinted.push(glowMat);
  const glow = new THREE.Mesh(new THREE.PlaneGeometry(540, 540), glowMat);
  glow.position.z = -50;
  core.add(glow);

  const tilt = new THREE.Group(); // pointer parallax
  core.add(tilt);

  // ---- Primary 3D orbit (clean sculpted ring + glowing dot + fading comet trail) ----
  const primaryPlane = new THREE.Group();
  tilt.add(primaryPlane);

  const R1 = 154;
  // Crisp thin 3D torus track
  const primaryTrack = new THREE.Mesh(new THREE.TorusGeometry(R1, 1.0, 12, 160), makeMat(0.32));
  primaryPlane.add(primaryTrack);

  // Rotating carrier holding the glowing dot and its fading tail
  const orbiter1 = new THREE.Group();
  primaryPlane.add(orbiter1);

  const dotCore = new THREE.Mesh(new THREE.SphereGeometry(4.2, 20, 16), makeMat(0.95, true, true));
  dotCore.position.set(R1, 0, 0);
  orbiter1.add(dotCore);

  const dotGlowMat = new THREE.SpriteMaterial({
    map: radialTexture(96, [[0, 1], [0.22, 0.65], [0.55, 0.18], [1, 0]]),
    color: accent.clone(), transparent: true, opacity: 0.92, depthWrite: false,
    blending: THREE.AdditiveBlending,
  });
  tinted.push(dotGlowMat);
  const dotGlow = new THREE.Sprite(dotGlowMat);
  dotGlow.position.set(R1, 0, 0);
  dotGlow.scale.set(46, 46, 1);
  orbiter1.add(dotGlow);

  // Fading luminous comet trail trailing behind the dot along R1
  const TRAIL_SEGS = 18;
  const TRAIL_ARC = 1.05; // radians (~60 deg)
  for (let i = 0; i < TRAIL_SEGS; i++) {
    const t0 = i / TRAIL_SEGS;
    const segLen = TRAIL_ARC / TRAIL_SEGS;
    const startAngle = -((i + 1) * segLen);
    const width = (1 - t0) * 2.6 + 0.5;
    const alpha = Math.pow(1 - t0, 1.9) * 0.62;
    const seg = new THREE.Mesh(
      new THREE.RingGeometry(R1 - width / 2, R1 + width / 2, 10, 1, startAngle, segLen * 1.08),
      makeMat(alpha),
    );
    orbiter1.add(seg);
  }

  // ---- Secondary whisper-thin counter-orbit (gives 3D depth without clutter) ----
  const secondaryPlane = new THREE.Group();
  tilt.add(secondaryPlane);

  const R2 = 186;
  const secondaryTrack = new THREE.Mesh(new THREE.TorusGeometry(R2, 0.65, 10, 160), makeMat(0.14));
  secondaryPlane.add(secondaryTrack);

  const orbiter2 = new THREE.Group();
  secondaryPlane.add(orbiter2);

  const dot2 = new THREE.Mesh(new THREE.SphereGeometry(2.4, 14, 12), makeMat(0.72, true, true));
  dot2.position.set(R2, 0, 0);
  orbiter2.add(dot2);

  const dot2GlowMat = new THREE.SpriteMaterial({
    map: radialTexture(64, [[0, 0.9], [0.35, 0.35], [1, 0]]),
    color: accent.clone(), transparent: true, opacity: 0.58, depthWrite: false,
    blending: THREE.AdditiveBlending,
  });
  tinted.push(dot2GlowMat);
  const dot2Glow = new THREE.Sprite(dot2GlowMat);
  dot2Glow.position.set(R2, 0, 0);
  dot2Glow.scale.set(24, 24, 1);
  orbiter2.add(dot2Glow);

  // ---- Subtle drifting background particles ----
  const PCOUNT = 140;
  const pPos = new Float32Array(PCOUNT * 3);
  const pSpeed = new Float32Array(PCOUNT);
  const pPhase = new Float32Array(PCOUNT);
  const pGeo = new THREE.BufferGeometry();
  pGeo.setAttribute('position', new THREE.BufferAttribute(pPos, 3));
  const pMat = new THREE.PointsMaterial({
    size: 2.2 * dpr, sizeAttenuation: false, map: radialTexture(32, [[0, 1], [0.4, 0.6], [1, 0]]),
    color: accent.clone(), transparent: true, opacity: 0.32, depthWrite: false,
    blending: THREE.AdditiveBlending, premultipliedAlpha: true,
  });
  tinted.push(pMat);
  const particles = new THREE.Points(pGeo, pMat);
  scene.add(particles);

  function seedParticles() {
    for (let i = 0; i < PCOUNT; i++) {
      pPos[i * 3] = (Math.random() - 0.5) * W * 1.2;
      pPos[i * 3 + 1] = (Math.random() - 0.5) * H * 1.2;
      pPos[i * 3 + 2] = -700 + Math.random() * 800;
      pSpeed[i] = 5 + Math.random() * 11;
      pPhase[i] = Math.random() * Math.PI * 2;
    }
    pGeo.attributes.position.needsUpdate = true;
  }

  function resize() {
    W = innerWidth; H = innerHeight;
    renderer.setSize(W, H, false);
    camera.aspect = W / H;
    camera.position.set(0, 0, (H / 2) / Math.tan(THREE.MathUtils.degToRad(FOV / 2)));
    camera.updateProjectionMatrix();
    seedParticles();
  }
  addEventListener('resize', resize);
  resize();

  // ---- state ----
  let idle = false, throttled = false, pulse = 0, last = 0, time = 0, alive = true;
  let activity = 0, activityTarget = 0, brightness = 0.5;
  const ptr = { x: 0.5, y: 0.5 }, look = { x: 0, y: 0 };

  function syncColors(k) {
    accent.lerp(accentTarget, k);
    coreCol.lerp(coreTarget, k);
    for (const m of tinted) m.color.copy(accent);
    for (const m of coreTinted) m.color.copy(coreCol);
  }
  syncColors(1);

  function update(dt) {
    activity += (activityTarget - activity) * Math.min(1, dt * 0.8);
    const speed = (reduced ? 0.3 : 1) * (1 + pulse * 2.2 + activity * 1.6);
    time += dt;
    pulse = Math.max(0, pulse - dt * 0.9);

    look.x += ((ptr.x - 0.5) * 0.28 - look.x) * Math.min(1, dt * 2.5);
    look.y += ((ptr.y - 0.5) * 0.20 - look.y) * Math.min(1, dt * 2.5);
    tilt.rotation.y = look.x;
    tilt.rotation.x = look.y;

    // Gentle 3D orbital precession + glowing satellite rotation
    primaryPlane.rotation.x = 0.34 + 0.08 * Math.sin(time * 0.25);
    primaryPlane.rotation.y = -0.18 + 0.08 * Math.cos(time * 0.21);
    orbiter1.rotation.z += 0.82 * dt * speed;
    const flare = 46 * (1 + 0.14 * Math.sin(time * 2.4) + pulse * 0.45);
    dotGlow.scale.set(flare, flare, 1);

    secondaryPlane.rotation.x = -0.26 + 0.07 * Math.cos(time * 0.19);
    secondaryPlane.rotation.y = 0.24 + 0.07 * Math.sin(time * 0.17);
    orbiter2.rotation.z -= 0.36 * dt * speed;

    const breathe = 1 + 0.035 * Math.sin(time * (1.2 + activity)) + pulse * 0.08;
    glow.scale.setScalar(breathe);
    glowMat.opacity = (0.20 + 0.04 * Math.sin(time * 1.2) + pulse * 0.18 + activity * 0.06) * (0.4 + 0.6 * brightness);

    for (let i = 0; i < PCOUNT; i++) {
      const j = i * 3;
      pPos[j + 1] += pSpeed[i] * dt * (reduced ? 0.3 : 1);
      pPos[j] += Math.sin(time * 0.3 + pPhase[i]) * 3.5 * dt;
      if (pPos[j + 1] > H * 0.62) { pPos[j + 1] = -H * 0.62; pPos[j] = (Math.random() - 0.5) * W * 1.2; }
    }
    pGeo.attributes.position.needsUpdate = true;
    particles.rotation.y = look.x * 0.12;
    particles.rotation.x = look.y * 0.12;

    if (!accent.equals(accentTarget) || !coreCol.equals(coreTarget)) syncColors(Math.min(1, dt * 4));
  }

  function frame(t) {
    if (!alive) return;
    requestAnimationFrame(frame);
    // Step down FPS when idle or throttled so GPU usage stays minimal.
    const fps = throttled ? 15 : (idle ? 15 : 60);
    const minDt = 1000 / fps;
    if (last && t - last < minDt - 1) return;
    const dt = last ? Math.min((t - last) / 1000, 0.1) : 0.016;
    last = t;
    update(dt);
    renderer.render(scene, camera);
  }
  requestAnimationFrame(frame);

  canvas.addEventListener('webglcontextlost', (e) => {
    e.preventDefault();
    alive = false;
  });
  canvas.addEventListener('webglcontextrestored', () => {
    if (alive) return;
    alive = true;
    last = 0;
    resize();
    syncColors(1);
    requestAnimationFrame(frame);
  });

  return {
    setCore(x, y, scale) {
      core.position.set(x - W / 2, H / 2 - y, 0);
      core.scale.setScalar(scale);
    },
    setColors(accentCss, coreCss) {
      try { accentTarget.set(accentCss); } catch { /* keep current */ }
      try { coreTarget.set(coreCss); } catch { /* keep current */ }
    },
    setActivity(v) { activityTarget = Math.max(0, Math.min(1, v || 0)); },
    setBrightness(v) {
      const n = Number(v);
      brightness = Number.isFinite(n) ? Math.max(0, Math.min(1, n)) : 0.5;
    },
    setIdle(v) { idle = v; },
    setThrottled(v) { throttled = !!v; },
    pointer(x, y) { ptr.x = x; ptr.y = y; },
    pulse() { pulse = 1; },
  };
}
