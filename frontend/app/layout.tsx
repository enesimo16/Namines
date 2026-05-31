import type { Metadata } from "next";
import { Geist, Geist_Mono, Inter } from "next/font/google";
import "./globals.css";
import Header from "../components/layout/Header";

const geistSans = Geist({
  variable: "--font-geist-sans",
  subsets: ["latin"],
});

const geistMono = Geist_Mono({
  variable: "--font-geist-mono",
  subsets: ["latin"],
});

const inter = Inter({
  variable: "--font-inter",
  subsets: ["latin"],
});

export const metadata: Metadata = {
  title: "Namines — AI Veritabanı Mimari Oluşturucu",
  description: "Yapay zeka ile saniyeler içinde interaktif veritabanı mimarileri oluşturun.",
};

export default function RootLayout({
  children,
}: Readonly<{
  children: React.ReactNode;
}>) {
  return (
    <html
      lang="tr"
      className={`${geistSans.variable} ${geistMono.variable} ${inter.variable} h-full antialiased`}
    >
      <head>
        <link href="https://cdnjs.cloudflare.com/ajax/libs/font-awesome/6.4.0/css/all.min.css" rel="stylesheet" />
      </head>
      <body className="min-h-full flex flex-col font-sans">
        {/* V2: Global Header — tüm sayfalarda sabit görünür */}
        <Header />
        {/* has-header: 52px padding-top ile header'ın altında başlar */}
        <div className="has-header flex flex-col flex-1">
          {children}
        </div>
      </body>
    </html>
  );
}
