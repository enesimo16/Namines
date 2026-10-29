# Namines Universal MCP — Harici Kullanıcı ve IDE Entegrasyon Kılavuzu

Namines Model Context Protocol (MCP) sunucusu; **dışarıdaki son kullanıcılar ve geliştiriciler** için sıfır bağımlılıkla çalışacak şekilde tasarlanmıştır.

> [!IMPORTANT]
> **Kullanıcıların makinesinde .NET, C# veya herhangi bir derleyici kurulu olmasına GEREK YOKTUR.**
> Sunucu platforma özel (Windows, macOS, Linux, ARM64) derlenmiş bağımsız binary'leri `npx -y @namines/mcp` üzerinden otomatik olarak çeker. Kullanıcı yalnızca aşağıdaki 5-6 satırlık JSON yapılandırmasını kendi editörüne yapıştırır.

---

## 📋 Hızlı Başvuru Tablosu

| Editör / AI Asistanı | Yapılandırma Dosyası / Yolu | Kurulum Tipi |
|---|---|---|
| **Cursor IDE** | `.cursor/mcp.json` veya `Settings -> Features -> MCP` | Stdio (`npx`) |
| **VS Code (GitHub Copilot Chat)** | `.vscode/mcp.json` | Stdio (`npx`) |
| **Claude Desktop** | `claude_desktop_config.json` | Stdio (`npx`) |
| **Claude Code (CLI)** | Terminal komutu (`claude mcp add`) | CLI argümanı |
| **Windsurf IDE (Codeium)** | `~/.codeium/windsurf/mcp_config.json` | Stdio (`npx`) |
| **Zed Editor** | `~/.config/zed/settings.json` (`context_servers`) | JSON formatı |
| **Roo Code / Cline (VS Code)** | `cline_mcp_settings.json` | Stdio (`npx`) |
| **Continue (VS Code & JetBrains)** | `~/.continue/config.json` | Stdio (`npx`) |
| **Google Antigravity** | `.mcp.json` veya WorkSpace Custom Skill | Stdio (`npx`) |

---

## 1. Cursor Entegrasyonu

Kullanıcı kendi projesinin kök dizinine `.cursor/mcp.json` ekleyebilir ya da Cursor global ayarlarından (`Cursor Settings -> Features -> MCP -> Add New MCP Server`) tanımlayabilir:

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

*İsteğe bağlı:* Projeye eklenen [`.cursorrules`](../.cursorrules) dosyası, Cursor Composer'ın şema işlemlerinde önce `namines_analyze_impact` çağırmasını zorunlu kılar.

---

## 2. VS Code & GitHub Copilot Chat (Codex)

VS Code'un yerel MCP desteği için proje köküne bir `.vscode/mcp.json` dosyası oluşturun (bu depoda hazır bir kopyası yoktur):

```json
{
  "servers": {
    "namines": {
      "command": "npx",
      "args": ["-y", "@namines/mcp"]
    }
  }
}
```

GitHub Copilot Chat, `@namines` araçlarını otomatik olarak tanır ve veri modelleme sorularında devreye sokar.

---

## 3. Claude Desktop (Anthropic)

Claude Desktop uygulamasında ayarlar dosyasını açın:
- **macOS:** `~/Library/Application Support/Claude/claude_desktop_config.json`
- **Windows:** `%APPDATA%\Claude\claude_desktop_config.json`

İçeriğine ekleyin:

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

Claude Desktop yeniden başlatıldığında sağ alttaki çekiç (Tools) simgesinde 13 adet `namines_*` aracı yeşil olarak görünür.

---

## 4. Claude Code (Terminal CLI)

Terminalden tek bir komut çalıştırmak yeterlidir:

```bash
claude mcp add namines -- npx -y @namines/mcp
```

Ayrıca proje kök dizinindeki [`CLAUDE.md`](../CLAUDE.md) talimatları sayesinde Claude Code şema değişikliklerinde otomatik olarak etki analizi ve kanıtlama döngüsünü uygular.

