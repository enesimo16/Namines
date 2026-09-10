'use client';

import { useEffect, useState } from 'react';

/**
 * MFA kurulum QR kodu — TAMAMEN TARAYICIDA üretilir.
 *
 * **Neden bu ayrı bileşen:** Önceki sürümde QR görüntüsü hiç yoktu, çünkü
 * hazır çözümlerin çoğu (chart API'leri, `api.qrserver.com` gibi) otpauth
 * bağlantısını — yani **TOTP sırrını** — üçüncü bir sunucuya URL içinde
 * gönderiyor. Bu, iki faktörlü doğrulamayı kurarken sırrı bir yabancıya
 * vermek demek: kurulumun amacını ortadan kaldırır. O yüzden "QR yok" seçimi
 * bilinçliydi.
 *
 * Bu bileşen o tavizi kaldırıyor: `qrcode` paketi kod çözümlemeyi yerelde
 * yapıyor, sonuç bir `data:` URI. **Ağ isteği yok, sır cihazdan çıkmıyor.**
 *
 * **Neden dinamik import:** Kütüphane yalnızca kullanıcı MFA kurarken —
 * hesabının ömrü boyunca genelde bir kez — gerekiyor. Ana pakete konsaydı
 * herkesin ilk yüklemesini yavaşlatırdı.
 *
 * **Neden hata durumunda görünmüyor:** QR bir kolaylık; elle girilebilir
 * anahtar zaten ekranda ve tek başına yeterli. Üretim başarısız olursa
 * kullanıcıyı çözemeyeceği bir hatayla korkutmak yerine sessizce elle giriş
 * yoluna bırakılıyor.
 */
export default function MfaQrCode({ otpauthUri }: { otpauthUri: string }) {
  const [dataUrl, setDataUrl] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;

    import('qrcode')
      .then(qr => qr.toDataURL(otpauthUri, {
        errorCorrectionLevel: 'M',
        margin: 1,
        width: 176,
        // Karanlık arayüzde de okunabilmesi için ZORUNLU beyaz zemin:
        // QR okuyucular koyu/açık kontrastına dayanır, saydam bir zemin
        // üzerinde ters kontrast oluşup okuma başarısız olabilir.
        color: { dark: '#000000', light: '#ffffff' },
      }))
      .then(url => { if (!cancelled) setDataUrl(url); })
      .catch(() => { /* Elle girilebilir anahtar zaten var; bkz. bileşen notu. */ });

    return () => { cancelled = true; };
  }, [otpauthUri]);

  if (!dataUrl) return null;

  return (
    <div className="flex justify-center">
      {/* eslint-disable-next-line @next/next/no-img-element -- data: URI, next/image optimizasyonu anlamsiz */}
      <img
        src={dataUrl}
        width={176}
        height={176}
        alt="Kimlik dogrulayici uygulamanizla taratabileceginiz QR kod. Taratamiyorsaniz yukaridaki anahtari elle girin."
        className="rounded-[var(--radius-control)] bg-white p-2"
      />
    </div>
  );
}
