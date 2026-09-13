param([ValidateSet('Mac','Windows','All','Prepare','Validate')][string]$Target = 'Windows', [string]$UnityEditor = 'C:\Program Files\Unity\Hub\Editor\6000.5.5f1\Editor\Unity.exe')
$projectRoot = Split-Path $PSScriptRoot -Parent
$method = if ($Target -eq 'Prepare' -or $Target -eq 'Validate') { $Target } else { "Build$Target" }
New-Item -ItemType Directory -Force -Path "$projectRoot\Builds" | Out-Null
& $UnityEditor -batchmode -nographics -quit -projectPath $projectRoot -executeMethod "MadeInArizona.Editor.BuildGame.$method" -logFile "$projectRoot\Builds\unity-$method.log"
exit $LASTEXITCODE
