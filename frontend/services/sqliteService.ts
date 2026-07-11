import initSqlJs from 'sql.js';
import localforage from 'localforage';

let SQL: any = null;
let sqlPromise: Promise<any> | null = null;
let dbInstance: any = null;

/**
 * sql.js WASM kütüphanesini yükler.
 * - In-flight promise CACHE'lenir → eşzamanlı çağrılar tek init'i paylaşır (yarış/"Setup Error" önlenir).
 * - Önce YEREL bundled /sql-wasm.wasm denenir (güvenilir, offline çalışır); sonra CDN fallback.
 */
async function getSqlInstance() {
  if (SQL) return SQL;
  if (sqlPromise) return sqlPromise;

  const version = '1.14.1';
  sqlPromise = (async () => {
    // Try 1: local public folder (bundled — en güvenilir)
    try {
      SQL = await initSqlJs({ locateFile: (file: string) => `/${file}` });
      console.log('✔ sql.js loaded from local /public');
      return SQL;
    } catch (errLocal) {
      console.warn('Local sql.js load failed, trying CDN...', errLocal);
    }
    // Try 2: jsdelivr CDN
    try {
      SQL = await initSqlJs({ locateFile: (file: string) => `https://cdn.jsdelivr.net/npm/sql.js@${version}/dist/${file}` });
      console.log('✔ sql.js loaded from jsdelivr CDN');
      return SQL;
    } catch (err1) {
      console.warn('jsdelivr failed, trying unpkg...', err1);
    }
    // Try 3: unpkg CDN
    try {
      SQL = await initSqlJs({ locateFile: (file: string) => `https://unpkg.com/sql.js@${version}/dist/${file}` });
      console.log('✔ sql.js loaded from unpkg CDN');
      return SQL;
    } catch (err2) {
      sqlPromise = null; // başarısız → sonraki çağrıda yeniden denenebilsin
      console.error('All sql-wasm.wasm loads failed:', err2);
      throw err2;
    }
  })();

  return sqlPromise;
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
        message: 'SQL script executed successfully.'
      };
    } catch (err: any) {
      console.error('SQLite script execution error:', err);
      throw new Error(err.message || 'An unknown SQLite error occurred while executing SQL.');
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
            message: 'Query successful but returned no results.'
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
          message: `Query executed successfully. Affected rows: ${modifiedRows}`
        };
      }
    } catch (err: any) {
      throw new Error(err.message || 'An SQLite error occurred while executing the query.');
    }
  },

  /**
   * Helper to check if database is loaded and active.
   */
  isActive(): boolean {
    return dbInstance !== null;
  },

  /**
   * Exports and saves the SQLite database binary to IndexedDB.
   */
  async saveToIndexedDb(): Promise<void> {
    if (dbInstance) {
      try {
        const binary = dbInstance.export();
        await localforage.setItem('namines-sqlite-db-binary', binary);
        console.log('✔ SQLite Wasm database saved to IndexedDB successfully.');
      } catch (err) {
        console.error('Failed to save SQLite Wasm database to IndexedDB:', err);
      }
    }
  },

  /**
   * Restores the SQLite database from IndexedDB binary.
   */
  async loadFromIndexedDb(): Promise<boolean> {
    try {
      const binary = await localforage.getItem<Uint8Array>('namines-sqlite-db-binary');
      if (binary) {
        const Sql = await getSqlInstance();
        if (dbInstance) {
          dbInstance.close();
        }
        dbInstance = new Sql.Database(binary);
        console.log('✔ SQLite Wasm database successfully restored from IndexedDB.');
        return true;
      }
    } catch (err) {
      console.error('Failed to load SQLite Wasm database from IndexedDB:', err);
    }
    return false;
  },

  /**
   * Clears the saved database binary from IndexedDB.
   */
  async clearSavedDb(): Promise<void> {
    try {
      await localforage.removeItem('namines-sqlite-db-binary');
      console.log('✔ SQLite Wasm database cleared from IndexedDB.');
    } catch (err) {
      console.error('Failed to clear SQLite Wasm database from IndexedDB:', err);
    }
  }
};
