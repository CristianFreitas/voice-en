"""Fala em portugues (wav) -> texto em ingles, via Whisper (task=translate)."""
import os
import sys

from faster_whisper import WhisperModel

MODEL = os.environ.get("VOICE_EN_MODEL", "medium")
LANG = os.environ.get("VOICE_EN_LANG", "pt")
# CPU por padrao: a GTX 1660 costuma estar ocupada por outros programas do Windows,
# e ai a inferencia na GPU fica ordens de grandeza mais lenta que na CPU.
DEVICE = os.environ.get("VOICE_EN_DEVICE", "cpu")


def load_model():
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
    print(translate(load_model(), sys.argv[1]))
