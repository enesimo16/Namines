import type { Metadata } from 'next';
import HomePage from '../page';
import LocaleInitializer from '../../components/layout/LocaleInitializer';

export const metadata: Metadata = {
  title: 'Namines — AI Database Architecture & Lifecycle Management',
  description: 'Describe a database in plain English. Get a deterministic schema verified against six real SQL dialects with automated migrations and live CRUD admin.',
  keywords: ['database', 'schema', 'AI', 'architecture', 'migration', 'PostgreSQL', 'MySQL', 'SQL Server', 'SQLite'],
  alternates: {
    canonical: '/en',
    languages: {
      en: '/en',
      tr: '/tr',
      'x-default': '/',
    },
  },
  openGraph: {
    title: 'Namines — AI Database Architecture & Lifecycle Management',
    description: 'Describe what you are building in plain English. Get back a verified multi-engine schema.',
    locale: 'en_US',
  },
};

export default function EnglishHomePage() {
  return (
    <>
      <LocaleInitializer locale="en" />
      <HomePage />
    </>
  );
}
