import type { DatabaseSchema } from '../types/schema';
import {
  generateClassDiagram,
  generateFlowchart,
  generateMindmap,
  generateStateDiagram,
  generateSequenceDiagram,
  generateGitGraph,
} from '../utils/diagramGenerators';

export interface MermaidTypeDefinition {
  id: string;
  name: string;
  description: string;
  fileExtension: string;
}

export const MERMAID_DIAGRAM_TYPES: MermaidTypeDefinition[] = [
  {
    id: 'ER',
    name: 'Entity-Relationship (ERD)',
    description: 'Relational database schema with tables, columns, primary & foreign keys',
    fileExtension: 'er.mmd',
  },
  {
    id: 'CLASS',
    name: 'Class Diagram',
    description: 'Object-oriented entities with properties, typed fields and associations',
    fileExtension: 'class.mmd',
  },
  {
    id: 'FLOW',
    name: 'Architecture Pipeline Flow',
    description: 'Directed graph flow of database tables, event cascades and data ingestion',
    fileExtension: 'flow.mmd',
  },
  {
    id: 'SEQUENCE',
    name: 'Gateway Protocol Sequence',
    description: 'Step-by-step transaction lifecycle between client, gateway and relational storage',
    fileExtension: 'sequence.mmd',
  },
  {
    id: 'STATE',
    name: 'Migration Lifecycle State',
    description: 'State machine transition diagram for database migrations and record states',
    fileExtension: 'state.mmd',
  },
  {
    id: 'MINDMAP',
    name: 'Domain Mind Map',
    description: 'Hierarchical conceptual cluster diagram of database modules and domain bounded contexts',
    fileExtension: 'mindmap.mmd',
  },
  {
    id: 'GIT',
    name: 'Branch & Merge Graph',
    description: 'Visual branching history showing feature branches and change request integration',
    fileExtension: 'git.mmd',
  },
];

export function generateMermaidByType(schema: DatabaseSchema, typeId: string): string {
  switch (typeId) {
    case 'CLASS':
      return generateClassDiagram(schema);
    case 'FLOW':
      return generateFlowchart(schema);
    case 'SEQUENCE':
      return generateSequenceDiagram(schema);
    case 'STATE':
      return generateStateDiagram(schema);
    case 'MINDMAP':
      return generateMindmap(schema);
    case 'GIT':
      return generateGitGraph(schema);
    case 'ER':
    default: {
      const tableMap = new Map((schema?.tables || []).map((t) => [t.id, t.name]));
      const lines = ['erDiagram'];
      for (const table of schema?.tables || []) {
        lines.push(`    ${table.name} {`);
        for (const col of table.columns) {
          lines.push(`        ${col.type} ${col.name}${col.isPK ? ' PK' : ''}`);
        }
        lines.push('    }');
      }
      for (const rel of schema?.relations || []) {
        const srcTable = tableMap.get(rel.sourceTableId) || rel.sourceTableId;
        const tgtTable = tableMap.get(rel.targetTableId) || rel.targetTableId;
        lines.push(`    ${srcTable} }|--|| ${tgtTable} : references`);
      }
      return lines.join('\n');
    }
  }
}

export interface BuildTargetDefinition {
  id: string;
  name: string;
  category: string;
  fileName: string;
  tag: string;
  description: string;
}

