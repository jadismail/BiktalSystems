#Requires -RunAsAdministrator
$ErrorActionPreference = 'Stop'

$pgBin = 'C:\Program Files\PostgreSQL\18\bin'
$pgData = 'C:\Program Files\PostgreSQL\18\data'
$pgHba = Join-Path $pgData 'pg_hba.conf'
$serviceName = 'postgresql-x64-18'
$newPassword = 'postgres'
$dbName = 'biktal'

if (-not (Test-Path $pgHba)) {
    throw "PostgreSQL config not found at $pgHba"
}

$backup = "$pgHba.biktal-backup"
Copy-Item $pgHba $backup -Force

try {
    $content = Get-Content $pgHba -Raw
    $content = $content -replace '(?m)^(local\s+all\s+all\s+)scram-sha-256', '$1trust'
    $content = $content -replace '(?m)^(host\s+all\s+all\s+127\.0\.0\.1/32\s+)scram-sha-256', '$1trust'
    $content = $content -replace '(?m)^(host\s+all\s+all\s+::1/128\s+)scram-sha-256', '$1trust'
    Set-Content -Path $pgHba -Value $content -NoNewline

    Restart-Service $serviceName

    $psql = Join-Path $pgBin 'psql.exe'
    & $psql -U postgres -h localhost -p 5432 -d postgres -v ON_ERROR_STOP=1 -c "ALTER USER postgres WITH PASSWORD '$newPassword';"
    $dbExists = & $psql -U postgres -h localhost -p 5432 -d postgres -tAc "SELECT 1 FROM pg_database WHERE datname = '$dbName';"
    if ($dbExists.Trim() -ne '1') {
        & $psql -U postgres -h localhost -p 5432 -d postgres -v ON_ERROR_STOP=1 -c "CREATE DATABASE $dbName;"
    }

    Copy-Item $backup $pgHba -Force
    Restart-Service $serviceName

    $env:PGPASSWORD = $newPassword
    & $psql -U postgres -h localhost -p 5432 -d $dbName -v ON_ERROR_STOP=1 -c 'SELECT 1;'

    Write-Host "PostgreSQL password reset to '$newPassword' and database '$dbName' is ready."
}
catch {
    if (Test-Path $backup) {
        Copy-Item $backup $pgHba -Force
        Restart-Service $serviceName -ErrorAction SilentlyContinue
    }
    throw
}
