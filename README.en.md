# Chess Scoresheet Printer

[Türkçe](README.md) | English

[![CI](https://github.com/TunahanDilercan/chess-scoresheet-printer/actions/workflows/ci.yml/badge.svg)](https://github.com/TunahanDilercan/chess-scoresheet-printer/actions/workflows/ci.yml)
[![Release](https://img.shields.io/github/v/release/TunahanDilercan/chess-scoresheet-printer)](https://github.com/TunahanDilercan/chess-scoresheet-printer/releases/latest)

Chess Scoresheet Printer is a Windows desktop tool for placing tournament pairing information on pre-printed chess scoresheets. It works with Chess-Results and Swiss-Manager exports.

The application is used at tournaments run by two provincial organizations of the Turkish Chess Federation. It is an independent project and is not an official application of the federation.

<p align="center">
  <img src="docs/images/main-window.png" alt="Chess Scoresheet Printer main window" width="760">
</p>

## Main functions

- Search tournaments and retrieve pairings from Chess-Results (byes and unpaired players are recognised)
- Per-category system support: Swiss, round robin (Berger) and team tournaments, read from each category's "tournament type"
- Team matches: board numbers such as "1.2" and team names (printed in the Club boxes)
- Quick-access buttons showing each category's latest round and printing it in one click (F5 refreshes them all)
- Bye boards are not printed by default, only when ticked manually in the list
- Tournament regulations, report and technical-meeting minutes: built-in templates or your own .docx are filled with Chess-Results data and the regulations published on the province's TSF website; every field is edited in one table; output as PDF, to a printer or as Word
- Official time-control suggestions by tempo (standard, rapid, blitz)
- Schedule and category checks against the TSF competition-regulations procedure: minimum time between round starts by tempo (e.g. 35+30 → 150 min), rounds per day (4 for national rating, 3 for FIDE; 90+30 and 2 per day with a 2400+ player), 12-hour day limit, check-in window (30–60 min), system and rounds by number of players, the same tempo across categories, "and under" categories ending on an even age; "suggest schedule" computes round times
- Category table cards (A4 landscape): tournament poster at the top, large category name below; one theme colour per category with two styles (coloured text on white, or white text on a coloured band); poster cropping tool (card slot, 16:9, 4:3, 1:1 or free) and an edge-to-edge full-width option; embedded fonts (Montserrat, Oswald, Inter) plus fonts you download yourself (e.g. Satoshi, which its licence does not allow us to redistribute — download it from Fontshare and add it with "+"); copies default to the category's board count
- Arbiter badges (85×54 or 90×60 mm, 2×5 / 2×4 per A4 with crop marks): logo, photo, name, role and a grade-coloured strip
- Silent printing to the configured printer, tray and copy count without dialogs
- Import JSON, CSV, TXT and XLSX files
- Choose the boards to print by ticking them in a list (remembered separately for each category and round)
- Batch-print by round or category
- Create print templates for pre-printed scoresheets
- Adjust field positions and text sizes
- Printer alignment offset (mm) with an alignment test page
- Configure excluded boards, copy count and page size
- Print preview with a faded scoresheet background, and PDF output

## Usage

1. Select a Chess-Results tournament or open a pairing file. Choosing a province opens its most recently added tournament automatically; search and link/number also work.
2. Select the category and round. Pairings load into the list on the right automatically; the default round is the latest round with published pairings (for round robins, the first round without results). A quick-access button (e.g. "🖨 7 Yaş · T2") sends that category's latest round straight to printing. Press F5 after a new round is published to refresh every button.
3. Untick boards that should not be printed and set the number of copies (default 4). Selections are remembered, and "All categories" and the quick-access buttons respect them.
4. Press **Print** (Ctrl+P) to open the preview. The pre-printed sheet is shown faded in the preview but is never printed. Choose a printer and print; selections and filters stay as they were.

The **📄 Yönerge / Tutanak** button in the header takes tournament details from Chess-Results (dates, venue, system, time control, categories, director, arbiters, round schedule) and also finds the regulations on the default province's TSF website (e.g. isparta.tsf.org.tr), reading the registration deadline, contact details, organiser and opening times. Built-in templates are the regulations and the technical-meeting minutes; values entered for a tournament carry over to its minutes. All fields are edited in one table with their source shown. Your own .docx templates may use `{{IL}}`-style markers, Word bookmarks, content controls or labelled table cells; formatting, tables and locked regions are preserved. "Preview and print" shows the document page by page before printing; printing and saving as PDF are done from the preview. Converting to PDF, preview and printing require Microsoft Word; without Word the document is saved as .docx.

**🏷 Masa Kartları** prints an A4 (landscape by default) card for each category. Each category gets its own colour, which is remembered; background and text colour can be changed in the table.

**🪪 Yaka Kartları** prints arbiter badges from the Chess-Results arbiter list; the grade is suggested from IA/FA/NA and can be set to a TSF grade, which is remembered.

**Settings → Printer** sets the target printer, paper tray, copy count and silent printing. With silent printing on, jobs go straight to the printer without preview or print dialog.

If the text is offset from the boxes on the pre-printed sheet, correct it in millimetres under **Settings → Printer alignment** and check it with the **alignment test** page; the template itself does not need to change. Venue, arbiter and time-control details are intentionally not printed.

The template editor allows tournament, date, category, round, board, player and team fields to be positioned separately. The default template is "Ana Örnek 2"; the previous layout remains available as "Ana Örnek". **Settings → Template by system** lets Swiss, round-robin and team categories use different templates.

<p align="center">
  <img src="docs/images/template-designer.png" alt="Pre-printed scoresheet template designer" width="720">
</p>

## Download

The Windows x64 package is available on the [Releases](https://github.com/TunahanDilercan/chess-scoresheet-printer/releases/latest) page. After extracting the ZIP archive, run `ChessScoresheetPrinter.exe`. The package includes the .NET runtime and is intended for Windows 10 and Windows 11.

The published executable is not code-signed, so Windows may display a SmartScreen warning on first launch.
