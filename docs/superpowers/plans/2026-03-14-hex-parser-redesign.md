# Hex Parser Redesign Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rework hex parsing so extraction and packet boundary detection follow the approved text rules, always exporting every extracted packet fragment without parse failures.

**Architecture:** Keep `HexInputParser` as the single parsing entry point, but replace protocol-validation-driven splitting with block-based extraction and line-based Ethernet boundary recognition. Preserve `ParseResult` for compatibility, keep `Errors` empty, and update the WPF summary logic to report exported packet count only.

**Tech Stack:** C# 5, .NET Framework 4.8, WPF, custom console test runner

---

## Chunk 1: Parser Behavior and Tests

### Task 1: Rewrite parser tests around the new rules

**Files:**
- Modify: `tests/HexToPcap.Tests/Program.cs`
- Reference: `docs/superpowers/specs/2026-03-14-hex-parser-redesign-design.md`

- [ ] **Step 1: Write failing parser tests for the new boundary and extraction rules**

```csharp
new KeyValuePair<string, Action>("IgnoresOffsetPrefixesInPlainHex", IgnoresOffsetPrefixesInPlainHex),
new KeyValuePair<string, Action>("PadsOddHexTokensInsteadOfFailing", PadsOddHexTokensInsteadOfFailing),
new KeyValuePair<string, Action>("OutputsIncompletePacketsWithoutErrors", OutputsIncompletePacketsWithoutErrors),
new KeyValuePair<string, Action>("SplitsOnlyWhenNewEthernetHeaderStartsOnNewLine", SplitsOnlyWhenNewEthernetHeaderStartsOnNewLine),
new KeyValuePair<string, Action>("IgnoresTcpdumpAsciiAndOffsetPrefixBytes", IgnoresTcpdumpAsciiAndOffsetPrefixBytes),
new KeyValuePair<string, Action>("KeepsTcpdumpPacketsWhenOffsetsJump", KeepsTcpdumpPacketsWhenOffsetsJump),
```

- [ ] **Step 2: Run tests to verify they fail for the expected reasons**

Run: `powershell -ExecutionPolicy Bypass -File .\build.ps1 -Configuration Release`
Expected: parser tests fail because current code still records errors, still validates protocol lengths, and still treats some inputs as failures.

- [ ] **Step 3: Expand or replace obsolete failure-oriented assertions**

```csharp
AssertCounts(result, 1, 0);
AssertSequenceEqual(expected, result.SuccessfulPackets[0], "...");
```

- [ ] **Step 4: Re-run tests to keep the suite red for parser behavior only**

Run: `powershell -ExecutionPolicy Bypass -File .\build.ps1 -Configuration Release`
Expected: test failures remain isolated to parser behavior mismatches.

### Task 2: Replace validation-driven parsing with extraction-driven parsing

**Files:**
- Modify: `src/HexToPcap.Core/Services/HexInputParser.cs`
- Reference: `docs/superpowers/specs/2026-03-14-hex-parser-redesign-design.md`

- [ ] **Step 1: Implement plain-block token extraction that ignores `0x` prefixes and line-start offset prefixes**

```csharp
private static byte[] ExtractPlainLineBytes(string line)
{
    // Remove optional line-start offset prefix like 0x0010:
    // Ignore invalid tokens, pad odd token lengths with trailing 0.
}
```

- [ ] **Step 2: Implement line-based packet boundary detection using common Ethernet EtherTypes**

```csharp
private static bool StartsWithRecognizedEthernetHeader(byte[] lineBytes)
{
    // Requires at least 14 bytes and one of the configured EtherTypes.
}
```

- [ ] **Step 3: Rework tcpdump parsing to treat `0x0000:` as metadata and ignore ASCII tails without validating offsets**

```csharp
if (offset == 0 && currentPacket.Count > 0)
{
    packets.Add(currentPacket.ToArray());
    currentPacket.Clear();
}
```

- [ ] **Step 4: Remove parse-failure paths so every extracted packet fragment is returned and `Errors` stays empty**

```csharp
return new ParseResult(packets, new List<PacketParseError>());
```

- [ ] **Step 5: Run tests to verify the parser turns green**

Run: `powershell -ExecutionPolicy Bypass -File .\build.ps1 -Configuration Release`
Expected: parser and writer tests pass.

## Chunk 2: UI Summary and Documentation

### Task 3: Align the UI summary with the no-failure parsing model

**Files:**
- Modify: `src/HexToPcap/ViewModels/MainWindowViewModel.cs`

- [ ] **Step 1: Update conversion summary text to report export count without failed packet counts**

```csharp
SummaryText = string.Format("成功导出 {0} 个 | {1}", packetCount, fileName);
```

- [ ] **Step 2: Keep the existing empty-state message for zero extracted packets**

Run: `powershell -ExecutionPolicy Bypass -File .\build.ps1 -Configuration Release`
Expected: build and tests still pass after the view-model change.

### Task 4: Refresh README rules to match the new parser

**Files:**
- Modify: `README.md`

- [ ] **Step 1: Update the parsing rules section to describe blank-line boundaries, line-start Ethernet boundaries, `0x` prefix handling, and odd-digit padding**
- [ ] **Step 2: Remove claims about parse-error reporting and protocol-length-based split failures**

Run: `powershell -ExecutionPolicy Bypass -File .\build.ps1 -Configuration Release`
Expected: documentation changes do not affect the build; command still passes.

### Task 5: Final verification

**Files:**
- Inspect: `src/HexToPcap.Core/Services/HexInputParser.cs`
- Inspect: `src/HexToPcap/ViewModels/MainWindowViewModel.cs`
- Inspect: `tests/HexToPcap.Tests/Program.cs`
- Inspect: `README.md`

- [ ] **Step 1: Run the full verification command**

Run: `powershell -ExecutionPolicy Bypass -File .\build.ps1 -Configuration Release`
Expected: 0 build errors, all tests pass.

- [ ] **Step 2: Review the diff for unintended changes**

Run: `git status --short`
Expected: only the intended parser, UI, test, plan, and docs files are modified in the worktree.