export const BUILD_CODE_TARGETS: BuildTargetDefinition[] = [
  {
    id: 'sdk.typescript',
    name: 'TypeScript SDK Client',
    category: 'SDK & Client',
    fileName: 'client.ts',
    tag: 'TypeScript',
    description: 'Full-featured typed client for querying, inserting and mutating records via Gateway API',
  },
  {
    id: 'types.typescript',
    name: 'TypeScript Interfaces',
    category: 'Types',
    fileName: 'types.d.ts',
    tag: 'TypeScript',
    description: 'Strongly-typed entity interfaces, row definitions, and primary key types',
  },
  {
    id: 'schemas.zod',
    name: 'Zod Validation Schemas',
    category: 'Validation',
    fileName: 'schemas.zod.ts',
    tag: 'Zod',
    description: 'Runtime request body validation schemas with custom type guards and error handling',
  },
  {
    id: 'orm.drizzle',
    name: 'Drizzle ORM Schema',
    category: 'ORM',
    fileName: 'schema.drizzle.ts',
    tag: 'Drizzle',
    description: 'Production-ready Drizzle ORM pgTable / sqliteTable definitions with relation helpers',
  },
  {
    id: 'orm.typeorm',
    name: 'TypeORM Entities',
    category: 'ORM',
    fileName: 'entities.typeorm.ts',
    tag: 'TypeORM',
    description: 'Decorated Entity classes with @PrimaryGeneratedColumn and relation decorators',
  },
  {
    id: 'schema.prisma',
    name: 'Prisma Schema Models',
    category: 'ORM',
    fileName: 'schema.prisma',
    tag: 'Prisma',
    description: 'Kanonik Prisma schema definitions with generator and relation directives',
  },
  {
    id: 'orm.sqlalchemy',
    name: 'SQLAlchemy Models',
    category: 'Python',
    fileName: 'models.sqlalchemy.py',
    tag: 'Python',
    description: 'Declarative Base models with mapped column types and ForeignKey constraints',
  },
  {
    id: 'types.pydantic',
    name: 'Pydantic v2 Models',
    category: 'Python',
    fileName: 'schemas.pydantic.py',
    tag: 'Python',
    description: 'FastAPI / Pydantic BaseModel request & response validation schemas',
  },
  {
    id: 'schema.graphql',
    name: 'GraphQL SDL Schema',
    category: 'API Spec',
    fileName: 'schema.graphql',
    tag: 'GraphQL',
    description: 'GraphQL types, queries, and mutation definitions for all tables',
  },
  {
    id: 'spec.ir',
    name: 'Canonical IR (ir.json)',
    category: 'Specification',
    fileName: 'ir.json',
    tag: 'JSON IR',
    description: 'Standard Namines Canonical Intermediate Representation for automated codegen pipelines',
  },
];

