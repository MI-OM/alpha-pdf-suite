## What changed and why
<!-- The issue this addresses, and what the change does. -->

## How it was tested
<!-- Commands you ran and their results. Do not claim a result you did not see. -->

## Dependencies
<!-- None, or each added or changed dependency with its row in docs/licensing/dependency-inventory.md. -->

## Known limitations and anything left undone

## Risk
<!-- Security (untrusted input, the worker boundary), privacy (logs, network), saved documents, compatibility. -->

## Checklist
- [ ] CI passes on Windows, macOS and Linux.
- [ ] Tests cover error and cancellation paths, and leak checks have a negative control.
- [ ] No tests deleted, assertions weakened, or warnings suppressed to make the build pass.
- [ ] Only the worker parses documents, and nothing logs document content.
- [ ] User-facing text follows docs/04-UI-COPY-AND-PRODUCT-RULES.md.
- [ ] The feature matrix (docs/07-FEATURE-MATRIX.md) is updated if a capability's status changed.
