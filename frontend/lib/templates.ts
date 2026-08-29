import { DatabaseSchema, SchemaTable, SchemaColumn, SchemaRelation } from '../types/schema';

/**
 * Şema şablonları.
 *
 * **Neden kompakt bir tanım dili:** şablonlar önce her kolon ve her ilişki için
 * elle kimlik yazılarak tutuluyordu (`col('ec-p5', 'category_id', ...)` +
 * ayrıca `rel('ec-rel1', ..., 'ec-p5', 'ec-cats', 'ec-ca1')`). Beş küçük şablonda
 * bu katlanılabilirdi; 20-25 tabloluk gerçekçi şablonlarda aynı kimliği üç ayrı
 * yerde doğru yazmak demek olurdu ve **sessizce kırık ilişki** üretmenin en kolay
 * yolu budur — bağıntı ekranda hiç çizilmez, kimse de fark etmez.
 *
 * Artık yabancı anahtar, kolonun kendi tanımında duruyor (`>tablo.kolon`) ve
 * ilişkiler oradan TÜRETİLİYOR. Hedefi olmayan bir bağıntı **açılışta hata
 * fırlatıyor**: kırık bir şablonla sessizce çalışmaktansa hemen görünmesi daha
 * iyi, çünkü şablonlar ürünün ilk temas yüzeyi (bkz. second-phase/17).
 *
 * ### Kolon dili
 * ```
 * 'ad TİP [bayraklar]'
 *   pk            birincil anahtar (NOT NULL ima eder)
 *   !             NOT NULL
 *   ?             NULL kabul eder — yabancı anahtarın varsayılanını bozar
 *   >tablo.kolon  yabancı anahtar (NOT NULL ima eder, '?' ile gevşetilir)
 * ```
 *
 * ### Şablonların uyduğu kurallar
 * Hepsi ürünün KENDİ kural motorundan hata/uyarısız geçiyor
 * (`npm run check:templates`). Bu tesadüf değil, şart: "AI üretir, kural motoru
 * kanıtlar" diyen bir ürünün kendi örnek şemalarının o motordan geçememesi,
 * iddiayı ilk temasta çürütürdü. Pratikte üç kurala dönüşüyor:
 *
 * 1. **Her tabloda tam bir birincil anahtar** — ara tablolarda bile. Bileşik
 *    anahtar yerine vekil `id` kullanılıyor, çünkü iki `pk` "birden çok birincil
 *    anahtar" hatası veriyor, sıfır `pk` ise "birincil anahtar yok" uyarısı.
 * 2. **Yabancı anahtarın tipi hedefin tipiyle aynı** — farklıysa hata.
 * 3. **İki tablo birbirini işaret etmiyor** (A→B ve B→A döngü uyarısı verir).
 *    Kendine dönen bağıntılar (`parent_id`, `manager_id`) güvenli ve kullanılıyor.
 */

// ── Kompakt tanım ────────────────────────────────────────────────────────────

/**
 * Şablonun ÖLÇEĞİ — bir etiket değil, bir vaat.
 *
 * Üç kategori var çünkü şablonlar iki farklı işe yarıyor ve tek bir boyut
 * ikisini birden yapamıyor: `mini` bir şeyi hemen kurup üstüne inşa etmek
 * için (6 tabloluk bir URL kısaltıcıya 25 tablo dayatmak, kullanıcıya
 * silecek 19 tablo vermek demek), `large` ise ürünün gerçek ölçekte ne
 * yaptığını göstermek için. `standard` ikisinin arası ve varsayılan.
 *
 * `check:templates` her kategoriye AYRI tablo aralığı uyguluyor: kategori
 * kutuyu doldurmayan bir şablon, yanlış yerde durup yanlış beklenti kurar.
 */
export type TemplateSize = 'mini' | 'standard' | 'large';

interface TemplateSpec {
  key: string;
  label: string;
  description: string;
  size: TemplateSize;
  /** Tablo adı → kolon tanımları. Sıra, tuvaldeki yerleşimi belirliyor. */
  tables: Record<string, string[]>;
}

interface ParsedColumn {
  name: string;
  type: string;
  isPK: boolean;
  isNullable: boolean;
  ref: { table: string; column: string } | null;
}

function parseColumn(spec: string): ParsedColumn {
  const [name, type, ...flags] = spec.trim().split(/\s+/);
  if (!name || !type) throw new Error(`Malformed column spec: "${spec}"`);

  const refFlag = flags.find(f => f.startsWith('>'));
  const ref = refFlag ? refFlag.slice(1) : null;
  const [refTable, refColumn] = ref ? ref.split('.') : [null, null];

  if (ref && (!refTable || !refColumn)) {
    throw new Error(`Malformed reference in "${spec}" — expected >table.column`);
  }

  const isPK = flags.includes('pk');
  // Yabancı anahtar varsayılan olarak zorunlu: isteğe bağlı olan istisnadır ve
  // yazarın onu açıkça '?' ile belirtmesi, unutulduğunda sessizce gevşek bir
  // şema üretmekten iyi.
  const required = isPK || flags.includes('!') || (ref !== null && !flags.includes('?'));

  return {
    name,
    type,
    isPK,
    isNullable: !required,
    ref: refTable && refColumn ? { table: refTable, column: refColumn } : null,
  };
}

function build(spec: TemplateSpec): DatabaseSchema {
  const tableId = (t: string) => `${spec.key}-${t}`;
  const columnId = (t: string, c: string) => `${spec.key}-${t}-${c}`;

  const parsed = Object.entries(spec.tables).map(([name, columns]) => ({
    name,
    columns: columns.map(parseColumn),
  }));

  const byName = new Map(parsed.map(t => [t.name, t]));

  const tables: SchemaTable[] = parsed.map(t => ({
    id: tableId(t.name),
    stableUuid: tableId(t.name),
    name: t.name,
    columns: t.columns.map<SchemaColumn>(c => ({
      id: columnId(t.name, c.name),
      stableUuid: columnId(t.name, c.name),
      name: c.name,
      type: c.type,
      isPK: c.isPK,
      isFK: c.ref !== null,
      isNullable: c.isNullable,
      length: null,
      defaultValue: null,
    })),
  }));

  const relations: SchemaRelation[] = [];
  for (const table of parsed) {
    for (const column of table.columns) {
      if (!column.ref) continue;

      const target = byName.get(column.ref.table);
      const targetColumn = target?.columns.find(c => c.name === column.ref!.column);

      // Hedefi olmayan bir bağıntı, tuvalde çizilmeyen ve kimsenin fark etmediği
      // bir bağ demek. Sessizce atlamak yerine açılışta patlıyor.
      if (!target || !targetColumn) {
        throw new Error(
          `Template "${spec.key}": ${table.name}.${column.name} references ` +
          `${column.ref.table}.${column.ref.column}, which does not exist.`,
        );
      }

      relations.push({
        id: `${spec.key}-fk-${table.name}-${column.name}`,
        type: 'ManyToOne',
        sourceTableId: tableId(table.name),
        sourceColumnId: columnId(table.name, column.name),
        targetTableId: tableId(target.name),
        targetColumnId: columnId(target.name, targetColumn.name),
      });
    }
  }

  return { schemaId: `tpl-${spec.key}`, name: spec.label, tables, relations };
}

// ── Şablon tanımları ─────────────────────────────────────────────────────────

