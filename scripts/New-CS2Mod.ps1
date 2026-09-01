<#
Scaffolds a new CS2 UI+C# mod folder under CS2_mods by cloning MidnightToggle
as a template and renaming every mod-specific identifier.

MidnightToggle itself is ~99% generic create-csii-ui-mod boilerplate; only
Mod.cs, Systems/*.cs, mod.json, the .csproj identity, and src/ are per-mod.
This script clones the boilerplate as-is and renames just those pieces.

Usage:
    .\scripts\New-CS2Mod.ps1 -Name FreeCamPlus
#>
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[A-Z][A-Za-z0-9]*$')]
    [string]$Name
)

$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $PSScriptRoot
$Template = Join-Path $Root "MidnightToggle"
$Dest = Join-Path $Root $Name

if (Test-Path $Dest) {
    throw "Folder '$Name' already exists at $Dest"
}
if (-not (Test-Path $Template)) {
    throw "Template folder not found at $Template"
}

# PascalCase -> kebab-case, e.g. FreeCamPlus -> free-cam-plus
$Kebab = ([regex]::Replace($Name, '(?<!^)([A-Z])', '-$1')).ToLower()

Write-Host "Cloning template MidnightToggle -> $Name" -ForegroundColor Cyan
robocopy $Template $Dest /E /XD node_modules bin obj Library /NFL /NDL /NJH /NJS | Out-Null

# --- rename mod-specific files ---
Rename-Item (Join-Path $Dest "MidnightToggle.csproj") "$Name.csproj"
Rename-Item (Join-Path $Dest "Systems\MidnightToggleUISystem.cs") "${Name}UISystem.cs"
Rename-Item (Join-Path $Dest "src\mods\midnight-toggle.tsx") "$Kebab.tsx"
Remove-Item (Join-Path $Dest "HOWTO.md") -ErrorAction SilentlyContinue

Rename-Item (Join-Path $Dest "icon\midnightToggle.png") "toggle-off.png"
Rename-Item (Join-Path $Dest "icon\midnightToggleOn.png") "toggle-on.png"

# --- token replacement across the mod-specific text files ---
$targets = @(
    "Mod.cs",
    "Systems\${Name}UISystem.cs",
    "$Name.csproj",
    "mod.json",
    "src\index.tsx",
    "src\mods\$Kebab.tsx"
)

foreach ($rel in $targets) {
    $path = Join-Path $Dest $rel
    $text = Get-Content $path -Raw
    $text = $text.Replace("MidnightToggle", $Name)
    $text = $text.Replace("midnight-toggle", $Kebab)
    $text = $text.Replace("midnightToggle.png", "toggle-off.png")
    $text = $text.Replace("midnightToggleOn.png", "toggle-on.png")
    Set-Content $path $text -NoNewline -Encoding utf8
}

Write-Host "`nCreated $Dest" -ForegroundColor Green
Write-Host "Next steps:"
Write-Host "  cd $Name"
Write-Host "  npm install"
Write-Host "  npm run build"
Write-Host "  dotnet build"
Write-Host "`nicon\toggle-off.png / toggle-on.png are just renamed copies of the MidnightToggle art - swap in real icons before shipping."
