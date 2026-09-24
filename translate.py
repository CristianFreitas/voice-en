"""Fala (wav) -> texto, via Whisper, em um de tres modos:

  pt-en  fala em portugues, texto em ingles (task=translate)
  pt-pt  fala em portugues, texto em portugues (transcricao)
  en-en  fala em ingles, texto em ingles (transcricao)

Dois caminhos: nuvem (Groq, whisper-large-v3) quando ha uma chave configurada, e local
(faster-whisper) caso contrario ou se a nuvem falhar.
"""
import json
import os
import sys
import urllib.request
import uuid

MODEL = os.environ.get("VOICE_EN_MODEL", "medium")
MODES = ("pt-en", "pt-pt", "en-en")
DEFAULT_MODE = "pt-en"
# CPU por padrao: a GTX 1660 costuma estar ocupada por outros programas do Windows,
# e ai a inferencia na GPU fica ordens de grandeza mais lenta que na CPU.
DEVICE = os.environ.get("VOICE_EN_DEVICE", "cpu")

GROQ_URL = "https://api.groq.com/openai/v1/audio/"
GROQ_MODEL = "whisper-large-v3"  # o turbo nao traduz
KEY_FILE = os.path.expanduser("~/.config/voice-en/groq-key")


def cloud_key():
    key = os.environ.get("VOICE_EN_GROQ_KEY", "")
    if not key and os.path.exists(KEY_FILE):
        with open(KEY_FILE, encoding="utf-8") as handle:
            key = handle.read()
    return key.strip()


def languages(mode):
    """("pt", "en") para "pt-en": idioma falado e idioma do texto."""
    if mode not in MODES:
        raise ValueError(f"modo desconhecido: {mode}")
    spoken, written = mode.split("-")
    return spoken, written


def translate_cloud(wav, key, mode=DEFAULT_MODE, timeout=20):
    spoken, written = languages(mode)
    boundary = uuid.uuid4().hex
    with open(wav, "rb") as handle:
        audio = handle.read()
    fields = {"model": GROQ_MODEL, "response_format": "json", "temperature": "0"}
    if spoken == written:
        endpoint = "transcriptions"
        fields["language"] = spoken
    else:
        endpoint = "translations"  # a Groq so traduz para ingles, e so o pt-en chega aqui
    body = b""
    for name, value in fields.items():
        body += f'--{boundary}\r\nContent-Disposition: form-data; name="{name}"\r\n\r\n{value}\r\n'.encode()
    body += (f'--{boundary}\r\nContent-Disposition: form-data; name="file"; filename="audio.wav"\r\n'
             "Content-Type: audio/wav\r\n\r\n").encode() + audio + f"\r\n--{boundary}--\r\n".encode()
    request = urllib.request.Request(GROQ_URL + endpoint, data=body, headers={
        "Authorization": f"Bearer {key}",
        "Content-Type": f"multipart/form-data; boundary={boundary}",
        "User-Agent": "voice-en",
    })
    with urllib.request.urlopen(request, timeout=timeout) as response:
        return json.load(response)["text"].strip()


def load_model():
    from faster_whisper import WhisperModel  # import pesado; so quando o caminho local e usado
    return WhisperModel(MODEL, device=DEVICE, compute_type="int8", cpu_threads=os.cpu_count() or 4)


def translate(model, wav, mode=DEFAULT_MODE):
    spoken, written = languages(mode)
    segments, _ = model.transcribe(
        wav,
        task="transcribe" if spoken == written else "translate",
        language=spoken,
        beam_size=1,
        vad_filter=True,
        condition_on_previous_text=False,
    )
    return " ".join(s.text.strip() for s in segments).strip()


if __name__ == "__main__":
    # translate.py <wav> [modo]
    mode = sys.argv[2] if len(sys.argv) > 2 else DEFAULT_MODE
    key = cloud_key()
    print(translate_cloud(sys.argv[1], key, mode) if key else translate(load_model(), sys.argv[1], mode))