export function generateBuildArtifact(
  schema: DatabaseSchema,
  targetId: string,
  engine = 'PostgreSQL',
): { fileName: string; content: string; tag: string } {
  const target = BUILD_CODE_TARGETS.find((t) => t.id === targetId) || BUILD_CODE_TARGETS[0];
  const tables = schema.tables || [];

  switch (targetId) {
    case 'types.typescript': {
      const code = `// Auto-generated TypeScript Entity Types\n// Target Engine: ${engine}\n// Tables: ${tables.length}\n\n${tables
        .map(
          (t) =>
            `export interface ${capitalize(t.name)} {\n${t.columns
              .map(
                (c) =>
                  `  ${c.name}${c.isNullable ? '?' : ''}: ${toTsType(c.type)}${c.isNullable ? ' | null' : ''};`,
              )
              .join('\n')}\n}`,
        )
        .join('\n\n')}\n`;
      return { fileName: target.fileName, content: code, tag: target.tag };
    }

    case 'sdk.typescript': {
      const code = `// Auto-generated Namines Gateway Client SDK\n// Target Engine: ${engine}\n\nimport { createClient } from '@namines/client';\n\nexport const db = createClient({\n  endpoint: process.env.NAMINES_GATEWAY_URL || 'http://localhost:5000',\n  apiKey: process.env.NAMINES_API_KEY || '',\n});\n\nexport const tables = {\n${tables
        .map(
          (t) =>
            `  ${t.name}: db.table('${t.name}'),`,
        )
        .join('\n')}\n};\n`;
      return { fileName: target.fileName, content: code, tag: target.tag };
    }

    case 'schemas.zod': {
      const code = `// Auto-generated Zod Schemas\nimport { z } from 'zod';\n\n${tables
        .map(
          (t) =>
            `export const ${capitalize(t.name)}Schema = z.object({\n${t.columns
              .map(
                (c) =>
                  `  ${c.name}: ${toZodType(c.type)}${c.isNullable ? '.nullable().optional()' : ''},`,
              )
              .join('\n')}\n});\nexport type ${capitalize(t.name)} = z.infer<typeof ${capitalize(t.name)}Schema>;`,
        )
        .join('\n\n')}\n`;
      return { fileName: target.fileName, content: code, tag: target.tag };
    }

    case 'orm.drizzle': {
      const code = `// Auto-generated Drizzle ORM Schema\nimport { pgTable, text, serial, timestamp, integer, boolean, uuid } from 'drizzle-orm/pg-core';\n\n${tables
        .map(
          (t) =>
            `export const ${t.name} = pgTable('${t.name}', {\n${t.columns
              .map((c) => `  ${c.name}: ${toDrizzleCol(c)},`)
              .join('\n')}\n});`,
        )
        .join('\n\n')}\n`;
      return { fileName: target.fileName, content: code, tag: target.tag };
    }

    case 'orm.typeorm': {
      const code = `// Auto-generated TypeORM Entities\nimport { Entity, PrimaryGeneratedColumn, Column } from 'typeorm';\n\n${tables
        .map(
          (t) =>
            `@Entity('${t.name}')\nexport class ${capitalize(t.name)} {\n${t.columns
              .map(
                (c) =>
                  `  ${c.isPK ? '@PrimaryGeneratedColumn()' : `@Column({ type: '${c.type}' })`}\n  ${c.name}!: ${toTsType(c.type)};`,
              )
              .join('\n\n')}\n}`,
        )
        .join('\n\n')}\n`;
      return { fileName: target.fileName, content: code, tag: target.tag };
    }

    case 'schema.prisma': {
      const code = `// Auto-generated Prisma Schema\ndatasource db {\n  provider = "postgresql"\n  url      = env("DATABASE_URL")\n}\n\ngenerator client {\n  provider = "prisma-client-js"\n}\n\n${tables
        .map(
          (t) =>
            `model ${capitalize(t.name)} {\n${t.columns
              .map(
                (c) =>
                  `  ${c.name.padEnd(16)} ${toPrismaType(c.type)}${c.isNullable ? '?' : ''}${c.isPK ? ' @id' : ''}`,
              )
              .join('\n')}\n\n  @@map("${t.name}")\n}`,
        )
        .join('\n\n')}\n`;
      return { fileName: target.fileName, content: code, tag: target.tag };
    }

    case 'orm.sqlalchemy': {
      const code = `# Auto-generated SQLAlchemy Models\nfrom sqlalchemy import Column, Integer, String, Boolean, DateTime, Float, ForeignKey\nfrom sqlalchemy.orm import declarative_base\n\nBase = declarative_base()\n\n${tables
        .map(
          (t) =>
            `class ${capitalize(t.name)}(Base):\n    __tablename__ = '${t.name}'\n\n${t.columns
              .map(
                (c) =>
                  `    ${c.name} = Column(${toSqlAlchemyType(c.type)}${c.isPK ? ', primary_key=True' : ''}${!c.isNullable ? ', nullable=False' : ''})`,
              )
              .join('\n')}`,
        )
        .join('\n\n')}\n`;
      return { fileName: target.fileName, content: code, tag: target.tag };
    }

    case 'types.pydantic': {
      const code = `# Auto-generated Pydantic v2 Models\nfrom pydantic import BaseModel, ConfigDict\nfrom typing import Optional\nfrom datetime import datetime\n\n${tables
        .map(
          (t) =>
            `class ${capitalize(t.name)}(BaseModel):\n    model_config = ConfigDict(from_attributes=True)\n\n${t.columns
              .map(
                (c) =>
                  `    ${c.name}: ${c.isNullable ? `Optional[${toPyType(c.type)}] = None` : toPyType(c.type)}`,
              )
              .join('\n')}`,
        )
        .join('\n\n')}\n`;
      return { fileName: target.fileName, content: code, tag: target.tag };
    }

    case 'schema.graphql': {
      const code = `# Auto-generated GraphQL Schema\n\n${tables
        .map(
          (t) =>
            `type ${capitalize(t.name)} {\n${t.columns
              .map(
                (c) =>
                  `  ${c.name}: ${toGqlType(c.type)}${c.isNullable ? '' : '!'}`,
              )
              .join('\n')}\n}`,
        )
        .join('\n\n')}\n\ntype Query {\n${tables
        .map((t) => `  ${t.name}: [${capitalize(t.name)}!]!`)
        .join('\n')}\n}\n`;
      return { fileName: target.fileName, content: code, tag: target.tag };
    }

    case 'spec.ir':
    default: {
      const ir = {
        $schema: 'https://namines.app/schemas/ir-v1.json',
        generator: 'Namines Eject Engine v1.0',
        timestamp: new Date().toISOString(),
        engine,
        schema: {
          id: schema.schemaId || 'canonical',
          name: schema.name || 'Architecture',
          tables: schema.tables,
          relations: schema.relations,
        },
      };
      return {
        fileName: target.fileName,
        content: JSON.stringify(ir, null, 2),
        tag: target.tag,
      };
    }
  }
}

