# F4 — Sunucu Taraflı 3-Yollu Merge — Uygulama Planı

> **Ajan çalışanlar için:** `superpowers:subagent-driven-development` veya
> `superpowers:executing-plans` ile görev görev uygula.

**Hedef:** İki branch'i ortak atalarına bakarak birleştirmek; yalnızca
gerçekten çakışan şeyleri kullanıcıya sormak, gerisini otomatik birleştirmek.

**Mimari:** Birleştirme kararı saf bir sınıfta (`SchemaThreeWayMerger`,
`Namines.Core`) — veritabanı, HTTP ve AI yok. Ortak ata veri modelinde ZATEN
kayıtlı: `Branch.ParentBranchId` + `Branch.ForkedFromVersion`. Uç bu üç şemayı
(base / ours / theirs) yükleyip motoru çağırıyor; arayüz ise var olan
`ConflictResolverModal`'ı besliyor.

**Teknoloji:** .NET 8, xUnit; Next.js 16 + Zustand, Vitest.

**Spec:** [`03-COKLU-GELISTIRICI-MERGE.md`](03-COKLU-GELISTIRICI-MERGE.md),
[`07-FAZ-PLANI.md`](07-FAZ-PLANI.md) F4.

## Bugünkü durum (koddan okundu, dokümandan değil)

- Merge **iki yollu ve tamamen istemcide**:
  `BranchControlPanel.handleStartMerge` → `calculateSchemaDiff(schema, target.schema)`
  → her fark bir "çakışma" olarak `useBranchStore.startMergeSession`'a gidiyor.
- **Ortak ata hiç kullanılmıyor**, dolayısıyla "yalnızca A değiştirdi" ile
  "ikisi de değiştirdi" ayırt edilemiyor: kullanıcıya, aslında otomatik
  birleşebilecek her şey de soruluyor.
- Sunucuda merge motoru **yok** (`backend` içinde `Merge`/`ThreeWay` araması
  boş döndü).
- `Branch.ParentBranchId` ve `Branch.ForkedFromVersion` **var ve dolduruluyor**
  — yani ortak atayı bulmak için yeni bir alan gerekmiyor.
- `SchemaTable` ve `SchemaColumn` `StableUuid` taşıyor; yeniden adlandırmayı
  sil+ekle'den ayıran şey bu.

## Kapsam kararları

1. **Bu dilim = motor + önizleme ucu + arayüz bağlantısı.** Merge kuyruğu ve
   migration versiyonunun merge anında atanması (03 §3.3, §4) integration DB'ye
   ve GitHub akışına bağlı; F2/F3 gelmeden test edilemezler, bu yüzden burada
   yok.
2. **Birleştirmeyi UYGULAMAK bu dilimde yok.** Uç bir ÖNİZLEME döndürüyor;
   kullanıcı seçimlerini yapıp mevcut akışla uyguluyor. Önce "ne olacağını
   doğru söylemek", sonra "yapmak" — tersi, yanlış bir birleştirmeyi kalıcı
   hâle getirir.
