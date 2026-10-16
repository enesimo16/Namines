# Namines SaaS Design System — Architecture & Developer Guide

**Document Version:** 1.0.0  
**Stack:** Next.js 16 (App Router), React 19, Tailwind CSS v4, TypeScript 5  
**Primary Typography:** Assistant (Weights: 400, 500, 600, 700)  
**Design Reference:** Shopify Polaris + Studio Console  

---

## 1. Architecture Overview & Sources of Truth

The Namines design system is governed by a **strict single source of truth** architecture. Design decisions are declared centrally in CSS custom properties and mirrored in type-safe TypeScript configurations.

### Source of Truth Files

1. **CSS Custom Properties & Utilities (Visual Source of Truth):**  
   [`frontend/app/globals.css`](file:///c:/Users/PC/Desktop/apps/namines/frontend/app/globals.css)  
   Contains the dynamic OKLCH color engine, typography tokens, spacing, border radii, shadows, semantic z-index scale, control dimensions, and `@theme inline` / `@utility` registrations for Tailwind CSS v4.

2. **TypeScript Token Schema (Programmatic Source of Truth):**  
   [`frontend/config/design-system.ts`](file:///c:/Users/PC/Desktop/apps/namines/frontend/config/design-system.ts)  
   Exports type-safe constants for typography, spacing, radius, shadows, z-index, layout dimensions, transition curves, and breakpoints.

3. **Runtime Computed Token Reader (Canvas & JS Source of Truth):**  
   [`frontend/lib/designTokens.ts`](file:///c:/Users/PC/Desktop/apps/namines/frontend/lib/designTokens.ts)  
   Provides `token('--color-...')` for places where CSS class names cannot be passed directly (e.g., React Flow canvas nodes, PNG image export via `toPng`, SVG fill attributes), with SSR-safe fallbacks.

4. **Shared UI Component Primitives (Component Source of Truth):**  
   [`frontend/components/ui/index.ts`](file:///c:/Users/PC/Desktop/apps/namines/frontend/components/ui/index.ts)  
   Production-ready, accessible, reusable primitives: `Button`, `IconButton`, `Input`, `Textarea`, `Select`, `Checkbox`, `Radio`, `Switch`, `Label`, `FormField`, `Card`, `Badge`, `Modal`, `Drawer`, `Tooltip`, `Tabs`, `Table`, `Pagination`, `Skeleton`, `Spinner`, `EmptyState`, `Alert`, and `Container`.

5. **Design System Guard (Automated Enforcement):**  
   [`frontend/scripts/check-design-rules.mjs`](file:///c:/Users/PC/Desktop/apps/namines/frontend/scripts/check-design-rules.mjs)  
   Enforced in CI via `npm run check:design`. Ensures 0 raw hex codes in `.tsx`, 0 off-palette Tailwind classes, 0 pure black/white, 0 unreadable text sizes (< 10px), and 0 undefined tokens.

---

## 2. Color System & OKLCH Engine

The color system uses perceptual OKLCH color spaces. In OKLCH, lightness ($L$) is uniform across all hues, guaranteeing consistent visual contrast regardless of theme or brand hue adjustments.

### Base Color Knobs (`app/globals.css`)

```css
:root {
  --ui-hue: 250;        /* Neutral hue base */
  --ui-chroma: 0;       /* 0 = pure neutral monochromatic slate */
  --ui-bg-l: 15%;       /* Background lightness: 15% (dark), 98.5% (light) */
  --ui-bg-step: 5%;     /* Step delta between elevation layers */
  --brand-hue: 0;       /* Brand hue */
  --brand-chroma: 0;    /* 0 = clean monochrome */
  --brand-l: 96%;       /* Accent lightness */
}
```

### Semantic Color Tokens

| Semantic Token | Purpose | Dark Value | Light Value |
|---|---|---|---|
| `--color-background` | Application base canvas | Surface 900 (15% L) | Surface 900 (98.5% L) |
| `--color-surface-800` | Primary cards, panels, headers | Surface 800 (20% L) | Surface 800 (94.5% L) |
| `--color-surface-700` | Input controls, secondary surfaces | Surface 700 (25% L) | Surface 700 (89.5% L) |
| `--color-surface-600` | Hover states, elevated chips | Surface 600 (30% L) | Surface 600 (83.0% L) |
| `--color-content-primary` | High-emphasis headings, main text | 95% L (17:1 ratio) | 16% L (15:1 ratio) |
| `--color-content-secondary`| Body copy, descriptions | 84% L (11:1 ratio) | 34% L (9:1 ratio) |
| `--color-content-muted` | Labels, captions, hints | 76% L (7:1 ratio) | 40% L (6:1 ratio) |
| `--color-border-hairline` | Standard dividing lines | `content-primary` 12% | `content-primary` 12% |
| `--color-border-strong` | Emphasized card/modal borders | `content-primary` 20% | `content-primary` 20% |
| `--color-accent` | Primary call to actions | Off-white / monochrome | Off-black |
| `--color-danger` | Destructive actions, errors | OKLCH (Hue 25) | OKLCH (Hue 25) |
| `--color-success` | Confirmations, healthy states | OKLCH (Hue 155) | OKLCH (Hue 155) |
| `--color-warning` | Non-blocking alerts (off-white) | `content-secondary` | `content-secondary` |

> [!IMPORTANT]
> Never write raw hex codes (`#1f2937`) or off-palette Tailwind colors (`text-zinc-500`, `bg-blue-600`) in `.tsx` files. Always use the semantic classes: `text-content-primary`, `bg-surface-800`, `border-line-strong`, `text-danger-text`, `bg-success-subtle`, etc.

---

## 3. Typography System — Assistant

The application universally loads **Assistant** from Google Fonts via Next.js `next/font/google` in `frontend/app/layout.tsx`.

### Typographic Scale

| Token | Rem | Pixels | Usage |
|---|---|---:|---|
| `--text-xs` | `0.6875rem` | 11px | Micro secondary details, badges, tags |
| `--text-sm` | `0.75rem` | 12px | Captions, footnotes, table headers |
| `--text-base-sm` | `0.8125rem` | 13px | Compact body, secondary labels |
| `--text-base` | `0.875rem` | 14px | Standard interface body text |
| `--text-md` | `1rem` | 16px | Large body text, card content |
| `--text-lg` | `1.125rem` | 18px | Sub-section headings, card titles |
| `--text-xl` | `1.25rem` | 20px | Section headers |
| `--text-2xl` | `1.5rem` | 24px | Modal titles, page headers |
| `--text-3xl` | `1.875rem` | 30px | Dashboard titles |
| `--text-4xl` | `2.25rem` | 36px | Primary page hero titles |
| `--text-5xl` | `3rem` | 48px | Landing page titles |
| `--text-6xl` | `3.5rem` | 56px | Hero display headers |

### Weights & Line Heights

- **Regular (400):** Body copy, descriptions.
- **Medium (500):** Navigation items, interactive controls, form inputs.
- **SemiBold (600):** Card headings, section labels, badges.
- **Bold (700):** Main page headings, hero metrics.
- **Line Heights:** Tight (`1.15`), Heading (`1.3`), Normal (`1.5`), Relaxed (`1.65`).

---

## 4. Spacing, Radius, and Shadows

### Spacing Scale
Values are mapped to `--space-N`:
- `--space-1`: 4px
- `--space-2`: 8px
- `--space-3`: 12px
- `--space-4`: 16px
- `--space-5`: 20px
- `--space-6`: 24px
- `--space-8`: 32px
- `--space-10`: 40px
- `--space-12`: 48px
- `--space-16`: 64px

### Border Radii
- `--radius-control` (12px): Buttons, inputs, selects, badges.
- `--radius-card` (20px): Content cards, tiles, panel containers.
- `--radius-modal` (24px): Dialogs, drawers, full modals.
- `rounded-full` (9999px): Avatars, status dots, circular icon buttons.

### Elevation & Shadows
- `--shadow-sm`: 0 1px 2px rgba(0, 0, 0, 0.05) (Subtle buttons, cards)
- `--shadow-md`: 0 4px 12px rgba(0, 0, 0, 0.08) (Floating toolbars)
- `--shadow-lg`: 0 8px 24px rgba(0, 0, 0, 0.12) (Elevated dropdowns)
- `--shadow-popover`: 0 10px 30px rgba(0, 0, 0, 0.16) (Contextual tooltips, menus)
- `--shadow-modal`: 0 16px 48px color-mix(...) (Modal dialogs)

---

## 5. Semantic Z-Index Scale

Arbitrary z-index values (`z-[9999]`, `z-[100]`) are **strictly forbidden**. All layers must use the semantic z-index utilities:

| Class | CSS Custom Property | Value | Purpose |
|---|---|---:|---|
| `z-base` | `--z-base` | 0 | Default content flow |
| `z-surface` | `--z-surface` | 10 | Surface overlays, cursors, canvas grid |
| `z-sticky` | `--z-sticky` | 20 | Sticky headers, canvas action bars, Flow bar |
| `z-drawer` | `--z-drawer` | 30 | Table editor drawer, DBA issue panel, sidebar |
| `z-dropdown` | `--z-dropdown` | 40 | Select popups, context menus |
| `z-modal-backdrop` | `--z-modal-backdrop` | 50 | Dialog backdrops, spotlight masks |
| `z-modal` | `--z-modal` | 60 | Modals, command palette, schema gallery |
| `z-popover` | `--z-popover` | 70 | Tooltips, contextual tour popups |
| `z-toast` | `--z-toast` | 80 | Toast notifications, floating feedback dock |
| `z-max` | `--z-max` | 90 | Critical confirmation alerts (`ConfirmDialog`), offline blockers |

---

## 6. Shared Component Usage Examples

All shared components are imported directly from `@/components/ui`:

```tsx
import {
  Button,
  IconButton,
  Input,
  Select,
  Checkbox,
  Switch,
  FormField,
  Card,
  CardHeader,
  CardBody,
  CardFooter,
  Badge,
  Modal,
  Drawer,
  Tooltip,
  Tabs,
  Table,
  TableHeader,
  TableBody,
  TableRow,
  TableCell,
  TableHead,
  Pagination,
  Skeleton,
  EmptyState,
  Alert,
} from '@/components/ui';
```

### Form Field Example

```tsx
<FormField
  label="Database Name"
  htmlFor="db-name"
  required
  hint="Must be alphanumeric and lowercase."
  error={errors.dbName}
>
  <Input
    id="db-name"
    leftIcon={Database}
    placeholder="e.g. namines_prod"
    value={name}
    onChange={e => setName(e.target.value)}
  />
</FormField>
```

### Modal Example

```tsx
<Modal
  isOpen={isOpen}
  onClose={() => setIsOpen(false)}
  title="Create Migration"
  description="Generate reversible SQL migration script for target database."
  size="md"
>
  <div className="space-y-4">
    <p className="text-xs text-content-secondary">
      Are you sure you want to commit these schema changes?
    </p>
    <div className="flex justify-end gap-2 pt-4">
      <Button variant="ghost" onClick={() => setIsOpen(false)}>
        Cancel
      </Button>
      <Button variant="primary" onClick={handleConfirm}>
        Run Migration
      </Button>
    </div>
  </div>
</Modal>
```

---

## 7. How to Add a New Token or Component

1. **Adding a New CSS Token:**
   - Define the custom property in `:root` and `@theme inline` in [`frontend/app/globals.css`](file:///c:/Users/PC/Desktop/apps/namines/frontend/app/globals.css).
   - If it changes in light mode, add the override under `:root[data-theme="light"]`.
   - Add the typed property to [`frontend/config/design-system.ts`](file:///c:/Users/PC/Desktop/apps/namines/frontend/config/design-system.ts).
   - Run `npm run check:design` to verify it registers without triggering undefined token warnings.

2. **Adding a New Shared Component:**
   - Create `components/ui/<ComponentName>.tsx`.
   - Use semantic design tokens exclusively (no raw hex, no arbitrary radius, no font < 10px).
   - Export it from [`components/ui/index.ts`](file:///c:/Users/PC/Desktop/apps/namines/frontend/components/ui/index.ts).
   - Run `npx tsc --noEmit` and `npm test` to verify types and functionality.

---

## 8. CI Verification Pipeline

Execute before committing:

```bash
# 1. Enforce design tokens and rules (0 violations required)
npm run check:design

# 2. Type check
npx tsc --noEmit

# 3. Unit test suite (212/212 green required)
npm test

# 4. Live browser end-to-end suite (39/39 green required)
node scripts/e2e-live-test.mjs
```
