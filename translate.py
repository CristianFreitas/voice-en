"""Fala em portugues (wav) -> texto em ingles, via Whisper (task=translate).

Dois caminhos: nuvem (Groq, whisper-large-v3) quando ha uma chave configurada, e local
(faster-whisper) caso contrario ou se a nuvem falhar.
"""
import json
import os
import sys
import urllib.request
import uuid

MODEL = os.environ.get("VOICE_EN_MODEL", "medium")
LANG = os.environ.get("VOICE_EN_LANG", "pt")
# CPU por padrao: a GTX 1660 costuma estar ocupada por outros programas do Windows,
# e ai a inferencia na GPU fica ordens de grandeza mais lenta que na CPU.
DEVICE = os.environ.get("VOICE_EN_DEVICE", "cpu")

GROQ_URL = "https://api.groq.com/openai/v1/audio/translations"
GROQ_MODEL = "whisper-large-v3"  # o turbo nao traduz
KEY_FILE = os.path.expanduser("~/.config/voice-en/groq-key")


def cloud_key():
    key = os.environ.get("VOICE_EN_GROQ_KEY", "")
    if not key and os.path.exists(KEY_FILE):
        with open(KEY_FILE, encoding="utf-8") as handle:
            key = handle.read()
    return key.strip()


def translate_cloud(wav, key, timeout=20):
    boundary = uuid.uuid4().hex
    with open(wav, "rb") as handle:
        audio = handle.read()
    fields = {"model": GROQ_MODEL, "response_format": "json", "temperature": "0"}
    body = b""
    for name, value in fields.items():
        body += f'--{boundary}\r\nContent-Disposition: form-data; name="{name}"\r\n\r\n{value}\r\n'.encode()
    body += (f'--{boundary}\r\nContent-Disposition: form-data; name="file"; filename="audio.wav"\r\n'
             "Content-Type: audio/wav\r\n\r\n").encode() + audio + f"\r\n--{boundary}--\r\n".encode()
    request = urllib.request.Request(GROQ_URL, data=body, headers={
        "Authorization": f"Bearer {key}",
        "Content-Type": f"multipart/form-data; boundary={boundary}",
        "User-Agent": "voice-en",
    })
    with urllib.request.urlopen(request, timeout=timeout) as response:
        return json.load(response)["text"].strip()


def load_model():
    from faster_whisper import WhisperModel  # import pesado; so quando o caminho local e usado
    return WhisperModel(MODEL, device=DEVICE, compute_type="int8", cpu_threads=os.cpu_count() or 4)


def translate(model, wav):
    segments, _ = model.transcribe(
        wav,
        task="translate",
        language=LANG,
        beam_size=1,
        vad_filter=True,
        condition_on_previous_text=False,
    )
    return " ".join(s.text.strip() for s in segments).strip()


if __name__ == "__main__":
    key = cloud_key()
    print(translate_cloud(sys.argv[1], key) if key else translate(load_model(), sys.argv[1]))