3. **Arayüz tanımadığı çakışma türünü DÜŞÜRMEZ**, genel bir satır olarak
   gösterir. Backend enum'unu istemcide birebir kopyalamak bu projede bir kez
   kırıldı (gözlem #3); sunucu yeni bir tür eklediğinde çakışmanın sessizce
   kaybolması, yanlış şema üretmenin en sessiz yolu olurdu.

## Global kısıtlar

- Yorumlar sadece NEDEN için, Türkçe (AGENTS.md).
- **Belirsizlikte en kısıtlayıcı davranışa düş** (`ReferentialActionSql`
  kuralı): emin olunamayan durum otomatik birleşmez, kullanıcıya sorulur ya da
  bloke eder. Şemada "son yazan kazanır" veri kaybıdır.
- Frontend işi `FRONTEND.md`'ye tabi; `npm run check:design` ve `lint` yeşil.
- Commit'i kullanıcı onaylamadan atma.

---

### Görev 1: `SchemaThreeWayMerger` (saf motor)

**Dosyalar:**
- Oluştur: `backend/Namines.Core/Analysis/SchemaThreeWayMerger.cs`
- Test: `backend/Namines.Tests/Analysis/SchemaThreeWayMergerTests.cs`

**Arayüzler:**
- Üretir:
  - `MergeConflictKind` enum: `TableAdded, TableDeleted, TableRenamed,
    ColumnAdded, ColumnDeleted, ColumnModified, NameCollision, DeleteVsModify`
  - `record MergeConflict(string Id, MergeConflictKind Kind, string TableName,
    string? ColumnName, object? OursValue, object? TheirsValue, bool Blocking, string Explanation)`
  - `record ThreeWayMergeResult(DatabaseSchema Merged, IReadOnlyList<MergeConflict> Conflicts,
    IReadOnlyList<string> AutoMerged)`
  - `SchemaThreeWayMerger.Merge(DatabaseSchema baseSchema, DatabaseSchema ours, DatabaseSchema theirs)`

**Karar tablosu** (tablo ve kolon için aynı; eşleşme `StableUuid` ile):

| base | ours | theirs | sonuç |
|---|---|---|---|
| var | değişmedi | değişmedi | otomatik (base) |
| var | değişti | değişmedi | otomatik (ours) |
| var | değişmedi | değişti | otomatik (theirs) |
| var | değişti | değişti, AYNI | otomatik |
| var | değişti | değişti, FARKLI | **çakışma** (`ColumnModified` / `TableRenamed`) |
| var | silindi | değişmedi | otomatik (silinir) |
| var | silindi | değişti | **bloke** (`DeleteVsModify`) |
| yok | eklendi | yok | otomatik (eklenir) |
| yok | eklendi | eklendi (farklı uuid), **aynı ad** | **çakışma** (`NameCollision`) |

- [ ] **Adım 1: Başarısız testi yaz**

```csharp
using System.Collections.Generic;
using System.Linq;
using Namines.Core.Analysis;
using Namines.Core.Models;

namespace Namines.Tests.Analysis;

/// <summary>github/03-COKLU-GELISTIRICI-MERGE.md — 3-yollu birleştirme kararları.</summary>
public class SchemaThreeWayMergerTests
{
    private static SchemaColumn Column(string uuid, string name, string type = "int") =>
        new() { Id = uuid, StableUuid = uuid, Name = name, Type = type };

    private static SchemaTable Table(string uuid, string name, params SchemaColumn[] columns) =>
        new() { Id = uuid, StableUuid = uuid, Name = name, Columns = columns.ToList() };

    private static DatabaseSchema Schema(params SchemaTable[] tables) =>
        new() { Tables = tables.ToList() };

    private static DatabaseSchema Clone(DatabaseSchema s) =>
        System.Text.Json.JsonSerializer.Deserialize<DatabaseSchema>(
            System.Text.Json.JsonSerializer.Serialize(s))!;

    [Fact]
    public void A_change_only_one_side_made_is_merged_without_asking()
    {
        // Bugünkü iki yollu diff'in yapamadığı şey tam olarak bu: ortak ata
        // olmadan "yalnızca onlar değiştirdi" ile "ikimiz de değiştirdik"
        // ayırt edilemiyor ve kullanıcıya HER fark soruluyor.
        var baseSchema = Schema(Table("t1", "users", Column("c1", "email", "varchar")));
        var ours = Clone(baseSchema);
        var theirs = Clone(baseSchema);
        theirs.Tables[0].Columns[0].Type = "text";

        var result = SchemaThreeWayMerger.Merge(baseSchema, ours, theirs);

        Assert.Empty(result.Conflicts);
        Assert.Equal("text", result.Merged.Tables[0].Columns[0].Type);
        Assert.NotEmpty(result.AutoMerged);
    }

    [Fact]
    public void The_same_change_on_both_sides_is_not_a_conflict()
    {
        var baseSchema = Schema(Table("t1", "users", Column("c1", "email", "varchar")));
        var ours = Clone(baseSchema); ours.Tables[0].Columns[0].Type = "text";
        var theirs = Clone(baseSchema); theirs.Tables[0].Columns[0].Type = "text";

        var result = SchemaThreeWayMerger.Merge(baseSchema, ours, theirs);

        Assert.Empty(result.Conflicts);
        Assert.Equal("text", result.Merged.Tables[0].Columns[0].Type);
    }

    [Fact]
    public void Different_changes_to_the_same_column_are_a_conflict()
    {
        var baseSchema = Schema(Table("t1", "users", Column("c1", "status", "varchar")));
        var ours = Clone(baseSchema); ours.Tables[0].Columns[0].Type = "text";
        var theirs = Clone(baseSchema); theirs.Tables[0].Columns[0].Type = "int";

        var result = SchemaThreeWayMerger.Merge(baseSchema, ours, theirs);

        var conflict = Assert.Single(result.Conflicts);
        Assert.Equal(MergeConflictKind.ColumnModified, conflict.Kind);
        Assert.Equal("status", conflict.ColumnName);
        Assert.False(conflict.Blocking);   // insan seçebilir
    }

    [Fact]
    public void Two_people_adding_the_same_column_name_is_caught_as_a_collision()
    {
        // BU, KULLANICININ ANLATTIĞI SENARYO. İki branch'te eklenen kolonların
        // StableUuid'leri FARKLI olur; yalnızca uuid'e bakan bir birleştirici
        // ikisini de ayrı kolon sanıp EKLER ve şema iki "status" kolonuyla
        // bozulur. Ad çakışması ayrıca aranmak zorunda.
        var baseSchema = Schema(Table("t1", "users"));
        var ours = Schema(Table("t1", "users", Column("c-ayse", "status", "varchar")));
        var theirs = Schema(Table("t1", "users", Column("c-mehmet", "status", "int")));

        var result = SchemaThreeWayMerger.Merge(baseSchema, ours, theirs);

        var conflict = Assert.Single(result.Conflicts);
        Assert.Equal(MergeConflictKind.NameCollision, conflict.Kind);
        Assert.Equal("status", conflict.ColumnName);

        // Birleşen şemada İKİ tane "status" kalmamalı — bozuk şema üretmektense
        // çakışmayı bildirip tek bir taraf koymak doğru.
        Assert.Single(result.Merged.Tables[0].Columns.Where(c => c.Name == "status"));
    }

    [Fact]
    public void Deleting_what_the_other_side_changed_blocks_the_merge()
    {
        // "Son yazan kazanır" burada veri kaybıdır: bir taraf kolonu silmiş,
        // diğeri ona güvenerek değiştirmiş. Otomatik seçim yapılmaz.
        var baseSchema = Schema(Table("t1", "users", Column("c1", "status", "varchar")));
        var ours = Schema(Table("t1", "users"));
        var theirs = Clone(baseSchema); theirs.Tables[0].Columns[0].Type = "text";

        var result = SchemaThreeWayMerger.Merge(baseSchema, ours, theirs);

        var conflict = Assert.Single(result.Conflicts);
        Assert.Equal(MergeConflictKind.DeleteVsModify, conflict.Kind);
        Assert.True(conflict.Blocking);
    }

    [Fact]
    public void A_rename_on_one_side_and_a_column_added_on_the_other_merge_cleanly()
    {
        // Satır bazlı merge'in çözemediği, StableUuid sayesinde çözülen durum.
        var baseSchema = Schema(Table("t1", "users", Column("c1", "email", "varchar")));
        var ours = Clone(baseSchema); ours.Tables[0].Name = "accounts";
        var theirs = Clone(baseSchema); theirs.Tables[0].Columns.Add(Column("c2", "phone", "varchar"));

        var result = SchemaThreeWayMerger.Merge(baseSchema, ours, theirs);

        Assert.Empty(result.Conflicts);
        Assert.Equal("accounts", result.Merged.Tables[0].Name);
        Assert.Contains(result.Merged.Tables[0].Columns, c => c.Name == "phone");
    }

    [Fact]
    public void A_table_added_on_one_side_only_is_merged_in()
    {
        var baseSchema = Schema(Table("t1", "users"));
        var ours = Clone(baseSchema);
        var theirs = Schema(Table("t1", "users"), Table("t2", "orders"));

        var result = SchemaThreeWayMerger.Merge(baseSchema, ours, theirs);

        Assert.Empty(result.Conflicts);
        Assert.Equal(2, result.Merged.Tables.Count);
    }

    [Fact]
    public void Two_people_adding_a_table_with_the_same_name_is_a_collision()
    {
        var baseSchema = Schema();
        var ours = Schema(Table("t-ayse", "orders", Column("c1", "id")));
        var theirs = Schema(Table("t-mehmet", "orders", Column("c2", "id")));

        var result = SchemaThreeWayMerger.Merge(baseSchema, ours, theirs);

        var conflict = Assert.Single(result.Conflicts);
        Assert.Equal(MergeConflictKind.NameCollision, conflict.Kind);
        Assert.Single(result.Merged.Tables);
    }

    [Fact]
    public void Identical_branches_produce_nothing_to_do()
    {
        var baseSchema = Schema(Table("t1", "users", Column("c1", "email")));

        var result = SchemaThreeWayMerger.Merge(baseSchema, Clone(baseSchema), Clone(baseSchema));

        Assert.Empty(result.Conflicts);
        Assert.Empty(result.AutoMerged);
    }
}
```

- [ ] **Adım 2: Çalıştır, DERLENMEDİĞİNİ gör**

`dotnet test backend/Namines.Tests --filter SchemaThreeWayMergerTests`
Beklenen: `SchemaThreeWayMerger` yok → derleme hatası.
**"0 test çalıştı" kırmızı sayılmaz** — adı geçen tipin bulunamadığını gör.

- [ ] **Adım 3: Motoru yaz**

Kurallar (yukarıdaki karar tablosu):
1. Tabloları `StableUuid` ile eşle; her üçlüyü (base/ours/theirs) karara bağla.
2. Eşleşen tablolarda kolonları yine `StableUuid` ile eşle, aynı kararı uygula.
3. Tablo adı ve kolon tanımı için "değişti mi" karşılaştırması alan alan
   yapılır; eşitlik için serileştirilmiş karşılaştırma kullanılabilir.
4. Birleştirme bittikten SONRA ad çakışması taraması: aynı isimde iki tablo ya
   da bir tabloda aynı isimde iki kolon kalmışsa, ikisi de base'de YOKSA bu bir
   `NameCollision`'dır; birleşen şemada `ours` tarafı tutulur ve çakışma
   bildirilir (bozuk şema üretmemek için).
5. `DeleteVsModify` → `Blocking = true`. Diğer çakışmalar `Blocking = false`.
6. `AutoMerged`, otomatik uygulanan her değişikliğin insan diliyle kısa
   açıklaması (ör. `"users.phone added from theirs"`).

- [ ] **Adım 4: Testleri çalıştır** → 9 test PASS.

- [ ] **Adım 5: Commit (onay sonrası)**

```bash
git add backend/Namines.Core/Analysis/SchemaThreeWayMerger.cs backend/Namines.Tests/Analysis/SchemaThreeWayMergerTests.cs
git commit -m "feat: merge two branch schemas against their common ancestor"
```

---

### Görev 2: `POST /api/branch/{branchId}/merge/preview`

**Dosyalar:**
- Değiştir: `backend/Namines.API/Controllers/BranchController.cs`
- Test: `backend/Namines.Tests/Controllers/BranchMergePreviewTests.cs`

**Arayüzler:**
- Kullanır: Görev 1'in `SchemaThreeWayMerger`; `Branch.ParentBranchId`,
  `Branch.ForkedFromVersion`, `SchemaVersion`.
- Üretir: `POST /api/branch/{branchId}/merge/preview` →
  `{ baseVersion, sourceBranch, targetBranch, autoMerged[], conflicts[{id,kind,tableName,columnName,ours,theirs,blocking,explanation}], merged }`

**Ortak ata çözümü:** `branch.ParentBranchId` + `branch.ForkedFromVersion` →
o branch'in o versiyonundaki `SchemaVersion.SchemaJson`. **Ata yoksa
(kök branch ya da `ForkedFromVersion` null) uç 400 döner ve nedenini söyler** —
ortak atası olmayan iki şemayı "3-yollu birleştirdik" diye sunmak, iki yollu
diff'i yeni bir adla satmak olurdu.

- [ ] **Adım 1: Başarısız testi yaz** — en az şu üç durum:
  1. Ata çözülüyor ve motorun sonucu dönüyor.
  2. `ForkedFromVersion` null → 400 + açıklayıcı mesaj.
  3. Başka kullanıcının projesine ait branch → mevcut yetki kontrolü ne
     yapıyorsa aynısı (BranchController'daki mevcut kalıbı ORADAN oku, tahmin
     etme).

- [ ] **Adım 2: Çalıştır, düştüğünü gör.**

- [ ] **Adım 3: Ucu yaz.** `BranchController`'ın mevcut yetki/DbContext
  kalıbını birebir izle.

- [ ] **Adım 4: Testleri çalıştır + UYGULAMAYI AYAĞA KALDIR.**
  `dotnet run --project backend/Namines.API` ve uca gerçek bir istek at.
  Bu projede 857 test yeşilken uygulama hiç başlamıyordu (AGENTS.md).

- [ ] **Adım 5: Commit (onay sonrası)**

```bash
git add backend/Namines.API/Controllers/BranchController.cs backend/Namines.Tests/Controllers/BranchMergePreviewTests.cs
git commit -m "feat: preview a branch merge against the common ancestor"
```

---

### Görev 3: Arayüzü sunucuya bağla

**Dosyalar:**
- Değiştir: `frontend/store/useBranchStore.ts` (bilinmeyen tür + otomatik özet)
- Değiştir: `frontend/components/canvas/panels/BranchControlPanel.tsx`
- Değiştir: `frontend/components/canvas/panels/ConflictResolverModal.tsx`
- Değiştir: `frontend/services/api.ts`
- Test: `frontend/components/canvas/panels/ConflictResolverModal.test.tsx` (yeni)

**Sözleşme:** `MergeConflictItem`'a iki alan eklenir — `kind: string` (sunucunun
kategorisi, STRING) ve `blocking: boolean`. Mevcut `type` alanı arayüzün zengin
gösterimi için kalır; **sunucudan gelen tanımadığı bir `kind` DÜŞÜRÜLMEZ**,
genel bir satır olarak gösterilir ve bloke ediyorsa merge düğmesi kapanır.

