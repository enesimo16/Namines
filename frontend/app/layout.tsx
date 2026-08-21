import type { Metadata } from "next";
import { IBM_Plex_Sans, JetBrains_Mono } from "next/font/google";
import "./globals.css";
import Header from "../components/layout/Header";
import ToastContainer from "../components/toast/ToastContainer";
import ConfirmDialog from "../components/common/ConfirmDialog";
import FeedbackWidget from "../components/feedback/FeedbackWidget";

// Arayüz fontu: IBM Plex Sans — bkz. FRONTEND.md §3 ("Developer Mono" eşleşmesi).
const ibmPlexSans = IBM_Plex_Sans({
  variable: "--font-inter",
  subsets: ["latin"],
  weight: ["300", "400", "500", "600", "700"],
});

// Başlık/kod/tablo-kolon/rakam bloklarında kullanılır (--font-mono).
const jetbrainsMono = JetBrains_Mono({
  variable: "--font-geist-mono",
  subsets: ["latin"],
});

export const metadata: Metadata = {
  title: "Namines — AI Database Architecture Builder",
  description: "Design interactive database architectures in seconds with artificial intelligence.",
};

export default function RootLayout({
  children,
}: Readonly<{
  children: React.ReactNode;
}>) {
  return (
    <html
      lang="en"
      className={`${jetbrainsMono.variable} ${ibmPlexSans.variable} h-full antialiased`}
    >
      {/* NOT: Font Awesome CDN kaldırıldı — render'ı bloklayan harici bir stylesheet'ti
          ve ikonlar lucide-react'e taşındı. İkon setini tek yerde tut. */}
      <body className="min-h-full flex flex-col font-sans">
        <a href="#main-content" className="skip-link">
          Skip to main content
        </a>
        {/* Erişilebilirlik: Screen reader'lar için gizli aria-live bölgesi.
            useToastStore.showToast() her çağrıldığında bu bölgeyi günceller. */}
        <div
          id="aria-live-region"
          aria-live="polite"
          aria-atomic="true"
          className="sr-only"
        />

        {/* V2: Global Header — tüm sayfalarda görünür */}
        <Header />

        {/* has-header: header yüksekliği kadar (52px) padding-top ile başlar */}
        <div className="has-header flex flex-col flex-1" id="main-content">
          {children}
        </div>

        {/* Global bildirim sistemi — tüm sayfalarda sağ alt köşede çalışır */}
        <ToastContainer />

        {/* Global onay dialogu — native confirm() yerine estetik modal */}
        <ConfirmDialog />

        {/* Geri bildirim widget'ı (sol altta yüzen buton) */}
        <FeedbackWidget />
      </body>
    </html>
  );
}
