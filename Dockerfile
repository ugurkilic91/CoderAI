FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
# Önce yalnızca proje dosyalarını kopyalayıp paketleri geri yüklüyoruz:
# kaynak kod değiştiğinde bu katman Docker önbelleğinden gelir.
COPY Directory.Build.props Directory.Packages.props ./
COPY src/Domain/Domain.csproj src/Domain/
COPY src/Application/Application.csproj src/Application/
COPY src/Infrastructure/Infrastructure.csproj src/Infrastructure/
COPY src/Web/Web.csproj src/Web/
RUN dotnet restore src/Web/Web.csproj
COPY src/ src/
RUN dotnet publish src/Web/Web.csproj -c Release -o /app/publish --no-restore

# Derleme/test sandbox'ı için Docker istemcisi (sandbox konteynerlerini ana makinenin Docker'ı çalıştırır).
FROM docker:29.8.1-cli AS docker-cli

FROM mcr.microsoft.com/dotnet/aspnet:10.0
# git: değişiklik takibi (projelerin dışında tutulan gizli depo) için gerekli.
RUN apt-get update \
    && apt-get install -y --no-install-recommends git \
    && rm -rf /var/lib/apt/lists/*
COPY --from=docker-cli /usr/local/bin/docker /usr/local/bin/docker
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "DevAgents.Web.dll"]
