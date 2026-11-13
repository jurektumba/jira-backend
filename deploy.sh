#!/usr/bin/env bash
set -euo pipefail
cd -- "$(dirname -- "${BASH_SOURCE[0]}")"

SUBSCRIPTION_ID="${SUBSCRIPTION_ID:-d9cbd201-81d6-4884-840f-3f29b7fd7ad7}"
RESOURCE_GROUP="${RESOURCE_GROUP:-rg-hackathon-jira}"
LOCATION="${LOCATION:-eastus2}"
SKU="${SKU:-F1}"
[[ "$SKU" == "F1" || "$SKU" == "B1" ]] || { echo "ERROR: SKU debe ser F1 o B1." >&2; exit 1; }
PLAN_NAME="${PLAN_NAME:-plan-jira-${SKU,,}-${LOCATION}}"
APP_NAME="${APP_NAME:-jira-g09-${SUBSCRIPTION_ID%%-*}-${LOCATION}}"
JIRA_EMAIL="${JIRA_EMAIL:-jurektumba@gmail.com}"
JIRA_BASE_URL="${JIRA_BASE_URL:-https://intellihack.atlassian.net}"
JIRA_PROJECT_KEY="${JIRA_PROJECT_KEY:-KAN}"
JIRA_ISSUE_TYPE="${JIRA_ISSUE_TYPE:-Tarea}"
CORS_ORIGIN="${CORS_ORIGIN:-http://localhost:5173}"

fail() { printf 'ERROR: %s\n' "$*" >&2; exit 1; }
for cmd in az curl unzip python3; do
  command -v "$cmd" >/dev/null || fail "Falta el comando $cmd. Usa Azure Cloud Shell (Bash)."
done
[[ -s app.zip ]] || fail "Falta app.zip. Extrae el paquete jira-backend-azure.zip completo."
unzip -tq app.zip >/dev/null || fail "app.zip esta danado."
unzip -Z1 app.zip | grep -Fxq 'JiraBackend.dll' || fail "app.zip no contiene la API publicada."

if [[ -z "${JIRA_API_TOKEN:-}" && -f token ]]; then
  JIRA_API_TOKEN="$(tr -d '\r\n' < token)"
fi
if [[ -z "${JIRA_API_TOKEN:-}" ]]; then
  read -rsp 'Pega tu API token de Jira (no se mostrara): ' JIRA_API_TOKEN
  printf '\n'
fi
[[ -n "$JIRA_API_TOKEN" ]] || fail "El token no puede estar vacio."

az account show --output none || fail "Inicia sesion con az login."
az account set --subscription "$SUBSCRIPTION_ID"
echo "Suscripcion seleccionada:"
az account show --query '{nombre:name,id:id}' --output table
RUNTIMES="$(az webapp list-runtimes --os linux --output tsv)"
grep -Eq 'DOTNETCORE[:|]10[.]0' <<< "$RUNTIMES" ||
  fail "App Service no anuncia .NET 10. No se crearon recursos; revisa az webapp list-runtimes --os linux."

echo "Destino: $APP_NAME | region: $LOCATION | SKU: $SKU"
if [[ "$SKU" == "B1" ]]; then
  echo "AVISO: B1 es de pago. Detener la app no detiene la facturacion del plan."
fi
GROUP_EXISTS="$(az group exists --name "$RESOURCE_GROUP" --output tsv)"
if [[ "$GROUP_EXISTS" == "true" ]]; then
  echo "Reutilizando grupo existente: $RESOURCE_GROUP (sin cambiar su ubicacion)."
elif [[ "$GROUP_EXISTS" == "false" ]]; then
  az group create --name "$RESOURCE_GROUP" --location "$LOCATION" --output none
else
  fail "No se pudo determinar si existe el grupo $RESOURCE_GROUP."
