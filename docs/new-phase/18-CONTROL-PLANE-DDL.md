# 18 — Control Plane Veritabanı Şeması (Tam DDL)

PostgreSQL 17. Faz 1'deki SQLite `AuthDbContext`'in yerini alır.

> Kendi ürünümüzü kendimizde kullanıyoruz: bu şema `control-plane.nsl` olarak Namines'te tasarlanır ve buradan üretilir (dogfooding).

```sql
-- ═══════════════════════════════════════════════════════════════
-- EXTENSIONS
-- ═══════════════════════════════════════════════════════════════
CREATE EXTENSION IF NOT EXISTS pgcrypto;
CREATE EXTENSION IF NOT EXISTS pg_trgm;
CREATE EXTENSION IF NOT EXISTS btree_gin;

-- ═══════════════════════════════════════════════════════════════
-- ENUMS
-- ═══════════════════════════════════════════════════════════════
CREATE TYPE org_role         AS ENUM ('owner','admin','editor','viewer','billing');
CREATE TYPE plan_tier        AS ENUM ('free','pro','team','enterprise');
CREATE TYPE sub_status       AS ENUM ('active','trialing','past_due','canceled','incomplete');
CREATE TYPE db_mode          AS ENUM ('ephemeral','managed','branch','byodb');
CREATE TYPE db_engine        AS ENUM ('postgres','mssql','mysql','mariadb','sqlite','oracle');
CREATE TYPE db_status        AS ENUM ('provisioning','ready','migrating','error','suspended','deleting');
CREATE TYPE env_kind         AS ENUM ('development','preview','staging','production');
CREATE TYPE mig_status       AS ENUM ('planned','approved','running','applied','failed','rolled_back');
CREATE TYPE risk_level       AS ENUM ('safe','risky','destructive','breaking');
CREATE TYPE job_status       AS ENUM ('queued','running','succeeded','failed','canceled');
CREATE TYPE ai_provider      AS ENUM ('anthropic','groq','gemini','openai','ollama');

-- ═══════════════════════════════════════════════════════════════
-- IDENTITY
-- ═══════════════════════════════════════════════════════════════
CREATE TABLE users (
    id                  char(26)      PRIMARY KEY,              -- usr_ ULID
    email               citext        NOT NULL,
    email_verified_at   timestamptz,
    password_hash       text,                                    -- Argon2id; OAuth-only ise NULL
    display_name        varchar(120),
    avatar_url          text,
    locale              varchar(10)   NOT NULL DEFAULT 'en',
    timezone            varchar(64)   NOT NULL DEFAULT 'UTC',
    totp_secret         text,                                    -- şifreli
    totp_enabled        boolean       NOT NULL DEFAULT false,
    last_login_at       timestamptz,
    failed_login_count  smallint      NOT NULL DEFAULT 0,
    locked_until        timestamptz,
    created_at          timestamptz   NOT NULL DEFAULT now(),
    updated_at          timestamptz   NOT NULL DEFAULT now(),
    deleted_at          timestamptz,
    CONSTRAINT ck_users_email_len CHECK (length(email) BETWEEN 3 AND 320)
);
CREATE UNIQUE INDEX ux_users_email        ON users (email) WHERE deleted_at IS NULL;
CREATE INDEX        ix_users_created      ON users (created_at DESC);

CREATE TABLE user_identities (                                   -- OAuth bağlantıları
    id            char(26)     PRIMARY KEY,
    user_id       char(26)     NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    provider      varchar(40)  NOT NULL,                         -- github | google
    provider_uid  varchar(200) NOT NULL,
    email         citext,
    raw_profile   jsonb        NOT NULL DEFAULT '{}',
    created_at    timestamptz  NOT NULL DEFAULT now()
);
CREATE UNIQUE INDEX ux_user_identities ON user_identities (provider, provider_uid);
CREATE INDEX ix_user_identities_user   ON user_identities (user_id);

CREATE TABLE refresh_tokens (
    id           char(26)    PRIMARY KEY,
    user_id      char(26)    NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    token_hash   char(64)    NOT NULL,
    family_id    char(26)    NOT NULL,                           -- rotasyon ailesi
    user_agent   text,
    ip           inet,
    expires_at   timestamptz NOT NULL,
    revoked_at   timestamptz,
    created_at   timestamptz NOT NULL DEFAULT now()
);
CREATE UNIQUE INDEX ux_refresh_tokens_hash ON refresh_tokens (token_hash);
CREATE INDEX ix_refresh_tokens_user        ON refresh_tokens (user_id) WHERE revoked_at IS NULL;

-- ═══════════════════════════════════════════════════════════════
-- ORGANIZATIONS
-- ═══════════════════════════════════════════════════════════════
CREATE TABLE organizations (
    id                    char(26)     PRIMARY KEY,              -- org_
    slug                  varchar(60)  NOT NULL,
    name                  varchar(160) NOT NULL,
    avatar_url            text,
    plan                  plan_tier    NOT NULL DEFAULT 'free',
    stripe_customer_id    varchar(80),
    stripe_subscription_id varchar(80),
    subscription_status   sub_status,
    trial_ends_at         timestamptz,
    current_period_end    timestamptz,
    seats                 smallint     NOT NULL DEFAULT 1,
    created_by            char(26)     NOT NULL REFERENCES users(id) ON DELETE RESTRICT,
    created_at            timestamptz  NOT NULL DEFAULT now(),
    updated_at            timestamptz  NOT NULL DEFAULT now(),
    deleted_at            timestamptz,
    CONSTRAINT ck_orgs_slug CHECK (slug ~ '^[a-z0-9][a-z0-9-]{1,58}[a-z0-9]$')
);
CREATE UNIQUE INDEX ux_orgs_slug     ON organizations (slug) WHERE deleted_at IS NULL;
CREATE INDEX ix_orgs_stripe_customer ON organizations (stripe_customer_id);  -- ★ Faz 1'de vardı

CREATE TABLE org_members (
    org_id     char(26)    NOT NULL REFERENCES organizations(id) ON DELETE CASCADE,
    user_id    char(26)    NOT NULL REFERENCES users(id)         ON DELETE CASCADE,
    role       org_role    NOT NULL DEFAULT 'editor',
    joined_at  timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (org_id, user_id)
);
CREATE INDEX ix_org_members_user ON org_members (user_id);

CREATE TABLE org_invites (
    id          char(26)     PRIMARY KEY,
    org_id      char(26)     NOT NULL REFERENCES organizations(id) ON DELETE CASCADE,
    email       citext       NOT NULL,
    role        org_role     NOT NULL DEFAULT 'editor',
    token_hash  char(64)     NOT NULL,
    invited_by  char(26)     NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    expires_at  timestamptz  NOT NULL,
    accepted_at timestamptz,
    created_at  timestamptz  NOT NULL DEFAULT now()
);
CREATE UNIQUE INDEX ux_org_invites_token ON org_invites (token_hash);
CREATE UNIQUE INDEX ux_org_invites_open  ON org_invites (org_id, email) WHERE accepted_at IS NULL;

-- ═══════════════════════════════════════════════════════════════
-- PROJECTS & SCHEMA
-- ═══════════════════════════════════════════════════════════════
CREATE TABLE projects (
    id             char(26)     PRIMARY KEY,                     -- prj_
    org_id         char(26)     NOT NULL REFERENCES organizations(id) ON DELETE CASCADE,
    slug           varchar(60)  NOT NULL,
    name           varchar(160) NOT NULL,
    description    text,
    icon           varchar(40),
    default_engine db_engine    NOT NULL DEFAULT 'postgres',
    visibility     varchar(20)  NOT NULL DEFAULT 'private',      -- private | unlisted | public
    archived_at    timestamptz,
    created_by     char(26)     NOT NULL REFERENCES users(id) ON DELETE RESTRICT,
    created_at     timestamptz  NOT NULL DEFAULT now(),
    updated_at     timestamptz  NOT NULL DEFAULT now(),
    deleted_at     timestamptz
);
CREATE UNIQUE INDEX ux_projects_org_slug ON projects (org_id, slug) WHERE deleted_at IS NULL;
CREATE INDEX ix_projects_org             ON projects (org_id) WHERE deleted_at IS NULL;
CREATE INDEX ix_projects_public          ON projects (visibility) WHERE visibility = 'public';

CREATE TABLE branches (
    id           char(26)     PRIMARY KEY,                       -- brn_
    project_id   char(26)     NOT NULL REFERENCES projects(id) ON DELETE CASCADE,
    name         varchar(120) NOT NULL,
    parent_id    char(26)     REFERENCES branches(id) ON DELETE SET NULL,
    forked_from_version integer,
    is_default   boolean      NOT NULL DEFAULT false,
    created_by   char(26)     NOT NULL REFERENCES users(id) ON DELETE RESTRICT,
    created_at   timestamptz  NOT NULL DEFAULT now(),
    closed_at    timestamptz
);
CREATE UNIQUE INDEX ux_branches_project_name ON branches (project_id, name);
CREATE UNIQUE INDEX ux_branches_default      ON branches (project_id) WHERE is_default;

CREATE TABLE schema_versions (
    id           char(26)    PRIMARY KEY,                        -- ver_
    project_id   char(26)    NOT NULL REFERENCES projects(id) ON DELETE CASCADE,
    branch_id    char(26)    NOT NULL REFERENCES branches(id) ON DELETE CASCADE,
    version      integer     NOT NULL,
    checksum     char(64)    NOT NULL,
    nsl_ref      text        NOT NULL,                           -- S3 anahtarı
    nsl_inline   jsonb,                                          -- küçük şemalar için
    message      varchar(500),
    table_count  smallint    NOT NULL DEFAULT 0,
    author_id    char(26)    REFERENCES users(id) ON DELETE SET NULL,
    created_at   timestamptz NOT NULL DEFAULT now()
);
CREATE UNIQUE INDEX ux_schema_versions ON schema_versions (branch_id, version);
CREATE INDEX ix_schema_versions_proj   ON schema_versions (project_id, created_at DESC);

CREATE TABLE crdt_documents (                                    -- Yjs kalıcılığı
    branch_id   char(26)    PRIMARY KEY REFERENCES branches(id) ON DELETE CASCADE,
    state       bytea       NOT NULL,
    op_count    integer     NOT NULL DEFAULT 0,
    updated_at  timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE comments (
    id           char(26)    PRIMARY KEY,
    project_id   char(26)    NOT NULL REFERENCES projects(id) ON DELETE CASCADE,
    anchor_type  varchar(20) NOT NULL,                           -- table | column | relation | canvas
    anchor_uuid  uuid,
    body         text        NOT NULL,
    author_id    char(26)    NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    parent_id    char(26)    REFERENCES comments(id) ON DELETE CASCADE,
    resolved_at  timestamptz,
    created_at   timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX ix_comments_project ON comments (project_id, created_at DESC);
CREATE INDEX ix_comments_anchor  ON comments (anchor_uuid) WHERE resolved_at IS NULL;

-- ═══════════════════════════════════════════════════════════════
-- ENVIRONMENTS & DATABASES
-- ═══════════════════════════════════════════════════════════════
CREATE TABLE environments (
    id          char(26)     PRIMARY KEY,
    project_id  char(26)     NOT NULL REFERENCES projects(id) ON DELETE CASCADE,
    kind        env_kind     NOT NULL,
    name        varchar(60)  NOT NULL,
    branch_id   char(26)     REFERENCES branches(id) ON DELETE SET NULL,
    created_at  timestamptz  NOT NULL DEFAULT now()
);
CREATE UNIQUE INDEX ux_environments ON environments (project_id, name);

CREATE TABLE databases (
    id                char(26)     PRIMARY KEY,                  -- db_
    project_id        char(26)     NOT NULL REFERENCES projects(id) ON DELETE CASCADE,
    environment_id    char(26)     REFERENCES environments(id) ON DELETE SET NULL,
    mode              db_mode      NOT NULL,
    engine            db_engine    NOT NULL,
    engine_version    varchar(20),
    provider          varchar(40)  NOT NULL,                     -- neon | planetscale | azure | k8s | external
    provider_ref      varchar(200),                              -- sağlayıcıdaki kimlik
    region            varchar(40),
    size              varchar(20),
    status            db_status    NOT NULL DEFAULT 'provisioning',
    secret_ref        varchar(200) NOT NULL,                     -- Vault yolu — düz metin YOK
    parent_db_id      char(26)     REFERENCES databases(id) ON DELETE SET NULL,  -- branch DB
    applied_version   integer,
    storage_bytes     bigint       NOT NULL DEFAULT 0,
    expires_at        timestamptz,                               -- ephemeral TTL
    last_error        text,
    created_at        timestamptz  NOT NULL DEFAULT now(),
    updated_at        timestamptz  NOT NULL DEFAULT now(),
    deleted_at        timestamptz
);
CREATE INDEX ix_databases_project ON databases (project_id) WHERE deleted_at IS NULL;
CREATE INDEX ix_databases_expiry  ON databases (expires_at) WHERE expires_at IS NOT NULL AND deleted_at IS NULL;
CREATE INDEX ix_databases_status  ON databases (status) WHERE status IN ('provisioning','migrating','error');

-- ═══════════════════════════════════════════════════════════════
-- MIGRATIONS
-- ═══════════════════════════════════════════════════════════════
CREATE TABLE migrations (
    id                char(26)     PRIMARY KEY,
    project_id        char(26)     NOT NULL REFERENCES projects(id) ON DELETE CASCADE,
    database_id       char(26)     REFERENCES databases(id) ON DELETE SET NULL,
    from_version      integer,
    to_version        integer      NOT NULL,
    status            mig_status   NOT NULL DEFAULT 'planned',
    max_risk          risk_level   NOT NULL DEFAULT 'safe',
    operations        jsonb        NOT NULL,
    up_sql            text,
    down_sql          text,
    estimated_ms      integer,
    actual_ms         integer,
    approved_by       char(26)[]   NOT NULL DEFAULT '{}',
    applied_by        char(26)     REFERENCES users(id) ON DELETE SET NULL,
    applied_at        timestamptz,
    error             text,
    backup_ref        text,
    created_at        timestamptz  NOT NULL DEFAULT now()
);
CREATE INDEX ix_migrations_project ON migrations (project_id, created_at DESC);
CREATE INDEX ix_migrations_pending ON migrations (status) WHERE status IN ('planned','approved','running');

-- ═══════════════════════════════════════════════════════════════
-- API KEYS & CONSOLE
-- ═══════════════════════════════════════════════════════════════
CREATE TABLE api_keys (
    id             char(26)     PRIMARY KEY,                     -- key_
    project_id     char(26)     NOT NULL REFERENCES projects(id) ON DELETE CASCADE,
    name           varchar(120) NOT NULL,
    prefix         varchar(24)  NOT NULL,
    key_hash       text         NOT NULL,                        -- Argon2id
    scopes         text[]       NOT NULL DEFAULT '{}',
    role_name      varchar(60),
    environment    env_kind     NOT NULL DEFAULT 'production',
    allowed_ips    inet[]       NOT NULL DEFAULT '{}',
    allowed_origins text[]      NOT NULL DEFAULT '{}',
    rate_limit_rpm integer      NOT NULL DEFAULT 600,
    last_used_at   timestamptz,
    expires_at     timestamptz,
    revoked_at     timestamptz,
    created_by     char(26)     NOT NULL REFERENCES users(id) ON DELETE RESTRICT,
    created_at     timestamptz  NOT NULL DEFAULT now()
);
CREATE UNIQUE INDEX ux_api_keys_prefix ON api_keys (prefix);
CREATE INDEX ix_api_keys_project       ON api_keys (project_id) WHERE revoked_at IS NULL;

CREATE TABLE console_configs (
    project_id  char(26)    PRIMARY KEY REFERENCES projects(id) ON DELETE CASCADE,
    config      jsonb       NOT NULL DEFAULT '{}',               -- overlay: görünüm, tema, widget
    roles       jsonb       NOT NULL DEFAULT '[]',
    custom_domain varchar(200),
    updated_at  timestamptz NOT NULL DEFAULT now()
);
CREATE UNIQUE INDEX ux_console_domain ON console_configs (custom_domain) WHERE custom_domain IS NOT NULL;

CREATE TABLE console_users (                                     -- müşterinin panel kullanıcıları
    id           char(26)     PRIMARY KEY,
    project_id   char(26)     NOT NULL REFERENCES projects(id) ON DELETE CASCADE,
    email        citext       NOT NULL,
    role_name    varchar(60)  NOT NULL,
    password_hash text,
    invited_at   timestamptz  NOT NULL DEFAULT now(),
    accepted_at  timestamptz,
    disabled_at  timestamptz
);
CREATE UNIQUE INDEX ux_console_users ON console_users (project_id, email);

-- ═══════════════════════════════════════════════════════════════
-- SHARING
-- ═══════════════════════════════════════════════════════════════
CREATE TABLE share_links (                                       -- ★ Faz 1 ShareToken
    id          char(26)     PRIMARY KEY,
    project_id  char(26)     NOT NULL REFERENCES projects(id) ON DELETE CASCADE,
    branch_id   char(26)     REFERENCES branches(id) ON DELETE CASCADE,
    token_hash  char(64)     NOT NULL,
    permission  varchar(20)  NOT NULL DEFAULT 'view',            -- view | comment | edit
    password_hash text,
    expires_at  timestamptz,
    view_count  integer      NOT NULL DEFAULT 0,
    created_by  char(26)     NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    revoked_at  timestamptz,
    created_at  timestamptz  NOT NULL DEFAULT now()
);
CREATE UNIQUE INDEX ux_share_links_token ON share_links (token_hash);
CREATE INDEX ix_share_links_project      ON share_links (project_id) WHERE revoked_at IS NULL;

-- ═══════════════════════════════════════════════════════════════
-- AI: POLICY, QUOTA, CACHE  (★ Faz 1'den taşındı ve yeniden modellendi)
-- ═══════════════════════════════════════════════════════════════
CREATE TABLE ai_policies (                                       -- ★ UserAIPolicy
    org_id            char(26)     PRIMARY KEY REFERENCES organizations(id) ON DELETE CASCADE,
    preferred_provider ai_provider NOT NULL DEFAULT 'anthropic',
    privacy_mode      varchar(20)  NOT NULL DEFAULT 'standard',  -- standard|strict|byok|local
    auto_index        boolean      NOT NULL DEFAULT true,
    naming_convention varchar(30)  NOT NULL DEFAULT 'snake_case',
    settings          jsonb        NOT NULL DEFAULT '{}',
    updated_at        timestamptz  NOT NULL DEFAULT now()
);

CREATE TABLE ai_credentials (                                    -- ★ BYOK
    id           char(26)     PRIMARY KEY,
    org_id       char(26)     NOT NULL REFERENCES organizations(id) ON DELETE CASCADE,
    provider     ai_provider  NOT NULL,
    secret_ref   varchar(200) NOT NULL,                          -- Vault yolu
    label        varchar(120),
    created_at   timestamptz  NOT NULL DEFAULT now()
);
CREATE UNIQUE INDEX ux_ai_credentials ON ai_credentials (org_id, provider);

CREATE TABLE ai_usage (                                          -- ★ UserAIQuota + GlobalAiUsage
    id            bigserial    PRIMARY KEY,
    org_id        char(26)     NOT NULL REFERENCES organizations(id) ON DELETE CASCADE,
    user_id       char(26)     REFERENCES users(id) ON DELETE SET NULL,
    project_id    char(26)     REFERENCES projects(id) ON DELETE SET NULL,
    day           date         NOT NULL,
    agent         varchar(60)  NOT NULL,
    provider      ai_provider  NOT NULL,
    model         varchar(80)  NOT NULL,
    input_tokens  integer      NOT NULL DEFAULT 0,
    output_tokens integer      NOT NULL DEFAULT 0,
    cached_tokens integer      NOT NULL DEFAULT 0,
    calls         integer      NOT NULL DEFAULT 1,
    cost_usd      numeric(10,6) NOT NULL DEFAULT 0,
    latency_ms    integer,
    cache_hit     boolean      NOT NULL DEFAULT false,
    created_at    timestamptz  NOT NULL DEFAULT now()
);
CREATE INDEX ix_ai_usage_org_day ON ai_usage (org_id, day DESC);
CREATE INDEX ix_ai_usage_project ON ai_usage (project_id, created_at DESC);

CREATE TABLE ai_prompt_versions (
    id          char(26)     PRIMARY KEY,
    agent       varchar(60)  NOT NULL,
    version     varchar(20)  NOT NULL,
    content_ref text         NOT NULL,
    model       varchar(80)  NOT NULL,
    params      jsonb        NOT NULL DEFAULT '{}',
    eval_score  numeric(4,3),
    active      boolean      NOT NULL DEFAULT false,
    traffic_pct smallint     NOT NULL DEFAULT 0,
    created_at  timestamptz  NOT NULL DEFAULT now()
);
CREATE UNIQUE INDEX ux_ai_prompt_versions ON ai_prompt_versions (agent, version);

-- ═══════════════════════════════════════════════════════════════
-- JOBS, INTEGRATIONS, AUDIT, FEEDBACK
-- ═══════════════════════════════════════════════════════════════
CREATE TABLE jobs (
    id            char(26)     PRIMARY KEY,                      -- job_
    org_id        char(26)     NOT NULL REFERENCES organizations(id) ON DELETE CASCADE,
    project_id    char(26)     REFERENCES projects(id) ON DELETE CASCADE,
    kind          varchar(60)  NOT NULL,
    status        job_status   NOT NULL DEFAULT 'queued',
    payload       jsonb        NOT NULL DEFAULT '{}',
    result        jsonb,
    progress      smallint     NOT NULL DEFAULT 0,
    attempts      smallint     NOT NULL DEFAULT 0,
    error         text,
    started_at    timestamptz,
    finished_at   timestamptz,
    created_at    timestamptz  NOT NULL DEFAULT now()
);
CREATE INDEX ix_jobs_status  ON jobs (status, created_at) WHERE status IN ('queued','running');
CREATE INDEX ix_jobs_project ON jobs (project_id, created_at DESC);

CREATE TABLE integrations (
    id           char(26)     PRIMARY KEY,
    project_id   char(26)     NOT NULL REFERENCES projects(id) ON DELETE CASCADE,
    kind         varchar(40)  NOT NULL,                          -- github | slack | webhook
    config       jsonb        NOT NULL DEFAULT '{}',
    secret_ref   varchar(200),
    enabled      boolean      NOT NULL DEFAULT true,
    created_at   timestamptz  NOT NULL DEFAULT now()
);
CREATE UNIQUE INDEX ux_integrations ON integrations (project_id, kind);

CREATE TABLE audit_log (
    id          bigserial    PRIMARY KEY,
    org_id      char(26)     NOT NULL,
    actor_id    char(26),
    actor_type  varchar(20)  NOT NULL,                           -- user | api_key | system
    action      varchar(80)  NOT NULL,
    target_type varchar(40),
    target_id   varchar(60),
    metadata    jsonb        NOT NULL DEFAULT '{}',
    ip          inet,
    user_agent  text,
    request_id  varchar(40),
    at          timestamptz  NOT NULL DEFAULT now()
);
CREATE INDEX ix_audit_org  ON audit_log (org_id, at DESC);
CREATE INDEX ix_audit_actor ON audit_log (actor_id, at DESC);
-- append-only: UPDATE/DELETE yetkisi uygulama rolüne verilmez

CREATE TABLE feedback (                                          -- ★ Faz 1 Feedback
    id         char(26)     PRIMARY KEY,
    user_id    char(26)     REFERENCES users(id) ON DELETE SET NULL,
    org_id     char(26)     REFERENCES organizations(id) ON DELETE SET NULL,
    kind       varchar(20)  NOT NULL,                            -- bug | idea | other
    body       text         NOT NULL,
    page       varchar(200),
    metadata   jsonb        NOT NULL DEFAULT '{}',
    status     varchar(20)  NOT NULL DEFAULT 'new',
    created_at timestamptz  NOT NULL DEFAULT now()
);
CREATE INDEX ix_feedback_created ON feedback (created_at DESC);

CREATE TABLE blueprints (                                        -- ★ şablon galerisi → Hub
    id          char(26)     PRIMARY KEY,
    slug        varchar(80)  NOT NULL,
    name        varchar(160) NOT NULL,
    description text,
    category    varchar(40),
    tags        text[]       NOT NULL DEFAULT '{}',
    nsl_ref     text         NOT NULL,
    preview_url text,
    author_id   char(26)     REFERENCES users(id) ON DELETE SET NULL,
    official    boolean      NOT NULL DEFAULT false,
    install_count integer    NOT NULL DEFAULT 0,
    published_at timestamptz,
    created_at  timestamptz  NOT NULL DEFAULT now()
);
CREATE UNIQUE INDEX ux_blueprints_slug ON blueprints (slug);
CREATE INDEX ix_blueprints_search ON blueprints USING gin (to_tsvector('english', name || ' ' || coalesce(description,'')));

-- ═══════════════════════════════════════════════════════════════
-- ROW LEVEL SECURITY (control plane'in kendi izolasyonu)
-- ═══════════════════════════════════════════════════════════════
ALTER TABLE projects        ENABLE ROW LEVEL SECURITY;
ALTER TABLE databases       ENABLE ROW LEVEL SECURITY;
ALTER TABLE api_keys        ENABLE ROW LEVEL SECURITY;
ALTER TABLE schema_versions ENABLE ROW LEVEL SECURITY;

CREATE POLICY p_projects_org ON projects
  USING (org_id = ANY (string_to_array(current_setting('app.org_ids', true), ',')));
-- ... diğer tablolar için benzer politikalar

-- ═══════════════════════════════════════════════════════════════
-- TRIGGERS
-- ═══════════════════════════════════════════════════════════════
CREATE OR REPLACE FUNCTION touch_updated_at() RETURNS trigger AS $$
BEGIN NEW.updated_at = now(); RETURN NEW; END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER tg_users_touch    BEFORE UPDATE ON users         FOR EACH ROW EXECUTE FUNCTION touch_updated_at();
CREATE TRIGGER tg_orgs_touch     BEFORE UPDATE ON organizations FOR EACH ROW EXECUTE FUNCTION touch_updated_at();
CREATE TRIGGER tg_projects_touch BEFORE UPDATE ON projects      FOR EACH ROW EXECUTE FUNCTION touch_updated_at();
CREATE TRIGGER tg_databases_touch BEFORE UPDATE ON databases    FOR EACH ROW EXECUTE FUNCTION touch_updated_at();
```

