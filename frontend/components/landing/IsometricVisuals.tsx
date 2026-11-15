'use client';

/**
 * 3D izometrik ve piksel-ızgara mimari görselleri.
 * Tüm renkler globals.css OKLCH token'larından beslenir (sıfır ham hex / rgba).
 */

export function IsometricIrPipeline() {
  return (
    <div
      aria-hidden="true"
      className="relative w-full max-w-[380px] mx-auto select-none"
    >
      <svg
        viewBox="0 0 380 300"
        className="w-full h-auto drop-shadow-xl"
        fill="none"
        xmlns="http://www.w3.org/2000/svg"
      >
        {/* Subtle Isometric Pixel Grid Backdrop */}
        <g className="stroke-surface-500/40" strokeWidth="1">
          <path d="M40 230 L190 290 L340 230" />
          <path d="M60 205 L190 258 L320 205" />
          <path d="M190 35 L190 290" strokeDasharray="3 4" />
        </g>

        {/* Bottom Slab: 6 SQL Dialects + 19 Eject Targets */}
        <g className="namines-iso-float">
          <polygon
            points="190,170 320,218 190,266 60,218"
            className="fill-surface-800 stroke-surface-500"
            strokeWidth="1.5"
          />
          <polygon
            points="60,218 190,266 190,280 60,232"
            className="fill-surface-950 stroke-surface-500"
            strokeWidth="1.5"
          />
          <polygon
            points="320,218 190,266 190,280 320,232"
            className="fill-surface-900 stroke-surface-500"
            strokeWidth="1.5"
          />
          {/* Pixel blocks representing 6 dialects */}
          <rect x="132" y="212" width="10" height="6" className="fill-accent namines-pixel-pulse" />
          <rect x="152" y="220" width="10" height="6" className="fill-accent/70" />
          <rect x="172" y="228" width="10" height="6" className="fill-accent" />
          <rect x="198" y="228" width="10" height="6" className="fill-accent/70 namines-pixel-pulse" />
          <rect x="218" y="220" width="10" height="6" className="fill-accent" />
          <rect x="238" y="212" width="10" height="6" className="fill-accent/70" />
        </g>

        {/* Middle Slab: Impact Analyzer & Ephemeral Sandbox */}
        <g className="namines-iso-float-delayed">
          <polygon
            points="190,104 306,148 190,192 74,148"
            className="fill-surface-850 stroke-accent/70"
            strokeWidth="1.5"
          />
          <polygon
            points="74,148 190,192 190,204 74,160"
            className="fill-surface-950 stroke-accent/50"
            strokeWidth="1.2"
          />
          <polygon
            points="306,148 190,192 190,204 306,160"
            className="fill-surface-900 stroke-accent/50"
            strokeWidth="1.2"
          />
          {/* Pixel check matrix */}
          <rect x="166" y="142" width="8" height="8" className="fill-accent" />
          <rect x="186" y="142" width="8" height="8" className="fill-accent/80 namines-pixel-pulse" />
          <rect x="206" y="142" width="8" height="8" className="fill-accent" />
        </g>

        {/* Top Floating Slab: Canonical ir.json */}
        <g className="namines-iso-float">
          <polygon
            points="190,34 290,74 190,114 90,74"
            className="fill-surface-800 stroke-accent"
            strokeWidth="2"
          />
          <polygon
            points="90,74 190,114 190,126 90,86"
            className="fill-accent/25 stroke-accent"
            strokeWidth="1.5"
          />
          <polygon
            points="290,74 190,114 190,126 290,86"
            className="fill-accent/15 stroke-accent"
            strokeWidth="1.5"
          />
          {/* Top Isometric Cube Core */}
          <polygon
            points="190,54 214,64 190,74 166,64"
            className="fill-accent"
          />
          <polygon
            points="166,64 190,74 190,90 166,80"
            className="fill-accent/75"
          />
          <polygon
            points="214,64 190,74 190,90 214,80"
            className="fill-accent/55"
          />
        </g>

        {/* Floating Pixel Nodes */}
        <rect x="52" y="96" width="6" height="6" className="fill-accent namines-pixel-pulse" />
        <rect x="320" y="110" width="6" height="6" className="fill-accent/80 namines-pixel-pulse" />
        <rect x="336" y="176" width="5" height="5" className="fill-content-muted/60" />
        <rect x="40" y="168" width="5" height="5" className="fill-content-muted/60" />
      </svg>
    </div>
  );
}

