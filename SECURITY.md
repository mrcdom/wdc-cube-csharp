# Security

## Reporting a vulnerability

Report it privately, through **GitHub's private vulnerability reporting**: the repository's *Security* tab →
*Report a vulnerability*. Please do not open a public issue or pull request for it.

Say what is affected (a package and version, or a sample), how to reproduce it, and what an attacker gains. You will
get an answer as soon as possible; a fix for a package goes out in its next version, with an entry in the
[changelog](CHANGELOG.md).

## What is in scope

- **The packages** (`Wdc.*`): the latest published version, and the code on `main` that becomes the next one.
- **The samples** (`samples/`), as examples: a flaw in them is likely to be copied into applications built from
  them, so it counts even though they are not products.

## Known limitations

These are accepted in the samples, which keep their infrastructure small; a report is welcome when it shows a way
to make one of them worse than described.

- **Shopping, per-address limit of the login routes:** counted in the memory of each instance, by the client address
  the server sees. Behind a proxy that address is the proxy's: let the proxy limit (and set
  `Shopping:Auth:RequestsPerMinute` to 0), or configure forwarded headers. The limit on failed sign-ins per user name is
  in the database and holds across instances.
- **Shopping, data protection keys:** the Blazor Server host keeps its key ring in the database without an XML
  encryptor, so whoever reads that table can read the session storage of the browsers.
- **Shopping, development mode:** without `Shopping:Jwt:Key` and `Shopping:History:Key`, the server signs tokens and
  seals addresses with random keys that die with the process; outside development it refuses to start without them.
- **Shopping, stored password hash:** the login digest is checked with the stored PBKDF2 hash as its key, so a
  leaked database is enough to sign in as any user. Fine for a sample; a real application must not reuse it.
- **Remote transactions** (`Wdc.Rest.Server`) live in the memory of the instance that began them; with several
  instances, a client's requests must reach the same one.
