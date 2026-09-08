/**
 * Namines Desk — şemadan arayüze DETERMİNİSTİK çeviri.
 *
 * <b>Buranın tamamı saf fonksiyon ve AI YOKTUR.</b> Ürünün vaadi bu: aynı
 * veritabanı her zaman aynı arayüzü üretir. Bir alanın metin mi sayı mı onay
 * kutusu mu olacağı tahmin edilmiyor, `dbintrospect`'in döndürdüğü kolon
 * meta verisinden ÇIKARILIYOR.
 */

export interface ColumnRef {
  table: string;
  column: string;
}

export interface DeskColumn {
  name: string;
  type: string;
  length: number | null;
  isPK: boolean;
  isFK: boolean;
  isNullable: boolean;
  references: ColumnRef | null;
  /**
   * Değeri veritabanı mı üretiyor (SERIAL / IDENTITY / AUTO_INCREMENT)?
   *
   * Sunucu bunu `GET /api/gateway/schema` ile açıkça söylüyor. `undefined`
   * yalnızca bu alanı henüz göndermeyen ESKİ bir backend'de olur — o durumda
   * `insertableColumns` eski tip tahminine düşer.
   */
  isIdentity?: boolean;
  /** Veritabanı tarafında bir varsayılan var mı — NOT NULL olsa bile zorunlu değil. */
  hasDefault?: boolean;
}

export interface DeskTable {
  name: string;
  canWrite: boolean;
  columns: DeskColumn[];
}

/** Formda kullanılacak giriş bileşeni. */
export type FieldKind = 'text' | 'textarea' | 'number' | 'boolean' | 'date' | 'datetime' | 'reference';

/**
 * Kolon tipinden giriş bileşenine eşleme.
 *
 * Motorlar aynı şeyi farklı adlandırıyor (`int4`/`INT`/`NUMBER`), o yüzden
 * eşleme tam ad değil ÖN EK/İÇERİK üzerinden. Tanınmayan tip `text`'e düşer —
 * yanlış bir bileşen göstermektense en genel olanı göstermek daha güvenli.
 */
export function fieldKind(column: DeskColumn): FieldKind {
  // FK önce gelir: tipi ne olursa olsun, hedefi olan bir alan seçim alanıdır.
  if (column.references) return 'reference';

  const t = column.type.toLowerCase();

  if (/(^|\b)(bool|boolean|bit)/.test(t)) return 'boolean';
  if (/(timestamp|datetime)/.test(t)) return 'datetime';
  if (/(^|\b)date/.test(t)) return 'date';
  if (/(int|serial|numeric|decimal|float|double|real|money)/.test(t)) return 'number';
  // Uzunluğu olmayan ya da çok uzun metin → çok satırlı.
  if (/(text|json|xml|clob)/.test(t)) return 'textarea';
  if (column.length !== null && column.length > 255) return 'textarea';

  return 'text';
}

/**
 * Bir satırı tek satırda özetleyen kolon — liste ve FK açılır listelerinde
 * "42" yerine "Ayşe Demir" göstermek için.
 *
 * Sıra bilinçli: insan tarafından okunabilir bir ad alanı varsa o, yoksa ilk
 * metin alanı, o da yoksa birincil anahtar. Hiçbir tahmin yürütmüyor —
 * yalnızca var olan kolon adlarına bakıyor.
 */
export function displayColumn(table: DeskTable): string {
  const preferred = ['name', 'title', 'full_name', 'fullname', 'label', 'email', 'username', 'plate'];
  for (const candidate of preferred) {
    const hit = table.columns.find(c => c.name.toLowerCase() === candidate);
    if (hit) return hit.name;
  }
  const firstText = table.columns.find(c => !c.isPK && fieldKind(c) === 'text');
  if (firstText) return firstText.name;
  return primaryKey(table) ?? table.columns[0]?.name ?? '';
}

/**
 * Güncelleme/silme için anahtar kolon.
 *
 * <b>Bileşik anahtar DESTEKLENMİYOR ve bu bilinçli:</b> Gateway'in
 * `update`/`delete` uçları tek bir `pkColumn`/`pkValue` alıyor. Bileşik
 * anahtarlı bir tabloyu düzenlenebilir göstermek, kaydetme anında sessizce
 * YANLIŞ SATIRI güncellemek demek olurdu. Böyle tablolar salt-okunur gösterilir
 * (bkz. `isEditable`).
 */
export function primaryKey(table: DeskTable): string | null {
  const pks = table.columns.filter(c => c.isPK);
  return pks.length === 1 ? pks[0].name : null;
}

/** Tablo düzenlenebilir mi: yazma izni + tek kolonlu birincil anahtar. */
export function isEditable(table: DeskTable): boolean {
  return table.canWrite && primaryKey(table) !== null;
}

