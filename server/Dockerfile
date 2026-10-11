# Doggo.Api image. Build from the server/ directory:
#   docker build -t doggo-api -f Dockerfile .
# A one-shot migration target shares the build stage:
#   docker build -t doggo-migrator --target migrator -f Dockerfile .
# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props ./
COPY src/Doggo.Domain/Doggo.Domain.csproj src/Doggo.Domain/
COPY src/Doggo.Infrastructure/Doggo.Infrastructure.csproj src/Doggo.Infrastructure/
COPY src/Doggo.Api/Doggo.Api.csproj src/Doggo.Api/
RUN dotnet restore src/Doggo.Api/Doggo.Api.csproj
COPY src/ src/
RUN dotnet publish src/Doggo.Api/Doggo.Api.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS app
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Doggo.Api.dll"]

# One-shot EF Core migration runner. Needs ConnectionStrings__Default and
# exits non-zero when the schema update fails.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS migrator
WORKDIR /src
COPY Directory.Build.props ./
COPY src/ src/
RUN dotnet restore src/Doggo.Infrastructure/Doggo.Infrastructure.csproj && \
    dotnet restore src/Doggo.Api/Doggo.Api.csproj
RUN dotnet tool install --global dotnet-ef --version 10.0.0
ENV PATH="/root/.dotnet/tools:${PATH}"
ENTRYPOINT ["dotnet", "ef", "database", "update", "--project", "src/Doggo.Infrastructure", "--startup-project", "src/Doggo.Api"]
