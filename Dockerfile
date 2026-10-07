FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY src/DevAgents.Api/DevAgents.Api.csproj src/DevAgents.Api/
RUN dotnet restore src/DevAgents.Api/DevAgents.Api.csproj
COPY src/ src/
RUN dotnet publish src/DevAgents.Api/DevAgents.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "DevAgents.Api.dll"]
