using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Namines.Core.Enums;
using Namines.Core.Models;
using Namines.Core.Security;
using Namines.Infrastructure.Generators.DdlGenerator;
using Namines.Infrastructure.Services;
using Xunit;

namespace Namines.Tests.Architecture;

/// <summary>
/// FAZ 5: PostgreSQL Multi-Tenant Row-Level Security (RLS) & DB Session Context Entegrasyonu testleri.
/// </summary>
public sealed class PostgreSqlRlsAndSessionContextTests
{
    private class FakeDbParameter : DbParameter
    {
        public override DbType DbType { get; set; }
        public override ParameterDirection Direction { get; set; }
        public override bool IsNullable { get; set; }
        [System.Diagnostics.CodeAnalysis.AllowNull] public override string ParameterName { get; set; } = string.Empty;
        [System.Diagnostics.CodeAnalysis.AllowNull] public override string SourceColumn { get; set; } = string.Empty;
        public override object? Value { get; set; }
        public override bool SourceColumnNullMapping { get; set; }
        public override int Size { get; set; }
        public override void ResetDbType() { }
    }

    private class FakeDbParameterCollection : DbParameterCollection
    {
        private readonly List<DbParameter> _params = new();
        public override int Count => _params.Count;
        public override object SyncRoot => this;
        public override int Add(object value) { _params.Add((DbParameter)value); return _params.Count - 1; }
        public override void AddRange(Array values) { foreach (var v in values) Add(v); }
        public override void Clear() => _params.Clear();
        public override bool Contains(object value) => _params.Contains((DbParameter)value);
        public override bool Contains(string value) => _params.Exists(p => p.ParameterName == value);
        public override void CopyTo(Array array, int index) => ((System.Collections.ICollection)_params).CopyTo(array, index);
        public override System.Collections.IEnumerator GetEnumerator() => _params.GetEnumerator();
        public override int IndexOf(object value) => _params.IndexOf((DbParameter)value);
        public override int IndexOf(string parameterName) => _params.FindIndex(p => p.ParameterName == parameterName);
        public override void Insert(int index, object value) => _params.Insert(index, (DbParameter)value);
        public override void Remove(object value) => _params.Remove((DbParameter)value);
        public override void RemoveAt(int index) => _params.RemoveAt(index);
        public override void RemoveAt(string parameterName) { var idx = IndexOf(parameterName); if (idx >= 0) _params.RemoveAt(idx); }
        protected override DbParameter GetParameter(int index) => _params[index];
        protected override DbParameter GetParameter(string parameterName) => _params.Find(p => p.ParameterName == parameterName)!;
        protected override void SetParameter(int index, DbParameter value) => _params[index] = value;
        protected override void SetParameter(string parameterName, DbParameter value) { var idx = IndexOf(parameterName); if (idx >= 0) _params[idx] = value; }
    }

