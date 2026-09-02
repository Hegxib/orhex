$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $root "orhex.exe"

$oldCerts = @(Get-ChildItem Cert:\CurrentUser\My | Where-Object { $_.Subject -like "*CN=Orhex*" -or $_.Subject -like "*CN=hegxib*" })
$oldCerts += @(Get-ChildItem Cert:\CurrentUser\Root | Where-Object { $_.Subject -like "*CN=Orhex*" -or $_.Subject -like "*CN=hegxib*" })
$oldCerts += @(Get-ChildItem Cert:\CurrentUser\TrustedPublisher | Where-Object { $_.Subject -like "*CN=Orhex*" -or $_.Subject -like "*CN=hegxib*" })
foreach ($c in ($oldCerts | Select-Object -Unique))
{
    try { Remove-Item $c.PSPath } catch { }
}

$sign = Join-Path $PSScriptRoot "orhex.pfx"
Remove-Item $sign -ErrorAction SilentlyContinue

$cert = Get-ChildItem Cert:\CurrentUser\My | Where-Object { $_.Subject -like "*CN=hegxib*" -and $_.HasPrivateKey } | Select-Object -First 1
if (-not $cert) {
    if (Test-Path $sign) {
        $cert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($sign, "OrhexSign2026!")
    }
}

if (-not $cert) {
    $cert = New-SelfSignedCertificate `
        -Type CodeSigningCert `
        -Subject "CN=hegxib" `
        -FriendlyName "hegxib Code Signing" `
        -NotAfter (Get-Date).AddYears(3) `
        -KeyExportPolicy Exportable `
        -KeyUsage DigitalSignature `
        -KeyAlgorithm RSA `
        -KeyLength 2048 `
        -CertStoreLocation "Cert:\CurrentUser\My" `
        -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3")

    $password = ConvertTo-SecureString "OrhexSign2026!" -Force -AsPlainText
    Export-PfxCertificate -Cert $cert -FilePath (Join-Path $PSScriptRoot "orhex.pfx") -Password $password | Out-Null

    $rootStore = New-Object System.Security.Cryptography.X509Certificates.X509Store("Root", "CurrentUser")
    $rootStore.Open("ReadWrite")
    $rootStore.Add($cert)
    $rootStore.Close()

    $pubStore = New-Object System.Security.Cryptography.X509Certificates.X509Store("TrustedPublisher", "CurrentUser")
    $pubStore.Open("ReadWrite")
    $pubStore.Add($cert)
    $pubStore.Close()
}

$tsServers = @(
    "http://timestamp.digicert.com",
    "https://timestamp.digicert.com",
    "http://timestamp.comodoca.com"
)

$signed = $false
foreach ($ts in $tsServers) {
    $job = Start-Job -ScriptBlock {
        param($exePath, $thumb, $tsUrl)
        $cert = Get-ChildItem Cert:\CurrentUser\My | Where-Object { $_.Thumbprint -eq $thumb -and $_.HasPrivateKey } | Select-Object -First 1
        $sig = Set-AuthenticodeSignature -FilePath $exePath -Certificate $cert -TimestampServer $tsUrl -ErrorAction Stop
        $sig.Status.ToString()
    } -ArgumentList $exe, $cert.Thumbprint, $ts

    if (Wait-Job $job -Timeout 25) {
        $out = @(Receive-Job $job)
        Remove-Job $job -Force
        if ($out -contains "Valid") { $signed = $true; break }
        Write-Host "Timestamp rejected: $ts"
    }
    else {
        Stop-Job $job -ErrorAction SilentlyContinue
        Remove-Job $job -Force
        Write-Host "Timestamp timed out: $ts"
    }
}

if (-not $signed) {
    Set-AuthenticodeSignature -FilePath $exe -Certificate $cert | Out-Null
}

Get-AuthenticodeSignature $exe | Format-List Status, StatusMessage, SignerCertificate
