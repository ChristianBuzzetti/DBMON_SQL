# dotnet publish never clears its output directory, so any program left there from earlier work ships
# inside the distribution and inside the Store MSIX. Only the executables of the build itself may be present.
param([Parameter(Mandatory = $true)][string]$PackageDirectory)
$ErrorActionPreference = 'Stop'
$package = [IO.Path]::GetFullPath($PackageDirectory).TrimEnd('\', '/')
$allowed = @('DBMON SQL.exe', 'createdump.exe')
$extensions = @('.exe', '.bat', '.cmd', '.com', '.msi', '.scr', '.vbs')
$programs = @(Get-ChildItem -LiteralPath $package -Recurse -File -Force | Where-Object { $extensions -contains $_.Extension })
$strays = @($programs | Where-Object { $_.DirectoryName -ne $package -or $allowed -notcontains $_.Name })
if ($strays.Count) {
    $list = (@($strays | ForEach-Object { $_.FullName.Substring($package.Length + 1) }) | Sort-Object) -join ', '
    throw "Programs the build did not produce are in the package and would ship inside it: $list. Remove them from $package, or publish to an empty directory."
}
