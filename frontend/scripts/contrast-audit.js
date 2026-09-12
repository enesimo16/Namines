/**
 * WCAG kontrast denetimi — TARAYICI konsolunda çalışır (B-42).
 *
 * **Node'da çalışmaz ve çalışması da mümkün değil:** ölçüm `getComputedStyle`
 * gerektiriyor. CSS'i statik olarak okumak bu projede yeterli DEĞİL — renkler
 * `oklch()` + `calc()` + tema override'larıyla üretiliyor ve gerçek değer
 * ancak tarayıcı hesapladıktan sonra biliniyor. (Daha önceki bir deneme CSS'i
 * ayrıştırmayı denedi ve `rgb()` varsaydığı için 717 sahte hata üretti.)
 *
 * KULLANIM
 * --------
 *   1. `npm run dev`
 *   2. Sayfayı aç, tarayıcı konsolunu aç
 *   3. Bu dosyanın içeriğini yapıştır
 *   4. `__audit()` çağır
 *
 * ÖZ SINAMA: sonuçtaki `selfTest` **21** olmalı (siyah üstünde beyaz). Başka
 * bir değer görürsen dönüştürücü bozuk demektir; bulgulara güvenme.
 *
 * NE ÖLÇÜLEMEZ
 * ------------
 * `unmeasurable` listesindeki öğelerin görünür rengi `background-image`'dan
 * (gradyan) geliyor; oran hesabı orada anlamsız. Onlar ELLE ölçülmeli —
 * gradyanın TÜM duraklarına karşı. Şerit ve başlık gradyanı böyle ölçüldü.
 *
 * SON ÖLÇÜM (12.09.2026): açık ve koyu temada 10 sayfa, 0 hata.
 */

