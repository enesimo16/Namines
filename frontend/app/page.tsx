import type { Metadata } from 'next';
import Link from 'next/link';
import Hero from '../components/landing/Hero';
import EngineLogosStrip from '../components/landing/EngineLogosStrip';
import ClickClickDone from '../components/landing/ClickClickDone';
import StackRibbon from '../components/landing/StackRibbon';
import ZeroDriftSection from '../components/landing/ZeroDriftSection';
import ArchitectureSpotlights from '../components/landing/ArchitectureSpotlights';
import BuilderGrid from '../components/landing/BuilderGrid';
import SecurityResilience from '../components/landing/SecurityResilience';
import OrbitingClosingCTA from '../components/landing/OrbitingClosingCTA';
import Footer from '../components/layout/Footer';

export const metadata: Metadata = {
  title: 'Namines — AI Database Architecture & Lifecycle Management',
  description: 'Describe a database in plain English. Get a deterministic schema verified against six real SQL dialects with automated migrations and live CRUD admin.',
};

export default function HomePage() {
  return (
    <div className="relative flex flex-col items-center w-full overflow-x-hidden bg-surface-900">
      {/* ───────────────────────── 1. Hero ───────────────────────── */}
      <Hero />

      {/* ────────────────── 2. Engine Logos Strip ────────────────── */}
      <EngineLogosStrip />

      {/* ──────────────── 3. "Click, click, done." ──────────────── */}
      <ClickClickDone />

      {/* ─────────────── 4. Full-Width Stack Ribbon ─────────────── */}
      <StackRibbon />

      {/* ───────────── 5. "Zero Drift" Feature Section ───────────── */}
      <ZeroDriftSection />

      {/* ────────── 6. Deep Architecture Spotlights ────────────── */}
      <div id="spotlights">
        <ArchitectureSpotlights />
      </div>

      {/* ────────── 7. 8-Grid Builder Ecosystem ─────────────────── */}
      <div id="builder-grid">
        <BuilderGrid />
      </div>

      {/* ────────── 8. Security & Resilience by Default ─────────── */}
      <div id="security">
        <SecurityResilience />
      </div>

      {/* ───────────────────────── 9. Orbiting Closing CTA ───────────────────────── */}
      <OrbitingClosingCTA />

      {/* ────────────────────────── 10. Comprehensive Developer Footer ────────────── */}
      <Footer />
    </div>
  );
}
