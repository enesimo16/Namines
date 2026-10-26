using System;
using System.Collections.Generic;
using System.Linq;
using Namines.Core.Models;

namespace Namines.Infrastructure.Catalogs;

public sealed record HubBlueprintDef(
    string Name,
    string Slug,
    string Description,
    string Category,
    string Tags,
    int StarCount,
    int ForkCount,
    int DaysAgo,
    DatabaseSchema Schema,
    string StarterIssueTitle,
    string StarterIssueBody,
    string StarterIssueTable,
    string? StarterIssueNslCode);

/// <summary>
/// Namines Hub ve MCP için 10 adet kurumsal, 3NF-onaylı ve doğrulanmış mimari şablonunun kataloğu.
/// </summary>
public static class HubBlueprintCatalog
{
    private static readonly Lazy<List<HubBlueprintDef>> _lazyList = new(BuildAll);

    public static IReadOnlyList<HubBlueprintDef> All => _lazyList.Value;

    public static HubBlueprintDef? FindBySlug(string slug) =>
        All.FirstOrDefault(b => string.Equals(b.Slug, slug, StringComparison.OrdinalIgnoreCase));

    public static IEnumerable<HubBlueprintDef> Search(string? query = null, string? category = null)
    {
        var items = (IEnumerable<HubBlueprintDef>)All;

        if (!string.IsNullOrWhiteSpace(category))
        {
            items = items.Where(b => string.Equals(b.Category, category, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(query))
        {
            var q = query.Trim().ToLowerInvariant();
            items = items.Where(b =>
                b.Name.ToLowerInvariant().Contains(q) ||
                b.Description.ToLowerInvariant().Contains(q) ||
                b.Tags.ToLowerInvariant().Contains(q) ||
                b.Slug.ToLowerInvariant().Contains(q) ||
                b.Schema.Tables.Any(t => t.Name.ToLowerInvariant().Contains(q)));
        }

        return items;
    }

    private static List<HubBlueprintDef> BuildAll()
    {
        return new List<HubBlueprintDef>
        {
            // 1. FinTech Ledger
            new(
                Name: "FinTech Ledger Engine",
                Slug: "fintech-ledger-engine",
                Description: "Double-entry general ledger architecture with immutable journal entries, multi-currency accounts, transactional balance verification, and complete audit trail.",
                Category: "FinTech",
                Tags: "fintech,ledger,double-entry,accounting,audit,3nf",
                StarCount: 142,
                ForkCount: 28,
                DaysAgo: 14,
                Schema: BuildFintechSchema(),
                StarterIssueTitle: "Add composite index on ledger_lines (account_id, entry_id)",
                StarterIssueBody: "For high-frequency bank reconciliation queries, ledger_lines should feature a composite index on account_id and entry_id to avoid sequential scans.",
                StarterIssueTable: "ledger_lines",
                StarterIssueNslCode: "NSL010"
            ),

            // 2. Enterprise ERP Core
            new(
                Name: "Enterprise ERP Core & Inventory",
                Slug: "enterprise-erp-core",
                Description: "Modular ERP foundation for enterprise resource planning with warehouses, product variants, inventory stock balances, vendors, and purchase orders.",
                Category: "ERP",
                Tags: "erp,inventory,supply-chain,procurement,3nf",
                StarCount: 218,
                ForkCount: 45,
                DaysAgo: 21,
                Schema: BuildErpSchema(),
                StarterIssueTitle: "Review stock_balances unique constraint across warehouse and variant",
                StarterIssueBody: "A compound unique constraint on (warehouse_id, variant_id) ensures no duplicate inventory records can exist for the same physical location.",
                StarterIssueTable: "stock_balances",
                StarterIssueNslCode: null
            ),

            // 3. Multi-Tenant SaaS
            new(
                Name: "Multi-Tenant B2B SaaS",
                Slug: "multitenant-b2b-saas",
                Description: "Production-ready B2B multi-tenant schema with strict organization isolation, workspace memberships, RBAC role permissions, and immutable audit logs.",
                Category: "SaaS",
                Tags: "saas,multi-tenant,rbac,enterprise,b2b",
                StarCount: 189,
                ForkCount: 37,
                DaysAgo: 10,
                Schema: BuildSaasSchema(),
                StarterIssueTitle: "Enforce cascading policy on organization deletion",
                StarterIssueBody: "Tenants and organization members should define explicit OnDelete behaviors to avoid orphaned records during workspace offboarding.",
                StarterIssueTable: "organization_members",
                StarterIssueNslCode: "NSL006"
            ),

            // 4. E-Commerce Pipeline
            new(
                Name: "E-Commerce Order Pipeline",
                Slug: "ecommerce-order-pipeline",
                Description: "Normalized order processing pipeline with customers, multiple delivery addresses, checkout orders, order items, coupon discounts, and shipment tracking.",
                Category: "E-Commerce",
                Tags: "ecommerce,checkout,order-management,retail",
                StarCount: 175,
                ForkCount: 32,
                DaysAgo: 8,
                Schema: BuildEcommerceSchema(),
                StarterIssueTitle: "Add foreign key index on order_items (order_id)",
                StarterIssueBody: "Order details query scans order_items frequently; ensure order_id is indexed to sustain checkout spikes.",
                StarterIssueTable: "order_items",
                StarterIssueNslCode: "NSL010"
            ),

            // 5. Healthcare EHR
            new(
                Name: "Healthcare EHR & Clinical Records",
                Slug: "healthcare-ehr-clinical",
                Description: "HIPAA-aligned electronic health record database model with patients, clinical appointments, diagnoses, medical prescriptions, and provider credentials.",
                Category: "Healthcare",
                Tags: "healthcare,ehr,hipaa,medical,clinical",
                StarCount: 96,
                ForkCount: 18,
                DaysAgo: 30,
                Schema: BuildHealthcareSchema(),
                StarterIssueTitle: "Audit trail requirement for clinical encounters",
                StarterIssueBody: "HIPAA compliance requires encounter modifications to record an explicit timestamp and practitioner ID for all diagnoses.",
                StarterIssueTable: "clinical_encounters",
                StarterIssueNslCode: null
            ),

            // 6. AI & LLM Ops Platform
            new(
                Name: "AI Prompt & LLM Ops Platform",
                Slug: "ai-llm-ops-platform",
                Description: "Scalable schema for LLM routing, token usage telemetry, prompt templates, version control, model evaluations, and agent execution runs.",
                Category: "AI",
                Tags: "ai,llm,prompt-engineering,telemetry,agent-eval",
                StarCount: 310,
                ForkCount: 64,
                DaysAgo: 5,
                Schema: BuildAiOpsSchema(),
                StarterIssueTitle: "Partition token_usages by month",
                StarterIssueBody: "Token usage logs grow rapidly with high-throughput agent runs. Consider PostgreSQL range partitioning on recorded_at.",
                StarterIssueTable: "token_usages",
                StarterIssueNslCode: null
            ),

            // 7. Logistics & Delivery Fleet
            new(
                Name: "Logistics & Delivery Fleet",
                Slug: "logistics-delivery-fleet",
                Description: "Fleet dispatch and delivery routing engine with couriers, dispatch hubs, delivery packages, route waypoints, and recipient signoffs.",
                Category: "Enterprise",
                Tags: "logistics,delivery,fleet,geo,tracking",
                StarCount: 87,
                ForkCount: 14,
                DaysAgo: 18,
                Schema: BuildLogisticsSchema(),
                StarterIssueTitle: "Waypoint sequencing check constraint",
                StarterIssueBody: "Add positive integer check constraint on stop_order in route_waypoints table.",
                StarterIssueTable: "route_waypoints",
                StarterIssueNslCode: null
            ),

            // 8. Social Community Network
            new(
                Name: "Social Community Network",
                Slug: "social-community-network",
                Description: "Social platform blueprint featuring user followers, posts, multimedia attachments, comments, threaded reactions, and community groups.",
                Category: "General",
                Tags: "social,community,feed,graph,ugc",
                StarCount: 154,
                ForkCount: 29,
                DaysAgo: 12,
                Schema: BuildSocialSchema(),
                StarterIssueTitle: "Prevent duplicate reactions per post and user",
                StarterIssueBody: "Ensure a compound unique constraint on (post_id, user_id, reaction_type) in reactions table.",
                StarterIssueTable: "reactions",
                StarterIssueNslCode: null
            ),

            // 9. LMS & Interactive Education
            new(
                Name: "LMS & Interactive Academy",
                Slug: "education-lms-academy",
                Description: "Learning management system architecture with courses, hierarchical curriculum modules, student enrollments, interactive quizzes, and grades.",
                Category: "General",
                Tags: "education,lms,courses,e-learning,academy",
                StarCount: 112,
                ForkCount: 22,
                DaysAgo: 16,
                Schema: BuildEducationSchema(),
                StarterIssueTitle: "Add index on enrollments (student_id, course_id)",
                StarterIssueBody: "Grade lookups require fast retrieval of course enrollments by student.",
                StarterIssueTable: "enrollments",
                StarterIssueNslCode: "NSL010"
            ),

            // 10. IoT Telemetry Mesh
            new(
                Name: "IoT Telemetry & Device Mesh",
                Slug: "iot-telemetry-mesh",
                Description: "Industrial IoT device fleet schema with hardware gateways, sensor nodes, metric readings, alert thresholds, and incident alarms.",
                Category: "Enterprise",
                Tags: "iot,telemetry,time-series,hardware,industrial",
                StarCount: 135,
                ForkCount: 26,
                DaysAgo: 25,
                Schema: BuildIotSchema(),
                StarterIssueTitle: "Define threshold severity enumeration",
                StarterIssueBody: "Replace severity text column with a structured enum type (info, warning, critical).",
                StarterIssueTable: "alert_rules",
                StarterIssueNslCode: null
            )
        };
    }

    private static SchemaTable Table(string id, string name, params SchemaColumn[] cols) =>
        new() { Id = id, Name = name, Columns = cols.ToList() };

    private static SchemaColumn Pk(string id, string name, string type = "uuid") =>
        new() { Id = id, Name = name, Type = type, IsPK = true, IsNullable = false };

    private static SchemaColumn Fk(string id, string name, string type = "uuid") =>
        new() { Id = id, Name = name, Type = type, IsFK = true, IsNullable = false };

    private static SchemaColumn Col(string id, string name, string type = "text", bool nullable = false) =>
        new() { Id = id, Name = name, Type = type, IsNullable = nullable };

    private static SchemaRelation Rel(string id, string fromTable, string fromCol, string toTable, string toCol) =>
        new() { Id = id, SourceTableId = fromTable, SourceColumnId = fromCol, TargetTableId = toTable, TargetColumnId = toCol };

    private static DatabaseSchema BuildFintechSchema()
    {
        var tCurrencies = Table("t_curr", "currencies", Pk("c1", "id"), Col("c2", "currency_code", "varchar"), Col("c3", "currency_name", "varchar"));
        var tAccounts = Table("t_acc", "accounts", Pk("a1", "id"), Col("a2", "account_number", "varchar"), Fk("a3", "currency_id"), Col("a4", "account_type", "varchar"), Col("a5", "balance", "decimal"));
        var tEntries = Table("t_entries", "journal_entries", Pk("e1", "id"), Col("e2", "entry_number", "varchar"), Col("e3", "entry_description", "text"), Col("e4", "posted_at", "timestamptz"));
        var tLines = Table("t_lines", "ledger_lines", Pk("l1", "id"), Fk("l2", "entry_id"), Fk("l3", "account_id"), Col("l4", "amount", "decimal"), Col("l5", "is_debit", "boolean"));
        var tRecon = Table("t_recon", "reconciliations", Pk("r1", "id"), Fk("r2", "account_id"), Col("r3", "reconciled_status", "varchar"), Col("r4", "reconciled_at", "timestamptz"));

        return new DatabaseSchema
        {
            Name = "FinTech Ledger",
            Tables = new List<SchemaTable> { tCurrencies, tAccounts, tEntries, tLines, tRecon },
            Relations = new List<SchemaRelation>
            {
                Rel("r_acc_curr", "t_acc", "a3", "t_curr", "c1"),
                Rel("r_lines_entry", "t_lines", "l2", "t_entries", "e1"),
                Rel("r_lines_acc", "t_lines", "l3", "t_acc", "a1"),
                Rel("r_recon_acc", "t_recon", "r2", "t_acc", "a1")
            }
        };
    }

    private static DatabaseSchema BuildErpSchema()
    {
        var tWh = Table("t_wh", "warehouses", Pk("w1", "id"), Col("w2", "warehouse_code", "varchar"), Col("w3", "warehouse_name", "varchar"));
        var tProd = Table("t_prod", "products", Pk("p1", "id"), Col("p2", "product_sku", "varchar"), Col("p3", "product_name", "varchar"));
        var tVariants = Table("t_var", "product_variants", Pk("v1", "id"), Fk("v2", "product_id"), Col("v3", "variant_code", "varchar"), Col("v4", "barcode", "varchar"));
        var tStock = Table("t_stock", "stock_balances", Pk("s1", "id"), Fk("s2", "warehouse_id"), Fk("s3", "variant_id"), Col("s4", "quantity_on_hand", "integer"));
        var tVendors = Table("t_ven", "vendors", Pk("vn1", "id"), Col("vn2", "vendor_name", "varchar"), Col("vn3", "tax_number", "varchar"));
        var tPo = Table("t_po", "purchase_orders", Pk("po1", "id"), Col("po2", "po_number", "varchar"), Fk("po3", "vendor_id"), Col("po4", "order_date", "timestamptz"));
        var tPoLines = Table("t_pol", "purchase_order_lines", Pk("pol1", "id"), Fk("pol2", "purchase_order_id"), Fk("pol3", "variant_id"), Col("pol4", "quantity", "integer"));

        return new DatabaseSchema
        {
            Name = "Enterprise ERP",
            Tables = new List<SchemaTable> { tWh, tProd, tVariants, tStock, tVendors, tPo, tPoLines },
            Relations = new List<SchemaRelation>
            {
                Rel("r_var_prod", "t_var", "v2", "t_prod", "p1"),
                Rel("r_stock_wh", "t_stock", "s2", "t_wh", "w1"),
                Rel("r_stock_var", "t_stock", "s3", "t_var", "v1"),
                Rel("r_po_ven", "t_po", "po3", "t_ven", "vn1"),
                Rel("r_pol_po", "t_pol", "pol2", "t_po", "po1"),
                Rel("r_pol_var", "t_pol", "pol3", "t_var", "v1")
            }
        };
    }

    private static DatabaseSchema BuildSaasSchema()
    {
        var tOrgs = Table("t_orgs", "organizations", Pk("o1", "id"), Col("o2", "org_slug", "varchar"), Col("o3", "display_name", "varchar"));
        var tTenants = Table("t_tenants", "tenants", Pk("tn1", "id"), Fk("tn2", "organization_id"), Col("tn3", "subdomain", "varchar"));
        var tUsers = Table("t_users", "users", Pk("u1", "id"), Col("u2", "user_email", "varchar"), Col("u3", "full_name", "varchar"));
        var tRoles = Table("t_roles", "roles", Pk("ro1", "id"), Col("ro2", "role_name", "varchar"));
        var tMembers = Table("t_members", "organization_members", Pk("m1", "id"), Fk("m2", "organization_id"), Fk("m3", "user_id"), Fk("m4", "role_id"));
        var tAudits = Table("t_audits", "audit_logs", Pk("au1", "id"), Fk("au2", "organization_id"), Fk("au3", "user_id"), Col("au4", "action_name", "varchar"), Col("au5", "created_at", "timestamptz"));

        return new DatabaseSchema
        {
            Name = "Multi-Tenant SaaS",
            Tables = new List<SchemaTable> { tOrgs, tTenants, tUsers, tRoles, tMembers, tAudits },
            Relations = new List<SchemaRelation>
            {
                Rel("r_tenants_org", "t_tenants", "tn2", "t_orgs", "o1"),
                Rel("r_mem_org", "t_members", "m2", "t_orgs", "o1"),
                Rel("r_mem_user", "t_members", "m3", "t_users", "u1"),
                Rel("r_mem_role", "t_members", "m4", "t_roles", "ro1"),
                Rel("r_audit_org", "t_audits", "au2", "t_orgs", "o1"),
                Rel("r_audit_user", "t_audits", "au3", "t_users", "u1")
            }
        };
    }

    private static DatabaseSchema BuildEcommerceSchema()
    {
        var tCust = Table("t_cust", "customers", Pk("cu1", "id"), Col("cu2", "customer_email", "varchar"), Col("cu3", "full_name", "varchar"));
        var tAddr = Table("t_addr", "customer_addresses", Pk("ad1", "id"), Fk("ad2", "customer_id"), Col("ad3", "street_line", "varchar"), Col("ad4", "city_name", "varchar"));
        var tProd = Table("t_p", "products", Pk("pr1", "id"), Col("pr2", "product_title", "varchar"), Col("pr3", "unit_price", "decimal"));
        var tOrders = Table("t_ord", "orders", Pk("or1", "id"), Col("or2", "order_number", "varchar"), Fk("or3", "customer_id"), Fk("or4", "address_id"), Col("or5", "total_price", "decimal"));
        var tOrderItems = Table("t_items", "order_items", Pk("oi1", "id"), Fk("oi2", "order_id"), Fk("oi3", "product_id"), Col("oi4", "quantity", "integer"));
        var tShip = Table("t_ship", "shipments", Pk("sh1", "id"), Fk("sh2", "order_id"), Col("sh3", "tracking_number", "varchar"), Col("sh4", "shipped_at", "timestamptz"));

        return new DatabaseSchema
        {
            Name = "E-Commerce Pipeline",
            Tables = new List<SchemaTable> { tCust, tAddr, tProd, tOrders, tOrderItems, tShip },
            Relations = new List<SchemaRelation>
            {
                Rel("r_addr_cust", "t_addr", "ad2", "t_cust", "cu1"),
                Rel("r_ord_cust", "t_orders", "or3", "t_cust", "cu1"),
                Rel("r_ord_addr", "t_orders", "or4", "t_addr", "ad1"),
                Rel("r_item_ord", "t_order_items", "oi2", "t_orders", "or1"),
                Rel("r_item_prod", "t_order_items", "oi3", "t_p", "pr1"),
                Rel("r_ship_ord", "t_ship", "sh2", "t_orders", "or1")
            }
        };
    }

    private static DatabaseSchema BuildHealthcareSchema()
    {
        var tPat = Table("t_pat", "patients", Pk("pt1", "id"), Col("pt2", "medical_number", "varchar"), Col("pt3", "birth_date", "date"));
        var tPrac = Table("t_prac", "practitioners", Pk("pc1", "id"), Col("pc2", "license_code", "varchar"), Col("pc3", "specialty_name", "varchar"));
        var tAppt = Table("t_appt", "appointments", Pk("ap1", "id"), Fk("ap2", "patient_id"), Fk("ap3", "practitioner_id"), Col("ap4", "scheduled_at", "timestamptz"));
        var tEnc = Table("t_enc", "clinical_encounters", Pk("en1", "id"), Fk("en2", "appointment_id"), Col("en3", "encounter_notes", "text"));
        var tPresc = Table("t_presc", "prescriptions", Pk("rx1", "id"), Fk("rx2", "encounter_id"), Col("rx3", "medication_name", "varchar"), Col("rx4", "dosage_instructions", "varchar"));
        var tDiag = Table("t_diag", "diagnoses", Pk("dg1", "id"), Fk("dg2", "encounter_id"), Col("dg3", "icd10_code", "varchar"), Col("dg4", "condition_title", "varchar"));

        return new DatabaseSchema
        {
            Name = "Healthcare Clinical Records",
            Tables = new List<SchemaTable> { tPat, tPrac, tAppt, tEnc, tPresc, tDiag },
            Relations = new List<SchemaRelation>
            {
                Rel("r_appt_pat", "t_appt", "ap2", "t_pat", "pt1"),
                Rel("r_appt_prac", "t_appt", "ap3", "t_prac", "pc1"),
                Rel("r_enc_appt", "t_enc", "en2", "t_appt", "ap1"),
                Rel("r_rx_enc", "t_presc", "rx2", "t_enc", "en1"),
                Rel("r_dg_enc", "t_diag", "dg2", "t_enc", "en1")
            }
        };
    }

    private static DatabaseSchema BuildAiOpsSchema()
    {
        var tModels = Table("t_models", "ai_models", Pk("m1", "id"), Col("m2", "model_identifier", "varchar"), Col("m3", "provider_name", "varchar"));
        var tPrompts = Table("t_prompts", "prompt_templates", Pk("pm1", "id"), Col("pm2", "template_slug", "varchar"), Col("pm3", "template_name", "varchar"));
        var tPVer = Table("t_pver", "prompt_versions", Pk("pv1", "id"), Fk("pv2", "template_id"), Col("pv3", "version_number", "integer"), Col("pv4", "system_prompt", "text"));
        var tSessions = Table("t_sess", "agent_sessions", Pk("as1", "id"), Col("as2", "session_label", "varchar"), Col("as3", "started_at", "timestamptz"));
        var tRuns = Table("t_runs", "session_runs", Pk("sr1", "id"), Fk("sr2", "session_id"), Fk("sr3", "model_id"), Fk("sr4", "prompt_version_id"), Col("sr5", "execution_time_ms", "integer"));
        var tUsage = Table("t_usage", "token_usages", Pk("tu1", "id"), Fk("tu2", "run_id"), Col("tu3", "prompt_tokens", "integer"), Col("tu4", "completion_tokens", "integer"));

        return new DatabaseSchema
        {
            Name = "AI & LLM Ops Platform",
            Tables = new List<SchemaTable> { tModels, tPrompts, tPVer, tSessions, tRuns, tUsage },
            Relations = new List<SchemaRelation>
            {
                Rel("r_pv_template", "t_pver", "pv2", "t_prompts", "pm1"),
                Rel("r_run_session", "t_runs", "sr2", "t_sess", "as1"),
                Rel("r_run_model", "t_runs", "sr3", "t_models", "m1"),
                Rel("r_run_pv", "t_runs", "sr4", "t_pver", "pv1"),
                Rel("r_usage_run", "t_usage", "tu2", "t_runs", "sr1")
            }
        };
    }

    private static DatabaseSchema BuildLogisticsSchema()
    {
        var tHubs = Table("t_hubs", "dispatch_hubs", Pk("hb1", "id"), Col("hb2", "hub_code", "varchar"), Col("hb3", "city_name", "varchar"));
        var tCouriers = Table("t_couriers", "couriers", Pk("cr1", "id"), Fk("cr2", "hub_id"), Col("cr3", "courier_name", "varchar"), Col("cr4", "vehicle_type", "varchar"));
        var tPackages = Table("t_packages", "delivery_packages", Pk("pk1", "id"), Fk("pk2", "hub_id"), Col("pk3", "tracking_code", "varchar"), Col("pk4", "weight_kg", "decimal"));
        var tRoutes = Table("t_routes", "delivery_routes", Pk("rt1", "id"), Fk("rt2", "courier_id"), Col("rt3", "departure_at", "timestamptz"));
        var tWaypoints = Table("t_waypoints", "route_waypoints", Pk("wp1", "id"), Fk("wp2", "route_id"), Fk("wp3", "package_id"), Col("wp4", "stop_sequence", "integer"));

        return new DatabaseSchema
        {
            Name = "Logistics Delivery Fleet",
            Tables = new List<SchemaTable> { tHubs, tCouriers, tPackages, tRoutes, tWaypoints },
            Relations = new List<SchemaRelation>
            {
                Rel("r_cr_hub", "t_couriers", "cr2", "t_hubs", "hb1"),
                Rel("r_pk_hub", "t_packages", "pk2", "t_hubs", "hb1"),
                Rel("r_rt_cr", "t_routes", "rt2", "t_couriers", "cr1"),
                Rel("r_wp_rt", "t_waypoints", "wp2", "t_routes", "rt1"),
                Rel("r_wp_pk", "t_waypoints", "wp3", "t_packages", "pk1")
            }
        };
    }

    private static DatabaseSchema BuildSocialSchema()
    {
        var tUsers = Table("t_u", "users", Pk("usr1", "id"), Col("usr2", "user_handle", "varchar"), Col("usr3", "display_name", "varchar"));
        var tComm = Table("t_comm", "communities", Pk("cm1", "id"), Col("cm2", "community_slug", "varchar"), Col("cm3", "title", "varchar"));
        var tMembers = Table("t_cmem", "community_members", Pk("cme1", "id"), Fk("cme2", "community_id"), Fk("cme3", "user_id"), Col("cme4", "member_role", "varchar"));
        var tPosts = Table("t_posts", "posts", Pk("ps1", "id"), Fk("ps2", "community_id"), Fk("ps3", "author_id"), Col("ps4", "post_body", "text"));
        var tComments = Table("t_comms", "post_comments", Pk("pcm1", "id"), Fk("pcm2", "post_id"), Fk("pcm3", "author_id"), Col("pcm4", "comment_text", "text"));
        var tReactions = Table("t_rx", "reactions", Pk("rxn1", "id"), Fk("rxn2", "post_id"), Fk("rxn3", "user_id"), Col("rxn4", "reaction_type", "varchar"));

        return new DatabaseSchema
        {
            Name = "Social Community Network",
            Tables = new List<SchemaTable> { tUsers, tComm, tMembers, tPosts, tComments, tReactions },
            Relations = new List<SchemaRelation>
            {
                Rel("r_cmem_comm", "t_cmem", "cme2", "t_comm", "cm1"),
                Rel("r_cmem_user", "t_cmem", "cme3", "t_u", "usr1"),
                Rel("r_post_comm", "t_posts", "ps2", "t_comm", "cm1"),
                Rel("r_post_user", "t_posts", "ps3", "t_u", "usr1"),
                Rel("r_pcm_post", "t_comments", "pcm2", "t_posts", "ps1"),
                Rel("r_pcm_user", "t_comments", "pcm3", "t_u", "usr1"),
                Rel("r_rx_post", "t_reactions", "rxn2", "t_posts", "ps1"),
                Rel("r_rx_user", "t_reactions", "rxn3", "t_u", "usr1")
            }
        };
    }

    private static DatabaseSchema BuildEducationSchema()
    {
        var tInst = Table("t_inst", "instructors", Pk("ins1", "id"), Col("ins2", "instructor_name", "varchar"), Col("ins3", "academic_field", "varchar"));
        var tCourses = Table("t_courses", "courses", Pk("cs1", "id"), Col("cs2", "course_code", "varchar"), Fk("cs3", "instructor_id"), Col("cs4", "course_name", "varchar"));
        var tModules = Table("t_mods", "course_modules", Pk("md1", "id"), Fk("md2", "course_id"), Col("md3", "module_name", "varchar"), Col("md4", "sequence_number", "integer"));
        var tLessons = Table("t_lessons", "lessons", Pk("ls1", "id"), Fk("ls2", "module_id"), Col("ls3", "lesson_title", "varchar"), Col("ls4", "video_url", "varchar"));
        var tStudents = Table("t_std", "students", Pk("st1", "id"), Col("st2", "student_number", "varchar"), Col("st3", "student_name", "varchar"));
        var tEnroll = Table("t_enroll", "enrollments", Pk("enr1", "id"), Fk("enr2", "course_id"), Fk("enr3", "student_id"), Col("enr4", "grade_mark", "decimal"));
        var tSubm = Table("t_subm", "quiz_submissions", Pk("qz1", "id"), Fk("qz2", "lesson_id"), Fk("qz3", "student_id"), Col("qz4", "score_percent", "integer"));

        return new DatabaseSchema
        {
            Name = "LMS & Interactive Academy",
            Tables = new List<SchemaTable> { tInst, tCourses, tModules, tLessons, tStudents, tEnroll, tSubm },
            Relations = new List<SchemaRelation>
            {
                Rel("r_cs_inst", "t_courses", "cs3", "t_inst", "ins1"),
                Rel("r_md_cs", "t_modules", "md2", "t_courses", "cs1"),
                Rel("r_ls_md", "t_lessons", "ls2", "t_modules", "md1"),
                Rel("r_enr_cs", "t_enroll", "enr2", "t_courses", "cs1"),
                Rel("r_enr_st", "t_enroll", "enr3", "t_std", "st1"),
                Rel("r_qz_ls", "t_subm", "qz2", "t_lessons", "ls1"),
                Rel("r_qz_st", "t_subm", "qz3", "t_std", "st1")
            }
        };
    }

    private static DatabaseSchema BuildIotSchema()
    {
        var tGtw = Table("t_gtw", "gateways", Pk("gw1", "id"), Col("gw2", "gateway_code", "varchar"), Col("gw3", "zone_name", "varchar"));
        var tNodes = Table("t_nodes", "sensor_nodes", Pk("sn1", "id"), Fk("sn2", "gateway_id"), Col("sn3", "serial_number", "varchar"));
        var tMetrics = Table("t_metrics", "metric_types", Pk("mt1", "id"), Col("mt2", "metric_code", "varchar"), Col("mt3", "measurement_unit", "varchar"));
        var tTelem = Table("t_telem", "sensor_telemetry", Pk("tm1", "id"), Fk("tm2", "sensor_id"), Fk("tm3", "metric_id"), Col("tm4", "metric_value", "decimal"), Col("tm5", "recorded_at", "timestamptz"));
        var tRules = Table("t_rules", "alert_rules", Pk("ar1", "id"), Fk("ar2", "metric_id"), Col("ar3", "threshold_max", "decimal"), Col("ar4", "severity_label", "varchar"));
        var tInc = Table("t_inc", "device_incidents", Pk("inc1", "id"), Fk("inc2", "sensor_id"), Fk("inc3", "alert_rule_id"), Col("inc4", "incident_time", "timestamptz"));

        return new DatabaseSchema
        {
            Name = "IoT Telemetry & Device Mesh",
            Tables = new List<SchemaTable> { tGtw, tNodes, tMetrics, tTelem, tRules, tInc },
            Relations = new List<SchemaRelation>
            {
                Rel("r_node_gtw", "t_nodes", "sn2", "t_gtw", "gw1"),
                Rel("r_tm_node", "t_telem", "tm2", "t_nodes", "sn1"),
                Rel("r_tm_metric", "t_telem", "tm3", "t_metrics", "mt1"),
                Rel("r_rule_metric", "t_rules", "ar2", "t_metrics", "mt1"),
                Rel("r_inc_node", "t_inc", "inc2", "t_nodes", "sn1"),
                Rel("r_inc_rule", "t_inc", "inc3", "t_rules", "ar1")
            }
        };
    }
}
