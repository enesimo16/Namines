/**
 * Centralized Design System Configuration (Single Source of Truth in TypeScript)
 * 
 * Maps directly to CSS custom properties defined in app/globals.css.
 * All shared UI components and design system utilities reference these tokens.
 */

export const typography = {
  fontFamily: {
    sans: 'var(--font-sans)',
    mono: 'var(--font-mono)',
  },
  weights: {
    regular: 400,
    medium: 500,
    semibold: 600,
    bold: 700,
  },
  scale: {
    xs: 'var(--text-xs)',           // 11px
    sm: 'var(--text-sm)',           // 12px
    'base-sm': 'var(--text-base-sm)', // 13px
    base: 'var(--text-base)',       // 14px
    md: 'var(--text-md)',           // 16px
    lg: 'var(--text-lg)',           // 18px
    xl: 'var(--text-xl)',           // 20px
    '2xl': 'var(--text-2xl)',       // 24px
    '3xl': 'var(--text-3xl)',       // 30px
    '4xl': 'var(--text-4xl)',       // 36px
    '5xl': 'var(--text-5xl)',       // 48px
    '6xl': 'var(--text-6xl)',       // 56px
  },
  lineHeights: {
    tight: 'var(--leading-tight)',     // 1.15
    heading: 'var(--leading-heading)', // 1.3
    normal: 'var(--leading-normal)',   // 1.5
    relaxed: 'var(--leading-relaxed)', // 1.65
  },
} as const;

export const spacing = {
  1: 'var(--space-1)',   // 4px
  2: 'var(--space-2)',   // 8px
  3: 'var(--space-3)',   // 12px
  4: 'var(--space-4)',   // 16px
  5: 'var(--space-5)',   // 20px
  6: 'var(--space-6)',   // 24px
  8: 'var(--space-8)',   // 32px
  10: 'var(--space-10)', // 40px
  12: 'var(--space-12)', // 48px
  16: 'var(--space-16)', // 64px
} as const;

export const radii = {
  sm: 'var(--radius-sm)',         // 4px
  md: 'var(--radius-md)',         // 8px
  lg: 'var(--radius-lg)',         // 12px
  xl: 'var(--radius-xl)',         // 16px
  full: 'var(--radius-full)',     // 9999px
  control: 'var(--radius-control)', // standard input/button control
  card: 'var(--radius-card)',       // standard card/tile
  modal: 'var(--radius-modal)',     // standard dialog/modal
} as const;

export const shadows = {
  none: 'var(--shadow-none)',
  sm: 'var(--shadow-sm)',
  md: 'var(--shadow-md)',
  lg: 'var(--shadow-lg)',
  popover: 'var(--shadow-popover)',
  modal: 'var(--shadow-modal)',
  glass: 'var(--shadow-glass)',
} as const;

export const zIndex = {
  base: 'var(--z-base)',                   // 0
  surface: 'var(--z-surface)',             // 10
  sticky: 'var(--z-sticky)',               // 20
  dropdown: 'var(--z-dropdown)',           // 30
  drawerBackdrop: 'var(--z-drawer-backdrop)', // 40
  drawer: 'var(--z-drawer)',               // 50
  modalBackdrop: 'var(--z-modal-backdrop)', // 60
  modal: 'var(--z-modal)',                 // 70
  popover: 'var(--z-popover)',             // 80
  toast: 'var(--z-toast)',                 // 90
  max: 'var(--z-max)',                     // 100
} as const;

export const layout = {
  headerHeight: 'var(--header-height)',                 // 60px
  sidebarWidth: 'var(--sidebar-width)',                 // 250px
  sidebarCollapsedWidth: 'var(--sidebar-collapsed-width)', // 72px
  pagePadding: 'var(--page-padding)',                   // 24px
  cardPadding: 'var(--card-padding)',                   // 24px
  contentGap: 'var(--content-gap)',                     // 24px
  containers: {
    prose: 'var(--w-prose)', // 720px
    app: 'var(--w-app)',     // 1200px
    wide: 'var(--w-wide)',   // 1440px
  },
  controlHeights: {
    sm: 'var(--control-h-sm)', // 32px
    md: 'var(--control-h-md)', // 36px
    lg: 'var(--control-h-lg)', // 44px
  },
} as const;

export const transitions = {
  durations: {
    fast: 'var(--dur-fast)', // 120ms
    base: 'var(--dur-base)', // 180ms
    slow: 'var(--dur-slow)', // 280ms
  },
  easing: {
    out: 'var(--ease-out)',  // cubic-bezier(0.2, 0, 0, 1)
  },
} as const;

export const breakpoints = {
  sm: 640,
  md: 768,
  lg: 1024,
  xl: 1280,
  '2xl': 1536,
} as const;

export const designSystem = {
  typography,
  spacing,
  radii,
  shadows,
  zIndex,
  layout,
  transitions,
  breakpoints,
} as const;

export default designSystem;
