# Contributing to NutClient

Thanks for taking the time to contribute! NutClient is a small focused project — patches, bug reports, and feature suggestions are all welcome.

## Reporting bugs

Open an issue using the **Bug Report** template. Because NutClient runs as a privileged service and executes shell scripts, it really helps if you include:

- Operating system and distro/version
- NUT server type and version
- Sanitized `nutclient.json` (remove passwords)
- Relevant excerpt from `nutclient.log`
- Contents of `nutclient-status.json` if available

## Suggesting features

Open an issue using the **Feature Request** template. For non-trivial features, please open the issue first and discuss the approach before sending a PR — it'll save you time.

## Submitting changes

1. Fork the repo and create a branch from `main`
2. Make your changes
3. Run the tests locally:
   ```bash
   dotnet test nutclient.tests
   ```
4. Open a pull request against `main`
5. CI will run automatically — make sure tests pass

The PR template will walk you through the basics.

## Development

The README has a [Development section](README.md#development) with the project structure, architecture overview, build commands, and a guide to adding new tests. Start there.

Quick reference:
```bash
# Build
dotnet build nutclient -c Release

# Run tests
dotnet test nutclient.tests

# Publish for a target platform
dotnet publish nutclient -c Release -r linux-x64 -o publish
```

## Code style

- **Commit messages:** Single-line imperative summary (e.g., "Add log rotation"), optional body for the why
- **C# style:** Match the existing code — 4-space indent, braces on new lines, no `var` for non-obvious types
- **Tests:** Add tests for new state machine logic. The `UpsStateMachine` is the pure decision layer and is fully testable via `FakeTimeProvider`.

## Branch strategy

- `main` is the working branch — all PRs target it
- Releases are cut by tagging `vX.Y.Z` on `main`, which triggers GitHub Actions to build and publish
- No `develop` branch or release branches

## License

By contributing, you agree that your contributions will be licensed under the [MIT License](LICENSE).
