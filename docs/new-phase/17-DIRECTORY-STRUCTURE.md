# 17 — Hedef Monorepo Yapısı

```
namines/
├── README.md
├── README.tr.md                          ★ korunur
├── LICENSE                               ★ (MIT → değerlendirilmeli, bkz. 22)
├── CONTRIBUTING.md
├── SECURITY.md
├── CHANGELOG.md
├── .env.example                          ★
├── .gitignore                            ★
├── .gitattributes
├── .editorconfig
├── Makefile
├── Taskfile.yml
├── docker-compose.yml                    ★ (docker.sock kaldırıldı)
├── docker-compose.observability.yml
├── turbo.json
├── pnpm-workspace.yaml
├── package.json
├── Namines.sln
├── Directory.Build.props                 # ortak .NET ayarları
├── Directory.Packages.props              # merkezî paket sürüm yönetimi
├── global.json                           # .NET SDK pinleme
├── nuget.config
│
├── .github/
│   ├── workflows/
│   │   ├── ci.yml
│   │   ├── eval.yml
│   │   ├── preview.yml
│   │   ├── release.yml
│   │   ├── nightly.yml
│   │   ├── codeql.yml
│   │   └── namines-schema-review.yml     ★ (namines-diff.mjs'in yerine)
│   ├── ISSUE_TEMPLATE/
│   ├── PULL_REQUEST_TEMPLATE.md
│   ├── dependabot.yml
│   └── CODEOWNERS
│
├── docs/
│   ├── screenshots/                      ★ korunur
│   ├── adr/                              # mimari karar kayıtları
│   │   ├── 0001-console-runtime-render.md
│   │   ├── 0002-nsl-as-core.md
│   │   └── ...
│   ├── runbooks/
│   │   ├── disaster-recovery.md
│   │   ├── incident-response.md
│   │   ├── secret-rotation.md
│   │   └── db-provisioning-failure.md
│   └── specs/
│       ├── nsl-1.0.md                    # NSL dil spesifikasyonu (public)
│       └── nsl-1.0.schema.json
│
├── new-phase/                            # ← bu planlama klasörü
│
├── src/                                  # .NET backend
│   ├── Namines.Contracts/
│   │   ├── Namines.Contracts.csproj
│   │   ├── Dtos/
│   │   ├── Events/                       # NATS olay şemaları
│   │   └── Errors/
│   │
│   ├── Namines.Nsl/                      ★★ YENİ ÇEKİRDEK
│   │   ├── Namines.Nsl.csproj
│   │   ├── Model/
│   │   │   ├── NslDocument.cs
│   │   │   ├── NslTable.cs
│   │   │   ├── NslColumn.cs
│   │   │   ├── NslIndex.cs               # ← Faz 1'de hiç yoktu
│   │   │   ├── NslForeignKey.cs          # ← onDelete/onUpdate ile
│   │   │   ├── NslUnique.cs
│   │   │   ├── NslCheck.cs
│   │   │   ├── NslEnum.cs
│   │   │   ├── NslView.cs
│   │   │   ├── NslRls.cs
│   │   │   ├── NslType.cs
│   │   │   └── NslUiHints.cs
│   │   ├── Parsing/
│   │   │   ├── NslLexer.cs
│   │   │   ├── NslParser.cs
│   │   │   └── NslWriter.cs
│   │   ├── Validation/
│   │   │   ├── NslValidator.cs
│   │   │   ├── Rules/                    # NSL001..NSL025
│   │   │   └── AutoFixer.cs
│   │   ├── Diffing/
│   │   │   ├── NslDiffer.cs
│   │   │   ├── MigrationPlanner.cs
│   │   │   ├── RiskClassifier.cs
│   │   │   └── FkCascadeAnalyzer.cs      # ← MSSQL cascade hatasını önler
│   │   ├── Merging/
│   │   │   └── NslMerger.cs              ★ (3-way merge mantığı taşındı)
│   │   └── Migration/
│   │       └── LegacyV1Migrator.cs       # Faz 1 şemalarını dönüştürür
│   │
│   ├── Namines.Compiler/
│   │   ├── Namines.Compiler.csproj
│   │   ├── ICompilerBackend.cs
│   │   ├── CompilerRegistry.cs
│   │   ├── TypeMaps/
│   │   │   ├── PostgresTypeMap.cs
│   │   │   ├── MssqlTypeMap.cs
│   │   │   ├── MySqlTypeMap.cs
│   │   │   ├── MariaDbTypeMap.cs
│   │   │   ├── SqliteTypeMap.cs
│   │   │   └── OracleTypeMap.cs
│   │   ├── Ddl/                          ★ (6 generator taşındı ve yeniden yazıldı)
│   │   │   ├── PostgresDdlBackend.cs
│   │   │   ├── MssqlDdlBackend.cs
│   │   │   ├── MySqlDdlBackend.cs
│   │   │   ├── MariaDbDdlBackend.cs
│   │   │   ├── SqliteDdlBackend.cs
│   │   │   ├── OracleDdlBackend.cs
│   │   │   └── DdlEmitter.cs             # topolojik sıralama, quoting
│   │   ├── Orm/
│   │   │   ├── EfCoreBackend.cs          ★
│   │   │   ├── EfCoreMigrationBackend.cs ★
│   │   │   ├── PrismaBackend.cs          ★
│   │   │   ├── DrizzleBackend.cs
│   │   │   ├── TypeOrmBackend.cs
│   │   │   ├── SqlAlchemyBackend.cs
│   │   │   └── DjangoBackend.cs
│   │   ├── Types/
│   │   │   ├── TypeScriptBackend.cs
│   │   │   ├── ZodBackend.cs
│   │   │   ├── CSharpBackend.cs
│   │   │   └── PydanticBackend.cs
│   │   ├── Contracts/
│   │   │   ├── OpenApiBackend.cs
│   │   │   ├── GraphQlSdlBackend.cs
│   │   │   └── JsonSchemaBackend.cs
│   │   ├── Docs/
│   │   │   ├── DataDictionaryPdfBackend.cs   ★
│   │   │   ├── ReadmeBackend.cs              ★
│   │   │   ├── MermaidBackend.cs             ★
│   │   │   ├── PlantUmlBackend.cs
│   │   │   ├── ExcelBackend.cs
│   │   │   └── DbmlBackend.cs                # ← GTM için kritik
│   │   ├── Apps/
│   │   │   ├── NextJsAppBackend.cs
│   │   │   ├── StreamlitAppBackend.cs        ★ (korundu!)
│   │   │   ├── BlazorAppBackend.cs
│   │   │   └── AspNetApiBackend.cs
│   │   ├── Importers/
│   │   │   ├── SqlDdlImporter.cs             ★
│   │   │   ├── DbmlImporter.cs
│   │   │   ├── PrismaImporter.cs
│   │   │   ├── DbContextImporter.cs          ★
│   │   │   └── AtlasHclImporter.cs
│   │   └── Templates/                        # Scriban şablonları
│   │       ├── nextjs/
│   │       ├── streamlit/                    ★
│   │       └── blazor/
│   │
│   ├── Namines.Core/
│   │   ├── Entities/                     # Organization, Project, Database, ApiKey...
│   │   ├── Interfaces/                   ★ (IAIService, IDdlGenerator... genişletildi)
│   │   ├── Services/
│   │   ├── Enums/                        ★
│   │   └── Errors/
│   │
│   ├── Namines.Ai/
│   │   ├── Agents/
│   │   │   ├── SchemaArchitectAgent.cs
│   │   │   ├── SchemaRefinerAgent.cs
│   │   │   ├── SchemaCriticAgent.cs      ★ (AIDbaService)
│   │   │   ├── DocWriterAgent.cs
│   │   │   ├── SeedPlannerAgent.cs       ★ (SmartSeedService)
│   │   │   ├── MigrationAnalystAgent.cs
│   │   │   ├── QueryWriterAgent.cs
│   │   │   ├── IndexAdvisorAgent.cs
│   │   │   ├── VisionParserAgent.cs      ★
│   │   │   └── OrchestratorAgent.cs
│   │   ├── Providers/
│   │   │   ├── AnthropicProvider.cs
│   │   │   ├── GroqProvider.cs           ★ (GroqAIService)
│   │   │   ├── GeminiProvider.cs         ★
│   │   │   ├── OllamaProvider.cs         ★ (OllamaAIService)
│   │   │   ├── OpenAiProvider.cs
│   │   │   └── AiProviderFactory.cs      ★ (AIFactory)
│   │   ├── Prompts/                      # dosya tabanlı, embedded resource
│   │   ├── Context/
│   │   │   └── SchemaContextBuilder.cs
│   │   ├── Caching/
│   │   │   └── SemanticCache.cs          ★ (SemanticCacheService)
│   │   ├── Structured/
│   │   │   └── JsonRepair.cs             ★ (JsonSanitizerPreprocessor)
│   │   └── Safety/
│   │       ├── PromptGuard.cs            ★
│   │       └── OutputValidator.cs
│   │
│   ├── Namines.DataPlane/
│   │   ├── Provisioning/
│   │   │   ├── IDatabaseProvider.cs
│   │   │   ├── NeonProvider.cs
│   │   │   ├── PlanetScaleProvider.cs
│   │   │   ├── AzureSqlProvider.cs
│   │   │   ├── EphemeralK8sProvider.cs   ★ (DockerService'in güvenli halefi)
│   │   │   └── SelfHostedPgProvider.cs
│   │   ├── Introspection/                ★ (DbIntrospectionService)
│   │   │   ├── PostgresIntrospector.cs
│   │   │   ├── MssqlIntrospector.cs
│   │   │   ├── MySqlIntrospector.cs
│   │   │   ├── SqliteIntrospector.cs
│   │   │   └── OracleIntrospector.cs
│   │   ├── Execution/
│   │   │   ├── MigrationExecutor.cs
│   │   │   ├── QueryExecutor.cs          ★ (DatabaseExecutorService)
│   │   │   └── AdvisoryLock.cs
│   │   ├── Seeding/
│   │   │   ├── DataFactory.cs            ★ (SmartSeedService)
│   │   │   ├── Generators/
│   │   │   └── Locales/                  # tr-TR dahil 40 yerel ayar
│   │   ├── Backup/
│   │   │   └── BackupService.cs          ★ (DockerBackupService)
│   │   └── Security/
│   │       └── SsrfGuard.cs              ★ (+ DNS rebinding koruması)
│   │
│   ├── Namines.Infrastructure/
│   │   ├── Data/
│   │   │   ├── ControlDbContext.cs       ★ (AuthDbContext)
│   │   │   ├── Configurations/
│   │   │   └── Migrations/               ★
│   │   ├── Cache/
│   │   ├── Messaging/                    # NATS
│   │   ├── Storage/                      # S3/R2
│   │   ├── Secrets/                      # Vault
│   │   ├── Billing/                      ★ (Stripe)
│   │   ├── Email/
│   │   ├── Analytics/                    # ClickHouse
│   │   └── Github/
│   │
│   ├── Namines.Api/
│   │   ├── Program.cs                    ★ (Database.Migrate kaldırıldı)
│   │   ├── Endpoints/                    # Minimal API grupları
│   │   │   ├── AuthEndpoints.cs          ★
│   │   │   ├── OrgEndpoints.cs
│   │   │   ├── ProjectEndpoints.cs
│   │   │   ├── SchemaEndpoints.cs        ★
│   │   │   ├── AiEndpoints.cs            ★
│   │   │   ├── CompileEndpoints.cs       ★
│   │   │   ├── DatabaseEndpoints.cs      ★
│   │   │   ├── MigrationEndpoints.cs     ★
│   │   │   ├── ConsoleEndpoints.cs
│   │   │   ├── GatewayConfigEndpoints.cs
│   │   │   ├── ShareEndpoints.cs         ★
│   │   │   ├── BillingEndpoints.cs       ★
│   │   │   ├── FeedbackEndpoints.cs      ★
│   │   │   └── InternalEndpoints.cs
│   │   ├── Middleware/                   ★
│   │   │   ├── ExceptionMiddleware.cs    ★
│   │   │   ├── TenantResolutionMiddleware.cs
│   │   │   ├── AiQuotaMiddleware.cs      ★
│   │   │   ├── ByokMiddleware.cs         ★
│   │   │   └── RequestIdMiddleware.cs
│   │   ├── Auth/
│   │   ├── HealthChecks/                 ★
│   │   └── Dockerfile
│   │
│   ├── Namines.Gateway/
│   │   ├── Program.cs
│   │   ├── Rest/
│   │   │   ├── RowEndpoints.cs
│   │   │   ├── QueryTranslator.cs        # ?filter= → SQL
│   │   │   └── ResponseWriter.cs
│   │   ├── GraphQl/
│   │   │   ├── DynamicSchemaBuilder.cs
│   │   │   └── DataLoaders/
│   │   ├── Security/
│   │   │   ├── ApiKeyAuthentication.cs
│   │   │   ├── RowLevelSecurity.cs
│   │   │   └── ColumnMasking.cs
│   │   ├── Metadata/
│   │   │   └── MetadataCache.cs
│   │   ├── Connections/
│   │   │   └── TenantConnectionFactory.cs
│   │   └── Dockerfile
│   │
│   ├── Namines.Realtime/
│   │   ├── Program.cs
│   │   ├── Hubs/CanvasHub.cs             ★ (auth + CRDT ile yeniden yazıldı)
│   │   ├── Presence/
│   │   ├── Persistence/
│   │   └── Dockerfile
│   │
│   ├── Namines.Worker/
│   │   ├── Program.cs
│   │   ├── Jobs/
│   │   │   ├── ProvisionDatabaseJob.cs
│   │   │   ├── ApplyMigrationJob.cs
│   │   │   ├── SeedDataJob.cs
│   │   │   ├── BackupJob.cs
│   │   │   ├── IntrospectJob.cs
│   │   │   ├── CompilePackageJob.cs
│   │   │   ├── AiAgentJob.cs
│   │   │   ├── SandboxSweeperJob.cs      ★ (DockerSweeperBackgroundService)
│   │   │   └── MeteringRollupJob.cs
│   │   └── Dockerfile
│   │
│   ├── Namines.Bot/
│   │   ├── Program.cs
│   │   ├── Handlers/
│   │   ├── Rendering/                    # PR yorum markdown'ı
│   │   └── Dockerfile
│   │
│   ├── Namines.Bridge/
│   │   ├── Program.cs
│   │   ├── Tunnel/
│   │   ├── Policy/
│   │   └── Namines.Bridge.csproj         # PublishAot=true
│   │
│   └── Namines.Cli/
│       ├── Program.cs
│       ├── Commands/
│       │   ├── ValidateCommand.cs
│       │   ├── DiffCommand.cs
│       │   ├── PlanCommand.cs
│       │   ├── ApplyCommand.cs
│       │   ├── PullCommand.cs
│       │   ├── CodegenCommand.cs
│       │   ├── DriftCommand.cs
│       │   └── InitCommand.cs
│       └── Namines.Cli.csproj            # PackAsTool=true
│
├── tests/
│   ├── Namines.Nsl.Tests/
│   ├── Namines.Compiler.Tests/
│   │   ├── Golden/                       # ★★ golden-file snapshot'ları
│   │   │   ├── postgres/
│   │   │   ├── mssql/
│   │   │   ├── mysql/
│   │   │   ├── mariadb/
│   │   │   ├── sqlite/
│   │   │   └── oracle/
│   │   └── Fixtures/                     # test şemaları (ecommerce, saas, crm...)
│   ├── Namines.Compiler.IntegrationTests/  # Testcontainers ile gerçek DB
│   ├── Namines.Api.Tests/
│   ├── Namines.Gateway.Tests/
│   ├── Namines.DataPlane.Tests/
│   ├── Namines.Ai.Tests/
│   ├── Namines.Security.Tests/           # kiracı izolasyon testleri
│   └── Namines.Load.Tests/               # NBomber
│
├── apps/                                 # frontend (pnpm workspace)
│   ├── web/                              ★ (Faz 1 frontend'i buraya taşınır)
│   │   ├── app/
│   │   │   ├── (marketing)/
│   │   │   ├── (studio)/p/[projectId]/
│   │   │   │   ├── page.tsx              ★ canvas
│   │   │   │   ├── design/               # NSL kod editörü (yeni)
│   │   │   │   ├── data/
│   │   │   │   ├── migrations/
│   │   │   │   ├── api/
│   │   │   │   ├── console/
│   │   │   │   └── settings/
│   │   │   ├── compile/                  ★
│   │   │   ├── share/[token]/            ★
│   │   │   └── s/[slug]/                 # public SEO sayfaları (yeni)
│   │   ├── components/
│   │   │   ├── canvas/                   ★ (TableNode, RelationEdge, panels...)
│   │   │   ├── compile/                  ★
│   │   │   ├── migration/                ★
│   │   │   ├── editor/                   # Monaco NSL (yeni)
│   │   │   ├── collab/                   ★ (MultiplayerCursors)
│   │   │   └── layout/                   ★
│   │   ├── hooks/                        ★
│   │   ├── stores/                       ★ (14 Zustand store'u korunur)
│   │   ├── lib/                          ★
│   │   └── e2e/
│   │
│   ├── console/                          # ★★ YENİ ANA ÜRÜN
│   │   ├── app/p/[slug]/
│   │   │   ├── page.tsx                  # dashboard
│   │   │   ├── [table]/page.tsx          # liste
│   │   │   ├── [table]/new/page.tsx
│   │   │   ├── [table]/[pk]/page.tsx
│   │   │   ├── _query/page.tsx
│   │   │   └── _settings/page.tsx
│   │   ├── components/
│   │   │   ├── renderer/                 # metadata → UI motoru
│   │   │   │   ├── TableRenderer.tsx
│   │   │   │   ├── FormRenderer.tsx
│   │   │   │   ├── FieldRenderer.tsx
│   │   │   │   ├── FilterBuilder.tsx
│   │   │   │   └── widgets/              # tip başına widget
│   │   │   ├── patterns/                 # CRUD, master-detail, tree, kanban...
│   │   │   └── dashboard/
│   │   └── lib/
│   │
│   └── docs/
│
├── packages/                             # paylaşılan TS paketleri
│   ├── nsl/                              # @namines/nsl
│   ├── client/                           # @namines/client
│   ├── ui/                               # @namines/ui
│   ├── cli/                              # namines (npm)
│   ├── prompts/                          # @namines/prompts
│   ├── evals/                            # @namines/evals
│   ├── eslint-config/
│   └── tsconfig/
│
├── services/
│   └── yjs/                              # Node CRDT sidecar
│
├── deploy/
│   ├── backend.env.example               ★
│   ├── frontend.env.example              ★
│   ├── k8s/
│   │   ├── base/
│   │   ├── overlays/{staging,production}/
│   │   └── sandbox/
│   ├── helm/namines/
│   └── terraform/
│
└── scripts/
    ├── dev-setup.sh / .ps1
    ├── seed-dev-data.ts
    ├── generate-golden-files.ts
    └── check-determinism.ts
```

---

## Faz 1 → Faz 2 dosya eşlemesi (göç rehberi)

| Faz 1 yolu | Faz 2 yolu |
|---|---|
| `backend/Namines.API/Controllers/*.cs` | `src/Namines.Api/Endpoints/*.cs` |
| `backend/Namines.Core/Models/DatabaseSchema.cs` | `src/Namines.Nsl/Model/*` (genişletilerek) |
| `backend/Namines.Core/Prompts/*.cs` | `packages/prompts/**/*.md` |
| `backend/Namines.Infrastructure/Generators/DdlGenerator/*` | `src/Namines.Compiler/Ddl/*` |
| `backend/Namines.Infrastructure/AI/*` | `src/Namines.Ai/Providers/*` |
| `backend/Namines.Infrastructure/Services/*` | `src/Namines.DataPlane/*` + `src/Namines.Ai/Agents/*` |
| `backend/Namines.API/Hubs/CanvasHub.cs` | `src/Namines.Realtime/Hubs/CanvasHub.cs` |
| `frontend/*` | `apps/web/*` |
| `scripts/namines-diff.mjs` | `packages/cli` + `src/Namines.Cli` |
| `Dockerfile.streamlit-base` | `src/Namines.Compiler/Templates/streamlit/` |
