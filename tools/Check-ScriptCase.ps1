<#
.SYNOPSIS
    Finds (and optionally fixes) C# script filename-casing problems that silently work on
    Windows but break Unity's script binding on case-sensitive filesystems (Linux / macOS / CI).

.DESCRIPTION
    THE PROBLEM
    -----------
    Unity binds a MonoBehaviour/ScriptableObject component to the type whose name EXACTLY
    matches the script's file name. On Windows the filesystem is case-insensitive, so
    "Enemyreward.cs" happily binds to "class EnemyReward" -- until something forces a clean
    reimport, or the repo is checked out on a case-sensitive system. Then Unity can't bind
    the script and shows the component as a missing "(Script) - does not derive from
    MonoBehaviour", WITH NO COMPILE ERROR. Any prefab carrying that component then fails to
    save, and the component never runs at all (which is how a whole loot/AI system can go
    silently dead).

    It hides easily because git on Windows defaults to core.ignorecase=true, so a case-only
    rename on disk is never recorded -- git and disk can disagree on case indefinitely.

    THIS SCRIPT CHECKS TWO THINGS
    -----------------------------
      Check A  git-tracked filename case  !=  true on-disk filename case
               (a case-only rename git never recorded; a fresh checkout elsewhere restores
                git's case, which may not match the class -> broken binding there)

      Check B  on-disk filename case  !=  the class/struct/enum name the file defines
               (works on Windows, breaks on case-sensitive systems)

    THE FIX (-Fix)
    --------------
    Renames the .cs (and its .cs.meta) so the file name matches the type name, recorded
    correctly in git, using a two-step "git mv" through a distinct temp name to force the
    case into the index. The .meta -- and therefore the Unity asset GUID -- is preserved, so
    every prefab and scene reference stays intact.

.PARAMETER Path
    Folder to scan, relative to the repo root. Default: "Assets". Only git-tracked *.cs files
    are considered.

.PARAMETER Fix
    Apply fixes. Omit for a report-only dry run (default).

.NOTES
    * RUN WITH UNITY CLOSED when using -Fix, so Unity reconciles the renames cleanly on next
      open. Re-running while Unity is open can briefly re-trigger the missing-script state.
    * Re-run after pulling branches or adding scripts.
    * Must be run from inside the git repository.
    * ASCII-only on purpose: Windows PowerShell 5.1 reads BOM-less files as ANSI, so keep
      non-ASCII characters out of this file.

.EXAMPLE
    powershell -File tools/Check-ScriptCase.ps1
        Report only -- lists every mismatch, changes nothing.

.EXAMPLE
    powershell -File tools/Check-ScriptCase.ps1 -Fix
        Fix every mismatch found under Assets (close Unity first).

.EXAMPLE
    powershell -File tools/Check-ScriptCase.ps1 -Path Assets/Scripts -Fix
        Fix mismatches under a narrower folder only.
#>
[CmdletBinding()]
param(
    [string]$Path = "Assets",
    [switch]$Fix
)

$ErrorActionPreference = "Stop"

# Verify we're in a git repo.
$null = git rev-parse --is-inside-work-tree 2>$null
if ($LASTEXITCODE -ne 0) { Write-Error "Not inside a git repository - run from the repo root."; exit 1 }

# Returns the true on-disk file (directory enumeration is the only reliable source of case on
# Windows; Get-Item / Resolve-Path echo back whatever case you passed in).
function Get-TrueDiskFile {
    param([string]$dir, [string]$leaf)
    if ([string]::IsNullOrEmpty($dir)) { $dir = "." }
    return Get-ChildItem -LiteralPath $dir -Force -File -ErrorAction SilentlyContinue |
           Where-Object { $_.Name -ieq $leaf } | Select-Object -First 1
}

# Two-step git mv through a distinct temp name -> forces the case into the index even with
# core.ignorecase=true. Moves the .cs and its .cs.meta together so the GUID is preserved.
function Repair-Case {
    param([string]$gitCsPath, [string]$canonicalStem)

    $dir = (Split-Path $gitCsPath -Parent) -replace '\\', '/'
    if ([string]::IsNullOrEmpty($dir)) { $dir = "." }

    $moves = @(
        @{ Src = $gitCsPath; Tmp = "$dir/$canonicalStem.__casefix__.cs"; Dst = "$dir/$canonicalStem.cs" }
    )
    $metaSrc = "$gitCsPath.meta"
    if (git ls-files -- $metaSrc) {
        $moves += @{ Src = $metaSrc; Tmp = "$dir/$canonicalStem.__casefix__.cs.meta"; Dst = "$dir/$canonicalStem.cs.meta" }
    }

    foreach ($m in $moves) {
        git mv -f -- $m.Src $m.Tmp
        if ($LASTEXITCODE -ne 0) { Write-Warning "  git mv failed: $($m.Src) -> $($m.Tmp)"; return $false }
        git mv -f -- $m.Tmp $m.Dst
        if ($LASTEXITCODE -ne 0) { Write-Warning "  git mv failed: $($m.Tmp) -> $($m.Dst)"; return $false }
    }
    return $true
}

# --- Scan ---
$tracked = git ls-files $Path | Where-Object { $_ -like "*.cs" }
$issues  = New-Object System.Collections.Generic.List[object]

foreach ($p in $tracked) {
    $gitLeaf = Split-Path $p -Leaf
    $dir     = Split-Path $p -Parent
    $disk    = Get-TrueDiskFile -dir $dir -leaf $gitLeaf
    if ($null -eq $disk) { continue }   # tracked but not on disk
    $diskLeaf = $disk.Name
    $diskStem = [System.IO.Path]::GetFileNameWithoutExtension($diskLeaf)

    # Canonical name is the type the file defines (if its name matches the stem,
    # case-insensitively); otherwise keep the on-disk stem and only reconcile git.
    $content = Get-Content -LiteralPath $disk.FullName -Raw
    $rx = "\b(class|struct|interface|enum)\s+($([regex]::Escape($diskStem)))\b"
    $m  = [regex]::Match($content, $rx, [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
    if ($m.Success) { $canonicalStem = $m.Groups[2].Value } else { $canonicalStem = $diskStem }

    $canonicalLeaf = "$canonicalStem.cs"
    $gitWrong  = ($gitLeaf  -cne $canonicalLeaf)
    $diskWrong = ($diskLeaf -cne $canonicalLeaf)
    if (-not $gitWrong -and -not $diskWrong) { continue }

    if ($diskWrong) { $check = "B (file vs class)" } else { $check = "A (git vs disk)" }
    $issues.Add([PSCustomObject]@{
        Check     = $check
        GitName   = $gitLeaf
        DiskName  = $diskLeaf
        Canonical = $canonicalLeaf
        Path      = $p
    })
}

# --- Report ---
$count = $tracked.Count
if ($issues.Count -eq 0) {
    Write-Output "OK - no filename/case mismatches found under '$Path' ($count scripts scanned)."
    exit 0
}

Write-Output "Found $($issues.Count) mismatch(es) under '$Path':"
Write-Output (($issues | Format-Table Check, GitName, DiskName, Canonical, Path -AutoSize | Out-String).TrimEnd())
Write-Output ""

if (-not $Fix) {
    Write-Output "Dry run. Re-run with -Fix (Unity CLOSED) to rename these to match their class names."
    exit 0
}

# --- Fix ---
Write-Output "Applying fixes (make sure Unity is closed)..."
$fixed = 0
foreach ($i in $issues) {
    $stem = [System.IO.Path]::GetFileNameWithoutExtension($i.Canonical)
    Write-Output "  $($i.GitName)  ->  $($i.Canonical)"
    if (Repair-Case -gitCsPath $i.Path -canonicalStem $stem) { $fixed++ }
}
Write-Output ""
Write-Output "Fixed $fixed of $($issues.Count). Review with 'git status', then commit. Reopen Unity and confirm the Console is clean."