- [ ] **Adım 1: Başarısız testi yaz** — en az:
  1. Tanınmayan `kind` taşıyan bir çakışma listede GÖRÜNÜR (düşmez).
  2. `blocking: true` bir çakışma varsa "Apply merge" düğmesi devre dışı.
  3. Otomatik birleşenlerin sayısı kullanıcıya gösterilir
     ("12 changes merged automatically").

- [ ] **Adım 2: Çalıştır, düştüğünü gör.**
  Dosya `components/**/*.test.tsx` desenine giriyor; "No test files found"
  görürsen bu kırmızı değil, yol hatasıdır.

- [ ] **Adım 3: `handleStartMerge`'ü sunucuya çevir.** İstemcideki
  `calculateSchemaDiff` tabanlı çakışma üretimi kaldırılır; yerine
  `branchService.mergePreview(branchId)` çağrısı gelir. `calculateSchemaDiff`
  **silinmez** — diff görünümü (`isDiffMode`) onu hâlâ kullanıyor.

- [ ] **Adım 4: Testler ve kontroller**

```bash
cd frontend && npx vitest run && npm run check:design && npm run lint && npx tsc --noEmit
```

- [ ] **Adım 5: CANLI doğrula**

İki branch açıp farklı değişiklikler yap ve gerçek uygulamada gör:
1. Yalnızca bir tarafın değiştirdiği şey **sorulmadan** birleşiyor.
2. Aynı kolona iki farklı tip → çakışma olarak soruluyor.
3. İki tarafta aynı isimde kolon → ad çakışması olarak yakalanıyor.
4. Bir taraf silmiş diğeri değiştirmiş → merge bloke.
5. Ekran görüntüsü al; 1 ve 3 kanıtlanmadan görev bitmiş sayılmaz.

