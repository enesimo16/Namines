FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Proje dosyalarını kopyala
COPY ["Directory.Build.props", "./"]
COPY ["Namines.API/Namines.API.csproj", "Namines.API/"]
COPY ["Namines.Core/Namines.Core.csproj", "Namines.Core/"]
COPY ["Namines.Infrastructure/Namines.Infrastructure.csproj", "Namines.Infrastructure/"]
COPY ["Namines.Vault/Namines.Vault.csproj", "Namines.Vault/"]
COPY ["Namines.Ground/Namines.Ground.csproj", "Namines.Ground/"]
COPY ["Namines.Flow/Namines.Flow.csproj", "Namines.Flow/"]
COPY ["Namines.Desk/Namines.Desk.csproj", "Namines.Desk/"]

# Bağımlılıkları restore et
RUN dotnet restore "Namines.API/Namines.API.csproj"

# Tüm kodları kopyala ve publish et
COPY . .
WORKDIR "/src/Namines.API"
RUN dotnet publish "Namines.API.csproj" -c Release -o /app/publish

# Runtime imajı
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

# curl: healthcheck için
RUN apt-get update && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

# Kalıcı data klasörü (Vault yedekleri vb.) ve Serilog'un göreli `logs/` dizini.
# Süreç root DEĞİL (aşağıda USER app) — bu yüzden ikisi de app kullanıcısına ait olmalı;
# yoksa yedek/log yazımı izin hatasıyla patlar. Yeni volume imajdaki sahipliği devralır;
# root sahipli ESKİ bir volume varsa bir kez: docker compose run --rm -u root --entrypoint chown namines-backend -R app:app /app/data
RUN mkdir -p /app/data /app/logs && chown -R app:app /app/data /app/logs

# Publish çıktısını kopyala
COPY --from=build /app/publish .

# Kestrel 8080'de dinlesin.
# ASPNETCORE_URLS şart: '+' tüm arayüzlere bind eder. 'localhost' olsaydı container
# dışından erişilemezdi.
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

# Bağlantı dizesi (ve parolası) imaja GÖMÜLMEZ — `docker history` ile okunabilirdi.
# Çalışma zamanında ConnectionStrings__DefaultConnection ortam değişkeniyle verilir
# (bkz. docker-compose*.yml).

# aspnet:8.0 imajındaki ayrıcalıksız kullanıcı (UID 1654); 8080 > 1024 olduğu için root gerekmez.
USER app

ENTRYPOINT ["dotnet", "Namines.API.dll"]