---

## ClickHouse — kullanım/analitik olayları

```sql
CREATE TABLE usage_events (
    ts            DateTime64(3),
    org_id        LowCardinality(String),
    project_id    LowCardinality(String),
    user_id       String,
    event         LowCardinality(String),     -- api.request | ai.call | db.query | console.view
    resource      String,
    duration_ms   UInt32,
    bytes         UInt64,
    status        UInt16,
    cost_usd      Decimal(12,6),
    props         Map(String, String)
) ENGINE = MergeTree
PARTITION BY toYYYYMM(ts)
ORDER BY (org_id, project_id, event, ts)
TTL ts + INTERVAL 13 MONTH;

CREATE MATERIALIZED VIEW usage_daily_mv
ENGINE = SummingMergeTree ORDER BY (org_id, project_id, event, day) AS
SELECT toDate(ts) AS day, org_id, project_id, event,
       count() AS calls, sum(duration_ms) AS total_ms, sum(cost_usd) AS cost
FROM usage_events GROUP BY day, org_id, project_id, event;
```

---

## Faz 1 tablo eşlemesi

| Faz 1 (SQLite) | Faz 2 (PostgreSQL) |
|---|---|
| `AspNetUsers` (ApplicationUser) | `users` + `user_identities` |
| `CloudProjects` | `projects` + `schema_versions` |
| `UserAIPolicy` | `ai_policies` |
| `UserAIQuota` | `ai_usage` |
| `GlobalAiUsage` | `ai_usage` (agregasyon MV ile) |
| `Feedback` | `feedback` |
| `ShareToken` | `share_links` |
| Stripe alanları (ApplicationUser'da) | `organizations` (org seviyesine taşındı) |
| — | Diğer 20 tablo yeni |
