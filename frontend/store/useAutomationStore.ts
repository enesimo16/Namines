import { create } from 'zustand';
import type { NaminesFlowEvent } from '../lib/naminesFlowEventBus';
import {
  fetchAutomationRules,
  createAutomationRule,
  updateAutomationRule,
  deleteAutomationRule,
} from '../lib/automationApi';

const genId = (): string =>
  typeof crypto !== 'undefined' && crypto.randomUUID
    ? crypto.randomUUID()
    : Math.random().toString(36).slice(2) + Date.now().toString(36);

/**
 * `addRule`'un dışa açık imzası (Global Constraints) bir `projectId`
 * parametresi ALAMIYOR, ama arka plandaki `createAutomationRule` çağrısı
 * bir projectId'ye ihtiyaç duyuyor. Modül seviyesinde tutulan bu değişken
 * `loadRules` her çağrıldığında (başarılı ya da başarısız fark etmez —
 * en azından "hangi projedeyiz" bilgisi hâlâ doğru) güncelleniyor, böylece
 * `addRule` en son yüklenen projeyi kullanabiliyor.
 */
let currentProjectId: string | null = null;

/**
 * `addRule` çağrıldığında arka planda başlattığı `createAutomationRule`
 * isteği, YEREL id'ye göre burada tutuluyor ve istek çözülünce/reddedilince
 * siliniyor. Amaç: `updateRule`/`deleteRule`/`deleteRulesForTable` aynı
 * kural için create HENÜZ sunucuya ulaşmadan (id takası olmadan) çağrılırsa,
 * arka plan PUT/DELETE'inin var olmayan yerel id'ye gidip sessizce 404
 * almasını önlemek. Promise sunucunun ürettiği GERÇEK id'yi (başarılıysa)
 * ya da `null`'ı (create başarısızsa — güncellenecek/silinecek bir sunucu
 * satırı yok) çözer.
 */
const pendingCreates = new Map<string, Promise<string | null>>();

export type AutomationActionType = 'Webhook' | 'DbaCheck' | 'SeedData' | 'Toast';

export type AutomationConditionField = 'tableName' | 'columnName' | 'columnType';
export type AutomationConditionOp = 'equals' | 'notEquals' | 'contains' | 'startsWith' | 'endsWith';

/** Tetikleyiciyi daraltan tek bir koşul. Koşullar arasında VE mantığı var. */
export interface AutomationCondition {
  field: AutomationConditionField;
  op: AutomationConditionOp;
  value: string;
}

/** Zincirdeki tek bir adım. Sıra, dizideki konumdan geliyor. */
export interface AutomationActionStep {
  actionType: AutomationActionType;
  actionConfig: { url?: string };
}

/** Kuralın en son çalışmasının özeti — listede tek bakışta durum rozeti için. */
export interface AutomationLastRun {
  status: string;
  triggeredAt: string;
  actionType: string;
  errorMessage: string | null;
}

export interface AutomationRule {
  id: string;
  /** Boş string = proje geneli (sunucuda `ScopeTableId == null`). */
  scopeTableId: string;
  name: string;
  triggerType: NaminesFlowEvent['type'];
  conditions: AutomationCondition[];
  actions: AutomationActionStep[];
  enabled: boolean;
  /** Yalnızca sunucudan gelir; yerel olarak oluşturulan kuralda yoktur. */
  lastRun?: AutomationLastRun | null;
}

interface AutomationStoreState {
  rules: AutomationRule[];
  selectedRuleId: string | null;
  loadRules: (projectId: string) => Promise<void>;
  addRule: (scopeTableId: string, triggerType: NaminesFlowEvent['type'], actionType: AutomationActionType) => string;
  updateRule: (
    id: string,
    patch: Partial<Pick<AutomationRule, 'name' | 'scopeTableId' | 'triggerType' | 'conditions' | 'actions' | 'enabled'>>,
  ) => void;
  deleteRule: (id: string) => void;
  deleteRulesForTable: (tableId: string) => void;
  rulesForTable: (tableId: string) => AutomationRule[];
  setSelectedRuleId: (id: string | null) => void;
}

