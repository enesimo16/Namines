import { DatabaseSchema } from '../types/schema';

const sanitize = (str: string) => str ? str.replace(/[\s"'\(\)]/g, '_') : 'unknown';

export const generateClassDiagram = (schema: DatabaseSchema) => {
  let code = 'classDiagram\n';
  schema.tables.forEach(t => {
    const tName = sanitize(t.name);
    code += `  class ${tName} {\n`;
    t.columns.forEach(c => {
      const type = sanitize(`${c.type}${c.length ? `(${c.length})` : ''}`);
      const cName = sanitize(c.name);
      const prefix = c.isPK ? '+' : (c.isFK ? '~' : '-');
      code += `    ${prefix}${type} ${cName}\n`;
    });
    code += `  }\n`;
  });
  schema.relations.forEach(r => {
    const source = sanitize(schema.tables.find(t => t.id === r.sourceTableId)?.name || '');
    const target = sanitize(schema.tables.find(t => t.id === r.targetTableId)?.name || '');
    if (source && target && source !== 'unknown' && target !== 'unknown') {
      const type = r.type.toLowerCase();
      let arrow = '-->';
      if (type === 'onetoone') arrow = '--|>';
      if (type === 'onetomany') arrow = '-->';
      if (type === 'manytomany') arrow = '<-->';
      code += `  ${target} ${arrow} ${source} : FK\n`;
    }
  });
  return code;
};

export const generateFlowchart = (schema: DatabaseSchema) => {
  let code = 'flowchart TD\n';
  schema.tables.forEach(t => {
    const tName = sanitize(t.name);
    code += `  ${tName}["${t.name}"]\n`;
  });
  schema.relations.forEach(r => {
    const source = sanitize(schema.tables.find(t => t.id === r.sourceTableId)?.name || '');
    const target = sanitize(schema.tables.find(t => t.id === r.targetTableId)?.name || '');
    if (source && target && source !== 'unknown' && target !== 'unknown') {
      code += `  ${target} -->|${r.type}| ${source}\n`;
    }
  });
  return code;
};

export const generateMindmap = (schema: DatabaseSchema) => {
  let code = 'mindmap\n';
  code += `  root((${sanitize(schema.name || 'Veritabani')}))\n`;
  schema.tables.forEach(t => {
    code += `    ${sanitize(t.name)}\n`;
    t.columns.slice(0, 5).forEach(c => {
      code += `      ${sanitize(c.name)}\n`;
    });
    if (t.columns.length > 5) {
      code += `      ...ve_${t.columns.length - 5}_daha\n`;
    }
  });
  return code;
};

export const generateStateDiagram = (schema: DatabaseSchema) => {
  let code = 'stateDiagram-v2\n';
  schema.tables.forEach(t => {
    code += `  state ${sanitize(t.name)} {\n`;
    code += `    [*] --> Aktif_${sanitize(t.name)}\n`;
    code += `  }\n`;
  });
  schema.relations.forEach(r => {
    const source = sanitize(schema.tables.find(t => t.id === r.sourceTableId)?.name || '');
    const target = sanitize(schema.tables.find(t => t.id === r.targetTableId)?.name || '');
    if (source && target && source !== 'unknown' && target !== 'unknown') {
      code += `  ${target} --> ${source} : ${r.type}\n`;
    }
  });
  return code;
};

export const generateSequenceDiagram = (schema: DatabaseSchema) => {
  let code = 'sequenceDiagram\n';
  code += '  autonumber\n';
  code += '  actor Kullanici as Kullanıcı (Client)\n';
  code += '  participant API as Backend API\n';
  schema.tables.forEach(t => {
    code += `  participant DB_${sanitize(t.name)} as DB: ${t.name}\n`;
  });
  schema.tables.forEach(t => {
    code += `  Kullanici->>API: HTTP Request (${t.name} Sorgusu)\n`;
    code += `  API->>DB_${sanitize(t.name)}: SELECT * FROM ${t.name}\n`;
    code += `  DB_${sanitize(t.name)}-->>API: Rowset Result\n`;
    code += `  API-->>Kullanici: JSON Response\n`;
  });
  return code;
};

export const generateGanttChart = (schema: DatabaseSchema) => {
  let code = 'gantt\n';
  code += `  title ${schema.name || 'Veritabanı'} Projesi Yol Haritası\n`;
  code += '  dateFormat  YYYY-MM-DD\n';
  code += '  section Tasarım Fazı\n';
  code += `  Şema Tasarımı          :active, d1, 2026-05-01, 7d\n`;
  code += '  Linter & Normalizasyon : d2, after d1, 3d\n';
  code += '  section Veritabanı Kurulumu\n';
  schema.tables.forEach((t, idx) => {
    code += `  ${t.name} Tablosu Kurulumu : d3_${idx}, after d2, 2d\n`;
  });
  code += '  section Test & Seeding\n';
  code += '  Mock Veri Üretimi      : d4, after d2, 4d\n';
  code += '  Entegrasyon Testleri   : d5, after d4, 5d\n';
  return code;
};

export const generatePieChart = (schema: DatabaseSchema) => {
  let code = `pie title ${schema.name || 'Veritabanı'} Tablo Kolon Yoğunluğu\n`;
  schema.tables.forEach(t => {
    code += `  "${t.name}" : ${t.columns.length}\n`;
  });
  if (schema.tables.length === 0) {
    code += '  "Tablo Yok" : 1\n';
  }
  return code;
};

export const generateGitGraph = (schema: DatabaseSchema) => {
  let code = 'gitGraph\n';
  code += '  commit id: "v1.0.0_initial"\n';
  schema.tables.forEach((t, idx) => {
    if (idx === 0) {
      code += `  commit id: "add_table_${sanitize(t.name)}"\n`;
    } else if (idx === 1) {
      code += `  branch migration_v1.1\n`;
      code += `  checkout migration_v1.1\n`;
      code += `  commit id: "add_table_${sanitize(t.name)}"\n`;
      code += `  checkout main\n`;
      code += `  merge migration_v1.1\n`;
    } else {
      code += `  commit id: "add_table_${sanitize(t.name)}"\n`;
    }
  });
  return code;
};

export const generateUserJourney = (schema: DatabaseSchema) => {
  let code = 'journey\n';
  code += `  title ${schema.name || 'Veritabanı'} Veri Yaşam Döngüsü\n`;
  code += '  section Kullanıcı Kaydı\n';
  code += '    Formu Doldurma: 5: Kullanıcı\n';
  code += '    Şifre Hashleme: 4: API Servisi\n';
  if (schema.tables.some(t => t.name.toLowerCase().includes('user') || t.name.toLowerCase().includes('uye'))) {
    code += '    Kullanıcı Tablosuna Yazma: 5: DB Engine\n';
  }
  code += '  section Veri Sorgulama\n';
  code += '    Dashboard Görüntüleme: 5: Kullanıcı\n';
  code += '    İlişkili Veri Joinleme: 4: DB Engine\n';
  code += '    Önbellek (Cache) Kontrolü: 3: Redis / API\n';
  return code;
};

export const generateTimeline = (schema: DatabaseSchema) => {
  let code = 'timeline\n';
  code += `  title ${schema.name || 'Veritabanı'} Sürüm Kronolojisi\n`;
  code += '  Tasarım Aşaması : Proje Başlangıcı : Şema Tasarımı\n';
  schema.tables.forEach((t, idx) => {
    code += `  Sürüm v1.${idx + 1} : ${t.name} Eklendi : ${t.columns.length} Sütun Tanımlandı\n`;
  });
  return code;
};

export const generateQuadrantChart = (schema: DatabaseSchema) => {
  let code = 'quadrantChart\n';
  code += '  title Tablo Kompleksite & Kullanım Analizi\n';
  code += '  x-axis Düşük İlişki Sayısı --> Yüksek İlişki Sayısı\n';
  code += '  y-axis Az Sütun Sayısı --> Çok Sütun Sayısı\n';
  code += '  quadrant-1 Kritik Çekirdek Tablolar\n';
  code += '  quadrant-2 Detay Veri Tabloları\n';
  code += '  quadrant-3 Ara/Geçici Tablolar\n';
  code += '  quadrant-4 Tanım/Look-up Tabloları\n';
  schema.tables.forEach((t, idx) => {
    const relCount = schema.relations.filter(r => r.sourceTableId === t.id || r.targetTableId === t.id).length;
    const xVal = Math.min(0.9, Math.max(0.1, relCount / 5));
    const yVal = Math.min(0.9, Math.max(0.1, t.columns.length / 15));
    code += `  ${t.name}: [${xVal.toFixed(2)}, ${yVal.toFixed(2)}]\n`;
  });
  return code;
};

export const generateRequirementDiagram = (schema: DatabaseSchema) => {
  let code = 'requirementDiagram\n';
  schema.tables.forEach((t, idx) => {
    code += `  requirement req_${sanitize(t.name)} {\n`;
    code += `    id: ${100 + idx}\n`;
    code += `    text: "${t.name} tablosunun veri bütünlüğü ve ilişkisel bütünlüğü korunmalıdır."\n`;
    code += `    risk: ${t.columns.some(c => c.isPK) ? 'medium' : 'low'}\n`;
    code += `    verifymethod: Test\n`;
    code += `  }\n`;
  });
  return code;
};
