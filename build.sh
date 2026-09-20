#!/usr/bin/env bash
# Compila o VoiceEn.exe com o csc.exe que ja vem no Windows e instala em %LOCALAPPDATA%\VoiceEn.
set -euo pipefail
here="$(dirname "$(readlink -f "$0")")"
csc='/mnt/c/Windows/Microsoft.NET/Framework64/v4.0.30319/csc.exe'

appdata_win="$(cd /mnt/c && cmd.exe /c 'echo %LOCALAPPDATA%' 2>/dev/null | tr -d '\r')"
dest_win="${appdata_win}\\VoiceEn"
dest="$(wslpath -u "$dest_win")"
mkdir -p "$dest"

# o exe em uso fica travado pelo Windows; encerra a instancia antiga antes de recompilar
taskkill.exe /IM VoiceEn.exe /F >/dev/null 2>&1 || true

sed -e "s|@DISTRO@|${WSL_DISTRO_NAME}|" -e "s|@ROOT@|${here}|" "$here/VoiceEn.cs" > "$dest/VoiceEn.cs"
cd "$dest"
"$csc" /nologo /codepage:65001 /target:winexe /optimize+ /out:VoiceEn.exe \
  /r:System.Windows.Forms.dll /r:System.Drawing.dll VoiceEn.cs
echo "ok: ${dest_win}\\VoiceEn.exe"
