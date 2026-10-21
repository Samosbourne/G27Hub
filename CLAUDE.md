# G27 Hub

App para Windows 10/11 que faz pelo Logitech G27 o que o LGS 5.10 fazia, com visual do G HUB (G29). Plano completo: `PLANO_G27HUB.md` (fonte da verdade; ler a seção da fase atual antes de começar).

## Regras (seção 1 do plano)
1. **Nada de driver de kernel próprio.** Modo Padrão é 100% modo usuário e funciona com Integridade de Memória ligada.
2. **Modo Compatibilidade Total (vJoy + HidHide) vem DESLIGADO** e só o usuário ativa, manualmente, com confirmação. O app no máximo **recomenda**.
3. Modo Padrão grava só em **HKCU**, sem admin. Compat. Total pede admin e reinício, com aviso antes.
4. Tudo **reversível**. Ponto de restauração antes de instalar qualquer driver.
5. Não copiar código, ícones ou imagens do G HUB/LGS.
6. Não copiar código GPL (`hid-lg4ff`); usar só **fatos do protocolo**.
7. Código MIT (`gcampa/g27-ffb-driverless`, `Jabelius/OpenG27`, SDK do vJoy) pode ser reaproveitado **com crédito**.
8. Todo comando HID novo é testado primeiro no `g27cli`.
9. **Tudo gera log** (via `G27Hub.Logging.AppLog`), para o console mostrar o que deu certo ou errado.
10. O Claude Code **não tem o volante**: cada fase termina com `docs/testes/faseN.md`, que o John executa e reporta. **Não avançar de fase sem a confirmação dele.**

## Modos (seção 4)
- **Padrão** (ativo de fábrica): o jogo vê o G27 real (`046D:C29B`). Modo nativo, rotação, autocentro e LEDs pelo app; FFB pela `g27ffb.dll` (DirectInput effect driver OEM, x86+x64). Sensibilidade/curvas e pedais combinados limitados.
- **Compatibilidade Total** (desligado): G27 escondido pelo HidHide, app aplica curvas e alimenta o vJoy; FFB volta do vJoy → app → G27. Jogo vê "vJoy Device". Para jogos sem suporte ao G27.
- **LGS oficial**: não é modo do app; o app só detecta drivers antigos da Logitech e avisa do conflito.

## Console de diagnóstico (seção 6)
Escondido; abre pelo ícone de terminal, **Ctrl+Shift+L** ou "Ver detalhes" de uma notificação de erro. Linhas `HH:mm:ss  NIVEL  ORIGEM  mensagem`, níveis `INFO`/`OK`/`AVISO`/`ERRO` (+`DEBUG` no modo detalhado), origens `HID`, `MODO`, `FFB`, `JOGO`, `PERFIL`, `LED`, `TELEMETRIA`, `VJOY`. Fonte: `AppLog.Memory` (últimas 5.000 linhas, evento `Added`). Logs da DLL chegam pelo pipe `\\.\pipe\G27Hub_Log` (Fase 2).

## Estrutura
```
src/G27Hub.Logging   AppLog: Serilog (arquivo rotativo) + buffer em memória
src/G27Hub.Core      protocolo (G27Commands), dispositivo (G27Device), estado (G27State)
src/G27Hub.Cli       g27cli
tests/G27Hub.Core.Tests
docs/protocolo-g27.md, docs/testes/faseN.md
```
Fases futuras adicionam: `G27Hub.Games`, `G27Hub.Virtual`, `G27Hub.Telemetry`, `G27Hub.App` (WPF + WPF-UI), `src/native/{ffb-core,g27ffb}` (C++17/CMake), `data/compat-db.json`, `assets/`.

Dados: config em `%APPDATA%\G27Hub\`, logs em `%LOCALAPPDATA%\G27Hub\logs\`.

## Comandos
```
dotnet build
dotnet test
dotnet run --project src/G27Hub.Cli -- <list|native|range|autocenter|leds|monitor|log> [-v]
```
