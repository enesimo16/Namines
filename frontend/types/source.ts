/**
 * github/06-EKLENTI-MIMARISI.md — sunucudaki kaynak kataloğunun istemci yüzü.
 *
 * **`kind` ve `capabilities` neden `string`, birlik tipi değil:** backend
 * enum'unu istemcide elle kopyalanmış bir birlik tipiyle eşlemek bu projede
 * bir kez kırıldı — sunucuya yeni bir değer eklendi, kopya güncellenmedi ve
 * istemci sessizce YANLIŞ değeri gösterdi. Alan `string` olunca sunucunun
 * eklediği yeni bir değer yanlış değil, *bilinmeyen* olur; onu tüketen yer
 * (bkz. `SourceMenu`) bilinmeyeni güvenli tarafa koyabilir.
 *
 * Aşağıdaki listeler bu yüzden birer *sözleşme kaydı*: bugün sunucunun ne
 * gönderdiğini belgeliyorlar, gelen değeri kısıtlamıyorlar.
 */
export const SOURCE_KINDS = ['connect', 'import', 'starter'] as const;

export const SOURCE_CAPABILITIES = ['import', 'compare', 'watch', 'writeback'] as const;

export interface SchemaSourceDescriptor {
  id: string;
  displayName: string;
  description: string;
  /** Bugün: 'connect' | 'import' | 'starter'. Bkz. yukarıdaki not. */
  kind: string;
  /** Bugün: 'import' | 'compare' | 'watch' | 'writeback'. */
  capabilities: string[];
  /** Üretilen şema bir çıkarım mı — UI bunu kullanıcıya göstermek zorunda. */
  producesGuess: boolean;
}

/** Okunmayan bir dosya ve NEDENİ; ikisi birlikte gösterilir. */
export interface RepositoryScanSkip {
  name: string;
  reason: string;
}

/** `POST /api/github/scan` yanıtı (github/01-DEPO-TARAMA.md). */
export interface RepositoryScanResult {
  format: string;
  branch: string;
  /** `DatabaseSchema` — canvas store'una olduğu gibi gider. */
  schema: unknown;
  parsedFiles: string[];
  skipped: RepositoryScanSkip[];
  /**
   * GitHub ağacı kesti mi. Kestiyse "deponda şema yok" demek yanlış olur;
   * kullanıcıya bakılmayan bir kısım olduğu söylenmeli.
   */
  treeTruncated: boolean;
  drift?: unknown;
}
