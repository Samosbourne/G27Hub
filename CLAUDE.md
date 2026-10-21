# G27 Hub

App para Windows 10/11 que faz pelo Logitech G27 o que o LGS 5.10 fazia, com visual do G HUB (G29). Plano completo: `PLANO_G27HUB.md` (fonte da verdade; ler a seção da fase atual antes de começar). `FUNCIONALIDADES_G29_PARA_G27.md` detalha cada funcionalidade (como fazer, prioridade); os itens já estão distribuídos no plano, marcados com (F). A Fase 5 é dividida em 5a (perfis) e 5b (atribuições).

Mapa de botões e eixos do G27 confirmado no volante: `docs/protocolo-g27.md` e `G27Buttons` (`src/G27Hub.Core/G27State.cs`). Não chutar números de botão: perguntar ao John.

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
src/G27Hub.Logging   AppLog: Serilog (arquivo rotativo) + buffer em memória; FfbLogServer (pipe da DLL)
src/G27Hub.Core      protocolo (G27Commands), dispositivo (G27Device), estado (G27State),
                     registro do FFB em HKCU (FfbRegistration), memória compartilhada (FfbShared)
src/G27Hub.Cli       g27cli
src/G27Hub.App       WPF (sem WPF-UI: estilos próprios em App.xaml imitam o G HUB); desenhos próprios do G27/pedais/câmbio
                     em Controls/. MainWindow lê o G27Device a 60 fps (CompositionTarget.Rendering), aplica ajustes
                     (config.json + HKCU\Software\G27Hub\FFB), console real, bandeja (NotifyIcon), iniciar com o Windows (HKCU Run).
                     "--shots <pasta> [--live]" salva PNG de cada tela; "--minimized" abre só na bandeja.
src/native/g27ffb    DirectInput effect driver em C (base: gcampa/g27-ffb-driverless, MIT, ver LICENSE-gcampa.txt);
                     hub.c = log pelo pipe + memória compartilhada. effects.c é o motor de efeitos (o "ffb-core").
src/native/fftest    g27fftest.exe: testa o FFB via DirectInput como um jogo (x64 e x86)
tests/G27Hub.Core.Tests
docs/protocolo-g27.md, docs/testes/faseN.md
```
Fases futuras adicionam: `G27Hub.Games`, `G27Hub.Virtual`, `G27Hub.Telemetry`, `data/compat-db.json`, `assets/`.

Todos os projetos .NET são `net10.0-windows`. O código nativo usa o MSVC + CMake do VS 2026 Build Tools (não precisa estar no PATH; o `build.ps1` acha pelo vswhere) e gera `src/native/out/{x64,x86}/`, que o g27cli copia para a sua pasta de saída.

Dados: config em `%APPDATA%\G27Hub\`, logs em `%LOCALAPPDATA%\G27Hub\logs\`.

## Comandos
```
.\g27hub                        # abre o app (g27hub.cmd = dotnet run --project src/G27Hub.App)
.\src\native\build.ps1          # DLL de FFB + g27fftest, x64 e x86 (antes do dotnet build quando mexer no C)
dotnet build
dotnet test
.\g27cli <list|native|range|autocenter|leds|monitor|log|ffb ...> [-v]   # g27cli.cmd = dotnet run --project src/G27Hub.Cli --
.\g27cli ffb <register|unregister|status|pulse|test [x64|x86] [const|sine|spring|damper|all]|gain N|invert on|off|log 0|1|2>
```
