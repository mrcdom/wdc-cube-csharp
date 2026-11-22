# 0008. Sealed addresses with AES-SIV, built on the platform's AES

## Status

Accepted (2026-11-18).

## Context

The address carries the state needed to rebuild a face of the cube (ids, pages). Some applications do not want that
state readable or editable in the address bar. The TypeScript implementation seals the query with AES-SIV, a
deterministic authenticated encryption (RFC 5297): `_e=base64url([version][flags][V ‖ C])`, a 32-byte key split
into a CMAC key and a CTR key (AES-128), raw deflate only when it makes the text shorter, and key versions for
rotation. .NET has no AES-SIV, and on WebAssembly it has no AES at all.

## Decision

- Implement the AES-SIV construction (S2V with CMAC, then CTR) in the project, on top of
  `System.Security.Cryptography.Aes` used one block at a time.
- Ship it as an optional package, `Wdc.Cube.Codec` (`AesHistoryCodec`), behind the core's `IHistoryCodec`.
- Keep the TypeScript envelope and API: a current key version that seals, previous versions that still open.
- Be byte-compatible with the TypeScript implementation: the empty nonce is passed to S2V as one empty associated-data
  component, `S2V(K1, ["", plaintext])`, as the TypeScript library does.
- Validate against the RFC 5297 test vectors and against addresses produced by the TypeScript implementation.
- A tampered address, or one sealed with another key, does not open (`Decode` returns `null`); the framework compares
  only plain text, never envelopes.
- The codec does not create, store or transport keys; that is the application's business.

## Alternatives considered

- **A third-party cryptography library** (everywhere, or only on WebAssembly): a large dependency for one construction.

## Consequences

- On Blazor WebAssembly the codec is unavailable: constructing it throws `PlatformNotSupportedException`, and addresses
  stay plain.
- Each side reads what the other compresses, but compressed output may differ between implementations (compression
  levels); this only affects long addresses.
- As an example of key management, the Shopping sample derives a key per user at login, so a link sealed for one user
  opens for another without its parameters.

See [History](../concepts/history.md#sealed-addresses).
