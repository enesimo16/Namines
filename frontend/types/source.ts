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
