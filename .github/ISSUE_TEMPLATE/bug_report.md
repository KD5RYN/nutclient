---
name: Bug report
about: Something is broken or not working as expected
title: ''
labels: bug
assignees: ''
---

## Describe the bug
A clear and concise description of what the bug is.

## Expected behavior
What you expected to happen.

## Actual behavior
What actually happened.

## Steps to reproduce
1. ...
2. ...
3. ...

## Environment
- **NutClient version:** (e.g., v1.3.0)
- **OS / distro:** (e.g., Windows Server 2022, Debian 12, Raspberry Pi OS)
- **NUT server type and version:** (e.g., upsd 2.8.1 on Raspberry Pi OS)
- **UPS model:** (e.g., APC Back-UPS ES 850G2)
- **Architecture:** (e.g., x86_64, arm64)

## nutclient.json
Please paste your config below, **with passwords removed**:

```json
{
  "NutServer": {
    "Host": "...",
    "Port": 3493,
    "UpsName": "...",
    "Username": "...",
    "Password": "REMOVED"
  },
  "Monitoring": {
    ...
  }
}
```

## Log excerpt
Relevant lines from `/var/log/nutclient.log` (Linux) or `C:\Scripts\nutclient.log` (Windows):

```
(paste here)
```

## Status file
Contents of `nutclient-status.json` at the time of the issue:

```json
(paste here)
```

## Additional context
Any other context, screenshots, or info that might help.
