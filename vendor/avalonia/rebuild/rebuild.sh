#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")"
# .NET 10.0.402, Node/npm, Python3, git, curl 필요. 새 빈 작업 폴더에서 실행.
test ! -e source
git clone https://github.com/AvaloniaUI/Avalonia.git source
git -C source checkout --detach 627ae9ef921621e27e7aa58df2796fbadb50af88
git -C source apply ../../browser-11.3.22.patch
(cd source/src/Browser/Avalonia.Browser/webapp && npm ci && npx tsc --noEmit && node build.js)
dotnet build Avalonia.Browser.Patched.csproj -c Release
curl --fail --location https://api.nuget.org/v3-flatcontainer/avalonia.browser/11.3.22/avalonia.browser.11.3.22.nupkg -o official.nupkg
python3 package.py
