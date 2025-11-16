# AMS Mapping Configuration Guide

## Overview

The **AMS (Automatic Material System) Mapping** configuration is a critical component for multi-color print jobs when using Bambu Labs' AMS. This parameter defines the relationship between color indices in your print file and the physical AMS slot numbers, ensuring the printer loads the correct filament for each color in your print.

## Table of Contents

- [What is AMS Mapping?](#what-is-ams-mapping)
- [Structure and Format](#structure-and-format)
- [How AMS Mapping Works](#how-ams-mapping-works)
- [Configuration Rules](#configuration-rules)
- [Examples](#examples)
- [Common Patterns](#common-patterns)
- [Troubleshooting](#troubleshooting)
- [Best Practices](#best-practices)

## What is AMS Mapping?

AMS mapping tells the printer which AMS slot to use for each color index in your sliced print file. When you slice a multi-color model in BambuStudio or other slicers, each color is assigned an index (starting from 0). The `ams_mapping` array creates the bridge between these color indices and your physical AMS slots.

**Why is this important?**
- Ensures correct filament is loaded for each color
- Prevents print failures due to incorrect filament selection
- Allows for flexible filament arrangement in your AMS
- Required for multi-color prints to start successfully

## Structure and Format

The `ams_mapping` parameter is part of the MQTT print command payload:

```json
{
  "print": {
    "ams_mapping": [
      -1,
      -1,
      -1,
      1,
      0
    ],
    "use_ams": true,
    "file": "path/to/file.gcode",
    // ... other print parameters
  }
}
```

### Key Components

| Property | Type | Description |
|----------|------|-------------|
| `ams_mapping` | `number[]` | Array of 5 integers mapping color indices to AMS slots |
| Array Values | `-1` or `0-3` | `-1` = unused slot, `0-3` = AMS slot number |
| Array Length | Always `5` | Fixed length supporting up to 4 colors + padding |
| `use_ams` | `boolean` | Must be `true` when using AMS mapping |

## How AMS Mapping Works

### Reverse Indexing System

The AMS mapping uses a **right-to-left (reverse)** indexing system:

```
Array Index:    [0]  [1]  [2]  [3]  [4]
Color Index:     -    -    -    0    1
AMS Slot:       -1   -1   -1    2    0
                 ↑    ↑    ↑    ↑    ↑
              Unused           Rightmost = First Color
```

**How to read this:**
- **Position `[4]`** (rightmost): Maps **Color 0** (first color) to **AMS Slot 0**
- **Position `[3]`**: Maps **Color 1** (second color) to **AMS Slot 2**
- **Positions `[0-2]`**: Unused slots filled with `-1`

### Mapping Process

1. **Color indices** in your print file start from `0` (first color = 0, second color = 1, etc.)
2. **Array positions** fill from **right to left** as you add more colors
3. **AMS slot numbers** are zero-indexed: `0, 1, 2, 3` for a standard 4-slot AMS
4. **Unused positions** at the beginning are filled with `-1`

## Configuration Rules

### Critical Rules

✅ **DO:**
- Always use exactly **5 elements** in the array
- Fill unused positions with `-1` at the **beginning** of the array
- Assign colors from **right to left**
- Use **zero-indexed** AMS slot numbers (0, 1, 2, 3)
- Set `"use_ams": true` when using AMS mapping
- Verify filament types and colors match your mapping

❌ **DON'T:**
- Use more or fewer than 5 array elements
- Place `-1` values at the end of active mappings
- Use AMS slot numbers outside the range 0-3
- Forget to set `use_ams: true`
- Start print without verifying filament is loaded in mapped slots

### Array Length Rules

| Colors Used | Pattern | Description |
|-------------|---------|-------------|
| **1 color** | `[-1, -1, -1, -1, X]` | Single color uses AMS slot X |
| **2 colors** | `[-1, -1, -1, X, Y]` | Two colors map to slots X and Y |
| **3 colors** | `[-1, -1, X, Y, Z]` | Three colors map to slots X, Y, and Z |
| **4 colors** | `[-1, W, X, Y, Z]` | Four colors map to slots W, X, Y, and Z |

## Examples

### Example 1: Single Color Print

**Scenario:** Print using only one color from AMS slot 2

```json
{
  "ams_mapping": [-1, -1, -1, -1, 2],
  "use_ams": true
}
```

**Explanation:**
- Color 0 (first/only color) → AMS Slot 2
- All other positions unused (`-1`)

---

### Example 2: Two Color Print

**Scenario:** Print with two colors - Red in slot 0, Blue in slot 3

```json
{
  "ams_mapping": [-1, -1, -1, 0, 3],
  "use_ams": true
}
```

**Explanation:**
- Color 0 (first color) → AMS Slot 0 (Red)
- Color 1 (second color) → AMS Slot 3 (Blue)
- First three positions unused (`-1`)

---

### Example 3: Three Color Print

**Scenario:** Print with three colors - slots 1, 2, and 3

```json
{
  "ams_mapping": [-1, -1, 1, 2, 3],
  "use_ams": true
}
```

**Explanation:**
- Color 0 → AMS Slot 1
- Color 1 → AMS Slot 2
- Color 2 → AMS Slot 3
- First two positions unused (`-1`)

---

### Example 4: Four Color Print (Maximum)

**Scenario:** Print using all four AMS slots

```json
{
  "ams_mapping": [-1, 0, 1, 2, 3],
  "use_ams": true
}
```

**Explanation:**
- Color 0 → AMS Slot 0
- Color 1 → AMS Slot 1
- Color 2 → AMS Slot 2
- Color 3 → AMS Slot 3
- Only first position unused (`-1`)

---

### Example 5: Non-Sequential Slot Usage

**Scenario:** Using only slots 0 and 2 (skipping slot 1)

```json
{
  "ams_mapping": [-1, -1, -1, 0, 2],
  "use_ams": true
}
```

**Explanation:**
- Color 0 → AMS Slot 0
- Color 1 → AMS Slot 2
- Slot 1 is not used in this print
- This is perfectly valid!

## Common Patterns

### Standard Sequential Mapping

Most common for prints where you use consecutive AMS slots:

```json
// 1 color from slot 0
[-1, -1, -1, -1, 0]

// 2 colors from slots 0-1
[-1, -1, -1, 0, 1]

// 3 colors from slots 0-2
[-1, -1, 0, 1, 2]

// 4 colors from slots 0-3
[-1, 0, 1, 2, 3]
```

### Custom Slot Selection

When you want to use specific slots (e.g., different filament types in non-consecutive slots):

```json
// Using only black (slot 0) and white (slot 3)
[-1, -1, -1, 0, 3]

// Using specific filament combinations
[-1, -1, -1, 2, 1]  // Slot 2 for color 0, slot 1 for color 1
```

## Troubleshooting

### Problem: Printer Won't Start / Pauses Immediately

**Possible Causes:**
1. ❌ Incorrect AMS mapping configuration
2. ❌ Filament not loaded in mapped slots
3. ❌ `use_ams` not set to `true`
4. ❌ Wrong array length (not 5 elements)

**Solutions:**
- ✅ Verify mapping array has exactly 5 elements
- ✅ Check that specified AMS slots contain filament
- ✅ Ensure `use_ams: true` is set
- ✅ Verify color count matches your sliced file

### Problem: Wrong Filament Loaded

**Possible Causes:**
1. ❌ Mapping doesn't match actual AMS slot contents
2. ❌ Reverse indexing misunderstood

**Solutions:**
- ✅ Double-check which color index corresponds to which AMS slot
- ✅ Remember: rightmost array position = first color (color 0)
- ✅ Verify physical filament colors match your slicer settings

### Problem: Print File Says 3 Colors, But Only Using 2

**Possible Causes:**
1. ❌ Unused color in slicer (e.g., support interface using same color)
2. ❌ Mapping includes unnecessary color index

**Solutions:**
- ✅ Check slicer preview for actual color usage
- ✅ Adjust mapping to match actual colors used
- ✅ Use only as many positions as colors actually used

## Best Practices

### Before Starting a Print

1. **Verify Slicer Settings**
   - Check how many colors your model uses
   - Note which colors are assigned to which model parts
   - Ensure color count matches your mapping

2. **Check Physical AMS**
   - Confirm all mapped slots have filament loaded
   - Verify filament types match print requirements
   - Check filament remaining percentage

3. **Validate Mapping Configuration**
   - Array length is exactly 5
   - Right-to-left assignment matches color count
   - AMS slot numbers are valid (0-3)
   - `use_ams: true` is set

4. **Test Configuration**
   - Consider testing mapping with a small multi-color test print first
   - Monitor first layer to ensure correct colors are loaded

### During Development

When implementing AMS mapping in code:

```typescript
// Type-safe interface
interface AMSMapping {
  ams_mapping: [number, number, number, number, number]; // Tuple of exactly 5
  use_ams: boolean;
}

// Helper function to create mapping
function createAMSMapping(slotMappings: number[]): number[] {
  if (slotMappings.length > 4) {
    throw new Error('Cannot map more than 4 colors');
  }
  
  // Pad with -1 at the beginning
  const padding = Array(5 - slotMappings.length).fill(-1);
  return [...padding, ...slotMappings];
}

// Example usage
const mapping = createAMSMapping([0, 2]); // [-1, -1, -1, 0, 2]
```

### Monitoring and Logging

When monitoring prints:
- Log the AMS mapping when print starts
- Track which tray is currently active (`tray_now` from MQTT)
- Monitor for AMS-related errors (operation code 26: `PAUSED_AMS_OFFLINE`)
- Display active filament info in overlays

## Integration with This Project

This streaming overlay project monitors AMS status in real-time through MQTT:

- **`AMSStatus` interface**: Contains filament info, tray status, humidity, temperature
- **`FilamentInfo` interface**: Details about each filament slot
- **Real-time tracking**: Active tray highlighting via `tray_now` field
- **Progress overlay**: Shows all 4 AMS trays with color and remaining filament

See the following files for implementation details:
- `src/types/printer.ts` - Type definitions for AMS data
- `public/overlays/progress-ams.html` - AMS status display
- `public/js/progress-ams.js` - AMS data handling

## Additional Resources

- [Bambu Labs Official Documentation](https://wiki.bambulab.com/)
- [BambuStudio Source Code](https://github.com/bambulab/BambuStudio)
- Project `OVERLAYS.md` - Overlay features including AMS monitoring
- Project `README.md` - Setup and MQTT configuration

---

## Summary

**Key Takeaways:**
- AMS mapping is a 5-element array mapping color indices to AMS slots
- Uses reverse (right-to-left) indexing with `-1` padding at start
- Must set `use_ams: true` when using AMS mapping
- Printer will pause/not start if mapping is incorrect
- Verify physical filament matches mapping before printing

**Quick Reference:**
```json
{
  "ams_mapping": [-1, -1, -1, slot_for_color_0, slot_for_color_1],
  "use_ams": true
}
```

For questions or issues, please open an issue on the [GitHub repository](https://github.com/KainCH/riress-streaming).
