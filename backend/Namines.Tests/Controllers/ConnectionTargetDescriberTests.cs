using Namines.Core;
using Namines.Core.Enums;
using Xunit;

namespace Namines.Tests.Controllers;

public class ConnectionTargetDescriberTests
{
    [Fact]
    public void Extracts_host_and_database_from_postgres_connection_string()
    {
        var (host, database) = ConnectionTargetDescriber.Describe(
            "Host=db.internal;Port=5432;Database=namines_control;Username=namines;Password=secret",
            DatabaseType.PostgreSQL);

        Assert.Equal("db.internal", host);
        Assert.Equal("namines_control", database);
    }

    [Fact]
    public void Never_returns_the_password()
    {
        var (host, database) = ConnectionTargetDescriber.Describe(
            "Host=db.internal;Port=5432;Database=namines_control;Username=namines;Password=super-secret-value",
            DatabaseType.PostgreSQL);

        Assert.DoesNotContain("super-secret-value", host ?? "");
        Assert.DoesNotContain("super-secret-value", database ?? "");
    }

    [Fact]
    public void Returns_nulls_for_a_null_connection_string()
    {
        var (host, database) = ConnectionTargetDescriber.Describe(null, DatabaseType.PostgreSQL);
        Assert.Null(host);
        Assert.Null(database);
    }
}
