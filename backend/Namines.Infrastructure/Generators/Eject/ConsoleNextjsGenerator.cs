using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using Namines.Core.Enums;
using Namines.Core.Interfaces;
using Namines.Core.Models;

namespace Namines.Infrastructure.Generators.Eject;

/// <summary>
/// <c>console.nextjs</c> — 07 §8. Şemadan çalışan bir admin paneli.
///
/// Panel Gateway'e API ANAHTARIYLA konuşuyor, veritabanına doğrudan DEĞİL. İki
/// sebep: (1) bağlantı dizesi tarayıcıya inmez, (2) Gateway'in tablo izinleri ve
/// PII maskelemesi panelde de geçerli olur — panel ayrı bir güvenlik yüzeyi açmaz.
///
/// <b>Dokümandan sapma, bilinçli:</b> 07 §8 "Next.js 16 + shadcn/ui + TanStack
/// Table" diyor. Üretilen paket bunları KULLANMIYOR; düz React + inline stil.
/// Sebep: shadcn kurulumu ayrı bir CLI adımı (<c>npx shadcn init</c>) ve onlarca
/// dosya ister, TanStack Table da ek bir bağımlılık. Kutudan çıktığı gibi
/// <c>npm install &amp;&amp; npm run dev</c> ile ÇALIŞAN bir panel, kurulum adımı
/// gerektiren "daha güzel" bir panelden iyidir. Kullanıcı isterse üstüne kurar —
/// çıktı gerçek kaynak kod, kilit yok.
/// </summary>
public sealed class ConsoleNextjsGenerator : IEjectGenerator
{
    public string Target => "console.nextjs";
    public string DisplayName => "Admin console (Next.js)";

    public EjectResult Generate(DatabaseSchema schema, DatabaseType engine)
    {
        var warnings = new List<string>();
        var metadata = ConsoleMetadata.Describe(schema);

        foreach (var table in metadata.Where(m => m.Pattern == PagePattern.ReadOnly))
            warnings.Add(
                $"{table.Table.Name}: no primary key, so the console can only list rows — " +
                "editing needs a key to target a single row safely.");

        foreach (var table in metadata.Where(m => m.Pattern == PagePattern.Junction))
            warnings.Add(
                $"{table.Table.Name}: detected as a junction table. It is listed but has no " +
                "dedicated editor; relationship editing is not generated yet.");

        if (schema.Tables.Any(t => t.Checks.Any(c => !string.IsNullOrWhiteSpace(c.Expression))))
            warnings.Add(
                "CHECK constraints are not enforced in the generated forms. The database still " +
                "rejects invalid values, but the user sees the error only after saving.");

        var files = new Dictionary<string, string>
        {
            ["package.json"] = PackageJson(schema),
            [".env.example"] = EnvExample(),
            ["README.md"] = Readme(schema, metadata),
            ["next.config.mjs"] = "export default {};\n",
            ["tsconfig.json"] = TsConfig(),
            ["lib/types.ts"] = new TypeScriptTypesGenerator().Generate(schema, engine).Files["types.ts"],
            ["lib/schema.ts"] = SchemaMetadata(metadata),
            ["lib/gateway.ts"] = GatewayClient(),
            ["app/layout.tsx"] = Layout(schema),
            ["app/page.tsx"] = IndexPage(),
            ["app/[table]/page.tsx"] = TablePage(),
        };

        return new EjectResult(files, warnings);
    }

    private static string PackageJson(DatabaseSchema schema)
    {
        var name = FlywayGenerator.Sanitize(schema.Name) + "-console";
        return $$"""
        {
          "name": "{{name}}",
          "private": true,
          "scripts": {
            "dev": "next dev",
            "build": "next build",
            "start": "next start"
          },
          "dependencies": {
            "next": "^15.0.0",
            "react": "^18.3.0",
            "react-dom": "^18.3.0"
          },
          "devDependencies": {
            "@types/node": "^20.0.0",
            "@types/react": "^18.3.0",
            "typescript": "^5.5.0"
          }
        }

        """;
    }

    private static string EnvExample() => """
        # The Namines Gateway this console talks to.
        NAMINES_API_URL=http://localhost:5000

        # A Gateway API key with read (and, if you want editing, write) permission
        # on the tables below. Create one in Namines under Review > Gateway API keys.
        #
        # This variable is NOT prefixed with NEXT_PUBLIC_ on purpose: the key stays
        # on the server. Exposing it to the browser would hand every visitor full
        # access to the tables it can reach.
        NAMINES_API_KEY=nmn_replace_me

        """;