fi
PLAN_NAMES="$(az appservice plan list -g "$RESOURCE_GROUP" --query '[].name' -o tsv)"
if grep -Fxq "$PLAN_NAME" <<< "$PLAN_NAMES"; then
  PLAN_INFO="$(az appservice plan show -g "$RESOURCE_GROUP" -n "$PLAN_NAME" -o json)"
  PLAN_INFO="$PLAN_INFO" TARGET_LOCATION="$LOCATION" TARGET_SKU="$SKU" python3 -c '
import json, os, sys
p = json.loads(os.environ["PLAN_INFO"])
valid = (p["sku"]["name"] == os.environ["TARGET_SKU"] and p.get("reserved") is True
         and p["location"].replace(" ", "").lower() == os.environ["TARGET_LOCATION"].lower())
if not valid:
    sys.exit("ERROR: El plan existente no coincide con SKU, Linux o region. Usa otro PLAN_NAME.")
'
else
  if ! az appservice plan create -g "$RESOURCE_GROUP" -n "$PLAN_NAME" \
      --location "$LOCATION" --is-linux --sku "$SKU" --number-of-workers 1 --output none; then
    fail "Azure rechazo el plan $SKU. Revisa el error anterior y solicita la cuota correspondiente a soporte. No se intentaran otros planes automaticamente."
  fi
fi

APP_NAMES="$(az webapp list -g "$RESOURCE_GROUP" --query '[].name' -o tsv)"
PLAN_ID="$(az appservice plan show -g "$RESOURCE_GROUP" -n "$PLAN_NAME" --query id -o tsv)"
if grep -Fxq "$APP_NAME" <<< "$APP_NAMES"; then
  APP_PLAN="$(az webapp show -g "$RESOURCE_GROUP" -n "$APP_NAME" --query serverFarmId -o tsv)"
  [[ "${APP_PLAN,,}" == "${PLAN_ID,,}" ]] ||
    fail "La app existente usa otro plan. Define otro APP_NAME."
else
  az webapp create -g "$RESOURCE_GROUP" -n "$APP_NAME" --plan "$PLAN_NAME" \
    --runtime 'DOTNETCORE:10.0' --output none
fi

az webapp update -g "$RESOURCE_GROUP" -n "$APP_NAME" --https-only true --output none
az webapp config appsettings set -g "$RESOURCE_GROUP" -n "$APP_NAME" --output none \
  --settings "SCM_DO_BUILD_DURING_DEPLOYMENT=false" \
    "Jira__Email=$JIRA_EMAIL" "Jira__ApiToken=$JIRA_API_TOKEN" \
    "Jira__BaseUrl=$JIRA_BASE_URL" "Jira__ProjectKey=$JIRA_PROJECT_KEY" \
    "Jira__IssueType=$JIRA_ISSUE_TYPE" "Cors__AllowedOrigins__0=$CORS_ORIGIN"
unset JIRA_API_TOKEN
az webapp config set -g "$RESOURCE_GROUP" -n "$APP_NAME" \
  --startup-file 'dotnet JiraBackend.dll' --output none
az webapp deploy -g "$RESOURCE_GROUP" -n "$APP_NAME" --src-path app.zip --type zip --output none

HOST="$(az webapp show -g "$RESOURCE_GROUP" -n "$APP_NAME" --query defaultHostName -o tsv)"
[[ -n "$HOST" && "$HOST" != "null" ]] || fail "Azure no devolvio el hostname."
URL="https://$HOST"
echo "Esperando a que arranque $URL ..."
for ((i=1; i<=24; i++)); do
  if BODY="$(curl --connect-timeout 10 --max-time 20 -fsS "$URL/api/health")"; then
    if python3 -c 'import json,sys; sys.exit(0 if json.load(sys.stdin).get("status") == "ok" else 1)' <<< "$BODY"; then
      echo "API verificada: $URL/api/health"
      echo "Crear tickets: POST $URL/api/tickets"
      exit 0
    fi
  fi
  sleep 10
done
fail "La API no paso health. Revisa Azure Portal > App Service > Log stream. App: $APP_NAME, grupo: $RESOURCE_GROUP."
