#!/usr/bin/env bash
# Instala o voice-en: ambiente Python com o Whisper (no WSL) + VoiceEn.exe (no Windows).
set -euo pipefail
here="$(dirname "$(readlink -f "$0")")"
cd "$here"

if [ -z "${WSL_DISTRO_NAME:-}" ]; then
  echo "erro: rode este script dentro do WSL2 (o microfone e o atalho ficam no Windows)" >&2
  exit 1
fi

uv="$(command -v uv || true)"
if [ -z "$uv" ]; then
  echo ">> instalando o uv em ~/.local/bin"
  curl -LsSf https://astral.sh/uv/install.sh | INSTALLER_NO_MODIFY_PATH=1 sh
  uv="$HOME/.local/bin/uv"
fi

echo ">> criando o ambiente Python"
[ -d .venv ] || "$uv" venv --python 3.12 .venv
"$uv" pip install --python .venv/bin/python -r requirements.txt

if [ "${1:-}" = "--no-model" ]; then
  echo ">> modelo local nao baixado (--no-model); ele sera baixado se a nuvem falhar algum dia"
else
  echo ">> baixando o modelo de traducao (~1,5 GB na primeira vez)"
  .venv/bin/python -c "from translate import load_model; load_model()"
fi

echo ">> compilando o VoiceEn.exe"
chmod +x build.sh voice-en
./build.sh

echo
echo "Pronto. Abra o VoiceEn.exe (caminho acima) e use Ctrl+Alt+Espaco para falar."
echo "Para o modo nuvem (~1 s por frase), veja a secao \"Modo nuvem\" do README."
