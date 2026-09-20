/**
 * `AutomationTemplate` (C#) sınıfının istemci tarafı eşleniği.
 *
 * <b>Neden bir kopya var:</b> `Toast` aksiyonu sunucuya HİÇ uğramıyor —
 * tamamen tarayıcıda çalışıyor. Şablonu yalnızca sunucuda çözmek, bildirim
 * metninde ham `{{tableName}}` bırakırdı. İki uygulamanın anlamı AYNI olmak
 * zorunda; testleri de aynı davranışı koruyor.
 */

/** Şablonun okuyabileceği somut değerler. */
export interface FlowTemplateContext {
  tableName?: string;
  columnName?: string;
  columnType?: string;
}

/**
 * TEK GEÇİŞ — sunucudaki uygulamayla aynı nedenden: değişkenleri sırayla
 * değiştirmek, bir DEĞERİN içindeki yer tutucunun sonraki geçişte çözülmesine
 * yol açıyor. Tablo/kolon adları kullanıcıdan geldiği için bu, metne başka bir
 * değişkenin değerini enjekte etmenin yolu olurdu. Yerine konan metin bir daha
 * okunmuyor.
 *
 * Bilinmeyen değişken OLDUĞU GİBİ kalıyor (yazım hatası görünür olsun),
 * değeri olmayan bilinen değişken boş stringe çevriliyor.
 */
export function renderFlowTemplate(
  template: string | undefined,
  trigger: string,
  context: FlowTemplateContext,
  projectName: string,
  now: Date = new Date(),
): string {
  if (!template) return '';
  if (!template.includes('{{')) return template;

  let out = '';
  let i = 0;
  while (i < template.length) {
    const open = template.indexOf('{{', i);
    if (open < 0) { out += template.slice(i); break; }

    const close = template.indexOf('}}', open + 2);
    if (close < 0) { out += template.slice(i); break; }

    out += template.slice(i, open);
    const name = template.slice(open + 2, close);

    const value =
      name === 'trigger' ? trigger
      : name === 'tableName' ? context.tableName ?? ''
      : name === 'columnName' ? context.columnName ?? ''
      : name === 'columnType' ? context.columnType ?? ''
      : name === 'projectName' ? projectName
      : name === 'timestamp' ? now.toISOString()
      : null;

    out += value ?? template.slice(open, close + 2);
    i = close + 2;
  }

  return out;
}
