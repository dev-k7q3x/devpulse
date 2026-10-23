# syntax=docker/dockerfile:1
# Jeden obraz: API + SPA (wwwroot) + serwer Hangfire. Railway podaje port w zmiennej PORT.

FROM node:24-alpine AS frontend
WORKDIR /src/frontend
COPY frontend/package.json frontend/package-lock.json ./
RUN npm ci
COPY frontend/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS backend
WORKDIR /src
COPY global.json Directory.Build.props ./
COPY src/DevPulse.Domain/DevPulse.Domain.csproj src/DevPulse.Domain/
COPY src/DevPulse.Application/DevPulse.Application.csproj src/DevPulse.Application/
COPY src/DevPulse.Infrastructure/DevPulse.Infrastructure.csproj src/DevPulse.Infrastructure/
COPY src/DevPulse.Api/DevPulse.Api.csproj src/DevPulse.Api/
RUN dotnet restore src/DevPulse.Api/DevPulse.Api.csproj
COPY src/ src/
RUN dotnet publish src/DevPulse.Api/DevPulse.Api.csproj -c Release -o /app/publish --no-restore -p:UseAppHost=false
COPY --from=frontend /src/frontend/dist /app/publish/wwwroot

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=backend /app/publish ./
ENV ASPNETCORE_ENVIRONMENT=Production
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["sh", "-c", "ASPNETCORE_HTTP_PORTS=${PORT:-8080} exec dotnet DevPulse.Api.dll"]
