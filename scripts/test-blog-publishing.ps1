param(
    [string]$BaseUrl = 'https://trophy.guru',
    [string]$ArticlePath
)
$ErrorActionPreference = 'Stop'
$uri = [Uri]$BaseUrl
if ($uri.Scheme -ne 'https' -or $uri.UserInfo -or $uri.Query -or $uri.Fragment) {
    throw 'Use an HTTPS site origin without credentials, query or fragment.'
}
if (-not $env:BLOG_PUBLISH_TOKEN -or $env:BLOG_PUBLISH_TOKEN.Length -lt 32) {
    throw 'Set BLOG_PUBLISH_TOKEN in this process environment before testing.'
}
$payload = @{ mode = 'test' }
if ($ArticlePath) {
    $payload = Get-Content -LiteralPath $ArticlePath -Raw | ConvertFrom-Json
    $payload | Add-Member -NotePropertyName mode -NotePropertyValue validate -Force
}
$response = Invoke-RestMethod -Uri ($BaseUrl.TrimEnd('/') + '/api/webhooks/writesonic') -Method Post -ContentType 'application/json' -Headers @{
    Authorization = 'Bearer ' + $env:BLOG_PUBLISH_TOKEN
} -Body ([Text.Encoding]::UTF8.GetBytes(($payload | ConvertTo-Json -Depth 12)))
if ($response.published -ne $false) { throw 'The validation endpoint returned an unexpected publication state.' }
$response | Select-Object status, published, url, warnings
