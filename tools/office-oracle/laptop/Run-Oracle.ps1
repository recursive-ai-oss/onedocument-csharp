# Runs one batch of checks against desktop Word, Excel and PowerPoint and writes JSON lines.
#
# This script is started by a scheduled task that is registered to run only while the user is logged on,
# because Office automation needs an interactive desktop session. It is the only thing that touches Office.
#
# Input : -JobDir names the job directory (or, without it, <base>\jobs\current.txt does); <job>\job.json describes the batch:
#   { "run": "name", "instance": "who", "maxMinutes": 25, "lockPath": null, "lockRetrySeconds": 60,
#     "lockWaitMinutes": 20, "items": [ { "file": "in\\a.docx", "mode": "open|probe|grid",
#                                          "exportPdf": false, "saveAs": false } ] }
# Output: <job>\out\results.jsonl, any exported files beside it, and <job>\out\done.json when finished.
#
# Detecting a repair prompt: documents are opened with repair switched off where the application has the
# switch (Word: OpenAndRepair=false) and with alerts on. A background watcher (UI Automation) records the
# text of every dialog the application raises and answers it with the non-repairing button (No, Cancel,
# Close, OK, in that order), so the open call fails instead of repairing. A prompt whose text mentions
# unreadable/corrupt/problem-with-content/recover/repair is classified "repair-prompt".

param([string]$Base = (Join-Path $env:USERPROFILE 'onedocument'), [string]$JobDir = '')

$ErrorActionPreference = 'Stop'
$utf8 = New-Object System.Text.UTF8Encoding($false)
$jobRoot = $null
$out = $null
$lockHeld = $null
$lockPath = $null
$startedPids = @{}
$pidFile = Join-Path $Base 'started-pids.json'

function Write-Line($obj) {
    $json = ConvertTo-Json -InputObject $obj -Compress -Depth 8
    [IO.File]::AppendAllText((Join-Path $out 'results.jsonl'), $json + "`n", $utf8)
}

function Get-Pids($names) {
    $ids = @()
    foreach ($n in $names) { $ids += @(Get-Process -Name $n -ErrorAction SilentlyContinue | ForEach-Object { $_.Id }) }
    return $ids
}

function Save-StartedPids {
    $list = @()
    foreach ($k in $startedPids.Keys) { $list += @{ kind = $k; id = $startedPids[$k].Id; start = $startedPids[$k].Start } }
    $json = ConvertTo-Json -InputObject @($list) -Compress
    [IO.File]::WriteAllText($pidFile, $json, $utf8)
}

# A previous run that was killed before its cleanup may have left an application we launched. The pid file names
# only processes a run launched; the start time must match so a reused id is never touched.
function Stop-StalePids {
    $stale = @()
    if (Test-Path $pidFile) {
        try {
            foreach ($e in @(Get-Content $pidFile -Raw | ConvertFrom-Json)) {
                $p = Get-Process -Id $e.id -ErrorAction SilentlyContinue
                if ($p -and $p.StartTime.ToUniversalTime().ToString('o') -eq $e.start) {
                    try { $p.Kill(); $stale += $e.kind } catch { }
                }
            }
        } catch { }
        Remove-Item $pidFile -Force -ErrorAction SilentlyContinue
    }
    return $stale
}


function Test-RepairText($text) {
    return [bool]($text -match 'unreadable|corrupt|problem with (some )?content|found a problem|recover|repair|cannot be opened because there are problems')
}

function Get-Outcome($dialogs, $err) {
    $all = (@($dialogs) -join ' ') + ' ' + $err
    if (Test-RepairText ((@($dialogs) -join ' '))) { return 'repair-prompt' }
    if ($err -and (Test-RepairText $err)) { return 'unreadable' }
    if ($err) { return 'error' }
    return 'clean'
}

function Get-Fonts {
    Add-Type -AssemblyName System.Drawing
    $fc = New-Object System.Drawing.Text.InstalledFontCollection
    return @($fc.Families | ForEach-Object { $_.Name } | Sort-Object)
}

