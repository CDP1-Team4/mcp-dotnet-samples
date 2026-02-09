<#!
.SYNOPSIS
Registers an Entra ID (Azure AD) application for the OneDrive Download MCP sample.

.DESCRIPTION
Creates a single-tenant SPA application with redirect URI http://localhost, requests
Microsoft Graph Mail.Send application permission, creates a service principal, grants
admin consent, and generates a client secret named 'default'. Outputs a JSON payload
containing identifiers and the secret value (only shown once). Optionally writes JSON
to a file via -OutFile.

.PARAMETER OutFile
Path to write JSON output instead of stdout.

.PARAMETER Help
Show this help.

.EXAMPLE
./Register-App.ps1

.EXAMPLE
./Register-App.ps1 -OutFile app-reg.json
#>
[CmdletBinding()] param(
    [Parameter(Position=0)]
    [string] $OutFile,
    [switch] $Help
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-Info { param([string]$Message) Write-Host "[register-app] $Message" }
function Write-Err  { param([string]$Message) Write-Host "[register-app][error] $Message" -ForegroundColor Red }

function Show-Usage {
@'
Usage: ./Register-App.ps1 [-OutFile <file>] [-Help]

Options:
    -OutFile <file>  Write JSON output to the specified file instead of stdout.
                     (The file will be overwritten if it exists.)
    -Help            Show this help.
'@ | Write-Host
}

if ($Help) { Show-Usage; return }

# Preconditions
if (-not (Get-Command az -ErrorAction SilentlyContinue)) {
    Write-Err 'Azure CLI (az) is required.'
    exit 1
}

# Ensure login
try {
    az account show | Out-Null
} catch {
    Write-Info 'Logging into Azure...'
    az login | Out-Null
}

$tenantId = "common"
Write-Info "Using tenant: $tenantId"

$graphAppId = '00000003-0000-0000-c000-000000000000'
$redirectUris = "['http://localhost','http://127.0.0.1','https://vscode.dev/redirect']"
$maxAttempts = 5

# Resolve email role id
$emailRoleId = az ad sp show --id $graphAppId --query "oauth2PermissionScopes[?value=='email' && type=='User'].id" -o tsv
if (-not $emailRoleId) {
    Write-Err 'Could not resolve email delegate permission ID from Microsoft Graph SP.'
    exit 1
}
Write-Info "Resolved email delegate permission ID: $emailRoleId"

# Resolve offline_access role id
$offlineAccessRoleId = az ad sp show --id $graphAppId --query "oauth2PermissionScopes[?value=='offline_access' && type=='User'].id" -o tsv
if (-not $offlineAccessRoleId) {
    Write-Err 'Could not resolve offline_access delegate permission ID from Microsoft Graph SP.'
    exit 1
}
Write-Info "Resolved offline_access delegate permission ID: $offlineAccessRoleId"

# Resolve openid role id
$openidRoleId = az ad sp show --id $graphAppId --query "oauth2PermissionScopes[?value=='openid' && type=='User'].id" -o tsv
if (-not $openidRoleId) {
    Write-Err 'Could not resolve openid delegate permission ID from Microsoft Graph SP.'
    exit 1
}
Write-Info "Resolved openid delegate permission ID: $openidRoleId"

# Resolve profile role id
$profileRoleId = az ad sp show --id $graphAppId --query "oauth2PermissionScopes[?value=='profile' && type=='User'].id" -o tsv
if (-not $profileRoleId) {
    Write-Err 'Could not resolve profile delegate permission ID from Microsoft Graph SP.'
    exit 1
}
Write-Info "Resolved profile delegate permission ID: $profileRoleId"

# Resolve Files.Read.All role id
$filesReadAllRoleId = az ad sp show --id $graphAppId --query "oauth2PermissionScopes[?value=='Files.Read.All' && type=='User'].id" -o tsv
if (-not $filesReadAllRoleId) {
    Write-Err 'Could not resolve Files.Read.All delegate permission ID from Microsoft Graph SP.'
    exit 1
}
Write-Info "Resolved Files.Read.All delegate permission ID: $filesReadAllRoleId"

# Generate unique app name
$attempt = 1
$appName = $null
while ($true) {
    $suffix = (Get-Random -Minimum 0 -Maximum 10000).ToString('0000')
    $candidate = "mcp-onedrivedownload-$suffix"
    $exists = az ad app list --display-name $candidate --query "[0].appId" -o tsv
    if (-not $exists) { $appName = $candidate; break }
    if ($attempt -ge $maxAttempts) { Write-Err "Failed to find unique app name after $maxAttempts attempts."; exit 1 }
    $attempt++
    Start-Sleep -Seconds 1
}
Write-Info "App display name will be: $appName"

# Create app registration
Write-Info 'Creating app registration...'
$appCreateJson = az ad app create --display-name $appName `
    --sign-in-audience AzureADandPersonalMicrosoftAccount `
    --is-fallback-public-client true -o json
$appCreate = $appCreateJson | ConvertFrom-Json
$appId = $appCreate.appId
$appObjectId = $appCreate.id
if (-not $appId -or -not $appObjectId) {
    Write-Err "Failed to parse app creation output. Raw: $appCreateJson"
    exit 1
}
Write-Info "Created app. Application (client) ID: $appId"
Write-Info "App object ID: $appObjectId"

# Apply Public Client redirect URI
Write-Info 'Applying public client redirect URI via update...'
az ad app update --id "$appId" --set "publicClient={'redirectUris':$redirectUris}" | Out-Null

# Prepare requiredResourceAccess
Write-Info 'Updating app with required resource access (Files.Read.All, email, offline_access, openid, profile)...'
$resourceAccess = "[{'id':'$filesReadAllRoleId','type':'Scope'}," +
                   "{'id':'$emailRoleId','type':'Scope'}," +
                   "{'id':'$offlineAccessRoleId','type':'Scope'}," +
                   "{'id':'$openidRoleId','type':'Scope'}," +
                   "{'id':'$profileRoleId','type':'Scope'}]"
az ad app update --id $appId --required-resource-accesses "[{'resourceAppId':'$graphAppId','resourceAccess':$resourceAccess}]" | Out-Null

# Create service principal (may already exist)
Write-Info 'Ensuring service principal exists...'
az ad sp create --id $appId | Out-Null
$spObjectId = az ad sp show --id $appId --query id -o tsv
Write-Info "Service principal object ID: $spObjectId"

# Build output
$result = [ordered]@{
    displayName         = $appName
    tenantId            = $tenantId
    clientId            = $appId
}
$json = $result | ConvertTo-Json -Depth 3

if ($OutFile) {
    Set-Content -Path $OutFile -Value $json -Encoding UTF8
    Write-Info "JSON output written to $OutFile. Secure this file (consider: chmod 600 $OutFile)."
} else {
    $json
    Write-Info 'JSON output emitted above.'
}
