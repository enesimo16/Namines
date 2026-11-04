# SECURITY AUDIT — Mimari Dönüşüm ve Kiracı Güvenliği Denetimi

> Tarih: 2026-11-03 · Kapsam: 7 Kritik Kontrol (Kiracı İzolasyonu, Migration Kurtarma, Sır Yönetimi, Transaction Sınırları, EF Migration İzolasyonu, Worker Sahipliği, API Geriye Dönük Uyumluluk)

---

## 1. Kritik Güvenlik Kontrolleri ve Durumları

| Kod | Kontrol Alanı | Risk Seviyesi | Mevcut Bulgu | Çözüm & Doğrulama | Durum |
|---|---|---|---|---|---|
| **SEC-01** | **Cache Tenant & Schema Isolation** | **HIGH** | `SemanticCacheService` şema özetinde `Indexes`, `CheckConstraints`, `UniqueConstraints`, `IsNullable`, `DefaultValue` alanlarını yok sayıyor ve kiracı/kullanıcı kapsamı (`tenantScope`) içermiyor. | `SemanticCacheService` içine tam kanonik şema özeti + `tenantScope` parametresi eklendi; `TenantCacheKey` yardımcısı `BuildingBlocks` içine alındı (`ModularMonolithArchitectureTests` ile doğrulandı). | ✅ Tamamlandı |
| **SEC-02** | **Raw Exception Message Disclosure** | **MEDIUM** | `AIDbaController` ve `SmartSeedController` HTTP 500 yanıtında ham `ex.Message` döndürüyor. | Ham istisna yalnızca Serilog'a yazılır (`PiiRedactionEnricher` korumalı); istemciye sabit güvenli hata mesajı döner. | ✅ Tamamlandı |
| **SEC-03** | **Tenant Isolation (`ITenantProjectAuthority`)** | **CRITICAL** | `VaultBackup`, `VaultRestore`, `VaultSchedule`, `GroundDatabase` üzerindeki geçişli `OrganizationId` sütunu proje sahipliği/org devrinde bayat kalabilir. | Yetki kontrolü `ITenantProjectAuthority` üzerinden her zaman canlı `Project.OrganizationId -> OrganizationMembers` zincirinden yapılır; `VaultController`, `GroundController`, `AutomationController` tamamen `ITenantProjectAuthority` kullanır. | ✅ Tamamlandı |
| **SEC-04** | **Connection String Secret Isolation** | **CRITICAL** | `CloudProject.EncryptedConnectionString` genel proje varlığında bulunuyor; modüller doğrudan `CloudProject` okuyordu. | `IProjectConnectionSecretProvider` sözleşmesi ile bağlantı sırrı erişimi izole edildi ve 3NF `studio.project_connections` (`ProjectConnection`) tablosuna ayrıldı. | ✅ Tamamlandı |
| **SEC-05** | **Plaintext ShareToken Removal (DB-34)** | **HIGH** | `CloudProjects.ShareToken` düz metin olarak tabloda duruyordu. | `studio.schema_share_links` (`SchemaShareLink`) tablosunda yalnızca 64 karakterlik SHA-256 `TokenHash` tutulur (`SchemaShareLink.ComputeTokenHash`); veri taşıma betiğinde `encode(sha256(...), 'hex')` kullanılır. | ✅ Tamamlandı |
| **SEC-06** | **SSRF & Host Access Policy Enforcement** | **HIGH** | `Vault`, `Ground`, `Gateway`, `AutomationWebhook` ve `DatabaseExecutor` dış bağlantılar açıyor. | `IDbHostAccessPolicy` ve `SsrfGuard` tüm modüllerde zorunlu tutulur; yönlendirme (`AllowAutoRedirect = false`) kapalıdır. | ✅ Tamamlandı |
| **SEC-07** | **Desk Handoff Token Isolation** | **MEDIUM** | Desk devir jetonu monolitik `AuthDbContext` üzerinden yönetiliyordu. | `DeskApplicationService` (`DeskDbContext`) içerisine taşındı; tek kullanımlık atomik `ExecuteUpdateAsync` yarış koruması ve SHA-256 `TokenHash` doğrulaması izole edildi. | ✅ Tamamlandı |
| **SEC-08** | **Transactional Migration Recovery** | **CRITICAL** | Çoklu tabloya bölünme (3NF) sırasında yarım kalan veri taşıma işlemi veri kaybına yol açabilir. | `ThreeNfAndSchemaNormalizationSql.BuildBackfillAndVerify3NfSql()` içinde tüm 3NF veri taşımaları `DO $$ ... RAISE EXCEPTION` satır sayısı doğrulamasıyla korunur; en küçük uyuşmazlıkta işlem (transaction) otomatik geri alınır. | ✅ Tamamlandı |
