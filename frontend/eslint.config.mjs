import { defineConfig, globalIgnores } from "eslint/config";
import nextVitals from "eslint-config-next/core-web-vitals";
import nextTs from "eslint-config-next/typescript";

const eslintConfig = defineConfig([
  ...nextVitals,
  ...nextTs,
  // Override default ignores of eslint-config-next.
  globalIgnores([
    // Default ignores of eslint-config-next:
    ".next/**",
    "out/**",
    "build/**",
    "next-env.d.ts",
  ]),
  {
    rules: {
      /**
       * `set-state-in-effect` → HATA DEĞİL, UYARI. (B-57)
       *
       * **Ölçüm:** bu kural depoda 16 yerde tetikliyor. Hepsine tek tek
       * bakıldı; baskın desen "monte olurken veri çek, gelince state'e yaz".
       *
       * **Kuralın neyi yakalayamadığı:** kural yalnızca söz dizimine değil,
       * çağrı grafiğine de bakıyor ve `await`'in ÖTESİNİ ayırt edemiyor.
       * Yani şu tamamen doğru kod da işaretleniyor:
       *
       *     useEffect(() => { fetchTeam().finally(() => setIsLoading(false)); }, []);
       *
       * Burada senkron hiçbir `setState` yok — `fetchTeam` ilk satırında
       * `await` ediyor. Buna rağmen "cascading render" hatası veriliyor.
       * Ölçülmüş durum: TeamModal ve CrossDatabasePanel bu biçime
       * dönüştürüldü (gerçek bir kusur da düzeldi: modal yeniden açılınca
       * ÖNCEKİ verinin bir kare görünmesi), kural yine hata verdi.
       *
       * **Neden kural tümden kapatılmadı:** kuralın haklı olduğu yerler de
       * vardı ve düzeltildi — `ThemeToggleButton` ile `ProductionScreen`
       * artık `useSyncExternalStore` kullanıyor, `CommandPalette` sıfırlamayı
       * efektten olay işleyicisine taşıdı, `Header`'daki senkronizasyon
       * efekti gereksizdi ve silindi, `CanvasSearch`/`CommandPalette`/
       * `TeamModal`/`CrossDatabasePanel` kapalıyken artık hiç monte
       * edilmiyor. Kural uyarı olarak kalınca bu tür yeni bulgular görünür
       * oluyor; hata olarak kalsa CI'a lint eklemek imkânsız olurdu ve o
       * zaman HİÇBİR kural CI'da çalışmazdı.
       *
       * Kalan 16 yerin dökümü: docs/FRONTEND-LINT-BORCU.md
       */
      "react-hooks/set-state-in-effect": "warn",
    },
  },
]);

export default eslintConfig;
