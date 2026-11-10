[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'win-arm64')]
    [string]$RuntimeIdentifier = 'win-x64'
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$projectRoot = $PSScriptRoot
$project = Join-Path $projectRoot 'src\SmartNetworkOptimizer\SmartNetworkOptimizer.csproj'
$version = '1.1.2'

function Show-SdkMissingWarning {
    $message = "Sisteminizde .NET 10 SDK bulunmadığı veya kullanılamadığı için yayın işlemi başlatılamadı.`n`nLütfen .NET 10 SDK'yı sisteminize kurun ve işlemi yeniden deneyin."
    $shown = $false

    try {
        Add-Type -AssemblyName PresentationFramework -ErrorAction Stop
        [System.Windows.MessageBox]::Show(
            $message,
            'BAYAT | Gerekli bileşen eksik',
            [System.Windows.MessageBoxButton]::OK,
            [System.Windows.MessageBoxImage]::Warning
        ) | Out-Null
        $shown = $true
    }
    catch {
        try {
            Add-Type -AssemblyName System.Windows.Forms -ErrorAction Stop
            [System.Windows.Forms.MessageBox]::Show(
                $message,
                'BAYAT | Gerekli bileşen eksik',
                [System.Windows.Forms.MessageBoxButtons]::OK,
                [System.Windows.Forms.MessageBoxIcon]::Warning
            ) | Out-Null
            $shown = $true
        }
        catch {
            Write-Warning $message -WarningAction Continue
        }
    }

    if (-not $shown) {
        Write-Warning 'Grafik uyarı penceresi açılamadı. SDK kontrolü başarısız olduğu için işlem durduruldu.' -WarningAction Continue
    }
}

function Invoke-DotNet {
    param(
        [Parameter(Mandatory)]
        [string]$Executable,

        [Parameter(Mandatory)]
        [string[]]$Arguments,

        [Parameter(Mandatory)]
        [string]$FailureMessage
    )

    & $Executable @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw $FailureMessage
    }
}

if (-not (Test-Path -LiteralPath $project -PathType Leaf)) {
    throw "Proje dosyası bulunamadı: $project"
}

# Yalnızca sistemde bulunan dotnet komutu kullanılır.
# İnternetten betik indirilmez ve SDK kurulumu yapılmaz.
$dotnetCommand = Get-Command dotnet -CommandType Application -ErrorAction SilentlyContinue |
    Select-Object -First 1

if (-not $dotnetCommand) {
    Show-SdkMissingWarning
    throw '.NET 10 SDK bulunamadı. Yayın işlemi güvenli biçimde durduruldu.'
}

$dotnetExe = $dotnetCommand.Source
if ([string]::IsNullOrWhiteSpace($dotnetExe)) {
    $dotnetExe = $dotnetCommand.Definition
}

$sdkVersion = $null
try {
    $sdkVersion = (& $dotnetExe --version 2>$null | Out-String).Trim()
    $sdkExitCode = $LASTEXITCODE
}
catch {
    $sdkExitCode = 1
}

if ($sdkExitCode -ne 0 -or $sdkVersion -notmatch '^10\.') {
    Show-SdkMissingWarning
    throw ".NET 10 SDK etkin değil veya kullanılamıyor. Yayın işlemi durduruldu."
}

$artifactRoot = Join-Path $projectRoot 'artifacts'
$outputDirectory = Join-Path $artifactRoot $RuntimeIdentifier
$zipName = "BAYAT-SmartNetworkOptimizer-v$version-$RuntimeIdentifier.zip"
$zipPath = Join-Path $artifactRoot $zipName
$hashPath = "$zipPath.sha256"

$operationId = [Guid]::NewGuid().ToString('N')
$stagingRoot = Join-Path $artifactRoot ".staging-$RuntimeIdentifier-$operationId"
$stagingOutput = Join-Path $stagingRoot $RuntimeIdentifier
$stagingZip = Join-Path $stagingRoot $zipName
$stagingHash = "$stagingZip.sha256"
$backupRoot = Join-Path $artifactRoot ".previous-$RuntimeIdentifier-$operationId"

$oldOutputMoved = $false
$oldZipMoved = $false
$oldHashMoved = $false
$newOutputInstalled = $false
$newZipInstalled = $false
$newHashInstalled = $false
$commitStarted = $false
$completed = $false

# Derleme sırasında değiştirilen ortam değişkenlerinin eski değerleri korunur.
$environmentNames = @(
    'DOTNET_CLI_TELEMETRY_OPTOUT',
    'DOTNET_NOLOGO',
    'DOTNET_SKIP_FIRST_TIME_EXPERIENCE'
)

$originalEnvironment = @{}

