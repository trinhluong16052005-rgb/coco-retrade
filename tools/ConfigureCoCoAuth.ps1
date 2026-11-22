param([switch]$GoogleOnly, [switch]$EmailOnly)
$ErrorActionPreference = 'Stop'
if ($GoogleOnly -and $EmailOnly) { throw 'Choose either GoogleOnly or EmailOnly.' }
$repoPath = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$projectPath = Join-Path $repoPath 'src\CoCoReTrade.Web\CoCoReTrade.Web.csproj'
$dotnetPath = Join-Path $env:USERPROFILE '.dotnet\dotnet.exe'
if (!(Test-Path -LiteralPath $dotnetPath)) { $dotnetPath = (Get-Command dotnet -ErrorAction Stop).Source }
$settings = @{}
function Read-PrivateValue([string]$Prompt) {
    $secureValue = Read-Host $Prompt -AsSecureString
    $buffer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secureValue)
    try { [Runtime.InteropServices.Marshal]::PtrToStringBSTR($buffer) }
    finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($buffer); $secureValue.Dispose() }
}
try {
    if (!$GoogleOnly) {
        $senderEmail = (Read-Host 'Gmail gui OTP').Trim()
        if ($senderEmail -notmatch '^[^\s@]+@[^\s@]+\.[^\s@]+$') { throw 'Invalid sender email.' }
        $settings['Smtp:Host'] = 'smtp.gmail.com'
        $settings['Smtp:Port'] = '587'
        $settings['Smtp:From'] = $senderEmail
        $settings['Smtp:User'] = $senderEmail
        $settings['Smtp:Password'] = (Read-PrivateValue 'Gmail App Password (khong dung mat khau Gmail)').Replace(' ', '')
        if ([string]::IsNullOrWhiteSpace($settings['Smtp:Password'])) { throw 'App Password is required.' }
    }
    if (!$EmailOnly) {
        $settings['Authentication:Google:ClientId'] = (Read-Host 'Google OAuth Client ID').Trim()
        $settings['Authentication:Google:ClientSecret'] = Read-PrivateValue 'Google OAuth Client Secret'
        if ([string]::IsNullOrWhiteSpace($settings['Authentication:Google:ClientId']) -or
            [string]::IsNullOrWhiteSpace($settings['Authentication:Google:ClientSecret'])) { throw 'Google credentials are required.' }
    }
    # JSON via stdin keeps secret values out of command arguments and shell history.
    $settings | ConvertTo-Json -Compress | & $dotnetPath user-secrets set --project $projectPath
    if ($LASTEXITCODE -ne 0) { throw 'Could not save User Secrets.' }
    Write-Host 'Da luu cau hinh rieng tren may. Khoi dong lai CoCo de ap dung.'
}
finally { $settings.Clear(); Remove-Variable senderEmail -ErrorAction SilentlyContinue }
