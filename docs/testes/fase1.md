# Teste da Fase 1 — núcleo + g27cli + log

Abra um terminal na pasta do projeto. Todos os comandos abaixo usam:

```
dotnet run --project src/G27Hub.Cli -- <comando>
```

(abreviado como `g27cli <comando>`). Acrescente `-v` para ver os bytes enviados.

Antes: G27 ligado na tomada e no USB, **nenhum** jogo aberto, Steam fechada.

Anote o resultado de cada passo (✓ / ✗ + o que apareceu).

## 1. Detecção
`g27cli list`
- [ ] Aparece uma linha `046D:C294 compatível (DFEX)` (ou `C29B nativo`, se já estava em modo nativo).

## 2. Modo nativo
`g27cli native`
- [ ] O volante dá a volta de calibração.
- [ ] Log mostra `OK HID G27 conectado (046D:C294) → enviando modo nativo` e depois `OK HID G27 em modo nativo (046D:C29B), rotação 900°`.
- [ ] Se aparecer `AVISO MODO G27 não reenumerou...`, anote.
- [ ] `g27cli list` agora mostra `046D:C29B nativo`.

## 3. joy.cpl
Win+R → `joy.cpl` → G27 → Propriedades.
- [ ] Nome do dispositivo (anote).
- [ ] Volante vai de batente a batente ao girar ~450° para cada lado.
- [ ] Acelerador, freio e **embreagem** aparecem como eixos **separados**.
- [ ] Botões e câmbio acendem.

## 4. Rotação
- `g27cli range 200` → girar: batente em ~100° para cada lado. [ ]
- `g27cli range 540` → ~270° para cada lado. [ ]
- `g27cli range 900` → ~450° para cada lado. [ ]

## 5. Monitor (mapeamento das entradas)
`g27cli monitor` (Ctrl+C sai)
- [ ] Ao abrir, já mostra valores (sem precisar mexer) — estado inicial.
- [ ] Volante: centro ≈ `+0.0°`, batentes ≈ `-450°` / `+450°`. Anote o "bruto" no centro e nos batentes.
- [ ] Pise em cada pedal sozinho e anote **qual eixo** mexe e os valores solto/pisado:
  - acelerador: eixo `___` solto `___` pisado `___`
  - freio: eixo `___` solto `___` pisado `___`
  - embreagem: eixo `___` solto `___` pisado `___`
- [ ] Engate cada marcha e anote o número do botão: 1ª `__` 2ª `__` 3ª `__` 4ª `__` 5ª `__` 6ª `__` R `__`
- [ ] Direcional do câmbio: valores do `Hat` para cima/direita/baixo/esquerda.
- [ ] Anote o número de cada botão do volante e do câmbio (opcional, ajuda na Fase 5).

## 6. Reconexão automática
Com o `monitor` aberto:
- [ ] Tire o USB: log mostra `ERRO HID G27 desconectado`, tela mostra "Aguardando o G27...".
- [ ] Recoloque: sozinho volta para modo nativo e `rotação 900°` (sem digitar nada).
- [ ] Repita desligando/religando a **fonte** do volante.

## 7. LEDs
`g27cli leds teste`
- [ ] Acende um LED por vez a cada 2 s, com "Bit N aceso" no terminal. Anote qual LED (da esquerda para a direita, 1–5) acendeu em cada bit: bit0 `__` bit1 `__` bit2 `__` bit3 `__` bit4 `__`
- [ ] No fim acendem todos e depois apagam.
- [ ] `g27cli leds 31` (todos acesos) e `g27cli leds 0` (todos apagados).

## 8. Autocentro
- `g27cli autocenter 50` → volante volta ao centro com força média. [ ]
- `g27cli autocenter 100` → força máxima. [ ]
- `g27cli autocenter off` → volante solto. [ ]

## 9. Log
`g27cli log`
- [ ] Mostra a pasta `%LOCALAPPDATA%\G27Hub\logs` e o fim do arquivo com cada passo acima.
- [ ] **Mande para o Claude Code:** este arquivo preenchido + o arquivo de log do dia.

## Aceite
900°, pedais separados e embreagem no `joy.cpl`; LEDs na ordem certa; log em arquivo mostrando cada passo.
