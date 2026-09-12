# Test-first development-tool contracts. Not usable until the checks pass.
function Get-DevCheckPlan { param([string]$Profile) throw 'Not implemented' }
function Assert-DevAudit { param([string]$Json) throw 'Not implemented' }
function Invoke-DevNative { param([string]$FilePath, [string[]]$Arguments, [string]$LogPrefix) throw 'Not implemented' }
function Invoke-DevSequence { param([string[]]$Steps, [scriptblock]$Execute, [string]$Directory, [hashtable]$Metadata) throw 'Not implemented' }
Export-ModuleMember -Function Get-DevCheckPlan, Assert-DevAudit, Invoke-DevNative, Invoke-DevSequence