/**
 * Namines Flow kurallarının istemci tarafı deposu.
 *
 * Bölüm 3'ten itibaren sunucudaki `/api/automation/rules`'a (Task 6,
 * AutomationController) bağlı — ama dışa açık imzalar SABİT kaldı ki
 * `AutomationNode`/`AutomationRuleDrawer` hiç değişmesin. Yerel state HER
 * ZAMAN senkron/iyimser güncelleniyor; sunucu çağrıları arka planda
 * (`void ...`) ateşleniyor ve başarısızlıkları sessizce yutuyor — bu
 * store'un sözleşmesi zaten senkron dönüş değerleri gerektiriyor
 * (`addRule` bir id döndürür), bu yüzden bir Promise'e bağlanamaz.
 */
export const useAutomationStore = create<AutomationStoreState>((set, get) => ({
  rules: [],
  selectedRuleId: null,

  loadRules: async (projectId) => {
    // Hangi projede olduğumuz — başarı/başarısızlık fark etmeksizin
    // biliniyor, bu yüzden try/catch'ten ÖNCE set ediliyor.
    currentProjectId = projectId;
    try {
      const rules = await fetchAutomationRules(projectId);
      set({ rules });
    } catch {
      // Ağ hatası: mevcut (muhtemelen boş) state korunur — kullanıcı
      // sayfayı yenileyince tekrar dener. Sessiz başarısızlık burada
      // kabul edilebilir çünkü kural DÜZENLEME hâlâ iyimser çalışır.
    }
  },

  addRule: (scopeTableId, triggerType, actionType) => {
    const id = genId();
    set(state => ({
      rules: [...state.rules, {
        id,
        scopeTableId,
        name: '',
        triggerType,
        conditions: [],
        actions: [{ actionType, actionConfig: {} }],
        enabled: true,
      }],
    }));
    // İyimser: yerel state anında güncellendi, sunucuya arka planda bildiriliyor.
    // Sunucu KENDİ id'sini üretiyor (AutomationRule.Id = Guid.NewGuid()), yani
    // yereldeki `genId()` id'si sunucudakiyle ASLA eşleşmiyor. Bu yüzden POST
    // çözüldüğünde yerel id, sunucunun gerçek id'siyle DEĞİŞTİRİLİYOR — aksi
    // hâlde sonraki PUT/DELETE çağrıları var olmayan bir id'ye gidip 404
    // alıyor, hata sessizce yutuluyor ve kural bir sonraki `loadRules`'ta geri
    // geliyordu.
    //
    // `addRule`'un imzası bir projectId almıyor (Global Constraints), o
    // yüzden `currentProjectId` (en son `loadRules` çağrısından) kullanılıyor.
    // Henüz hiç `loadRules` çalışmadıysa (`currentProjectId === null`) arka
    // plan çağrısı TAMAMEN ATLANIYOR — yanlış/boş bir projectId ile sunucuya
    // istek atıp sessizce 404 almaktansa, kural bir sonraki `loadRules`'a
    // kadar yalnızca istemcide kalıyor. v1 için kabul edilebilir: canvas
    // mount olduğunda `loadRules`'ı her zaman önce çağırıyor (Task 8),
    // kullanıcı "Namines Flow'a Ekle" eylemine ancak ondan sonra ulaşabiliyor.
    if (currentProjectId !== null) {
      const created = createAutomationRule(currentProjectId, scopeTableId, triggerType, actionType);
      // `pendingCreates`'e KAYDEDİLEN promise, id takasını yapan promise'in
      // AYNISI değil — o iş bitene kadar çözülmeyen ayrı bir zincir. Böylece
      // bu create HENÜZ bitmeden gelen updateRule/deleteRule çağrıları onu
      // `await`leyip GERÇEK sonucu (sunucu id'si ya da başarısızlıkta null)
      // öğrenebiliyor.
      const pending = Promise.resolve(created)
        .then((serverRule) => {
          if (!serverRule?.id) return null;
          if (serverRule.id === id) return id;
          // Yerel id → sunucu id takası. Seçili kural buysa seçim de taşınıyor,
          // yoksa kullanıcı düzenleme sırasında drawer'ın seçimini kaybederdi.
          set(state => ({
            rules: state.rules.map(r => (r.id === id ? { ...r, id: serverRule.id } : r)),
            selectedRuleId: state.selectedRuleId === id ? serverRule.id : state.selectedRuleId,
          }));
          return serverRule.id;
        })
        .catch(() => null)
        .finally(() => {
          // Yalnızca HÂLÂ bu create'e ait olan girdiyi temizle — teorik
          // olarak aynı id ikinci bir addRule tarafından yeniden kullanılmış
          // olabilir (id çakışması pratikte olmaz ama savunmacı davranıyoruz).
          if (pendingCreates.get(id) === pending) pendingCreates.delete(id);
        });
      pendingCreates.set(id, pending);
    }
    return id;
  },

  updateRule: (id, patch) => {
    set(state => ({
      rules: state.rules.map(r => r.id === id ? { ...r, ...patch } : r),
    }));
    // Yerel state YUKARIDA senkron güncellendi; sunucuya arka planda
    // (`addRule`/`deleteRule` ile aynı fire-and-forget deseni) yazılıyor.
    // Sunucu kısmi patch kabul etmediği için birleştirilmiş NİHAİ kayıt
    // okunup gönderiliyor. Kural bulunamazsa (ör. eşzamanlı silme) çağrı
    // tamamen atlanıyor.
    //
    // C3 RACE FİKSİ: bu `id` için hâlâ devam eden bir create varsa (kullanıcı
    // kuralı oluşturduktan HEMEN sonra, sunucu yanıtı gelmeden çekmeceyi
    // düzenlediyse), arka plan PUT'u create çözülene kadar ERTELENİYOR —
    // aksi hâlde sunucunun hiç görmediği yerel id'ye gidip sessizce 404 alır
    // ve düzenleme kaybolurdu. Create başarısız olursa (sunucuda satır YOK)
    // PUT tamamen atlanıyor.
    const pendingCreate = pendingCreates.get(id);
    if (pendingCreate) {
      void pendingCreate.then((resolvedId) => {
        if (!resolvedId) return;
        const rule = get().rules.find(r => r.id === resolvedId);
        if (!rule) return;
        void updateAutomationRule(resolvedId, rule)?.catch?.(() => {});
      });
      return;
    }
    const merged = get().rules.find(r => r.id === id);
    if (!merged) return;
    void updateAutomationRule(id, merged)?.catch?.(() => {});
  },

  deleteRule: (id) => {
    set(state => ({
      rules: state.rules.filter(r => r.id !== id),
      selectedRuleId: state.selectedRuleId === id ? null : state.selectedRuleId,
    }));
    // Aynı yarış koşulu updateRule'daki gibi: create hâlâ beklemedeyse
    // önce onu bekleyip sunucu id'siyle sil, başarısızsa hiç çağrı yapma.
    const pendingCreate = pendingCreates.get(id);
    if (pendingCreate) {
      void pendingCreate.then((resolvedId) => {
        if (!resolvedId) return;
        void deleteAutomationRule(resolvedId)?.catch?.(() => {});
      });
      return;
    }
    void deleteAutomationRule(id)?.catch?.(() => {});
  },

  deleteRulesForTable: (tableId) => {
    const toDelete = get().rules.filter(r => r.scopeTableId === tableId);
    set(state => ({ rules: state.rules.filter(r => r.scopeTableId !== tableId) }));
    toDelete.forEach(r => {
      const pendingCreate = pendingCreates.get(r.id);
      if (pendingCreate) {
        void pendingCreate.then((resolvedId) => {
          if (!resolvedId) return;
          void deleteAutomationRule(resolvedId)?.catch?.(() => {});
        });
        return;
      }
      void deleteAutomationRule(r.id)?.catch?.(() => {});
    });
  },

  rulesForTable: (tableId) => get().rules.filter(r => r.scopeTableId === tableId),

  setSelectedRuleId: (id) => set({ selectedRuleId: id }),
}));
