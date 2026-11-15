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
 * Footer içinde kutusuz/çerçevesiz, şeffaf arka planlı interaktif 3D
 * Namines Veritabanı Silindiri & Şema Katmanları (Database Strata Cylinder).
 */
export default function Footer3DModel() {
  const canvasRef = useRef<HTMLCanvasElement | null>(null);
  const { theme } = useHomeThemeStore();
  const [isDragging, setIsDragging] = useState(false);

  const rotationRef = useRef({
    rx: 0.46,
    ry: -0.4,
    vx: 0,
    vy: 0.005,
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

    const vertices: Vec3[] = [];
    const edges: Edge[] = [];

    // 1. 3D Veritabanı Silindir Katmanları (4 yatay disk halkası: üst kapak, 2 ara katman, alt taban)
    const SEGMENTS = 20;
    const ringLevels = [
      { y: -0.72, r: 0.88, color: 'mint' as const, width: 1.7 },
      { y: -0.24, r: 0.92, color: 'frame' as const, width: 1.2 },
      { y: 0.24, r: 0.92, color: 'indigo' as const, width: 1.3 },
      { y: 0.72, r: 0.88, color: 'frame' as const, width: 1.5 },
    ];

    const ringBases: number[] = [];
    ringLevels.forEach((ring) => {
      const base = vertices.length;
      ringBases.push(base);
      for (let i = 0; i < SEGMENTS; i++) {
        const ang = (i / SEGMENTS) * Math.PI * 2;
        vertices.push({
          x: Math.cos(ang) * ring.r,
          y: ring.y,
          z: Math.sin(ang) * ring.r,
        });
      }
      for (let i = 0; i < SEGMENTS; i++) {
        edges.push({
          a: base + i,
          b: base + ((i + 1) % SEGMENTS),
          color: ring.color,
          width: ring.width,
        });
      }
    });

    // Silindir dikey kolonları (her 5 segmente bir: 4 ana ilişki sütunu)
    for (let i = 0; i < SEGMENTS; i += 5) {
      for (let l = 0; l < ringBases.length - 1; l++) {
        edges.push({
          a: ringBases[l] + i,
          b: ringBases[l + 1] + i,
          color: i % 10 === 0 ? 'mint' : 'subtle',
          width: 1.15,
        });
      }
    }

    // 2. İç Namines Çekirdek Kristali (Elmas/Oktahedron IR Core)
    const coreBase = vertices.length;
    const cr = 0.42;
    vertices.push(
      { x: 0, y: -cr * 1.15, z: 0 },
      { x: cr, y: 0, z: 0 },
      { x: 0, y: 0, z: cr },
      { x: -cr, y: 0, z: 0 },
      { x: 0, y: 0, z: -cr },
      { x: 0, y: cr * 1.15, z: 0 }
    );
    const coreEdges = [
      [0, 1], [0, 2], [0, 3], [0, 4],
      [5, 1], [5, 2], [5, 3], [5, 4],
      [1, 2], [2, 3], [3, 4], [4, 1],
    ];
    coreEdges.forEach(([a, b], idx) =>
      edges.push({
        a: coreBase + a,
        b: coreBase + b,
        color: idx % 2 === 0 ? 'mint' : 'indigo',
        width: 1.5,
      })
    );

    // 3. Yörüngedeki 6 SQL Lehçesi Düğümü
    const orbitBase = vertices.length;
    for (let i = 0; i < 6; i++) {
      const ang = (i / 6) * Math.PI * 2;
      vertices.push({
        x: Math.cos(ang) * 1.24,
        y: Math.sin(ang * 2) * 0.22,
        z: Math.sin(ang) * 1.24,
      });
    }

    let animId = 0;
    let time = 0;

    const render = () => {
      const dpr = Math.min(window.devicePixelRatio || 1, 2);
      const rect = canvas.getBoundingClientRect();
      const w = Math.max(180, rect.width);
      const h = Math.max(180, rect.height);

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
        rot.vy = rot.vy * 0.96 + 0.0048 * 0.04;
      }
      if (!prefersReducedMotion) {
        time += 0.018;
      }

      for (let i = 0; i < 6; i++) {
        const ang = (i / 6) * Math.PI * 2 + time * 0.65;
        vertices[orbitBase + i].x = Math.cos(ang) * 1.24;
        vertices[orbitBase + i].y = Math.sin(ang * 2 + time * 0.4) * 0.22;
        vertices[orbitBase + i].z = Math.sin(ang) * 1.24;
      }

      const targetRx = rot.rx + rot.pointerY * 0.18;
      const targetRy = rot.ry + rot.pointerX * 0.24;

      const cosX = Math.cos(targetRx);
      const sinX = Math.sin(targetRx);
      const cosY = Math.cos(targetRy);
      const sinY = Math.sin(targetRy);

      const fov = Math.min(w, h) * 0.85;
      const dist = 4.0;

      const projected = vertices.map((v) => {
        const x1 = v.x * cosY - v.z * sinY;
        const z1 = v.x * sinY + v.z * cosY;
        const y2 = v.y * cosX - z1 * sinX;
        const z2 = v.y * sinX + z1 * cosX;

        const scale = fov / (dist + z2);
        return {
          x: w / 2 + x1 * scale,
          y: h / 2 + y2 * scale,
          z: z2,
        };
      });

      const cs = getComputedStyle(canvas);
      const mint = cs.getPropertyValue('--color-accent').trim() || cs.color;
      const indigo = cs.getPropertyValue('--color-accent-hover').trim() || mint;
      const frame = cs.getPropertyValue('--color-content-secondary').trim() || mint;
      const subtle = cs.getPropertyValue('--color-content-subtle').trim() || frame;

      const colors = { frame, subtle, mint, indigo };

      // Yumuşak arka plan parıltısı (kutu yok)
      ctx.save();
      ctx.globalAlpha = theme !== 'light' ? 0.14 : 0.16;
      const glow = ctx.createRadialGradient(w / 2, h / 2, 6, w / 2, h / 2, Math.min(w, h) * 0.45);
      glow.addColorStop(0, mint);
      glow.addColorStop(0.6, indigo);
      glow.addColorStop(1, 'transparent');
      ctx.fillStyle = glow;
      ctx.fillRect(0, 0, w, h);
      ctx.restore();

      const sortedEdges = edges
        .map((e) => ({
          ...e,
          depth: (projected[e.a].z + projected[e.b].z) * 0.5,
        }))
        .sort((a, b) => b.depth - a.depth);

      sortedEdges.forEach((edge) => {
        const p1 = projected[edge.a];
        const p2 = projected[edge.b];
        const depthAlpha = Math.max(0.28, Math.min(1, 0.82 - edge.depth * 0.28));

        ctx.beginPath();
        ctx.moveTo(p1.x, p1.y);
        ctx.lineTo(p2.x, p2.y);
        ctx.lineWidth = edge.width;
        ctx.globalAlpha =
          edge.color === 'mint' || edge.color === 'indigo'
            ? depthAlpha
            : edge.color === 'frame'
              ? 0.42
              : 0.22;
        ctx.strokeStyle = colors[edge.color];
        ctx.stroke();
      });

      // Yörünge Düğümleri (6 SQL Motoru)
      for (let i = 0; i < 6; i++) {
        const p = projected[orbitBase + i];
        const isMint = i % 2 === 0;
        const r = Math.max(2.4, 3.8 - p.z * 0.7);
        const nodeAlpha = Math.max(0.4, Math.min(1, 0.9 - p.z * 0.22));

        ctx.globalAlpha = nodeAlpha * 0.2;
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
      className="relative w-full h-[180px] sm:h-[200px] select-none"
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
      aria-label="Interactive 3D Namines Database Strata Cylinder"
    >
      <canvas
        ref={canvasRef}
        className={`w-full h-full block ${isDragging ? 'cursor-grabbing' : 'cursor-grab'}`}
      />
    </div>
  );
}
