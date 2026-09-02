#Requires -PSEdition Core

[CmdletBinding()]
param(
    [string]$LunaPath = 'luna',
    [string]$SourceRef = 'main',
    [string]$Workspace = (Join-Path ([System.IO.Path]::GetTempPath()) "luna-sanity-$([guid]::NewGuid().ToString('N'))")
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$lunaCommand = Get-Command $LunaPath -CommandType Application -ErrorAction Stop | Select-Object -First 1
$lunaExecutable = $lunaCommand.Source
$workspacePath = [System.IO.Path]::GetFullPath($Workspace)
$removeWorkspace = -not $PSBoundParameters.ContainsKey('Workspace')

function Invoke-Luna {
    param(
        [Parameter(Mandatory)]
        [string[]]$Arguments
    )

    Write-Information "luna $($Arguments -join ' ')" -InformationAction Continue
    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $lunaExecutable
    $startInfo.WorkingDirectory = $workspacePath
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardInput = $true
    foreach ($argument in $Arguments) {
        $startInfo.ArgumentList.Add($argument)
    }

    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    try {
        if (-not $process.Start()) {
            throw "Unable to start Luna CLI."
        }

        $process.StandardInput.Close()
        $process.WaitForExit()
        if ($process.ExitCode -ne 0) {
            throw "luna $($Arguments -join ' ') failed with exit code $($process.ExitCode)."
        }
    }
    finally {
        $process.Dispose()
    }
}

try {
    New-Item -ItemType Directory -Force -Path $workspacePath | Out-Null

    Invoke-Luna -Arguments @('init')
    Invoke-Luna -Arguments @('sources', 'add', 'github', 'lunapack', 'lunarisdigitalsolutions/lunapack', '--ref', $SourceRef, '--path', 'projects/packs')
    Invoke-Luna -Arguments @('sources', 'list')
    Invoke-Luna -Arguments @('discover')
    Invoke-Luna -Arguments @('search', 'luna')
    Invoke-Luna -Arguments @('validate', 'lunapack-testing@1.0.0')
    Invoke-Luna -Arguments @('inspect', 'lunapack-testing@1.0.0')
    Invoke-Luna -Arguments @(
        'install',
        'lunapack-testing@1.0.0',
        '--parameter',
        'projectName=Sanity Check',
        '--parameter',
        'includeOptional=true',
        '--parameter',
        'profile=full',
        '--parameter',
        'features=docs',
        '--parameter',
        'features=ci',
        '--parameter',
        'features=scripts',
        '--scripts',
        'skip'
    )
    Invoke-Luna -Arguments @('audit')
    Invoke-Luna -Arguments @('outdated')
    Invoke-Luna -Arguments @('update', 'lunapack-testing', '--scripts', 'skip')
    Invoke-Luna -Arguments @('audit')
    Invoke-Luna -Arguments @('uninstall', 'lunapack-testing', '--scripts', 'skip')
    Invoke-Luna -Arguments @('audit')
}
finally {
    if ($removeWorkspace -and (Test-Path -LiteralPath $workspacePath)) {
        Remove-Item -LiteralPath $workspacePath -Recurse -Force
    }
}
