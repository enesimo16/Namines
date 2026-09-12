import type { SchemaTable } from './schema';

/**
 * React Flow düğümlerinin `data` alanı (FE-008 / B-57).
 *
 * **Neden gerekli:** Kod tabanı `(node.data as any)?.table?.color` gibi
 * erişimler yapıyordu. `any` burada iki şeyi birden gizliyor: alan adının
 * yazım hatasını ve alanın gerçekten var olup olmadığını. `tableColor`
 * `undefined` geldiğinde düğüm sessizce varsayılan renkle çizilirdi ve
 * sebebi görünmezdi.
 *
 * `schemaToFlow` düğümleri bu şekille üretiyor; tip onun sözleşmesi.
 */
export interface TableNodeData {
  table: SchemaTable & {
    /** Tuvalde kullanıcının seçtiği renk; verilmemişse varsayılan uygulanır. */
    color?: string;
  };
  [key: string]: unknown;
}

/**
 * Sunucunun bazı uçları şemayı PascalCase döndürüyor (C# serileştirmesi),
 * bazıları camelCase.
 *
 * **Neden tip olarak yazıldı:** `(schema as any).Tables ?? schema.tables`
 * deseni üç ayrı dosyada vardı ve `any` cast'i bu ikiliği gizliyordu —
 * okuyan biri hangi biçimin nereden geldiğini göremiyordu. İsimlendirilmiş
 * bir tip, bunun bir TAVİZ olduğunu görünür kılıyor.
 */
export interface PascalCaseSchema {
  Tables?: unknown[];
  Relations?: unknown[];
}
