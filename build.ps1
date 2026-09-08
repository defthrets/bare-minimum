<#
  Bare Minimum build script.

  Drives the self-contained Roslyn compiler in tools\ rather than `dotnet build`, because the
  .NET SDK on this machine is broken (Microsoft.NETCore.App\8.0.28 is a partial install, so
  every `dotnet` invocation dies on a missing hostpolicy.dll). This path needs no SDK, no
  Visual Studio and no admin rights. Same approach as the Hoodrich and Overspray repos.

  Usage:
    .\build.ps1                        # build to .\build\BareMinimum.dll
    .\build.ps1 -Deploy                # build, then install into both GTA V editions
    .\build.ps1 -Deploy -Target Legacy # ...into one of them
    .\build.ps1 -Deploy -FreshData     # ...and overwrite the installed data files
    .\build.ps1 -Package               # build a release zip in .\release\
    .\build.ps1 -Package -Full         # ...and a second one with ScriptHookVDotNet in it
#>
[CmdletBinding()]
param(
    [ValidateSet('Release', 'Debug')]
    [string]$Configuration = 'Release',

    [switch]$Deploy,
    [switch]$Package,

    # Also bundle ScriptHookVDotNet, for somebody who does not already run script mods.
    #
    # Its licence expressly allows redistribution. ScriptHookV's does NOT, and that one is
    # locked to a single game build besides -- so a copy bundled today would be the wrong one
    # the week after the next patch. The read-me sends people to dev-c.com for it.
    [switch]$Full,

    # Which install(s) -Deploy writes to. Bare Minimum is a pure SHVDN script with no asset
    # dependencies and both editions ship the identical ScriptHookVDotNet3.dll, so one build
    # runs on both.
    [ValidateSet('Legacy', 'Enhanced', 'Both')]
    [string]$Target = 'Both',

    # Overwrite the installed data files with the ones just built. Off by default so a
    # player's hand-edited foods.json survives an update.
    [switch]$FreshData,

    [string]$GtaDir = 'C:\Program Files (x86)\Steam\steamapps\common\Grand Theft Auto V',
    [string]$EnhancedDir = 'C:\Program Files (x86)\Steam\steamapps\common\Grand Theft Auto V Enhanced',

    # Where -Full takes ScriptHookVDotNet from. An install that is known to work.
    [string]$ShvdnFrom = 'C:\Program Files (x86)\Steam\steamapps\common\Grand Theft Auto V Enhanced'
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

$csc    = Join-Path $root 'tools\roslyn\tasks\net472\csc.exe'
$refDir = Join-Path $root 'tools\refasm\build\.NETFramework\v4.8'
$srcDir = Join-Path $root 'src\BareMinimum'
$outDir = Join-Path $root 'build'
$outDll = Join-Path $outDir 'BareMinimum.dll'

if (-not (Test-Path $csc))    { throw "Compiler missing: $csc  (see tools\README.md)" }
if (-not (Test-Path $refDir)) { throw "net48 reference assemblies missing: $refDir" }

# SHVDN IS THE VENDORED 3.6.0, NOT WHATEVER THE INSTALL HAPPENS TO HOLD.
#
# Building against the installed Enhanced fork stamps a reference to Version=3.9.0.0 into the
# dll, and SHVDN resolves a script's references in its own AppDomain and DECLINES one newer
# than itself -- so a genuine 3.6 or a 3.7 nightly does not load the mod at all. Not a missing
# feature: "Could not load file or assembly", and nothing else runs.
#
# The other direction is the case every loader handles: compiled against 3.6.0.0, every host
# is newer than the reference, and the dll still binds by simple name on 3.9.
#
# Learned on Fumes the hard way -- it claimed 3.6 support for four releases while every
# confirmation came from somebody on the 3.9 fork.
$shvdn = Join-Path $root 'tools\shvdn\3.6.0\ScriptHookVDotNet3.dll'

if (-not (Test-Path $shvdn)) {
    Write-Host "WARN  vendored SHVDN 3.6.0 missing at $shvdn -- falling back to an install, which stamps THAT version in and will not load on anything older" -ForegroundColor Yellow
    $shvdn = $null
    foreach ($dir in @($GtaDir, $EnhancedDir)) {
        $candidate = Join-Path $dir 'ScriptHookVDotNet3.dll'
        if (Test-Path $candidate) { $shvdn = $candidate; break }
    }
    if (-not $shvdn) { throw "ScriptHookVDotNet3.dll not found vendored or in either install." }
}

New-Item -ItemType Directory -Force $outDir | Out-Null

# --- references -------------------------------------------------------------
# Deliberately minimal. Bare Minimum has ZERO external runtime dependencies: only the BCL and
# SHVDN. No Newtonsoft, no LemonUI, no NativeUI -- nothing that can lose a version fight
# with another mod sharing the same scripts\ folder.
$refNames = @(
    'mscorlib.dll'
    'System.dll'
    'System.Core.dll'
    'System.Drawing.dll'
    'System.Windows.Forms.dll'
)
$refs = @()
foreach ($n in $refNames) {
    $p = Join-Path $refDir $n
    if (-not (Test-Path $p)) { throw "Reference assembly missing: $p" }
    $refs += "/reference:`"$p`""
}
$refs += "/reference:`"$shvdn`""

# --- sources ----------------------------------------------------------------
$sources = Get-ChildItem $srcDir -Recurse -Filter *.cs |
    Where-Object { $_.FullName -notmatch '\\(obj|bin)\\' } |
    ForEach-Object { $_.FullName }

if (-not $sources) { throw "No .cs sources found under $srcDir" }

# --- compiler options -------------------------------------------------------
$opts = @(
    '/target:library'
    '/platform:x64'
    '/langversion:9.0'
    '/nologo'
    '/warnaserror-'
    '/warn:4'
    '/nostdlib+'
    '/utf8output'
    "/out:`"$outDll`""
)
if ($Configuration -eq 'Debug') {
    $opts += '/debug:portable', '/define:DEBUG;TRACE', '/optimize-'
} else {
    $opts += '/debug-', '/optimize+'
}

$rsp = Join-Path $outDir 'build.rsp'
($opts + $refs + ($sources | ForEach-Object { "`"$_`"" })) | Set-Content -Path $rsp -Encoding UTF8

Write-Host "Compiling $($sources.Count) source files -> $outDll ($Configuration)" -ForegroundColor Cyan
$sw = [Diagnostics.Stopwatch]::StartNew()
& $csc "@$rsp"
$exit = $LASTEXITCODE
$sw.Stop()

if ($exit -ne 0) { throw "Compilation failed (csc exit $exit)." }
Write-Host ("OK  {0:N0} bytes in {1:N1}s" -f (Get-Item $outDll).Length, $sw.Elapsed.TotalSeconds) -ForegroundColor Green

# --- what SHVDN did we actually stamp? ---------------------------------------
# READ BACK, NOT ASSUMED. A dll built against 3.9 compiles, deploys and runs perfectly on the
# machine that built it, and does not load at all on 3.6 or a 3.7 nightly -- SHVDN declines a
# reference newer than itself. Nothing else in this build can see that, and the only symptom
# is a line in a stranger's log. So the output is opened and asked.
$stamped = $null
$fs = [IO.File]::OpenRead($outDll)
try {
    $pe = [System.Reflection.PortableExecutable.PEReader]::new($fs)
    $mr = [System.Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($pe)
    foreach ($h in $mr.AssemblyReferences) {
        $a = $mr.GetAssemblyReference($h)
        if ($mr.GetString($a.Name) -eq 'ScriptHookVDotNet3') { $stamped = $a.Version }
    }
} finally { $fs.Dispose() }

if (-not $stamped) { throw "The dll references no ScriptHookVDotNet3 at all." }

if ($stamped -gt [version]'3.6.0.0') {
    throw ("Stamped ScriptHookVDotNet3 $stamped -- anything above 3.6.0.0 will not load on " +
           "3.6 or a 3.7 nightly. Check tools\shvdn\3.6.0\ScriptHookVDotNet3.dll is present " +
           "and that no source file uses a newer API.")
}

Write-Host "     needs ScriptHookVDotNet3 $stamped or newer" -ForegroundColor DarkGray

# --- deploy -----------------------------------------------------------------
function Get-ReloadKey([string]$gameDir) {
    <#
        Whatever SHVDN is actually set to reload on, per install. Not assumed: the two
        installs on this machine disagree, and a wrong key in a reminder is worse than none.
    #>
    $ini = Join-Path $gameDir 'ScriptHookVDotNet.ini'
    if (-not (Test-Path $ini)) { return $null }

    $hit = Select-String -Path $ini -Pattern '^\s*ReloadKeyBinding\s*=\s*(\S+)' |
           Select-Object -First 1

    if ($hit) { return $hit.Matches[0].Groups[1].Value }
    return $null
}

function Deploy-To([string]$gameDir, [string]$label) {
    if (-not (Test-Path $gameDir)) {
        Write-Host "skip $label - not installed at $gameDir" -ForegroundColor DarkGray
        return
    }

    $scripts = Join-Path $gameDir 'scripts'
    if (-not (Test-Path $scripts)) {
        Write-Host "skip $label - no scripts folder (ScriptHookVDotNet not installed?)" -ForegroundColor Yellow
        return
    }

    Write-Host "$label -> $scripts" -ForegroundColor Cyan

    # THE COPY IS ATTEMPTED, NOT PRE-REFUSED.
    #
    # This used to check whether the game was running and give up if it was, which was a
    # guess dressed up as a rule: SHVDN SHADOW-COPIES script assemblies into the .NET
    # download cache and runs them from there, so the dll sitting in scripts\ is very often
    # not locked at all. Refusing on the strength of a process name meant closing the game
    # for every change for no reason.
    #
    # So it tries. A genuine lock throws, and that is reported for what it is.
    $locked = $false
    try {
        Copy-Item $outDll $scripts -Force -ErrorAction Stop
    } catch {
        $locked = $true
        Write-Host "  LOCKED BareMinimum.dll is in use - the data files below still went." -ForegroundColor Yellow
        Write-Host "         Close the game and re-run to update the dll." -ForegroundColor DarkGray
    }

    $pdb = Join-Path $outDir 'BareMinimum.pdb'
    if (-not $locked -and (Test-Path $pdb)) {
        try { Copy-Item $pdb $scripts -Force -ErrorAction Stop } catch { }
    }

    if (-not $locked -and (Get-Process GTA5, GTA5_Enhanced -ErrorAction SilentlyContinue)) {
        # The reload key is read from SHVDN's own ini rather than assumed. The two installs
        # on this machine do not agree on it -- Legacy is Pause, Enhanced is Insert -- so a
        # hardcoded reminder would be wrong half the time.
        $key = Get-ReloadKey $gameDir
        $named = if ($key) { $key } else { "the SHVDN reload key" }
        Write-Host "  LIVE   dll replaced while the game runs - press $named in game to reload." -ForegroundColor Green
    }

    $dataSrc = Join-Path $root 'data'
    $dataDst = Join-Path $scripts 'BareMinimum'
    New-Item -ItemType Directory -Force $dataDst | Out-Null

    # THE FILES THE MOD ITSELF WRITES: the needs, the pocket and the fridge. They live in the
    # folder being managed here and they look exactly like data files we stopped shipping.
    # Anything else added to Paths that the mod WRITES has to be added to this list in the
    # same change, or the next deploy destroys it and nothing says why.
    #
    # (It read "the player's saved fuel for every vehicle they own" until now, which is Fumes'
    # comment -- this script was started from that one and the sentence came along with it.)
    #
    # NOTHING PRUNES THE DEPLOY FOLDER TODAY, so this list is not load-bearing yet -- it is
    # here so that whoever adds a prune inherits a list that is already right. Overspray lost
    # somebody's graffiti to exactly that gap: a file the mod writes, in the folder the deploy
    # manages, looking precisely like a data file that stopped shipping.
    $ours = @('needs.json', 'needs.json.bak',
              'pantry.json', 'pantry.json.bak',
              'fridge.json', 'fridge.json.bak',
              'BareMinimum.log', 'BareMinimum.log.1')

    Get-ChildItem $dataSrc -Recurse -File | ForEach-Object {
        $rel = $_.FullName.Substring($dataSrc.Length).TrimStart('\')
        $dst = Join-Path $dataDst $rel
        New-Item -ItemType Directory -Force (Split-Path $dst) | Out-Null

        if (-not (Test-Path $dst)) {
            Copy-Item $_.FullName $dst
            Write-Host "  new    $rel" -ForegroundColor DarkGray
            return
        }

        if ((Get-FileHash $_.FullName).Hash -eq (Get-FileHash $dst).Hash) {
            Write-Host "  same   $rel" -ForegroundColor DarkGray
        } elseif ($FreshData) {
            Copy-Item $_.FullName $dst -Force
            Write-Host "  update $rel" -ForegroundColor Green
        } else {
            Write-Host "  KEEP   $rel  (differs from source - re-run with -FreshData to overwrite)" -ForegroundColor Yellow
        }
    }

    # The ini is never overwritten: it is the file players hand-edit. Say what is missing from
    # it instead, so a new setting is not silently on its default forever.
    $iniSrc = Join-Path $root 'BareMinimum.ini'
    $iniDst = Join-Path $scripts 'BareMinimum.ini'

    if (-not (Test-Path $iniSrc)) { return }

    if (-not (Test-Path $iniDst)) {
        Copy-Item $iniSrc $iniDst
        Write-Host "  new    BareMinimum.ini" -ForegroundColor DarkGray
        return
    }

    $srcKeys = Read-IniKeys $iniSrc
    $dstKeys = Read-IniKeys $iniDst

    $absent = $srcKeys | Where-Object { $dstKeys -notcontains $_ }
    $extra  = $dstKeys | Where-Object { $srcKeys -notcontains $_ }

    if ($absent) {
        Write-Host "  STALE  BareMinimum.ini is missing $($absent.Count) setting(s):" -ForegroundColor Yellow
        Write-Host "         $($absent -join ', ')" -ForegroundColor DarkGray
        Write-Host "         Defaults apply until they are added." -ForegroundColor DarkGray
    }
    if ($extra) {
        Write-Host "  STALE  BareMinimum.ini has $($extra.Count) setting(s) nothing reads:" -ForegroundColor Yellow
        Write-Host "         $($extra -join ', ')" -ForegroundColor DarkGray
    }
    if (-not $absent -and -not $extra) {
        Write-Host "  keep   BareMinimum.ini ($($srcKeys.Count) settings, all current)" -ForegroundColor DarkGray
    }
}

function Read-IniKeys {
    <#
        Every "Section.Key" in an ini, so two of them can be compared by what they actually
        SET rather than by which headings they happen to have. A section-only comparison
        reports a file as current while it carries twenty dead keys inside the right headings.
    #>
    param([string]$Path)

    $section = ''
    $keys = New-Object System.Collections.Generic.List[string]

    foreach ($line in (Get-Content -LiteralPath $Path)) {
        $t = $line.Trim()

        if ($t -match '^\[(.+)\]$') { $section = $Matches[1]; continue }
        if ($t.StartsWith(';') -or $t.StartsWith('#') -or -not $t.Contains('=')) { continue }
        if (-not $section) { continue }

        $keys.Add("$section.$($t.Split('=')[0].Trim())")
    }

    return $keys
}

if ($Deploy) {
    # No process check at all any more. Deploy-To attempts the copy and reports a real lock
    # if it hits one; a running game is usually not a reason to stop, because SHVDN runs
    # scripts out of a shadow copy rather than out of scripts\.
    if ($Target -in 'Legacy', 'Both')   { Deploy-To $GtaDir      'Legacy' }
    if ($Target -in 'Enhanced', 'Both') { Deploy-To $EnhancedDir 'Enhanced' }

    Write-Host "Deploy complete." -ForegroundColor Green
}

# --- packaging ---------------------------------------------------------------
# A zip that merges straight over the GTA V folder, because that is the one install
# instruction nobody gets wrong. Built only from the repo -- never from the game folder, or a
# release ships whatever this machine happens to be testing with, including somebody's save.
if ($Package) {
    $version = (Select-String -Path (Join-Path $root 'src\BareMinimum\Core\Log.cs') `
                              -Pattern 'Version = "([^"]+)"').Matches[0].Groups[1].Value

    $relDir = Join-Path $root 'release'
    $stage  = Join-Path $relDir "BareMinimum-$version"

    if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }

    $scripts = Join-Path $stage 'scripts'
    $dataOut = Join-Path $scripts 'BareMinimum'
    New-Item -ItemType Directory -Force -Path $dataOut | Out-Null

    Copy-Item $outDll (Join-Path $scripts 'BareMinimum.dll')
    Copy-Item (Join-Path $root 'BareMinimum.ini') (Join-Path $scripts 'BareMinimum.ini')
    Copy-Item (Join-Path $root 'data\*.json') $dataOut

    # The icons.
    #
    # THIS USED TO BE GUARDED BY Test-Path AND SILENTLY SKIPPED IF ABSENT, which is the exact
    # failure the comment beside it was warning about: a folder that quietly does not ship, on
    # the one machine that cannot notice because the files are already in place from being
    # deployed. Overspray lost its voice pack that way. It is required now, and the manifest
    # below COUNTS the files rather than trusting that a copy of a folder copied all of it.
    $icons = Join-Path $root 'data\icons'
    if (-not (Test-Path $icons)) { throw "data\icons is missing -- run tools\make_icons.py" }

    Copy-Item $icons $dataOut -Recurse

    foreach ($doc in @('README.txt', 'CHANGES.txt')) {
        $d = Join-Path $relDir $doc
        if (-not (Test-Path $d)) { throw "release\$doc is missing, and a release without it is a dll in a zip." }
        Copy-Item $d $stage
    }

    # EVERY FILE A DOWNLOAD NEEDS, NAMED. A wildcard cannot tell the difference between "this
    # folder is empty" and "this folder was never meant to have anything in it", so the things
    # that must be there are written down and checked after staging.
    $must = @(
        'scripts\BareMinimum.dll',
        'scripts\BareMinimum.ini',
        'scripts\BareMinimum\foods.json',
        'scripts\BareMinimum\vendors.json',
        'scripts\BareMinimum\doors.txt',
        'scripts\BareMinimum\brands.json',
        'scripts\BareMinimum\socials.json',
        'README.txt',
        'CHANGES.txt'
    )

    # Belt and braces: a save or a log in a release zip would overwrite the first thing a
    # player did with the mod.
    Get-ChildItem $stage -Recurse -Include 'needs.json', 'pantry.json', 'fridge.json',
                                           '*.log', '*.log.1', '*.bak' |
        ForEach-Object { Remove-Item $_.FullName -Force }

    # The artwork, COUNTED rather than assumed. A half-copied folder passes a Test-Path.
    $wantIcons = (Get-ChildItem $icons -File).Count
    $gotIcons  = @(Get-ChildItem (Join-Path $dataOut 'icons') -File -ErrorAction SilentlyContinue).Count

    if ($gotIcons -ne $wantIcons) {
        throw "Package has $gotIcons icon(s) and the repo has $wantIcons."
    }

    function Assert-Staged($names) {
        $absent = @()
        foreach ($m in $names) { if (-not (Test-Path (Join-Path $stage $m))) { $absent += $m } }
        if ($absent) { throw "Package is missing: $($absent -join ', ')" }
    }

    function Write-Zip($path, $names) {
        if (Test-Path $path) { Remove-Item $path -Force }
        Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $path -CompressionLevel Optimal

        $count = (Get-ChildItem $stage -Recurse -File).Count
        $kb    = [math]::Round((Get-Item $path).Length / 1KB)

        Write-Host ""
        Write-Host ("Packaged  " + (Split-Path $path -Leaf)) -ForegroundColor Green
        Write-Host "          $count files, $kb KB, $gotIcons icons, version $version"
        foreach ($m in $names) { Write-Host "          + $m" -ForegroundColor DarkGray }
    }

    # THE MOD-ONLY ZIP IS ALWAYS WRITTEN, AND THE FULL ONE IS BUILT ON TOP OF IT, from this
    # same staged folder and therefore from this same compile.
    #
    # They used to be two runs of this script, which meant two compiles -- and a C# build is
    # not byte-reproducible, so the two downloads both called 0.1.0 contained different
    # binaries. Nobody would ever have noticed and it would have made a bug report from one
    # of them impossible to match against the other.
    Assert-Staged $must
    Write-Zip (Join-Path $relDir "BareMinimum-$version.zip") $must

    if ($Full) {
        # Everything needed to run it, for somebody who does not already run script mods.
        # Without this the zip is the mod and nothing else, which is right for a modder and
        # wrong for everybody else -- and "it does not do anything" with no ScriptHookVDotNet
        # installed looks identical to a mod that is broken.
        foreach ($f in @('ScriptHookVDotNet.asi', 'ScriptHookVDotNet2.dll',
                         'ScriptHookVDotNet3.dll', 'ScriptHookVDotNet.ini')) {
            $src = Join-Path $ShvdnFrom $f
            if (-not (Test-Path $src)) { throw "-Full needs $f, and it is not in $ShvdnFrom" }

            Copy-Item $src $stage
            $must += $f
        }

        # Their licence travels with their binaries. That is the condition of shipping them.
        $lic = Join-Path $ShvdnFrom 'Licenses'
        if (-not (Test-Path $lic)) { throw "-Full needs the Licenses folder from $ShvdnFrom" }
        Copy-Item $lic $stage -Recurse

        $first = Join-Path $relDir 'READ ME FIRST.txt'
        if (-not (Test-Path $first)) { throw "release\READ ME FIRST.txt is missing." }
        Copy-Item $first $stage
        $must += 'READ ME FIRST.txt'

        # A stray log from the copied install would ride along with the runtime.
        Get-ChildItem $stage -Recurse -Include '*.log', '*.log.1' |
            ForEach-Object { Remove-Item $_.FullName -Force }

        Assert-Staged $must
        Write-Zip (Join-Path $relDir "BareMinimum-$version-full.zip") $must
    }
}
