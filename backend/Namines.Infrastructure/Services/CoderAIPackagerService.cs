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
            string dbUrl = "";

            // PAKET BASINA RASTGELE PAROLA (bkz. docs/last-review M-17):
            // uretilen her pakete AYNI sabit parola gomuluyordu ve o parola bu
            // depoda acik yazili. Yani her Namines kullanicisinin urettigi yigin
            // ayni, herkesin bildigi veritabani parolasini tasiyordu.
            var dbPassword = GeneratePassword();
            
            switch (dbType)
            {
                case DatabaseType.PostgreSQL:
                    reqContent += "psycopg2-binary\n";
                    dbImage = "postgres:15";
                    dbPort = "5432:5432";
                    dbEnv = "      - POSTGRES_PASSWORD=${DB_PASSWORD}\n      - POSTGRES_DB=naminesdb";
                    dbUrl = $"postgresql://postgres:{dbPassword}@db:5432/naminesdb";
                    break;
                case DatabaseType.MySQL:
                    reqContent += "pymysql\ncryptography\n";
                    dbImage = "mysql:8.0";
                    dbPort = "3306:3306";
                    dbEnv = "      - MYSQL_ROOT_PASSWORD=${DB_PASSWORD}\n      - MYSQL_DATABASE=naminesdb";
                    dbUrl = $"mysql+pymysql://root:{dbPassword}@db:3306/naminesdb";
                    break;
                case DatabaseType.MSSQL:
                    reqContent += "pyodbc\n";
                    dbImage = "mcr.microsoft.com/mssql/server:2022-latest";
                    dbPort = "1433:1433";
                    dbEnv = "      - ACCEPT_EULA=Y\n      - MSSQL_SA_PASSWORD=${DB_PASSWORD}";
                    dbUrl = $"mssql+pymssql://sa:{dbPassword}@db:1433/naminesdb";
                    break;
                default:
                    dbImage = "postgres:15";
                    dbPort = "5432:5432";
                    dbEnv = "      - POSTGRES_PASSWORD=${DB_PASSWORD}\n      - POSTGRES_DB=naminesdb";
                    dbUrl = $"postgresql://postgres:{dbPassword}@db:5432/naminesdb";
                    break;
            }

            await File.WriteAllTextAsync(Path.Combine(tempDir, "requirements.txt"), reqContent);

            // docker-compose `.env`i kendiliginden okuyor; paketi acan kisi ekstra
            // bir sey yapmiyor. Parola tek bir dosyada duruyor, yani degistirmek ve
            // versiyon kontrolunden dislamak kolay.
            await File.WriteAllTextAsync(Path.Combine(tempDir, ".env"),
                "# Bu paket icin URETILMIS parola. Versiyon kontrolune EKLEMEYIN.\n" +
                $"DB_PASSWORD={dbPassword}\n" +
                $"DATABASE_URL={dbUrl}\n");

            await File.WriteAllTextAsync(Path.Combine(tempDir, ".gitignore"), ".env\n");

            string composeContent = $@"version: '3.8'
services:
  db:
    image: {dbImage}
    environment:
{dbEnv}
    # 127.0.0.1e BAGLI. Onek olmadan (yalnizca PORT:PORT) Docker portu hostun TUM
    # arayuzlerine (0.0.0.0) acar; genel IPsi olan bir makinede bu, veritabanini
    # internete acmak demekti (bkz. docs/last-review M-17). Yerel bir istemciyle
    # baglanmak hala calisiyor; degisen tek sey disaridan erisilemiyor olmasi.
    ports:
      - ""127.0.0.1:{dbPort}""
    volumes:
      - ./schema.sql:/docker-entrypoint-initdb.d/init.sql

  admin:
    image: python:3.11-slim
    environment:
      - DATABASE_URL=${{DATABASE_URL}}
    ports:
      - ""127.0.0.1:8501:8501""
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

    /// <summary>
    /// Paket basina veritabani parolasi.
    ///
    /// URL-guvenli alfabe: parola bir SQLAlchemy baglanti URLsinin icine giriyor ve
    /// <c>@</c>, <c>:</c>, <c>/</c> gibi karakterler orada AYIRICI. Onlari
    /// yuzde-kodlamak yerine alfabeden cikarmak, uretilen paketin ilk denemede
    /// calismasini sagliyor. 32 karakter x 62 harfli alfabe = ~190 bit.
    /// </summary>
    private static string GeneratePassword()
    {
        const string alphabet = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        var chars = new char[32];

        for (var i = 0; i < chars.Length; i++)
            chars[i] = alphabet[System.Security.Cryptography.RandomNumberGenerator.GetInt32(alphabet.Length)];

        return new string(chars);
    }
}
