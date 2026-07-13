#Requires -RunAsAdministrator
param(
    [string]$SiteName = "BiktalSystems",
    [string]$AppPoolName = "BiktalSystems",
    [string]$PhysicalPath = "D:\Systems\BiktalSystems\publish",
    [int]$Port = 8088,
    [string]$HostName = "BiktalSystems",
    [switch]$SkipHostingBundle,
    [switch]$SkipHostsEntry
)

$ErrorActionPreference = "Stop"

function Ensure-IisFeatures {
    $features = @(
        "IIS-WebServerRole",
        "IIS-WebServer",
        "IIS-CommonHttpFeatures",
        "IIS-StaticContent",
        "IIS-DefaultDocument",
        "IIS-DirectoryBrowsing",
        "IIS-HttpErrors",
        "IIS-HttpLogging",
        "IIS-RequestFiltering",
        "IIS-HttpCompressionStatic",
        "IIS-ManagementConsole",
        "IIS-HttpRedirect"
    )

    foreach ($feature in $features) {
        $state = (Get-WindowsOptionalFeature -Online -FeatureName $feature -ErrorAction SilentlyContinue).State
        if ($state -ne "Enabled") {
            Write-Host "Enabling Windows feature $feature..."
            Enable-WindowsOptionalFeature -Online -FeatureName $feature -All -NoRestart | Out-Null
        }
    }
}

function Ensure-HostingBundle {
    $ancm = "C:\Program Files\IIS\Asp.Net Core Module\V2\aspnetcorev2.dll"
    $runtime9 = Get-ChildItem "C:\Program Files\dotnet\shared\Microsoft.AspNetCore.App\9.0*" -ErrorAction SilentlyContinue | Select-Object -First 1

    if ((Test-Path $ancm) -and $runtime9) {
        Write-Host "ASP.NET Core Hosting Bundle 9.x is already installed."
        return
    }

    if ($SkipHostingBundle) {
        throw "ASP.NET Core Hosting Bundle 9.x is required. Install from https://dotnet.microsoft.com/download/dotnet/9.0 (Hosting Bundle), then rerun."
    }

    Write-Host "Downloading .NET 9 Hosting Bundle..."
    $installer = Join-Path $env:TEMP "dotnet-hosting-9.0.17-win.exe"
    Invoke-WebRequest -Uri "https://builds.dotnet.microsoft.com/dotnet/aspnetcore/Runtime/9.0.17/dotnet-hosting-9.0.17-win.exe" -OutFile $installer

    Write-Host "Installing Hosting Bundle (this may take a minute)..."
    $process = Start-Process -FilePath $installer -ArgumentList "/install", "/quiet", "/norestart" -Wait -PassThru
    if ($process.ExitCode -ne 0 -and $process.ExitCode -ne 3010) {
        throw "Hosting Bundle installer failed with exit code $($process.ExitCode)"
    }

    Write-Host "Hosting Bundle installed. Restarting IIS..."
    net stop was /y | Out-Null
    net start w3svc | Out-Null
}

function Ensure-PublishOutput {
    if (-not (Test-Path (Join-Path $PhysicalPath "Biktal.WebMVC.dll"))) {
        Write-Host "Publish output not found. Running Publish-IIS.ps1..."
        & (Join-Path $PSScriptRoot "Publish-IIS.ps1") -OutputPath $PhysicalPath
    }

    New-Item -ItemType Directory -Path (Join-Path $PhysicalPath "logs") -Force | Out-Null
}

function Set-WebConfigForIis {
    $webConfigPath = Join-Path $PhysicalPath "web.config"
    if (-not (Test-Path $webConfigPath)) {
        throw "web.config not found in $PhysicalPath. Publish the app first."
    }

    [xml]$xml = Get-Content $webConfigPath
    $aspNetCore = $xml.SelectSingleNode("//aspNetCore")
    if (-not $aspNetCore) {
        Write-Host "web.config has no aspNetCore node; skipping web.config update."
        return
    }

    $aspNetCore.SetAttribute("stdoutLogEnabled", "true")
    $aspNetCore.SetAttribute("hostingModel", "inprocess")

    $envVars = $aspNetCore.SelectSingleNode("environmentVariables")
    if (-not $envVars) {
        $envVars = $xml.CreateElement("environmentVariables")
        [void]$aspNetCore.AppendChild($envVars)
    }

    $existing = $envVars.SelectSingleNode("environmentVariable[@name='ASPNETCORE_ENVIRONMENT']")
    if ($existing) {
        $existing.SetAttribute("value", "Production")
    }
    else {
        $node = $xml.CreateElement("environmentVariable")
        $node.SetAttribute("name", "ASPNETCORE_ENVIRONMENT")
        $node.SetAttribute("value", "Production")
        [void]$envVars.AppendChild($node)
    }

    $xml.Save($webConfigPath)
}

function Ensure-HostsEntry {
    param([string]$HostEntry)

    if ($SkipHostsEntry -or -not $HostEntry -or $HostEntry -eq "localhost") {
        return
    }

    $hostsPath = "$env:Windir\System32\drivers\etc\hosts"
    $content = Get-Content $hostsPath -Raw
    $pattern = "(?m)^\s*127\.0\.0\.1\s+$([regex]::Escape($HostEntry))\s*$"

    if ($content -match $pattern) {
        Write-Host "Hosts entry for $HostEntry already exists."
        return
    }

    Write-Host "Adding hosts entry: 127.0.0.1 $HostEntry"
    Add-Content -Path $hostsPath -Value "`n127.0.0.1`t$HostEntry"
}

