"""Servidor de traducao para o VoiceEn.exe.

Protocolo (linhas em stdin/stdout): imprime READY quando esta pronto; para cada linha
"<modo>\t[corrigir\t]<caminho do wav>" (ou so o caminho, que vale pt-en) responde "OK\t<texto>" ou
"ERR\t<mensagem>". Os modos estao em translate.MODES; "corrigir" pede a correcao da fala
(translate.correct), que so vale para translate.CORRECTABLE e exige a chave da Groq. Sai no EOF do
stdin.

Com chave da Groq configurada a traducao vai para a nuvem e o modelo local so e carregado se a
nuvem falhar; sem chave, o modelo local fica em memoria desde o inicio.
"""
import re
import sys
import time

from translate import (CORRECTABLE, DEFAULT_MODE, cloud_key, correct, is_silent, languages,
                       load_model, translate, translate_cloud)


def log(message):
    print(message, file=sys.stderr, flush=True)


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    model = None
    if not cloud_key():
        model = load_model()
    print("READY", flush=True)

    def whisper(wav, mode, key):
        """Texto e caminho usado ("groq" ou "local"); sem key vai direto ao modelo local."""
        nonlocal model
        if key:
            try:
                return translate_cloud(wav, key, mode), "groq"
            except Exception as err:
                log(f"nuvem falhou ({err}); usando o modelo local")
        if model is None:
            model = load_model()
        return translate(model, wav, mode), "local"

    for line in sys.stdin:
        line = line.strip()
        if not line:
            continue
        parts = line.split("\t")
        wav = parts[-1]
        mode = parts[0] if len(parts) > 1 else DEFAULT_MODE
        fix = "corrigir" in parts[1:-1] and mode in CORRECTABLE
        started = time.time()
        try:
            languages(mode)  # modo desconhecido vira ERR antes de gastar uma chamada
            try:
                silent = is_silent(wav)
            except Exception as err:
                log(f"nao consegui medir o volume ({err}); seguindo")
                silent = False
            if silent:
                log(f"silencio {mode}: nada enviado ao Whisper")
                print("OK\t", flush=True)
                continue

            key = cloud_key()  # relido a cada pedido: da para ligar/desligar a nuvem sem reiniciar
            if fix and not key:
                log("correcao pedida, mas ela exige a chave da Groq; texto sem correcao")
                fix = False
            spoken, _ = languages(mode)
            heard = f"{spoken}-{spoken}" if fix else mode  # com correcao, o modelo de texto traduz
            text, backend = whisper(wav, heard, key)
            # Se a nuvem ja caiu, a correcao (tambem na Groq) cairia junto: pula para nao somar
            # timeouts alem do limite do app. Pelo mesmo motivo a retraducao nao volta a nuvem.
            if fix and text and backend == "groq":
                try:
                    text = correct(text, mode, key)
                    backend += "+correcao"
                except Exception as err:
                    log(f"correcao falhou ({err}); texto sem correcao")
                    if heard != mode:
                        text, backend = whisper(wav, mode, key)
            elif fix and text and heard != mode:
                log("nuvem fora; correcao pulada")
                text, backend = whisper(wav, mode, None)
            log(f"{backend} {mode} {time.time() - started:.1f}s")
            print("OK\t" + re.sub(r"[\r\n]+", " ", text), flush=True)
        except Exception as err:
            print("ERR\t" + str(err).replace("\n", " "), flush=True)


if __name__ == "__main__":
    main()
