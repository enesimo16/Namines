import { beforeEach, describe, expect, it, vi } from 'vitest';
import { useToastStore } from './useToastStore';

/**
 * `useToastStore` (FE-001 / B-15).
 *
 * **Neden bu store'dan başlandı:** Ürünün kullanıcıya "oldu / olmadı" dediği
 * tek yer. Sessizce bozulursa hiçbir şey çökmez — yalnızca kullanıcı bir daha
 * geri bildirim görmez, ve bunu kimse fark etmez. Bu oturumda tam olarak bu
 * cinsten hatalar (sessiz düşme, sessiz yutma) tekrar tekrar çıktı.
 */
describe('useToastStore', () => {
  beforeEach(() => {
    // Store modül düzeyinde tek örnek; testler arası sızmasın.
    useToastStore.setState({ toasts: [] });
    vi.useRealTimers();
  });

  it('bildirim ekler ve kimliğini döndürür', () => {
    const id = useToastStore.getState().showToast('Kaydedildi', 'success');

    const { toasts } = useToastStore.getState();
    expect(toasts).toHaveLength(1);
    expect(toasts[0].id).toBe(id);
    expect(toasts[0].message).toBe('Kaydedildi');
    expect(toasts[0].type).toBe('success');
  });

  /**
   * AYNI mesaj + AYNI tür ikinci kez eklenmemeli.
   *
   * Tekrarlayan bir istek (ör. yeniden denenen bir senkron) ekranı aynı
   * uyarıyla doldurursa kullanıcı gerçek mesajı kaybeder. Aynı kimliğin
   * dönmesi de önemli: çağıran onu kapatmak için kullanıyor.
   */
  it('aynı mesaj tekrar eklenmez, var olanın kimliği döner', () => {
    const first = useToastStore.getState().showToast('Ağ hatası', 'error');
    const second = useToastStore.getState().showToast('Ağ hatası', 'error');

    expect(second).toBe(first);
    expect(useToastStore.getState().toasts).toHaveLength(1);
  });

  it('aynı mesaj FARKLI türde ayrı bildirim sayılır', () => {
    useToastStore.getState().showToast('Bağlantı', 'error');
    useToastStore.getState().showToast('Bağlantı', 'success');

    expect(useToastStore.getState().toasts).toHaveLength(2);
  });

  it('kapatılan bildirim listeden çıkar', () => {
    const id = useToastStore.getState().showToast('Geçici', 'info');

    useToastStore.getState().dismissToast(id);

    expect(useToastStore.getState().toasts.some(t => t.id === id)).toBe(false);
  });

  it('bilinmeyen kimliği kapatmak durumu bozmaz', () => {
    useToastStore.getState().showToast('Duran', 'info');

    useToastStore.getState().dismissToast('boyle-bir-id-yok');

    expect(useToastStore.getState().toasts).toHaveLength(1);
  });

  it('clearAll hepsini temizler', () => {
    useToastStore.getState().showToast('bir', 'info');
    useToastStore.getState().showToast('iki', 'warning');

    useToastStore.getState().clearAll();

    expect(useToastStore.getState().toasts).toHaveLength(0);
  });

  it('updateToast mesajı ve türü değiştirir', () => {
    const id = useToastStore.getState().showToast('Yükleniyor', 'info');

    useToastStore.getState().updateToast(id, { message: 'Bitti', type: 'success' });

    const toast = useToastStore.getState().toasts.find(t => t.id === id)!;
    expect(toast.message).toBe('Bitti');
    expect(toast.type).toBe('success');
  });

  /**
   * Süresi dolan bildirim KENDİLİĞİNDEN kapanmalı. Kapanmazsa ekranda
   * birikir ve bir süre sonra arayüzü kaplar.
   */
  it('süresi dolunca kendiliğinden kapanır', () => {
    vi.useFakeTimers();

    const id = useToastStore.getState().showToast('Kısa ömürlü', { duration: 1000 });
    expect(useToastStore.getState().toasts.some(t => t.id === id)).toBe(true);

    vi.advanceTimersByTime(5000);

    expect(useToastStore.getState().toasts.some(t => t.id === id)).toBe(false);
  });

  /**
   * `duration: 0` = "ben kapatana kadar dur". Kalıcı bir hatanın kendi
   * kendine kaybolması, kullanıcının hiç görmemesi anlamına gelebilir.
   */
  it('duration 0 verilen bildirim kendiliğinden kapanmaz', () => {
    vi.useFakeTimers();

    const id = useToastStore.getState().showToast('Kalıcı hata', { duration: 0 });

    vi.advanceTimersByTime(60_000);

    expect(useToastStore.getState().toasts.some(t => t.id === id)).toBe(true);
  });
});
