# Teste da Fase 2 — Force feedback (Modo Padrão)

Já validado pelo Claude Code no volante, em 21/10/2026: pulso direto no HID, `ffb register`, teste DirectInput x64 e x86 (força constante e mola), log pelo pipe, log de reserva em arquivo, memória compartilhada e `unregister` restaurando o registro.

**Falta: jogos de verdade.** Comandos no PowerShell, na pasta do projeto (`.\g27cli ...`).

## 0. Preparação (uma vez)
- `.\g27cli ffb status` → as duas DLLs registradas e "Chave OEM C29B: aponta para o G27 Hub". Se não, `.\g27cli ffb register`.
- Se você mudar o código C: `.\src\native\build.ps1` e depois `dotnet build`.

## 1. Antes de cada sessão de jogo
1. Volante ligado → `.\g27cli native` (ou deixe o `.\g27cli monitor` aberto, que reaplica sozinho).
2. Deixe **`.\g27cli monitor` aberto** num terminal: as linhas `FFB` da DLL aparecem nele.
3. Só então abra o jogo.

## 2. Teste rápido sem jogo
- `.\g27cli ffb test x64 all` → força para um lado, para o outro, vibração (seno), mola (gire o volante: ele volta ao centro) e amortecedor (fica "pesado" ao girar rápido). [ ]
- `.\g27cli ffb test x86 all` → mesma coisa (caminho dos jogos 32 bits). [ ]

## 3. Jogos
Para cada jogo, anote: ✓/✗, se a força faz sentido (puxa para o centro nas curvas, sente zebra/batida) e as linhas `FFB` do monitor.

| Jogo | Bits | Linha "g27ffb.dll carregada"? | FFB funciona? | Sentido certo? | Observação |
|---|---|---|---|---|---|
| Assetto Corsa | 64 | | | | |
| Assetto Corsa Competizione | 64 | | | | |
| Forza Horizon 5 / 6 | 64 | | | | |
| um jogo 32 bits (ex.: rFactor 1, LFS, GTR2, Richard Burns Rally) | 32 | | | | |

- No Forza: desconecte controle de Xbox e desligue o Steam Input; o FFB vai para o "Device 1".
- Força puxando para o batente em vez do centro → `.\g27cli ffb invert on`, reabra o jogo.
- Forte/fraca demais → `.\g27cli ffb gain 70` (0–100), reabra o jogo.
- Para ver os efeitos em detalhe: `.\g27cli ffb log 2` (volte para `1` depois, é bastante texto).

## 4. Durante o jogo
- `.\g27cli ffb status` num segundo terminal → "Força: +x%" mudando e o pid do jogo. [ ]

## 5. Se não tiver FFB
Mande o fim do `.\g27cli log` e, se o app estava fechado, o arquivo `%LOCALAPPDATA%\G27Hub\logs\ffb-<pid>.log`.
- Nenhuma linha "g27ffb.dll carregada": o jogo não pediu FFB pelo DirectInput (ou abriu antes do modo nativo, ou usa outra API).
- Linha aparece mas a força não muda: mande o log com `ffb log 2`.

## Aceite
FFB em AC/ACC, num jogo 32 bits e no Forza Horizon 5/6, com o log mostrando a DLL carregada e os efeitos chegando.

## Desfazer tudo
`.\g27cli ffb unregister` — o registro volta exatamente ao que era antes.
