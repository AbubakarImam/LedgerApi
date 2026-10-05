#!/usr/bin/env bash
# Issue, rotate, revoke and list per-visitor demo API keys for the Azure deployment.
#
#   scripts/demo-key.sh issue <name>    new key for one visitor (client id demo-<name>)
#   scripts/demo-key.sh rotate <name>   replace that visitor's key
#   scripts/demo-key.sh revoke <name>   remove that visitor's access
#   scripts/demo-key.sh list            show the configured API clients
#
# Keys are saved to your macOS Keychain (service "LedgerApi API key", account demo-<name>) and are never
# printed; only the SHA-256 hash goes to App Service (DESIGN.md decision #35). Every change restarts the
# app, so the API is unavailable for about a minute. Requires: az (signed in), openssl, curl, macOS security.
set -euo pipefail
export PYTHONWARNINGS=ignore # silences Python warnings printed by the Azure CLI

RESOURCE_GROUP="${RESOURCE_GROUP:-ledger-rg}"
APP_NAME="${APP_NAME:-ledgerapi}"
KEYCHAIN_SERVICE="LedgerApi API key"
DEMO_SCOPES=(ledger.read ledger.accounts ledger.transfer ledger.funding ledger.reverse) # no ledger.admin
PROBE_PATH="/api/accounts/NGN100000001" # a seeded funding account, readable with ledger.read

die() { echo "error: $*" >&2; exit 1; }
usage() { sed -n '4,7p' "$0" | sed 's/^# *//'; exit 1; }

# Only demo-<name> clients are ever touched, so payment-api and ops-console cannot be changed by mistake.
client_id_for() {
  [[ "$1" =~ ^[a-z0-9][a-z0-9-]{0,29}$ ]] || die "name must be 1-30 lowercase letters, digits or hyphens"
  echo "demo-$1"
}

app_url() { echo "https://$(az webapp show -g "$RESOURCE_GROUP" -n "$APP_NAME" --query defaultHostName -o tsv)"; }

# N in ApiKeys__Clients__N__ClientId for this client id, or empty if it has no key.
index_of() {
  az webapp config appsettings list -g "$RESOURCE_GROUP" -n "$APP_NAME" \
    --query "[?starts_with(name, 'ApiKeys__Clients__') && ends_with(name, '__ClientId') && value=='$1'].name | [0]" -o tsv \
    | sed -E 's/^ApiKeys__Clients__([0-9]+)__ClientId$/\1/'
}

# One past the highest index in use (gaps left by revoked clients are fine: the config binder skips them).
next_index() {
  az webapp config appsettings list -g "$RESOURCE_GROUP" -n "$APP_NAME" \
    --query "[?starts_with(name, 'ApiKeys__Clients__') && ends_with(name, '__ClientId')].name" -o tsv \
    | sed -E 's/^ApiKeys__Clients__([0-9]+)__ClientId$/\1/' | sort -n | tail -1 \
    | awk '{ print $1 + 1 } END { if (NR == 0) print 0 }'
}

new_key() { echo "lk_$(openssl rand -base64 32 | tr '+/' '-_' | tr -d '=')"; }
hash_of() { printf %s "$1" | openssl dgst -sha256 -binary | base64; }
keychain_save() { security add-generic-password -U -a "$1" -s "$KEYCHAIN_SERVICE" -w "$2"; }
keychain_read() { security find-generic-password -a "$1" -s "$KEYCHAIN_SERVICE" -w 2>/dev/null; }
keychain_delete() { security delete-generic-password -a "$1" -s "$KEYCHAIN_SERVICE" >/dev/null 2>&1 || true; }

# The app restarts after every settings change and the old instance keeps answering for a minute or two,
# so poll (up to 5 minutes) until a request with this key gets the expected status.
wait_for_status() {
  local key=$1 expected=$2 url
  url="$(app_url)$PROBE_PATH"
  for _ in $(seq 1 30); do
    [ "$(curl -s -o /dev/null --max-time 30 -w '%{http_code}' -H "X-Api-Key: $key" "$url")" = "$expected" ] && return 0
    sleep 10
  done
  die "timed out waiting for HTTP $expected from $url"
}

issue() {
  local client i key
  client=$(client_id_for "$1")
  [ -z "$(index_of "$client")" ] || die "$client already has a key; use rotate or revoke"
  i=$(next_index)
  key=$(new_key)
  keychain_save "$client" "$key" # saved first, so the key is never lost if a later step fails

  local settings=("ApiKeys__Clients__${i}__ClientId=$client" "ApiKeys__Clients__${i}__KeyHash=$(hash_of "$key")")
  for j in "${!DEMO_SCOPES[@]}"; do settings+=("ApiKeys__Clients__${i}__Scopes__${j}=${DEMO_SCOPES[$j]}"); done

  echo "Adding $client to App Service (the app restarts)..."
  az webapp config appsettings set -g "$RESOURCE_GROUP" -n "$APP_NAME" --settings "${settings[@]}" -o none
  wait_for_status "$key" 200

  echo "Done: $client is live. Its key is in your Keychain ('$KEYCHAIN_SERVICE', account $client)."
  echo "Copy it:   security find-generic-password -a $client -s \"$KEYCHAIN_SERVICE\" -w | pbcopy"
  echo "Send it privately with the Swagger link: $(app_url)/swagger"
}

rotate() {
  local client i old new
  client=$(client_id_for "$1")
  i=$(index_of "$client")
  [ -n "$i" ] || die "$client has no key; use issue"
  old=$(keychain_read "$client" || true)
  new=$(new_key)
  keychain_save "$client" "$new"

  echo "Replacing $client's key (the app restarts)..."
  az webapp config appsettings set -g "$RESOURCE_GROUP" -n "$APP_NAME" \
    --settings "ApiKeys__Clients__${i}__KeyHash=$(hash_of "$new")" -o none
  wait_for_status "$new" 200
  [ -z "$old" ] || wait_for_status "$old" 401

  echo "Done: $client has a new key and the old one no longer works. Send the new one privately."
}

revoke() {
  local client i key names
  client=$(client_id_for "$1")
  i=$(index_of "$client")
  [ -n "$i" ] || die "$client has no key"
  key=$(keychain_read "$client" || true)
  names=$(az webapp config appsettings list -g "$RESOURCE_GROUP" -n "$APP_NAME" \
    --query "[?starts_with(name, 'ApiKeys__Clients__${i}__')].name" -o tsv)

  echo "Removing $client (the app restarts)..."
  # shellcheck disable=SC2086 # one argument per setting name
  az webapp config appsettings delete -g "$RESOURCE_GROUP" -n "$APP_NAME" --setting-names $names -o none
  [ -z "$key" ] || wait_for_status "$key" 401
  keychain_delete "$client"

  echo "Done: $client's key no longer works and was removed from your Keychain."
}

list() {
  az webapp config appsettings list -g "$RESOURCE_GROUP" -n "$APP_NAME" \
    --query "[?starts_with(name, 'ApiKeys__Clients__') && ends_with(name, '__ClientId')].{setting:name, client:value}" -o table
}

command -v az >/dev/null || die "Azure CLI not found (brew install azure-cli)"
az account show >/dev/null 2>&1 || die "not signed in to Azure: run az login"

case "${1:-}" in
  issue | rotate | revoke) [ $# -eq 2 ] || usage; "$1" "$2" ;;
  list) list ;;
  *) usage ;;
esac
