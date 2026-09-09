import React from 'react';

/**
 * İlk kullanımda boş bir listenin yerini alan açıklama.
 *
 * **Neden ortak bir bileşen:** Desk'te boş durumlar zaten vardı ama iki farklı
 * kalitedeydi. `Projects` ekranı doğru olanı yapıyordu — başlık, sebep ve
 * sonraki adım. Diğerleri tek satırlıktı: "Bu projede üye yok." Bu cümle
 * kullanıcıya *ne olduğunu* söylüyor ama *ne yapacağını* söylemiyor.
 *
 * İlk giriş yapan bir kullanıcı için her liste boştur. O an, ürünün kendini
 * anlatmak için elindeki tek fırsattır; boş bir satır o fırsatı harcar.
 *
 * Filtre sonucu boş kalan listeler bu bileşeni KULLANMAZ — orada kullanıcı ne
 * yapacağını zaten biliyor (filtreyi değiştirecek) ve büyük bir kutu gürültü
 * olurdu.
 */
export default function EmptyState({
  title,
  description,
  action,
}: {
  title: string;
  description: React.ReactNode;
  /** Sonraki adımı atmayı sağlayan düğme/bağlantı. Yoksa yalnızca açıklama gösterilir. */
  action?: React.ReactNode;
}) {
  return (
    <div className="empty">
      <div style={{ fontWeight: 600, color: 'var(--content-primary)', marginBottom: 6 }}>
        {title}
      </div>
      <div style={{ marginBottom: action ? 14 : 0 }}>{description}</div>
      {action}
    </div>
  );
}
