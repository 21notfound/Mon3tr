$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
New-Item -ItemType Directory -Force -Path (Join-Path $projectRoot 'bin') | Out-Null
$framework = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
$references = @('System.dll','System.Core.dll','System.Web.Extensions.dll','System.Drawing.dll','System.Windows.Forms.dll','WPF/WindowsBase.dll','WPF/PresentationCore.dll','WPF/PresentationFramework.dll','System.Xaml.dll')
$compilerArgs = @('/nologo','/target:winexe','/optimize+','/platform:x64',('/out:' + (Join-Path $projectRoot 'bin/Mon3tr.exe')),('/win32manifest:' + (Join-Path $projectRoot 'app.manifest')))
foreach ($reference in $references) { $compilerArgs += '/reference:' + (Join-Path $framework $reference) }
$compilerArgs += '/resource:' + (Join-Path $projectRoot 'assets/v2/animation.bin.gz') + ',animation.bin.gz'
$compilerArgs += '/win32icon:' + (Join-Path $projectRoot 'assets/icon/mon3tr.ico')
$compilerArgs += '/resource:' + (Join-Path $projectRoot 'assets/icon/mon3tr.ico') + ',mon3tr.ico'
foreach ($voice in @('deploy','greeting','tap')) { $compilerArgs += '/resource:' + (Join-Path $projectRoot "assets/voices/$voice.wav") + ",voice-$voice.wav" }
foreach ($source in @('PetV2.cs','Usage.cs','UsageConnection.cs','Tests.cs')) { $compilerArgs += Join-Path $projectRoot "$source" }
& (Join-Path $framework 'csc.exe') @compilerArgs
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
Write-Output 'Built bin/Mon3tr.exe'

