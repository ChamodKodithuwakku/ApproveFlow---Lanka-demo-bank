#!/usr/bin/env bash
# One-time developer setup for ApproveFlow (Linux/macOS). Stores secrets with dotnet user-secrets.
#   ./scripts/setup-dev.sh            -> demo password + JWT key (enough for SQLite demo mode)
#   ./scripts/setup-dev.sh --docker   -> also create .env and use the docker-compose SQL Server
set -euo pipefail

root="$(cd "$(dirname "$0")/.." && pwd)"
project="$root/src/ApproveFlow.Web"
demo_password="${DEMO_PASSWORD:-ApproveFlow@2026}"

secret() { LC_ALL=C tr -dc 'A-HJ-NP-Za-km-z2-9' </dev/urandom | head -c "$1"; }
set_secret() { dotnet user-secrets set "$1" "$2" --project "$project" >/dev/null && echo "  set $1"; }

echo "Configuring user-secrets for ApproveFlow.Web..."
set_secret "Seed:DemoPassword" "$demo_password"
set_secret "Jwt:SigningKey" "$(secret 64)"

if [[ "${1:-}" == "--docker" ]]; then
  if [[ ! -f "$root/.env" ]]; then
    echo "MSSQL_SA_PASSWORD=$(secret 20)#9a" > "$root/.env"
    echo "  created .env with a random SA password"
  fi
  sa="$(grep '^MSSQL_SA_PASSWORD=' "$root/.env" | cut -d= -f2-)"
  set_secret "ConnectionStrings:DefaultConnection" "Server=localhost,1433;Database=ApproveFlow;User Id=sa;Password=$sa;TrustServerCertificate=True"
  echo "Run: docker compose up -d && dotnet run --project src/ApproveFlow.Web --launch-profile http"
else
  echo "Run: dotnet run --project src/ApproveFlow.Web --launch-profile sqlite-demo"
fi
echo "Demo accounts use the password: $demo_password"
