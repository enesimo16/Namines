# Namines Universal MCP — Entegrasyon ve Kurulum Kılavuzu

Namines Model Context Protocol (MCP) sunucusu; **Google Antigravity**, **Cursor**, **Claude Code** ve **GitHub Copilot / OpenAI Codex (VS Code)** gibi yapay zeka kodlama araçlarına deterministik veritabanı analiz, migrasyon kanıtlama, 19 dilde kod üretimi ve canlı sorgulama yetenekleri kazandırır.

---

## 🛠️ Desteklenen Araçlar (Tools)

Namines MCP sunucusu 13 adet birinci sınıf araç sunar:

| Araç Adı | Kapsam / Modül | Ne İşe Yarar? |
|---|---|---|
| `namines_analyze_impact` | **Diagram / Review** | Temel şema ile hedef şema arasındaki riski deterministik olarak hesaplar (`Safe`, `Risky`, `Breaking`, `Destructive`). |
| `namines_prove_migration` | **Run Tests** | DDL kodunu gerçek bir Docker motor konteynerinde çalıştırarak motorun kabul edip etmediğini kanıtlar. |
| `namines_generate_artifact` | **Build / Eject** | Şemayı 19 farklı hedefe (Drizzle, SQLAlchemy, Pydantic, TypeScript SDK, Zod, GraphQL, Gorm vb.) derler. |
| `namines_list_artifact_targets`| **Build / Eject** | Desteklenen tüm hedef dilleri ve ORM kütüphanelerini listeler. |
| `namines_generate_prisma` | **Build / Eject** | Şemadan eksiksiz `schema.prisma` ve ortam değişkeni üretir. |
| `namines_generate_ddl` | **Build / Eject** | 6 veritabanı motoruna (PostgreSQL, MSSQL, MySQL, MariaDB, SQLite, Oracle) özel DDL çıktısı verir. |
| `namines_pull_schema` | **Gateway / Introspect** | Canlı veritabanını salt-okunur analiz edip Namines JSON formatında şemasını çeker. |
| `namines_desk_execute_query` | **Desk** | Bağlı veritabanı üzerinde güvenli, sınırlı ve denetimli SQL sorgusu çalıştırır. |
| `namines_desk_browse_table` | **Desk** | Bir tablodaki kayıtları sayfalı, sıralı ve yüksek yoğunluklu olarak inceler. |
| `namines_hub_search_blueprints`| **Hub** | FinTech, ERP, SaaS, EHR, IoT, E-Commerce gibi 10 onaylı kurumsal mimari şablonunu arar. |
| `namines_hub_get_blueprint` | **Hub** | İlgili mimari şablonunun tüm tablolarını, ilişkilerini ve DBA başlangıç notlarını çeker. |
| `namines_flow_match_rules` | **Flow** | Şema değişikliklerini Namines Flow olay kurallarıyla (webhook, yedekleme vb.) eşleştirir. |
| `namines_open_change_request` | **Hub / Cloud** | İnsan onayı gerektiren şemaları Namines Cloud üzerinde Change Request olarak açar. |

---

## 🚀 Platform Entegrasyonları

### 1. Cursor Entegrasyonu

Depo kökünde [`.cursor/mcp.json`](../.cursor/mcp.json) ve [`.cursorrules`](../.cursorrules) dosyaları hazırdır.

Cursor Ayarlarında (`Settings -> Features -> MCP`):
1. **Name**: `namines`
2. **Type**: `command`
3. **Command**:
   ```bash
   dotnet run --project backend/Namines.Mcp/Namines.Mcp.csproj --no-build
   ```
   *(Veya npx ile: `npx -y @namines/mcp`)*
4. Artık Cursor Composer veya Chat penceresinde `namines_*` araçları otomatik olarak algılanır.

---

### 2. Google Antigravity Entegrasyonu

Antigravity, MCP standart protokolünü JSON-RPC stdio ile destekler:
- Kök dizindeki [`.mcp.json`](../.mcp.json) otomatik olarak tanınır.
- [`skills/namines-schema-review/SKILL.md`](../skills/namines-schema-review/SKILL.md) becerisi sayesinde Antigravity, şema değişikliklerinde önce `namines_analyze_impact` çalıştırır ve yıkıcı değişikliklerde kullanıcıdan onay ister.

---

### 3. Claude Code Entegrasyonu

Claude Code terminalinden doğrudan:
```bash
claude mcp add namines -- dotnet run --project backend/Namines.Mcp/Namines.Mcp.csproj --no-build
```
Veya projedeki [`CLAUDE.md`](../CLAUDE.md) talimatları ile Claude otomatik olarak `namines_*` araçlarını veritabanı işlemlerinde devreye sokar.

---

### 4. VS Code & GitHub Copilot / OpenAI Codex

VS Code'un yerel MCP desteği için [`.vscode/mcp.json`](../.vscode/mcp.json) ve [`.github/copilot-instructions.md`](../.github/copilot-instructions.md) yapılandırılmıştır.

```json
{
  "servers": {
    "namines": {
      "command": "dotnet",
      "args": ["run", "--project", "backend/Namines.Mcp/Namines.Mcp.csproj", "--no-build"]
    }
  }
}
```

Copilot Chat açıldığında `@namines` araçları bağlamda hazır bulunur.