- [ ] **Adım 6: Commit (onay sonrası)**

---

## Öz-denetim

- **Spec kapsamı:** 03 §3.1 (metin çakışması → IR merge) ve §3.2 (semantik
  çakışma sınıflandırması) → Görev 1; §2'nin eşleme modeli → Görev 2'nin ata
  çözümü; §3.3 (versiyon çakışması), §4 (merge kuyruğu) ve §5 (canvas rozeti)
  **bilerek dışarıda** — integration DB ve GitHub akışına bağlılar.
- **Yer tutucu taraması:** Görev 1 Adım 3 kural listesi hâlinde; davranışın
  tamamı Adım 1'deki 9 test tarafından kilitleniyor. Görev 2 ve 3'ün testleri
  madde madde sayıldı ama kodları yazılmadı — ikisi de mevcut dosyalardaki
  kalıbı (yetki kontrolü, modal yapısı) izlemek zorunda ve o kalıp ancak o
  dosyalar okunduğunda doğru kopyalanır. **Bu bilinçli:** F1'de imzaları
  okumadan tahmin eden bir plan adımı yanlış çıktı.
- **Tip tutarlılığı:** `MergeConflictKind` sunucuda enum, sınırda STRING,
  istemcide tanınmayan değeri düşürmeyen bir eşleme.
