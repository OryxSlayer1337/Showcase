# Shifted BigInts - Code Showcase

Extracted arrow-notation scaling arithmetic and the supporting short-suffix number
types from **Absentia**. The gameplay
amounts in that game pass through this code whenever an item with a big "value",
"feed power", or "fame" number is rendered to the client, so these classes sit in
the hot path of every economy-based packet.

Every source file below is a verbatim copy of the production implementation,
again apart from the documented branch-level tweaks. The public API is exactly
what the game uses.

## Production Path Mapping

| Showcase file | Production source | Shared | Match |
|---|---|---|---|
| `FNA Client Side\BigDouble.cs` | `Vortex FNA Engine\src\Core\BigDouble.cs` | &mdash; | Byte-identical |
| `FNA Client Side\BigExp.cs` | `Vortex FNA Engine\src\Core\BigExp.cs` | &mdash; | Byte-identical |
| `FNA Client Side\BigIntChunked.cs` | `Vortex FNA Engine\src\Core\BigIntChunked.cs` | &mdash; | Byte-identical |
| `FNA Client Side\NumberDisplay.cs` | `Vortex FNA Engine\src\Core\NumberDisplay.cs` | &mdash; | Byte-identical |
| `FNA Client Side\NumberDisplayScales.cs` | `Vortex FNA Engine\src\Core\NumberDisplayScales.cs` | &mdash; | Byte-identical |
| `Server Side\BigDouble.cs` | `Vortex Server\common\BigDouble.cs` | shared, client &amp; server | Byte-identical |
| `Server Side\BigExp.cs` | `Vortex Server\common\BigExp.cs` | shared, client &amp; server | Byte-identical |
| `Server Side\BigIntChunked.cs` | `Vortex Server\common\BigIntChunked.cs` | shared, client &amp; server | Byte-identical |
| `Server Side\BigIntUtils.cs` | `Vortex Server\common\BigIntUtils.cs` | server-only | 1 added line, see below |
| `Server Side\NReader.cs` | `Vortex Server\common\NReader.cs` | server-only | Trailing line-ending drift |
| `Server Side\NWriter.cs` | `Vortex Server\common\NWriter.cs` | server-only | Trailing line-ending drift |
| `Server Side\StringInt.cs` | `Vortex Server\common\StringInt.cs` | server-only | Byte-identical |

### Deltas from production

- **`BigIntUtils.cs`** - identical to production except a single additional
  `using VortexClient.Core.Numbers;` line at the top. This copy exists for the
  **benchmark** harness (it links the client and server halves into one .NET 10
  console app without the original solution/namespace structure); the **server**
  build gets the file as-is with no `using`. The full `AbbrevScales` table
  (K up to `YZCePi`, exponents 0&ndash;462) and every helper method are unchanged.
- **`NReader.cs` / `NWriter.cs`** - same content line-for-line; production ends
  with one extra trailing blank line (CRLF drift from the repo's `.gitattributes`).
  No functional difference.
- Everything else is byte-identical, CRLF-normalized.

## What's interesting

- **Short suffixes go farther than the base game.** The production
  `AbbrevScales` table goes out to 10^462 (`YZCePi`), versus the vanilla
  ~10^18 (&ldquo;Qd&rdquo;) the unmodified client hard-codes.
- **Exponent delta is the key.** A number &ge; 10^exponent gets the suffix shown;
  the table is the contract trusted by both `client` and `server`, and it has to
  stay in lockstep with what the game writes to the ledger.
- **One implementation, two halves.** The `FNA Client Side` (client render) and
  `Server Side` (server persistence) versions are literally the same real
  one &mdash; checked byte-for-byte against the production repo.