export function IsometricPricingVault() {
  return (
    <div
      aria-hidden="true"
      className="relative w-full max-w-[340px] mx-auto select-none"
    >
      <svg
        viewBox="0 0 340 240"
        className="w-full h-auto drop-shadow-xl"
        fill="none"
        xmlns="http://www.w3.org/2000/svg"
      >
        {/* Base Platform */}
        <polygon
          points="170,150 296,196 170,236 44,196"
          className="fill-surface-800/80 stroke-surface-500"
          strokeWidth="1.5"
        />

        {/* Left Tier Pillar (Free/Pro) */}
        <g className="namines-iso-float-delayed">
          <polygon points="104,114 136,126 104,138 72,126" className="fill-surface-700 stroke-surface-500" strokeWidth="1.2" />
          <polygon points="72,126 104,138 104,178 72,166" className="fill-surface-900 stroke-surface-500" strokeWidth="1.2" />
          <polygon points="136,126 104,138 104,178 136,166" className="fill-surface-850 stroke-surface-500" strokeWidth="1.2" />
        </g>

        {/* Center Highlighted Pillar (Team / Governance) */}
        <g className="namines-iso-float">
          <polygon points="170,42 214,60 170,78 126,60" className="fill-accent stroke-accent" strokeWidth="1.5" />
          <polygon points="126,60 170,78 170,164 126,146" className="fill-accent/30 stroke-accent" strokeWidth="1.5" />
          <polygon points="214,60 170,78 170,164 214,146" className="fill-accent/20 stroke-accent" strokeWidth="1.5" />
          {/* Pixel telemetry bars on center pillar */}
          <rect x="138" y="88" width="18" height="5" className="fill-accent namines-pixel-pulse" />
          <rect x="138" y="100" width="12" height="5" className="fill-accent/70" />
          <rect x="138" y="112" width="22" height="5" className="fill-accent" />
        </g>

        {/* Right Tier Pillar (Enterprise VPC) */}
        <g className="namines-iso-float-delayed">
          <polygon points="236,90 268,102 236,114 204,102" className="fill-surface-700 stroke-accent/70" strokeWidth="1.2" />
          <polygon points="204,102 236,114 236,178 204,166" className="fill-surface-900 stroke-accent/50" strokeWidth="1.2" />
          <polygon points="268,102 236,114 236,178 268,166" className="fill-surface-850 stroke-accent/50" strokeWidth="1.2" />
        </g>

        {/* Pixel Accents */}
        <rect x="64" y="72" width="6" height="6" className="fill-accent namines-pixel-pulse" />
        <rect x="272" y="58" width="6" height="6" className="fill-accent/80 namines-pixel-pulse" />
      </svg>
    </div>
  );
}

export type PixelEmblemKind =
  | 'ci-cd'
  | 'modernize'
  | 'ai-layer'
  | 'multi-engine'
  | 'teams'
  | 'governance';

export function IsometricPixelEmblem({ kind }: { kind: PixelEmblemKind }) {
  return (
    <svg
      viewBox="0 0 36 36"
      className="w-9 h-9 shrink-0"
      fill="none"
      aria-hidden="true"
    >
      {/* Base 3D isometric diamond */}
      <polygon
        points="18,4 32,11 18,18 4,11"
        className="fill-surface-800 stroke-accent/80"
        strokeWidth="1.2"
      />
      <polygon
        points="4,11 18,18 18,31 4,24"
        className="fill-surface-950 stroke-surface-500"
        strokeWidth="1.2"
      />
      <polygon
        points="32,11 18,18 18,31 32,24"
        className="fill-surface-900 stroke-surface-500"
        strokeWidth="1.2"
      />
      {/* Distinct pixel pattern per solution */}
      {kind === 'ci-cd' && (
        <>
          <rect x="14" y="8" width="3" height="3" className="fill-accent" />
          <rect x="19" y="8" width="3" height="3" className="fill-accent" />
          <rect x="16" y="11" width="4" height="3" className="fill-accent/70" />
        </>
      )}
      {kind === 'modernize' && (
        <>
          <rect x="12" y="9" width="12" height="3" className="fill-accent" />
          <rect x="15" y="13" width="6" height="2" className="fill-accent/70" />
        </>
      )}
      {kind === 'ai-layer' && (
        <>
          <rect x="16" y="7" width="4" height="4" className="fill-accent" />
          <rect x="12" y="11" width="3" height="3" className="fill-accent/75" />
          <rect x="21" y="11" width="3" height="3" className="fill-accent/75" />
        </>
      )}
      {kind === 'multi-engine' && (
        <>
          <rect x="13" y="8" width="3" height="3" className="fill-accent" />
          <rect x="17" y="10" width="3" height="3" className="fill-accent/80" />
          <rect x="21" y="8" width="3" height="3" className="fill-accent" />
        </>
      )}
      {kind === 'teams' && (
        <>
          <rect x="12" y="9" width="4" height="4" className="fill-accent/80" />
          <rect x="20" y="9" width="4" height="4" className="fill-accent" />
        </>
      )}
      {kind === 'governance' && (
        <>
          <rect x="15" y="7" width="6" height="5" className="fill-accent" />
          <rect x="16" y="12" width="4" height="2" className="fill-accent/70" />
        </>
      )}
    </svg>
  );
}
