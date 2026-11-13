$ErrorActionPreference = 'Stop'
$framework = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
$output = Join-Path (Split-Path -Parent $PSScriptRoot) '每日清单.exe'
$temporary = Join-Path $PSScriptRoot 'daily-checklist.build.exe'
$layout = Join-Path $PSScriptRoot 'Widget.xaml'
$arguments = @(
    '/nologo'
    '/target:winexe'
    '/platform:anycpu'
    '/codepage:65001'
    '/optimize+'
    "/out:$temporary"
    "/resource:$layout,Widget.xaml"
    "/reference:$framework/WPF/WindowsBase.dll"
    "/reference:$framework/WPF/PresentationCore.dll"
    "/reference:$framework/WPF/PresentationFramework.dll"
    "/reference:$framework/System.Xaml.dll"
    "/reference:$framework/System.Drawing.dll"
    "/reference:$framework/System.Windows.Forms.dll"
    "/reference:$framework/System.Runtime.Serialization.dll"
    (Join-Path $PSScriptRoot 'Program.cs')
    (Join-Path $PSScriptRoot 'ChecklistState.cs')
)
try {
    & (Join-Path $framework 'csc.exe') @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "编译失败，退出码 $LASTEXITCODE"
    }
    [IO.File]::Move($temporary, $output, $true)
}
finally {
    if (Test-Path -LiteralPath $temporary) {
        Remove-Item -LiteralPath $temporary
    }
}
