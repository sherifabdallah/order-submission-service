# syntax=docker/dockerfile:1

# One image = the whole application: the ASP.NET Core API, the background delivery worker and
# the Angular client served from wwwroot.

# ---- Angular client ----
FROM node:24-alpine AS client
WORKDIR /src/client
COPY client/package.json client/package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY client/ ./
RUN npx ng build --configuration production --output-path /out/client

# ---- .NET API ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/OrderSubmission.Domain/OrderSubmission.Domain.csproj src/OrderSubmission.Domain/
COPY src/OrderSubmission.Application/OrderSubmission.Application.csproj src/OrderSubmission.Application/
COPY src/OrderSubmission.Infrastructure/OrderSubmission.Infrastructure.csproj src/OrderSubmission.Infrastructure/
COPY src/OrderSubmission.Api/OrderSubmission.Api.csproj src/OrderSubmission.Api/
RUN dotnet restore src/OrderSubmission.Api/OrderSubmission.Api.csproj
COPY src/ src/
RUN dotnet publish src/OrderSubmission.Api/OrderSubmission.Api.csproj -c Release --no-restore -o /app/publish

# ---- Runtime ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish ./
COPY --from=client /out/client/browser ./wwwroot
RUN mkdir -p /data && chown "$APP_UID" /data
ENV ASPNETCORE_URLS=http://+:8080 \
    ConnectionStrings__Orders="Data Source=/data/orders.db"
VOLUME /data
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "OrderSubmission.Api.dll"]