function Get-AppBuild($exe) {
    $p = Join-Path $env:ProgramFiles ('Microsoft Office\root\Office16\' + $exe)
    if (Test-Path $p) { return (Get-Item $p).VersionInfo.ProductVersion }
    return $null
}

# ---- the shared laptop lock -------------------------------------------------------------------------

function Get-Lock($path, $instance, $retrySeconds, $waitMinutes) {
    $deadline = (Get-Date).AddMinutes($waitMinutes)
    $waited = 0
    $stale = $null
    while ($true) {
        try {
            # CreateNew fails when the file exists, which makes the acquire atomic.
            $fs = [IO.File]::Open($path, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::Read)
            $text = "project=onedocument`ninstance=$instance`nstarted=" + (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ') + "`n"
            $bytes = $utf8.GetBytes($text)
            $fs.Write($bytes, 0, $bytes.Length)
            $fs.Close()
            return @{ ok = $true; waitedSeconds = $waited; staleRemoved = $stale }
        } catch [IO.IOException] {
            if (-not (Test-Path $path)) { throw }
            $age = (Get-Date).ToUniversalTime() - (Get-Item $path).LastWriteTimeUtc
            $started = $null
            foreach ($line in @(Get-Content $path -ErrorAction SilentlyContinue)) {
                if ($line -match '^started=(.+)$') { try { $started = [DateTime]::Parse($Matches[1]).ToUniversalTime() } catch { } }
            }
            if ($started) { $age = (Get-Date).ToUniversalTime() - $started }
            if ($age.TotalHours -gt 2) {
                $stale = 'removed stale lock aged ' + [int]$age.TotalMinutes + ' min: ' + ((Get-Content $path -ErrorAction SilentlyContinue) -join ' ')
                Remove-Item $path -Force
                continue
            }
            if ((Get-Date) -gt $deadline) { return @{ ok = $false; waitedSeconds = $waited; staleRemoved = $stale } }
            Start-Sleep -Seconds $retrySeconds
            $waited += $retrySeconds
        }
    }
}

# COM calls that take a string or a number get explicitly typed arguments: with untyped values the call never
# returns on this setup, so every call below casts.
# ---- per-application checks ---------------------------------------------------------------------------

$missing = [Type]::Missing

function Invoke-Word($app, $item, $path, $outDir, $res) {
    $doc = $null
    try {
        # Positional: ConfirmConversions=false, ReadOnly=true, AddToRecentFiles=false, Visible=false, OpenAndRepair=false; read-only so the input is never changed.
        try {
            $doc = $app.Documents.Open($path, $false, $true, $false, $missing, $missing, $missing, $missing, $missing, $missing, $missing, $false, $false)
        } catch {
            # Word declined to open it as is. If it opens with repair switched on, the file needs repair, which is what
            # the interactive "unreadable content" prompt offers.
            $res.firstOpenError = $_.Exception.Message
            $doc = $app.Documents.Open($path, $false, $true, $false, $missing, $missing, $missing, $missing, $missing, $missing, $missing, $false, $true)
            $res.needsRepair = $true
        }
        if ($job.trace) { Write-Line @{ kind = 'trace'; step = 'opened' } }
        $res.pages = $doc.ComputeStatistics(2)
        if ($job.trace) { Write-Line @{ kind = 'trace'; step = 'pages' } }
        $res.paragraphs = $doc.Paragraphs.Count
        if ($item.mode -eq 'probe') {
            $rows = @()
            $i = 0
            foreach ($p in $doc.Paragraphs) {
                $i++
                $r = $p.Range
                $f = $r.Font
                $pf = $p.Format
                $txt = $r.Text -replace '[\r\n\a]', ''
                $rows += @{
                    index = $i; text = $txt; listString = $r.ListFormat.ListString; listType = $r.ListFormat.ListType
                    listLevel = $r.ListFormat.ListLevelNumber; listValue = $r.ListFormat.ListValue
                    style = $p.Style.NameLocal; fontName = $f.Name; fontSize = $f.Size; bold = $f.Bold; italic = $f.Italic
                    underline = $f.Underline; color = $f.Color; smallCaps = $f.SmallCaps; allCaps = $f.AllCaps
                    leftIndent = $pf.LeftIndent; firstLineIndent = $pf.FirstLineIndent; rightIndent = $pf.RightIndent
                    spaceBefore = $pf.SpaceBefore; spaceAfter = $pf.SpaceAfter; alignment = $pf.Alignment
                    lineSpacing = $pf.LineSpacing; lineSpacingRule = $pf.LineSpacingRule
                    page = $r.Information(3); physicalPage = $r.Information(10)
                }
            }
            $res.paragraphInfo = $rows
            if ($job.trace) { Write-Line @{ kind = 'trace'; step = 'paragraphs-done' } }
            $secs = @()
            foreach ($sec in $doc.Sections) {
                $hf = @()
                foreach ($kind in 1, 2, 3) {
                    $h = $sec.Headers.Item($kind)
                    $f = $sec.Footers.Item($kind)
                    $hf += @{ kind = @('primary', 'first', 'even')[$kind - 1]; headerExists = $h.Exists; headerLinked = $h.LinkToPrevious
                              headerText = ($h.Range.Text -replace '[\r\n\a]', ''); footerExists = $f.Exists; footerLinked = $f.LinkToPrevious
                              footerText = ($f.Range.Text -replace '[\r\n\a]', '') }
                }
                $secs += @{ index = $sec.Index; differentFirstPage = $sec.PageSetup.DifferentFirstPageHeaderFooter; oddAndEven = $sec.PageSetup.OddAndEvenPagesHeaderFooter
                            restartNumbering = $sec.Headers.Item(1).PageNumbers.RestartNumberingAtSection; startingNumber = $sec.Headers.Item(1).PageNumbers.StartingNumber
                            headersFooters = $hf }
            }
            $res.sections = $secs
            if ($job.trace) { Write-Line @{ kind = 'trace'; step = 'sections-done' } }
            $tbls = @()
            $ti = 0
            foreach ($t in $doc.Tables) {
                $ti++
                $cells = @()
                foreach ($c in $t.Range.Cells) {
                    $cells += @{ row = $c.RowIndex; column = $c.ColumnIndex; width = $c.Width; text = ($c.Range.Text -replace '[\r\n\a]', ''); bold = $c.Range.Font.Bold
                                 shading = $c.Shading.BackgroundPatternColor; italic = $c.Range.Font.Italic }
                }
                $tbls += @{ index = $ti; style = $t.Style.NameLocal; uniform = $t.Uniform; rows = $t.Rows.Count; columns = $t.Columns.Count; cellCount = $t.Range.Cells.Count; cells = $cells }
            }
            $res.tables = $tbls
            if ($job.trace) { Write-Line @{ kind = 'trace'; step = 'tables-done' } }
            $revs = @()
            $ri = 0
            foreach ($rv in $doc.Revisions) {
                $ri++
                $revs += @{ index = $ri; type = $rv.Type; author = $rv.Author; text = ($rv.Range.Text -replace '[\r\n\a]', '\u00b6') }
            }
            $res.revisions = $revs
            $notes = @{ footnotes = $doc.Footnotes.Count; endnotes = $doc.Endnotes.Count; footnoteNumberStyle = $doc.Footnotes.NumberStyle; endnoteNumberStyle = $doc.Endnotes.NumberStyle }
            if ($doc.Endnotes.Count -gt 0) { $notes.endnoteText = ($doc.Endnotes.Item(1).Range.Text -replace '[\r\n\a]', ''); $notes.endnoteReference = $doc.Endnotes.Item(1).Reference.Text }
            $res.notes = $notes
            $cms = @()
            $ci = 0
            foreach ($cm in $doc.Comments) {
                $ci++
                $anc = $null; try { $anc = $cm.Ancestor } catch { }
                $cms += @{ index = $ci; author = $cm.Author; text = ($cm.Range.Text -replace '[\r\n\a]', ''); scope = ($cm.Scope.Text); isReply = ($null -ne $anc); replies = $cm.Replies.Count; done = $cm.Done }
            }
            $res.comments = $cms
            # Every story of the document (main text, headers, footers, notes, comments, text boxes) with the list label Word gives each paragraph.
            $stories = @()
            foreach ($sr in $doc.StoryRanges) {
                $r = $sr
                $k = 0
                while ($null -ne $r) {
                    $rows = @()
                    foreach ($pp in $r.Paragraphs) {
                        $lf = $pp.Range.ListFormat
                        $rows += @{ text = ($pp.Range.Text -replace '[\r\n\a]', ''); listString = $lf.ListString; listType = $lf.ListType; listLevel = $lf.ListLevelNumber; listValue = $lf.ListValue }
                    }
                    $srevs = @()
                    try { foreach ($rv in $r.Revisions) { $srevs += @{ type = $rv.Type; author = $rv.Author; text = ($rv.Range.Text -replace '[\r\n\a]', '\u00b6') } } } catch { }
                    $stories += @{ storyType = $r.StoryType; chainIndex = $k; paragraphs = $rows; revisions = $srevs }
                    $k++
                    $nx = $null
                    try { $nx = $r.NextStoryRange } catch { }
                    $r = $nx
                }
            }
            $res.stories = $stories
            if ($job.trace) { Write-Line @{ kind = 'trace'; step = 'stories-done' } }
        }
        $base = [IO.Path]::GetFileNameWithoutExtension($path)
        if ($item.exportPdf) {
            $pdf = Join-Path $outDir ($base + '.word.pdf')
            if ($job.trace) { Write-Line @{ kind = 'trace'; step = 'exporting'; pdf = $pdf } }
            if ($job.pdfVia -eq 'saveas2') { $doc.SaveAs2($pdf, 17) } else { $doc.ExportAsFixedFormat([string]$pdf, [int]17, [bool]$false) }
            if ($job.trace) { Write-Line @{ kind = 'trace'; step = 'exported' } }
            $res.exportedPdf = [IO.Path]::GetFileName($pdf)
        }
        if ($item.saveAs) {
            $dst = Join-Path $outDir ($base + '.word.docx')
            $doc.SaveAs2([string]$dst, [int]16)
            $res.savedDocx = [IO.Path]::GetFileName($dst)
        }
        if ($item.actions) {
            $res.actions = @()
            $k = 0
            foreach ($act in @($item.actions)) {
                $k++
                $d2 = $null
                $entry = @{ action = $act }
                try {
                    $d2 = $app.Documents.Open($path, $false, $true, $false, $missing, $missing, $missing, $missing, $missing, $missing, $missing, $false, [bool]$res.needsRepair)
                    $d2.Activate()
                    if ($job.trace) { Write-Line @{ kind = 'trace'; step = 'action-open'; action = $act } }
                    $d2.TrackRevisions = $false
                    # A generated file without a compatibility mode opens in the oldest mode, and saving it with a DrawingML text box
                    # would stop on the Compatibility Checker dialog.
                    try { $d2.CheckCompatibility = $false } catch { }
                    $kind, $arg = $act -split ':', 2
                    switch ($kind) {
                        'accept-all' { $d2.Revisions.AcceptAll() }
                        'reject-all' { $d2.Revisions.RejectAll() }
                        'accept' { $d2.Revisions.Item([int]$arg).Accept() }
                        'reject' { $d2.Revisions.Item([int]$arg).Reject() }
                        'accept-all-stories' { Invoke-AllStories $d2 $true }
                        'reject-all-stories' { Invoke-AllStories $d2 $false }
                        'mailmerge' {
                            # mailmerge:DATA.csv|single -- Word's own merge of the template with the data file beside it into a new document
                            # (every record, a next-page section break between them). The merged document is what is read and saved below.
                            $dataName = ($arg -split '\|', 2)[0]
                            $dataPath = Join-Path (Split-Path -Parent $path) $dataName
                            $tpl = $d2
                            $tpl.MailMerge.OpenDataSource([string]$dataPath, [int]0, [bool]$false, [bool]$true, [bool]$false, [bool]$false)
                            $tpl.MailMerge.Destination = 0
                            $tpl.MailMerge.Execute([bool]$false)
                            $d2 = $app.ActiveDocument
                            $d2.Activate()
                            try { $tpl.Close(0) } catch { }
                        }
                        'accept-para' { $d2.Paragraphs.Item([int]$arg).Range.Revisions.AcceptAll() }
                        'reject-para' { $d2.Paragraphs.Item([int]$arg).Range.Revisions.RejectAll() }
                        default { throw "unknown action $act" }
                    }
                    if ($job.trace) { Write-Line @{ kind = 'trace'; step = 'action-done'; action = $act } }
                    $entry.text = ($d2.Content.Text -replace '\r', '\u00b6' -replace '[\n\a]', '')
                    $entry.revisionsLeft = $d2.Revisions.Count
                    $entry.storyRevisionsLeft = Get-StoryRevisionCount $d2
                    $dst2 = Join-Path $outDir ($base + '.' + ($act -replace '[^a-z0-9]+', '-') + '.docx')
                    if ($job.trace) { Write-Line @{ kind = 'trace'; step = 'action-saving'; file = $dst2 } }
                    $d2.SaveAs2([string]$dst2, [int]16)
                    $entry.saved = [IO.Path]::GetFileName($dst2)
                } catch {
                    $entry.error = $_.Exception.Message
                } finally {
                    if ($d2) { try { $d2.Close(0) } catch { } }
                }
                $res.actions += $entry
            }
        }
    } finally {
        if ($doc) { try { $doc.Close(0) } catch { } }
    }
}

# Accept or reject every revision in every story (main text, headers, footers, footnotes, endnotes, comments, text boxes).
function Invoke-AllStories($doc, $accept) {
    foreach ($sr in $doc.StoryRanges) {
        $r = $sr
        while ($null -ne $r) {
            if ($accept) { $r.Revisions.AcceptAll() } else { $r.Revisions.RejectAll() }
            $r = $r.NextStoryRange
        }
    }
}

function Get-StoryRevisionCount($doc) {
    $n = 0
    foreach ($sr in $doc.StoryRanges) {
        $r = $sr
        while ($null -ne $r) {
            $n += $r.Revisions.Count
            $r = $r.NextStoryRange
        }
    }
    return $n
}

function Invoke-ExcelFormulas($app, $item, $path, $outDir, $res) {
    # Formulas mode: the input is <name>.fgrid, one line per test, id<TAB>how<TAB>text (a1, r1c1 or local; tabs, newlines and
    # backslashes escaped). Each text is entered with Formula2 / Formula2R1C1 / Formula2Local (dynamic-array semantics) into
    # its own block of 60 rows of a new workbook, which is recalculated in full. The answer file <name>.fgrid.tsv has
    # id<TAB>entered-or-error<TAB>Formula2<TAB>Formula2R1C1<TAB>Formula2Local<TAB>rows<TAB>cols<TAB>cells, where cells are
    # t:value items (t = z blank, n number, s text, b boolean, e error code) separated by U+001F, row-major over the spill range.
    $un = { param($x) [regex]::Replace([string]$x, '\\(.)', { param($m) switch ($m.Groups[1].Value) { 't' { "`t" } 'n' { "`n" } 'r' { "`r" } default { $m.Groups[1].Value } } }) }
    $esc = { param($x) ([string]$x).Replace('\', '\\').Replace("`t", '\t').Replace("`n", '\n').Replace("`r", '\r') }
    $inv = [Globalization.CultureInfo]::InvariantCulture
    $sep = [string][char]31
    # A file named *.comma.fgrid is entered with a comma decimal separator (Excel's own setting, restored afterwards), to see how
    # Formula2Local writes separators for such a locale. Function names stay English: the language is the installed one.
    $comma = $path -like '*.comma*.fgrid'
    if ($comma) { $app.UseSystemSeparators = $false; $app.DecimalSeparator = ','; $app.ThousandsSeparator = $(if ($path -like '*.commasp.fgrid') { ' ' } else { '.' }) }
    $wb = $app.Workbooks.Add()
    try {
        # Manual calculation keeps closing the workbook quick; a slow shutdown makes Windows relaunch Excel with /restore.
        $app.Calculation = -4135
        $ws = $wb.Worksheets.Item(1)
        # Sheets the tests refer to; a reference to a missing sheet would make Excel ask for a file.
        foreach ($sn in @('Sheet2', 'Sheet3', 'My Sheet')) {
            $ns = $wb.Worksheets.Add($missing, $wb.Worksheets.Item($wb.Worksheets.Count))
            $ns.Name = [string]$sn
        }
        $ws.Activate()
        $tests = New-Object System.Collections.Generic.List[object]
        foreach ($line in [IO.File]::ReadAllLines($path, $utf8)) {
            if ($line.Length -eq 0) { continue }
            $p = $line.Split("`t")
            $tests.Add(@{ id = $p[0]; how = $p[1]; text = (& $un $p[2]) })
        }
        $entered = @{}
        for ($i = 0; $i -lt $tests.Count; $i++) {
            $t = $tests[$i]
            $cell = $ws.Cells.Item(1 + 60 * $i, 2)
            try {
                if ($t.how -eq 'r1c1') { $cell.Formula2R1C1 = [string]$t.text }
                elseif ($t.how -eq 'local') { $cell.Formula2Local = [string]$t.text }
                else { $cell.Formula2 = [string]$t.text }
                $entered[$i] = 'ok'
            } catch { $entered[$i] = 'rejected: ' + $_.Exception.Message }
        }
        $app.CalculateFull()
        $lines = New-Object System.Collections.Generic.List[string]
        for ($i = 0; $i -lt $tests.Count; $i++) {
            $t = $tests[$i]
            $cell = $ws.Cells.Item(1 + 60 * $i, 2)
            if ($entered[$i] -ne 'ok') { $lines.Add($t.id + "`t" + (& $esc $entered[$i])); continue }
            $rng = $cell
            try { if ($cell.HasSpill) { $rng = $cell.SpillingToRange } } catch { }
            $v = $rng.Value2
            $items = New-Object System.Collections.Generic.List[string]
            $rows = 1; $cols = 1
            if ($v -is [System.Array]) {
                $r0 = $v.GetLowerBound(0); $c0 = $v.GetLowerBound(1)
                $rows = $v.GetLength(0); $cols = $v.GetLength(1)
                for ($r = 0; $r -lt $rows; $r++) { for ($c = 0; $c -lt $cols; $c++) { $items.Add((Format-Cell ($v.GetValue($r0 + $r, $c0 + $c)) $inv $esc)) } }
            } else { $items.Add((Format-Cell $v $inv $esc)) }
            $lines.Add($t.id + "`tok`t" + (& $esc $cell.Formula2) + "`t" + (& $esc $cell.Formula2R1C1) + "`t" + (& $esc $cell.Formula2Local) + "`t" + $rows + "`t" + $cols + "`t" + ($items -join $sep))
        }
        [IO.File]::WriteAllLines((Join-Path $outDir ([IO.Path]::GetFileName($path) + '.tsv')), $lines, $utf8)
        # The file form of every formula (prefixes, array flags) is only visible in the saved file.
        try { $wb.SaveCopyAs((Join-Path $outDir ([IO.Path]::GetFileName($path) + '.xlsx'))) } catch { $res.saveCopyError = $_.Exception.Message }
        $res.formulas = @{ tests = $tests.Count; decimal = [string]$app.International(3); list = [string]$app.International(5); arrayCol = [string]$app.International(14); arrayRow = [string]$app.International(15); arrayAlt = [string]$app.International(16); country = [string]$app.International(1); excelVersion = [string]$app.Version; build = [string]$app.Build }
    } finally {
        try { $wb.Close($false) } catch { }
        if ($comma) { try { $app.UseSystemSeparators = $true } catch { } }
    }
}

function Format-Cell($v, $inv, $esc) {
    if ($v -eq $null) { return 'z:' }
    if ($v -is [string]) { return 's:' + (& $esc $v) }
    if ($v -is [bool]) { return 'b:' + [string]$v }
    if ($v -is [double]) { return 'n:' + $v.ToString('R', $inv) }
    return 'e:' + [string][int]$v
}

function Invoke-Excel($app, $item, $path, $outDir, $res) {
    if ($item.mode -eq 'formulas') { Invoke-ExcelFormulas $app $item $path $outDir $res; return }
    $wb = $null
    try {
        try {
            $wb = $app.Workbooks.Open($path, 0, $true)
        } catch {
            # CorruptLoad = xlRepairFile (1): the same retry as for Word.
            $res.firstOpenError = $_.Exception.Message
            $wb = $app.Workbooks.Open($path, 0, $true, $missing, $missing, $missing, $missing, $missing, $missing, $missing, $missing, $missing, $missing, $missing, 1)
            $res.needsRepair = $true
        }
        $res.sheets = $wb.Worksheets.Count
        $res.sheetNames = @($wb.Worksheets | ForEach-Object { $_.Name })
        if ($item.mode -eq 'probe') {
            $cells = @()
            foreach ($ws in $wb.Worksheets) {
                foreach ($c in $ws.UsedRange.Cells) {
                    $cells += @{ sheet = $ws.Name; address = $c.Address($false, $false); value = $c.Value2; text = $c.Text; formula = $c.Formula }
                    if ($cells.Count -ge 500) { break }
                }
            }
            $res.cells = $cells
        }
        if ($item.mode -eq 'cells') {
            # Cells mode: every used cell of the first sheet as row<TAB>col<TAB>type<TAB>value<TAB>numberFormat<TAB>text, to
            # <workbook>.cells.tsv beside the results. type is string, number, boolean, error or empty; numbers are round-trip
            # text in the invariant culture; errors are the numeric code Excel reports (2007 is #DIV/0!, 2042 is #N/A).
            $ws = $wb.Worksheets.Item(1)
            $ur = $ws.UsedRange
            $r0 = [int]$ur.Row; $c0 = [int]$ur.Column
            $lines = New-Object System.Collections.Generic.List[string]
            $inv = [Globalization.CultureInfo]::InvariantCulture
            foreach ($c in $ur.Cells) {
                $v = $c.Value2
                if ($v -eq $null) { $type = 'empty'; $vt = '' }
                elseif ($v -is [string]) { $type = 'string'; $vt = $v }
                elseif ($v -is [bool]) { $type = 'boolean'; $vt = [string]$v }
                elseif ($v -is [double]) { $type = 'number'; $vt = $v.ToString('R', $inv) }
                else { $type = 'error'; $vt = [string][int]$v }
                $esc = { param($x) ([string]$x).Replace('\', '\\').Replace("`t", '\t').Replace("`n", '\n').Replace("`r", '\r') }
                $lines.Add(([string]$c.Row) + "`t" + ([string]$c.Column) + "`t" + $type + "`t" + (& $esc $vt) + "`t" + (& $esc $c.NumberFormat) + "`t" + (& $esc $c.Text))
            }
            [IO.File]::WriteAllLines((Join-Path $outDir ([IO.Path]::GetFileNameWithoutExtension($path) + '.cells.tsv')), $lines, $utf8)
            $res.cells = @{ rows = $lines.Count; usedRow = $r0; usedColumn = $c0; excelVersion = [string]$app.Version; build = [string]$app.Build }
        }
        if ($item.mode -eq 'grid') {
            # Grid mode: column A holds an id; the answer is column D (sheet "text", the TEXT() result) or column B
            # (sheet "cells", the displayed text of a cell with a custom number format). Written as id<TAB>answer lines,
            # tabs, newlines and backslashes escaped, to <workbook>.answers.tsv beside the results.
            $app.CalculateFull()
            $ws = $wb.Worksheets.Item(1)
            $col = if ($ws.Name -eq 'text') { 4 } else { 2 }
            $ws.Columns.Item($col).ColumnWidth = 100
            $last = $ws.Cells.Item($ws.Rows.Count, 1).End(-4162).Row
            $lines = New-Object System.Collections.Generic.List[string]
            for ($r = 2; $r -le $last; $r++) {
                $c = $ws.Cells.Item($r, $col)
                $v = $c.Value2
                $t = if ($ws.Name -eq 'text' -and $v -is [string]) { $v } else { [string]$c.Text }
                $t = $t.Replace('\', '\\').Replace("`t", '\t').Replace("`n", '\n').Replace("`r", '\r')
                $lines.Add(([string]$ws.Cells.Item($r, 1).Value2) + "`t" + $t)
            }
            [IO.File]::WriteAllLines((Join-Path $outDir ([IO.Path]::GetFileNameWithoutExtension($path) + '.answers.tsv')), $lines, $utf8)
            $res.grid = @{ sheet = $ws.Name; rows = $lines.Count; decimal = [string]$app.International(3); thousands = [string]$app.International(4); country = [string]$app.International(1); excelVersion = [string]$app.Version; build = [string]$app.Build }
        }
        $base = [IO.Path]::GetFileNameWithoutExtension($path)
        if ($item.exportPdf) {
            $pdf = Join-Path $outDir ($base + '.excel.pdf')
            $wb.ExportAsFixedFormat([int]0, [string]$pdf)
            $res.exportedPdf = [IO.Path]::GetFileName($pdf)
        }
    } finally {
        if ($wb) { try { $wb.Close($false) } catch { } }
    }
}

function Invoke-PowerPoint($app, $item, $path, $outDir, $res) {
    $pres = $null
    try {
        # ReadOnly, Untitled=false, WithWindow=false
        $pres = $app.Presentations.Open($path, -1, 0, 0)
        $res.slides = $pres.Slides.Count
        $res.slideSize = @($pres.PageSetup.SlideWidth, $pres.PageSetup.SlideHeight)
        $base = [IO.Path]::GetFileNameWithoutExtension($path)
        if ($item.exportPdf) {
            $pdf = Join-Path $outDir ($base + '.powerpoint.pdf')
            $pres.SaveAs([string]$pdf, [int]32)
            $res.exportedPdf = [IO.Path]::GetFileName($pdf)
        }
    } finally {
        if ($pres) { try { $pres.Close() } catch { } }
    }
}

# ---- main ----------------------------------------------------------------------------------------------

try {
    # -JobDir names the run's own directory; the old shared pointer file is only a fallback for manual starts.
    $jobRoot = if ($JobDir) { $JobDir } else { (Get-Content (Join-Path $Base 'jobs\current.txt') -Raw).Trim() }
    $out = Join-Path $jobRoot 'out'
    New-Item -ItemType Directory -Force -Path $out | Out-Null
    $job = Get-Content (Join-Path $jobRoot 'job.json') -Raw | ConvertFrom-Json
    $instance = if ($job.instance) { $job.instance } else { 'unknown' }
    $lockPath = if ($job.lockPath) { [string]$job.lockPath } else { Join-Path $env:USERPROFILE 'oracle.lock' }
    $retry = if ($job.lockRetrySeconds) { [int]$job.lockRetrySeconds } else { 60 }
    $waitMin = if ($job.lockWaitMinutes) { [int]$job.lockWaitMinutes } else { 20 }
    $maxMin = if ($job.maxMinutes) { [int]$job.maxMinutes } else { 25 }

    $lock = Get-Lock $lockPath $instance $retry $waitMin
    Write-Line @{ kind = 'lock'; acquired = $lock.ok; waitedSeconds = $lock.waitedSeconds; staleRemoved = $lock.staleRemoved; lockPath = (Split-Path $lockPath -Leaf) }
    if (-not $lock.ok) { throw 'laptop lock not acquired in time' }
    $lockHeld = $true
    $started = Get-Date
    $staleKilled = @(Stop-StalePids)
    if ($staleKilled.Count -gt 0) { Write-Line @{ kind = 'stale'; killedFromEarlierRun = $staleKilled } }

    $fonts = Get-Fonts
    $relevant = @('Calibri', 'Cambria', 'Aptos', 'Aptos Display', 'Aptos Narrow', 'Times New Roman', 'Arial', 'Courier New', 'Segoe UI',
        'Consolas', 'Carlito', 'Caladea', 'Liberation Sans', 'Liberation Serif', 'Noto Sans', 'Cambria Math', 'Symbol', 'Wingdings', 'MS Gothic', 'SimSun')
    Write-Line @{
        kind = 'env'; run = $job.run; instance = $instance; startedUtc = $started.ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
        os = [Environment]::OSVersion.VersionString
        word = Get-AppBuild 'WINWORD.EXE'; excel = Get-AppBuild 'EXCEL.EXE'; powerpoint = Get-AppBuild 'POWERPNT.EXE'
        fontFamilyCount = $fonts.Count; fontsPresent = @($relevant | Where-Object { $fonts -contains $_ })
        fontsAbsent = @($relevant | Where-Object { $fonts -notcontains $_ })
    }
    [IO.File]::WriteAllLines((Join-Path $out 'fonts.txt'), $fonts, $utf8)

    $apps = @{}
    $prePids = @(Get-Pids @('WINWORD', 'EXCEL', 'POWERPNT'))
    $exeNames = @{ word = 'WINWORD'; excel = 'EXCEL'; powerpoint = 'POWERPNT' }
    $progIds = @{ word = 'Word.Application'; excel = 'Excel.Application'; powerpoint = 'PowerPoint.Application' }
    if ($true) {
Add-Type -ReferencedAssemblies 'UIAutomationClient', 'UIAutomationTypes', 'WindowsBase' -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Windows.Automation;

public class DialogWatcher
{
    private readonly string[] names;
    private readonly List<string> seen = new List<string>();
    private readonly HashSet<string> handled = new HashSet<string>();
    private readonly Dictionary<string, DateTime> firstSeen = new Dictionary<string, DateTime>();
    private volatile bool stop;
    public readonly List<string> Debug = new List<string>();
    private Thread thread;

    private long deadlineTicks;
    private bool handleDialogs = true;
    public bool TimedOut;

    public DialogWatcher(string[] processNames, bool dialogs) { names = processNames; handleDialogs = dialogs; }

    // Kills the named applications if the current step has not finished in time, so a hung call cannot hold the lock.
    public void Arm(int seconds) { TimedOut = false; deadlineTicks = DateTime.UtcNow.AddSeconds(seconds).Ticks; }
    public void Disarm() { deadlineTicks = 0; }

    public void Start()
    {
        thread = new Thread(Loop);
        thread.IsBackground = true;
        thread.Start();
    }

    public void Stop() { stop = true; if (thread != null) thread.Join(5000); }

    public string[] Seen { get { lock (seen) { return seen.ToArray(); } } }

    public void Clear() { lock (seen) { seen.Clear(); } }

    private static string Strip(string s) { return (s ?? "").Replace("&", "").Trim(); }

    private void Loop()
    {
        string[] frames = { "OpusApp", "XLMAIN", "PPTFrameClass", "screenClass", "PPTFrameClass" };
        string[] prefer = { "No", "Cancel", "Close", "OK" };
        while (!stop)
        {
            try
            {
                long dl = deadlineTicks;
                if (dl != 0 && DateTime.UtcNow.Ticks > dl)
                {
                    TimedOut = true;
                    deadlineTicks = 0;
                    foreach (string n in names)
                        foreach (Process p in Process.GetProcessesByName(n)) { try { p.Kill(); } catch { } }
                }
                HashSet<int> pids = new HashSet<int>();
                foreach (string n in names)
                    foreach (Process p in Process.GetProcessesByName(n)) pids.Add(p.Id);
                if (handleDialogs && pids.Count > 0)
                {
                    foreach (AutomationElement w in AutomationElement.RootElement.FindAll(TreeScope.Children, Condition.TrueCondition))
                    {
                        int pid;
                        string cls;
                        try { pid = w.Current.ProcessId; cls = w.Current.ClassName; } catch { continue; }
                        if (!pids.Contains(pid) || Array.IndexOf(frames, cls) >= 0) continue;
                        lock (Debug) { if (Debug.Count < 200) Debug.Add(DateTime.UtcNow.ToString("HH:mm:ss.fff") + " window class=" + cls + " name=" + w.Current.Name); }
                        AutomationElementCollection buttons = w.FindAll(TreeScope.Descendants,
                            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button));
                        if (buttons.Count == 0) continue;
                        string key = pid + ":" + string.Join(".", w.GetRuntimeId());
                        if (handled.Contains(key)) continue;
                        if (!firstSeen.ContainsKey(key)) firstSeen[key] = DateTime.UtcNow;
                        List<string> texts = new List<string>();
                        foreach (AutomationElement t in w.FindAll(TreeScope.Descendants,
                            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text)))
                        {
                            string name = t.Current.Name;
                            if (!string.IsNullOrWhiteSpace(name)) texts.Add(name.Trim());
                        }
                        List<string> labels = new List<string>();
                        AutomationElement chosen = null;
                        foreach (AutomationElement b in buttons) labels.Add(Strip(b.Current.Name));
                        foreach (string want in prefer)
                        {
                            foreach (AutomationElement b in buttons)
                                if (string.Equals(Strip(b.Current.Name), want, StringComparison.OrdinalIgnoreCase)) { chosen = b; break; }
                            if (chosen != null) break;
                        }
                        string clicked = null;
                        if (chosen != null)
                        {
                            try
                            {
                                ((InvokePattern)chosen.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
                                clicked = Strip(chosen.Current.Name);
                            }
                            catch { }
                        }
                        if (clicked != null || (DateTime.UtcNow - firstSeen[key]).TotalSeconds > 60)
                        {
                            handled.Add(key);
                            lock (seen)
                            {
                                seen.Add("title=" + w.Current.Name + " | class=" + cls + " | text=" + string.Join(" / ", texts) +
                                    " | buttons=" + string.Join(",", labels) + " | clicked=" + (clicked ?? "(none; process killed)"));
                            }
                            if (clicked == null)
                            {
                                try { Process.GetProcessById(pid).Kill(); } catch { }
                            }
                        }
                    }
                }
            }
            catch { }
            Thread.Sleep(250);
        }
    }
}
'@
    }
    $watcher = New-Object DialogWatcher ([string[]]@('WINWORD', 'EXCEL', 'POWERPNT'), (-not $job.noWatcher))
    $watcher.Start()
    $itemSeconds = if ($job.itemSeconds) { [int]$job.itemSeconds } else { 90 }

    foreach ($item in $job.items) {
        if (((Get-Date) - $started).TotalMinutes -gt $maxMin) {
            Write-Line @{ kind = 'file'; file = $item.file; outcome = 'skipped'; error = 'batch time limit reached' }
            continue
        }
        $path = Join-Path $jobRoot $item.file
        $ext = [IO.Path]::GetExtension($path).ToLowerInvariant()
        $kind = switch -regex ($ext) { '^\.(docx|docm|dotx|dotm|doc|rtf|xml|odt|html|htm)$' { 'word' } '^\.(xlsx|xlsm|xltx|xltm|xlsb|xls|csv|fgrid|ods)$' { 'excel' } '^\.(pptx|pptm|potx|ppsx|ppt|odp)$' { 'powerpoint' } default { $null } }
        # Flat OPC files share the .xml extension: the processing instruction names the application that owns them.
        if ($ext -eq '.xml') {
            $head = [Text.Encoding]::UTF8.GetString([IO.File]::ReadAllBytes($path), 0, [Math]::Min(1024, (Get-Item $path).Length))
            if ($head -match 'progid="PowerPoint\.') { $kind = 'powerpoint' } elseif ($head -match 'progid="Excel\.') { $kind = 'excel' }
        }
        $res = @{ kind = 'file'; file = $item.file; app = $kind; mode = $item.mode }
        if (-not $kind) { $res.outcome = 'error'; $res.error = 'unknown extension'; Write-Line $res; continue }
        $watch = [Diagnostics.Stopwatch]::StartNew()
        $err = $null
        try {
            if (-not $apps.ContainsKey($kind)) {
                if ((Get-Pids @($exeNames[$kind])).Count -gt 0) {
                    # Never attach to, or close, an application the owner is using.
                    $apps[$kind] = 'busy'
                } else {
                    $before = Get-Pids @($exeNames[$kind])
                    $a = New-Object -ComObject $progIds[$kind]
                    foreach ($np in @(Get-Pids @($exeNames[$kind]))) {
                        if ($before -notcontains $np) {
                            $proc = Get-Process -Id $np -ErrorAction SilentlyContinue
                            if ($proc) { $startedPids[$kind] = @{ Id = $np; Start = $proc.StartTime.ToUniversalTime().ToString('o') } }
                        }
                    }
                    Save-StartedPids
                    if ($kind -ne 'powerpoint') { $a.Visible = $false }
                    $a.DisplayAlerts = $(if ($kind -eq 'word') { if ($null -ne $job.wordAlerts) { [int]$job.wordAlerts } else { -1 } } elseif ($kind -eq 'powerpoint') { 2 } else { $true })
                    $apps[$kind] = $a
                    $res.appBuild = $a.Build
                    Write-Line @{ kind = 'app'; app = $kind; version = $a.Version; build = $a.Build }
                }
            }
            if ($apps[$kind] -eq 'busy') { throw 'skipped-office-running' }
            $app = $apps[$kind]
            $watcher.Clear()
            $watcher.Arm($itemSeconds)
            if ($job.trace) { Write-Line @{ kind = 'trace'; step = 'opening'; file = $item.file } }
            switch ($kind) {
                'word' { Invoke-Word $app $item $path $out $res }
                'excel' { Invoke-Excel $app $item $path $out $res }
                'powerpoint' { Invoke-PowerPoint $app $item $path $out $res }
            }
        } catch {
            $err = $_.Exception.Message
            $res.errorAt = $_.InvocationInfo.ScriptLineNumber
        }
        $watcher.Disarm()
        Start-Sleep -Milliseconds 400
        if ($watcher.TimedOut) { $res.timedOut = $true }
        $dialogs = $watcher.Seen
        $res.dialogs = @($dialogs)
        if ($err) { $res.error = $err }
        $res.outcome = if ($err -eq 'skipped-office-running') { 'skipped' } elseif ($res.needsRepair -and -not $err) { 'repair-prompt' } else { Get-Outcome $dialogs $err }
        $res.seconds = [Math]::Round($watch.Elapsed.TotalSeconds, 2)
        Write-Line $res
        # A killed (hung) application is replaced on the next item.
        foreach ($k in @($apps.Keys)) {
            if ($apps[$k] -ne 'busy') {
                $alive = $true
                try { $null = $apps[$k].Version } catch { $alive = $false }
                if (-not $alive) { $apps.Remove($k) }
            }
        }
    }
    $watcher.Stop()
    if ($job.trace) { Write-Line @{ kind = 'trace'; step = 'watcher'; windows = @($watcher.Debug) } }
} catch {
    if ($out) { Write-Line @{ kind = 'fatal'; error = $_.Exception.Message } }
} finally {
    foreach ($k in @($apps.Keys)) {
        if ($apps[$k] -ne 'busy') {
            # No prompt may keep an invisible application alive: discard every open document first.
            try { $apps[$k].DisplayAlerts = $(if ($k -eq 'excel') { $false } elseif ($k -eq 'word') { 0 } else { 1 }) } catch { }
            try {
                if ($k -eq 'excel') { foreach ($w in @($apps[$k].Workbooks)) { try { $w.Saved = $true; $w.Close($false) } catch { } } }
                elseif ($k -eq 'word') { foreach ($d in @($apps[$k].Documents)) { try { $d.Close(0) } catch { } } }
                else { foreach ($d in @($apps[$k].Presentations)) { try { $d.Saved = -1; $d.Close() } catch { } } }
            } catch { }
            try { $apps[$k].Quit() } catch { }
            try { [void][Runtime.InteropServices.Marshal]::ReleaseComObject($apps[$k]) } catch { }
        }
    }
    $apps = @{}
    [GC]::Collect(); [GC]::WaitForPendingFinalizers()
    # Every application we launched must be gone; wait for Quit, then kill only that process id.
    $killed = @()
    $quitSeconds = @{}
    $survivors = @()
    foreach ($k in @($startedPids.Keys)) {
        $e = $startedPids[$k]
        $waited = 0
        for ($t = 0; $t -lt 60; $t++) {
            $p = Get-Process -Id $e.Id -ErrorAction SilentlyContinue
            if (-not $p -or $p.StartTime.ToUniversalTime().ToString('o') -ne $e.Start) { $p = $null; break }
            Start-Sleep -Milliseconds 500; $waited++
        }
        $quitSeconds[$k] = $waited / 2
        if ($p) {
            try { $p.Kill(); $killed += $k; $p.WaitForExit(5000) | Out-Null } catch { }
            $p = Get-Process -Id $e.Id -ErrorAction SilentlyContinue
            if ($p -and $p.StartTime.ToUniversalTime().ToString('o') -eq $e.Start) { $survivors += $k }
        }
    }
    if ($survivors.Count -eq 0) { Remove-Item $pidFile -Force -ErrorAction SilentlyContinue }
    # Windows may relaunch a slow-closing application with /restore. While we still hold the lock, a windowless Office
    # process that did not exist when the run began and was created after it began is that relaunch; close it by id.
    $relaunched = @()
    Start-Sleep -Seconds 10
    foreach ($n in $(if ($lockHeld -and $started -and $null -ne $prePids) { @('WINWORD', 'EXCEL', 'POWERPNT') } else { @() })) {
        foreach ($p in @(Get-Process -Name $n -ErrorAction SilentlyContinue)) {
            if ($prePids -contains $p.Id) { continue }
            if ($p.StartTime -lt $started) { continue }
            if ($p.MainWindowHandle -ne 0) { continue }
            try { $p.Kill(); $relaunched += ($n + ':' + $p.Id) } catch { }
        }
    }
    if ($lockHeld -and $lockPath -and (Test-Path $lockPath)) { Remove-Item $lockPath -Force }
    if ($out) {
        $left = @()
        foreach ($n in @('WINWORD', 'EXCEL', 'POWERPNT')) { $left += @(Get-Process -Name $n -ErrorAction SilentlyContinue | ForEach-Object { $_.Name }) }
        Write-Line @{ kind = 'end'; lockReleased = [bool]$lockHeld; officeProcessesLeft = $left; launchedPidsKilled = $killed; launchedPidsSurvived = $survivors; secondsToExitAfterQuit = $quitSeconds; relaunchedAndClosed = $relaunched }
        [IO.File]::WriteAllText((Join-Path $out 'done.json'), '{"done":true}', $utf8)
    }
}
