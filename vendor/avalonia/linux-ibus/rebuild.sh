#!/usr/bin/env bash
set -euo pipefail
here=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
work=${1:?새 빌드 작업 디렉터리를 지정하세요}
if [ -e "$work" ]; then echo "기존 디렉터리는 변경하지 않습니다" >&2; exit 1; fi
mkdir -p "$work"
work=$(cd "$work" && pwd)
git clone --no-checkout https://github.com/AvaloniaUI/Avalonia.git "$work/source"
git -C "$work/source" checkout --detach 627ae9ef921621e27e7aa58df2796fbadb50af88
git -C "$work/source" apply "$here/ibus-keycode.patch"
dotnet pack "$here/Avalonia.FreeDesktop.Patched.csproj" -c Release -p:AvaloniaSourceRoot="$work/source" -o "$work/packages"