foreach ($name in $environmentNames) {
    $originalEnvironment[$name] = [Environment]::GetEnvironmentVariable(
        $name,
        [EnvironmentVariableTarget]::Process
    )
}

try {
    New-Item -ItemType Directory -Path $artifactRoot -Force | Out-Null
    New-Item -ItemType Directory -Path $stagingOutput -Force | Out-Null

    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    $env:DOTNET_NOLOGO = '1'
    $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'

    Write-Host "BAYAT | .NET SDK: $sdkVersion" -ForegroundColor Cyan
    Write-Host "BAYAT | $RuntimeIdentifier için NuGet bağımlılıkları geri yükleniyor..." -ForegroundColor Cyan

    Invoke-DotNet -Executable $dotnetExe `
        -Arguments @('restore', $project, '--runtime', $RuntimeIdentifier) `
        -FailureMessage 'NuGet restore başarısız. Mevcut yayın dosyaları korunmuştur.'

    Write-Host 'BAYAT | Tek dosyalı, self-contained EXE derleniyor...' -ForegroundColor Cyan

    $publishArguments = @(
        'publish',
        $project,
        '-c', 'Release',
        '-r', $RuntimeIdentifier,
        '--self-contained', 'true',
        '--no-restore',
        '-o', $stagingOutput,
        '/p:PublishSingleFile=true',
        '/p:IncludeNativeLibrariesForSelfExtract=true',
        '/p:EnableCompressionInSingleFile=true',
        '/p:PublishTrimmed=false',
        '/p:DebugType=None',
        '/p:DebugSymbols=false'
    )

    Invoke-DotNet -Executable $dotnetExe `
        -Arguments $publishArguments `
        -FailureMessage 'EXE derlemesi başarısız. Mevcut yayın dosyaları korunmuştur.'

    $exePath = Join-Path $stagingOutput 'SmartNetworkOptimizer.exe'

    if (-not (Test-Path -LiteralPath $exePath -PathType Leaf)) {
        throw "Derleme tamamlandı ancak EXE bulunamadı: $exePath"
    }

    if ((Get-Item -LiteralPath $exePath).Length -lt 1MB) {
        throw 'EXE boyutu 1 MB altında. Şüpheli veya eksik çıktı nedeniyle yayın durduruldu.'
    }

    Write-Host 'BAYAT | ZIP paketi hazırlanıyor...' -ForegroundColor Cyan

    Compress-Archive `
        -Path (Join-Path $stagingOutput '*') `
        -DestinationPath $stagingZip `
        -CompressionLevel Optimal

    if (-not (Test-Path -LiteralPath $stagingZip -PathType Leaf)) {
        throw 'ZIP paketi oluşturulamadı.'
    }

    $hash = (Get-FileHash -LiteralPath $stagingZip -Algorithm SHA256).Hash.ToLowerInvariant()
    Set-Content -LiteralPath $stagingHash `
        -Value "$hash  $zipName" `
        -Encoding ascii

    # Yeni çıktılar tamamen hazır olmadan mevcut yayın dosyalarına dokunulmaz.
    New-Item -ItemType Directory -Path $backupRoot -Force | Out-Null
    $commitStarted = $true

    if (Test-Path -LiteralPath $outputDirectory) {
        Move-Item -LiteralPath $outputDirectory -Destination (Join-Path $backupRoot $RuntimeIdentifier)
        $oldOutputMoved = $true
    }

    if (Test-Path -LiteralPath $zipPath) {
        Move-Item -LiteralPath $zipPath -Destination (Join-Path $backupRoot $zipName)
        $oldZipMoved = $true
    }

    if (Test-Path -LiteralPath $hashPath) {
        Move-Item -LiteralPath $hashPath -Destination (Join-Path $backupRoot "$zipName.sha256")
        $oldHashMoved = $true
    }

    Move-Item -LiteralPath $stagingOutput -Destination $outputDirectory
    $newOutputInstalled = $true

    Move-Item -LiteralPath $stagingZip -Destination $zipPath
    $newZipInstalled = $true

    Move-Item -LiteralPath $stagingHash -Destination $hashPath
    $newHashInstalled = $true

    $completed = $true

    Write-Host ''
    Write-Host 'BAYAT | DERLEME VE PAKETLEME TAMAMLANDI' -ForegroundColor Green
    Write-Host "EXE : $outputDirectory\SmartNetworkOptimizer.exe"
    Write-Host "ZIP : $zipPath"
    Write-Host "SHA : $hashPath"
    Write-Host "SHA256: $hash"
    Write-Host 'Not: EXE kod imzalı değildir. Dağıtımdan önce temiz Windows sistemlerinde test edin.' -ForegroundColor Yellow
}
catch {
    $originalError = $_
    $rollbackErrors = @()

    if ($commitStarted) {
        if ($newHashInstalled -and (Test-Path -LiteralPath $hashPath)) {
            try {
                Remove-Item -LiteralPath $hashPath -Force
            }
            catch {
                $rollbackErrors += "Yeni SHA256 dosyası kaldırılamadı: $($_.Exception.Message)"
            }
        }

        if ($newZipInstalled -and (Test-Path -LiteralPath $zipPath)) {
            try {
                Remove-Item -LiteralPath $zipPath -Force
            }
            catch {
                $rollbackErrors += "Yeni ZIP dosyası kaldırılamadı: $($_.Exception.Message)"
            }
        }

        if ($newOutputInstalled -and (Test-Path -LiteralPath $outputDirectory)) {
            try {
                Remove-Item -LiteralPath $outputDirectory -Recurse -Force
            }
            catch {
                $rollbackErrors += "Yeni çıktı klasörü kaldırılamadı: $($_.Exception.Message)"
            }
        }

        if ($oldOutputMoved) {
            try {
                $oldOutputBackup = Join-Path $backupRoot $RuntimeIdentifier
                if (-not (Test-Path -LiteralPath $oldOutputBackup -PathType Container)) {
                    throw "Eski çıktı yedeği bulunamadı: $oldOutputBackup"
                }
                if (Test-Path -LiteralPath $outputDirectory) {
                    throw "Eski çıktı geri yüklenemedi; hedef konum hâlâ mevcut. Yedek korundu: $oldOutputBackup"
                }
                Move-Item -LiteralPath $oldOutputBackup -Destination $outputDirectory
            }
            catch {
                $rollbackErrors += "Eski çıktı klasörü geri yüklenemedi: $($_.Exception.Message)"
            }
        }

        if ($oldZipMoved) {
            try {
                $oldZipBackup = Join-Path $backupRoot $zipName
                if (-not (Test-Path -LiteralPath $oldZipBackup -PathType Leaf)) {
                    throw "Eski ZIP yedeği bulunamadı: $oldZipBackup"
                }
                if (Test-Path -LiteralPath $zipPath) {
                    throw "Eski ZIP geri yüklenemedi; hedef dosya hâlâ mevcut. Yedek korundu: $oldZipBackup"
                }
                Move-Item -LiteralPath $oldZipBackup -Destination $zipPath
            }
            catch {
                $rollbackErrors += "Eski ZIP dosyası geri yüklenemedi: $($_.Exception.Message)"
            }
        }

        if ($oldHashMoved) {
            try {
                $oldHashBackup = Join-Path $backupRoot "$zipName.sha256"
                if (-not (Test-Path -LiteralPath $oldHashBackup -PathType Leaf)) {
                    throw "Eski SHA256 yedeği bulunamadı: $oldHashBackup"
                }
                if (Test-Path -LiteralPath $hashPath) {
                    throw "Eski SHA256 geri yüklenemedi; hedef dosya hâlâ mevcut. Yedek korundu: $oldHashBackup"
                }
                Move-Item -LiteralPath $oldHashBackup -Destination $hashPath
            }
            catch {
                $rollbackErrors += "Eski SHA256 dosyası geri yüklenemedi: $($_.Exception.Message)"
            }
        }
    }

    Write-Error "Yayın işlemi başarısız: $($originalError.Exception.Message)" -ErrorAction Continue

    if ($rollbackErrors.Count -gt 0) {
        Write-Warning "Otomatik geri alma sırasında hata oluştu. Yedek klasörünü silmeyin: $backupRoot" -WarningAction Continue
        foreach ($rollbackError in $rollbackErrors) {
            Write-Warning $rollbackError -WarningAction Continue
        }
    }

    throw
}
finally {
    if (Test-Path -LiteralPath $stagingRoot) {
        Remove-Item -LiteralPath $stagingRoot -Recurse -Force -ErrorAction SilentlyContinue
    }

    # Derleme için değiştirilen ortam değişkenleri eski durumuna döndürülür.
    foreach ($name in $environmentNames) {
        try {
            [Environment]::SetEnvironmentVariable(
                $name,
                $originalEnvironment[$name],
                [EnvironmentVariableTarget]::Process
            )
        }
        catch {
            Write-Warning ("BAYAT | Ortam değişkeni geri yüklenemedi ({0}): {1}" -f $name, $_.Exception.Message) -WarningAction Continue
        }
    }

    # Onceki yayin yedekleri otomatik silinmez.
    # Kullanici bu yedekleri kendisi inceleyip yonetir.
    if ($completed -and ($oldOutputMoved -or $oldZipMoved -or $oldHashMoved)) {
        Write-Host "Onceki yayin yedegi korundu: $backupRoot" -ForegroundColor Yellow
    }
}