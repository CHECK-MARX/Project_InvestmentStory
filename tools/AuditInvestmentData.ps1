param(
    [string]$DatabasePath = "$env:LOCALAPPDATA\InvestmentStory\investment_story.db"
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path -LiteralPath $DatabasePath)) {
    Write-Output "Database: $DatabasePath"
    Write-Output "Status: not found"
    exit 0
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot "InvestmentStory.Tools.DbAudit\InvestmentStory.Tools.DbAudit.csproj"

if (Test-Path -LiteralPath $project) {
    dotnet run --project $project -- "$DatabasePath"
    exit $LASTEXITCODE
}

Write-Output "Database: $DatabasePath"
Write-Output "Status: audit console project is not installed; use DatabaseAuditService from tests/app."
