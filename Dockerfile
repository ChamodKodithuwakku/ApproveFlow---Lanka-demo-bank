# ApproveFlow container image (used for the public demo on Render, and usable on any Docker host).
# Build:  docker build -t approveflow .
# Run:    docker run -p 8080:8080 -e Jwt__SigningKey=<48+ random chars> -e Seed__DemoPassword=ApproveFlow@2026 approveflow

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY database/ database/
COPY src/ src/
RUN dotnet restore src/ApproveFlow.Web/ApproveFlow.Web.csproj
RUN dotnet publish src/ApproveFlow.Web/ApproveFlow.Web.csproj --configuration Release --no-restore --output /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

# Demo environment: SQLite database and log files under /tmp (writable by the non-root "app" user).
# The database is re-created and re-seeded whenever the container starts, so the demo always resets to clean data.
# Behind a TLS-terminating proxy (Render, App Service, ...): trust X-Forwarded-* and redirect plain HTTP to 443.
ENV ASPNETCORE_ENVIRONMENT=Demo \
    ASPNETCORE_HTTP_PORTS=8080 \
    ASPNETCORE_FORWARDEDHEADERS_ENABLED=true \
    ASPNETCORE_HTTPS_PORT=443 \
    ConnectionStrings__Sqlite="Data Source=/tmp/approveflow-demo.db" \
    Serilog__WriteTo__1__Args__path=/tmp/logs/approveflow-.log

EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "ApproveFlow.Web.dll"]
