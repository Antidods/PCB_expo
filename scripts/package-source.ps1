#requires -Version 5.1
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$ReleaseDirectory,
    [Parameter(Mandatory = $true)][string]$Ref
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$release = (Resolve-Path -LiteralPath $ReleaseDirectory -ErrorAction Stop).Path
$manifestPath = Join-Path $release 'release-manifest.json'
$manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
$version = [string]$manifest.version
if ($Ref -ne ('v' + $version)) { throw 'Тег должен соответствовать версии Windows-пакета.' }
Get-Command git -ErrorAction Stop | Out-Null
Push-Location $repository
try {
    $commit = & git rev-parse --verify ($Ref + '^{commit}')
    if ($LASTEXITCODE -ne 0) { throw 'Не найден коммит указанного тега.' }
    $tree = & git rev-parse --verify ($Ref + '^{tree}')
    if ($LASTEXITCODE -ne 0) { throw 'Не найдено дерево исходников тега.' }
    if (-not $manifest.sourceTree -or [string]$manifest.sourceTree -ne [string]$tree) {
        throw 'Дерево исходников тега не совпадает со сборкой. Подготовьте индекс Git и повторно соберите Windows-пакет.'
    }
    $properties = & git show ($Ref + ':Directory.Build.props')
    if ($LASTEXITCODE -ne 0) { throw 'В теге отсутствует Directory.Build.props.' }
    [xml]$versionProperties = $properties -join "`n"
    if ([string]$versionProperties.Project.PropertyGroup.Version -ne $version) { throw 'Версия в исходниках не совпадает с пакетом.' }
    $sourceName = 'PcbExpo-' + $version + '-source.zip'
    $sourceArchive = Join-Path $release $sourceName
    if (Test-Path -LiteralPath $sourceArchive) { throw 'Архив исходников уже существует; выберите новый каталог сборки.' }
    & git archive --format=zip ('--prefix=PcbExpo-' + $version + '/') ('--output=' + $sourceArchive) $Ref
    if ($LASTEXITCODE -ne 0) { throw 'Не удалось создать архив исходников.' }
    $manifest.sourceCommit = [string]$commit
    $manifest.tag = $Ref
    $manifest.files = @($manifest.files) + @([ordered]@{
        name = $sourceName
        sha256 = (Get-FileHash -LiteralPath $sourceArchive -Algorithm SHA256).Hash.ToLowerInvariant()
        bytes = (Get-Item -LiteralPath $sourceArchive).Length
    })
    $checksums = foreach ($file in $manifest.files) {
        $actual = (Get-FileHash -LiteralPath (Join-Path $release $file.name) -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($actual -ne $file.sha256) { throw "Контрольная сумма не совпадает: $($file.name)" }
        "$actual  $($file.name)"
    }
    $checksums | Set-Content -LiteralPath (Join-Path $release 'SHA256SUMS.txt') -Encoding ASCII
    $manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $manifestPath -Encoding UTF8
    Write-Host "SOURCE: $sourceArchive"
    Write-Host "COMMIT: $commit"
} finally { Pop-Location }