    private static string TsConfig() => """
        {
          "compilerOptions": {
            "target": "ES2022",
            "lib": ["dom", "dom.iterable", "esnext"],
            "allowJs": true,
            "skipLibCheck": true,
            "strict": true,
            "noEmit": true,
            "esModuleInterop": true,
            "module": "esnext",
            "moduleResolution": "bundler",
            "resolveJsonModule": true,
            "isolatedModules": true,
            "jsx": "preserve",
            "incremental": true,
            "plugins": [{ "name": "next" }],
            "paths": { "@/*": ["./*"] }
          },
          "include": ["next-env.d.ts", "**/*.ts", "**/*.tsx", ".next/types/**/*.ts"],
          "exclude": ["node_modules"]
        }

        """;

    private static string Readme(DatabaseSchema schema, IReadOnlyList<TableMetadata> metadata)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# {schema.Name} — admin console");
        sb.AppendLine();
        sb.AppendLine("Generated by Namines from your schema. This is real source code: change");
        sb.AppendLine("anything you like, it will keep working without Namines.");
        sb.AppendLine();
        sb.AppendLine("## Run it");
        sb.AppendLine();
        sb.AppendLine("```bash");
        sb.AppendLine("cp .env.example .env.local   # then fill in NAMINES_API_KEY");
        sb.AppendLine("npm install");
        sb.AppendLine("npm run dev");
        sb.AppendLine("```");
        sb.AppendLine();
        sb.AppendLine("## How it talks to your data");
        sb.AppendLine();
        sb.AppendLine("The console calls the Namines Gateway, not your database directly. That");
        sb.AppendLine("means the API key's table permissions and PII masking apply here too — the");
        sb.AppendLine("console cannot reach anything the key cannot.");
        sb.AppendLine();
        sb.AppendLine("The key is read on the **server** only. It is never sent to the browser.");
        sb.AppendLine();
        sb.AppendLine("## Pages");
        sb.AppendLine();
        sb.AppendLine("| Table | Pattern |");
        sb.AppendLine("|---|---|");
        foreach (var table in metadata)
            sb.AppendLine($"| `{table.Table.Name}` | {table.Pattern} |");
        sb.AppendLine();
        sb.AppendLine("Patterns are chosen automatically from the schema shape: a table with no");
        sb.AppendLine("primary key is read-only, a table whose composite key is entirely foreign");
        sb.AppendLine("keys is a junction, a table referencing itself is a tree.");
        return sb.ToString();
    }

    /// <summary>
    /// Şema meta verisi VERİ olarak gömülüyor, sayfa başına kod olarak değil.
    ///
    /// 40 tablolu bir şemada 40 ayrı sayfa üretmek, kullanıcının bakımını yapacağı
    /// kod miktarını 40 katına çıkarır. Tek bir dinamik sayfa + veri, aynı işi
    /// yapıp değiştirilebilir kalıyor.
    /// </summary>
    private static string SchemaMetadata(IReadOnlyList<TableMetadata> metadata)
    {
        var payload = metadata.Select(m => new
        {
            name = m.Table.Name,
            pattern = m.Pattern.ToString().ToLowerInvariant(),
            primaryKey = m.PrimaryKey?.Name,
            labelColumn = m.LabelColumn?.Name,
            columns = m.Table.Columns.Select(c => new
            {
                name = c.Name,
                widget = ConsoleMetadata.Widget(c),
                nullable = c.IsNullable,
                isPrimaryKey = c.IsPK,
                maxLength = c.Length,
            }),
        });

        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });

        return $$"""
        // Generated by Namines. Do not edit by hand.

        export interface ConsoleColumn {
          name: string;
          widget: string;
          nullable: boolean;
          isPrimaryKey: boolean;
          maxLength: number | null;
        }

        export interface ConsoleTable {
          name: string;
          pattern: "crud" | "junction" | "tree" | "readonly";
          primaryKey: string | null;
          labelColumn: string | null;
          columns: ConsoleColumn[];
        }

        export const tables: ConsoleTable[] = {{json}};

        export function findTable(name: string): ConsoleTable | undefined {
          return tables.find((t) => t.name === name);
        }

        """;
    }

    private static string GatewayClient() => """
        // Generated by Namines. Do not edit by hand.
        //
        // Server-only: this module reads NAMINES_API_KEY. Importing it from a client
        // component would ship the key to the browser, so it is marked accordingly.
        import "server-only";

        const baseUrl = process.env.NAMINES_API_URL ?? "http://localhost:5000";
        const apiKey = process.env.NAMINES_API_KEY ?? "";

        export interface ListResult<T> {
          rows: T[];
          page: number;
          pageSize: number;
          totalCount: number;
        }

        async function call<T>(path: string, body: unknown): Promise<T> {
          if (!apiKey) {
            throw new Error(
              "NAMINES_API_KEY is not set. Copy .env.example to .env.local and fill it in."
            );
          }

          const response = await fetch(`${baseUrl}/api/gateway/${path}`, {
            method: "POST",
            headers: {
              "Content-Type": "application/json",
              "X-Namines-Key": apiKey,
            },
            body: JSON.stringify(body),
            // Admin console: always show current data, never a cached page.
            cache: "no-store",
          });

          if (!response.ok) {
            // The Gateway explains refusals precisely (permission, rate limit,
            // origin). Passing the message through is more useful than a generic
            // "request failed".
            const detail = await response.text();
            throw new Error(`Gateway ${response.status}: ${detail}`);
          }

          return (await response.json()) as T;
        }

        /**
         * The connection string lives on this server, not in Namines: the Gateway
         * needs it per request. Keep it beside the API key.
         */
        function connection() {
          const value = process.env.NAMINES_DB_CONNECTION;
          if (!value) {
            throw new Error(
              "NAMINES_DB_CONNECTION is not set. It is the connection string the Gateway uses."
            );
          }
          return value;
        }

        export function listRows<T>(
          tableName: string,
          page = 1,
          pageSize = 25,
          orderByColumn?: string | null
        ): Promise<ListResult<T>> {
          return call<ListResult<T>>("list", {
            connectionString: connection(),
            dbType: process.env.NAMINES_DB_TYPE ?? "PostgreSQL",
            tableName,
            page,
            pageSize,
            orderByColumn,
          });
        }

        export function deleteRow(tableName: string, pkColumn: string, pkValue: string) {
          return call<{ affectedRows: number }>("delete", {
            connectionString: connection(),
            dbType: process.env.NAMINES_DB_TYPE ?? "PostgreSQL",
            tableName,
            pkColumn,
            pkValue,
          });
        }

        """;

    /// <summary>
    /// Ham dize + Replace kullanılıyor, interpolasyon DEĞİL: JSX'te <c>style={{...}}</c>
    /// çift süslü parantez taşır ve C#'ın <c>$$</c> interpolasyon sınırlayıcısıyla
    /// çakışır (derleme hatası). Yer tutucu değiştirmek bu çakışmayı tamamen
    /// ortadan kaldırıyor.
    /// </summary>
    private static string Layout(DatabaseSchema schema) => """
        // Generated by Namines. Do not edit by hand.
        import type { ReactNode } from "react";
        import Link from "next/link";
        import { tables } from "../lib/schema";

        export const metadata = { title: "__SCHEMA_NAME__ — console" };

        export default function RootLayout({ children }: { children: ReactNode }) {
          return (
            <html lang="en">
              <body style={{ margin: 0, fontFamily: "system-ui, sans-serif", display: "flex" }}>
                <nav
                  style={{
                    width: 220,
                    minHeight: "100vh",
                    borderRight: "1px solid #e5e7eb",
                    padding: "16px 12px",
                  }}
                >
                  <Link href="/" style={{ fontWeight: 600, textDecoration: "none", color: "#111827" }}>
                    __SCHEMA_NAME__
                  </Link>
                  <ul style={{ listStyle: "none", padding: 0, marginTop: 16 }}>
                    {tables.map((table) => (
                      <li key={table.name} style={{ margin: "4px 0" }}>
                        <Link
                          href={`/${table.name}`}
                          style={{ textDecoration: "none", color: "#374151", fontSize: 14 }}
                        >
                          {table.name}
                        </Link>
                      </li>
                    ))}
                  </ul>
                </nav>
                <main style={{ flex: 1, padding: 24, minWidth: 0 }}>{children}</main>
              </body>
            </html>
          );
        }

        """.Replace("__SCHEMA_NAME__", schema.Name);

    private static string IndexPage() => """
        // Generated by Namines. Do not edit by hand.
        import Link from "next/link";
        import { tables } from "../lib/schema";

        export default function Home() {
          return (
            <div>
              <h1 style={{ fontSize: 20, marginTop: 0 }}>Tables</h1>
              <table style={{ borderCollapse: "collapse", fontSize: 14 }}>
                <thead>
                  <tr>
                    <th style={{ textAlign: "left", padding: "6px 12px" }}>Table</th>
                    <th style={{ textAlign: "left", padding: "6px 12px" }}>Pattern</th>
                    <th style={{ textAlign: "left", padding: "6px 12px" }}>Columns</th>
                  </tr>
                </thead>
                <tbody>
                  {tables.map((table) => (
                    <tr key={table.name} style={{ borderTop: "1px solid #e5e7eb" }}>
                      <td style={{ padding: "6px 12px" }}>
                        <Link href={`/${table.name}`}>{table.name}</Link>
                      </td>
                      <td style={{ padding: "6px 12px", color: "#6b7280" }}>{table.pattern}</td>
                      <td style={{ padding: "6px 12px", color: "#6b7280" }}>{table.columns.length}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          );
        }

        """;

    private static string TablePage() => """
        // Generated by Namines. Do not edit by hand.
        //
        // One dynamic page serves every table. Generating a file per table would
        // multiply the code you maintain by the number of tables without adding
        // anything — the shape differences live in lib/schema.ts as data.
        import { notFound } from "next/navigation";
        import { findTable } from "../../lib/schema";
        import { listRows } from "../../lib/gateway";

        export default async function TablePage({
          params,
          searchParams,
        }: {
          params: Promise<{ table: string }>;
          searchParams: Promise<{ page?: string }>;
        }) {
          const { table: tableName } = await params;
          const { page: pageParam } = await searchParams;

          const table = findTable(tableName);
          if (!table) notFound();

          const page = Number(pageParam ?? "1") || 1;

          let rows: Record<string, unknown>[] = [];
          let total = 0;
          let error: string | null = null;

          try {
            // Sıralama kolonu olmadan sayfalar arası tutarlılık garanti değildir;
            // birincil anahtar varsa onu kullanıyoruz.
            const result = await listRows<Record<string, unknown>>(
              table.name,
              page,
              25,
              table.primaryKey
            );
            rows = result.rows;
            total = result.totalCount;
          } catch (e) {
            error = e instanceof Error ? e.message : String(e);
          }

          if (error) {
            return (
              <div>
                <h1 style={{ fontSize: 20, marginTop: 0 }}>{table.name}</h1>
                <pre
                  style={{
                    background: "#fef2f2",
                    border: "1px solid #fecaca",
                    padding: 12,
                    borderRadius: 6,
                    fontSize: 13,
                    whiteSpace: "pre-wrap",
                  }}
                >
                  {error}
                </pre>
              </div>
            );
          }

          return (
            <div>
              <h1 style={{ fontSize: 20, marginTop: 0 }}>
                {table.name}{" "}
                <span style={{ fontSize: 13, color: "#6b7280", fontWeight: 400 }}>
                  {total >= 0 ? `${total} rows` : ""}
                </span>
              </h1>

              {table.pattern === "readonly" && (
                <p style={{ fontSize: 13, color: "#6b7280" }}>
                  This table has no primary key, so rows can be listed but not edited.
                </p>
              )}

              <div style={{ overflowX: "auto" }}>
                <table style={{ borderCollapse: "collapse", fontSize: 13, width: "100%" }}>
                  <thead>
                    <tr>
                      {table.columns.map((column) => (
                        <th
                          key={column.name}
                          style={{ textAlign: "left", padding: "6px 12px", whiteSpace: "nowrap" }}
                        >
                          {column.name}
                        </th>
                      ))}
                    </tr>
                  </thead>
                  <tbody>
                    {rows.map((row, index) => (
                      <tr key={index} style={{ borderTop: "1px solid #e5e7eb" }}>
                        {table.columns.map((column) => (
                          <td key={column.name} style={{ padding: "6px 12px", whiteSpace: "nowrap" }}>
                            {format(row[column.name])}
                          </td>
                        ))}
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>

              {rows.length === 0 && (
                <p style={{ fontSize: 13, color: "#6b7280" }}>No rows on this page.</p>
              )}

              <nav style={{ marginTop: 16, fontSize: 13 }}>
                {page > 1 && <a href={`/${table.name}?page=${page - 1}`}>← previous</a>}{" "}
                {rows.length === 25 && <a href={`/${table.name}?page=${page + 1}`}>next →</a>}
              </nav>
            </div>
          );
        }

        /** null ile boş metni ayırt eder: ikisi de boş görünürse veri yanlış okunur. */
        function format(value: unknown) {
          if (value === null || value === undefined) {
            return <span style={{ color: "#9ca3af" }}>null</span>;
          }
          if (typeof value === "object") return JSON.stringify(value);
          return String(value);
        }

        """;
}
