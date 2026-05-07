param(
    [int]$KeySize = 2048
)

$ErrorActionPreference = "Stop"

$rsa = New-Object System.Security.Cryptography.RSACryptoServiceProvider($KeySize)
$publicXml = $rsa.ToXmlString($false)
$privateXml = $rsa.ToXmlString($true)
$privateKeyB64 = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($privateXml))

Write-Host "Public key XML for LicenseKeyVerifier.DefaultPublicKey:" -ForegroundColor Cyan
Write-Output $publicXml
Write-Host ""
Write-Host "Private key environment value. Keep this secret and do not commit it:" -ForegroundColor Yellow
Write-Output $privateKeyB64
