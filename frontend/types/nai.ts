/**
 * Netleştirme akışı ve Namines AI modelleri (new-phase/36 §3).
 */

/** Kullanıcıya sorulan tek bir netleştirici soru. */
export interface ClarifyingQuestion {
  id: string;
  text: string;
  options: string[];
  /**
   * Sorunun neden sorulduğu. Gerekçesiz soru, doldurulacak bir form gibi
   * hissettiriyor — kullanıcı yarıda bırakıyor.
   */
  why: string;
  /** Atlanırsa kullanılacak cevap; her sorunun bir varsayılanı var. */
  defaultOption: string;
}

export interface ClarifyResponse {
  /** Tespit edilen iş türü ('Ecommerce', 'Game', 'Generic', …). */
  archetype: string;
  /**
   * İş türü tanınabildi mi. 'Generic' dönerse sorular geneldir ve bunu
   * gizlemek, alakasız görünen soruları açıklamasız bırakır.
   */
  recognised: boolean;
  questions: ClarifyingQuestion[];
}

/**
 * Plan modu (second-phase/05-PLAN-MODU.md): cevaplardan çıkan deterministik
 * tablo listesi. Model uydurmuyor — bkz. backend/Namines.Core/Analysis/PlanBuilder.cs.
 */
export interface PlannedTable {
  name: string;
  reason: string;
}

export interface SchemaPlan {
  archetype: string;
  tables: PlannedTable[];
  /** Cevaplanmamış sorular için kullanılan varsayımlar — sessiz kalmıyor. */
  assumptions: string[];
  /** Doluysa plan henüz kesin değil; bu soru cevaplanıp plan yeniden istenmeli. */
  followUp: ClarifyingQuestion | null;
  round: number;
  /** Onaylandığında üretim prompt'una eklenecek hazır metin. */
  planSummary: string;
}

/** Plana göre kullanılabilirliğiyle birlikte bir NAI modeli. */
export interface NaiModelOption {
  id: string;
  displayName: string;
  description: string;
  /** Bu modelin günlük bütçeyi kaç kat hızlı tükettiği. */
  costMultiplier: number;
  /** Kullanıcının planı bu modele yetiyor mu. */
  available: boolean;
  isDefault: boolean;
}
