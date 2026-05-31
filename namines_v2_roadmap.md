# Namines V2 — Mimari Plan & Yol Haritası

> **Durum:** Planlama Aşaması | **Hedef:** V1 Prototip → Profesyonel DevTool  
> **Mevcut Stack:** Next.js 16 (App Router) + Tailwind v4 + @xyflow/react + Zustand v5 | .NET 8 Web API + QuestPDF + Docker.DotNet

---

## 1. Sistem Mimarisi Genel Bakış

### 1.1 Mevcut V1 Durumu (Tespit Edilen Yapı)

```
Frontend
  app/page.tsx          → Landing (prompt form, AI provider seçimi)
  app/canvas/           → React Flow diyagram ekranı
  app/compile/          → DDL/EF Core çıktı ekranı
  store/useSchemaStore.ts → Tek store, kalıcı değil (persist yok)
  
Backend  
  Controllers/          → Schema, Documentation, Compile, Docker, Lint, Voice
  Core/Interfaces/      → IAIService, IDdlGenerator (MSSQL/PG/MySQL), IDocumentationGenerator
  Infrastructure/       → AI/, Generators/DdlGenerator/, Generators/DocumentationGenerator/
```

**Kritik V1 Eksiklikleri:**
- `useSchemaStore` → `persist` middleware yok, sayfa yenilenince her şey sıfırlanıyor
- `dbType` state'i store'da değil, `page.tsx`'in local state'inde yaşıyor
- Header / global navigasyon yok
- Manuel canvas düzenleme yok
- SQLite, Oracle, MariaDB DDL generator yok
- PDF'de AI-driven kapak sayfası yok

---

## 2. Mimari Değişiklikler

### 2.1 Frontend — Yeni Kütüphaneler

| Kütüphane | Versiyon | Faz | Amaç |
|---|---|---|---|
| `html-to-image` | ^1.11 | Faz 1 | Canvas'ı PNG/JPEG olarak export |
| `zustand/middleware` (persist) | (mevcut Zustand v5 içinde) | Faz 2 | Store'u localStorage'a yazma |
| `localforage` | ^1.10 | Faz 2 | IndexedDB üzerinden büyük şema verisi saklama |
| `@radix-ui/react-drawer` veya `vaul` | ^0.9 | Faz 3 | Tablo düzenleme yan panel (Drawer) |
| `@radix-ui/react-context-menu` | ^2.2 | Faz 3 | Canvas sağ tık menüsü |
| `react-hotkeys-hook` | ^4.5 | Faz 3 | Klavye kısayolları (Del, Ctrl+Z) |
| `uuid` | ^9 | Faz 3 | Manuel eklenen tablo/kolon için ID üretimi |

> **Not:** `mermaid` zaten mevcut. `@radix-ui/react-dialog` zaten mevcut. Yeni kurulum minimumda tutuldu.

### 2.2 Frontend — Zustand Store Refactor

**Hedef:** Tek monolitik store'u ikiye bölmek.

#### `useSchemaStore.ts` (REFACTORED — Faz 2+3)
Mevcut store'a eklenecekler:
```
+ persist middleware (localStorage, JSON serializer)
+ projectName: string
+ dbType: 'MSSQL' | 'PostgreSQL' | 'MySQL' | 'SQLite' | 'Oracle' | 'MariaDB'
+ isEditMode: boolean
+ setProjectName(name)
+ setDbType(type)
+ toggleEditMode()
+ addTable(table: TableDefinition)
+ deleteTable(tableId: string)
+ updateTable(table: TableDefinition)
+ addRelation(relation: Relation)
+ deleteRelation(relationId: string)
+ resetProject()  ← Faz 1 "yeni prompt" için
```

#### `useProjectHistoryStore.ts` (YENİ — Faz 2)
```typescript
interface ProjectSnapshot {
  id: string;           // uuid
  name: string;
  createdAt: string;
  updatedAt: string;
  dbType: string;
  schema: DatabaseSchema;
  nodePositions: Record<string, {x: number; y: number}>;
}

interface ProjectHistoryState {
  projects: ProjectSnapshot[];
  activeProjectId: string | null;
  saveCurrentProject(schema, nodes, name, dbType): void;
  loadProject(id: string): ProjectSnapshot | undefined;
  deleteProject(id: string): void;
  renameProject(id: string, name: string): void;
}
// persist → localforage (IndexedDB), büyük şema verisi için
```

