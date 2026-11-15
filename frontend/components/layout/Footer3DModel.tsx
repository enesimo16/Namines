'use client';

import { useEffect, useRef, useState } from 'react';
import { useHomeThemeStore } from '../../store/useHomeThemeStore';

interface Vec3 {
  x: number;
  y: number;
  z: number;
}

interface Edge {
  a: number;
  b: number;
  color: 'frame' | 'mint' | 'indigo' | 'subtle';
  width: number;
}

/**
 * Namines Footer için interaktif gerçek-zamanlı 3D Mimari Şema Kafesi (Strata Vault).
 *
 * Dışa dönük ağır WebGL bağımlılığı eklemeden 60fps HTML5 Canvas üzerinde
 * perspektif projeksiyonlu, derinlik sıralamalı (painter's algorithm),
 * fare paralakslı ve sürükleyerek döndürülebilen (drag-to-orbit) bir
 * 3D çok katmanlı şema çekirdeği çizer.
 */
export default function Footer3DModel() {
  const canvasRef = useRef<HTMLCanvasElement | null>(null);
  const { theme } = useHomeThemeStore();
  const [isDragging, setIsDragging] = useState(false);

  const rotationRef = useRef({
    rx: 0.42,
    ry: -0.58,
    vx: 0,
    vy: 0.0045,
    pointerX: 0,
    pointerY: 0,
    dragging: false,
    lastX: 0,
    lastY: 0,
  });

  useEffect(() => {
    const canvas = canvasRef.current;
    if (!canvas) return;
    const ctx = canvas.getContext('2d');
    if (!ctx) return;

    const prefersReducedMotion =
      typeof window !== 'undefined' &&
      window.matchMedia('(prefers-reduced-motion: reduce)').matches;

    // 1. Geometri Tanımı: Pahlı Dış Kasa + 3 Katmanlı IR Çekirdeği + Oktahedron Çekirdek
    const vertices: Vec3[] = [];
    const edges: Edge[] = [];

    // Pahlı küp köşeleri (outer chamfered vault) - 8 köşe biraz içe kırılmış + merkez halkalar
    const s = 1.05;
    const c = 0.72;
    // Dış küp (0..7)
    const cubeSigns = [
      [-1, -1, -1], [1, -1, -1], [1, 1, -1], [-1, 1, -1],
      [-1, -1, 1], [1, -1, 1], [1, 1, 1], [-1, 1, 1],
    ];
    cubeSigns.forEach(([sx, sy, sz]) => {
      vertices.push({ x: sx * s, y: sy * s, z: sz * s });
    });

    const cubeEdges = [
      [0, 1], [1, 2], [2, 3], [3, 0],
      [4, 5], [5, 6], [6, 7], [7, 4],
      [0, 4], [1, 5], [2, 6], [3, 7],
    ];
    cubeEdges.forEach(([a, b]) => edges.push({ a, b, color: 'subtle', width: 1 }));

    // İç Oktahedron (8..13) — Deterministic IR Core
    const octBase = vertices.length;
    vertices.push(
      { x: 0, y: -c, z: 0 },
      { x: c, y: 0, z: 0 },
      { x: 0, y: 0, z: c },
      { x: -c, y: 0, z: 0 },
      { x: 0, y: 0, z: -c },
      { x: 0, y: c, z: 0 }
    );
    const octEdges = [
      [0, 1], [0, 2], [0, 3], [0, 4],
      [5, 1], [5, 2], [5, 3], [5, 4],
      [1, 2], [2, 3], [3, 4], [4, 1],
    ];
    octEdges.forEach(([a, b], idx) =>
      edges.push({
        a: octBase + a,
        b: octBase + b,
        color: idx % 2 === 0 ? 'mint' : 'indigo',
        width: 1.6,
      })
    );

    // 3 Yatay Şema Katmanı (Strata Planes: y = -0.45, 0, +0.45)
    const strataLevels = [-0.45, 0, 0.45];
    strataLevels.forEach((y, levelIdx) => {
      const base = vertices.length;
      const r = levelIdx === 1 ? 0.88 : 0.62;
      vertices.push(
        { x: -r, y, z: -r },
        { x: r, y, z: -r },
        { x: r, y, z: r },
        { x: -r, y, z: r }
      );
      const col = levelIdx === 1 ? 'mint' : 'frame';
      edges.push(
        { a: base + 0, b: base + 1, color: col, width: levelIdx === 1 ? 1.5 : 1.1 },
        { a: base + 1, b: base + 2, color: col, width: levelIdx === 1 ? 1.5 : 1.1 },
        { a: base + 2, b: base + 3, color: col, width: levelIdx === 1 ? 1.5 : 1.1 },
        { a: base + 3, b: base + 0, color: col, width: levelIdx === 1 ? 1.5 : 1.1 }
      );
    });

    // 6 SQL Motor Yörünge Düğümü (6 orbiting engine nodes)
    const orbitBase = vertices.length;
    for (let i = 0; i < 6; i++) {
      const ang = (i / 6) * Math.PI * 2;
      vertices.push({
        x: Math.cos(ang) * 1.28,
        y: Math.sin(ang * 2) * 0.25,
        z: Math.sin(ang) * 1.28,
      });
    }

    let animId = 0;
    let time = 0;

    const render = () => {
      const dpr = Math.min(window.devicePixelRatio || 1, 2);
      const rect = canvas.getBoundingClientRect();
      const w = Math.max(240, rect.width);
      const h = Math.max(200, rect.height);

      if (canvas.width !== Math.round(w * dpr) || canvas.height !== Math.round(h * dpr)) {
        canvas.width = Math.round(w * dpr);
        canvas.height = Math.round(h * dpr);
      }

      ctx.save();
      ctx.scale(dpr, dpr);
      ctx.clearRect(0, 0, w, h);

      const rot = rotationRef.current;
      if (!rot.dragging && !prefersReducedMotion) {
        rot.ry += rot.vy;
        rot.rx += rot.vx;
        rot.vx *= 0.94;
        rot.vy = rot.vy * 0.96 + 0.0042 * 0.04;
      }
      if (!prefersReducedMotion) {
        time += 0.018;
      }

      // Yörünge düğümlerini güncelle
      for (let i = 0; i < 6; i++) {
        const ang = (i / 6) * Math.PI * 2 + time * 0.65;
        vertices[orbitBase + i].x = Math.cos(ang) * 1.32;
        vertices[orbitBase + i].y = Math.sin(ang * 2 + time * 0.4) * 0.28;
        vertices[orbitBase + i].z = Math.sin(ang) * 1.32;
      }

      const targetRx = rot.rx + rot.pointerY * 0.16;
      const targetRy = rot.ry + rot.pointerX * 0.22;

      const cosX = Math.cos(targetRx);
      const sinX = Math.sin(targetRx);
      const cosY = Math.cos(targetRy);
      const sinY = Math.sin(targetRy);

      const fov = Math.min(w, h) * 0.78;
      const dist = 4.1;

      const projected = vertices.map((v) => {
        // Rotate Y
        const x1 = v.x * cosY - v.z * sinY;
        const z1 = v.x * sinY + v.z * cosY;
        // Rotate X
        const y2 = v.y * cosX - z1 * sinX;
        const z2 = v.y * sinX + z1 * cosX;

        const scale = fov / (dist + z2);
        return {
          x: w / 2 + x1 * scale,
          y: h / 2 + y2 * scale,
          z: z2,
          scale,
        };
      });

      const cs = getComputedStyle(canvas);
      const mint = cs.getPropertyValue('--color-accent').trim() || cs.color;
      const indigo = cs.getPropertyValue('--color-accent-hover').trim() || mint;
      const frame = cs.getPropertyValue('--color-content-secondary').trim() || mint;
      const subtle = cs.getPropertyValue('--color-content-subtle').trim() || frame;

      const colors = {
        frame,
        subtle,
        mint,
        indigo,
      };

      // Arka plan radyal parıltısı (globalAlpha ile saf token rengi)
      ctx.save();
      ctx.globalAlpha = theme !== 'light' ? 0.14 : 0.18;
      const glow = ctx.createRadialGradient(w / 2, h / 2, 10, w / 2, h / 2, Math.min(w, h) * 0.48);
      glow.addColorStop(0, mint);
      glow.addColorStop(0.55, indigo);
      glow.addColorStop(1, 'transparent');
      ctx.fillStyle = glow;
      ctx.fillRect(0, 0, w, h);
      ctx.restore();

      // Kenarları derinliğe göre sırala (Painter's Algorithm)
      const sortedEdges = edges
        .map((e) => ({
          ...e,
          depth: (projected[e.a].z + projected[e.b].z) * 0.5,
        }))
        .sort((a, b) => b.depth - a.depth);

      sortedEdges.forEach((edge) => {
        const p1 = projected[edge.a];
        const p2 = projected[edge.b];
        const depthAlpha = Math.max(0.25, Math.min(1, 0.78 - edge.depth * 0.28));

        ctx.beginPath();
        ctx.moveTo(p1.x, p1.y);
        ctx.lineTo(p2.x, p2.y);
        ctx.lineWidth = edge.width;
        ctx.globalAlpha =
          edge.color === 'mint' || edge.color === 'indigo'
            ? depthAlpha
            : edge.color === 'frame'
              ? 0.4
              : 0.22;
        ctx.strokeStyle = colors[edge.color];
        ctx.stroke();
      });

      // Yörünge Düğümleri (6 SQL Motoru) + merkeze bağlantı
      const center = projected[octBase];
      for (let i = 0; i < 6; i++) {
        const p = projected[orbitBase + i];
        const isMint = i % 2 === 0;
        const r = Math.max(2.6, 4.2 - p.z * 0.8);

        // Merkeze ince veri ışını
        ctx.beginPath();
        ctx.moveTo(w / 2, h / 2);
        ctx.lineTo(p.x, p.y);
        ctx.globalAlpha = 0.18;
        ctx.lineWidth = 1;
        ctx.strokeStyle = isMint ? colors.mint : colors.indigo;
        ctx.stroke();

        // Dış halka + dolgu
        const nodeAlpha = Math.max(0.4, Math.min(1, 0.9 - p.z * 0.22));
        ctx.globalAlpha = nodeAlpha * 0.22;
        ctx.beginPath();
        ctx.arc(p.x, p.y, r + 2.5, 0, Math.PI * 2);
        ctx.fillStyle = isMint ? colors.mint : colors.indigo;
        ctx.fill();

        ctx.globalAlpha = nodeAlpha;
        ctx.beginPath();
        ctx.arc(p.x, p.y, r, 0, Math.PI * 2);
        ctx.fillStyle = isMint ? colors.mint : colors.indigo;
        ctx.fill();
      }

      // Köşe eklem noktaları
      ctx.globalAlpha = 0.85;
      for (let i = 0; i < 8; i++) {
        const p = projected[i];
        ctx.beginPath();
        ctx.arc(p.x, p.y, 2, 0, Math.PI * 2);
        ctx.fillStyle = colors.frame;
        ctx.fill();
      }

      // Merkez çekirdek parıltısı
      if (center) {
        ctx.globalAlpha = 0.9;
        ctx.beginPath();
        ctx.arc(w / 2, h / 2, 4.5, 0, Math.PI * 2);
        ctx.fillStyle = colors.mint;
        ctx.fill();
      }

      ctx.restore();

      if (!prefersReducedMotion) {
        animId = requestAnimationFrame(render);
      }
    };

    render();
    return () => cancelAnimationFrame(animId);
  }, [theme]);

  return (
    <div
      className="relative w-full h-[220px] sm:h-[250px] rounded-[var(--radius-card)] border border-surface-500/70 bg-surface-950/60 overflow-hidden select-none group"
      onPointerDown={(e) => {
        rotationRef.current.dragging = true;
        rotationRef.current.lastX = e.clientX;
        rotationRef.current.lastY = e.clientY;
        setIsDragging(true);
        (e.currentTarget as HTMLElement).setPointerCapture(e.pointerId);
      }}
      onPointerMove={(e) => {
        const rect = e.currentTarget.getBoundingClientRect();
        rotationRef.current.pointerX = ((e.clientX - rect.left) / rect.width - 0.5) * 2;
        rotationRef.current.pointerY = ((e.clientY - rect.top) / rect.height - 0.5) * 2;
        if (rotationRef.current.dragging) {
          const dx = e.clientX - rotationRef.current.lastX;
          const dy = e.clientY - rotationRef.current.lastY;
          rotationRef.current.ry += dx * 0.012;
          rotationRef.current.rx += dy * 0.012;
          rotationRef.current.vy = dx * 0.0025;
          rotationRef.current.vx = dy * 0.0025;
          rotationRef.current.lastX = e.clientX;
          rotationRef.current.lastY = e.clientY;
        }
      }}
      onPointerUp={(e) => {
        rotationRef.current.dragging = false;
        setIsDragging(false);
        try {
          (e.currentTarget as HTMLElement).releasePointerCapture(e.pointerId);
        } catch {
          // ignore
        }
      }}
      onPointerLeave={() => {
        rotationRef.current.pointerX = 0;
        rotationRef.current.pointerY = 0;
        rotationRef.current.dragging = false;
        setIsDragging(false);
      }}
      role="img"
      aria-label="Interactive 3D Namines Schema Strata Polyhedron"
    >
      {/* Teknik ızgara arka planı */}
      <div
        aria-hidden="true"
        className="absolute inset-0 opacity-25 pointer-events-none"
        style={{
          backgroundImage:
            'radial-gradient(circle at 1px 1px, var(--color-content-subtle) 1px, transparent 0)',
          backgroundSize: '18px 18px',
        }}
      />

      <canvas
        ref={canvasRef}
        className={`w-full h-full block ${isDragging ? 'cursor-grabbing' : 'cursor-grab'}`}
      />

      {/* Köşe telemetri etiketi */}
      <div className="pointer-events-none absolute top-3 left-3.5 flex items-center gap-2 px-2.5 py-1 rounded-full bg-surface-900/80 border border-surface-500/60 backdrop-blur-md">
        <span className="w-1.5 h-1.5 rounded-full bg-accent animate-pulse" />
        <span className="text-[10px] font-mono font-semibold tracking-wider text-content-secondary uppercase">
          IR Core · 3D Lattice
        </span>
      </div>

      <div className="pointer-events-none absolute bottom-3 right-3.5 px-2 py-0.5 rounded-[var(--radius-control)] bg-surface-900/75 border border-surface-500/50 text-[10px] font-mono text-content-muted opacity-80 group-hover:opacity-100 transition-opacity">
        Drag to orbit · 6 engines
      </div>
    </div>
  );
}
