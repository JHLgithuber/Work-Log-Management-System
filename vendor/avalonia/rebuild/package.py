from pathlib import Path
from zipfile import ZipFile, ZIP_DEFLATED
root=Path(__file__).resolve().parent
output=root/'Avalonia.Browser.11.3.22-qa-ime.6.nupkg'
with ZipFile(root/'official.nupkg') as source, ZipFile(output,'w',ZIP_DEFLATED) as target:
    for entry in source.infolist():
        if entry.filename == '.signature.p7s':
            continue  # 수정 패키지는 공식 NuGet 서명을 주장하지 않는다.
        data=source.read(entry.filename)
        if entry.filename.endswith('/Avalonia.Browser.dll'):
            data=(root/'bin/Release/net8.0-browser/Avalonia.Browser.dll').read_bytes()
        if entry.filename.startswith('staticwebassets/'):
            asset=root/'source/src/Browser/Avalonia.Browser'/entry.filename
            if asset.is_file(): data=asset.read_bytes()
        if entry.filename.endswith('.nuspec'):
            data=data.replace(b'<version>11.3.22</version>',b'<version>11.3.22-qa-ime.6</version>')
        target.writestr(entry,data)
print(output.name)