### 2.3 Frontend — Yeni Bileşenler

```
components/
  layout/
    Header.tsx              ← Faz 1: Proje adı input + Home butonu
    ProjectSidebar.tsx      ← Faz 2: Geçmiş projeler drawer
  canvas/
    CanvasToolbar.tsx       ← Faz 1+3: İndirme + Edit mode toggle butonları
    TableEditorDrawer.tsx   ← Faz 3: Kolon düzenleme yan panel
    CanvasContextMenu.tsx   ← Faz 3: Sağ tık menü
    nodes/
      EditableTableNode.tsx ← Faz 3: V1 TableNode'un düzenlenebilir versiyonu
  compile/
    DbPushModal.tsx         ← Faz 5: Connection string + Execute butonu
```

### 2.4 Backend — Yeni Interface'ler (Core)

#### `IAIService.cs` — Genişletme (Faz 4)
```csharp
// Mevcut interface'e yeni method eklenir:
Task<string> GenerateProjectSummaryAsync(DatabaseSchema schema, string projectName);
// Amaç: PDF kapak sayfası için AI'dan yönetici özeti üretir
```

#### `IDatabaseExecutor.cs` — YENİ (Faz 5)
```csharp
namespace Namines.Core.Interfaces;

public interface IDatabaseExecutor
{
    Task<ExecutionResult> ExecuteScriptAsync(string connectionString, string ddlScript, DbProviderType provider);
}

public record ExecutionResult(bool Success, string? ErrorMessage, int StatementsExecuted);
```

#### `IDdlGenerator.cs` — Değişiklik yok
Sadece yeni implementasyon sınıfları eklenir (SQLite, Oracle, MariaDB).

### 2.5 Backend — Yeni Controller'lar

#### `DatabaseExecutorController.cs` (Faz 5)
```
POST /api/executor/test-connection  → Bağlantıyı test eder
POST /api/executor/execute          → DDL scriptini hedef DB'ye gönderir
```

### 2.6 Backend — Mevcut Controller Güncellemeleri

#### `DocumentationController.cs` (Faz 4)
```
POST /api/documentation/pdf  → Body'ye projectName alanı eklenir
                               AI ile kapak sayfası üretilip PDF'e eklenir
```

---

## 3. Güncellenmiş Dosya Ağacı

### Frontend (Değişen/Eklenen Dosyalar)

```
frontend/
├── app/
│   ├── layout.tsx                    [MOD] Header ve Sidebar sarmalayıcı eklenir
│   ├── page.tsx                      [MOD] dbType ve projectName store'a taşınır
│   ├── canvas/
│   │   └── page.tsx                  [MOD] Edit mode, context menu, export entegrasyonu
│   └── compile/
│       └── page.tsx                  [MOD] Yeni DB tipleri + DbPushModal
├── components/
│   ├── layout/
│   │   ├── Header.tsx                [YENİ] Faz 1
│   │   └── ProjectSidebar.tsx        [YENİ] Faz 2
│   ├── canvas/
│   │   ├── CanvasToolbar.tsx         [YENİ] Faz 1+3
│   │   ├── TableEditorDrawer.tsx     [YENİ] Faz 3
│   │   ├── CanvasContextMenu.tsx     [YENİ] Faz 3
│   │   └── nodes/
│   │       └── EditableTableNode.tsx [YENİ] Faz 3
│   └── compile/
│       └── DbPushModal.tsx           [YENİ] Faz 5
├── store/
│   ├── useSchemaStore.ts             [MOD] persist + manuel düzenleme actions
│   └── useProjectHistoryStore.ts     [YENİ] Faz 2
├── hooks/
│   ├── useCanvasExport.ts            [YENİ] Faz 1: html-to-image wrapper
│   └── useProjectAutoSave.ts         [YENİ] Faz 2: debounce ile otomatik kayıt
└── types/
    └── project.ts                    [YENİ] ProjectSnapshot type
```

### Backend (Değişen/Eklenen Dosyalar)

