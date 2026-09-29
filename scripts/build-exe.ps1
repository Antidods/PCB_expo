#requires -Version 5.1
[CmdletBinding()]
param(
    [string]$DotnetPath,
    [switch]$IncludeTemplate
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ($env:OS -ne 'Windows_NT') { throw 'Сборка предназначена для Windows x64.' }
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$template = Join-Path $repository '150x100.cxdlpv4'
if ($IncludeTemplate -and -not (Test-Path -LiteralPath $template -PathType Leaf)) {
    throw 'Для -IncludeTemplate поместите 150x100.cxdlpv4 в корень репозитория.'
}

if ($DotnetPath) {
    $candidates = @((Get-Command $DotnetPath -ErrorAction Stop).Source)
} else {
    $candidates = @()
    if ($env:DOTNET_ROOT) { $candidates += Join-Path $env:DOTNET_ROOT 'dotnet.exe' }
    if ($env:ProgramFiles) { $candidates += Join-Path $env:ProgramFiles 'dotnet/dotnet.exe' }
    $onPath = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($onPath) { $candidates += $onPath.Source }
}
$dotnet = $null
foreach ($candidate in ($candidates | Select-Object -Unique)) {
    if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) { continue }
    $sdks = & $candidate --list-sdks
    if ($LASTEXITCODE -eq 0 -and ($sdks -match '^10\.\d+\.\d+\s+\[')) {
        $dotnet = $candidate
        break
    }
}
if (-not $dotnet) { throw 'Не найден .NET 10 SDK. Установите SDK x64 или укажите -DotnetPath к dotnet.exe с этим SDK.' }

# Каждый запуск создаёт новый каталог; предыдущие сборки и исходники не удаляются.
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$buildName = 'PcbExpo-win-x64-' + $stamp + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8)
$buildDirectory = Join-Path $repository ('artifacts/' + $buildName)
$publishDirectory = Join-Path $buildDirectory 'app'
$checksDirectory = Join-Path $buildDirectory 'checks'
New-Item -ItemType Directory -Path $publishDirectory, $checksDirectory | Out-Null

Push-Location $repository
try {
    Write-Host "SDK: $dotnet"
    & $dotnet test PcbExpo.slnx -c Release -r win-x64 --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Тесты не пройдены; сборка exe остановлена.' }

    & $dotnet publish src/PcbExpo.App/PcbExpo.App.csproj -p:PublishProfile=Windows-x64 -o $publishDirectory --nologo
    if ($LASTEXITCODE -ne 0) { throw 'dotnet publish завершился с ошибкой.' }
    $executable = Join-Path $publishDirectory 'PcbExpo.App.exe'
    if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) { throw 'В результате публикации отсутствует PcbExpo.App.exe.' }

    $stdout = Join-Path $checksDirectory 'runtime.stdout.log'
    $stderr = Join-Path $checksDirectory 'runtime.stderr.log'
    $process = Start-Process -FilePath $executable -ArgumentList '--check-runtime' -WorkingDirectory $publishDirectory `
        -RedirectStandardOutput $stdout -RedirectStandardError $stderr -WindowStyle Hidden -PassThru
    try {
        if (-not $process.WaitForExit(60000)) {
            $process.Kill()
            throw 'Проверка запуска exe превысила 60 секунд.'
        }
        $process.WaitForExit()
        if ($process.ExitCode -ne 0) {
            Get-Content -LiteralPath $stderr -Encoding UTF8 | Write-Host
            throw "Проверка exe завершилась с кодом $($process.ExitCode). Журнал: $stderr"
        }
    } finally { $process.Dispose() }
    Get-Content -LiteralPath $stdout -Encoding UTF8 | Write-Host

    Copy-Item -LiteralPath (Join-Path $repository 'README.md') -Destination $publishDirectory
    if ($IncludeTemplate) { Copy-Item -LiteralPath $template -Destination $publishDirectory }
    $archive = Join-Path $buildDirectory ($buildName + '.zip')
    Compress-Archive -Path (Join-Path $publishDirectory '*') -DestinationPath $archive
    Get-FileHash -LiteralPath $executable, $archive -Algorithm SHA256 |
        Select-Object Path, Hash | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $buildDirectory 'sha256.json') -Encoding UTF8
    Write-Host "EXE: $executable"
    Write-Host "ZIP: $archive"
} finally { Pop-Location }
