import type { Metadata } from "next";
import { Assistant, Geist_Mono } from "next/font/google";
import "./globals.css";
import Header from "../components/layout/Header";
import ToastContainer from "../components/toast/ToastContainer";
import ConfirmDialog from "../components/common/ConfirmDialog";
import FeedbackWidget from "../components/feedback/FeedbackWidget";
import TourOverlay from "../components/tour/TourOverlay";
import BackToTop from "../components/landing/BackToTop";

/* ─────────────────────────────────────────────────────────────────────────
   FONT YÖNETİM MERKEZİ (Single Source of Truth for Typography)
   Uygulamanın tüm fontları buradan yönetilir:
   - primarySans: Assistant (font-sans)
   - primaryMono: Geist Mono (font-mono)
   ───────────────────────────────────────────────────────────────────────── */
const primarySans = Assistant({
  variable: "--font-app-sans",
  subsets: ["latin"],
  weight: ["400", "500", "600", "700"],
  display: "swap",
  fallback: ["sans-serif"],
});

const primaryMono = Geist_Mono({
  variable: "--font-app-mono",
  subsets: ["latin"],
});

const appUrl = process.env.NEXT_PUBLIC_APP_URL || 'https://namines.app';

export const metadata: Metadata = {
  metadataBase: new URL(appUrl),
  title: {
    default: "Namines — AI Database Architecture & Lifecycle Management",
    template: "%s | Namines",
  },
  description: "AI-native database architecture design, change review governance, migration risk analysis, and full-stack code generation.",
  keywords: ["database", "schema", "AI", "architecture", "migration", "PostgreSQL", "MySQL", "SQL Server", "change review"],
  authors: [{ name: "Namines Team" }],
  creator: "Namines",
  icons: {
    icon: [
      { url: '/icon.png', sizes: '512x512', type: 'image/png' },
      { url: '/icon.svg', type: 'image/svg+xml' },
      { url: '/favicon.svg', type: 'image/svg+xml' },
      { url: '/favicon.ico', sizes: 'any' },
    ],
    apple: [
      { url: '/brand/namines-app-icon.png', sizes: '180x180', type: 'image/png' },
    ],
  },
  openGraph: {
    type: "website",
    locale: "en_US",
    url: appUrl,
    title: "Namines — AI Database Architecture & Lifecycle Management",
    description: "Design interactive database architectures and safely govern database changes with AI.",
    siteName: "Namines",
  },
  twitter: {
    card: "summary_large_image",
    title: "Namines — AI Database Architecture & Lifecycle Management",
    description: "Design interactive database architectures and safely govern database changes with AI.",
  },
};

export default function RootLayout({
  children,
}: Readonly<{
  children: React.ReactNode;
}>) {
  return (
    <html
      lang="en"
      className={`${primaryMono.variable} ${primarySans.variable} font-sans h-full antialiased`}
      suppressHydrationWarning
    >
      <head>
        <script
          dangerouslySetInnerHTML={{
            __html: `
              (function() {
                try {
                  var stored = localStorage.getItem('namines-home-theme-preview');
                  var theme = 'light';
                  if (stored) {
                    var parsed = JSON.parse(stored);
                    if (parsed && parsed.state && parsed.state.theme) {
                      theme = parsed.state.theme;
                    }
                  }
                  document.documentElement.setAttribute('data-theme', theme);
                  if (theme === 'light') {
                    document.documentElement.classList.add('light');
                  } else {
                    document.documentElement.classList.add('dark');
                  }
                } catch (e) {}
              })();
            `,
          }}
        />
      </head>
      {/* NOT: Font Awesome CDN kaldırıldı — render'ı bloklayan harici bir stylesheet'ti
          ve ikonlar lucide-react'e taşındı. İkon setini tek yerde tut. */}
      {/* `suppressHydrationWarning` BODY'de de gerekli: `useHomeThemeStore`
          tema sınıfını/`data-theme`'i <html> ile birlikte <body>'ye de yazıyor
          (store/useHomeThemeStore.ts), ama React'in bastırması aşağı doğru
          MİRAS ALINMIYOR — yalnızca <html>'de olması yetmiyordu. Sonuç: her
          sayfa yüklemesinde konsola "tree hydrated but some attributes
          didn't match" hatası düşüyordu. Bu hata zararsız olduğu hâlde
          GERÇEK hidrasyon hatalarını gürültüye boğuyordu. */}
      <body className="min-h-full flex flex-col font-sans" suppressHydrationWarning>
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
        {/* `<div>` DEĞİL `<main>`: "Skip to main content" bağlantısı vardı ama
            gittiği yer bir landmark değildi — ekran okuyucu kullanıcısı
            atlayabiliyor ama nereye atladığını duyamıyordu. Ölçüldü: sayfada
            `<main>` sayısı 0'dı (bkz. UI_UX_PRODUCT_AUDIT.md §4 / Y2). */}
        <main className="has-header flex flex-col flex-1" id="main-content">
          {children}
        </main>

        {/* Global bildirim sistemi — tüm sayfalarda sağ alt köşede çalışır */}
        <ToastContainer />

        {/* Global onay dialogu — native confirm() yerine estetik modal */}
        <ConfirmDialog />

        {/* Global kılavuz ve rehber turu (Spotlight Tour) */}
        <TourOverlay />

        {/* Geri bildirim widget'ı (sol altta yüzen buton) */}
        <FeedbackWidget />

        {/* Temaya uygun Başa Dön butonu (sağ altta) */}
        <BackToTop />
      </body>
    </html>
  );
}