window.__audit = () => {
  // ── renk ayristirma ───────────────────────────────────────────────────────
  const srgbFromLinear = (x) => x <= 0.0031308 ? 12.92 * x : 1.055 * Math.pow(x, 1 / 2.4) - 0.055;
  const linearFromSrgb = (x) => x <= 0.04045 ? x / 12.92 : Math.pow((x + 0.055) / 1.055, 2.4);

  function oklabToLinearSrgb(L, a, b) {
    const l_ = L + 0.3963377774 * a + 0.2158037573 * b;
    const m_ = L - 0.1055613458 * a - 0.0638541728 * b;
    const s_ = L - 0.0894841775 * a - 1.2914855480 * b;
    const l = l_ ** 3, m = m_ ** 3, s = s_ ** 3;
    return [
      +4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s,
      -1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s,
      -0.0041960863 * l - 0.7034186147 * m + 1.7076147010 * s,
    ];
  }

  /** Herhangi bir CSS rengini {r,g,b,a} (0-255, sRGB) yapar. */
  function parse(str) {
    if (!str) return null;
    str = str.trim();
    if (str === 'transparent') return { r: 0, g: 0, b: 0, a: 0 };
    let m = str.match(/^rgba?\(([^)]+)\)$/);
    if (m) {
      const p = m[1].split(/[,\s/]+/).filter(Boolean).map(Number);
      return { r: p[0], g: p[1], b: p[2], a: p.length > 3 ? p[3] : 1 };
    }
    m = str.match(/^oklch\(([^)]+)\)$/);
    if (m) {
      const raw = m[1].split('/');
      const p = raw[0].trim().split(/\s+/);
      let L = parseFloat(p[0]); if (p[0].includes('%')) L /= 100;
      const C = parseFloat(p[1]);
      let H = parseFloat(p[2]) || 0;
      const a = raw[1] !== undefined ? (raw[1].includes('%') ? parseFloat(raw[1]) / 100 : parseFloat(raw[1])) : 1;
      const hr = H * Math.PI / 180;
      const [lr, lg, lb] = oklabToLinearSrgb(L, C * Math.cos(hr), C * Math.sin(hr));
      const cl = (v) => Math.max(0, Math.min(255, Math.round(srgbFromLinear(Math.max(0, Math.min(1, v))) * 255)));
      return { r: cl(lr), g: cl(lg), b: cl(lb), a };
    }
    return null;
  }

  const relLum = (c) => {
    const f = (v) => linearFromSrgb(v / 255);
    return 0.2126 * f(c.r) + 0.7152 * f(c.g) + 0.0722 * f(c.b);
  };
  const ratio = (c1, c2) => {
    const a = relLum(c1), b = relLum(c2);
    const hi = Math.max(a, b), lo = Math.min(a, b);
    return (hi + 0.05) / (lo + 0.05);
  };
  const over = (fg, bg) => ({
    r: fg.r * fg.a + bg.r * (1 - fg.a),
    g: fg.g * fg.a + bg.g * (1 - fg.a),
    b: fg.b * fg.a + bg.b * (1 - fg.a),
    a: 1,
  });

  // ── ÖZ SINAMA: siyah/beyaz 21.00 vermeli ─────────────────────────────────
  const selfTest = ratio({ r: 0, g: 0, b: 0, a: 1 }, { r: 255, g: 255, b: 255, a: 1 });
  const oklchTest = parse('oklch(100% 0 0)');   // saf beyaz
  const oklchTest2 = parse('oklch(0% 0 0)');    // saf siyah

  /** Ata zincirinde ilk opak arka planı bul (alfa varsa bindir). */
  function effectiveBg(el) {
    const stack = [];
    let node = el;
    while (node && node !== document.documentElement.parentNode) {
      const c = parse(getComputedStyle(node).backgroundColor);
      if (c && c.a > 0) {
        stack.push(c);
        if (c.a >= 0.999) break;
      }
      node = node.parentElement;
    }
    // KOK ZEMIN: beyaz VARSAYMAK koyu temada 4 yanlis pozitif uretiyordu
    // (baslik/serit seffaf arka plana sahip, yurutec tepeye kadar cikip beyaza
    // dusuyordu). Gercek kok zemini oku.
    // Kok zemin bir GRADYAN olabilir (koyu temada body radial-gradient,
     // L %15-%25). Tek bir deger secmek yanlis olurdu: EN KOTU durak
     // kullaniliyor, yani metnin kontrastini en cok dusuren durak.
    return ROOT_BGS.map((root) => {
      let bg = root;
      for (let i = stack.length - 1; i >= 0; i--) bg = over(stack[i], bg);
      return bg;
    });
  }


  /**
   * Herhangi bir CSS rengini sRGB'ye cevirir.
   *
   * NEDEN: derleyici `oklch()` degerlerini bazen `lab()`'e ceviriyor ve
   * `getComputedStyle` bunlari OLDUGU GIBI donduruyor. Elle yazilmis bir
   * ayristirici her bicimi kovalamak zorunda kalirdi; `color-mix(in srgb ...)`
   * tarayiciya donusumu YAPTIRIYOR.
   */
  function toSrgb(css) {
    const d = document.createElement('div');
    d.style.color = 'color-mix(in srgb, ' + css + ' 100%, white 0%)';
    document.body.appendChild(d);
    const c = getComputedStyle(d).color;
    d.remove();
    let m = c.match(/^color\(srgb ([^)]+)\)/);
    if (m) {
      const p = m[1].split(/[\s/]+/).filter(Boolean).map(Number);
      return { r: Math.max(0, Math.min(255, Math.round(p[0] * 255))),
               g: Math.max(0, Math.min(255, Math.round(p[1] * 255))),
               b: Math.max(0, Math.min(255, Math.round(p[2] * 255))), a: 1 };
    }
    return parse(c);
  }

  /** Kok zeminin duraklari: duz renk ya da gradyanin TUM duraklari. */
  function rootBackgrounds() {
    for (const node of [document.body, document.documentElement]) {
      const cs = getComputedStyle(node);
      const solid = parse(cs.backgroundColor);
      if (solid && solid.a >= 0.999) return [solid];
      const img = cs.backgroundImage;
      if (img && img !== 'none') {
        const stops = img.match(/(oklch\([^)]*\)|lab\([^)]*\)|oklab\([^)]*\)|#[0-9a-fA-F]{3,8}|rgba?\([^)]*\))/g);
        if (stops && stops.length) {
          const out = stops.map(toSrgb).filter(Boolean);
          if (out.length) return out;
        }
      }
    }
    return [{ r: 255, g: 255, b: 255, a: 1 }];
  }

  const ROOT_BGS = rootBackgrounds();

  const results = [];
  const unmeasurable = [];
  const seen = new Set();
  document.querySelectorAll('body *').forEach((el) => {
    const cs = getComputedStyle(el);
    if (cs.display === 'none' || cs.visibility === 'hidden' || parseFloat(cs.opacity) === 0) return;
    const r = el.getBoundingClientRect();
    if (r.width === 0 || r.height === 0) return;
    // yalnizca KENDI metin dugumu olanlar
    const text = Array.from(el.childNodes)
      .filter((n) => n.nodeType === 3).map((n) => n.textContent.trim()).join(' ').trim();
    if (!text) return;

    let fg = parse(cs.color);
    if (!fg) return;
    const bgs = effectiveBg(el);
    // En kotu (en dusuk oranli) zemin secilir.
    let bg = bgs[0], got = Infinity;
    for (const cand of bgs) {
      const f = fg.a < 1 ? over(fg, cand) : fg;
      const rr = ratio(f, cand);
      if (rr < got) { got = rr; bg = cand; }
    }
    if (fg.a < 1) fg = over(fg, bg);

    const px = parseFloat(cs.fontSize);
    const weight = parseInt(cs.fontWeight, 10) || 400;
    const large = px >= 24 || (px >= 18.66 && weight >= 700);
    const need = large ? 3.0 : 4.5;

    const key = `${cs.color}|${bg.r},${bg.g},${bg.b}|${large}`;
    if (seen.has(key)) return;
    seen.add(key);

    // Gradyan zeminli ya da seffaf-metin (bg-clip-text) ogeler ORANLA
    // olculemez: gorunur renk `background-image`'dan geliyor. Isaretleniyor ki
    // rapor onlari "hata" gibi gostermesin -- elle olculmeleri gerekiyor.
    // Yukari dogru yurunur ve OPAK bir arka plan rengi bulundugu anda durulur:
    // o renk gradyani zaten kapatiyor demektir. Yalnizca opak bir renkten
    // ONCE gradyan gorulurse oge "olculemez" sayilir. (Onceki hali kor
    // sekilde 4 ata kontrol ediyordu ve body'nin radial gradyani yuzunden
    // opak zeminli ogeleri de isaretliyordu.)
    let gradient = false;
    let node2 = el;
    while (node2 && node2 !== document.body) {
      const s2 = getComputedStyle(node2);
      if (s2.backgroundImage !== 'none') { gradient = true; break; }
      const solid2 = parse(s2.backgroundColor);
      if (solid2 && solid2.a >= 0.999) break;
      node2 = node2.parentElement;
    }
    if (gradient) { unmeasurable.push({ tag: el.tagName.toLowerCase(), cls: (el.className||'').toString().slice(0,60), text: text.slice(0,40) }); return; }

    results.push({
      pass: got >= need, got: Math.round(got * 100) / 100, need,
      fg: cs.color, bg: `rgb(${Math.round(bg.r)},${Math.round(bg.g)},${Math.round(bg.b)})`,
      px, weight, tag: el.tagName.toLowerCase(),
      cls: (el.className || '').toString().slice(0, 70),
      text: text.slice(0, 45),
    });
  });

  const fails = results.filter((r) => !r.pass).sort((a, b) => a.got - b.got);
  return {
    selfTest: Math.round(selfTest * 100) / 100,
    oklchWhite: oklchTest, oklchBlack: oklchTest2,
    theme: document.documentElement.getAttribute('data-theme'),
    uniqueCombos: results.length, failing: fails.length,
    unmeasurable: unmeasurable.length, unmeasurableList: unmeasurable.slice(0, 15),
    fails: fails.slice(0, 40),
  };
};