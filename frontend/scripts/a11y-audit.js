/**
 * Klavye + ekran okuyucu denetimi — TARAYICI konsolunda çalışır (B-42).
 *
 * Neden tarayıcıda: erişilebilir isim, odak görünürlüğü ve dokunma hedefi
 * boyutu ancak HESAPLANMIŞ stil ve düzenle bilinebilir. Statik JSX taraması
 * bunların hiçbirini göremez (`aria-label` bir değişkenden gelebilir, odak
 * halkası CSS'ten gelir, boyut düzenden gelir).
 *
 * KULLANIM: `npm run dev` → sayfayı aç → konsola bu dosyayı yapıştır →
 * `__a11y()` çağır.
 */
window.__a11y = () => {
  const FOCUSABLE = 'a[href],area[href],input:not([disabled]),select:not([disabled]),textarea:not([disabled]),button:not([disabled]),iframe,[contenteditable],[tabindex]:not([tabindex="-1"])';

  const visible = (el) => {
    const cs = getComputedStyle(el);
    if (cs.display === 'none' || cs.visibility === 'hidden') return false;
    const r = el.getBoundingClientRect();
    return r.width > 0 && r.height > 0;
  };

  /** Ekran okuyucunun duyuracağı isim (yaklaşık ama yeterli). */
  const accName = (el) => {
    const aria = el.getAttribute('aria-label');
    if (aria && aria.trim()) return aria.trim();
    const labelledby = el.getAttribute('aria-labelledby');
    if (labelledby) {
      const t = labelledby.split(/\s+/).map((id) => document.getElementById(id)?.textContent || '').join(' ').trim();
      if (t) return t;
    }
    if (el.tagName === 'INPUT' || el.tagName === 'SELECT' || el.tagName === 'TEXTAREA') {
      if (el.labels && el.labels.length) {
        const t = Array.from(el.labels).map((l) => l.textContent.trim()).join(' ').trim();
        if (t) return t;
      }
      const ph = el.getAttribute('placeholder');
      if (ph && ph.trim()) return '(placeholder) ' + ph.trim();
    }
    const txt = (el.textContent || '').trim();
    if (txt) return txt;
    const title = el.getAttribute('title');
    if (title && title.trim()) return '(title) ' + title.trim();
    const img = el.querySelector('img[alt]');
    if (img && img.getAttribute('alt').trim()) return '(img alt) ' + img.getAttribute('alt').trim();
    return '';
  };

  const desc = (el) => {
    const cls = (el.className || '').toString().slice(0, 45);
    return `${el.tagName.toLowerCase()}${cls ? '.' + cls : ''}`;
  };

  // ── 1) İSİMSİZ interaktif öğeler (ekran okuyucu "düğme" der, hepsi bu) ──
  const unnamed = [];
  document.querySelectorAll('button,a[href],[role="button"],[role="link"],[role="tab"],[role="switch"],[role="checkbox"]').forEach((el) => {
    if (!visible(el)) return;
    if (el.getAttribute('aria-hidden') === 'true') return;
    if (!accName(el)) unnamed.push(desc(el));
  });

  // ── 2) Klavyeyle ERİŞİLEMEYEN tıklanabilir öğeler ─────────────────────────
  // REACT'IN KENDİ PROP'LARI okunuyor (`__reactProps$…`), `cursor: pointer`
  // sezgisi DEĞİL. Sezgi denendi ve 74 sonuç verdi; gerçek sayı 10'du —
  // çünkü `cursor` CSS'te MİRAS ALINIYOR, yani tıklanabilir bir div'in tüm
  // torunları da "pointer" görünüyordu. React prop'u kesin bilgi: bu öğeye
  // gerçekten bir `onClick` bağlı mı?
  const reactProps = (el) => {
    const k = Object.keys(el).find((x) => x.startsWith('__reactProps$'));
    return k ? el[k] : null;
  };
  const unreachable = [];
  document.querySelectorAll('*').forEach((el) => {
    const p = reactProps(el);
    if (!p || typeof p.onClick !== 'function') return;
    if (!visible(el)) return;
    if (el.matches(FOCUSABLE) || el.closest(FOCUSABLE)) return;
    unreachable.push({
      el: desc(el),
      role: el.getAttribute('role'),
      // Tuş işleyicisi VARSA `tabindex` ile birlikte kabul edilebilir olabilir.
      klavyeIsleyicisi: typeof p.onKeyDown === 'function' || typeof p.onKeyPress === 'function',
      text: (el.textContent || '').trim().slice(0, 35),
    });
  });

  // ── 3) ODAK GÖRÜNÜRLÜĞÜ ──────────────────────────────────────────────────
  // BU BURADA ÖLÇÜLMÜYOR ve ölçülemez: `el.focus()` PROGRAMATİK odaktır ve
  // Chrome bunun icin `:focus-visible` tetiklemez (kasitli, dogru davranis).
  // Bir onceki surum bunu olcmeye calisti ve 75 odaklanabilir ogenin 73'unu
  // "odak halkasi yok" diye bildirdi -- hepsi YANLIS POZITIFTI.
  //
  // Dogru dogrulama: GERCEK Tab tusuna basmak. Olculdu (12.09.2026):
  // `.skip-link` -> `:focus-visible` true, `outline: 2px solid`. Halka
  // `globals.css`teki global `*:focus-visible { ... !important }` kuralindan
  // geliyor, yani tuvale klavyeyle odaklanan HER oge halkayi aliyor.
  const focusables = Array.from(document.querySelectorAll(FOCUSABLE)).filter(visible);
  const focusRingRule = Array.from(document.styleSheets).some((sh) => {
    try {
      return Array.from(sh.cssRules).some((r) => (r.cssText || '').includes(':focus-visible') && (r.cssText || '').includes('outline'));
    } catch { return false; }
  });

  // ── 4) DOKUNMA HEDEFİ: 44x44 (WCAG 2.5.5) / 24x24 (2.5.8 AA) ────────────
  // Öğenin KUTUSU ölçülemez — yeterli değil. `@utility tap-44` görünmez bir
  // `::after` ile tıklama alanını 44px'e çıkarıyor, yani 22x22 bir düğmenin
  // GERÇEK hedefi 44x44 olabiliyor. Bu yüzden ISABET TESTİ yapılıyor:
  // görsel kutunun dışında ama 44px hedefin içinde bir noktaya
  // `elementFromPoint` atılıp o noktanın hâlâ bu düğmeye denk gelip
  // gelmediğine bakılıyor.
  //
  // `elementFromPoint` görünüm alanı DIŞINDA null döner; o durumda ölçüm
  // yapılamadığı `olculemedi: true` ile bildirilir — "geçti" diye sayılmaz.
  const smallTargets = [];
  focusables.forEach((el) => {
    let r = el.getBoundingClientRect();
    // ETIKET SARIYORSA hedef ETIKETIN kendisidir: `<label>` onay kutusunu ve
    // metnini birlikte sariyorsa kullanici metne de tiklayabilir. 14x14 bir
    // onay kutusu, 256x24 bir etiketin icindeyse kural ihlali YOK.
    // (Bir onceki surum yalnizca kutuyu olcuyor ve 13 sahte hata uretiyordu.)
    if (el.labels && el.labels[0] && el.labels[0].contains(el)) {
      const lr = el.labels[0].getBoundingClientRect();
      if (lr.width > r.width || lr.height > r.height) r = lr;
    }
    if (r.width >= 24 && r.height >= 24) return;
    const cx = Math.round(r.left + r.width / 2);
    const cy = Math.round(r.top + r.height / 2);
    const inView = cx >= 0 && cy >= 0 && cx < window.innerWidth && cy < window.innerHeight;
    let expanded = null;
    if (inView) {
      const probe = document.elementFromPoint(cx + 20, cy);
      expanded = probe ? (probe === el || el.contains(probe) || probe.contains(el)) : false;
    }
    smallTargets.push({
      el: desc(el),
      name: accName(el).slice(0, 30),
      kutu: Math.round(r.width) + 'x' + Math.round(r.height),
      tap44Sinifi: el.classList.contains('tap-44'),
      isabetGenis: expanded,
      olculemedi: !inView,
    });
  });

  // ── 5) BAŞLIK SIRASI ve YER İMLERİ ───────────────────────────────────────
  const headings = Array.from(document.querySelectorAll('h1,h2,h3,h4,h5,h6')).filter(visible)
    .map((h) => ({ level: Number(h.tagName[1]), text: h.textContent.trim().slice(0, 35) }));
  const skips = [];
  headings.forEach((h, i) => {
    if (i === 0) return;
    if (h.level - headings[i - 1].level > 1) skips.push(`${headings[i - 1].level}→${h.level}: "${h.text}"`);
  });

  const landmarks = {
    main: document.querySelectorAll('main,[role="main"]').length,
    nav: document.querySelectorAll('nav,[role="navigation"]').length,
    h1: document.querySelectorAll('h1').length,
    skipLink: document.querySelectorAll('.skip-link,a[href="#main-content"]').length,
  };

  // ── 6) alt'sız görseller ─────────────────────────────────────────────────
  const imgNoAlt = Array.from(document.querySelectorAll('img')).filter(visible)
    .filter((i) => !i.hasAttribute('alt')).map((i) => (i.getAttribute('src') || '').slice(0, 45));

  const uniq = (a) => Array.from(new Set(a.map((x) => JSON.stringify(x)))).map((s) => JSON.parse(s));

  return {
    sayfa: location.pathname,
    odaklanabilirSayisi: focusables.length,
    isimsizKontroller: uniq(unnamed),
    klavyeyleErisilemez: uniq(unreachable),
    odakHalkasiKuraliVar: focusRingRule,
    kucukHedefler: uniq(smallTargets),
    baslikAtlamalari: skips,
    yerImleri: landmarks,
    altsizGorseller: imgNoAlt,
  };
};
