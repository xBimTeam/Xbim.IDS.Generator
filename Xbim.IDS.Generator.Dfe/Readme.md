# Xbim.IDS.Generator.Dfe

Generates IDS (Information Delivery Specification) files that help assure IFC models meet the Department for Education's Information Management Requirements (IMR). The IMR includes the Projects Information Standard (PIS) and the Exchange Information Requirements (EIR).

It also builds two demo IFC models against DfE conventions that can be used to test the generated IDS files.

Raise a GitHub issue with any issues identified in the IDS produced via the generator.

---

## IMR Versions

Two IMR versions are supported, selectable via the `--s21` flag:

| Version | Flag | Status |
|---|---|---|
| **S25** *(default)* | *(omit flag)* | Current standard — available on [GOV.UK](https://www.gov.uk/government/collections/school-design-and-construction) |
| **S21** | `--s21` | Previous standard — legacy projects |

---

## Setup

### Visual Studio 2022

1. Open `Xbim.IDS.Generator.sln`.
2. Restore NuGet packages — VS will prompt automatically, or right-click the Solution → **Restore NuGet Packages**.
3. Build: **Build → Build Solution** (`Ctrl+Shift+B`).

### VS Code

1. Install the [C# Dev Kit](https://marketplace.visualstudio.com/items?itemName=ms-dotnettools.csdevkit) extension.
2. Open the **repo root folder** (`Xbim.IDS.Generator/`) — opening a subfolder will break the `.vscode` launch config.
3. NuGet packages restore automatically on open. To trigger manually: `dotnet restore`.
4. Build: `Ctrl+Shift+B` or `dotnet build` in the terminal.

---

## Running

`cd` into the project directory first, then:

```bash
dotnet run [flags]
```

**F5 / Debug:** A `.vscode/launch.json` is pre-configured for the DfE project. Output appears in the **Debug Console** tab.

---

## CLI Flags

| Flag | Default | Description |
|---|---|---|
| `--s21` | *(S25)* | Generate against the S21 IMR. Omit for S25 (current). |
| `--status=VALUE` | `Sn` | IDS status code — appears in all output filenames. |
| `--revision=VALUE` | `Pnn` | IDS revision code — appears in all output filenames. |
| `--bs=N` | `3` | Number of above-ground storeys (1–5). Controls per-storey rules for elevation (04_08) and height (04_09). |
| `--uniclass-version=VALUE` | *(latest)* | Pin Uniclass 2015 SL and EN tables to a specific release, e.g. `1_32`. |
| `--nrm-version=VALUE` | *(latest)* | Pin NRM cost classification to a specific edition year, e.g. `2016`. |
| `--sfg20-version=VALUE` | *(latest)* | Pin SFG20 FM classification to a specific release year, e.g. `2023`. |

### Status codes

| Value | Meaning |
|---|---|
| `Sn` *(default)* | Placeholder — no formal status assigned |
| `S2` | Shared — not for publication |
| `S3` | Ready for review / comment |
| `A` | Ready for publication |

### Revision codes

| Value | Meaning |
|---|---|
| `Pnn` *(default)* | Placeholder — no revision assigned |
| `P01`, `P02` … | Preliminary issues |
| `C01`, `C02` … | Published / confirmed issues |

### Building storeys (`--bs=N`)

Sets how many above-ground storeys the building has. Levels are activated in order from ground up:

| Value | Active levels |
|---|---|
| `1` | 00 |
| `2` | 00, 01 |
| `3` *(default)* | 00, 01, 02 |
| `4` | 00, 01, 02, 03 |
| `5` | 00, 01, 02, 03, 04 |

### Classification versioning (`--uniclass-version`, `--nrm-version`, `--sfg20-version`)

Classification code files (Uniclass SL/EN, NRM, SFG20) can change between releases — codes may be renumbered or renamed. By default the generator uses the latest bundled version. To pin to a specific release, pass the appropriate flag.

Version tokens use underscores in place of dots (e.g. `1_32` for v1.32) to avoid conflicts with file path separators.

- Versioned files are bundled alongside the latest: `SL_Codes_1_32.txt`, `EN_Codes_1_32.txt`, etc.
- If a requested version file is not found, a warning is printed and the latest is used.
- When pinned, the chosen version is recorded in the generated IDS description (e.g. `[Classification versions: Uniclass SL/EN v1.32]`).
- The S21/S25 TypeCodes files declare which Uniclass SL version their mappings were authored against (via a `# uniclass-sl-version:` header). A mismatch between that declaration and `--uniclass-version` produces a warning on stderr.

### Examples

```bash
# S25, default 3 storeys — placeholder status/revision in filenames
dotnet run

# Single-storey building, published issue
dotnet run --bs=1 --status=A --revision=C01

# Four-storey building, S21, first working draft
dotnet run --bs=4 --s21 --status=S2 --revision=P01

# Pin Uniclass SL/EN to version 1.32
dotnet run --uniclass-version=1_32

# Pin Uniclass 1.32, published S25 issue
dotnet run --uniclass-version=1_32 --status=A --revision=C01
```

---

## Output Files

Files are written to `Outputs/` relative to the project directory, organised by IMR version:

```
Outputs/
  S25/
    IDS/
      ER-DFE-XX-XX-L-X-0030-Information Model Assurance Stage 3-{status}-{revision}.ids
      Individual/Stage_N/   ← one .ids file per rule
      Grouped/Stage_N/      ← one .ids file per domain group
    IFC/
      ER-DFE-XX-XX-M3-X-0044-...-Spatial.ifc
      ER-DFE-XX-XX-M3-X-0044-...-MetaData.ifc
  S21/
    IDS/  ...
    IFC/  ...
```
