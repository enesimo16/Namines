using System;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;

namespace Namines.API.Controllers;

/// <summary>
/// Veritabanı hatasının GERÇEKTEN bir benzersizlik ihlali olduğunu ayırt eder.
///
/// <c>catch (DbUpdateException)</c> her veritabanı hatasını "çakışma" sayıyordu:
/// bağlantı kopması ya da kısıt dışı bir hata da "zaten var" gibi yorumlanıp
/// sessizce yutuluyordu. Postgres'te benzersizlik ihlali SQLSTATE 23505'tir.
/// </summary>
internal static class DbUniqueViolation
{
    private const string PostgresUniqueViolation = "23505";

    public static bool IsUniqueViolation(Exception ex) =>
        ex is DbUpdateException { InnerException: DbException { SqlState: PostgresUniqueViolation } };
}
