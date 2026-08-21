# Namines MCP Sunucusu

Claude Code / Cursor / Zed gibi MCP istemcilerinin Namines'in **deterministik şema
analizi** ve **gerçek motorda kanıtlama** yeteneklerini araç olarak kullanmasını sağlar.

Tasarım kararları: [new-phase/33-MCP-AND-SKILL.md](../../new-phase/33-MCP-AND-SKILL.md)

---

## Ne işe yarar (ve ne işe yaramaz)

Claude zaten `psql` ile şema okuyup migration yazabiliyor — bu araçların vaadi o değil.
Vaat: **Claude'un yazdığı migration'ı, çalıştırmadan önce kanıtlatmak.**

| Claude tek başına | Bu araçlarla |
|---|---|
| Riski *tahmin eder* | `namines_analyze_impact` deterministik **hesaplar** |
| Migration'ın doğru olduğunu *varsayar* | `namines_prove_migration` gerçek motorda **çalıştırıp kanıtlar** |
| Tek motor varsayar | 6 motor, gerçek container'a karşı doğrulanmış DDL |

---

## Araçlar

| Araç | Ne yapar | Docker gerekir mi |
|---|---|---|
| `namines_pull_schema` | Canlı DB'yi okuyup Namines şeması (JSON) döndürür. Salt-okunur. | Hayır |
| `namines_analyze_impact` | İki şemayı karşılaştırıp risk raporu üretir (breaking, veri kaybı, kilit riski, rollback). | Hayır |
| `namines_generate_ddl` | Şemadan hedef motora uygun DDL üretir (6 lehçe, golden-file testli). | Hayır |
| `namines_generate_prisma` | Şemadan `schema.prisma` üretir. **`warnings` dizisini oku ve aktar** — Prisma'nın ifade edemediği yapılar (CHECK, kısmi index) çıktıda YOKTUR. Oracle reddedilir. | Hayır |
| `namines_prove_migration` | Şemayı gerçek, tek kullanımlık bir container'da çalıştırıp motorun kabul edip etmediğini bildirir. | Evet (SQLite hariç) |
| `namines_open_change_request` | Şemayı sunucuda Change Request olarak açar (insan onayı için). **Yazan tek araç** — kullanıcının DB'sine yine dokunmaz. | Hayır |

---

## Kurulum

### En kolay yol — .NET gerekmez

```json
{
  "mcpServers": {
    "namines": {
      "command": "npx",
      "args": ["-y", "@namines/mcp"]
    }
  }
}
```

Sarmalayıcı, platforma uygun self-contained binary'yi GitHub Releases'ten indirir
(`packaging/npm`). Kullanıcıda hiçbir ön koşul yok.

### .NET'i olanlar için

```bash
dotnet tool install -g Namines.Mcp   # MCP sunucusu
dotnet tool install -g Namines.Cli   # aynı çekirdek, terminal komutu: namines
```

```json
{
  "mcpServers": {
    "namines": { "command": "namines-mcp" }
  }
}
```

### Depodan derleyerek

```bash
dotnet publish backend/Namines.Mcp/Namines.Mcp.csproj -c Release -o ./mcp-dist
```

```json
{
  "mcpServers": {
    "namines": {
      "command": "dotnet",
      "args": ["<MUTLAK_YOL>/mcp-dist/namines-mcp.dll"]
    }
  }
}
```

### Politika katmanı (önerilir)

`skills/namines-schema-review/` — MCP "ne yapabilirim"i verir, Skill "ne zaman ve
nasıl"ı. Risk seviyelerinin ne yapmayı zorunlu kıldığı (Destructive/Breaking →
uygula**ma**, insana sor) orada yazılı; bu bir politika olduğu için araç
tanımlarına gömülemez.

İkisini tek adımda kurmak için Claude Code eklentisi:

```
/plugin marketplace add enesimo16/Namines
/plugin install namines@namines
```

Ayrıntı ve elle kurulum: [skills/namines-schema-review/README.md](../../skills/namines-schema-review/README.md)

---

## Tipik akış

```
1. namines_pull_schema   → mevcut DB'nin şeması (base)
2. (Claude değişikliği önerir → hedef şema)
3. namines_analyze_impact(base, hedef, engine) → risk raporu
4. Risk Destructive/Breaking ise: DUR, insana sor
   Risky ise: namines_prove_migration ile gerçek motorda kanıtla
```

---

## Bilinmesi gerekenler

- **localhost çalışır.** Barındırılan Namines API'si SSRF koruması nedeniyle özel/
  ayrılmış adreslere bağlanamaz; bu sunucu kullanıcının kendi makinesinde çalıştığı
  için o kısıt burada geçerli değil (33 §2). Kapatmak için
  `Security__AllowPrivateDbHosts=false` ortam değişkeni.
- **Kullanıcının veritabanına yazan araç YOK.** `open_change_request` bile sunucuda
  bir inceleme açar, migration uygulamaz. Şema değişikliğini otomatik uygulamak
  yanlış gittiğinde sessizce veri kaybettirir (33 §7).
- **`open_change_request` için `NAMINES_API_TOKEN` gerekir**; diğer dört araç tamamen
  çevrimdışı çalışır ve token olmadan da kullanılabilir.
- **Connection string ağdan geçmez** — süreç yerelde çalışır, Namines sunucusuna
  hiçbir şey gönderilmez. Backend'in ayakta olmasına da gerek yoktur.
- **`prove_migration` Docker ister** ve container açılışı nedeniyle 10-30 saniye
  sürer. SQLite dosya tabanlı olduğu için Docker'sız çalışır.
- **stdout protokol kanalıdır.** Sunucu log'ları stderr'e yazar; oraya serbest metin
  yazan bir değişiklik JSON-RPC akışını bozar.

---

## Sınırlar (bilinçli)

- **`generate_migration` bilinçli olarak YOK.** Mevcut
  `MigrationService.GenerateMigrationAsync` migration kodunu Groq'a yazdırıyor; onu
  araç olarak sunmak, BAŞKA bir dil modelinin tahminini "Namines'in deterministik
  çıktısı" kılığında geri vermek olurdu — 33 §3'ün tam tersi. ALTER cümlelerini
  Claude zaten yazabilir; katma değer onu kanıtlamak. Deterministik bir üretici
  yazıldığında (6 motor + golden-file) eklenebilir.
- `pull_schema` motorları: PostgreSQL, MSSQL, MySQL, MariaDB, Oracle.
- `prove_migration` motorları: PostgreSQL, MSSQL, MySQL, SQLite. Diğerleri
  `supported=false` döndürür — var olmayan bir doğrulamayı varmış gibi göstermez.
