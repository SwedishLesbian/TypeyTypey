#!/bin/bash
# Prepares a Claude Code on the web container to work on TypeyTypey.
#
# TypeyTypey itself cannot be built here: net8.0-windows with UseWindowsForms needs the
# Microsoft.NET.Sdk.WindowsDesktop targets, which the Linux SDK does not carry. What this buys is a
# dotnet CLI, a warm NuGet cache, and tools/linux-check — the subset of the suite that does run.
# See AGENTS.md §4 and tools/linux-check/README.md.
set -euo pipefail

# Local machines are already set up by their owner; this only shapes the remote container.
if [ "${CLAUDE_CODE_REMOTE:-}" != "true" ]; then
  exit 0
fi

cd "${CLAUDE_PROJECT_DIR:-$(dirname "$0")/../..}"

if ! command -v dotnet >/dev/null 2>&1; then
  echo "Installing the .NET 8 SDK…"
  export DEBIAN_FRONTEND=noninteractive
  apt-get update -qq
  apt-get install -y -qq dotnet-sdk-8.0
fi

# First run of the CLI prints a long banner and writes ~/.dotnet; get it out of the way here.
export DOTNET_NOLOGO=1
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
dotnet --version

{
  echo 'export DOTNET_NOLOGO=1'
  echo 'export DOTNET_CLI_TELEMETRY_OPTOUT=1'
} >> "${CLAUDE_ENV_FILE:-/dev/null}"

echo "Restoring packages…"
dotnet restore tools/linux-check/LinuxPolicyCheck.csproj
# Warms the cache for the real test project. Restore succeeds on Linux even though the build cannot.
dotnet restore TypeyTypey.Tests/TypeyTypey.Tests.csproj || echo "Windows-only restore skipped."

echo "Ready. 'dotnet test tools/linux-check/LinuxPolicyCheck.csproj' runs the platform-free tests;"
echo "the full suite and the executable need Windows or CI."
