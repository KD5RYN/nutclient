# Security Policy

NutClient runs as a privileged service (Windows service or Linux systemd unit running as root) and executes shell scripts during shutdown. We take security seriously.

## Reporting a Vulnerability

**Please do not report security vulnerabilities through public GitHub issues.**

Instead, report them privately via [GitHub's Security Advisory](https://github.com/KD5RYN/nutclient/security/advisories/new) feature. This lets us discuss the issue and ship a fix before disclosure.

If you can't use Security Advisories for some reason, you can contact the maintainer through their GitHub profile.

When reporting, please include:
- A description of the vulnerability and its potential impact
- Steps to reproduce
- The NutClient version affected
- Any suggested mitigations or fixes

I'll acknowledge receipt within a few days and work with you on a coordinated disclosure timeline.

## Supported Versions

Only the latest released version receives security fixes. Older releases are not maintained.

## Scope

Examples of in-scope issues:
- Privilege escalation through NutClient or its scripts
- Command injection via config values or NUT server responses
- Path traversal in shutdown script arguments
- Denial of service against the client or shutdown script execution

Out of scope:
- Issues in NUT itself (report to upstream NUT)
- Issues that require local admin/root access to exploit (you already have that level of access)
- Self-signed PowerShell scripts triggering execution policy warnings (this is documented behavior, not a vulnerability)
