import type { Database, SqlJsStatic, SqlValue } from 'sql.js';
import localforage from 'localforage';
import { errorMessage } from '../lib/errors';

let SQL: SqlJsStatic | null = null;
let sqlPromise: Promise<SqlJsStatic> | null = null;
let dbInstance: Database | null = null;

/**
 * sql.js WASM kütüphanesini yükler.
 * - In-flight promise CACHE'lenir → eşzamanlı çağrılar tek init'i paylaşır (yarış/"Setup Error" önlenir).
 * - Önce YEREL bundled /sql-wasm.wasm denenir (güvenilir, offline çalışır); sonra CDN fallback.
 *
 * **Kütüphanenin kendisi de DİNAMİK yükleniyor (B-51/PERF-005).** Statik import
 * sql.js'i `/canvas`'ın ilk yüklemesine sokuyordu: ölçüm, sql.js + şablonları
 * taşıyan tek chunk'ın **385 KB** olduğunu ve rotanın 1653 KB'lık ilk
 * yüklemesinin en büyük parçası olduğunu gösterdi. Oysa bu kod yalnızca SQL
 * Explorer paneli açıldığında çalışıyor — tuvale giren çoğu kullanıcı hiç
 * açmıyor.
 *
 * `initDb()` zaten async ve panel zaten bir yükleniyor durumu gösteriyordu,
 * yani gecikme kullanıcıya YENİ bir bekleme olarak görünmüyor.
 */
async function getSqlInstance() {
  if (SQL) return SQL;
  if (sqlPromise) return sqlPromise;

  const version = '1.14.1';

  // WASM dosya adi SABIT ve `locateFile`'a gelen `file` argumani BILEREK
  // yok sayiliyor.
  //
  // NEDEN — canli calistirmada yakalanan gercek ariza: sql.js'in package
  // `exports` alani iki giris noktasi tanimliyor ve ikisi FARKLI wasm adi
  // istiyor:
  //     "browser" -> dist/sql-wasm-browser.js  ->  sql-wasm-browser.wasm
  //     "default" -> dist/sql-wasm.js          ->  sql-wasm.wasm
  // `public/` klasorunde yalnizca `sql-wasm.wasm` var. Statik import
  // "default"a, dinamik import ise "browser"a cozuluyordu; sonuc
  // `GET /sql-wasm-browser.wasm 404` ve emscripten'in
  // "both async and sync fetching of the wasm failed" hatasi. Konsol
  // hic acilmiyordu.
  //
  // Iki dosya BAYT BAYT AYNI (sha256 438c88f6…), yani adi sabitlemek bir
  // taviz degil: paketleyicinin hangi girisi sectigine olan gizli bagimliligi
  // ORTADAN KALDIRIYOR. Bu bagimlilik degisiklikten once de vardi, yalnizca
  // gorunmuyordu.
  const WASM_FILE = 'sql-wasm.wasm';

  sqlPromise = (async () => {
    const initSqlJs = (await import('sql.js')).default;
    // Try 1: local public folder (bundled — en güvenilir)
    try {
      SQL = await initSqlJs({ locateFile: () => `/${WASM_FILE}` });
      console.log('✔ sql.js loaded from local /public');
      return SQL;
    } catch (errLocal) {
      console.warn('Local sql.js load failed, trying CDN...', errLocal);
    }
    // Try 2: jsdelivr CDN
    try {
      SQL = await initSqlJs({ locateFile: () => `https://cdn.jsdelivr.net/npm/sql.js@${version}/dist/${WASM_FILE}` });
      console.log('✔ sql.js loaded from jsdelivr CDN');
      return SQL;
    } catch (err1) {
      console.warn('jsdelivr failed, trying unpkg...', err1);
    }
    // Try 3: unpkg CDN
    try {
      SQL = await initSqlJs({ locateFile: () => `https://unpkg.com/sql.js@${version}/dist/${WASM_FILE}` });
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
  rows: Record<string, SqlValue>[];
  message?: string;
  isSelect: boolean;
}

/**
 * Veritabanini gerektigi gibi baslatir ve ORNEGI DONDURUR.
 *
 * <b>Neden gerekli:</b> `if (!dbInstance) await this.initDb()` deseni
 * TypeScript'in null daraltmasini `await` sonrasinda KORUYAMIYOR -- ve bu
 * yalnizca bir tip sikayeti degildi: `initDb` bir sebeple basarisiz olursa
 * (WASM inmedi) sonraki satir `dbInstance.run` ile TypeError atiyordu, yani
 * kullanici "Cannot read properties of null" goruyordu. Simdi ACIK bir hata
 * mesaji veriliyor.
 */
async function requireDb(): Promise<Database> {
  if (!dbInstance) await sqliteService.initDb();
  if (!dbInstance) {
    throw new Error('The in-memory SQLite database could not be initialised.');
  }
  return dbInstance;
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
    const db = await requireDb();

    try {
      db.run(sql);
      return {
        success: true,
        message: 'SQL script executed successfully.'
      };
    } catch (err) {
      console.error('SQLite script execution error:', err);
      throw new Error(errorMessage(err, 'An unknown SQLite error occurred while executing SQL.'));
    }
  },

  /**
   * Executes a single SQL query (SELECT or DDL/DML) and returns normalized results.
   */
  async executeQuery(sql: string): Promise<SqlQueryResult> {
    const db = await requireDb();

    const trimmedSql = sql.trim().toLowerCase();
    const isSelect = trimmedSql.startsWith('select') || trimmedSql.startsWith('pragma') || trimmedSql.startsWith('explain');

    try {
      if (isSelect) {
        const res = db.exec(sql);
        
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
        const rows = values.map((row: SqlValue[]) => {
          const rowObj: Record<string, SqlValue> = {};
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
        db.run(sql);
        const modifiedRows = db.getRowsModified();
        return {
          columns: [],
          rows: [],
          isSelect: false,
          message: `Query executed successfully. Affected rows: ${modifiedRows}`
        };
      }
    } catch (err) {
      throw new Error(errorMessage(err, 'An SQLite error occurred while executing the query.'));
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
