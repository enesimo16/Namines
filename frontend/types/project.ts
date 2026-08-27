import { DatabaseSchema } from './schema';
import { DbType } from '../store/useSchemaStore';

/** Aktif Docker sandbox / Admin Panel oturumunu temsil eder. */
export interface ActiveSandbox {
  /** "DB" = Docker Sandbox (.bak), "ADMIN" = Streamlit Admin Panel */
  type: 'DB' | 'ADMIN';
  /** Backend'in döndürdüğü job ID */
  jobId: string;
  /** Streamlit URL'i (ADMIN tipi için), ya da indirme URL'i (DB tipi için) */
  url?: string;
  /** Sandbox'ın oluşturulduğu zaman (ISO 8601) */
  createdAt: string;
}

export interface Branch {
  name: string;
  schema: DatabaseSchema;
  nodePositions: Record<string, { x: number; y: number }>;
  createdAt: string;
  updatedAt: string;
  migrationBaseline?: DatabaseSchema | null;
}

/** Tek bir projenin tüm durumunu temsil eder. IndexedDB'de saklanır. */
export interface ProjectSnapshot {
  /** Benzersiz proje ID'si (uuid) */
  id: string;
  /** Kullanıcının verdiği proje adı */
  name: string;
  /** ISO 8601 tarih string'i — ilk kayıt zamanı */
  createdAt: string;
  /** ISO 8601 tarih string'i — son güncelleme zamanı */
  updatedAt: string;
  /** Hedef veritabanı türü */
  dbType: DbType;
  /** AI'ın ürettiği tam şema objesi */
  schema: DatabaseSchema;
  /**
   * Her React Flow node'u için kaydedilen x/y pozisyonu.
   * key: node.id, value: { x, y }
   */
  nodePositions: Record<string, { x: number; y: number }>;
  /**
   * Aktif sandbox/admin panel oturumu.
   * Sayfa yenilendiğinde bu alanı okuyarak geçmiş oturumu geri yükle.
   */
  activeSandbox?: ActiveSandbox | null;
  /**
   * Projenin sahip olduğu yerel dallanmalar (git-like branching).
   */
  branches?: Branch[];
  /**
   * Aktif olarak seçili olan dalın adı (varsayılan: 'main').
   */
  currentBranch?: string;
  migrationBaseline?: DatabaseSchema | null;
  /**
   * Bu şemayı üreten prompt ve netleştirme cevapları —
   * second-phase/09-SEMA-ALTERNATIFLERI.md.
   *
   * Projeyle birlikte saklanıyor çünkü "Alternatif üret" aynı prompt'u tekrar
   * çalıştırıyor. Yalnızca oturum belleğinde tutulduğunda buton, sayfa
   * yenilendiği ya da proje workspace'ten açıldığı anda kalıcı olarak ölüyordu
   * — yani pratikte kullanıcıların çoğu özelliği hiç çalışır görmüyordu.
   *
   * Projeye BAĞLI olması şart: tek bir global değer tutmak, A projesinde
   * B projesinin prompt'uyla "alternatif" üretmek olurdu.
   */
  generationPrompt?: string | null;
  generationAnswers?: Record<string, string> | null;
}

