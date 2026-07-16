import { DatabaseSchema, SchemaTable, SchemaColumn, SchemaRelation } from '../types/schema';

const col = (
  id: string, name: string, type: string,
  isPK = false, isFK = false, isNullable = true,
): SchemaColumn => ({ id, stableUuid: id, name, type, isPK, isFK, isNullable, length: null, defaultValue: null });

const tbl = (id: string, name: string, columns: SchemaColumn[]): SchemaTable =>
  ({ id, stableUuid: id, name, columns });

const rel = (
  id: string, type: string,
  srcT: string, srcC: string, tgtT: string, tgtC: string,
): SchemaRelation => ({ id, type, sourceTableId: srcT, sourceColumnId: srcC, targetTableId: tgtT, targetColumnId: tgtC });

// ── Templates ─────────────────────────────────────────────────────────────────

export interface SchemaTemplate {
  key: string;
  label: string;
  description: string;
  emoji: string;
  schema: DatabaseSchema;
}

export const TEMPLATES: SchemaTemplate[] = [
  // ── E-Commerce ─────────────────────────────────────────────────────────────
  {
    key: 'ecommerce',
    label: 'E-Commerce',
    description: 'Users, products, categories, orders, order items, reviews',
    emoji: '🛒',
    schema: {
      schemaId: 'tpl-ecommerce',
      name: 'E-Commerce',
      tables: [
        tbl('ec-users', 'users', [
          col('ec-u1', 'id', 'INT', true), col('ec-u2', 'email', 'VARCHAR', false, false, false),
          col('ec-u3', 'name', 'VARCHAR'), col('ec-u4', 'created_at', 'TIMESTAMP'),
        ]),
        tbl('ec-cats', 'categories', [
          col('ec-ca1', 'id', 'INT', true), col('ec-ca2', 'name', 'VARCHAR', false, false, false),
          col('ec-ca3', 'parent_id', 'INT', false, true),
        ]),
        tbl('ec-prods', 'products', [
          col('ec-p1', 'id', 'INT', true), col('ec-p2', 'name', 'VARCHAR', false, false, false),
          col('ec-p3', 'price', 'DECIMAL', false, false, false), col('ec-p4', 'stock', 'INT', false, false, false),
          col('ec-p5', 'category_id', 'INT', false, true), col('ec-p6', 'description', 'TEXT'),
        ]),
        tbl('ec-orders', 'orders', [
          col('ec-o1', 'id', 'INT', true), col('ec-o2', 'user_id', 'INT', false, true, false),
          col('ec-o3', 'status', 'VARCHAR', false, false, false), col('ec-o4', 'total', 'DECIMAL', false, false, false),
          col('ec-o5', 'created_at', 'TIMESTAMP'),
        ]),
        tbl('ec-oi', 'order_items', [
          col('ec-oi1', 'id', 'INT', true), col('ec-oi2', 'order_id', 'INT', false, true, false),
          col('ec-oi3', 'product_id', 'INT', false, true, false), col('ec-oi4', 'quantity', 'INT', false, false, false),
          col('ec-oi5', 'unit_price', 'DECIMAL', false, false, false),
        ]),
        tbl('ec-rev', 'reviews', [
          col('ec-r1', 'id', 'INT', true), col('ec-r2', 'user_id', 'INT', false, true, false),
          col('ec-r3', 'product_id', 'INT', false, true, false), col('ec-r4', 'rating', 'INT', false, false, false),
          col('ec-r5', 'body', 'TEXT'),
        ]),
      ],
      relations: [
        rel('ec-rel1', 'ManyToOne', 'ec-prods', 'ec-p5', 'ec-cats', 'ec-ca1'),
        rel('ec-rel2', 'ManyToOne', 'ec-orders', 'ec-o2', 'ec-users', 'ec-u1'),
        rel('ec-rel3', 'ManyToOne', 'ec-oi', 'ec-oi2', 'ec-orders', 'ec-o1'),
        rel('ec-rel4', 'ManyToOne', 'ec-oi', 'ec-oi3', 'ec-prods', 'ec-p1'),
        rel('ec-rel5', 'ManyToOne', 'ec-rev', 'ec-r2', 'ec-users', 'ec-u1'),
        rel('ec-rel6', 'ManyToOne', 'ec-rev', 'ec-r3', 'ec-prods', 'ec-p1'),
      ],
    },
  },

  // ── Blog / CMS ─────────────────────────────────────────────────────────────
  {
    key: 'blog',
    label: 'Blog / CMS',
    description: 'Users, posts, tags, comments, media',
    emoji: '📝',
    schema: {
      schemaId: 'tpl-blog',
      name: 'Blog / CMS',
      tables: [
        tbl('bl-users', 'users', [
          col('bl-u1', 'id', 'INT', true), col('bl-u2', 'username', 'VARCHAR', false, false, false),
          col('bl-u3', 'email', 'VARCHAR', false, false, false), col('bl-u4', 'role', 'VARCHAR'),
        ]),
        tbl('bl-posts', 'posts', [
          col('bl-p1', 'id', 'INT', true), col('bl-p2', 'title', 'VARCHAR', false, false, false),
          col('bl-p3', 'slug', 'VARCHAR', false, false, false), col('bl-p4', 'body', 'TEXT'),
          col('bl-p5', 'author_id', 'INT', false, true, false), col('bl-p6', 'published_at', 'TIMESTAMP'),
          col('bl-p7', 'status', 'VARCHAR', false, false, false),
        ]),
        tbl('bl-tags', 'tags', [
          col('bl-t1', 'id', 'INT', true), col('bl-t2', 'name', 'VARCHAR', false, false, false),
          col('bl-t3', 'slug', 'VARCHAR', false, false, false),
        ]),
        tbl('bl-pt', 'post_tags', [
          col('bl-pt1', 'post_id', 'INT', false, true, false), col('bl-pt2', 'tag_id', 'INT', false, true, false),
        ]),
        tbl('bl-cmts', 'comments', [
          col('bl-c1', 'id', 'INT', true), col('bl-c2', 'post_id', 'INT', false, true, false),
          col('bl-c3', 'author_id', 'INT', false, true), col('bl-c4', 'body', 'TEXT', false, false, false),
          col('bl-c5', 'created_at', 'TIMESTAMP'),
        ]),
      ],
      relations: [
        rel('bl-rel1', 'ManyToOne', 'bl-posts', 'bl-p5', 'bl-users', 'bl-u1'),
        rel('bl-rel2', 'ManyToOne', 'bl-pt', 'bl-pt1', 'bl-posts', 'bl-p1'),
        rel('bl-rel3', 'ManyToOne', 'bl-pt', 'bl-pt2', 'bl-tags', 'bl-t1'),
        rel('bl-rel4', 'ManyToOne', 'bl-cmts', 'bl-c2', 'bl-posts', 'bl-p1'),
        rel('bl-rel5', 'ManyToOne', 'bl-cmts', 'bl-c3', 'bl-users', 'bl-u1'),
      ],
    },
  },

  // ── SaaS / Multi-tenant ────────────────────────────────────────────────────
  {
    key: 'saas',
    label: 'SaaS / Multi-tenant',
    description: 'Organizations, members, subscriptions, API keys',
    emoji: '🏢',
    schema: {
      schemaId: 'tpl-saas',
      name: 'SaaS / Multi-tenant',
      tables: [
        tbl('sa-users', 'users', [
          col('sa-u1', 'id', 'UUID', true), col('sa-u2', 'email', 'VARCHAR', false, false, false),
          col('sa-u3', 'name', 'VARCHAR'), col('sa-u4', 'created_at', 'TIMESTAMP'),
        ]),
        tbl('sa-orgs', 'organizations', [
          col('sa-o1', 'id', 'UUID', true), col('sa-o2', 'name', 'VARCHAR', false, false, false),
          col('sa-o3', 'slug', 'VARCHAR', false, false, false), col('sa-o4', 'plan', 'VARCHAR', false, false, false),
        ]),
        tbl('sa-mem', 'memberships', [
          col('sa-m1', 'id', 'UUID', true), col('sa-m2', 'user_id', 'UUID', false, true, false),
          col('sa-m3', 'org_id', 'UUID', false, true, false), col('sa-m4', 'role', 'VARCHAR', false, false, false),
          col('sa-m5', 'joined_at', 'TIMESTAMP'),
        ]),
        tbl('sa-subs', 'subscriptions', [
          col('sa-s1', 'id', 'UUID', true), col('sa-s2', 'org_id', 'UUID', false, true, false),
          col('sa-s3', 'stripe_id', 'VARCHAR'), col('sa-s4', 'status', 'VARCHAR', false, false, false),
          col('sa-s5', 'current_period_end', 'TIMESTAMP'),
        ]),
        tbl('sa-keys', 'api_keys', [
          col('sa-k1', 'id', 'UUID', true), col('sa-k2', 'org_id', 'UUID', false, true, false),
          col('sa-k3', 'key_hash', 'VARCHAR', false, false, false), col('sa-k4', 'name', 'VARCHAR'),
          col('sa-k5', 'expires_at', 'TIMESTAMP'), col('sa-k6', 'last_used_at', 'TIMESTAMP'),
        ]),
      ],
      relations: [
        rel('sa-rel1', 'ManyToOne', 'sa-mem', 'sa-m2', 'sa-users', 'sa-u1'),
        rel('sa-rel2', 'ManyToOne', 'sa-mem', 'sa-m3', 'sa-orgs', 'sa-o1'),
        rel('sa-rel3', 'ManyToOne', 'sa-subs', 'sa-s2', 'sa-orgs', 'sa-o1'),
        rel('sa-rel4', 'ManyToOne', 'sa-keys', 'sa-k2', 'sa-orgs', 'sa-o1'),
      ],
    },
  },

  // ── CRM ────────────────────────────────────────────────────────────────────
  {
    key: 'crm',
    label: 'CRM',
    description: 'Contacts, companies, deals, activities, notes',
    emoji: '📊',
    schema: {
      schemaId: 'tpl-crm',
      name: 'CRM',
      tables: [
        tbl('crm-users', 'users', [
          col('crm-u1', 'id', 'INT', true), col('crm-u2', 'name', 'VARCHAR', false, false, false),
          col('crm-u3', 'email', 'VARCHAR', false, false, false),
        ]),
        tbl('crm-cos', 'companies', [
          col('crm-co1', 'id', 'INT', true), col('crm-co2', 'name', 'VARCHAR', false, false, false),
          col('crm-co3', 'industry', 'VARCHAR'), col('crm-co4', 'website', 'VARCHAR'),
          col('crm-co5', 'owner_id', 'INT', false, true),
        ]),
        tbl('crm-cts', 'contacts', [
          col('crm-ct1', 'id', 'INT', true), col('crm-ct2', 'first_name', 'VARCHAR', false, false, false),
          col('crm-ct3', 'last_name', 'VARCHAR', false, false, false), col('crm-ct4', 'email', 'VARCHAR'),
          col('crm-ct5', 'company_id', 'INT', false, true), col('crm-ct6', 'owner_id', 'INT', false, true),
        ]),
        tbl('crm-deals', 'deals', [
          col('crm-d1', 'id', 'INT', true), col('crm-d2', 'name', 'VARCHAR', false, false, false),
          col('crm-d3', 'value', 'DECIMAL'), col('crm-d4', 'stage', 'VARCHAR', false, false, false),
          col('crm-d5', 'contact_id', 'INT', false, true), col('crm-d6', 'owner_id', 'INT', false, true),
          col('crm-d7', 'close_date', 'DATE'),
        ]),
        tbl('crm-acts', 'activities', [
          col('crm-a1', 'id', 'INT', true), col('crm-a2', 'type', 'VARCHAR', false, false, false),
          col('crm-a3', 'deal_id', 'INT', false, true), col('crm-a4', 'user_id', 'INT', false, true, false),
          col('crm-a5', 'notes', 'TEXT'), col('crm-a6', 'due_at', 'TIMESTAMP'),
        ]),
      ],
      relations: [
        rel('crm-rel1', 'ManyToOne', 'crm-cos', 'crm-co5', 'crm-users', 'crm-u1'),
        rel('crm-rel2', 'ManyToOne', 'crm-cts', 'crm-ct5', 'crm-cos', 'crm-co1'),
        rel('crm-rel3', 'ManyToOne', 'crm-cts', 'crm-ct6', 'crm-users', 'crm-u1'),
        rel('crm-rel4', 'ManyToOne', 'crm-deals', 'crm-d5', 'crm-cts', 'crm-ct1'),
        rel('crm-rel5', 'ManyToOne', 'crm-deals', 'crm-d6', 'crm-users', 'crm-u1'),
        rel('crm-rel6', 'ManyToOne', 'crm-acts', 'crm-a3', 'crm-deals', 'crm-d1'),
        rel('crm-rel7', 'ManyToOne', 'crm-acts', 'crm-a4', 'crm-users', 'crm-u1'),
      ],
    },
  },

  // ── Healthcare ─────────────────────────────────────────────────────────────
  {
    key: 'healthcare',
    label: 'Healthcare',
    description: 'Patients, doctors, appointments, prescriptions',
    emoji: '🏥',
    schema: {
      schemaId: 'tpl-healthcare',
      name: 'Healthcare',
      tables: [
        tbl('hc-pts', 'patients', [
          col('hc-p1', 'id', 'INT', true), col('hc-p2', 'first_name', 'VARCHAR', false, false, false),
          col('hc-p3', 'last_name', 'VARCHAR', false, false, false), col('hc-p4', 'date_of_birth', 'DATE'),
          col('hc-p5', 'gender', 'VARCHAR'), col('hc-p6', 'email', 'VARCHAR'),
        ]),
        tbl('hc-docs', 'doctors', [
          col('hc-d1', 'id', 'INT', true), col('hc-d2', 'name', 'VARCHAR', false, false, false),
          col('hc-d3', 'specialty', 'VARCHAR', false, false, false), col('hc-d4', 'license_no', 'VARCHAR'),
        ]),
        tbl('hc-appts', 'appointments', [
          col('hc-a1', 'id', 'INT', true), col('hc-a2', 'patient_id', 'INT', false, true, false),
          col('hc-a3', 'doctor_id', 'INT', false, true, false), col('hc-a4', 'scheduled_at', 'TIMESTAMP', false, false, false),
          col('hc-a5', 'status', 'VARCHAR', false, false, false), col('hc-a6', 'notes', 'TEXT'),
        ]),
        tbl('hc-rx', 'prescriptions', [
          col('hc-rx1', 'id', 'INT', true), col('hc-rx2', 'appointment_id', 'INT', false, true, false),
          col('hc-rx3', 'medication', 'VARCHAR', false, false, false), col('hc-rx4', 'dosage', 'VARCHAR'),
          col('hc-rx5', 'duration_days', 'INT'),
        ]),
      ],
      relations: [
        rel('hc-rel1', 'ManyToOne', 'hc-appts', 'hc-a2', 'hc-pts', 'hc-p1'),
        rel('hc-rel2', 'ManyToOne', 'hc-appts', 'hc-a3', 'hc-docs', 'hc-d1'),
        rel('hc-rel3', 'ManyToOne', 'hc-rx', 'hc-rx2', 'hc-appts', 'hc-a1'),
      ],
    },
  },
];
