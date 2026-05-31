using System.Collections.Generic;
using Namines.Core.Enums;

namespace Namines.Core.Models;

public class ContainerProfile
{
    public string Image { get; set; } = string.Empty;
    public string Tag { get; set; } = "latest";
    public Dictionary<string, string> EnvVars { get; set; } = new();
    public string ExecCmd { get; set; } = string.Empty;
}

public static class ContainerProfiles
{
    public static ContainerProfile GetProfile(DatabaseType dbType)
    {
        return dbType switch
        {
            DatabaseType.MSSQL => new ContainerProfile
            {
                Image = "mcr.microsoft.com/mssql/server",
                Tag = "2022-latest",
                EnvVars = new Dictionary<string, string>
                {
                    { "ACCEPT_EULA", "Y" },
                    { "MSSQL_SA_PASSWORD", "Namines_Secure123!" },
                    { "MSSQL_PID", "Developer" }
                },
                ExecCmd = "/opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P \"Namines_Secure123!\" -No -i /tmp/schema.sql"
            },
            DatabaseType.PostgreSQL => new ContainerProfile
            {
                Image = "postgres",
                Tag = "15-alpine",
                EnvVars = new Dictionary<string, string>
                {
                    { "POSTGRES_PASSWORD", "Namines_Secure123!" },
                    { "POSTGRES_USER", "postgres" },
                    { "POSTGRES_DB", "naminesdb" }
                },
                ExecCmd = "psql -U postgres -d naminesdb -f /tmp/schema.sql"
            },
            DatabaseType.MySQL => new ContainerProfile
            {
                Image = "mysql",
                Tag = "8.0",
                EnvVars = new Dictionary<string, string>
                {
                    { "MYSQL_ROOT_PASSWORD", "Namines_Secure123!" },
                    { "MYSQL_DATABASE", "naminesdb" }
                },
                ExecCmd = "mysql -u root -p\"Namines_Secure123!\" naminesdb < /tmp/schema.sql"
            },
            _ => throw new System.Exception("Unsupported database type for containerization")
        };
    }
}
