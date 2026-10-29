# Copyright (C) 2026 EthenGod
# SPDX-License-Identifier: GPL-3.0-only
param([string]$OutputDirectory = 'releases', [string]$NodePath, [switch]$Desktop)
$ErrorActionPreference = 'Stop'
# -Desktop is retained only as an invocation alias; there is one two-asset distribution.
& (Join-Path $PSScriptRoot 'frontend/desktop-release.ps1') -OutputDirectory $OutputDirectory -NodePath $NodePath