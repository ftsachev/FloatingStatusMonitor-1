# Floating Status Monitor - Agent Rules

A lightweight Windows desktop application that displays real-time CPU usage,
memory usage, and CPU temperature in a floating transparent window. See
[`README.md`](README.md) for full documentation.

## Conventions

- **Language**: C# (.NET Framework / .NET Core)
- **UI**: Windows Forms (`Form1.cs`)
- **Configuration**: JSON-based via `config.json` (auto-reloads on change)
- **Platform**: Windows only (relies on LibreHardwareManager for temperature)

## Notes

This is a standalone project (not part of the inflight ecosystem). Changes
should be minimal and focused - primarily layout fixes, configuration options,
and error handling.
