'use client';

/**
 * Her görünümün üstündeki başlık şeridi — "neredeyim ve bu ekran ne yapar"
 * sorusunu tek yerde yanıtlıyor.
 *
 * Neden ayrı bileşen: v2.1 öncesinde görünümler doğrudan içerikle başlıyordu
 * (bir tablo, bir grafik). Kullanıcı ekrana girdiğinde ne gördüğünü ve
 * sınırlarını (ör. "Logs YALNIZCA yazma işlemlerini gösterir") ancak
 * dokümandan öğrenebiliyordu. Açıklama artık ekranın kendisinde.
 */
export default function PageHead({
  title, desc, actions,
}: {
  title: string;
  desc?: React.ReactNode;
  actions?: React.ReactNode;
}) {
  return (
    <div className="page-head">
      <div>
        <h1 className="page-title">{title}</h1>
        {desc && <p className="page-desc">{desc}</p>}
      </div>
      {actions && <div className="page-actions">{actions}</div>}
    </div>
  );
}
