namespace Namines.Core.Enums;

public enum DatabaseType
{
    MSSQL,
    PostgreSQL,
    MySQL,
    Oracle,
    SQLite,
    MariaDB
}

// Db2, Firebird, Spanner ve Redshift buradan KALDIRILDI. Listede duruyorlardı ama
// kendi DDL üreticileri yoktu: fabrika Db2'yi Oracle'a, Firebird'ü SQLite'a,
// Spanner ve Redshift'i PostgreSQL'e yönlendiriyordu. Kullanıcı "Google Spanner"
// seçip PostgreSQL DDL'i alıyordu — Spanner'da SERIAL yok, Redshift yabancı anahtar
// kısıtını zorlamaz, Db2 sözdizimi Oracle'dan farklıdır. Bir motoru hiç sunmamak,
// çalıştırılmadan fark edilmeyecek yanlış DDL vermekten iyidir.
//
// Yeniden eklemek için önce gerçek bir üretici yazılmalı; test bunu zorunlu kılıyor
// (TypeMappingTests.Every_database_type_has_its_own_generator).
