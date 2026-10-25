# Security Policy

## Supported versions

| Version | Supported |
| --- | --- |
| 1.x | ✅ |

Security fixes go into the latest release. Please update before reporting.

## Reporting a vulnerability

**Please don't report security problems in public issues.**

Use GitHub's private reporting instead: go to the repository's **Security** tab and choose **Report a vulnerability**. Include:

- what you found and why it's a problem,
- steps or a file that reproduces it,
- the Sentinel version and Windows version.

You should get a reply within 7 days. Once a fix is released, the report can be published with credit to you if you'd like.

## What's in scope

Sentinel writes to game settings files, changes some Windows settings, and can ask Windows to run `reg.exe` or `powercfg` with administrator rights after you approve the prompt. Issues in these areas are especially welcome:

- writing outside the intended settings file, or following a crafted path,
- anything that lets catalog data, a game's files or downloaded artwork run code or trigger elevation,
- backups or reverts that silently lose a user's original data.
