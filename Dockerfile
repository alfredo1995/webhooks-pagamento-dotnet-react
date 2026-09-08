# ---------- build ----------
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Manifestos primeiro: o restore so refaz quando uma dependencia muda.
COPY Directory.Build.props ./
COPY SabemiPagamentos.sln ./
COPY src/Sabemi.Pagamentos.Domain/Sabemi.Pagamentos.Domain.csproj src/Sabemi.Pagamentos.Domain/
COPY src/Sabemi.Pagamentos.Application/Sabemi.Pagamentos.Application.csproj src/Sabemi.Pagamentos.Application/
COPY src/Sabemi.Pagamentos.Infrastructure/Sabemi.Pagamentos.Infrastructure.csproj src/Sabemi.Pagamentos.Infrastructure/
COPY src/Sabemi.Pagamentos.Api/Sabemi.Pagamentos.Api.csproj src/Sabemi.Pagamentos.Api/
COPY tests/Directory.Build.props tests/
COPY tests/Sabemi.Pagamentos.UnitTests/Sabemi.Pagamentos.UnitTests.csproj tests/Sabemi.Pagamentos.UnitTests/
COPY tests/Sabemi.Pagamentos.IntegrationTests/Sabemi.Pagamentos.IntegrationTests.csproj tests/Sabemi.Pagamentos.IntegrationTests/
RUN dotnet restore SabemiPagamentos.sln

COPY . .
RUN dotnet publish src/Sabemi.Pagamentos.Api/Sabemi.Pagamentos.Api.csproj -c Release -o /app/publish --no-restore

# ---------- runtime ----------
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

USER $APP_UID

COPY --from=build /app/publish .

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "Sabemi.Pagamentos.Api.dll"]
