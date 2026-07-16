FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Proje dosyalarını kopyala
COPY ["Namines.API/Namines.API.csproj", "Namines.API/"]
COPY ["Namines.Core/Namines.Core.csproj", "Namines.Core/"]
COPY ["Namines.Infrastructure/Namines.Infrastructure.csproj", "Namines.Infrastructure/"]

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

# SQLite DB için kalıcı data klasörü
RUN mkdir -p /app/data

# Publish çıktısını kopyala
COPY --from=build /app/publish .

# Kestrel 8080'de dinlesin.
# ASPNETCORE_URLS şart: '+' tüm arayüzlere bind eder. 'localhost' olsaydı container
# dışından erişilemezdi.
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

# SQLite connection string'i /app/data klasörüne yönlendir (volume ile kalıcı)
ENV ConnectionStrings__DefaultConnection="Data Source=/app/data/namines_auth.db"

ENTRYPOINT ["dotnet", "Namines.API.dll"]
