// Üç Namines AI modeli (new-phase/36 §3). Sekiz sağlayıcı modeli yerine üç
// kendi adımız duruyor.
//
// Bunun sebebi kozmetik değil: yapılandırmadaki bir Groq modeli bir gün
// kaldırıldı ve o modeli seçmiş kullanıcıların şema üretimi tamamen durdu.
// Ad bizim olunca üstteki değişiklik sunucuda tek satırda kapanıyor.
//
// Sayılar eski AIMode değerleri: sunucu bunları üç modele indirgiyor
// (Low→flash, Medium→standard, Ultra→pro). Kayıtlı eski tercihler
// atılmıyor, karşılığına çevriliyor.
export const policyOptions = [
  { value: 1, label: 'NAI v1 Flash · fast, light' },
  { value: 2, label: 'NAI v1 · balanced' },
  { value: 4, label: 'NAI v1 Pro · deepest reasoning' }
];

export const seedDomainOptions = [
  { value: 'general', label: 'General Purpose Domain' },
  { value: 'e-commerce', label: 'E-Commerce & Retail' },
  { value: 'crm', label: 'CRM & Sales Pipelines' },
  { value: 'healthcare', label: 'Healthcare & Clinical ERP' },
  { value: 'fintech', label: 'Fintech & Ledger Auditing' }
];

export const docLevelOptions = [
  { value: 'standard', label: 'Standard Level Summary' },
  { value: 'deep', label: 'Deep Architectural Specifications' },
  { value: 'exec', label: 'Executive High-Level Abstract' }
];

export const scaffoldOptions = [
  { value: '.net8', label: '.NET 8.0 Entity Framework Core' },
  { value: '.net9', label: '.NET 9.0 Entity Framework Core' },
  { value: 'dapper', label: 'Dapper Lightweight ORM Models' },
  { value: 'ado', label: 'Raw ADO.NET SQL Command Builder' }
];

export const dbaSeverityOptions = [
  { value: 'warning', label: 'Warnings and Critical Issues' },
  { value: 'critical', label: 'Critical Vulnerability Blockers Only' },
  { value: 'all', label: 'Verbose Analysis (Include Warnings & Info)' }
];

export const tempOptions = [
  { value: '0.0', label: '0.0 (Strictly Deterministic)' },
  { value: '0.2', label: '0.2 (Balanced & Optimal - Recommended)' },
  { value: '0.7', label: '0.7 (Creative Architecture Ideas)' }
];

export const promptStyleOptions = [
  { value: 'clean', label: 'Clean Code (Decoupled, Modular)' },
  { value: 'minimalist', label: 'Minimalist (Compact, No Comments)' },
  { value: 'documented', label: 'Richly Documented (Verbose Comments)' }
];

export const namingOptions = [
  { value: 'snake_case', label: 'snake_case (PostgreSQL / MySQL)' },
  { value: 'PascalCase', label: 'PascalCase (SQL Server / EF Core)' },
  { value: 'camelCase', label: 'camelCase (MongoDB / Web APIs)' }
];

// RESTRICT varsayılan, CASCADE değil: bu kod tabanının kuralı, varsayılanın
// asla veri kaybına doğru düşmemesi (bkz. ReferentialActionSql).
export const fkActionOptions = [
  { value: 'restrict', label: 'ON DELETE RESTRICT (Default, safest)' },
  { value: 'set_null', label: 'ON DELETE SET NULL' },
  { value: 'cascade', label: 'ON DELETE CASCADE (deletes children)' }
];

export const maxTokensOptions = [
  { value: '2048', label: '2048 Tokens (Short, Quick Compilation)' },
  { value: '4096', label: '4096 Tokens (Standard)' },
  { value: '8192', label: '8192 Tokens (Rich Schema)' },
  { value: '16000', label: '16000 Tokens (Comprehensive — 20-30 Tables)' },
  { value: '32000', label: '32000 Tokens (Maximum — Team/Enterprise ceiling, Highly Recommended)' },
];

export const autoIndexOptions = [
  { value: 'true', label: 'Enable Auto-Indexing Suggestions' },
  { value: 'false', label: 'Disable (Explicit Indexing Only)' }
];

export const sqlPrettyOptions = [
  { value: 'true', label: 'Enable SQL Pretty Formatting & Indentation' },
  { value: 'false', label: 'Disable Formatting (Raw Fast Stream Output)' }
];

export const policyFields = [
  { key: 'smartSeed', label: 'Smart Seeding', desc: 'Mock database records generator (requires AI for domain-aware data).' },
  { key: 'documentation', label: 'Documentation & Reports', desc: 'PDF data dictionary and README files.' },
  { key: 'scaffolding', label: 'Backend Scaffolding Architecture', desc: 'C# DbContext and Model classes.' },
  { key: 'schemaRevision', label: 'Schema Revision', desc: 'AI-powered regional and global schema revision prompts.' },
  { key: 'dbaAnalysis', label: 'DBA Linter & Diagnostics', desc: 'Performance and security analyzer (requires AI — local engine not supported).' },
  { key: 'migration', label: 'Schema Migration', desc: 'C# EF Core migration code generator and parser.' },
  { key: 'voice', label: 'Voice Input & Transcription', desc: 'Whisper-powered speech-to-text transcription for prompts.' }
] as const;
