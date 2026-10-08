param([Parameter(Mandatory)][string]$ArchivePath, [string]$ExtractTo)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'candidate-common.ps1')
Test-CandidateArchive ([IO.Path]::GetFullPath($ArchivePath)) (Get-CandidateVersion) $ExtractTo
