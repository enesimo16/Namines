import type { Metadata } from 'next';
import './globals.css';

export const metadata: Metadata = {
  title: 'Namines Desk',
  description: 'A deterministic CRUD interface for your database.',
};

export default function RootLayout({ children }: { children: React.ReactNode }) {
  return (
    <html lang="en">
      <body>{children}</body>
    </html>
  );
}