    private class FakeDbCommand : DbCommand
    {
        [System.Diagnostics.CodeAnalysis.AllowNull] public override string CommandText { get; set; } = string.Empty;
        public override int CommandTimeout { get; set; }
        public override CommandType CommandType { get; set; }
        public override bool DesignTimeVisible { get; set; }
        public override UpdateRowSource UpdatedRowSource { get; set; }
        protected override DbConnection? DbConnection { get; set; }
        protected override DbParameterCollection DbParameterCollection { get; } = new FakeDbParameterCollection();
        protected override DbTransaction? DbTransaction { get; set; }
        public override void Cancel() { }
        public override int ExecuteNonQuery() => 0;
        public override Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken) => Task.FromResult(0);
        public override object? ExecuteScalar() => null;
        public override void Prepare() { }
        protected override DbParameter CreateDbParameter() => new FakeDbParameter();
        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) => throw new NotImplementedException();
    }

    private class FakeDbConnection : DbConnection
    {
        [System.Diagnostics.CodeAnalysis.AllowNull] public override string ConnectionString { get; set; } = string.Empty;
        public override string Database => "fake_db";
        public override string DataSource => "localhost";
        public override string ServerVersion => "16.0";
        public override ConnectionState State => ConnectionState.Open;
        public FakeDbCommand LastCommand { get; } = new();
        public override void ChangeDatabase(string databaseName) { }
        public override void Close() { }
        public override void Open() { }
        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => throw new NotImplementedException();
        protected override DbCommand CreateDbCommand() => LastCommand;
    }

    [Fact]
    public void PostgresDdlGenerator_Tablo_Icin_RLS_Ve_Politikalari_Uretir()
    {
        var schema = new DatabaseSchema
        {
            Name = "MultiTenantDb",
            Tables = new List<SchemaTable>
            {
                new()
                {
                    Name = "projects",
                    EnableRls = true,
                    Columns = new List<SchemaColumn>
                    {
                        new() { Name = "id", Type = "UUID", IsPK = true },
                        new() { Name = "tenant_id", Type = "UUID", IsNullable = false },
                        new() { Name = "title", Type = "VARCHAR", Length = 255 }
                    },
                    RlsPolicies = new List<SchemaRlsPolicy>
                    {
                        new()
                        {
                            Name = "tenant_isolation_policy",
                            Command = "ALL",
                            Role = "PUBLIC",
                            Using = "tenant_id = current_setting('app.current_tenant', true)::uuid",
                            WithCheck = "tenant_id = current_setting('app.current_tenant', true)::uuid"
                        },
                        new()
                        {
                            Name = "readonly_select_policy",
                            Command = "SELECT",
                            Role = "authenticated",
                            Using = "tenant_id = current_setting('app.current_tenant', true)::uuid"
                        }
                    }
                }
            }
        };

        var generator = new PostgresDdlGenerator();
        var ddl = generator.Generate(schema);

        Assert.Contains("ALTER TABLE \"projects\" ENABLE ROW LEVEL SECURITY;", ddl);
        Assert.Contains("ALTER TABLE \"projects\" FORCE ROW LEVEL SECURITY;", ddl);
        Assert.Contains("CREATE POLICY \"tenant_isolation_policy\" ON \"projects\" FOR ALL TO PUBLIC USING (tenant_id = current_setting('app.current_tenant', true)::uuid) WITH CHECK (tenant_id = current_setting('app.current_tenant', true)::uuid);", ddl);
        Assert.Contains("CREATE POLICY \"readonly_select_policy\" ON \"projects\" FOR SELECT TO authenticated USING (tenant_id = current_setting('app.current_tenant', true)::uuid);", ddl);
    }

    [Fact]
    public void Rls_Olmayan_Tablolarda_RLS_Komutlari_Uretilmez()
    {
        var schema = new DatabaseSchema
        {
            Name = "StandardDb",
            Tables = new List<SchemaTable>
            {
                new()
                {
                    Name = "users",
                    EnableRls = false,
                    Columns = new List<SchemaColumn>
                    {
                        new() { Name = "id", Type = "INT", IsPK = true },
                        new() { Name = "email", Type = "VARCHAR", Length = 100 }
                    }
                }
            }
        };

        var generator = new PostgresDdlGenerator();
        var ddl = generator.Generate(schema);

        Assert.DoesNotContain("ROW LEVEL SECURITY", ddl);
        Assert.DoesNotContain("CREATE POLICY", ddl);
    }

    [Fact]
    public void Diger_Motorlar_RLS_Kullanmaz_Ve_Bozulmaz()
    {
        var table = new SchemaTable
        {
            Name = "orders",
            EnableRls = true,
            RlsPolicies = new List<SchemaRlsPolicy>
            {
                new() { Name = "test_policy", Command = "ALL", Using = "1=1" }
            }
        };

        var sqliteRls = RlsSql.GenerateTableRls(table, DatabaseType.SQLite, s => $"\"{s}\"");
        var mysqlRls = RlsSql.GenerateTableRls(table, DatabaseType.MySQL, s => $"`{s}`");
        var mssqlRls = RlsSql.GenerateTableRls(table, DatabaseType.MSSQL, s => $"[{s}]");

        Assert.Empty(sqliteRls);
        Assert.Empty(mysqlRls);
        Assert.Empty(mssqlRls);
    }

    [Fact]
    public void DbSessionContext_ForTenant_Baglami_Eksiksiz_Turetir()
    {
        var context = DbSessionContext.ForTenant("tenant-xyz-99", "user-456", "admin");

        Assert.Equal("tenant-xyz-99", context.TenantId);
        Assert.Equal("user-456", context.UserId);
        Assert.Equal("admin", context.Role);
    }

    [Fact]
    public async Task UserDbConnection_ApplySessionContext_Postgres_Parametrelerini_Ayarliyor()
    {
        var fakeConn = new FakeDbConnection();
        var context = DbSessionContext.ForTenant("org-1234", "user-5678", "member");

        await UserDbConnection.ApplySessionContextAsync(fakeConn, "POSTGRESQL", context, CancellationToken.None);

        var cmd = fakeConn.LastCommand;
        Assert.Contains("set_config('app.current_tenant'", cmd.CommandText);
        Assert.Contains("set_config('app.user_id'", cmd.CommandText);
        Assert.Contains("set_config('app.user_role'", cmd.CommandText);

        var pTenant = Assert.IsAssignableFrom<DbParameter>(cmd.Parameters["tenant_id"]);
        Assert.Equal("org-1234", pTenant.Value);

        var pUser = Assert.IsAssignableFrom<DbParameter>(cmd.Parameters["user_id"]);
        Assert.Equal("user-5678", pUser.Value);

        var pRole = Assert.IsAssignableFrom<DbParameter>(cmd.Parameters["user_role"]);
        Assert.Equal("member", pRole.Value);
    }

    [Fact]
    public async Task UserDbConnection_Postgres_Disindaki_Motorlarda_Session_Context_Calistirilmaz()
    {
        var fakeConn = new FakeDbConnection();
        var context = DbSessionContext.ForTenant("org-1234");

        await UserDbConnection.ApplySessionContextAsync(fakeConn, "MYSQL", context, CancellationToken.None);

        Assert.Empty(fakeConn.LastCommand.CommandText);

        await UserDbConnection.ResetSessionContextAsync(fakeConn, "MYSQL", CancellationToken.None);
        Assert.Empty(fakeConn.LastCommand.CommandText);
    }

    [Fact]
    public async Task UserDbConnection_ResetSessionContext_Postgres_Parametrelerini_Sifirliyor()
    {
        var fakeConn = new FakeDbConnection();

        await UserDbConnection.ResetSessionContextAsync(fakeConn, "POSTGRESQL", CancellationToken.None);

        var cmd = fakeConn.LastCommand;
        Assert.Contains("set_config('app.current_tenant', '', false)", cmd.CommandText);
        Assert.Contains("set_config('app.user_id', '', false)", cmd.CommandText);
        Assert.Contains("set_config('app.user_role', '', false)", cmd.CommandText);
    }
    [Theory]
    [InlineData("ALL; DROP TABLE users;")]
    [InlineData("SELECT; DELETE FROM users;")]
    [InlineData("FOOBAR")]
    [InlineData("DROP TABLE")]
    public void RlsSql_Gecersiz_Komut_Verildiginde_ArgumentException_Firlatir(string maliciousCommand)
    {
        var table = new SchemaTable
        {
            Name = "orders",
            EnableRls = true,
            RlsPolicies = new List<SchemaRlsPolicy>
            {
                new() { Name = "malicious_policy", Command = maliciousCommand, Using = "1=1" }
            }
        };

        Assert.Throws<ArgumentException>(() =>
            RlsSql.GenerateTableRls(table, DatabaseType.PostgreSQL, s => $"\"{s}\""));
    }

    [Theory]
    [InlineData("PUBLIC; DROP TABLE users; --")]
    [InlineData("authenticated--")]
    [InlineData("role/*comment*/")]
    [InlineData("bad role with spaces")]
    [InlineData("bad;role")]
    public void RlsSql_Zararli_Rol_Enjeksiyonunda_ArgumentException_Firlatir(string maliciousRole)
    {
        var table = new SchemaTable
        {
            Name = "orders",
            EnableRls = true,
            RlsPolicies = new List<SchemaRlsPolicy>
            {
                new() { Name = "malicious_policy", Command = "SELECT", Role = maliciousRole, Using = "1=1" }
            }
        };

        Assert.Throws<ArgumentException>(() =>
            RlsSql.GenerateTableRls(table, DatabaseType.PostgreSQL, s => $"\"{s}\""));
    }

    [Theory]
    [InlineData("1=1); DROP TABLE orders; --", null)]
    [InlineData(null, "1=1); DROP TABLE orders; --")]
    [InlineData("tenant_id = 1; SELECT 1;", null)]
    public void RlsSql_Using_Veya_WithCheck_Nokatali_Virgul_Icerdiginde_ArgumentException_Firlatir(string? badUsing, string? badWithCheck)
    {
        var table = new SchemaTable
        {
            Name = "orders",
            EnableRls = true,
            RlsPolicies = new List<SchemaRlsPolicy>
            {
                new() { Name = "test_policy", Command = "ALL", Using = badUsing, WithCheck = badWithCheck }
            }
        };

        Assert.Throws<ArgumentException>(() =>
            RlsSql.GenerateTableRls(table, DatabaseType.PostgreSQL, s => $"\"{s}\""));
    }

    [Fact]
    public void RlsSql_Coklu_Gecerli_Rol_Dizesini_Duzenli_Uretir()
    {
        var table = new SchemaTable
        {
            Name = "orders",
            EnableRls = true,
            RlsPolicies = new List<SchemaRlsPolicy>
            {
                new() { Name = "multi_role_policy", Command = "SELECT", Role = "authenticated, app_reader, PUBLIC", Using = "1=1" }
            }
        };

        var ddl = RlsSql.GenerateTableRls(table, DatabaseType.PostgreSQL, s => $"\"{s}\"" );
        Assert.Contains("CREATE POLICY \"multi_role_policy\" ON \"orders\" FOR SELECT TO authenticated, app_reader, PUBLIC USING (1=1);", ddl);
    }
}