# Multi-stage build producing two images from one Dockerfile:
#   docker build --target api    -t leadpilot-api .
#   docker build --target worker -t leadpilot-worker .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props LeadPilot.slnx ./
COPY src/LeadPilot.Domain/*.csproj src/LeadPilot.Domain/
COPY src/LeadPilot.Application/*.csproj src/LeadPilot.Application/
COPY src/LeadPilot.Infrastructure/*.csproj src/LeadPilot.Infrastructure/
COPY src/LeadPilot.API/*.csproj src/LeadPilot.API/
COPY src/LeadPilot.Worker/*.csproj src/LeadPilot.Worker/
RUN dotnet restore src/LeadPilot.API/LeadPilot.API.csproj && dotnet restore src/LeadPilot.Worker/LeadPilot.Worker.csproj
COPY src/ src/
RUN dotnet publish src/LeadPilot.API/LeadPilot.API.csproj -c Release -o /out/api --no-restore \
 && dotnet publish src/LeadPilot.Worker/LeadPilot.Worker.csproj -c Release -o /out/worker --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime-base
RUN groupadd --system leadpilot && useradd --system --gid leadpilot --home /app leadpilot \
 && mkdir -p /var/lib/leadpilot/keys && chown -R leadpilot:leadpilot /var/lib/leadpilot
ENV ASPNETCORE_ENVIRONMENT=Production DOTNET_EnableDiagnostics=0
WORKDIR /app

FROM runtime-base AS api
COPY --from=build /out/api .
USER leadpilot
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "LeadPilot.API.dll"]

FROM runtime-base AS worker
COPY --from=build /out/worker .
USER leadpilot
ENTRYPOINT ["dotnet", "LeadPilot.Worker.dll"]
