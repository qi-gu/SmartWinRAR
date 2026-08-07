<#
Build WinRAR Smart Extract with the .NET Framework C# compiler included on Windows.
No network access or third-party build tool is required.
#>

[CmdletBinding()]
param(
    [string]$OutputDirectory
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $PSScriptRoot 'dist'
}

function Find-CSharpCompiler {
    $windows = [Environment]::GetEnvironmentVariable('WINDIR')
    foreach ($relativePath in @(
        'Microsoft.NET\Framework64\v4.0.30319\csc.exe',
        'Microsoft.NET\Framework\v4.0.30319\csc.exe'
    )) {
        $candidate = Join-Path $windows $relativePath
        if ([IO.File]::Exists($candidate)) { return $candidate }
    }
    throw 'Cannot find the .NET Framework C# compiler (csc.exe).'
}

function Invoke-Compiler {
    param([string]$Compiler, [string[]]$Arguments)
    & $Compiler $Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "C# compilation failed with exit code $LASTEXITCODE."
    }
}

$sourceDirectory = Join-Path $PSScriptRoot 'src'
$manifest = Join-Path $sourceDirectory 'app.manifest'
$compiler = Find-CSharpCompiler
$temporaryDirectory = Join-Path ([IO.Path]::GetTempPath()) ('WinRARSmartExtractBuild-' + [Guid]::NewGuid().ToString('N'))

try {
    [void][IO.Directory]::CreateDirectory($temporaryDirectory)
    [void][IO.Directory]::CreateDirectory($OutputDirectory)

    $helper = Join-Path $temporaryDirectory 'WinRARSmartExtract.exe'
    $installer = Join-Path $temporaryDirectory 'Install.exe'
    $uninstaller = Join-Path $temporaryDirectory 'Uninstall.exe'

    Invoke-Compiler $compiler @(
        '/nologo', '/target:winexe', '/platform:anycpu', '/optimize+', '/debug-',
        ('/win32manifest:' + $manifest),
        '/reference:System.Windows.Forms.dll',
        '/reference:System.IO.Compression.dll',
        '/reference:System.IO.Compression.FileSystem.dll',
        ('/out:' + $helper),
        (Join-Path $sourceDirectory 'SmartExtract.cs')
    )

    Invoke-Compiler $compiler @(
        '/nologo', '/target:winexe', '/platform:anycpu', '/optimize+', '/debug-',
        ('/win32manifest:' + $manifest),
        '/reference:System.Windows.Forms.dll',
        ('/resource:' + $helper + ',WinRARSmartExtract.Payload'),
        ('/out:' + $installer),
        (Join-Path $sourceDirectory 'Installer.cs')
    )

    Invoke-Compiler $compiler @(
        '/nologo', '/target:winexe', '/platform:anycpu', '/optimize+', '/debug-',
        ('/win32manifest:' + $manifest),
        '/reference:System.Windows.Forms.dll',
        ('/out:' + $uninstaller),
        (Join-Path $sourceDirectory 'Uninstaller.cs')
    )

    $helperOutput = Join-Path $OutputDirectory 'WinRARSmartExtract.exe'
    $installerOutput = Join-Path $OutputDirectory '安装.exe'
    $uninstallerOutput = Join-Path $OutputDirectory '卸载.exe'
    $packageOutput = Join-Path $OutputDirectory 'WinRAR智能解压_安装包.zip'

    Copy-Item -LiteralPath $helper -Destination $helperOutput -Force
    Copy-Item -LiteralPath $installer -Destination $installerOutput -Force
    Copy-Item -LiteralPath $uninstaller -Destination $uninstallerOutput -Force
    Compress-Archive -LiteralPath $installerOutput, $uninstallerOutput -DestinationPath $packageOutput -CompressionLevel Optimal -Force

    Write-Output 'Build succeeded.'
    foreach ($path in @($helperOutput, $installerOutput, $uninstallerOutput, $packageOutput)) {
        $item = Get-Item -LiteralPath $path
        $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
        Write-Output ($item.Name + ' | ' + $item.Length + ' bytes | SHA256 ' + $hash)
    }
}
finally {
    $expectedPrefix = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    $resolvedTemporary = [IO.Path]::GetFullPath($temporaryDirectory)
    if ($resolvedTemporary.StartsWith($expectedPrefix, [StringComparison]::OrdinalIgnoreCase) -and
        [IO.Path]::GetFileName($resolvedTemporary).StartsWith('WinRARSmartExtractBuild-', [StringComparison]::Ordinal)) {
        if ([IO.Directory]::Exists($resolvedTemporary)) { [IO.Directory]::Delete($resolvedTemporary, $true) }
    }
}