/**
 * Ekleme formunda gösterilecek kolonlar.
 *
 * Otomatik artan birincil anahtar dışarıda: kullanıcıya "id gir" demek hem
 * gereksiz hem de çakışma üretir.
 *
 * <b>Karar sunucunun, tahminin değil.</b> Önceden tip üzerinden tahmin
 * ediliyordu ve kural "PK + tamsayı ⇒ otomatik" idi. Bu, DIŞARIDAN ATANAN bir
 * tamsayı kimliği (başka bir sistemden gelen sipariş numarası gibi) olan her
 * tabloyu kullanılamaz yapıyordu: alan formda hiç görünmüyor, INSERT NOT NULL
 * ihlaliyle düşüyor ve kullanıcının anahtarı verebileceği hiçbir yol kalmıyordu.
 * Sunucu artık `isIdentity` gönderiyor (bkz. GatewayController.Schema).
 */
export function insertableColumns(table: DeskTable): DeskColumn[] {
  return table.columns.filter(c => {
    if (!c.isPK) return true;
    // Sunucu net konuştuysa ona uy.
    if (c.isIdentity !== undefined) return !c.isIdentity;
    // `isIdentity` göndermeyen eski bir backend — eski davranışta kal ki
    // sürüm uyuşmazlığında her ekleme formu birden "id gir" istemesin.
    const t = c.type.toLowerCase();
    return !(/serial|identity/.test(t) || /(int|bigint|smallint)/.test(t));
  });
}

/**
 * Bu alan formda zorunlu mu?
 *
 * NOT NULL tek başına yetmiyor: veritabanı tarafında bir varsayılanı olan
 * (`created_at DEFAULT now()`) NOT NULL bir kolonu zorunlu göstermek,
 * kullanıcıyı sunucunun zaten üreteceği bir değeri elle yazmaya zorlar.
 */
export function isRequired(column: DeskColumn): boolean {
  return !column.isNullable && column.hasDefault !== true;
}

/** Düzenleme formunda gösterilecek kolonlar — anahtar hariç (değiştirilemez). */
export function editableColumns(table: DeskTable): DeskColumn[] {
  const pk = primaryKey(table);
  return table.columns.filter(c => c.name !== pk);
}

/**
 * Boş bir metin kutusunu ne olarak göndermeli.
 *
 * <b>NULL ile boş metin AYNI ŞEY DEĞİL.</b> Nullable bir alanda boş bırakmak
 * "değer yok" demektir; NOT NULL bir alanda ise boş metnin kendisi geçerli bir
 * değerdir. İkisini karıştırmak, veritabanına sessizce yanlış veri yazar.
 */
export function normalizeValue(column: DeskColumn, raw: string): string | null {
  // Onay kutusuna hiç dokunulmadıysa değer '' kalıyor ama ekranda "işaretsiz",
  // yani KULLANICI "false" görüyor. NOT NULL bir kolonda '' göndermek
  // veritabanına `invalid input syntax for type boolean: ""` dedirtiyordu;
  // gördüğü şeyi göndermek doğru cevap.
  if (raw === '' && fieldKind(column) === 'boolean' && !column.isNullable) return 'false';

  if (raw === '' && column.isNullable) return null;
  return raw;
}

/**
 * Veritabanından gelen ham değeri, HTML giriş bileşeninin kabul ettiği biçime
 * çevirir.
 *
 * <b>Neden gerekli:</b> `<input type="datetime-local">` yalnızca
 * `YYYY-MM-DDTHH:mm[:ss]` kabul ediyor; PostgreSQL bir zaman damgasını
 * `2026-09-08T12:08:43.629717` (mikrosaniyeli) olarak döndürüyor. Tarayıcı bunu
 * ayrıştıramayınca alanı BOŞ gösteriyordu — kullanıcı başka bir alanı düzenleyip
 * kaydettiğinde var olan zaman damgası siliniyordu. Aynı sorun `date` alanında
 * saat bileşeni taşıyan bir değerde de vardı.
 *
 * Ayrıştırılamayan bir değer OLDUĞU GİBİ bırakılır — biçimi tahmin edip
 * bozmaktansa ham göstermek yeğdir.
 */
export function formatForInput(column: DeskColumn, raw: string): string {
  if (raw === '') return '';
  const kind = fieldKind(column);

  if (kind === 'date') {
    const m = /^(\d{4}-\d{2}-\d{2})/.exec(raw);
    return m ? m[1] : raw;
  }

  if (kind === 'datetime') {
    // Saniye korunur, saniye altı atılır: datetime-local'ın kabul ettiği en
    // geniş biçim saniyeye kadar.
    const m = /^(\d{4}-\d{2}-\d{2})[T ](\d{2}:\d{2}(?::\d{2})?)/.exec(raw);
    return m ? `${m[1]}T${m[2]}` : raw;
  }

  return raw;
}

/** Hücreyi listede gösterilecek metne çevirir. */
export function formatCell(value: unknown): string {
  if (value === null || value === undefined) return '—';
  if (typeof value === 'boolean') return value ? '✓' : '✗';
  return String(value);
}
