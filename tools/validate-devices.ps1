#!/usr/bin/env pwsh
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$devicesPath = Join-Path $root 'Core/devices.json'

if (-not (Test-Path -LiteralPath $devicesPath)) {
    Write-Host "::error::找不到 $devicesPath"
    exit 1
}

$raw = Get-Content -LiteralPath $devicesPath -Raw
if (-not $raw.TrimStart().StartsWith('[')) {
    Write-Host "::error::devices.json 顶层必须是数组"
    exit 1
}

try {
    $profiles = @($raw | ConvertFrom-Json)
}
catch {
    Write-Host "::error::devices.json 不是合法 JSON：$($_.Exception.Message)"
    exit 1
}

if ($profiles.Count -eq 0) {
    Write-Host "::error::devices.json 至少需要一条设备描述"
    exit 1
}

$script:errors = [System.Collections.Generic.List[string]]::new()
$script:ids = @{}

function Add-Error([string]$profile, [string]$message) {
    $script:errors.Add("[$profile] $message")
}

function Test-Hex([string]$value) {
    return -not [string]::IsNullOrWhiteSpace($value) -and $value -match '^0x[0-9A-Fa-f]{1,4}$'
}

function Test-Integer($value) {
    return $null -ne $value -and (
        $value -is [int] -or $value -is [long] -or $value -is [int16] -or
        $value -is [uint16] -or $value -is [uint32] -or $value -is [uint64] -or
        $value -is [byte] -or $value -is [sbyte]
    )
}

function Test-Boolean($value) {
    return $value -is [bool]
}

function Test-PositiveNumber($value) {
    return $null -ne $value -and (
        $value -is [int] -or $value -is [long] -or
        $value -is [double] -or $value -is [decimal]
    ) -and $value -gt 0
}

$count = 0
foreach ($profile in $profiles) {
    $count++
    $label = if ($null -ne $profile.id -and -not [string]::IsNullOrWhiteSpace([string]$profile.id)) {
        [string]$profile.id
    }
    else {
        "#$count"
    }

    if ($null -eq $profile.schema -or [int]$profile.schema -ne 1) {
        Add-Error $label 'schema 必须为 1'
    }

    if ([string]::IsNullOrWhiteSpace([string]$profile.id)) {
        Add-Error $label '缺少 id'
    }
    elseif ($profile.id -notmatch '^[a-z0-9][a-z0-9-]*$') {
        Add-Error $label 'id 只能使用小写字母、数字和短横线'
    }
    elseif ($script:ids.ContainsKey([string]$profile.id)) {
        Add-Error $label 'id 重复'
    }
    else {
        $script:ids[[string]$profile.id] = $true
    }

    if ([string]::IsNullOrWhiteSpace([string]$profile.name)) {
        Add-Error $label '缺少 name'
    }

    if (-not (Test-Hex $profile.vendorId)) {
        Add-Error $label 'vendorId 格式必须类似 0x35D8'
    }

    if ($null -eq $profile.productIds -or $profile.productIds -isnot [System.Array] -or $profile.productIds.Count -eq 0) {
        Add-Error $label 'productIds 必须是非空数组'
    }
    else {
        foreach ($productId in $profile.productIds) {
            if (-not (Test-Hex $productId)) {
                Add-Error $label "productId 格式错误：$productId"
            }
        }
    }

    if (-not (Test-Hex $profile.reportId)) {
        Add-Error $label 'reportId 格式错误'
    }
    if (-not (Test-Hex $profile.readOp)) {
        Add-Error $label 'readOp 格式错误'
    }
    if (-not (Test-Hex $profile.writeOp)) {
        Add-Error $label 'writeOp 格式错误'
    }

    if (-not (Test-Integer $profile.bandCount) -or [int64]$profile.bandCount -le 0) {
        Add-Error $label 'bandCount 必须是正整数'
    }
    if (-not (Test-Integer $profile.bandPayloadSize) -or [int64]$profile.bandPayloadSize -le 0) {
        Add-Error $label 'bandPayloadSize 必须是正整数'
    }
    if (-not (Test-Integer $profile.curveSlot) -or [int64]$profile.curveSlot -lt 0) {
        Add-Error $label 'curveSlot 必须是非负整数'
    }
    if (-not (Test-PositiveNumber $profile.gainScale)) {
        Add-Error $label 'gainScale 必须大于 0'
    }

    if ($null -eq $profile.commands) {
        Add-Error $label '缺少 commands'
    }
    else {
        foreach ($key in @('eqIndex', 'eqBand', 'save')) {
            if (-not (Test-Hex $profile.commands.$key)) {
                Add-Error $label "commands.$key 缺失或格式错误"
            }
        }
        foreach ($key in @('commit', 'preGain', 'globalGain')) {
            $value = $profile.commands.$key
            if ($null -ne $value -and -not (Test-Hex $value)) {
                Add-Error $label "commands.$key 格式错误"
            }
        }
    }

    if ($null -eq $profile.capabilities) {
        Add-Error $label '缺少 capabilities'
    }
    else {
        foreach ($key in @('builtInSlots', 'curve', 'preGain', 'globalGain', 'needsCommit', 'dualChannel')) {
            if (-not (Test-Boolean $profile.capabilities.$key)) {
                Add-Error $label "capabilities.$key 必须是布尔值"
            }
        }
    }

    if ($null -eq $profile.timing) {
        Add-Error $label '缺少 timing'
    }
    else {
        foreach ($key in @('switchDelayMs', 'bandDelayMs', 'saveDelayMs')) {
            $value = $profile.timing.$key
            if (-not (Test-Integer $value) -or [int64]$value -lt 0) {
                Add-Error $label "timing.$key 必须是非负整数"
            }
        }
    }

    if ($null -eq $profile.slots -or $profile.slots -isnot [System.Array] -or $profile.slots.Count -eq 0) {
        Add-Error $label 'slots 必须是非空数组'
    }
    else {
        $slotIndexes = @{}
        foreach ($slot in $profile.slots) {
            if (-not (Test-Integer $slot.index) -or [int64]$slot.index -lt 0) {
                Add-Error $label 'slot.index 必须是非负整数'
            }
            elseif ($slotIndexes.ContainsKey([int]$slot.index)) {
                Add-Error $label "slot.index 重复：$($slot.index)"
            }
            else {
                $slotIndexes[[int]$slot.index] = $true
            }

            if ([string]::IsNullOrWhiteSpace([string]$slot.name)) {
                Add-Error $label 'slot.name 不能为空'
            }
        }
    }

    if ($null -ne $profile.verified) {
        $level = [string]$profile.verified.level
        if ($level -notin @('official', 'community', 'experimental')) {
            Add-Error $label 'verified.level 只能是 official、community 或 experimental'
        }
    }
}

if ($script:errors.Count -gt 0) {
    foreach ($message in $script:errors) {
        Write-Host "::error::$message"
    }
    Write-Host "设备描述校验失败：$($script:errors.Count) 个问题"
    exit 1
}

Write-Host "设备描述校验通过：$count 条"
