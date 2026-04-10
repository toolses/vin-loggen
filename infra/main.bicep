// ── VinLoggen – Azure Static Web Apps ────────────────────────────────────────
// Free tier: includes hosting for both the Angular SPA and the .NET Azure Functions.
// Deploy via: az deployment group create --resource-group <rg> --template-file main.bicep

@description('Name of the Static Web App resource.')
param appName string = 'vinloggen'

@description('Azure region. Static Web Apps are globally distributed; this affects the management plane only.')
param location string = 'westeurope'

@description('Git repository URL (e.g. https://github.com/org/vinloggen).')
param repositoryUrl string = ''

@description('Branch to deploy from.')
param branch string = 'main'

// ── Static Web App ────────────────────────────────────────────────────────────
resource staticWebApp 'Microsoft.Web/staticSites@2023-01-01' = {
  name: appName
  location: location
  sku: {
    name: 'Free'
    tier: 'Free'
  }
  properties: {
    repositoryUrl: repositoryUrl
    branch: branch
    buildProperties: {
      appLocation: 'client'
      apiLocation: 'api'
      outputLocation: 'dist/vin-loggen/browser'
    }
  }
}

// ── Outputs ───────────────────────────────────────────────────────────────────
output defaultHostname string = staticWebApp.properties.defaultHostname
output resourceId string = staticWebApp.id
