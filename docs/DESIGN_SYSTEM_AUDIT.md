# MASTER AUDIT REPORT: Clean Code, Design Tokens & Centralized Design System

**Date:** 2026-10-16  
**Auditor:** Principal Frontend Architect & Design Systems Engineer  
**Target Codebase:** `namines/frontend`  

---

## 1. Current Architecture

- **Framework & Core:** Next.js 16.2.6 (App Router), React 19.2.4, TypeScript 5.
- **Styling Architecture:** Tailwind CSS v4 (`@tailwindcss/postcss: ^4`, `@import "tailwindcss";`) configured via CSS variables under `:root` and `@theme inline` in `app/globals.css`.
- **Color Engine:** Perceptual OKLCH dynamic single-knob palette (10 base numbers deriving all surfaces, content levels, accents, and WCAG AA compliant semantic states for both dark mode and light mode).
- **Typography:** Google Font `Assistant` (weights 400, 500, 600, 700) loaded via Next.js `next/font/google` in `app/layout.tsx`, with CSS variables `--text-xs` to `--text-6xl`.
- **Existing Primitives:**
  - `components/ui/Button.tsx` (single button primitive)
  - `components/ui/Container.tsx` (width wrapper: prose, app, wide)
  - `components/workspace/WorkspaceKit.tsx` (comprehensive workspace kit for Flow, Ground, Vault, Launch, Review)
  - `lib/designTokens.ts` (computes computed CSS values in JS for canvas export and React Flow)
- **Validation Pipeline:**
  - `npm run check:design` (`scripts/check-design-rules.mjs` enforcing zero raw hex, zero unreadable text < 10px, zero off-palette colors, zero deprecated radius values)
  - `npx tsc --noEmit` (strict TypeScript validation)
  - `npm test` (212 Vitest unit tests)
  - `node scripts/e2e-live-test.mjs` (39 Playwright end-to-end integration tests across all routes)

---

## 2. Main Problems Identified

1. **Z-Index Layering Fragmentation:**
   - 19 different arbitrary `z-[...]` values scattered across components (`z-[40]`, `z-[45]`, `z-[60]`, `z-[70]`, `z-[80]`, `z-[90]`, `z-[95]`, `z-[100]`, `z-[110]`, `z-[200]`, `z-[300]`, `z-[999]`, `z-[8000]`, `z-[9990]`, `z-[9991]`, `z-[9997]`, `z-[9998]`, `z-[9999]`, `z-[10000]`).
   - Lack of semantic z-index scale causes stacking collision risks between canvas toolbars, drawers, context menus, and modals.

2. **Component Catalog Gaps in `components/ui`:**
   - `components/ui/` only had `Button` and `Container`.
   - Modals (`ConfirmDialog`, `TeamModal`, `AIGatewayModal`, `VisionUploadModal`, `AuthModal`) implemented ad-hoc backdrop, blur, and escape key handling instead of a shared `Modal` / `Dialog` primitive.
   - Form controls (`Input`, `Select`, `Checkbox`, `Radio`, `Switch`, `Textarea`, `FormField`) had fragmented definitions between `preferences/ui.tsx`, `WorkspaceKit.tsx`, and inline classes.

3. **Elevation & Shadow Inconsistency:**
   - Arbitrary box-shadow definitions (`shadow-[0_20px_60px_color-mix(...)]`, `shadow-[0_4px_15px_...]`) rather than standardized elevation tokens (`--shadow-sm`, `--shadow-md`, `--shadow-lg`, `--shadow-modal`, `--shadow-popover`).

4. **Programmatic Design System Configuration:**
   - `lib/designTokens.ts` existed as a lookup helper, but there was no comprehensive typed TypeScript design token schema defining breakpoints, spacing, z-index, animation curves, and component size defaults.

---

## 3. Proposed Design System Structure

