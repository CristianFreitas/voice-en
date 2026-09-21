"""Servidor de traducao para o VoiceEn.exe.

Protocolo (linhas em stdin/stdout): imprime READY quando esta pronto; para cada caminho de wav
recebido responde "OK\t<texto>" ou "ERR\t<mensagem>". Sai no EOF do stdin.

Com chave da Groq configurada a traducao vai para a nuvem e o modelo local so e carregado se a
nuvem falhar; sem chave, o modelo local fica em memoria desde o inicio.
"""
import sys
import time

from translate import cloud_key, load_model, translate, translate_cloud


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    model = None
    if not cloud_key():
        model = load_model()
    print("READY", flush=True)

    for line in sys.stdin:
        wav = line.strip()
        if not wav:
            continue
        started = time.time()
        try:
            text, backend = None, "local"
            key = cloud_key()  # relido a cada pedido: da para ligar/desligar a nuvem sem reiniciar
            if key:
                try:
                    text, backend = translate_cloud(wav, key), "groq"
                except Exception as err:
                    print(f"nuvem falhou ({err}); usando o modelo local", file=sys.stderr, flush=True)
            if text is None:
                if model is None:
                    model = load_model()
                text = translate(model, wav)
            print(f"{backend} {time.time() - started:.1f}s", file=sys.stderr, flush=True)
            print("OK\t" + text.replace("\n", " "), flush=True)
        except Exception as err:
            print("ERR\t" + str(err).replace("\n", " "), flush=True)


if __name__ == "__main__":
    main()
