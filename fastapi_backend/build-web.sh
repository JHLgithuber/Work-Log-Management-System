#!/bin/sh
set -eu

backend_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
repository_dir=$(dirname "$backend_dir")
browser_project="$repository_dir/avalonia_ui/WorkLogManagementSystem_UI/WorkLogManagementSystem_UI.Browser/WorkLogManagementSystem_UI.Browser.csproj"
web_dir="$backend_dir/web"
publish_dir=$(mktemp -d /tmp/worklog-browser-publish.XXXXXX)

cleanup() {
    rm -rf -- "$publish_dir"
}
trap cleanup EXIT HUP INT TERM

dotnet publish "$browser_project" -c Release -o "$publish_dir"

rm -rf -- "$web_dir"
mkdir -p "$web_dir"
cp -a "$publish_dir/wwwroot/." "$web_dir/"

echo "Avalonia browser UI published to $web_dir"