const SPECS: TemplateSpec[] = [
  {
    key: 'ecommerce',
    label: 'E-Commerce',
    description:
      'Catalogue with variants, multi-warehouse stock, carts, orders, split shipments, payments, refunds and coupons.',
    size: 'standard',
    tables: {
      users: [
        'id INT pk', 'email VARCHAR !', 'password_hash VARCHAR !', 'full_name VARCHAR',
        'phone VARCHAR', 'is_active BOOLEAN !', 'created_at TIMESTAMP !',
      ],
      addresses: [
        'id INT pk', 'user_id INT >users.id', 'label VARCHAR', 'line1 VARCHAR !',
        'line2 VARCHAR', 'city VARCHAR !', 'postal_code VARCHAR', 'country_code VARCHAR !',
        'is_default BOOLEAN !',
      ],
      categories: [
        'id INT pk', 'parent_id INT >categories.id ?', 'name VARCHAR !', 'slug VARCHAR !',
        'position INT !',
      ],
      brands: ['id INT pk', 'name VARCHAR !', 'slug VARCHAR !', 'logo_url VARCHAR'],
      products: [
        'id INT pk', 'category_id INT >categories.id', 'brand_id INT >brands.id ?',
        'name VARCHAR !', 'slug VARCHAR !', 'description TEXT', 'status VARCHAR !',
        'created_at TIMESTAMP !',
      ],
      product_variants: [
        'id INT pk', 'product_id INT >products.id', 'sku VARCHAR !', 'title VARCHAR !',
        'price DECIMAL !', 'compare_at_price DECIMAL', 'weight_grams INT', 'barcode VARCHAR',
      ],
      product_images: [
        'id INT pk', 'product_id INT >products.id', 'variant_id INT >product_variants.id ?',
        'url VARCHAR !', 'alt_text VARCHAR', 'position INT !',
      ],
      warehouses: [
        'id INT pk', 'code VARCHAR !', 'name VARCHAR !', 'city VARCHAR', 'country_code VARCHAR !',
      ],
      inventory_items: [
        'id INT pk', 'variant_id INT >product_variants.id', 'warehouse_id INT >warehouses.id',
        'on_hand INT !', 'reserved INT !', 'reorder_point INT',
      ],
      suppliers: [
        'id INT pk', 'name VARCHAR !', 'contact_email VARCHAR', 'phone VARCHAR', 'country_code VARCHAR',
      ],
      purchase_orders: [
        'id INT pk', 'supplier_id INT >suppliers.id', 'warehouse_id INT >warehouses.id',
        'status VARCHAR !', 'ordered_at TIMESTAMP !', 'expected_at DATE', 'total DECIMAL !',
      ],
      carts: [
        'id INT pk', 'user_id INT >users.id ?', 'session_token VARCHAR', 'currency VARCHAR !',
        'created_at TIMESTAMP !', 'updated_at TIMESTAMP',
      ],
      cart_items: [
        'id INT pk', 'cart_id INT >carts.id', 'variant_id INT >product_variants.id',
        'quantity INT !', 'unit_price DECIMAL !',
      ],
      coupons: [
        'id INT pk', 'code VARCHAR !', 'discount_type VARCHAR !', 'discount_value DECIMAL !',
        'starts_at TIMESTAMP', 'ends_at TIMESTAMP', 'max_redemptions INT',
      ],
      orders: [
        'id INT pk', 'user_id INT >users.id', 'shipping_address_id INT >addresses.id ?',
        'billing_address_id INT >addresses.id ?', 'number VARCHAR !', 'status VARCHAR !',
        'currency VARCHAR !', 'subtotal DECIMAL !', 'shipping_total DECIMAL !',
        'discount_total DECIMAL !', 'grand_total DECIMAL !', 'placed_at TIMESTAMP !',
      ],
      order_items: [
        'id INT pk', 'order_id INT >orders.id', 'variant_id INT >product_variants.id',
        'quantity INT !', 'unit_price DECIMAL !', 'line_total DECIMAL !',
      ],
      coupon_redemptions: [
        'id INT pk', 'coupon_id INT >coupons.id', 'order_id INT >orders.id',
        'amount_off DECIMAL !', 'redeemed_at TIMESTAMP !',
      ],
      order_shipments: [
        'id INT pk', 'order_id INT >orders.id', 'warehouse_id INT >warehouses.id',
        'carrier VARCHAR', 'tracking_number VARCHAR', 'status VARCHAR !', 'shipped_at TIMESTAMP',
      ],
      shipment_items: [
        'id INT pk', 'shipment_id INT >order_shipments.id', 'order_item_id INT >order_items.id',
        'quantity INT !',
      ],
      payments: [
        'id INT pk', 'order_id INT >orders.id', 'provider VARCHAR !', 'provider_reference VARCHAR',
        'status VARCHAR !', 'amount DECIMAL !', 'currency VARCHAR !', 'captured_at TIMESTAMP',
      ],
      refunds: [
        'id INT pk', 'payment_id INT >payments.id', 'amount DECIMAL !', 'reason VARCHAR',
        'status VARCHAR !', 'created_at TIMESTAMP !',
      ],
      reviews: [
        'id INT pk', 'product_id INT >products.id', 'user_id INT >users.id',
        'order_item_id INT >order_items.id ?', 'rating INT !', 'title VARCHAR', 'body TEXT',
        'created_at TIMESTAMP !',
      ],
      review_votes: [
        'id INT pk', 'review_id INT >reviews.id', 'user_id INT >users.id', 'is_helpful BOOLEAN !',
      ],
      wishlists: [
        'id INT pk', 'user_id INT >users.id', 'name VARCHAR !', 'is_public BOOLEAN !',
      ],
      wishlist_items: [
        'id INT pk', 'wishlist_id INT >wishlists.id', 'variant_id INT >product_variants.id',
        'added_at TIMESTAMP !',
      ],
    },
  },

  {
    key: 'saas',
    label: 'SaaS Platform',
    description:
      'Multi-tenant workspaces with roles and permissions, metered billing, API keys, webhooks and an audit trail.',
    size: 'standard',
    tables: {
      users: [
        'id UUID pk', 'email VARCHAR !', 'password_hash VARCHAR', 'full_name VARCHAR',
        'avatar_url VARCHAR', 'email_verified_at TIMESTAMP', 'created_at TIMESTAMP !',
      ],
      sessions: [
        'id UUID pk', 'user_id UUID >users.id', 'token_hash VARCHAR !', 'ip_address VARCHAR',
        'user_agent VARCHAR', 'expires_at TIMESTAMP !', 'created_at TIMESTAMP !',
      ],
      organizations: [
        'id UUID pk', 'owner_id UUID >users.id', 'name VARCHAR !', 'slug VARCHAR !',
        'logo_url VARCHAR', 'created_at TIMESTAMP !',
      ],
      roles: [
        'id UUID pk', 'organization_id UUID >organizations.id ?', 'name VARCHAR !',
        'is_system BOOLEAN !',
      ],
      permissions: ['id UUID pk', 'code VARCHAR !', 'description VARCHAR'],
      role_permissions: [
        'id UUID pk', 'role_id UUID >roles.id', 'permission_id UUID >permissions.id',
      ],
      memberships: [
        'id UUID pk', 'user_id UUID >users.id', 'organization_id UUID >organizations.id',
        'role_id UUID >roles.id', 'status VARCHAR !', 'joined_at TIMESTAMP !',
      ],
      invitations: [
        'id UUID pk', 'organization_id UUID >organizations.id', 'invited_by UUID >users.id',
        'role_id UUID >roles.id', 'email VARCHAR !', 'token_hash VARCHAR !',
        'expires_at TIMESTAMP !', 'accepted_at TIMESTAMP',
      ],
      projects: [
        'id UUID pk', 'organization_id UUID >organizations.id', 'name VARCHAR !', 'slug VARCHAR !',
        'archived_at TIMESTAMP', 'created_at TIMESTAMP !',
      ],
      project_members: [
        'id UUID pk', 'project_id UUID >projects.id', 'user_id UUID >users.id',
        'access_level VARCHAR !',
      ],
      api_keys: [
        'id UUID pk', 'organization_id UUID >organizations.id', 'created_by UUID >users.id',
        'name VARCHAR !', 'key_hash VARCHAR !', 'last_used_at TIMESTAMP', 'expires_at TIMESTAMP',
        'revoked_at TIMESTAMP',
      ],
      api_key_scopes: [
        'id UUID pk', 'api_key_id UUID >api_keys.id', 'permission_id UUID >permissions.id',
      ],
      webhooks: [
        'id UUID pk', 'organization_id UUID >organizations.id', 'target_url VARCHAR !',
        'secret VARCHAR !', 'event_types VARCHAR !', 'is_active BOOLEAN !',
      ],
      webhook_deliveries: [
        'id UUID pk', 'webhook_id UUID >webhooks.id', 'event_type VARCHAR !',
        'response_status INT', 'attempt INT !', 'delivered_at TIMESTAMP', 'payload TEXT',
      ],
      plans: [
        'id UUID pk', 'code VARCHAR !', 'name VARCHAR !', 'monthly_price DECIMAL !',
        'yearly_price DECIMAL', 'seat_limit INT', 'is_public BOOLEAN !',
      ],
      subscriptions: [
        'id UUID pk', 'organization_id UUID >organizations.id', 'plan_id UUID >plans.id',
        'provider_reference VARCHAR', 'status VARCHAR !', 'seats INT !',
        'current_period_start TIMESTAMP !', 'current_period_end TIMESTAMP !',
        'cancel_at TIMESTAMP',
      ],
      invoices: [
        'id UUID pk', 'subscription_id UUID >subscriptions.id', 'number VARCHAR !',
        'status VARCHAR !', 'total DECIMAL !', 'currency VARCHAR !', 'issued_at TIMESTAMP !',
        'paid_at TIMESTAMP',
      ],
      invoice_lines: [
        'id UUID pk', 'invoice_id UUID >invoices.id', 'description VARCHAR !', 'quantity INT !',
        'unit_price DECIMAL !', 'amount DECIMAL !',
      ],
      usage_records: [
        'id UUID pk', 'organization_id UUID >organizations.id', 'metric VARCHAR !',
        'quantity BIGINT !', 'recorded_at TIMESTAMP !',
      ],
      feature_flags: [
        'id UUID pk', 'key VARCHAR !', 'description VARCHAR', 'default_enabled BOOLEAN !',
      ],
      feature_flag_overrides: [
        'id UUID pk', 'feature_flag_id UUID >feature_flags.id',
        'organization_id UUID >organizations.id', 'is_enabled BOOLEAN !',
      ],
      notifications: [
        'id UUID pk', 'user_id UUID >users.id', 'type VARCHAR !', 'body TEXT',
        'read_at TIMESTAMP', 'created_at TIMESTAMP !',
      ],
      audit_logs: [
        'id UUID pk', 'organization_id UUID >organizations.id', 'actor_id UUID >users.id ?',
        'action VARCHAR !', 'entity_type VARCHAR !', 'entity_id VARCHAR', 'ip_address VARCHAR',
        'created_at TIMESTAMP !',
      ],
    },
  },

  {
    key: 'cms',
    label: 'Publishing / CMS',
    description:
      'Multi-site publishing with revisions, editorial roles, media library, menus, redirects and moderated comments.',
    size: 'standard',
    tables: {
      users: [
        'id INT pk', 'email VARCHAR !', 'display_name VARCHAR !', 'password_hash VARCHAR !',
        'bio TEXT', 'created_at TIMESTAMP !',
      ],
      roles: ['id INT pk', 'name VARCHAR !', 'description VARCHAR'],
      user_roles: ['id INT pk', 'user_id INT >users.id', 'role_id INT >roles.id'],
      sites: [
        'id INT pk', 'name VARCHAR !', 'domain VARCHAR !', 'default_locale VARCHAR !',
        'is_published BOOLEAN !',
      ],
      media_folders: [
        'id INT pk', 'site_id INT >sites.id', 'parent_id INT >media_folders.id ?', 'name VARCHAR !',
      ],
      media_assets: [
        'id INT pk', 'folder_id INT >media_folders.id ?', 'uploaded_by INT >users.id',
        'file_name VARCHAR !', 'mime_type VARCHAR !', 'size_bytes BIGINT !', 'url VARCHAR !',
        'alt_text VARCHAR', 'uploaded_at TIMESTAMP !',
      ],
      categories: [
        'id INT pk', 'site_id INT >sites.id', 'parent_id INT >categories.id ?', 'name VARCHAR !',
        'slug VARCHAR !',
      ],
      tags: ['id INT pk', 'site_id INT >sites.id', 'name VARCHAR !', 'slug VARCHAR !'],
      posts: [
        'id INT pk', 'site_id INT >sites.id', 'author_id INT >users.id',
        'cover_image_id INT >media_assets.id ?', 'title VARCHAR !', 'slug VARCHAR !',
        'excerpt TEXT', 'body TEXT', 'status VARCHAR !', 'published_at TIMESTAMP',
        'created_at TIMESTAMP !',
      ],
      post_revisions: [
        'id INT pk', 'post_id INT >posts.id', 'edited_by INT >users.id', 'title VARCHAR !',
        'body TEXT', 'revision_number INT !', 'created_at TIMESTAMP !',
      ],
      post_categories: [
        'id INT pk', 'post_id INT >posts.id', 'category_id INT >categories.id',
      ],
      post_tags: ['id INT pk', 'post_id INT >posts.id', 'tag_id INT >tags.id'],
      pages: [
        'id INT pk', 'site_id INT >sites.id', 'parent_id INT >pages.id ?',
        'author_id INT >users.id', 'title VARCHAR !', 'slug VARCHAR !', 'body TEXT',
        'template VARCHAR', 'status VARCHAR !', 'published_at TIMESTAMP',
      ],
      page_revisions: [
        'id INT pk', 'page_id INT >pages.id', 'edited_by INT >users.id', 'body TEXT',
        'revision_number INT !', 'created_at TIMESTAMP !',
      ],
      comments: [
        'id INT pk', 'post_id INT >posts.id', 'parent_id INT >comments.id ?',
        'author_id INT >users.id ?', 'guest_name VARCHAR', 'body TEXT !', 'status VARCHAR !',
        'created_at TIMESTAMP !',
      ],
      comment_reports: [
        'id INT pk', 'comment_id INT >comments.id', 'reported_by INT >users.id ?',
        'reason VARCHAR !', 'resolved_at TIMESTAMP',
      ],
      menus: ['id INT pk', 'site_id INT >sites.id', 'name VARCHAR !', 'location VARCHAR !'],
      menu_items: [
        'id INT pk', 'menu_id INT >menus.id', 'parent_id INT >menu_items.id ?',
        'page_id INT >pages.id ?', 'label VARCHAR !', 'url VARCHAR', 'position INT !',
      ],
      redirects: [
        'id INT pk', 'site_id INT >sites.id', 'from_path VARCHAR !', 'to_path VARCHAR !',
        'status_code INT !',
      ],
      forms: [
        'id INT pk', 'site_id INT >sites.id', 'name VARCHAR !', 'fields_json TEXT',
        'notify_email VARCHAR',
      ],
      form_submissions: [
        'id INT pk', 'form_id INT >forms.id', 'payload TEXT', 'ip_address VARCHAR',
        'submitted_at TIMESTAMP !',
      ],
    },
  },

  {
    key: 'crm',
    label: 'CRM & Sales',
    description:
      'Accounts, contacts and leads through a staged pipeline, with quotes, activities, campaigns and territories.',
    size: 'standard',
    tables: {
      users: [
        'id INT pk', 'email VARCHAR !', 'full_name VARCHAR !', 'title VARCHAR',
        'manager_id INT >users.id ?', 'is_active BOOLEAN !',
      ],
      territories: [
        'id INT pk', 'parent_id INT >territories.id ?', 'name VARCHAR !', 'region_code VARCHAR',
      ],
      teams: ['id INT pk', 'name VARCHAR !', 'territory_id INT >territories.id ?'],
      team_members: ['id INT pk', 'team_id INT >teams.id', 'user_id INT >users.id', 'role VARCHAR'],
      accounts: [
        'id INT pk', 'owner_id INT >users.id', 'territory_id INT >territories.id ?',
        'name VARCHAR !', 'industry VARCHAR', 'website VARCHAR', 'employee_count INT',
        'annual_revenue DECIMAL', 'created_at TIMESTAMP !',
      ],
      contacts: [
        'id INT pk', 'account_id INT >accounts.id ?', 'owner_id INT >users.id',
        'first_name VARCHAR !', 'last_name VARCHAR !', 'title VARCHAR', 'phone VARCHAR',
        'created_at TIMESTAMP !',
      ],
      contact_emails: [
        'id INT pk', 'contact_id INT >contacts.id', 'email VARCHAR !', 'is_primary BOOLEAN !',
        'opted_out BOOLEAN !',
      ],
      lead_sources: ['id INT pk', 'name VARCHAR !', 'channel VARCHAR'],
      leads: [
        'id INT pk', 'owner_id INT >users.id', 'source_id INT >lead_sources.id ?',
        'converted_contact_id INT >contacts.id ?', 'company VARCHAR', 'first_name VARCHAR',
        'last_name VARCHAR !', 'email VARCHAR', 'status VARCHAR !', 'score INT',
        'created_at TIMESTAMP !',
      ],
      opportunity_stages: [
        'id INT pk', 'name VARCHAR !', 'position INT !', 'win_probability INT !',
        'is_closed BOOLEAN !',
      ],
      opportunities: [
        'id INT pk', 'account_id INT >accounts.id', 'primary_contact_id INT >contacts.id ?',
        'owner_id INT >users.id', 'stage_id INT >opportunity_stages.id', 'name VARCHAR !',
        'amount DECIMAL', 'currency VARCHAR !', 'expected_close DATE', 'closed_at TIMESTAMP',
      ],
      products: [
        'id INT pk', 'code VARCHAR !', 'name VARCHAR !', 'list_price DECIMAL !',
        'is_active BOOLEAN !',
      ],
      opportunity_products: [
        'id INT pk', 'opportunity_id INT >opportunities.id', 'product_id INT >products.id',
        'quantity INT !', 'unit_price DECIMAL !', 'discount_percent DECIMAL',
      ],
      quotes: [
        'id INT pk', 'opportunity_id INT >opportunities.id', 'prepared_by INT >users.id',
        'number VARCHAR !', 'status VARCHAR !', 'valid_until DATE', 'total DECIMAL !',
      ],
      quote_lines: [
        'id INT pk', 'quote_id INT >quotes.id', 'product_id INT >products.id', 'quantity INT !',
        'unit_price DECIMAL !', 'line_total DECIMAL !',
      ],
      activities: [
        'id INT pk', 'user_id INT >users.id', 'account_id INT >accounts.id ?',
        'contact_id INT >contacts.id ?', 'opportunity_id INT >opportunities.id ?',
        'type VARCHAR !', 'subject VARCHAR !', 'occurred_at TIMESTAMP !', 'duration_minutes INT',
      ],
      tasks: [
        'id INT pk', 'assigned_to INT >users.id', 'opportunity_id INT >opportunities.id ?',
        'subject VARCHAR !', 'due_at TIMESTAMP', 'priority VARCHAR !', 'completed_at TIMESTAMP',
      ],
      notes: [
        'id INT pk', 'author_id INT >users.id', 'account_id INT >accounts.id ?',
        'contact_id INT >contacts.id ?', 'body TEXT !', 'created_at TIMESTAMP !',
      ],
      campaigns: [
        'id INT pk', 'owner_id INT >users.id', 'name VARCHAR !', 'channel VARCHAR !',
        'budget DECIMAL', 'starts_on DATE', 'ends_on DATE', 'status VARCHAR !',
      ],
      campaign_members: [
        'id INT pk', 'campaign_id INT >campaigns.id', 'lead_id INT >leads.id ?',
        'contact_id INT >contacts.id ?', 'status VARCHAR !', 'responded_at TIMESTAMP',
      ],
      tickets: [
        'id INT pk', 'account_id INT >accounts.id', 'contact_id INT >contacts.id ?',
        'assigned_to INT >users.id ?', 'subject VARCHAR !', 'priority VARCHAR !',
        'status VARCHAR !', 'opened_at TIMESTAMP !', 'closed_at TIMESTAMP',
      ],
      ticket_messages: [
        'id INT pk', 'ticket_id INT >tickets.id', 'author_id INT >users.id ?', 'body TEXT !',
        'is_internal BOOLEAN !', 'sent_at TIMESTAMP !',
      ],
    },
  },

  {
    key: 'healthcare',
    label: 'Healthcare / EMR',
    description:
      'Patient records with encounters, coded diagnoses and procedures, prescriptions, labs, vitals and insurance claims.',
    size: 'standard',
    tables: {
      patients: [
        'id INT pk', 'medical_record_number VARCHAR !', 'first_name VARCHAR !',
        'last_name VARCHAR !', 'date_of_birth DATE !', 'sex_at_birth VARCHAR', 'phone VARCHAR',
        'email VARCHAR', 'created_at TIMESTAMP !',
      ],
      patient_addresses: [
        'id INT pk', 'patient_id INT >patients.id', 'line1 VARCHAR !', 'city VARCHAR !',
        'postal_code VARCHAR', 'country_code VARCHAR !', 'is_primary BOOLEAN !',
      ],
      insurers: ['id INT pk', 'name VARCHAR !', 'payer_code VARCHAR !', 'phone VARCHAR'],
      insurance_policies: [
        'id INT pk', 'patient_id INT >patients.id', 'insurer_id INT >insurers.id',
        'policy_number VARCHAR !', 'group_number VARCHAR', 'valid_from DATE !', 'valid_to DATE',
      ],
      facilities: [
        'id INT pk', 'name VARCHAR !', 'address VARCHAR', 'city VARCHAR', 'phone VARCHAR',
      ],
      departments: [
        'id INT pk', 'facility_id INT >facilities.id', 'name VARCHAR !', 'floor VARCHAR',
      ],
      specialties: ['id INT pk', 'code VARCHAR !', 'name VARCHAR !'],
      practitioners: [
        'id INT pk', 'department_id INT >departments.id ?', 'first_name VARCHAR !',
        'last_name VARCHAR !', 'license_number VARCHAR !', 'npi VARCHAR', 'email VARCHAR',
        'is_active BOOLEAN !',
      ],
      practitioner_specialties: [
        'id INT pk', 'practitioner_id INT >practitioners.id', 'specialty_id INT >specialties.id',
        'certified_on DATE',
      ],
      appointments: [
        'id INT pk', 'patient_id INT >patients.id', 'practitioner_id INT >practitioners.id',
        'facility_id INT >facilities.id', 'scheduled_at TIMESTAMP !', 'duration_minutes INT !',
        'reason VARCHAR', 'status VARCHAR !',
      ],
      encounters: [
        'id INT pk', 'appointment_id INT >appointments.id ?', 'patient_id INT >patients.id',
        'practitioner_id INT >practitioners.id', 'department_id INT >departments.id ?',
        'encounter_type VARCHAR !', 'started_at TIMESTAMP !', 'ended_at TIMESTAMP',
        'chief_complaint TEXT',
      ],
      icd_codes: ['id INT pk', 'code VARCHAR !', 'description VARCHAR !', 'version VARCHAR !'],
      diagnoses: [
        'id INT pk', 'encounter_id INT >encounters.id', 'icd_code_id INT >icd_codes.id',
        'is_primary BOOLEAN !', 'noted_at TIMESTAMP !', 'notes TEXT',
      ],
      cpt_codes: ['id INT pk', 'code VARCHAR !', 'description VARCHAR !', 'base_price DECIMAL'],
      procedures: [
        'id INT pk', 'encounter_id INT >encounters.id', 'cpt_code_id INT >cpt_codes.id',
        'performed_by INT >practitioners.id', 'performed_at TIMESTAMP !', 'outcome VARCHAR',
      ],
      medications: [
        'id INT pk', 'name VARCHAR !', 'form VARCHAR', 'strength VARCHAR', 'rxnorm_code VARCHAR',
      ],
      prescriptions: [
        'id INT pk', 'encounter_id INT >encounters.id', 'medication_id INT >medications.id',
        'prescribed_by INT >practitioners.id', 'dosage VARCHAR !', 'frequency VARCHAR !',
        'duration_days INT', 'refills INT', 'prescribed_at TIMESTAMP !',
      ],
      allergies: [
        'id INT pk', 'patient_id INT >patients.id', 'substance VARCHAR !', 'reaction VARCHAR',
        'severity VARCHAR !', 'recorded_at TIMESTAMP !',
      ],
      immunizations: [
        'id INT pk', 'patient_id INT >patients.id', 'vaccine VARCHAR !', 'dose_number INT',
        'administered_at TIMESTAMP !', 'lot_number VARCHAR',
      ],
      vitals: [
        'id INT pk', 'encounter_id INT >encounters.id', 'systolic INT', 'diastolic INT',
        'heart_rate INT', 'temperature_c DECIMAL', 'weight_kg DECIMAL', 'height_cm DECIMAL',
        'measured_at TIMESTAMP !',
      ],
      lab_orders: [
        'id INT pk', 'encounter_id INT >encounters.id', 'ordered_by INT >practitioners.id',
        'panel_name VARCHAR !', 'priority VARCHAR !', 'status VARCHAR !', 'ordered_at TIMESTAMP !',
      ],
      lab_results: [
        'id INT pk', 'lab_order_id INT >lab_orders.id', 'analyte VARCHAR !', 'value VARCHAR !',
        'unit VARCHAR', 'reference_range VARCHAR', 'is_abnormal BOOLEAN !',
        'resulted_at TIMESTAMP !',
      ],
      referrals: [
        'id INT pk', 'patient_id INT >patients.id', 'referred_by INT >practitioners.id',
        'specialty_id INT >specialties.id', 'reason TEXT', 'status VARCHAR !',
        'created_at TIMESTAMP !',
      ],
      claims: [
        'id INT pk', 'encounter_id INT >encounters.id', 'policy_id INT >insurance_policies.id',
        'claim_number VARCHAR !', 'status VARCHAR !', 'billed_amount DECIMAL !',
        'paid_amount DECIMAL', 'submitted_at TIMESTAMP !',
      ],
    },
  },

  {
    key: 'lms',
    label: 'Learning Platform',
    description:
      'Courses broken into modules and lessons, with enrolments, progress tracking, graded assignments, quizzes and certificates.',
    size: 'standard',
    tables: {
      users: [
        'id INT pk', 'email VARCHAR !', 'full_name VARCHAR !', 'password_hash VARCHAR !',
        'avatar_url VARCHAR', 'created_at TIMESTAMP !',
      ],
      instructors: [
        'id INT pk', 'user_id INT >users.id', 'headline VARCHAR', 'bio TEXT',
        'payout_account VARCHAR', 'approved_at TIMESTAMP',
      ],
      students: [
        'id INT pk', 'user_id INT >users.id', 'timezone VARCHAR', 'enrolled_since TIMESTAMP !',
      ],
      course_categories: [
        'id INT pk', 'parent_id INT >course_categories.id ?', 'name VARCHAR !', 'slug VARCHAR !',
      ],
      courses: [
        'id INT pk', 'instructor_id INT >instructors.id', 'category_id INT >course_categories.id',
        'title VARCHAR !', 'slug VARCHAR !', 'summary TEXT', 'level VARCHAR !', 'price DECIMAL !',
        'language VARCHAR !', 'status VARCHAR !', 'published_at TIMESTAMP',
      ],
      modules: [
        'id INT pk', 'course_id INT >courses.id', 'title VARCHAR !', 'position INT !',
        'is_preview BOOLEAN !',
      ],
      lessons: [
        'id INT pk', 'module_id INT >modules.id', 'title VARCHAR !', 'content TEXT',
        'video_url VARCHAR', 'duration_seconds INT', 'position INT !',
      ],
      lesson_resources: [
        'id INT pk', 'lesson_id INT >lessons.id', 'title VARCHAR !', 'file_url VARCHAR !',
        'size_bytes BIGINT',
      ],
      enrollments: [
        'id INT pk', 'student_id INT >students.id', 'course_id INT >courses.id',
        'status VARCHAR !', 'price_paid DECIMAL !', 'enrolled_at TIMESTAMP !',
        'completed_at TIMESTAMP',
      ],
      lesson_progress: [
        'id INT pk', 'enrollment_id INT >enrollments.id', 'lesson_id INT >lessons.id',
        'seconds_watched INT !', 'is_complete BOOLEAN !', 'last_viewed_at TIMESTAMP',
      ],
      assignments: [
        'id INT pk', 'module_id INT >modules.id', 'title VARCHAR !', 'instructions TEXT',
        'max_points INT !', 'due_at TIMESTAMP',
      ],
      submissions: [
        'id INT pk', 'assignment_id INT >assignments.id', 'enrollment_id INT >enrollments.id',
        'file_url VARCHAR', 'body TEXT', 'submitted_at TIMESTAMP !', 'attempt INT !',
      ],
      grades: [
        'id INT pk', 'submission_id INT >submissions.id', 'graded_by INT >instructors.id',
        'points DECIMAL !', 'feedback TEXT', 'graded_at TIMESTAMP !',
      ],
      quizzes: [
        'id INT pk', 'module_id INT >modules.id', 'title VARCHAR !', 'time_limit_minutes INT',
        'pass_percent INT !', 'max_attempts INT',
      ],
      questions: [
        'id INT pk', 'quiz_id INT >quizzes.id', 'prompt TEXT !', 'question_type VARCHAR !',
        'points INT !', 'position INT !',
      ],
      answer_options: [
        'id INT pk', 'question_id INT >questions.id', 'label VARCHAR !', 'is_correct BOOLEAN !',
        'position INT !',
      ],
      quiz_attempts: [
        'id INT pk', 'quiz_id INT >quizzes.id', 'enrollment_id INT >enrollments.id',
        'score DECIMAL', 'started_at TIMESTAMP !', 'submitted_at TIMESTAMP',
      ],
      quiz_responses: [
        'id INT pk', 'attempt_id INT >quiz_attempts.id', 'question_id INT >questions.id',
        'selected_option_id INT >answer_options.id ?', 'free_text TEXT', 'is_correct BOOLEAN',
      ],
      certificates: [
        'id INT pk', 'enrollment_id INT >enrollments.id', 'serial VARCHAR !',
        'issued_at TIMESTAMP !', 'pdf_url VARCHAR',
      ],
      discussions: [
        'id INT pk', 'course_id INT >courses.id', 'lesson_id INT >lessons.id ?',
        'title VARCHAR !', 'created_at TIMESTAMP !',
      ],
      discussion_posts: [
        'id INT pk', 'discussion_id INT >discussions.id', 'parent_id INT >discussion_posts.id ?',
        'author_id INT >users.id', 'body TEXT !', 'created_at TIMESTAMP !',
      ],
    },
  },

  {
    key: 'banking',
    label: 'Banking & Ledger',
    description:
      'Double-entry ledger behind customer accounts, cards, transfers, standing orders, loans and fraud alerts.',
    size: 'standard',
    tables: {
      branches: [
        'id INT pk', 'code VARCHAR !', 'name VARCHAR !', 'city VARCHAR', 'country_code VARCHAR !',
      ],
      employees: [
        'id INT pk', 'branch_id INT >branches.id', 'manager_id INT >employees.id ?',
        'full_name VARCHAR !', 'role VARCHAR !', 'email VARCHAR !', 'hired_on DATE !',
      ],
      currencies: ['id INT pk', 'code VARCHAR !', 'name VARCHAR !', 'minor_units INT !'],
      exchange_rates: [
        'id INT pk', 'base_currency_id INT >currencies.id', 'quote_currency_id INT >currencies.id',
        'rate DECIMAL !', 'as_of TIMESTAMP !',
      ],
      customers: [
        'id INT pk', 'branch_id INT >branches.id', 'customer_number VARCHAR !',
        'first_name VARCHAR !', 'last_name VARCHAR !', 'date_of_birth DATE !',
        'national_id VARCHAR', 'email VARCHAR', 'risk_rating VARCHAR !', 'onboarded_at TIMESTAMP !',
      ],
      customer_documents: [
        'id INT pk', 'customer_id INT >customers.id', 'document_type VARCHAR !',
        'reference VARCHAR !', 'verified_at TIMESTAMP', 'expires_on DATE',
      ],
      account_types: [
        'id INT pk', 'code VARCHAR !', 'name VARCHAR !', 'is_interest_bearing BOOLEAN !',
        'overdraft_allowed BOOLEAN !',
      ],
      accounts: [
        'id INT pk', 'account_type_id INT >account_types.id', 'branch_id INT >branches.id',
        'currency_id INT >currencies.id', 'iban VARCHAR !', 'status VARCHAR !',
        'balance DECIMAL !', 'available_balance DECIMAL !', 'opened_on DATE !', 'closed_on DATE',
      ],
      account_holders: [
        'id INT pk', 'account_id INT >accounts.id', 'customer_id INT >customers.id',
        'holder_role VARCHAR !', 'added_on DATE !',
      ],
      transaction_categories: [
        'id INT pk', 'parent_id INT >transaction_categories.id ?', 'name VARCHAR !',
        'mcc_range VARCHAR',
      ],
      transactions: [
        'id BIGINT pk', 'account_id INT >accounts.id',
        'category_id INT >transaction_categories.id ?', 'reference VARCHAR !',
        'description VARCHAR', 'amount DECIMAL !', 'direction VARCHAR !', 'status VARCHAR !',
        'booked_at TIMESTAMP !', 'value_date DATE',
      ],
      ledger_entries: [
        'id BIGINT pk', 'transaction_id BIGINT >transactions.id', 'account_id INT >accounts.id',
        'debit DECIMAL !', 'credit DECIMAL !', 'balance_after DECIMAL !', 'posted_at TIMESTAMP !',
      ],
      cards: [
        'id INT pk', 'account_id INT >accounts.id', 'customer_id INT >customers.id',
        'masked_pan VARCHAR !', 'network VARCHAR !', 'status VARCHAR !', 'expires_on DATE !',
        'daily_limit DECIMAL',
      ],
      card_transactions: [
        'id BIGINT pk', 'card_id INT >cards.id', 'transaction_id BIGINT >transactions.id ?',
        'merchant_name VARCHAR !', 'merchant_mcc VARCHAR', 'amount DECIMAL !',
        'authorised_at TIMESTAMP !', 'settled_at TIMESTAMP',
      ],
      beneficiaries: [
        'id INT pk', 'customer_id INT >customers.id', 'display_name VARCHAR !', 'iban VARCHAR !',
        'bank_name VARCHAR', 'currency_id INT >currencies.id', 'added_at TIMESTAMP !',
      ],
      transfers: [
        'id BIGINT pk', 'source_account_id INT >accounts.id',
        'beneficiary_id INT >beneficiaries.id ?', 'amount DECIMAL !', 'currency_id INT >currencies.id',
        'reference VARCHAR', 'status VARCHAR !', 'requested_at TIMESTAMP !',
        'executed_at TIMESTAMP',
      ],
      standing_orders: [
        'id INT pk', 'account_id INT >accounts.id', 'beneficiary_id INT >beneficiaries.id',
        'amount DECIMAL !', 'frequency VARCHAR !', 'next_run_on DATE !', 'ends_on DATE',
        'is_active BOOLEAN !',
      ],
      interest_rates: [
        'id INT pk', 'account_type_id INT >account_types.id', 'currency_id INT >currencies.id',
        'annual_rate DECIMAL !', 'effective_from DATE !', 'effective_to DATE',
      ],
      loans: [
        'id INT pk', 'customer_id INT >customers.id', 'account_id INT >accounts.id',
        'approved_by INT >employees.id ?', 'principal DECIMAL !', 'annual_rate DECIMAL !',
        'term_months INT !', 'status VARCHAR !', 'disbursed_on DATE', 'matures_on DATE',
      ],
      loan_payments: [
        'id BIGINT pk', 'loan_id INT >loans.id', 'due_on DATE !', 'principal_due DECIMAL !',
        'interest_due DECIMAL !', 'paid_amount DECIMAL', 'paid_at TIMESTAMP', 'status VARCHAR !',
      ],
      fraud_alerts: [
        'id BIGINT pk', 'account_id INT >accounts.id', 'transaction_id BIGINT >transactions.id ?',
        'rule_code VARCHAR !', 'severity VARCHAR !', 'status VARCHAR !',
        'raised_at TIMESTAMP !', 'reviewed_by INT >employees.id ?',
      ],
      statements: [
        'id INT pk', 'account_id INT >accounts.id', 'period_start DATE !', 'period_end DATE !',
        'opening_balance DECIMAL !', 'closing_balance DECIMAL !', 'generated_at TIMESTAMP !',
        'pdf_url VARCHAR',
      ],
    },
  },

  {
    key: 'logistics',
    label: 'Warehouse & Logistics',
    description:
      'Bin-level stock with movement history, inbound receiving, pick-and-pack fulfilment, carriers and route planning.',
    size: 'standard',
    tables: {
      warehouses: [
        'id INT pk', 'code VARCHAR !', 'name VARCHAR !', 'city VARCHAR', 'country_code VARCHAR !',
        'timezone VARCHAR',
      ],
      zones: [
        'id INT pk', 'warehouse_id INT >warehouses.id', 'code VARCHAR !', 'zone_type VARCHAR !',
        'temperature_c DECIMAL',
      ],
      bins: [
        'id INT pk', 'zone_id INT >zones.id', 'code VARCHAR !', 'aisle VARCHAR', 'rack VARCHAR',
        'level VARCHAR', 'capacity_units INT',
      ],
      products: [
        'id INT pk', 'sku VARCHAR !', 'name VARCHAR !', 'weight_grams INT', 'volume_cm3 INT',
        'is_hazardous BOOLEAN !', 'requires_cold_chain BOOLEAN !',
      ],
      stock_levels: [
        'id INT pk', 'product_id INT >products.id', 'bin_id INT >bins.id', 'quantity INT !',
        'reserved INT !', 'counted_at TIMESTAMP',
      ],
      stock_movements: [
        'id BIGINT pk', 'product_id INT >products.id', 'from_bin_id INT >bins.id ?',
        'to_bin_id INT >bins.id ?', 'quantity INT !', 'reason VARCHAR !',
        'occurred_at TIMESTAMP !',
      ],
      suppliers: [
        'id INT pk', 'name VARCHAR !', 'contact_email VARCHAR', 'lead_time_days INT',
        'country_code VARCHAR',
      ],
      purchase_orders: [
        'id INT pk', 'supplier_id INT >suppliers.id', 'warehouse_id INT >warehouses.id',
        'number VARCHAR !', 'status VARCHAR !', 'ordered_at TIMESTAMP !', 'expected_on DATE',
      ],
      purchase_order_lines: [
        'id INT pk', 'purchase_order_id INT >purchase_orders.id', 'product_id INT >products.id',
        'quantity_ordered INT !', 'quantity_received INT !', 'unit_cost DECIMAL !',
      ],
      inbound_shipments: [
        'id INT pk', 'purchase_order_id INT >purchase_orders.id ?',
        'warehouse_id INT >warehouses.id', 'reference VARCHAR !', 'status VARCHAR !',
        'arrived_at TIMESTAMP',
      ],
      inbound_lines: [
        'id INT pk', 'inbound_shipment_id INT >inbound_shipments.id',
        'product_id INT >products.id', 'bin_id INT >bins.id ?', 'quantity INT !',
        'damaged_quantity INT !',
      ],
      outbound_orders: [
        'id INT pk', 'warehouse_id INT >warehouses.id', 'reference VARCHAR !',
        'customer_name VARCHAR !', 'ship_to_city VARCHAR', 'ship_to_country VARCHAR !',
        'priority VARCHAR !', 'status VARCHAR !', 'placed_at TIMESTAMP !',
      ],
      outbound_lines: [
        'id INT pk', 'outbound_order_id INT >outbound_orders.id', 'product_id INT >products.id',
        'quantity INT !', 'picked_quantity INT !',
      ],
      pick_tasks: [
        'id INT pk', 'outbound_order_id INT >outbound_orders.id', 'bin_id INT >bins.id',
        'product_id INT >products.id', 'quantity INT !', 'status VARCHAR !',
        'assigned_at TIMESTAMP', 'completed_at TIMESTAMP',
      ],
      cartons: [
        'id INT pk', 'outbound_order_id INT >outbound_orders.id', 'code VARCHAR !',
        'weight_grams INT', 'length_cm INT', 'width_cm INT', 'height_cm INT',
      ],
      pack_tasks: [
        'id INT pk', 'carton_id INT >cartons.id', 'outbound_line_id INT >outbound_lines.id',
        'quantity INT !', 'packed_at TIMESTAMP',
      ],
      carriers: [
        'id INT pk', 'name VARCHAR !', 'scac_code VARCHAR', 'tracking_url_template VARCHAR',
      ],
      carrier_services: [
        'id INT pk', 'carrier_id INT >carriers.id', 'code VARCHAR !', 'name VARCHAR !',
        'transit_days INT', 'max_weight_grams INT',
      ],
      shipments: [
        'id INT pk', 'outbound_order_id INT >outbound_orders.id',
        'carrier_service_id INT >carrier_services.id', 'tracking_number VARCHAR',
        'status VARCHAR !', 'cost DECIMAL', 'dispatched_at TIMESTAMP',
        'delivered_at TIMESTAMP',
      ],
      shipment_events: [
        'id BIGINT pk', 'shipment_id INT >shipments.id', 'code VARCHAR !', 'description VARCHAR',
        'location VARCHAR', 'occurred_at TIMESTAMP !',
      ],
      vehicles: [
        'id INT pk', 'carrier_id INT >carriers.id ?', 'plate VARCHAR !', 'vehicle_type VARCHAR !',
        'capacity_kg INT', 'is_refrigerated BOOLEAN !',
      ],
      drivers: [
        'id INT pk', 'full_name VARCHAR !', 'licence_number VARCHAR !', 'phone VARCHAR',
        'licence_expires_on DATE',
      ],
      routes: [
        'id INT pk', 'vehicle_id INT >vehicles.id', 'driver_id INT >drivers.id',
        'warehouse_id INT >warehouses.id', 'planned_for DATE !', 'stop_count INT !',
        'distance_km DECIMAL', 'status VARCHAR !',
      ],
    },
  },

  {
    key: 'hr',
    label: 'HR & Payroll',
    description:
      'Org structure with contracts and salary components, payroll runs and payslips, time and leave, reviews and hiring.',
    size: 'standard',
    tables: {
      departments: [
        'id INT pk', 'parent_id INT >departments.id ?', 'name VARCHAR !', 'cost_centre VARCHAR',
      ],
      positions: [
        'id INT pk', 'department_id INT >departments.id', 'title VARCHAR !', 'grade VARCHAR',
        'is_management BOOLEAN !',
      ],
      employees: [
        'id INT pk', 'department_id INT >departments.id', 'position_id INT >positions.id',
        'manager_id INT >employees.id ?', 'employee_number VARCHAR !', 'first_name VARCHAR !',
        'last_name VARCHAR !', 'work_email VARCHAR !', 'date_of_birth DATE', 'hired_on DATE !',
        'terminated_on DATE', 'status VARCHAR !',
      ],
      employment_contracts: [
        'id INT pk', 'employee_id INT >employees.id', 'contract_type VARCHAR !',
        'weekly_hours DECIMAL !', 'starts_on DATE !', 'ends_on DATE', 'notice_days INT',
        'signed_at TIMESTAMP',
      ],
      salary_components: [
        'id INT pk', 'code VARCHAR !', 'name VARCHAR !', 'component_type VARCHAR !',
        'is_taxable BOOLEAN !',
      ],
      salaries: [
        'id INT pk', 'employee_id INT >employees.id', 'component_id INT >salary_components.id',
        'amount DECIMAL !', 'currency VARCHAR !', 'effective_from DATE !', 'effective_to DATE',
      ],
      payroll_runs: [
        'id INT pk', 'period_start DATE !', 'period_end DATE !', 'status VARCHAR !',
        'approved_by INT >employees.id ?', 'paid_on DATE',
      ],
      payslips: [
        'id INT pk', 'payroll_run_id INT >payroll_runs.id', 'employee_id INT >employees.id',
        'gross DECIMAL !', 'net DECIMAL !', 'tax_total DECIMAL !', 'currency VARCHAR !',
        'pdf_url VARCHAR',
      ],
      payslip_lines: [
        'id INT pk', 'payslip_id INT >payslips.id', 'component_id INT >salary_components.id',
        'amount DECIMAL !', 'quantity DECIMAL',
      ],
      time_entries: [
        'id BIGINT pk', 'employee_id INT >employees.id', 'worked_on DATE !', 'hours DECIMAL !',
        'project_code VARCHAR', 'is_billable BOOLEAN !', 'approved_by INT >employees.id ?',
      ],
      attendance: [
        'id BIGINT pk', 'employee_id INT >employees.id', 'clock_in TIMESTAMP !',
        'clock_out TIMESTAMP', 'source VARCHAR !', 'location VARCHAR',
      ],
      leave_types: [
        'id INT pk', 'code VARCHAR !', 'name VARCHAR !', 'is_paid BOOLEAN !',
        'annual_allowance_days DECIMAL',
      ],
      leave_requests: [
        'id INT pk', 'employee_id INT >employees.id', 'leave_type_id INT >leave_types.id',
        'approver_id INT >employees.id ?', 'starts_on DATE !', 'ends_on DATE !', 'days DECIMAL !',
        'status VARCHAR !', 'reason VARCHAR',
      ],
      leave_balances: [
        'id INT pk', 'employee_id INT >employees.id', 'leave_type_id INT >leave_types.id',
        'year INT !', 'entitled_days DECIMAL !', 'taken_days DECIMAL !',
        'carried_over_days DECIMAL !',
      ],
      performance_reviews: [
        'id INT pk', 'employee_id INT >employees.id', 'reviewer_id INT >employees.id',
        'period_start DATE !', 'period_end DATE !', 'overall_rating DECIMAL',
        'status VARCHAR !', 'submitted_at TIMESTAMP',
      ],
      review_goals: [
        'id INT pk', 'review_id INT >performance_reviews.id', 'title VARCHAR !',
        'description TEXT', 'weight_percent INT !', 'score DECIMAL',
      ],
      trainings: [
        'id INT pk', 'title VARCHAR !', 'provider VARCHAR', 'delivery_mode VARCHAR !',
        'duration_hours DECIMAL', 'cost DECIMAL',
      ],
      training_enrollments: [
        'id INT pk', 'training_id INT >trainings.id', 'employee_id INT >employees.id',
        'status VARCHAR !', 'enrolled_on DATE !', 'completed_on DATE', 'score DECIMAL',
      ],
      job_openings: [
        'id INT pk', 'department_id INT >departments.id', 'position_id INT >positions.id',
        'hiring_manager_id INT >employees.id', 'headcount INT !', 'status VARCHAR !',
        'opened_on DATE !', 'closed_on DATE',
      ],
      candidates: [
        'id INT pk', 'first_name VARCHAR !', 'last_name VARCHAR !', 'email VARCHAR !',
        'phone VARCHAR', 'resume_url VARCHAR', 'source VARCHAR', 'created_at TIMESTAMP !',
      ],
      applications: [
        'id INT pk', 'job_opening_id INT >job_openings.id', 'candidate_id INT >candidates.id',
        'stage VARCHAR !', 'status VARCHAR !', 'applied_at TIMESTAMP !',
        'rejection_reason VARCHAR',
      ],
      interviews: [
        'id INT pk', 'application_id INT >applications.id', 'interviewer_id INT >employees.id',
        'round INT !', 'scheduled_at TIMESTAMP !', 'recommendation VARCHAR', 'notes TEXT',
      ],
    },
  },

  {
    key: 'booking',
    label: 'Hotel & Reservations',
    description:
      'Rooms and rate plans by season, reservations with per-night pricing, housekeeping, folios and channel mappings.',
    size: 'standard',
    tables: {
      properties: [
        'id INT pk', 'name VARCHAR !', 'address VARCHAR !', 'city VARCHAR !',
        'country_code VARCHAR !', 'star_rating INT', 'check_in_time VARCHAR',
        'check_out_time VARCHAR',
      ],
      room_types: [
        'id INT pk', 'property_id INT >properties.id', 'code VARCHAR !', 'name VARCHAR !',
        'max_occupancy INT !', 'base_beds INT !', 'size_m2 DECIMAL',
      ],
      rooms: [
        'id INT pk', 'room_type_id INT >room_types.id', 'number VARCHAR !', 'floor INT',
        'status VARCHAR !', 'is_accessible BOOLEAN !',
      ],
      amenities: ['id INT pk', 'code VARCHAR !', 'name VARCHAR !', 'icon VARCHAR'],
      room_amenities: [
        'id INT pk', 'room_type_id INT >room_types.id', 'amenity_id INT >amenities.id',
      ],
      rate_plans: [
        'id INT pk', 'property_id INT >properties.id', 'name VARCHAR !',
        'includes_breakfast BOOLEAN !', 'is_refundable BOOLEAN !', 'min_nights INT !',
      ],
      rates: [
        'id INT pk', 'rate_plan_id INT >rate_plans.id', 'room_type_id INT >room_types.id',
        'stay_date DATE !', 'price DECIMAL !', 'currency VARCHAR !', 'allotment INT !',
      ],
      cancellation_policies: [
        'id INT pk', 'rate_plan_id INT >rate_plans.id', 'free_until_hours INT !',
        'penalty_percent DECIMAL !', 'description VARCHAR',
      ],
      availability_blocks: [
        'id INT pk', 'room_id INT >rooms.id', 'reason VARCHAR !', 'starts_on DATE !',
        'ends_on DATE !', 'notes VARCHAR',
      ],
      guests: [
        'id INT pk', 'first_name VARCHAR !', 'last_name VARCHAR !', 'email VARCHAR',
        'phone VARCHAR', 'nationality VARCHAR', 'loyalty_number VARCHAR',
        'created_at TIMESTAMP !',
      ],
      guest_documents: [
        'id INT pk', 'guest_id INT >guests.id', 'document_type VARCHAR !', 'number VARCHAR !',
        'issuing_country VARCHAR', 'expires_on DATE',
      ],
      channel_mappings: [
        'id INT pk', 'property_id INT >properties.id', 'room_type_id INT >room_types.id',
        'channel VARCHAR !', 'external_id VARCHAR !', 'is_active BOOLEAN !',
      ],
      reservations: [
        'id INT pk', 'property_id INT >properties.id', 'guest_id INT >guests.id',
        'rate_plan_id INT >rate_plans.id', 'confirmation_code VARCHAR !', 'status VARCHAR !',
        'check_in DATE !', 'check_out DATE !', 'adults INT !', 'children INT !',
        'total_amount DECIMAL !', 'currency VARCHAR !', 'source_channel VARCHAR',
        'booked_at TIMESTAMP !',
      ],
      reservation_rooms: [
        'id INT pk', 'reservation_id INT >reservations.id', 'room_id INT >rooms.id ?',
        'room_type_id INT >room_types.id', 'stay_date DATE !', 'nightly_rate DECIMAL !',
      ],
      reservation_guests: [
        'id INT pk', 'reservation_id INT >reservations.id', 'guest_id INT >guests.id',
        'is_primary BOOLEAN !',
      ],
      staff: [
        'id INT pk', 'property_id INT >properties.id', 'full_name VARCHAR !', 'role VARCHAR !',
        'phone VARCHAR', 'is_active BOOLEAN !',
      ],
      housekeeping_tasks: [
        'id INT pk', 'room_id INT >rooms.id', 'assigned_to INT >staff.id ?', 'task_type VARCHAR !',
        'status VARCHAR !', 'due_at TIMESTAMP', 'completed_at TIMESTAMP',
      ],
      invoices: [
        'id INT pk', 'reservation_id INT >reservations.id', 'number VARCHAR !',
        'status VARCHAR !', 'total DECIMAL !', 'tax_total DECIMAL !', 'currency VARCHAR !',
        'issued_at TIMESTAMP !',
      ],
      invoice_lines: [
        'id INT pk', 'invoice_id INT >invoices.id', 'description VARCHAR !', 'quantity INT !',
        'unit_price DECIMAL !', 'amount DECIMAL !',
      ],
      payments: [
        'id INT pk', 'invoice_id INT >invoices.id', 'method VARCHAR !', 'amount DECIMAL !',
        'currency VARCHAR !', 'status VARCHAR !', 'captured_at TIMESTAMP',
      ],
      reviews: [
        'id INT pk', 'reservation_id INT >reservations.id', 'guest_id INT >guests.id',
        'cleanliness INT', 'location INT', 'value INT', 'overall INT !', 'body TEXT',
        'created_at TIMESTAMP !',
      ],
    },
  },

  {
    key: 'social',
    label: 'Social Network',
    description:
      'Follow graph, media posts with hashtags and mentions, threaded comments, direct messages, groups and moderation.',
    size: 'standard',
    tables: {
      users: [
        'id BIGINT pk', 'username VARCHAR !', 'email VARCHAR !', 'password_hash VARCHAR !',
        'is_verified BOOLEAN !', 'created_at TIMESTAMP !',
      ],
      profiles: [
        'id BIGINT pk', 'user_id BIGINT >users.id', 'display_name VARCHAR !', 'bio TEXT',
        'avatar_url VARCHAR', 'website VARCHAR', 'location VARCHAR', 'is_private BOOLEAN !',
      ],
      follows: [
        'id BIGINT pk', 'follower_id BIGINT >users.id', 'followee_id BIGINT >users.id',
        'status VARCHAR !', 'created_at TIMESTAMP !',
      ],
      blocks: [
        'id BIGINT pk', 'blocker_id BIGINT >users.id', 'blocked_id BIGINT >users.id',
        'created_at TIMESTAMP !',
      ],
      posts: [
        'id BIGINT pk', 'author_id BIGINT >users.id', 'reply_to_id BIGINT >posts.id ?',
        'body TEXT', 'visibility VARCHAR !', 'like_count INT !', 'comment_count INT !',
        'created_at TIMESTAMP !', 'edited_at TIMESTAMP',
      ],
      post_media: [
        'id BIGINT pk', 'post_id BIGINT >posts.id', 'media_type VARCHAR !', 'url VARCHAR !',
        'width INT', 'height INT', 'position INT !',
      ],
      post_likes: [
        'id BIGINT pk', 'post_id BIGINT >posts.id', 'user_id BIGINT >users.id',
        'created_at TIMESTAMP !',
      ],
      comments: [
        'id BIGINT pk', 'post_id BIGINT >posts.id', 'author_id BIGINT >users.id',
        'parent_id BIGINT >comments.id ?', 'body TEXT !', 'like_count INT !',
        'created_at TIMESTAMP !',
      ],
      comment_likes: [
        'id BIGINT pk', 'comment_id BIGINT >comments.id', 'user_id BIGINT >users.id',
        'created_at TIMESTAMP !',
      ],
      hashtags: ['id BIGINT pk', 'tag VARCHAR !', 'post_count BIGINT !'],
      post_hashtags: [
        'id BIGINT pk', 'post_id BIGINT >posts.id', 'hashtag_id BIGINT >hashtags.id',
      ],
      mentions: [
        'id BIGINT pk', 'post_id BIGINT >posts.id ?', 'comment_id BIGINT >comments.id ?',
        'mentioned_user_id BIGINT >users.id', 'created_at TIMESTAMP !',
      ],
      stories: [
        'id BIGINT pk', 'author_id BIGINT >users.id', 'media_url VARCHAR !', 'caption VARCHAR',
        'expires_at TIMESTAMP !', 'view_count INT !', 'created_at TIMESTAMP !',
      ],
      conversations: [
        'id BIGINT pk', 'created_by BIGINT >users.id', 'is_group BOOLEAN !', 'title VARCHAR',
        'created_at TIMESTAMP !',
      ],
      conversation_participants: [
        'id BIGINT pk', 'conversation_id BIGINT >conversations.id', 'user_id BIGINT >users.id',
        'joined_at TIMESTAMP !', 'muted_until TIMESTAMP',
      ],
      messages: [
        'id BIGINT pk', 'conversation_id BIGINT >conversations.id',
        'sender_id BIGINT >users.id', 'body TEXT', 'media_url VARCHAR',
        'sent_at TIMESTAMP !', 'deleted_at TIMESTAMP',
      ],
      message_reads: [
        'id BIGINT pk', 'message_id BIGINT >messages.id', 'user_id BIGINT >users.id',
        'read_at TIMESTAMP !',
      ],
      groups: [
        'id BIGINT pk', 'owner_id BIGINT >users.id', 'name VARCHAR !', 'slug VARCHAR !',
        'description TEXT', 'privacy VARCHAR !', 'member_count INT !', 'created_at TIMESTAMP !',
      ],
      group_members: [
        'id BIGINT pk', 'group_id BIGINT >groups.id', 'user_id BIGINT >users.id',
        'role VARCHAR !', 'joined_at TIMESTAMP !',
      ],
      group_posts: [
        'id BIGINT pk', 'group_id BIGINT >groups.id', 'post_id BIGINT >posts.id',
        'pinned_at TIMESTAMP',
      ],
      notifications: [
        'id BIGINT pk', 'user_id BIGINT >users.id', 'actor_id BIGINT >users.id ?',
        'type VARCHAR !', 'entity_type VARCHAR', 'entity_id BIGINT', 'read_at TIMESTAMP',
        'created_at TIMESTAMP !',
      ],
      reports: [
        'id BIGINT pk', 'reporter_id BIGINT >users.id', 'post_id BIGINT >posts.id ?',
        'comment_id BIGINT >comments.id ?', 'reported_user_id BIGINT >users.id ?',
        'reason VARCHAR !', 'status VARCHAR !', 'created_at TIMESTAMP !',
      ],
    },
  },

  {
    key: 'helpdesk',
    label: 'Helpdesk & SLA',
    description:
      'Ticketing with routing rules, SLA targets and escalations, canned macros, a knowledge base and CSAT scoring.',
    size: 'standard',
    tables: {
      organizations: [
        'id UUID pk', 'name VARCHAR !', 'domain VARCHAR', 'plan VARCHAR !',
        'created_at TIMESTAMP !',
      ],
      users: [
        'id UUID pk', 'organization_id UUID >organizations.id', 'email VARCHAR !',
        'full_name VARCHAR !', 'is_active BOOLEAN !', 'created_at TIMESTAMP !',
      ],
      agents: [
        'id UUID pk', 'user_id UUID >users.id', 'signature TEXT', 'max_open_tickets INT',
        'is_available BOOLEAN !',
      ],
      teams: [
        'id UUID pk', 'organization_id UUID >organizations.id', 'name VARCHAR !',
        'inbox_email VARCHAR',
      ],
      team_agents: [
        'id UUID pk', 'team_id UUID >teams.id', 'agent_id UUID >agents.id', 'role VARCHAR !',
      ],
      customers: [
        'id UUID pk', 'organization_id UUID >organizations.id', 'email VARCHAR !',
        'full_name VARCHAR', 'phone VARCHAR', 'external_id VARCHAR', 'created_at TIMESTAMP !',
      ],
      priorities: ['id UUID pk', 'code VARCHAR !', 'name VARCHAR !', 'weight INT !'],
      statuses: [
        'id UUID pk', 'code VARCHAR !', 'name VARCHAR !', 'is_terminal BOOLEAN !',
        'position INT !',
      ],
      categories: [
        'id UUID pk', 'organization_id UUID >organizations.id',
        'parent_id UUID >categories.id ?', 'name VARCHAR !',
      ],
      sla_policies: [
        'id UUID pk', 'organization_id UUID >organizations.id', 'name VARCHAR !',
        'business_hours_only BOOLEAN !', 'is_default BOOLEAN !',
      ],
      sla_targets: [
        'id UUID pk', 'sla_policy_id UUID >sla_policies.id', 'priority_id UUID >priorities.id',
        'first_response_minutes INT !', 'resolution_minutes INT !',
      ],
      tickets: [
        'id UUID pk', 'organization_id UUID >organizations.id',
        'customer_id UUID >customers.id', 'assigned_agent_id UUID >agents.id ?',
        'team_id UUID >teams.id ?', 'category_id UUID >categories.id ?',
        'priority_id UUID >priorities.id', 'status_id UUID >statuses.id',
        'sla_policy_id UUID >sla_policies.id ?', 'number VARCHAR !', 'subject VARCHAR !',
        'channel VARCHAR !', 'opened_at TIMESTAMP !', 'first_responded_at TIMESTAMP',
        'resolved_at TIMESTAMP', 'due_at TIMESTAMP',
      ],
      ticket_messages: [
        'id UUID pk', 'ticket_id UUID >tickets.id', 'agent_id UUID >agents.id ?',
        'customer_id UUID >customers.id ?', 'body TEXT !', 'is_public BOOLEAN !',
        'sent_at TIMESTAMP !',
      ],
      ticket_attachments: [
        'id UUID pk', 'message_id UUID >ticket_messages.id', 'file_name VARCHAR !',
        'mime_type VARCHAR !', 'size_bytes BIGINT !', 'url VARCHAR !',
      ],
      tags: [
        'id UUID pk', 'organization_id UUID >organizations.id', 'name VARCHAR !',
        'colour VARCHAR',
      ],
      ticket_tags: ['id UUID pk', 'ticket_id UUID >tickets.id', 'tag_id UUID >tags.id'],
      escalations: [
        'id UUID pk', 'ticket_id UUID >tickets.id', 'escalated_to UUID >agents.id ?',
        'reason VARCHAR !', 'level INT !', 'escalated_at TIMESTAMP !',
        'acknowledged_at TIMESTAMP',
      ],
      macros: [
        'id UUID pk', 'organization_id UUID >organizations.id', 'created_by UUID >agents.id',
        'name VARCHAR !', 'body TEXT !', 'usage_count INT !',
      ],
      knowledge_articles: [
        'id UUID pk', 'organization_id UUID >organizations.id',
        'category_id UUID >categories.id ?', 'author_id UUID >agents.id', 'title VARCHAR !',
        'slug VARCHAR !', 'body TEXT', 'status VARCHAR !', 'view_count INT !',
        'published_at TIMESTAMP',
      ],
      article_feedback: [
        'id UUID pk', 'article_id UUID >knowledge_articles.id',
        'customer_id UUID >customers.id ?', 'was_helpful BOOLEAN !', 'comment TEXT',
        'created_at TIMESTAMP !',
      ],
      satisfaction_ratings: [
        'id UUID pk', 'ticket_id UUID >tickets.id', 'customer_id UUID >customers.id',
        'score INT !', 'comment TEXT', 'rated_at TIMESTAMP !',
      ],
    },
  },

  // ── Hızlı başlangıç (mini) ─────────────────────────────────────────────
  //
  // Bunlar "ürün ne kadar büyük şey kaldırıyor" göstermek için değil; bir şeyi
  // BUGÜN kurup üstüne inşa etmek için. 6 tabloluk bir URL kısaltıcıya 25
  // tablo dayatmak, kullanıcıya silecek 19 tablo vermek olurdu.

  {
    key: 'auth',
    label: 'Auth & Roles',
    description: 'Sign-in, sessions, role-based permissions and password resets — the part every app rewrites.',
    size: 'mini',
    tables: {
      users: [
        'id UUID pk', 'email VARCHAR !', 'password_hash VARCHAR !', 'full_name VARCHAR',
        'email_verified_at TIMESTAMP', 'is_active BOOLEAN !', 'created_at TIMESTAMP !',
      ],
      sessions: [
        'id UUID pk', 'user_id UUID >users.id', 'token_hash VARCHAR !', 'ip_address VARCHAR',
        'user_agent VARCHAR', 'expires_at TIMESTAMP !', 'revoked_at TIMESTAMP',
      ],
      roles: ['id UUID pk', 'name VARCHAR !', 'description VARCHAR', 'is_system BOOLEAN !'],
      permissions: ['id UUID pk', 'code VARCHAR !', 'description VARCHAR'],
      role_permissions: [
        'id UUID pk', 'role_id UUID >roles.id', 'permission_id UUID >permissions.id',
      ],
      user_roles: [
        'id UUID pk', 'user_id UUID >users.id', 'role_id UUID >roles.id',
        'granted_at TIMESTAMP !',
      ],
      password_resets: [
        'id UUID pk', 'user_id UUID >users.id', 'token_hash VARCHAR !',
        'expires_at TIMESTAMP !', 'used_at TIMESTAMP',
      ],
    },
  },

  {
    key: 'tasks',
    label: 'Tasks & Projects',
    description: 'Projects, assignable tasks with labels and threaded comments. A to-do list that survives a team.',
    size: 'mini',
    tables: {
      users: ['id INT pk', 'email VARCHAR !', 'full_name VARCHAR !', 'avatar_url VARCHAR'],
      projects: [
        'id INT pk', 'owner_id INT >users.id', 'name VARCHAR !', 'key VARCHAR !',
        'archived_at TIMESTAMP', 'created_at TIMESTAMP !',
      ],
      labels: ['id INT pk', 'project_id INT >projects.id', 'name VARCHAR !', 'colour VARCHAR'],
      tasks: [
        'id INT pk', 'project_id INT >projects.id', 'assignee_id INT >users.id ?',
        'parent_id INT >tasks.id ?', 'title VARCHAR !', 'description TEXT', 'status VARCHAR !',
        'priority VARCHAR !', 'due_on DATE', 'position INT !', 'created_at TIMESTAMP !',
      ],
      task_labels: ['id INT pk', 'task_id INT >tasks.id', 'label_id INT >labels.id'],
      comments: [
        'id INT pk', 'task_id INT >tasks.id', 'author_id INT >users.id', 'body TEXT !',
        'created_at TIMESTAMP !',
      ],
    },
  },

  {
    key: 'links',
    label: 'Link Shortener',
    description: 'Short links on custom domains, with per-click analytics and tagging.',
    size: 'mini',
    tables: {
      users: ['id INT pk', 'email VARCHAR !', 'plan VARCHAR !', 'created_at TIMESTAMP !'],
      domains: [
        'id INT pk', 'user_id INT >users.id', 'hostname VARCHAR !', 'verified_at TIMESTAMP',
        'is_default BOOLEAN !',
      ],
      links: [
        'id INT pk', 'user_id INT >users.id', 'domain_id INT >domains.id ?', 'slug VARCHAR !',
        'target_url TEXT !', 'title VARCHAR', 'expires_at TIMESTAMP', 'click_count INT !',
        'created_at TIMESTAMP !',
      ],
      link_clicks: [
        'id BIGINT pk', 'link_id INT >links.id', 'referrer VARCHAR', 'country_code VARCHAR',
        'device_type VARCHAR', 'user_agent VARCHAR', 'clicked_at TIMESTAMP !',
      ],
      tags: ['id INT pk', 'user_id INT >users.id', 'name VARCHAR !'],
      link_tags: ['id INT pk', 'link_id INT >links.id', 'tag_id INT >tags.id'],
    },
  },

  {
    key: 'newsletter',
    label: 'Newsletter',
    description: 'Subscriber lists with double opt-in, campaigns built from templates, and per-send delivery state.',
    size: 'mini',
    tables: {
      subscribers: [
        'id INT pk', 'email VARCHAR !', 'full_name VARCHAR', 'status VARCHAR !',
        'confirmed_at TIMESTAMP', 'unsubscribed_at TIMESTAMP', 'created_at TIMESTAMP !',
      ],
      lists: [
        'id INT pk', 'name VARCHAR !', 'description VARCHAR', 'double_opt_in BOOLEAN !',
      ],
      list_subscribers: [
        'id INT pk', 'list_id INT >lists.id', 'subscriber_id INT >subscribers.id',
        'subscribed_at TIMESTAMP !',
      ],
      templates: [
        'id INT pk', 'name VARCHAR !', 'subject VARCHAR !', 'html_body TEXT',
        'updated_at TIMESTAMP',
      ],
      campaigns: [
        'id INT pk', 'list_id INT >lists.id', 'template_id INT >templates.id ?',
        'name VARCHAR !', 'subject VARCHAR !', 'status VARCHAR !', 'scheduled_at TIMESTAMP',
        'sent_at TIMESTAMP',
      ],
      campaign_sends: [
        'id BIGINT pk', 'campaign_id INT >campaigns.id', 'subscriber_id INT >subscribers.id',
        'status VARCHAR !', 'opened_at TIMESTAMP', 'clicked_at TIMESTAMP',
        'bounced_at TIMESTAMP',
      ],
    },
  },

  {
    key: 'feedback',
    label: 'Feedback Board',
    description: 'Public roadmap: users post ideas, vote, and comment while the team moves them across statuses.',
    size: 'mini',
    tables: {
      users: ['id INT pk', 'email VARCHAR !', 'display_name VARCHAR !', 'is_staff BOOLEAN !'],
      boards: ['id INT pk', 'name VARCHAR !', 'slug VARCHAR !', 'is_public BOOLEAN !'],
      statuses: [
        'id INT pk', 'board_id INT >boards.id', 'name VARCHAR !', 'colour VARCHAR',
        'position INT !', 'is_closed BOOLEAN !',
      ],
      tags: ['id INT pk', 'board_id INT >boards.id', 'name VARCHAR !'],
      posts: [
        'id INT pk', 'board_id INT >boards.id', 'author_id INT >users.id',
        'status_id INT >statuses.id ?', 'tag_id INT >tags.id ?', 'title VARCHAR !',
        'body TEXT', 'vote_count INT !', 'created_at TIMESTAMP !',
      ],
      post_votes: [
        'id INT pk', 'post_id INT >posts.id', 'user_id INT >users.id',
        'created_at TIMESTAMP !',
      ],
      post_comments: [
        'id INT pk', 'post_id INT >posts.id', 'author_id INT >users.id',
        'parent_id INT >post_comments.id ?', 'body TEXT !', 'is_staff_reply BOOLEAN !',
        'created_at TIMESTAMP !',
      ],
    },
  },

  {
    key: 'bookmarks',
    label: 'Bookmarks & Notes',
    description: 'Saved pages in collections, tagged and highlighted — a read-it-later backend.',
    size: 'mini',
    tables: {
      users: ['id INT pk', 'email VARCHAR !', 'display_name VARCHAR', 'created_at TIMESTAMP !'],
      collections: [
        'id INT pk', 'user_id INT >users.id', 'parent_id INT >collections.id ?',
        'name VARCHAR !', 'is_private BOOLEAN !',
      ],
      bookmarks: [
        'id INT pk', 'user_id INT >users.id', 'collection_id INT >collections.id ?',
        'url TEXT !', 'title VARCHAR', 'excerpt TEXT', 'favicon_url VARCHAR',
        'is_archived BOOLEAN !', 'saved_at TIMESTAMP !',
      ],
      tags: ['id INT pk', 'user_id INT >users.id', 'name VARCHAR !'],
      bookmark_tags: [
        'id INT pk', 'bookmark_id INT >bookmarks.id', 'tag_id INT >tags.id',
      ],
      highlights: [
        'id INT pk', 'bookmark_id INT >bookmarks.id', 'quote TEXT !', 'note TEXT',
        'colour VARCHAR', 'created_at TIMESTAMP !',
      ],
    },
  },

  // ── Kurumsal ölçek (large) ─────────────────────────────────────────────
  //
  // 25 tabloda kural motorunun söyledikleri hâlâ gözle taranabilir. 40 tabloda
  // taranamaz — ve ürünün asıl işe yaradığı yer tam olarak orası. Bu iki
  // şablon "büyük olsun diye" büyük değil; ikisi de gerçek sistemlerde
  // ayrılmak ZORUNDA olan kalemleri ayırıyor.

  {
    key: 'marketplace',
    label: 'Multi-vendor Marketplace',
    description:
      'Many sellers on one storefront: per-vendor listings and stock, split payments, commissions, payouts and disputes.',
    size: 'large',
    tables: {
      users: [
        'id INT pk', 'email VARCHAR !', 'password_hash VARCHAR !', 'full_name VARCHAR',
        'phone VARCHAR', 'is_active BOOLEAN !', 'created_at TIMESTAMP !',
      ],
      addresses: [
        'id INT pk', 'user_id INT >users.id', 'line1 VARCHAR !', 'line2 VARCHAR',
        'city VARCHAR !', 'postal_code VARCHAR', 'country_code VARCHAR !',
        'is_default BOOLEAN !',
      ],
      vendors: [
        'id INT pk', 'legal_name VARCHAR !', 'display_name VARCHAR !', 'slug VARCHAR !',
        'tax_id VARCHAR', 'country_code VARCHAR !', 'status VARCHAR !',
        'commission_percent DECIMAL !', 'onboarded_at TIMESTAMP !',
      ],
      vendor_users: [
        'id INT pk', 'vendor_id INT >vendors.id', 'user_id INT >users.id', 'role VARCHAR !',
        'invited_at TIMESTAMP', 'accepted_at TIMESTAMP',
      ],
      vendor_documents: [
        'id INT pk', 'vendor_id INT >vendors.id', 'document_type VARCHAR !',
        'file_url VARCHAR !', 'verified_at TIMESTAMP', 'expires_on DATE',
      ],
      categories: [
        'id INT pk', 'parent_id INT >categories.id ?', 'name VARCHAR !', 'slug VARCHAR !',
        'position INT !',
      ],
      attributes: [
        'id INT pk', 'category_id INT >categories.id ?', 'code VARCHAR !', 'name VARCHAR !',
        'data_type VARCHAR !', 'is_variant_axis BOOLEAN !',
      ],
      attribute_values: [
        'id INT pk', 'attribute_id INT >attributes.id', 'value VARCHAR !', 'position INT !',
      ],
      products: [
        'id INT pk', 'vendor_id INT >vendors.id', 'category_id INT >categories.id',
        'name VARCHAR !', 'slug VARCHAR !', 'description TEXT', 'brand VARCHAR',
        'status VARCHAR !', 'created_at TIMESTAMP !',
      ],
      product_attributes: [
        'id INT pk', 'product_id INT >products.id', 'attribute_id INT >attributes.id',
        'attribute_value_id INT >attribute_values.id ?', 'free_value VARCHAR',
      ],
      variants: [
        'id INT pk', 'product_id INT >products.id', 'sku VARCHAR !', 'title VARCHAR !',
        'weight_grams INT', 'barcode VARCHAR', 'is_active BOOLEAN !',
      ],
      variant_attribute_values: [
        'id INT pk', 'variant_id INT >variants.id',
        'attribute_value_id INT >attribute_values.id',
      ],
      media: [
        'id INT pk', 'product_id INT >products.id', 'variant_id INT >variants.id ?',
        'url VARCHAR !', 'media_type VARCHAR !', 'alt_text VARCHAR', 'position INT !',
      ],
      price_lists: [
        'id INT pk', 'vendor_id INT >vendors.id', 'name VARCHAR !', 'currency VARCHAR !',
        'starts_on DATE', 'ends_on DATE',
      ],
      prices: [
        'id INT pk', 'price_list_id INT >price_lists.id', 'variant_id INT >variants.id',
        'amount DECIMAL !', 'compare_at DECIMAL', 'min_quantity INT !',
      ],
      inventory_locations: [
        'id INT pk', 'vendor_id INT >vendors.id', 'name VARCHAR !', 'city VARCHAR',
        'country_code VARCHAR !', 'is_default BOOLEAN !',
      ],
      inventory: [
        'id INT pk', 'variant_id INT >variants.id', 'location_id INT >inventory_locations.id',
        'on_hand INT !', 'reserved INT !', 'restock_eta DATE',
      ],
      listings: [
        'id INT pk', 'variant_id INT >variants.id', 'vendor_id INT >vendors.id',
        'price_list_id INT >price_lists.id ?', 'condition VARCHAR !', 'handling_days INT !',
        'is_buy_box BOOLEAN !', 'published_at TIMESTAMP',
      ],
      carts: [
        'id INT pk', 'user_id INT >users.id ?', 'session_token VARCHAR', 'currency VARCHAR !',
        'created_at TIMESTAMP !',
      ],
      cart_items: [
        'id INT pk', 'cart_id INT >carts.id', 'listing_id INT >listings.id', 'quantity INT !',
        'unit_price DECIMAL !',
      ],
      shipping_methods: [
        'id INT pk', 'vendor_id INT >vendors.id', 'name VARCHAR !', 'carrier VARCHAR',
        'transit_days INT', 'is_active BOOLEAN !',
      ],
      shipping_rates: [
        'id INT pk', 'shipping_method_id INT >shipping_methods.id', 'country_code VARCHAR !',
        'min_weight_grams INT !', 'max_weight_grams INT', 'amount DECIMAL !',
      ],
      tax_rates: [
        'id INT pk', 'country_code VARCHAR !', 'region VARCHAR', 'name VARCHAR !',
        'percent DECIMAL !', 'applies_to VARCHAR !',
      ],
      orders: [
        'id INT pk', 'buyer_id INT >users.id', 'shipping_address_id INT >addresses.id ?',
        'billing_address_id INT >addresses.id ?', 'number VARCHAR !', 'status VARCHAR !',
        'currency VARCHAR !', 'items_total DECIMAL !', 'shipping_total DECIMAL !',
        'tax_total DECIMAL !', 'grand_total DECIMAL !', 'placed_at TIMESTAMP !',
      ],
      order_lines: [
        'id INT pk', 'order_id INT >orders.id', 'listing_id INT >listings.id',
        'vendor_id INT >vendors.id', 'quantity INT !', 'unit_price DECIMAL !',
        'line_total DECIMAL !', 'status VARCHAR !',
      ],
      tax_lines: [
        'id INT pk', 'order_line_id INT >order_lines.id', 'tax_rate_id INT >tax_rates.id',
        'amount DECIMAL !',
      ],
      fulfilments: [
        'id INT pk', 'order_id INT >orders.id', 'vendor_id INT >vendors.id',
        'location_id INT >inventory_locations.id ?',
        'shipping_method_id INT >shipping_methods.id ?', 'tracking_number VARCHAR',
        'status VARCHAR !', 'shipped_at TIMESTAMP', 'delivered_at TIMESTAMP',
      ],
      order_line_fulfilments: [
        'id INT pk', 'fulfilment_id INT >fulfilments.id', 'order_line_id INT >order_lines.id',
        'quantity INT !',
      ],
      payments: [
        'id INT pk', 'order_id INT >orders.id', 'provider VARCHAR !',
        'provider_reference VARCHAR', 'method VARCHAR !', 'status VARCHAR !',
        'amount DECIMAL !', 'currency VARCHAR !', 'captured_at TIMESTAMP',
      ],
      payment_splits: [
        'id INT pk', 'payment_id INT >payments.id', 'vendor_id INT >vendors.id',
        'gross_amount DECIMAL !', 'commission_amount DECIMAL !', 'net_amount DECIMAL !',
      ],
      commissions: [
        'id INT pk', 'order_line_id INT >order_lines.id', 'vendor_id INT >vendors.id',
        'percent DECIMAL !', 'amount DECIMAL !', 'settled_at TIMESTAMP',
      ],
      refunds: [
        'id INT pk', 'payment_id INT >payments.id', 'order_line_id INT >order_lines.id ?',
        'amount DECIMAL !', 'reason VARCHAR', 'status VARCHAR !', 'created_at TIMESTAMP !',
      ],
      disputes: [
        'id INT pk', 'order_id INT >orders.id', 'opened_by INT >users.id',
        'vendor_id INT >vendors.id', 'reason VARCHAR !', 'status VARCHAR !',
        'resolution VARCHAR', 'opened_at TIMESTAMP !', 'closed_at TIMESTAMP',
      ],
      payouts: [
        'id INT pk', 'vendor_id INT >vendors.id', 'period_start DATE !', 'period_end DATE !',
        'gross_amount DECIMAL !', 'commission_amount DECIMAL !', 'net_amount DECIMAL !',
        'status VARCHAR !', 'paid_at TIMESTAMP',
      ],
      payout_lines: [
        'id INT pk', 'payout_id INT >payouts.id', 'order_line_id INT >order_lines.id',
        'amount DECIMAL !',
      ],
      reviews: [
        'id INT pk', 'product_id INT >products.id', 'author_id INT >users.id',
        'order_line_id INT >order_lines.id ?', 'rating INT !', 'title VARCHAR', 'body TEXT',
        'is_verified BOOLEAN !', 'created_at TIMESTAMP !',
      ],
      review_replies: [
        'id INT pk', 'review_id INT >reviews.id', 'vendor_id INT >vendors.id', 'body TEXT !',
        'created_at TIMESTAMP !',
      ],
      message_threads: [
        'id INT pk', 'buyer_id INT >users.id', 'vendor_id INT >vendors.id',
        'order_id INT >orders.id ?', 'subject VARCHAR', 'status VARCHAR !',
        'created_at TIMESTAMP !',
      ],
      messages: [
        'id INT pk', 'thread_id INT >message_threads.id', 'sender_user_id INT >users.id ?',
        'body TEXT !', 'is_from_vendor BOOLEAN !', 'sent_at TIMESTAMP !',
      ],
    },
  },

  {
    key: 'erp',
    label: 'Manufacturing ERP',
    description:
      'Bills of materials and routings driving production, purchasing and sales, all posting to a double-entry general ledger.',
    size: 'large',
    tables: {
      companies: [
        'id INT pk', 'legal_name VARCHAR !', 'tax_id VARCHAR', 'country_code VARCHAR !',
        'base_currency VARCHAR !',
      ],
      sites: [
        'id INT pk', 'company_id INT >companies.id', 'code VARCHAR !', 'name VARCHAR !',
        'city VARCHAR', 'country_code VARCHAR !',
      ],
      warehouses: [
        'id INT pk', 'site_id INT >sites.id', 'code VARCHAR !', 'name VARCHAR !',
        'is_default BOOLEAN !',
      ],
      cost_centres: [
        'id INT pk', 'company_id INT >companies.id', 'parent_id INT >cost_centres.id ?',
        'code VARCHAR !', 'name VARCHAR !',
      ],
      currencies: ['id INT pk', 'code VARCHAR !', 'name VARCHAR !', 'minor_units INT !'],
      exchange_rates: [
        'id INT pk', 'base_currency_id INT >currencies.id',
        'quote_currency_id INT >currencies.id', 'rate DECIMAL !', 'as_of DATE !',
      ],
      fiscal_periods: [
        'id INT pk', 'company_id INT >companies.id', 'name VARCHAR !', 'starts_on DATE !',
        'ends_on DATE !', 'is_closed BOOLEAN !',
      ],
      gl_accounts: [
        'id INT pk', 'company_id INT >companies.id', 'parent_id INT >gl_accounts.id ?',
        'code VARCHAR !', 'name VARCHAR !', 'account_type VARCHAR !', 'is_postable BOOLEAN !',
      ],
      journals: [
        'id INT pk', 'company_id INT >companies.id', 'fiscal_period_id INT >fiscal_periods.id',
        'reference VARCHAR !', 'source VARCHAR !', 'posted_at TIMESTAMP', 'status VARCHAR !',
      ],
      journal_lines: [
        'id BIGINT pk', 'journal_id INT >journals.id', 'gl_account_id INT >gl_accounts.id',
        'cost_centre_id INT >cost_centres.id ?', 'debit DECIMAL !', 'credit DECIMAL !',
        'currency_id INT >currencies.id', 'description VARCHAR',
      ],
      partners: [
        'id INT pk', 'company_id INT >companies.id', 'code VARCHAR !', 'name VARCHAR !',
        'is_customer BOOLEAN !', 'is_supplier BOOLEAN !', 'tax_id VARCHAR',
        'payment_terms_days INT',
      ],
      partner_addresses: [
        'id INT pk', 'partner_id INT >partners.id', 'address_type VARCHAR !',
        'line1 VARCHAR !', 'city VARCHAR !', 'postal_code VARCHAR',
        'country_code VARCHAR !',
      ],
      contacts: [
        'id INT pk', 'partner_id INT >partners.id', 'full_name VARCHAR !', 'email VARCHAR',
        'phone VARCHAR', 'job_title VARCHAR',
      ],
      units_of_measure: [
        'id INT pk', 'code VARCHAR !', 'name VARCHAR !', 'category VARCHAR !',
        'ratio_to_base DECIMAL !',
      ],
      item_categories: [
        'id INT pk', 'parent_id INT >item_categories.id ?', 'code VARCHAR !', 'name VARCHAR !',
      ],
      items: [
        'id INT pk', 'category_id INT >item_categories.id', 'uom_id INT >units_of_measure.id',
        'code VARCHAR !', 'name VARCHAR !', 'item_type VARCHAR !', 'standard_cost DECIMAL',
        'lead_time_days INT', 'is_active BOOLEAN !',
      ],
      boms: [
        'id INT pk', 'item_id INT >items.id', 'version VARCHAR !', 'quantity DECIMAL !',
        'is_active BOOLEAN !', 'effective_from DATE',
      ],
      bom_lines: [
        'id INT pk', 'bom_id INT >boms.id', 'component_item_id INT >items.id',
        'quantity DECIMAL !', 'scrap_percent DECIMAL', 'position INT !',
      ],
      work_centres: [
        'id INT pk', 'site_id INT >sites.id', 'cost_centre_id INT >cost_centres.id ?',
        'code VARCHAR !', 'name VARCHAR !', 'capacity_hours_per_day DECIMAL',
        'hourly_rate DECIMAL',
      ],
      routings: [
        'id INT pk', 'item_id INT >items.id', 'version VARCHAR !', 'is_active BOOLEAN !',
      ],
      routing_operations: [
        'id INT pk', 'routing_id INT >routings.id', 'work_centre_id INT >work_centres.id',
        'sequence INT !', 'name VARCHAR !', 'setup_minutes DECIMAL', 'run_minutes DECIMAL !',
      ],
      production_orders: [
        'id INT pk', 'item_id INT >items.id', 'bom_id INT >boms.id ?',
        'routing_id INT >routings.id ?', 'warehouse_id INT >warehouses.id',
        'number VARCHAR !', 'quantity DECIMAL !', 'status VARCHAR !', 'due_on DATE',
        'released_at TIMESTAMP', 'closed_at TIMESTAMP',
      ],
      production_order_lines: [
        'id INT pk', 'production_order_id INT >production_orders.id',
        'component_item_id INT >items.id', 'required_quantity DECIMAL !',
        'issued_quantity DECIMAL !',
      ],
      work_orders: [
        'id INT pk', 'production_order_id INT >production_orders.id',
        'routing_operation_id INT >routing_operations.id ?',
        'work_centre_id INT >work_centres.id', 'sequence INT !', 'status VARCHAR !',
        'planned_minutes DECIMAL', 'actual_minutes DECIMAL', 'completed_at TIMESTAMP',
      ],
      purchase_requisitions: [
        'id INT pk', 'site_id INT >sites.id', 'item_id INT >items.id',
        'requested_by VARCHAR', 'quantity DECIMAL !', 'needed_on DATE', 'status VARCHAR !',
        'created_at TIMESTAMP !',
      ],
      purchase_orders: [
        'id INT pk', 'partner_id INT >partners.id', 'warehouse_id INT >warehouses.id',
        'currency_id INT >currencies.id', 'number VARCHAR !', 'status VARCHAR !',
        'ordered_at TIMESTAMP !', 'expected_on DATE', 'total DECIMAL !',
      ],
      purchase_order_lines: [
        'id INT pk', 'purchase_order_id INT >purchase_orders.id', 'item_id INT >items.id',
        'requisition_id INT >purchase_requisitions.id ?', 'quantity DECIMAL !',
        'unit_price DECIMAL !', 'received_quantity DECIMAL !',
      ],
      goods_receipts: [
        'id INT pk', 'purchase_order_id INT >purchase_orders.id',
        'warehouse_id INT >warehouses.id', 'reference VARCHAR !', 'received_at TIMESTAMP !',
        'status VARCHAR !',
      ],
      goods_receipt_lines: [
        'id INT pk', 'goods_receipt_id INT >goods_receipts.id',
        'purchase_order_line_id INT >purchase_order_lines.id', 'quantity DECIMAL !',
        'rejected_quantity DECIMAL !',
      ],
      supplier_invoices: [
        'id INT pk', 'partner_id INT >partners.id',
        'purchase_order_id INT >purchase_orders.id ?', 'journal_id INT >journals.id ?',
        'number VARCHAR !', 'status VARCHAR !', 'issued_on DATE !', 'due_on DATE',
        'total DECIMAL !',
      ],
      supplier_invoice_lines: [
        'id INT pk', 'supplier_invoice_id INT >supplier_invoices.id',
        'item_id INT >items.id ?', 'gl_account_id INT >gl_accounts.id',
        'description VARCHAR', 'quantity DECIMAL !', 'unit_price DECIMAL !',
        'amount DECIMAL !',
      ],
      sales_orders: [
        'id INT pk', 'partner_id INT >partners.id', 'warehouse_id INT >warehouses.id',
        'currency_id INT >currencies.id', 'number VARCHAR !', 'status VARCHAR !',
        'ordered_at TIMESTAMP !', 'promised_on DATE', 'total DECIMAL !',
      ],
      sales_order_lines: [
        'id INT pk', 'sales_order_id INT >sales_orders.id', 'item_id INT >items.id',
        'quantity DECIMAL !', 'unit_price DECIMAL !', 'delivered_quantity DECIMAL !',
      ],
      deliveries: [
        'id INT pk', 'sales_order_id INT >sales_orders.id',
        'warehouse_id INT >warehouses.id', 'reference VARCHAR !', 'status VARCHAR !',
        'shipped_at TIMESTAMP', 'delivered_at TIMESTAMP',
      ],
      delivery_lines: [
        'id INT pk', 'delivery_id INT >deliveries.id',
        'sales_order_line_id INT >sales_order_lines.id', 'quantity DECIMAL !',
      ],
      customer_invoices: [
        'id INT pk', 'partner_id INT >partners.id', 'sales_order_id INT >sales_orders.id ?',
        'journal_id INT >journals.id ?', 'number VARCHAR !', 'status VARCHAR !',
        'issued_on DATE !', 'due_on DATE', 'total DECIMAL !',
      ],
      customer_invoice_lines: [
        'id INT pk', 'customer_invoice_id INT >customer_invoices.id',
        'item_id INT >items.id ?', 'gl_account_id INT >gl_accounts.id',
        'description VARCHAR', 'quantity DECIMAL !', 'unit_price DECIMAL !',
        'amount DECIMAL !',
      ],
      payments: [
        'id INT pk', 'partner_id INT >partners.id', 'journal_id INT >journals.id ?',
        'direction VARCHAR !', 'method VARCHAR !', 'amount DECIMAL !',
        'currency_id INT >currencies.id', 'paid_on DATE !', 'reference VARCHAR',
      ],
      stock_moves: [
        'id BIGINT pk', 'item_id INT >items.id', 'from_warehouse_id INT >warehouses.id ?',
        'to_warehouse_id INT >warehouses.id ?',
        'production_order_id INT >production_orders.id ?',
        'goods_receipt_id INT >goods_receipts.id ?', 'delivery_id INT >deliveries.id ?',
        'quantity DECIMAL !', 'reason VARCHAR !', 'moved_at TIMESTAMP !',
      ],
      stock_valuations: [
        'id BIGINT pk', 'item_id INT >items.id', 'warehouse_id INT >warehouses.id',
        'stock_move_id BIGINT >stock_moves.id ?', 'quantity DECIMAL !',
        'unit_cost DECIMAL !', 'value DECIMAL !', 'valued_at TIMESTAMP !',
      ],
    },
  },
];

// ── Dışa açılan şablonlar ────────────────────────────────────────────────────

export interface SchemaTemplate {
  key: string;
  label: string;
  description: string;
  size: TemplateSize;
  schema: DatabaseSchema;
}

export const TEMPLATES: SchemaTemplate[] = SPECS.map(spec => ({
  key: spec.key,
  label: spec.label,
  description: spec.description,
  size: spec.size,
  schema: build(spec),
}));

/** Galeri/şerit sıralaması: büyükten küçüğe değil, ölçek gruplarına göre. */
export const TEMPLATE_SIZES: { id: TemplateSize; label: string; blurb: string }[] = [
  { id: 'mini', label: 'Quick start', blurb: 'Small, focused schemas to build on.' },
  { id: 'standard', label: 'Full product', blurb: 'What a real application looks like.' },
  { id: 'large', label: 'Enterprise', blurb: 'Scale where the checks start to matter.' },
];

export const templatesOfSize = (size: TemplateSize) => TEMPLATES.filter(t => t.size === size);
