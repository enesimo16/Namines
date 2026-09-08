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
  code += `  root((${sanitize(schema.name || 'Database')}))\n`;
  schema.tables.forEach(t => {
    code += `    ${sanitize(t.name)}\n`;
    t.columns.slice(0, 5).forEach(c => {
      code += `      ${sanitize(c.name)}\n`;
    });
    if (t.columns.length > 5) {
      code += `      ...and_${t.columns.length - 5}_more\n`;
    }
  });
  return code;
};

export const generateStateDiagram = (schema: DatabaseSchema) => {
  let code = 'stateDiagram-v2\n';
  schema.tables.forEach(t => {
    code += `  state ${sanitize(t.name)} {\n`;
    code += `    [*] --> Active_${sanitize(t.name)}\n`;
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
  code += '  actor User as User (Client)\n';
  code += '  participant API as Backend API\n';
  schema.tables.forEach(t => {
    code += `  participant DB_${sanitize(t.name)} as DB: ${t.name}\n`;
  });
  schema.tables.forEach(t => {
    code += `  User->>API: HTTP Request (${t.name} Query)\n`;
    code += `  API->>DB_${sanitize(t.name)}: SELECT * FROM ${t.name}\n`;
    code += `  DB_${sanitize(t.name)}-->>API: Rowset Result\n`;
    code += `  API-->>User: JSON Response\n`;
  });
  return code;
};

export const generateGanttChart = (schema: DatabaseSchema) => {
  let code = 'gantt\n';
  code += `  title ${schema.name || 'Database'} Project Roadmap\n`;
  code += '  dateFormat  YYYY-MM-DD\n';
  code += '  section Design Phase\n';
  code += `  Schema Design          :active, d1, 2026-05-01, 7d\n`;
  code += '  Linter & Normalization : d2, after d1, 3d\n';
  code += '  section Database Setup\n';
  schema.tables.forEach((t, idx) => {
    code += `  ${t.name} Table Setup : d3_${idx}, after d2, 2d\n`;
  });
  code += '  section Test & Seeding\n';
  code += '  Mock Data Generation   : d4, after d2, 4d\n';
  code += '  Integration Tests      : d5, after d4, 5d\n';
  return code;
};

export const generatePieChart = (schema: DatabaseSchema) => {
  let code = `pie title ${schema.name || 'Database'} Table Column Density\n`;
  schema.tables.forEach(t => {
    code += `  "${t.name}" : ${t.columns.length}\n`;
  });
  if (schema.tables.length === 0) {
    code += '  "No Tables" : 1\n';
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
  code += `  title ${schema.name || 'Database'} Data Lifecycle\n`;
  code += '  section User Registration\n';
  code += '    Filling the Form: 5: User\n';
  code += '    Password Hashing: 4: API Service\n';
  if (schema.tables.some(t => t.name.toLowerCase().includes('user') || t.name.toLowerCase().includes('uye'))) {
    code += '    Writing to the User Table: 5: DB Engine\n';
  }
  code += '  section Data Querying\n';
  code += '    Viewing the Dashboard: 5: User\n';
  code += '    Joining Related Data: 4: DB Engine\n';
  code += '    Cache Lookup: 3: Redis / API\n';
  return code;
};

export const generateTimeline = (schema: DatabaseSchema) => {
  let code = 'timeline\n';
  code += `  title ${schema.name || 'Database'} Release Timeline\n`;
  code += '  Design Phase : Project Kickoff : Schema Design\n';
  schema.tables.forEach((t, idx) => {
    code += `  Release v1.${idx + 1} : ${t.name} Added : ${t.columns.length} Columns Defined\n`;
  });
  return code;
};

export const generateQuadrantChart = (schema: DatabaseSchema) => {
  let code = 'quadrantChart\n';
  code += '  title Table Complexity & Usage Analysis\n';
  code += '  x-axis Few Relations --> Many Relations\n';
  code += '  y-axis Few Columns --> Many Columns\n';
  code += '  quadrant-1 Critical Core Tables\n';
  code += '  quadrant-2 Detail Data Tables\n';
  code += '  quadrant-3 Junction/Temporary Tables\n';
  code += '  quadrant-4 Reference/Look-up Tables\n';
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
    code += `    text: "Data integrity and referential integrity of the ${t.name} table must be preserved."\n`;
    code += `    risk: ${t.columns.some(c => c.isPK) ? 'medium' : 'low'}\n`;
    code += `    verifymethod: Test\n`;
    code += `  }\n`;
  });
  return code;
};
