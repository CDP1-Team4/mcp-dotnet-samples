#!/usr/bin/env bash
set -euo pipefail

# register-app.sh
#
# Registers an Entra ID (Azure AD) application for the OneDrive Download MCP sample.
#
# Features:
# - Display name: mcp-onedrivedownload-<random 4 digits>
# - Adds Microsoft Graph delegate permissions: Files.Read.All, email, offline_access, openid, profile
#
# Requirements: Azure CLI (az)

OUT_FILE=""

msg() { echo -e "[register-app] $*"; }
err() { echo -e "[register-app][error] $*" >&2; }

print_usage() {
  cat <<USAGE
Usage: $0 [--out-file <file>] | [-o <file>]

Options:
  -o, --out-file <file>   Write JSON output to the specified file instead of stdout.
                          (The file will be overwritten if it exists.)
  -h, --help              Show this help.
USAGE
}

# Parse arguments
while [[ $# -gt 0 ]]; do
  case "$1" in
    -o|--out-file)
      [[ $# -lt 2 ]] && { err "Missing filename after $1"; exit 1; }
      OUT_FILE="$2"; shift 2 ;;
    -h|--help)
      print_usage; exit 0 ;;
    *)
      err "Unknown argument: $1"; print_usage; exit 1 ;;
  esac
done

command -v az >/dev/null 2>&1 || { err "Azure CLI (az) is required."; exit 1; }

# Ensure we're logged in
if ! az account show >/dev/null 2>&1; then
  msg "Logging into Azure..."
  az login 1>/dev/null
fi

TENANT_ID=common
msg "Using tenant: $TENANT_ID"

GRAPH_APP_ID="00000003-0000-0000-c000-000000000000" # Microsoft Graph
REDIRECT_URIS="['http://localhost','http://127.0.0.1','https://vscode.dev/redirect']"
MAX_ATTEMPTS=5

# Resolve email role ID (delegated permission)
EMAIL_ROLE_ID=$(az ad sp show --id "$GRAPH_APP_ID" --query "oauth2PermissionScopes[?value=='email' && type=='User'].id" -o tsv)
if [[ -z "$EMAIL_ROLE_ID" ]]; then
  err "Could not resolve email delegate permission ID from Microsoft Graph SP."
  exit 1
fi
msg "Resolved email delegate permission ID: $EMAIL_ROLE_ID"

# Resolve offline_access role ID (delegated permission)
OFFLINE_ACCESS_ROLE_ID=$(az ad sp show --id "$GRAPH_APP_ID" --query "oauth2PermissionScopes[?value=='offline_access' && type=='User'].id" -o tsv)
if [[ -z "$OFFLINE_ACCESS_ROLE_ID" ]]; then
  err "Could not resolve offline_access delegate permission ID from Microsoft Graph SP."
  exit 1
fi
msg "Resolved offline_access delegate permission ID: $OFFLINE_ACCESS_ROLE_ID"

# Resolve openid role ID (delegated permission)
OPENID_ROLE_ID=$(az ad sp show --id "$GRAPH_APP_ID" --query "oauth2PermissionScopes[?value=='openid' && type=='User'].id" -o tsv)
if [[ -z "$OPENID_ROLE_ID" ]]; then
  err "Could not resolve openid delegate permission ID from Microsoft Graph SP."
  exit 1
fi
msg "Resolved openid delegate permission ID: $OPENID_ROLE_ID"

# Resolve profile role ID (delegated permission)
PROFILE_ROLE_ID=$(az ad sp show --id "$GRAPH_APP_ID" --query "oauth2PermissionScopes[?value=='profile' && type=='User'].id" -o tsv)
if [[ -z "$PROFILE_ROLE_ID" ]]; then
  err "Could not resolve profile delegate permission ID from Microsoft Graph SP."
  exit 1
fi
msg "Resolved profile delegate permission ID: $PROFILE_ROLE_ID"

# Resolve Files.Read.All role ID (delegated permission)
FILES_READ_ALL_ROLE_ID=$(az ad sp show --id "$GRAPH_APP_ID" --query "oauth2PermissionScopes[?value=='Files.Read.All' && type=='User'].id" -o tsv)
if [[ -z "$FILES_READ_ALL_ROLE_ID" ]]; then
  err "Could not resolve Files.Read.All delegate permission ID from Microsoft Graph SP."
  exit 1
fi
msg "Resolved Files.Read.All delegate permission ID: $FILES_READ_ALL_ROLE_ID"

# Generate a unique app name
attempt=1
while :; do
  SUFFIX=$(printf "%04d" $(( RANDOM % 10000 )) )
  APP_NAME="mcp-onedrivedownload-${SUFFIX}"
  EXISTS=$(az ad app list --display-name "$APP_NAME" --query "[0].appId" -o tsv || true)
  if [[ -z "$EXISTS" ]]; then
    break
  fi
  if (( attempt >= MAX_ATTEMPTS )); then
    err "Failed to find unique app name after $MAX_ATTEMPTS attempts. Try again."
    exit 1
  fi
  attempt=$(( attempt + 1 ))
  sleep 1
done
msg "App display name will be: $APP_NAME"

# Create the app registration
msg "Creating app registration..."
APP_CREATE_OUTPUT=$(az ad app create \
  --display-name "$APP_NAME" \
  --sign-in-audience AzureADandPersonalMicrosoftAccount \
  --is-fallback-public-client true \
  -o json)

APP_ID=$(echo "$APP_CREATE_OUTPUT" | az jq -r '.appId' 2>/dev/null || echo "$APP_CREATE_OUTPUT" | grep -o '"appId": *"[^"]*"' | head -n1 | cut -d '"' -f4)
APP_OBJECT_ID=$(echo "$APP_CREATE_OUTPUT" | az jq -r '.id' 2>/dev/null || echo "$APP_CREATE_OUTPUT" | grep -o '"id": *"[^"]*"' | head -n1 | cut -d '"' -f4)

if [[ -z "$APP_ID" || -z "$APP_OBJECT_ID" ]]; then
  err "Failed to parse app creation output. Raw output: $APP_CREATE_OUTPUT"
  exit 1
fi
msg "Created app. Application (client) ID: $APP_ID"
msg "App object ID: $APP_OBJECT_ID"

# Apply Public Client redirect URI
msg "Applying public client redirect URI via update..."
az ad app update --id "$APP_ID" --set "publicClient={'redirectUris':$REDIRECT_URIS}" 1>/dev/null

# Prepare requiredResourceAccess JSON dynamically
REQ_JSON=$(cat <<EOF
[
  {
    "resourceAppId": "$GRAPH_APP_ID",
    "resourceAccess": [
      { "id": "$EMAIL_ROLE_ID", "type": "Scope" },
      { "id": "$OFFLINE_ACCESS_ROLE_ID", "type": "Scope" },
      { "id": "$OPENID_ROLE_ID", "type": "Scope" },
      { "id": "$PROFILE_ROLE_ID", "type": "Scope" },
      { "id": "$FILES_READ_ALL_ROLE_ID", "type": "Scope" }
    ]
  }
]
EOF
)

REQ_FILE=$(mktemp)
trap 'rm -f "$REQ_FILE"' EXIT
printf '%s' "$REQ_JSON" > "$REQ_FILE"

msg "Updating app with required resource access (Files.SelectedOperations.Selected)..."
az ad app update --id "$APP_ID" --required-resource-accesses @"$REQ_FILE" 1>/dev/null

# Create service principal so the app can be used with permissions
msg "Ensuring service principal exists..."
az ad sp create --id "$APP_ID" 1>/dev/null || true
SP_OBJECT_ID=$(az ad sp show --id "$APP_ID" --query id -o tsv || true)
msg "Service principal object ID: ${SP_OBJECT_ID:-<unknown>}"

# Simple JSON escaping for quotes and backslashes
json_escape() {
  local s="$1"
  s="${s//\\/\\\\}"   # escape backslashes
  s="${s//\"/\\\"}"   # escape quotes
  s="${s//$'\n'/ }"       # replace newlines with space (shouldn't occur)
  printf '%s' "$s"
}

JSON_OUTPUT=$(cat <<JSON
{
  "displayName": "$(json_escape "$APP_NAME")",
  "tenantId": "$(json_escape "$TENANT_ID")",
  "clientId": "$(json_escape "$APP_ID")"
}
JSON
)

if [[ -n "$OUT_FILE" ]]; then
  printf '%s\n' "$JSON_OUTPUT" > "$OUT_FILE"
  msg "JSON output written to $OUT_FILE. Secure this file and consider restricting permissions (e.g. chmod 600)."
else
  printf '%s\n' "$JSON_OUTPUT"
  msg "JSON output emitted above."

fi