function capitalize(str: string): string {
  if (!str) return '';
  return str.charAt(0).toUpperCase() + str.slice(1);
}

function toTsType(type: string): string {
  const t = type.toLowerCase();
  if (t.includes('int') || t.includes('serial') || t.includes('numeric') || t.includes('decimal') || t.includes('float')) return 'number';
  if (t.includes('bool')) return 'boolean';
  if (t.includes('date') || t.includes('time')) return 'string';
  return 'string';
}

function toZodType(type: string): string {
  const t = type.toLowerCase();
  if (t.includes('int') || t.includes('serial')) return 'z.number().int()';
  if (t.includes('numeric') || t.includes('decimal') || t.includes('float')) return 'z.number()';
  if (t.includes('bool')) return 'z.boolean()';
  if (t.includes('date') || t.includes('time')) return 'z.string().datetime()';
  if (t.includes('uuid')) return 'z.string().uuid()';
  return 'z.string()';
}

function toDrizzleCol(col: { name: string; type: string; isPK: boolean }): string {
  const t = col.type.toLowerCase();
  let fn = `text('${col.name}')`;
  if (col.isPK) fn = `serial('${col.name}').primaryKey()`;
  else if (t.includes('int')) fn = `integer('${col.name}')`;
  else if (t.includes('bool')) fn = `boolean('${col.name}')`;
  else if (t.includes('uuid')) fn = `uuid('${col.name}')`;
  else if (t.includes('time') || t.includes('date')) fn = `timestamp('${col.name}')`;
  return fn;
}

function toPrismaType(type: string): string {
  const t = type.toLowerCase();
  if (t.includes('int') || t.includes('serial')) return 'Int';
  if (t.includes('numeric') || t.includes('float') || t.includes('decimal')) return 'Float';
  if (t.includes('bool')) return 'Boolean';
  if (t.includes('date') || t.includes('time')) return 'DateTime';
  return 'String';
}

function toSqlAlchemyType(type: string): string {
  const t = type.toLowerCase();
  if (t.includes('int') || t.includes('serial')) return 'Integer';
  if (t.includes('numeric') || t.includes('float') || t.includes('decimal')) return 'Float';
  if (t.includes('bool')) return 'Boolean';
  if (t.includes('date') || t.includes('time')) return 'DateTime';
  return 'String(255)';
}

function toPyType(type: string): string {
  const t = type.toLowerCase();
  if (t.includes('int') || t.includes('serial')) return 'int';
  if (t.includes('numeric') || t.includes('float') || t.includes('decimal')) return 'float';
  if (t.includes('bool')) return 'bool';
  if (t.includes('date') || t.includes('time')) return 'datetime';
  return 'str';
}

function toGqlType(type: string): string {
  const t = type.toLowerCase();
  if (t.includes('int') || t.includes('serial')) return 'Int';
  if (t.includes('numeric') || t.includes('float') || t.includes('decimal')) return 'Float';
  if (t.includes('bool')) return 'Boolean';
  if (t.includes('uuid')) return 'ID';
  return 'String';
}
