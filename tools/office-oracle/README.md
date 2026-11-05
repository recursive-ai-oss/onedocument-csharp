# office-oracle

Drives desktop Word, Excel and PowerPoint on a Windows machine, black-box, and brings the answers back as JSON
lines. It exists so that OneOffice's output can be checked against what the real applications do with it: does the
file open clean, does the application offer to repair it, how many pages, what list label does each paragraph get.

Only files that were generated (by `make-cases.py`, by `DocxProbe generate`, by OneOffice itself from generated
input) may be sent. Downloaded corpus files never leave the build host.

## Moving parts

| Part | Where | Job |
|---|---|---|
| `office-oracle.sh` | build host | `install` places the scripts and registers the task; `run` copies inputs, starts the task, waits, pulls results. |
| `laptop/Install.ps1` | Windows | Registers the on-demand task `OneOfficeOracle`, logon type *Interactive* (runs only while the user is logged on). |
| `laptop/Run-Oracle.ps1` | Windows, inside the task | Takes the shared lock, opens each file in its application through COM, writes `results.jsonl`, quits what it started, releases the lock. |
| `make-cases.py` | build host | Writes the small clean and deliberately damaged files and the style-precedence document. |

Office automation needs the signed-in user's desktop, so SSH is only used to copy files, start the task and fetch
results. Everything lives under `<profile>\oneoffice\` on the machine (`driver\`, `jobs\<run>\`). The one file outside
it is the shared lock `<profile>\oracle.lock`.

## Environment

| Variable | Meaning |
|---|---|
| `OFFICE_ORACLE_SSH` | `user@host` of the Windows machine (required). |
| `OFFICE_ORACLE_SSH_KEY` | Private key file (optional). |
| `ONEOFFICE_HOME` | Results go to `$ONEOFFICE_HOME/results/office/<run>/` unless `--out` is given. |
| `ONEOFFICE_INSTANCE` | Name written into the lock (default `oneoffice`). |

## Use

    office-oracle.sh install
    make-cases.py DIR
    office-oracle.sh run --run NAME --instance ID [--pdf] [--saveas] [--probe] FILE|DIR...

`--pdf` exports a PDF from the application, `--saveas` (Word) re-saves as DOCX, `--probe` (Word) adds the
per-paragraph answers, `--lock-path`, `--lock-retry`, `--lock-wait` change the lock (tests use a private lock file),
`--trace` logs each step. A batch is limited to 25 minutes.

## The laptop lock

The machine is shared. Before touching Office the task creates `<profile>\oracle.lock` with `CreateNew`, which fails
if the file exists, and writes `project=oneoffice`, `instance=` and `started=` (UTC) into it. If the file exists the
task waits (60 s between tries, 20 min in total by default). A lock older than two hours is treated as stale, removed,
and reported in the `lock` result line. The lock is removed in a `finally` block.

## Results

`results.jsonl`, one object per line:

| `kind` | Fields |
|---|---|
| `lock` | `acquired`, `waitedSeconds`, `staleRemoved` |
| `env` | `word`, `excel`, `powerpoint` (file versions), `fontFamilyCount`, `fontsPresent`, `fontsAbsent`, `os` |
| `app` | `app`, `version`, `build` as the application reports them |
| `file` | `file`, `app`, `outcome`, `dialogs`, `error`, `seconds`, plus `pages`/`paragraphs`/`paragraphInfo` (Word), `sheets` (Excel), `slides` (PowerPoint), `exportedPdf`, `savedDocx` |
| `end` | `lockReleased`, `officeProcessesLeft` |

`outcome` is one of:

* `clean`: opened with no dialog and no error.
* `repair-prompt`: the file needs repair. Either the application raised a dialog that offers repair or recovery (text and
  buttons in `dialogs`; the watcher answers with the non-repairing button), or the open failed as is and succeeded
  with repair switched on (`needsRepair`, the first error in `firstOpenError`). On the tested build Word refuses a file
  that needs repair without showing a dialog when it runs hidden, so the second form is the one that fires.
* `unreadable`: the open call failed with an error saying the file is corrupt or unreadable, and no dialog appeared.
* `error`: any other failure. `skipped`: not attempted (the owner's own copy of the application was running, or the
  batch ran out of time).

`fonts.txt` lists every installed font family.

## Detecting a repair prompt

Word is opened with `OpenAndRepair` off and alerts on; the others use their defaults. While a file is opening, a
watcher thread polls the application's top-level windows through UI Automation. A window that is not the main frame
and has buttons is a dialog: its text and button labels are recorded and the first of No, Cancel, Close, OK is
pressed. A dialog that cannot be answered within 60 s gets the application killed so the batch cannot hang.

## Notes from running it

* Every COM call that takes a string or a number gets explicitly typed arguments (`[string]`, `[int]`, `[bool]`). With
  untyped values some calls (PDF export, Save As) never return from PowerShell on the tested setup.
* Each item has a time limit (90 s by default); when it passes, the watcher kills the applications so a hung call
  cannot hold the lock.
* Documents opened hidden need `Activate()` before revisions can be accepted or rejected.
* If the owner already has Word, Excel or PowerPoint running, that application is skipped (`skipped`), never attached
  to or closed.
* Excel: a damaged workbook fails with a generic open error; the repair retry (`CorruptLoad`) did not open the cases
  tried so far, so they report `error`.
* Probe mode (`--probe`, Word) returns, per paragraph, the list label, style and font/paragraph values and page number;
  per section the header/footer texts and links; per table cell the row, column, bold/italic; the revision list; footnote
  and endnote number styles; and comments with their reply links. `NAME.actions.txt` beside a document lists revision
  actions (`accept-all`, `reject-all`, `accept:N`, `reject:N`, `accept-para:N`, `reject-para:N`) whose results are saved
  next to the results file.

## Grid mode (Excel)

`office-oracle.sh run --grid ...` opens each workbook in Excel, recalculates fully, and writes `NAME.answers.tsv`
beside the results (`id<TAB>text` per row, with tabs, newlines and backslashes escaped). A sheet named `text` is read
from column D (the result of a formula), any other sheet from column B (the displayed text of the cell), keyed by the
id in column A. The `grid` field of the result line records Excel's decimal and thousands separators, country code and
build. It is used by the number-format grid in `tools/NumFmtGrid`.

## Runs are self-contained

Each `run` registers its own one-off task, `OneOfficeOracle-<run>` (`laptop/Start-Run.ps1`), whose command line carries the
run's job directory (`Run-Oracle.ps1 -JobDir`), and deletes it when the results are back. There is no shared pointer file any
more, so two runs started at the same time cannot overwrite each other; they queue on the shared lock inside the script
(the one that gets the lock goes first, the other waits up to `--lock-wait`). Run names may contain only letters, digits,
`.`, `_` and `-`. `jobs\current.txt` is still honoured when the script is started by hand without `-JobDir`.

Every run ends by quitting the applications it started and killing, by process id, any it started that did not exit; the
result's `end` line lists the Office processes left, which should be empty unless the owner has the application open.
When `officeProcessesLeft` is not empty, look at the process's start time, parent and command line before anyone
kills it: it may be the owner's own copy, and only a process a run started may be killed.
