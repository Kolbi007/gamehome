# GameHome — To be fixed

[GitHub issue #58](https://github.com/Kolbi007/gamehome/issues/58) is the primary backlog for reports, acceptance criteria and task status. This file is the repository entry point. Keep status and PR links on the issue.

| Priority | Confirmed user report | Acceptance on Legion Go |
| --- | --- | --- |
| P0-A | After updating to Beta 0.2.26, the built-in controller stopped navigating menus. Touch still works. | D-pad and left stick move focus, A selects and B goes back after a fresh install, upgrade, sleep and return from a game. |
| P0-B | Local WoW Forever launch shows two splash screens, and the WoW window stays behind GameHome. | A ready game window takes over automatically. GameHome clears its cover and leaves Battle.net prompts visible. |
| P1 | WoW startup is unreliable when the player is signed out of Battle.net. | Keep the official launcher accessible for sign-in and MFA. Continue after reliable readiness or explicit confirmation. Give clear feedback on timeout and avoid blind launch retries. |
| P1 | GameHome still points to Handheld Companion for menus, shortcuts and TDP. | Routine use works with Lenovo's controls. Verify Legion Space and Quick Menu on hardware and document a supported path for settings GameHome cannot control directly. |
| P2 | Sonic 3 shows a black background instead of its YouTube trailer. | Play the correct trailer where embedding works. Restore artwork and offer Open on YouTube if playback fails. Check error 153, embed permissions, autoplay and startup timeout. |

Use separate pull requests for controller input, WoW launch, integrations and media. Record items under work, implemented with hardware testing pending, and verified complete on issue #58. Completion requires a test on the physical device.

## Technical risks to investigate

- Check whether executable discovery treats sibling folders with a shared prefix as if they were inside the install directory.
- Review log redaction, minidump handling and backup recovery for game metadata.
- Measure background work, controller polling and media activity during games and after sleep. Library recovery and scan isolation already shipped in the earlier Beta stabilization.

## Proposed improvements

- Evaluate shared launch states and smaller components for controller navigation and game launch after the P0 fixes.
- Consider a repair mode and a recovery path for problematic Beta builds. Evaluate game profiles, controller diagnostics and library filters separately.

## Decisions to revisit

The Legion-native direction conflicts with the Handheld Companion workflow in [ADR 0006](adr/0006-handheld-companion.md), [ADR 0007](adr/0007-ytelse-og-hc-oppsett.md) and [ADR 0011](adr/0011-tdp-fjernstyrt-i-hc.md). Document the supported Lenovo workflow and verify shortcuts and controls on the device before revising those decisions.
