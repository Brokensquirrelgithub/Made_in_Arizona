param([ValidateSet('Mac','Windows','All','Prepare','Validate')][string]$Target = 'Windows', [string]$UnityEditor)
$projectRoot = Split-Path $PSScriptRoot -Parent
$candidates = @(
    $env:MIA_UNITY_EDITOR,
    'C:\Program Files\Unity\6000.5.5f1\Editor\Unity.exe',
    'C:\Program Files\Unity\Hub\Editor\6000.5.5f1\Editor\Unity.exe'
)
if (-not $UnityEditor) { $UnityEditor = $candidates | Where-Object { $_ -and (Test-Path -LiteralPath $_) } | Select-Object -First 1 }
if (-not $UnityEditor -or -not (Test-Path -LiteralPath $UnityEditor)) { throw 'Unity 6000.5.5f1 editor not found; pass -UnityEditor or set MIA_UNITY_EDITOR.' }
$method = if ($Target -eq 'Prepare' -or $Target -eq 'Validate') { $Target } else { "Build$Target" }
New-Item -ItemType Directory -Force -Path "$projectRoot\Builds" | Out-Null
$arguments = @('-batchmode', '-nographics', '-quit', '-projectPath', ('"' + $projectRoot + '"'),
    '-executeMethod', "MadeInArizona.Editor.BuildGame.$method", '-logFile',
    ('"' + (Join-Path $projectRoot "Builds\unity-$method.log") + '"'))
$process = Start-Process -FilePath $UnityEditor -ArgumentList $arguments -Wait -PassThru -WindowStyle Hidden
exit $process.ExitCode
