import type { Metadata } from 'next';
import './globals.css';

export const metadata: Metadata = {
  title: 'Namines Desk',
  description: 'Veritabanınız için deterministik CRUD arayüzü.',
};

export default function RootLayout({ children }: { children: React.ReactNode }) {
  return (
    <html lang="tr">
      <body>{children}</body>
    </html>
  );
}