```
backend/
├── Namines.Core/
│   └── Interfaces/
│       ├── IAIService.cs             [MOD] GenerateProjectSummaryAsync eklenir
│       └── IDatabaseExecutor.cs      [YENİ] Faz 5
├── Namines.Infrastructure/
│   └── Generators/
│       └── DdlGenerator/
│           ├── SqliteDdlGenerator.cs [YENİ] Faz 4
│           ├── OracleDdlGenerator.cs [YENİ] Faz 4
│           ├── MariaDbDdlGenerator.cs[YENİ] Faz 4
│           └── DdlGeneratorFactory.cs[MOD] Yeni tipler eklenir
│       └── DocumentationGenerator/
│           └── PdfReportGenerator.cs [MOD] AI kapak sayfası entegrasyonu
└── Namines.API/
    ├── Controllers/
    │   ├── DocumentationController.cs[MOD] projectName param
    │   └── DatabaseExecutorController.cs [YENİ] Faz 5
    └── Services/
        └── DatabaseExecutorService.cs    [YENİ] Faz 5
```

---

## 4. Adım Adım Görev Listesi (Checklist)

---

### ✅ FAZ 1: Navigasyon ve Görsel Çıktılar

**Frontend — Zustand Temizliği**
- [ ] `useSchemaStore.ts`'e `projectName: string` ve `dbType` state'i taşı (page.tsx'ten sil)
- [ ] `resetProject()` action'ı ekle (tüm state'i temizler, schema null, nodes/edges boşaltır)
- [ ] `page.tsx`'teki `dbType` local state'ini store'a bağla

**Frontend — Header**
- [ ] `components/layout/Header.tsx` oluştur
  - Sol: Namines logosu + tıklanabilir (resetProject + router.push('/') tetikler)
  - Orta: Proje adı input (inline edit, blur'da store'a yazar)
  - Sağ: GitHub linki veya versiyon badge
- [ ] `app/layout.tsx`'e Header'ı ekle, canvas sayfasında görünür olacak şekilde conditional yap

**Frontend — Canvas Export**
- [ ] `npm install html-to-image` 
- [ ] `hooks/useCanvasExport.ts` hook'unu yaz
  - `exportAsPng()`: React Flow `.react-flow__viewport` elementini `toPng()` ile yakala
  - `exportAsJpeg()`: `toJpeg()` ile yakala
- [ ] `CanvasToolbar.tsx` bileşeni oluştur, PNG ve JPEG butonlarını ekle
- [ ] Mermaid SVG export: `DocumentationController`'dan gelen mermaid string'ini, `Blob` + `URL.createObjectURL` ile `.svg` olarak indir
- [ ] Canvas sayfasına `CanvasToolbar` entegre et

---

### ✅ FAZ 2: Tarayıcı Tabanlı Workspace

**Frontend — Store Persist**
- [ ] `npm install localforage`
- [ ] `useSchemaStore.ts`'e Zustand `persist` middleware ekle (storage: `localStorage`, sadece hafif alanlar)
- [ ] `useProjectHistoryStore.ts` dosyasını oluştur
  - `ProjectSnapshot` interface'ini `types/project.ts`'e yaz
  - `persist` middleware → `localforage` (IndexedDB, storage adapter yaz)
  - `saveCurrentProject`, `loadProject`, `deleteProject`, `renameProject` action'larını implement et

**Frontend — Auto-Save**
- [ ] `hooks/useProjectAutoSave.ts` hook'unu yaz
  - `schema` veya `nodes` değiştiğinde 2 saniye debounce ile `saveCurrentProject` çağırır
  - Canvas sayfasına ekle

**Frontend — Proje Geçmişi Sidebar**
- [ ] `components/layout/ProjectSidebar.tsx` bileşenini oluştur
  - Sol kenar drawer (Radix UI Dialog veya mevcut `@radix-ui/react-dialog` ile)
  - Proje listesi: isim, oluşturma tarihi, DB tipi badge
  - Tıklayınca `loadProject(id)` → `useSchemaStore`'a `loadFromSchema` + node pozisyonlarını geri yükler → `router.push('/canvas')`
  - Silme butonu (onay dialogu)
- [ ] Header'a "Projelerim" butonunu ekle, Sidebar'ı açsın

---

### ✅ FAZ 3: Kanvas Üzerinde Tam Hakimiyet

