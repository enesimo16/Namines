using System;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using Namines.Core.Enums;
using Namines.Core.Interfaces;
using Namines.Core.Models;

namespace Namines.Infrastructure.Services;

public class CoderAIPackagerService : ICoderAIPackager
{
    public async Task<string> PackageAsZipAsync(string appPyContent, string sqlContent, DatabaseType dbType, string projectName)
    {
        string jobId = Guid.NewGuid().ToString();
        string tempDir = Path.Combine(Path.GetTempPath(), $"namines-packager-{jobId}");
        Directory.CreateDirectory(tempDir);

        try
        {
            await File.WriteAllTextAsync(Path.Combine(tempDir, "app.py"), appPyContent);
            await File.WriteAllTextAsync(Path.Combine(tempDir, "schema.sql"), sqlContent);

            string reqContent = "streamlit\nsqlalchemy\npandas\nplotly\n";
            string dbImage = "";
            string dbPort = "";
            string dbEnv = "";
            
            switch (dbType)
            {
                case DatabaseType.PostgreSQL:
                    reqContent += "psycopg2-binary\n";
                    dbImage = "postgres:15";
                    dbPort = "5432:5432";
                    dbEnv = "      - POSTGRES_PASSWORD=Namines_Secure123!\n      - POSTGRES_DB=naminesdb";
                    break;
                case DatabaseType.MySQL:
                    reqContent += "pymysql\ncryptography\n";
                    dbImage = "mysql:8.0";
                    dbPort = "3306:3306";
                    dbEnv = "      - MYSQL_ROOT_PASSWORD=Namines_Secure123!\n      - MYSQL_DATABASE=naminesdb";
                    break;
                case DatabaseType.MSSQL:
                    reqContent += "pyodbc\n";
                    dbImage = "mcr.microsoft.com/mssql/server:2022-latest";
                    dbPort = "1433:1433";
                    dbEnv = "      - ACCEPT_EULA=Y\n      - MSSQL_SA_PASSWORD=Namines_Secure123!";
                    break;
                default:
                    dbImage = "postgres:15";
                    dbPort = "5432:5432";
                    dbEnv = "      - POSTGRES_PASSWORD=Namines_Secure123!\n      - POSTGRES_DB=naminesdb";
                    break;
            }

            await File.WriteAllTextAsync(Path.Combine(tempDir, "requirements.txt"), reqContent);

            string composeContent = $@"version: '3.8'
services:
  db:
    image: {dbImage}
    environment:
{dbEnv}
    ports:
      - ""{dbPort}""
    volumes:
      - ./schema.sql:/docker-entrypoint-initdb.d/init.sql

  admin:
    image: python:3.11-slim
    ports:
      - ""8501:8501""
    depends_on:
      - db
    volumes:
      - .:/app
    working_dir: /app
    command: sh -c ""pip install -r requirements.txt && streamlit run app.py --server.port 8501 --server.enableCORS=false""
";
            if (dbType == DatabaseType.MSSQL)
            {
                composeContent = composeContent.Replace("pip install -r requirements.txt", "apt-get update && apt-get install -y curl gnupg2 apt-transport-https unixodbc-dev && curl https://packages.microsoft.com/keys/microsoft.asc | apt-key add - && curl https://packages.microsoft.com/config/debian/11/prod.list > /etc/apt/sources.list.d/mssql-release.list && apt-get update && ACCEPT_EULA=Y apt-get install -y msodbcsql17 && pip install -r requirements.txt");
            }

            await File.WriteAllTextAsync(Path.Combine(tempDir, "docker-compose.yml"), composeContent);

            var outputsDir = Path.Combine(Directory.GetCurrentDirectory(), "Outputs");
            if (!Directory.Exists(outputsDir)) Directory.CreateDirectory(outputsDir);

            string zipPath = Path.Combine(outputsDir, $"coderai_{jobId}.zip");
            ZipFile.CreateFromDirectory(tempDir, zipPath);

            return zipPath;
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    public Task<byte[]> PackageNextJsZipAsync(DatabaseSchema schema, string projectName)
    {
        throw new NotImplementedException("Next.js Enterprise Dashboard (Premium Feature) is coming soon.");
    }
}
