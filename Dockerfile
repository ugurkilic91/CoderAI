FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
# Restore first, so the package layer stays cached while only source files change.
COPY src/Directory.Build.props src/
COPY src/DevAgents.Domain/DevAgents.Domain.csproj src/DevAgents.Domain/
COPY src/DevAgents.Application/DevAgents.Application.csproj src/DevAgents.Application/
COPY src/DevAgents.Infrastructure/DevAgents.Infrastructure.csproj src/DevAgents.Infrastructure/
COPY src/DevAgents.Api/DevAgents.Api.csproj src/DevAgents.Api/
RUN dotnet restore src/DevAgents.Api/DevAgents.Api.csproj
COPY src/ src/
RUN dotnet publish src/DevAgents.Api/DevAgents.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "DevAgents.Api.dll"]