**Frontend — Store Manuel Düzenleme Actions**
- [ ] `useSchemaStore.ts`'e ekle:
  - `isEditMode: boolean` + `toggleEditMode()`
  - `addTable(table)` → nodes'a yeni node, schema.tables'a yeni tablo ekler
  - `deleteTable(tableId)` → node, ilgili edge'ler ve schema'dan siler
  - `updateTable(table)` → schema'yı ve node data'sını senkron günceller
  - `addRelation(relation)` → edge ve schema.relations'a ekler
  - `deleteRelation(relationId)`

**Frontend — Sağ Tık Menüsü**
- [ ] `npm install @radix-ui/react-context-menu`
- [ ] `CanvasContextMenu.tsx` bileşenini oluştur
  - Canvas boş alanına sağ tık: "Yeni Tablo Ekle" → `addTable()` çağırır, pozisyon olarak tıklanan koordinatı kullanır
  - Node üzerine sağ tık: "Tabloyu Sil" → `deleteTable()` çağırır
- [ ] React Flow `onContextMenu` event'ini yakala, custom menu'yü konumla

**Frontend — Tablo Düzenleme Drawer**
- [ ] `npm install vaul` (veya Radix Sheet)
- [ ] `EditableTableNode.tsx` bileşenini oluştur — mevcut `TableNode`'un düzenlenebilir versiyonu, tıklanınca drawer açar
- [ ] `TableEditorDrawer.tsx` bileşenini oluştur:
  - Tablo adı düzenleme alanı
  - Kolon listesi: her satırda isim, tip (select), uzunluk, PK checkbox, FK dropdown (diğer tablolar)
  - "Kolon Ekle" butonu
  - Kolon silme (satır başındaki x)
  - Tüm değişiklikler anlık olarak `updateTable()` action'ını çağırır → schema otomatik güncellenir
- [ ] `EditableTableNode`'u React Flow node type olarak kaydet
- [ ] Edit mode toggle butonu `CanvasToolbar`'a ekle; edit mode'da node'lar `EditableTableNode` tipine geçer

---

### ✅ FAZ 4: Kurumsal Çıktılar ve Yeni DB Destekleri

**Backend — Yeni DDL Generator'lar**
- [ ] `SqliteDdlGenerator.cs` yaz (mevcut `MssqlDdlGenerator` şablonundan türet)
  - SQLite tip mapping: `NVARCHAR` → `TEXT`, `INT` → `INTEGER`, `UNIQUEIDENTIFIER` → `TEXT`
  - `AUTOINCREMENT` syntax, `FOREIGN KEY` pragma notları
- [ ] `OracleDdlGenerator.cs` yaz
  - Oracle tip mapping: `INT` → `NUMBER(10)`, `NVARCHAR(n)` → `NVARCHAR2(n)`
  - `SEQUENCE` + `TRIGGER` ile auto-increment pattern
