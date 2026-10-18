import type { Metadata } from 'next';
import HomePage from '../page';
import LocaleInitializer from '../../components/layout/LocaleInitializer';

export const metadata: Metadata = {
  title: 'Namines — Yapay Zeka Veritabanı Mimarisi ve Yaşam Döngüsü Yönetimi',
  description: 'Veritabanınızı doğal dille tarif edin. 6 gerçek SQL motoruna karşı doğrulanmış şemalar, otomatik deterministik geçişler ve anında CRUD yönetim panelleri.',
  keywords: ['veritabanı', 'şema', 'yapay zeka', 'mimari', 'migration', 'PostgreSQL', 'MySQL', 'SQL Server', 'SQLite'],
  alternates: {
    canonical: '/tr',
    languages: {
      en: '/en',
      tr: '/tr',
      'x-default': '/',
    },
  },
  openGraph: {
    title: 'Namines — Yapay Zeka Veritabanı Mimarisi ve Yaşam Döngüsü Yönetimi',
    description: 'Doğal dille tarif edin, 6 gerçek SQL motorunda doğrulanmış deterministik şema elde edin.',
    locale: 'tr_TR',
  },
};

export default function TurkishHomePage() {
  return (
    <>
      <LocaleInitializer locale="tr" />
      <HomePage />
    </>
  );
}
