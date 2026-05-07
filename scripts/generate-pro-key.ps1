param(
    [Parameter(Mandatory = $true)]
    [string]$MachineId,

    [string]$PrivateKeyXmlPath
)

$ErrorActionPreference = "Stop"

function ConvertTo-Base64Url {
    param([byte[]]$Bytes)

    return [Convert]::ToBase64String($Bytes).TrimEnd("=").Replace("+", "-").Replace("/", "_")
}

$privateXml = $null
if (-not [string]::IsNullOrWhiteSpace($PrivateKeyXmlPath)) {
    if (-not (Test-Path $PrivateKeyXmlPath)) {
        throw "Private key file not found: $PrivateKeyXmlPath"
    }

    $privateXml = Get-Content -Path $PrivateKeyXmlPath -Raw
} elseif (-not [string]::IsNullOrWhiteSpace($env:FLUXRAM_LICENSE_PRIVATE_KEY_B64)) {
    $privateXml = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($env:FLUXRAM_LICENSE_PRIVATE_KEY_B64))
} else {
    throw "Provide -PrivateKeyXmlPath or set FLUXRAM_LICENSE_PRIVATE_KEY_B64."
}

$payload = [ordered]@{
    version = 1
    product = "FluxRAM"
    edition = "Pro"
    machineId = $MachineId.Trim().ToUpperInvariant()
    issuedAt = [DateTimeOffset]::UtcNow.ToString("o")
}

$payloadJson = $payload | ConvertTo-Json -Compress
$payloadBytes = [Text.Encoding]::UTF8.GetBytes($payloadJson)

$rsa = New-Object System.Security.Cryptography.RSACryptoServiceProvider(2048)
$rsa.FromXmlString($privateXml)
$signatureBytes = $rsa.SignData($payloadBytes, "SHA256")

$licenseKey = "FLX1-" + (ConvertTo-Base64Url $payloadBytes) + "." + (ConvertTo-Base64Url $signatureBytes)
Write-Output $licenseKey