function Get-SslCertificate {
    param([string]$DnsName)

    $existing = Get-ChildItem Cert:\LocalMachine\My | Where-Object {
        $_.FriendlyName -eq "BiktalSystems IIS" -and $_.NotAfter -gt (Get-Date)
    } | Select-Object -First 1

    if ($existing) {
        Write-Host "Using existing SSL certificate (thumbprint $($existing.Thumbprint))."
        return Get-ChildItem "Cert:\LocalMachine\My\$($existing.Thumbprint)"
    }

    Write-Host "Creating self-signed SSL certificate for $DnsName..."
    return New-SelfSignedCertificate `
        -Subject "CN=$DnsName" `
        -DnsName @($DnsName, "localhost") `
        -CertStoreLocation "Cert:\LocalMachine\My" `
        -KeyExportPolicy Exportable `
        -KeyAlgorithm RSA `
        -KeyLength 2048 `
        -NotAfter (Get-Date).AddYears(5) `
        -FriendlyName "BiktalSystems IIS"
}

function Set-SslCertificateBinding {
    param(
        [System.Security.Cryptography.X509Certificates.X509Certificate2]$Certificate,
        [int]$Port,
        [string]$HostHeader
    )

    $thumbprint = $Certificate.Thumbprint
    $appId = "{4dc32564-0354-4aa7-9253-27893d4d49fa}"

    if ($HostHeader) {
        $target = "hostnameport=${HostHeader}:${Port}"
    }
    else {
        $target = "ipport=0.0.0.0:${Port}"
    }

    Write-Host "Binding certificate $thumbprint to $target..."
    & netsh http delete sslcert $target 2>$null | Out-Null
    $output = & netsh http add sslcert $target certhash=$thumbprint appid=$appId certstorename=MY 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to bind SSL certificate: $output"
    }
}

function Ensure-IisSite {
    param(
        [System.Security.Cryptography.X509Certificates.X509Certificate2]$Certificate
    )

    Import-Module WebAdministration

    if (-not (Test-Path "IIS:\AppPools\$AppPoolName")) {
        Write-Host "Creating app pool $AppPoolName..."
        New-WebAppPool -Name $AppPoolName | Out-Null
    }

    Set-ItemProperty "IIS:\AppPools\$AppPoolName" -Name managedRuntimeVersion -Value ""
    Set-ItemProperty "IIS:\AppPools\$AppPoolName" -Name startMode -Value "AlwaysRunning"

    if (-not (Get-Website -Name $SiteName -ErrorAction SilentlyContinue)) {
        Write-Host "Creating site $SiteName..."
        New-Website -Name $SiteName -PhysicalPath $PhysicalPath -ApplicationPool $AppPoolName -Port $Port -Force | Out-Null
    }
    else {
        Write-Host "Updating existing site $SiteName..."
        Set-ItemProperty "IIS:\Sites\$SiteName" -Name physicalPath -Value $PhysicalPath
        Set-ItemProperty "IIS:\Sites\$SiteName" -Name applicationPool -Value $AppPoolName
    }

    Write-Host "Configuring HTTPS binding on port $Port..."
    Get-WebBinding -Name $SiteName | ForEach-Object {
        Remove-WebBinding -Name $SiteName -Binding $_.bindingInformation -Protocol $_.protocol
    }

    if ($HostName) {
        New-WebBinding -Name $SiteName -Protocol "https" -Port $Port -HostHeader $HostName -SslFlags 1
    }
    else {
        New-WebBinding -Name $SiteName -Protocol "https" -Port $Port -SslFlags 0
    }

    Set-SslCertificateBinding -Certificate $Certificate -Port $Port -HostHeader $HostName

    $appPoolIdentity = "IIS AppPool\$AppPoolName"
    Write-Host "Granting read access to $appPoolIdentity..."
    icacls $PhysicalPath /grant "${appPoolIdentity}:(OI)(CI)RX" /T | Out-Null
    icacls (Join-Path $PhysicalPath "logs") /grant "${appPoolIdentity}:(OI)(CI)M" | Out-Null

    Start-WebAppPool -Name $AppPoolName
}

Ensure-IisFeatures
Ensure-HostingBundle
Ensure-PublishOutput
Set-WebConfigForIis
Ensure-HostsEntry -HostEntry $HostName
$cert = Get-SslCertificate -DnsName $HostName
Ensure-IisSite -Certificate $cert

$urlHost = if ($HostName) { $HostName } else { "localhost" }
$url = "https://${urlHost}:${Port}/"

Write-Host ""
Write-Host "IIS setup complete."
Write-Host "Site:  $SiteName"
Write-Host "URL:   $url"
Write-Host "Path:  $PhysicalPath"
Write-Host "Logs:  $PhysicalPath\logs"
Write-Host "Cert:  BiktalSystems IIS (self-signed; trust in browser if prompted)"
Write-Host ""
Write-Host "Default login: admin@biktal.local / ChangeThisPassword1!"
