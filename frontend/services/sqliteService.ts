import initSqlJs from 'sql.js';

let SQL: any = null;
let dbInstance: any = null;

/**
 * Initializes and fetches the sql.js Wasm library from CDN.
 */
async function getSqlInstance() {
  if (SQL) return SQL;
  
  const version = '1.14.1';

  // Try 1: jsdelivr CDN
  try {
    SQL = await initSqlJs({
      locateFile: (file: string) => `https://cdn.jsdelivr.net/npm/sql.js@${version}/dist/${file}`
    });
    console.log('✔ sql.js successfully loaded from jsdelivr CDN');
    return SQL;
  } catch (err1) {
    console.warn("Failed to load sql.js from jsdelivr, trying unpkg...", err1);
  }

  // Try 2: unpkg CDN
  try {
    SQL = await initSqlJs({
      locateFile: (file: string) => `https://unpkg.com/sql.js@${version}/dist/${file}`
    });
    console.log('✔ sql.js successfully loaded from unpkg CDN');
    return SQL;
  } catch (err2) {
    console.warn("Failed to load sql.js from unpkg, trying local fallback...", err2);
  }

  // Try 3: local public folder fallback
  try {
    SQL = await initSqlJs({
      locateFile: (file: string) => `/${file}`
    });
    console.log('✔ sql.js loaded from local fallback');
    return SQL;
  } catch (err3) {
    console.error("All sql-wasm.wasm loads failed:", err3);
    throw err3;
  }
}

export interface SqlQueryResult {
  columns: string[];
  rows: Record<string, any>[];
  message?: string;
  isSelect: boolean;
}

export const sqliteService = {
  /**
   * Initializes or resets the in-memory SQLite database.
   */
  async initDb(): Promise<void> {
    const Sql = await getSqlInstance();
    if (dbInstance) {
      dbInstance.close();
    }
    dbInstance = new Sql.Database();
  },

  /**
   * Closes the active database instance.
   */
  closeDb(): void {
    if (dbInstance) {
      dbInstance.close();
      dbInstance = null;
    }
  },

  /**
   * Executes a multi-statement SQL script (DDL / Seeding).
   */
  async executeScript(sql: string): Promise<{ success: boolean; message: string }> {
    if (!dbInstance) {
      await this.initDb();
    }

    try {
      dbInstance.run(sql);
      return {
        success: true,
        message: 'SQL scripti başarıyla çalıştırıldı.'
      };
    } catch (err: any) {
      console.error('SQLite script execution error:', err);
      throw new Error(err.message || 'SQL çalıştırılırken bilinmeyen bir SQLite hatası oluştu.');
    }
  },

  /**
   * Executes a single SQL query (SELECT or DDL/DML) and returns normalized results.
   */
  async executeQuery(sql: string): Promise<SqlQueryResult> {
    if (!dbInstance) {
      await this.initDb();
    }

    const trimmedSql = sql.trim().toLowerCase();
    const isSelect = trimmedSql.startsWith('select') || trimmedSql.startsWith('pragma') || trimmedSql.startsWith('explain');

    try {
      if (isSelect) {
        const res = dbInstance.exec(sql);
        
        if (!res || res.length === 0) {
          return {
            columns: [],
            rows: [],
            isSelect: true,
            message: 'Sorgu başarılı ancak sonuç dönmedi.'
          };
        }

        const columns = res[0].columns;
        const values = res[0].values;
        const rows = values.map((row: any[]) => {
          const rowObj: Record<string, any> = {};
          columns.forEach((col: string, idx: number) => {
            rowObj[col] = row[idx];
          });
          return rowObj;
        });

        return {
          columns,
          rows,
          isSelect: true
        };
      } else {
        // DDL / DML execute
        dbInstance.run(sql);
        const modifiedRows = dbInstance.getRowsModified();
        return {
          columns: [],
          rows: [],
          isSelect: false,
          message: `Sorgu başarıyla çalıştırıldı. Etkilenen satır sayısı: ${modifiedRows}`
        };
      }
    } catch (err: any) {
      throw new Error(err.message || 'Sorgu çalıştırılırken SQLite hatası oluştu.');
    }
  },

  /**
   * Helper to check if database is loaded and active.
   */
  isActive(): boolean {
    return dbInstance !== null;
  }
};