```
frontend/
├── app/
│   └── globals.css              # Central CSS Token Source of Truth (Colors, Typography, Spacing, Radius, Elevation, Z-Index)
├── config/
│   └── design-system.ts         # Type-safe TypeScript Design System Tokens & Configurations
├── lib/
│   └── designTokens.ts          # Programmatic runtime token reader with SSR safe fallbacks
├── components/
│   └── ui/                      # Standardized Reusable UI Primitives
│       ├── Button.tsx           # Button with variants (primary, secondary, ghost, danger) & sizes (sm, md, lg)
│       ├── IconButton.tsx       # Accessible square icon button with tap-44 touch targets
│       ├── Input.tsx            # Form input with icon slots, validation states & helper text
│       ├── Textarea.tsx         # Resizable/fixed textarea with semantic styling
│       ├── Select.tsx           # Styled select control with keyboard support
│       ├── Checkbox.tsx         # Accessible custom checkbox
│       ├── Radio.tsx            # Accessible radio button group
│       ├── Switch.tsx           # Accessible switch toggle with ARIA switch role
│       ├── Label.tsx            # Form label with required/optional indicator
│       ├── FormField.tsx        # Compound form field wrapper linking label, control, and error
│       ├── Card.tsx             # Card container with CardHeader, CardBody, CardFooter
│       ├── Badge.tsx            # Semantic status and category badge (neutral, accent, success, danger, warning, info)
│       ├── Modal.tsx            # Accessible modal dialog with focus trap, backdrop blur & animations
│       ├── Drawer.tsx           # Accessible slide-out sheet/drawer
│       ├── Tooltip.tsx          # Accessible tooltip
│       ├── Tabs.tsx             # WAI-ARIA tablist with arrow key keyboard navigation
│       ├── Table.tsx            # Semantic table with header, body, row, cell, and pagination
│       ├── Skeleton.tsx         # Content placeholder pulse skeleton
│       ├── Spinner.tsx          # Accessible loading indicator
│       ├── EmptyState.tsx       # Shopify-standard empty state illustration, title, desc, action
│       ├── Alert.tsx            # Callout / Alert box with icon and action
│       ├── Container.tsx        # Layout width wrapper (prose, app, wide)
│       └── index.ts             # Central export point for all primitives
├── components/workspace/
│   └── WorkspaceKit.tsx         # Workspace-specific extension kit re-exporting standardized primitives
└── docs/
    └── DESIGN_SYSTEM_GUIDE.md   # Complete Design System Architecture & Governance Manual
```

---

## 4. High-Risk Areas

1. **`check-design-rules.mjs` Compatibility:**
   - Any new code in `components/` and `app/` must strictly avoid raw hex colors, raw Tailwind colors (e.g. `zinc-*`, `blue-*`), raw `text-white`/`bg-black`, deprecated radius classes, and font sizes < 10px.
   - Any new CSS variables used in `.css` must be declared in `app/globals.css` under `:root` or `@theme inline` to avoid "tanımsız token" errors.
2. **React Flow Canvas Stacking:**
   - Canvas nodes, minimap, controls, and connection lines operate within `@xyflow/react` viewport. Semantic z-indexes must keep node controls interactive while allowing modals, drawers, and context menus to sit cleanly on top without clipping.
3. **Dual Theme Contrast Ratios:**
   - Both dark mode (default) and light mode (`data-theme="light"`) must maintain WCAG AA (> 4.5:1 for normal text, > 3.0:1 for large text/borders).

---

## 5. Existing Behavior to Preserve

- All 212 unit tests in `npm test` must continue to pass 100%.
- Live E2E tests in `node scripts/e2e-live-test.mjs` (39 assertions covering Canvas, Review console, Desk, Compile CodeStudioViewer, Flow, Ground, Vault) must remain 100% green.
- Canvas state management (Zustand stores: `useSchemaStore`, `useAutomationStore`, `useBranchStore`, `useToastStore`).
- Zero breaking changes to API endpoints, SignalR hubs, or canvas keyboard shortcuts.

---

## 6. Implementation Plan

1. **Phase 3 (Central Tokens):**
   - Add semantic Z-index scale (`--z-base` to `--z-max`), elevation levels (`--shadow-modal`, `--shadow-popover`), control dimensions, and animation curves in `app/globals.css`.
   - Create `config/design-system.ts` providing TypeScript constants for all tokens.
   - Update `lib/designTokens.ts` to reflect the expanded token registry.
2. **Phase 4 & 5 (Reusable Component Architecture):**
   - Build out comprehensive, accessible UI primitives in `components/ui/` (`IconButton`, `Input`, `Textarea`, `Select`, `Checkbox`, `Radio`, `Switch`, `Label`, `FormField`, `Card`, `Badge`, `Modal`, `Drawer`, `Tooltip`, `Tabs`, `Table`, `Skeleton`, `Spinner`, `EmptyState`, `Alert`).
   - Export all primitives cleanly from `components/ui/index.ts`.
   - Align `components/workspace/WorkspaceKit.tsx` to build on top of these shared primitives.
3. **Phase 6 & 8 (Refactoring & Stacking Hygiene):**
   - Refactor arbitrary z-indexes across existing dialogs/drawers to use semantic classes (`z-modal`, `z-toast`, `z-popover`, `z-dropdown`, `z-drawer`, `z-sticky`).
4. **Phase 10 (Documentation):**
   - Create comprehensive `docs/DESIGN_SYSTEM_GUIDE.md` documenting usage, tokens, components, accessibility, and governance.
5. **Phase 11 (Validation):**
   - Run `check:design`, `tsc --noEmit`, `npm test`, and `e2e-live-test.mjs`. Verify 0 errors.