- [ ] `MariaDbDdlGenerator.cs` yaz (MySQL generator'dan türet, farklar minimal)
  - `ENGINE=InnoDB`, `AUTO_INCREMENT` syntax
- [ ] `DdlGeneratorFactory.cs`'e `SQLite`, `Oracle`, `MariaDB` case'lerini ekle
- [ ] Frontend `page.tsx` select dropdown'a yeni 3 DB tipini ekle
- [ ] `useSchemaStore`'daki `dbType` union type'ını genişlet

**Backend — AI Kapak Sayfası**
- [ ] `IAIService.cs`'e `Task<string> GenerateProjectSummaryAsync(DatabaseSchema schema, string projectName)` ekle
- [ ] `GroqAIService.cs` ve `OllamaAIService.cs`'e implement et
  - Prompt: Şema adı, tablo sayısı, ilişki sayısı, tablo isimleri verilerek "Yönetici Özeti" paragrafı döndürür
- [ ] `PdfReportGenerator.cs`'i güncelle:
  - `GeneratePdf(schema)` → `GeneratePdf(schema, string projectSummary)` imzasına geç
  - İlk sayfaya: Proje adı, AI üretimi özet paragrafı, oluşturma tarihi, tablo sayısı stats
- [ ] `DocumentationController.cs`'e `projectName` alın, AI summary üretip PDF'e geçin

---

### ✅ FAZ 5: DevOps Mode (Doğrudan DB Push)

**Backend — Database Executor Servisi**
- [ ] `IDatabaseExecutor.cs` interface'ini `Core/Interfaces/`'e ekle
- [ ] `Namines.Infrastructure.csproj`'e NuGet paketleri ekle:
  - `Microsoft.Data.SqlClient` (MSSQL) — zaten olabilir
  - `Npgsql` (PostgreSQL) — zaten olabilir
  - `MySql.Data` veya `MySqlConnector` (MySQL/MariaDB)
  - `Microsoft.Data.Sqlite` (SQLite)
  - `Oracle.ManagedDataAccess.Core` (Oracle)
- [ ] `DatabaseExecutorService.cs` yaz:
  - `DbProviderType`'a göre doğru connection/command factory kullan
  - DDL scriptini `;` ile statement'lara böl, her birini `ExecuteNonQuery` ile çalıştır
  - Transaction içinde çalıştır; hata olursa rollback yap
  - `ExecutionResult` döndür: başarı/hata mesajı + kaç statement çalıştı
- [ ] `DatabaseExecutorController.cs` ekle:
  - `POST /api/executor/test-connection` → `DbConnection.OpenAsync()` ile test et
  - `POST /api/executor/execute` → `IDatabaseExecutor.ExecuteScriptAsync` çağır
- [ ] `Program.cs`'e `IDatabaseExecutor` → `DatabaseExecutorService` DI kaydını ekle

**Frontend — DB Push UI**
- [ ] `DbPushModal.tsx` (Radix Dialog) oluştur:
  - DB provider dropdown (store'daki dbType ile pre-fill)
  - Connection string textarea (placeholder: `Server=...;Database=...;User=...;Password=...`)
  - "Bağlantıyı Test Et" butonu → `POST /api/executor/test-connection`
  - "Veritabanına Gönder" butonu (test başarılıysa aktif) → `POST /api/executor/execute`
  - Sonuç: başarı (kaç statement çalıştı) veya hata mesajı göster
- [ ] Compile sayfasına "Veritabanına Gönder" butonunu ekle, Modal'ı açsın

> [!CAUTION]
> **Güvenlik Notu (Faz 5):** Connection string'ler asla backend'de loglanmamalı, hiçbir zaman persist edilmemeli. Request sadece RAM'de yaşar. Rate limiting ve CORS kuralları production öncesi mutlaka gözden geçirilmeli.

---

## 5. Faz Önceliklendirme ve Süre Tahmini

| Faz | Etki | Karmaşıklık | Tahmini Süre |
|---|---|---|---|
| **Faz 1** — Header + Export | Yüksek (UX) | Düşük | 1-2 gün |
| **Faz 2** — Workspace Hafızası | Çok Yüksek (Core) | Orta | 2-3 gün |
| **Faz 3** — Manuel Kanvas | Çok Yüksek (Core) | Yüksek | 4-5 gün |
| **Faz 4** — Yeni DB + PDF | Orta | Orta | 2-3 gün |
| **Faz 5** — DB Push | Orta | Yüksek | 3-4 gün |

**Önerilen Geliştirme Sırası:** Faz 1 → Faz 2 → Faz 3 → Faz 4 → Faz 5

---

## 6. Kritik Tasarım Kararları

### Persist Stratejisi (Faz 2)
- **Schema verisi** (büyük JSON) → `localforage` (IndexedDB, ~10MB+)
- **UI state** (provider, dbType, editMode) → `localStorage` (Zustand persist built-in)
- **Node pozisyonları** → `ProjectSnapshot` içinde ayrı alan olarak saklanır

### Manuel Düzenleme ↔ Schema Senkronizasyonu (Faz 3)
Her manuel işlem (addTable, updateTable, deleteTable) tek bir `set()` çağrısı ile hem `schema` hem `nodes/edges`'i atomik günceller. `schemaToFlow()` utility fonksiyonu her güncelleme sonrası partial olarak çağrılır; mevcut node pozisyonları `applyRevision` mantığı ile korunur (V1'de bu zaten var).

### Oracle DDL Güçlüğü (Faz 4)
Oracle `AUTO_INCREMENT` yerine `SEQUENCE + BEFORE INSERT TRIGGER` kullanır. `OracleDdlGenerator`'da her PK kolonu için otomatik `CREATE SEQUENCE` + `CREATE TRIGGER` bloğu üretilmeli.
