import type { Metadata } from 'next';
import { Assistant, Geist_Mono } from 'next/font/google';
import './globals.css';

const primarySans = Assistant({
  variable: '--font-app-sans',
  subsets: ['latin'],
  weight: ['400', '500', '600', '700'],
  display: 'swap',
  fallback: ['sans-serif'],
});

const primaryMono = Geist_Mono({
  variable: '--font-app-mono',
  subsets: ['latin'],
});

export const metadata: Metadata = {
  title: 'Namines Desk',
  description: 'A deterministic CRUD interface for your database.',
};

export default function RootLayout({ children }: { children: React.ReactNode }) {
  return (
    <html lang="en" className={`${primaryMono.variable} ${primarySans.variable}`}>
      <body>{children}</body>
    </html>
  );
}