---

## 5. Windsurf IDE (Codeium)

Windsurf yapılandırma dosyasını açın:
`~/.codeium/windsurf/mcp_config.json`

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

Cascade AI, veritabanı düzenlemelerinde Namines araçlarını kullanmaya başlar.

---

## 6. Zed Editor

`~/.config/zed/settings.json` içerisine `context_servers` alanını ekleyin (sunucu adına göre anahtarlanmış bir nesne; dizi değil):

```json
{
  "context_servers": {
    "namines": {
      "source": "custom",
      "command": "npx",
      "args": ["-y", "@namines/mcp"]
    }
  }
}
```

---

## 7. Roo Code & Cline (VS Code Eklentileri)

Eklenti ayarları (`cline_mcp_settings.json` veya Roo Code MCP paneli):

```json
{
  "mcpServers": {
    "namines": {
      "command": "npx",
      "args": ["-y", "@namines/mcp"],
      "disabled": false,
      "autoApprove": []
    }
  }
}
```

---

## 8. Continue (VS Code & JetBrains Eklentisi)

`~/.continue/config.json` dosyasında `experimental.modelContextProtocolServers` altına ekleyin:

```json
{
  "experimental": {
    "modelContextProtocolServers": [
      {
        "transport": {
          "type": "stdio",
          "command": "npx",
          "args": ["-y", "@namines/mcp"]
        }
      }
    ]
  }
}
```

---

## 🛠️ Dış Kullanıcıların Sahip Olduğu 13 Araç

Kullanıcı bu entegrasyonu kurduğu andan itibaren AI asistanı şu yetenekleri kazanır:

1. **`namines_analyze_impact`**: İki şema arasındaki migrasyon riskini hesaplar (`Safe`, `Risky`, `Breaking`, `Destructive`).
2. **`namines_prove_migration`**: DDL'i gerçek Docker veritabanında çalıştırıp kabul edilip edilmediğini kanıtlar.
3. **`namines_generate_artifact`**: Şemadan **19 hedefte** (Drizzle, Prisma, SQLAlchemy, Pydantic, Zod, TypeScript, GraphQL, Gorm vb.) hazır kod üretir.
4. **`namines_list_artifact_targets`**: Tüm desteklenen kod üretimi dillerini listeler.
5. **`namines_generate_prisma`**: `schema.prisma` modellerini üretir.
6. **`namines_generate_ddl`**: 6 veritabanı motoru için (Postgres, MSSQL, MySQL, MariaDB, SQLite, Oracle) hatasız SQL DDL üretir.
7. **`namines_pull_schema`**: Kullanıcının canlı veritabanını tarayarak şemasını JSON olarak çeker.
8. **`namines_desk_execute_query`**: Kullanıcının veritabanında **yalnızca okuma** amaçlı tek bir SELECT / WITH / EXPLAIN / SHOW ifadesi çalıştırır. Yazma (INSERT/UPDATE/DELETE), DDL, `SELECT ... INTO` ve birden çok ifade reddedilir; PostgreSQL/MySQL/MariaDB'de oturum ayrıca salt-okunur açılır. SQLite desteklenmez.
9. **`namines_desk_browse_table`**: Tablo satırlarını sayfalı ve filtreli olarak inceler.
10. **`namines_hub_search_blueprints`**: FinTech, SaaS, ERP, EHR gibi 10 seçilmiş kurumsal mimari şablonunu arar (başlangıç noktasıdır, sertifikasyon değildir).
11. **`namines_hub_get_blueprint`**: İstenen mimari şablonunun tüm tablolarını ve ilişkilerini projeye aktarır.
12. **`namines_flow_match_rules`**: Şema değişikliklerinin tetikleyeceği otomasyon kurallarını denetler.
13. **`namines_open_change_request`**: İnsan onayı için Namines sunucusunda Change Request açar.
