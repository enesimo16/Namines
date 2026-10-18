import type { Metadata } from 'next';
import Hero from '../components/landing/Hero';
import EngineLogosStrip from '../components/landing/EngineLogosStrip';
import ClickClickDone from '../components/landing/ClickClickDone';
import StackRibbon from '../components/landing/StackRibbon';
import ZeroDriftSection from '../components/landing/ZeroDriftSection';
import ArchitectureSpotlights from '../components/landing/ArchitectureSpotlights';
import BuilderGrid from '../components/landing/BuilderGrid';
import SecurityResilience from '../components/landing/SecurityResilience';
import CommunityFeedbackSection from '../components/landing/CommunityFeedbackSection';
import OrbitingClosingCTA from '../components/landing/OrbitingClosingCTA';
import Footer from '../components/layout/Footer';

const appUrl = (process.env.NEXT_PUBLIC_APP_URL || 'https://namines.app').replace(/\/+$/, '');

export const metadata: Metadata = {
  title: 'Namines — AI Database Architecture & Lifecycle Management',
  description: 'Describe a database in plain English. Get a deterministic schema verified against six real SQL dialects with automated migrations and live CRUD admin.',
  keywords: [
    'AI database design',
    'schema generator',
    'SQL migration risk analyzer',
    'PostgreSQL DDL',
    'MySQL schema builder',
    'SQL Server schema',
    'SQLite strict tables',
    'database lifecycle management',
    'canonical schema IR',
  ],
  alternates: {
    canonical: '/',
    languages: {
      en: '/en',
      tr: '/tr',
      'x-default': '/',
    },
  },
  openGraph: {
    title: 'Namines — AI Database Architecture & Lifecycle Management',
    description: 'Design interactive database architectures and safely govern database changes with deterministic AI validation.',
    url: appUrl,
    siteName: 'Namines',
    images: [
      {
        url: '/brand/namines-app-icon.png',
        width: 512,
        height: 512,
        alt: 'Namines Logo',
      },
    ],
    locale: 'en_US',
    type: 'website',
  },
  twitter: {
    card: 'summary_large_image',
    title: 'Namines — AI Database Architecture & Lifecycle Management',
    description: 'Deterministic database architecture design, automated migrations, and multi-engine schema verification.',
    images: ['/brand/namines-app-icon.png'],
  },
};

const jsonLdData = {
  '@context': 'https://schema.org',
  '@graph': [
    {
      '@type': 'Organization',
      '@id': `${appUrl}/#organization`,
      name: 'Namines',
      url: appUrl,
      logo: `${appUrl}/brand/namines-logo-only.png`,
      sameAs: ['https://github.com/enesimo16/Namines'],
    },
    {
      '@type': 'WebSite',
      '@id': `${appUrl}/#website`,
      url: appUrl,
      name: 'Namines',
      publisher: { '@id': `${appUrl}/#organization` },
    },
    {
      '@type': 'SoftwareApplication',
      '@id': `${appUrl}/#software`,
      name: 'Namines AI Database Engine',
      operatingSystem: 'All',
      applicationCategory: 'DeveloperApplication',
      description: 'Deterministic database architecture, automated migrations, and multi-engine SQL verification.',
      offers: {
        '@type': 'Offer',
        price: '0',
        priceCurrency: 'USD',
      },
      publisher: { '@id': `${appUrl}/#organization` },
    },
  ],
};

export default function HomePage() {
  return (
    <div className="relative flex flex-col items-center w-full overflow-x-hidden bg-surface-900">
      {/* ───────────────────────── SEO Structured Data ───────────────────────── */}
      <script
        type="application/ld+json"
        dangerouslySetInnerHTML={{ __html: JSON.stringify(jsonLdData) }}
      />

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

      {/* ────────── 9. Developer Community & Feedback Section ───── */}
      <CommunityFeedbackSection />

      {/* ───────────────────────── 10. Orbiting Closing CTA ───────────────────────── */}
      <OrbitingClosingCTA />

      {/* ────────────────────────── 10. Comprehensive Developer Footer ────────────── */}
      <Footer />
    </div>
  );
}
