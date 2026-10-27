# syntax=docker/dockerfile:1

# HR & Payroll as one image: the Angular app built, then served by the .NET API from its wwwroot.
#
#   docker build -t hr-payroll .
#   docker run -p 8080:8080 -p 5092:5092 -e PORT=8080 -e DATABASE_URL=postgres://… -e Auth__SigningKey=… hr-payroll
#
# On Railway: the API listens on Railway's PORT (the HTTPS domain), and on the attendance device's
# own port 5092 (Attendance__DevicePort), which a Railway TCP Proxy exposes for the device's plain
# HTTP. See docs/deploy-railway.md.

# ---- 1. The Angular app ----
FROM node:22-bookworm-slim AS app
WORKDIR /src/frontend
ENV NG_CLI_ANALYTICS=false CI=1
COPY frontend/package.json frontend/package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY frontend/ ./
# Through scripts/build.mjs: `ng build` itself never exits after building (see the script).
RUN node scripts/build.mjs --configuration production --output-path /app-dist

# ---- 2. The API, with the app in its wwwroot ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS api
WORKDIR /src
# The projects alone first, so a code change does not download the packages again.
COPY backend/src/HrPayroll.Domain/HrPayroll.Domain.csproj backend/src/HrPayroll.Domain/
COPY backend/src/HrPayroll.Application/HrPayroll.Application.csproj backend/src/HrPayroll.Application/
COPY backend/src/HrPayroll.Infrastructure/HrPayroll.Infrastructure.csproj backend/src/HrPayroll.Infrastructure/
COPY backend/src/HrPayroll.Api/HrPayroll.Api.csproj backend/src/HrPayroll.Api/
RUN dotnet restore backend/src/HrPayroll.Api/HrPayroll.Api.csproj
COPY backend/src/ backend/src/
RUN dotnet publish backend/src/HrPayroll.Api/HrPayroll.Api.csproj -c Release -o /publish --no-restore /p:UseAppHost=false
COPY --from=app /app-dist/browser/ /publish/wwwroot/

# ---- 3. The runtime ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=api /publish ./

# Production; behind the platform's proxy, the client's own address and scheme from its
# forwarded headers (the sign-in limits are per address). PORT, the database, the signing key and
# the first manager come from the platform's variables, never from this file.
ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_FORWARDEDHEADERS_ENABLED=true \
    PORT=8080 \
    Attendance__DevicePort=5092

# The web port (PORT) and the attendance device's own port.
EXPOSE 8080 5092

USER $APP_UID
ENTRYPOINT ["dotnet", "HrPayroll.Api.dll"]
