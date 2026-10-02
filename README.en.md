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
- Import JSON, CSV, TXT and XLSX files
- Choose the boards to print by ticking them in a list
- Batch-print by round or category
- Create print templates for pre-printed scoresheets
- Adjust field positions and text sizes
- Printer alignment offset (mm) with an alignment test page
- Configure excluded boards, copy count and page size
- Print preview with a faded scoresheet background, and PDF output

## Usage

1. Select a Chess-Results tournament (by province, search or link/number) or open a pairing file.
2. Select the category and round. Pairings load into the list on the right automatically; the default round is the latest round with published pairings.
3. Untick boards that should not be printed and set the number of copies.
4. Press **Print** (Ctrl+P) to open the preview. The pre-printed sheet is shown faded in the preview but is never printed. Choose a printer and print; the next category is then loaded automatically.

If the text is offset from the boxes on the pre-printed sheet, correct it in millimetres under **Settings → Printer alignment** and check it with the **alignment test** page; the template itself does not need to change. Venue, arbiter and time-control details are intentionally not printed.

The template editor allows tournament, date, category, round, board and player fields to be positioned separately.

<p align="center">
  <img src="docs/images/template-designer.png" alt="Pre-printed scoresheet template designer" width="720">
</p>

## Download

The Windows x64 package is available on the [Releases](https://github.com/TunahanDilercan/chess-scoresheet-printer/releases/latest) page. After extracting the ZIP archive, run `ChessScoresheetPrinter.exe`. The package includes the .NET runtime and is intended for Windows 10 and Windows 11.

The published executable is not code-signed, so Windows may display a SmartScreen warning on first launch.
