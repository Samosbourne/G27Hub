# Protocolo do G27

Relatórios de saída HID de 7 bytes (o app envia com o report ID `00` na frente; relatório de saída = 8 bytes). Fatos levantados do `hid-lg4ff` (Linux) e de `gcampa/g27-ffb-driverless` (MIT); nenhum código GPL copiado. Implementação: `src/G27Hub.Core/G27Commands.cs` (C#) e `src/native/g27ffb/` (DLL de FFB).

| Função | Bytes | Status |
|---|---|---|
| Reverter modo ao resetar USB | `F8 0A 00 00 00 00 00` | ✅ validado (Fase 1) |
| Trocar para modo nativo (com detach) | `F8 09 04 01 00 00 00` | ✅ reenumera `C294` → `C29B` |
| Rotação 40°–900° | `F8 81 <LSB> <MSB> 00 00 00` | ✅ validado |
| LEDs de RPM | `F8 12 <máscara 5 bits> 00 00 00 00` | ✅ funciona; ordem física dos bits a anotar |
| Desligar autocentro | `F5 00 00 00 00 00 00` | ✅ validado |
| Configurar autocentro | `F5…` (desliga) → `FE 0D <k> <k> <força> 00 00` → `14 00 00 00 00 00 00` (liga) | ✅ validado |
| Força constante (slot 1) | `11 08 <nível> 80 00 00 00` | ✅ validado (Fase 2): `80` = zero, acima de `80` gira para a **esquerda** |
| Parar forças | `13 00 00 00 00 00 00` | ✅ validado |

## Autocentro
- **O volante ignora novos parâmetros com a mola ligada**: sempre `F5` antes de `FE 0D` (descoberto pelo gcampa; o `G27Commands.Autocenter` já faz isso).
- Força 0–100% vira magnitude `0..0xFFFF`. Até `0xAAAA` (2/3): `k = 0x0C·m`, `força = 0x80·m`; acima as inclinações passam a `0x06` e `0xFF`. No G27 `k` vai pela metade. Os dois são divididos por `0xAAAA`. 100% → `k=07 força=FF`, 50% → `k=04 força=5F`. (`k` vai de 0 a 7.)

## Force feedback (Fase 2)
A `g27ffb.dll` não usa os slots de efeito do volante: soma todos os efeitos DirectInput em software (mixer de 2 ms) e manda **uma** força constante pelo slot 1. Mola/amortecedor/inércia/atrito são calculados a partir da posição lida do próprio volante.

## IDs
- `046D:C294` — como o G27 aparece ao ligar (modo de compatibilidade Driving Force EX: 200°, pedais combinados).
- `046D:C29B` — G27 em modo nativo.

## Entradas (modo nativo, relatório de 12 bytes sem report ID)
Lidas pelo descritor HID (parser do HidSharp), não por offsets fixos.

| Campo | Usage | Faixa | Observação |
|---|---|---|---|
| Hat (direcional do câmbio) | HatSwitch | 0–7, fora disso = solto | 4 bits |
| Botões 1–22 | Button 1–22 | 0/1 | câmbio H, botões do volante e do câmbio |
| Volante | X | 0–16383 (14 bits) | centro ≈ 8190 |
| Pedal | Y | 0–255 | 255 = solto |
| Pedal | Z | 0–255 | 255 = solto |
| Pedal | Rz | 0–255 | 255 = solto |
| Vendor | 0xFF000001 | 2 bytes | desconhecido |
| Botão 23 | Button 23 | 0/1 | |

### Botões (1-based, como no joy.cpl) — `G27Buttons` em `src/G27Hub.Core/G27State.cs`
| Botões | Controle | Status |
|---|---|---|
| 1–4 | pretos da base do câmbio | dedução |
| 5, 6 | borboletas (direita, esquerda) | dedução |
| 8, 21, 22 | vermelhos da lateral esquerda do volante (cima, meio, baixo) | ✅ John, 21/10 |
| 7, 19, 20 | vermelhos da lateral direita do volante (cima, meio, baixo) | ✅ John, 21/10 |
| 9–12 | vermelhos do câmbio (da esquerda p/ direita: 12, 9, 10, 11) | ✅ John, 21/10 |
| 13–18 | marchas 1ª–6ª | ✅ 1ª–3ª pelo John; 4ª–6ª pela sequência |
| 23 | ré (o bit separado depois dos bytes do fabricante) | dedução |
| Hat | direcional do câmbio | |

O "botão 16 apertado em repouso" visto na Fase 1 era o câmbio parado na 4ª.

Pedais: Z = acelerador, Rz = freio, Y = embreagem (a confirmar).

## Registro do FFB (HKCU, sem admin)
- COM: `HKCU\Software\Classes\CLSID\{44CD99D0-68D8-40C3-917D-0F52047DFCEB}\InProcServer32` → DLL x64; a mesma chave na visão 32 bits (`WOW6432Node`) → DLL x86.
- DirectInput: `HKCU\System\CurrentControlSet\Control\MediaProperties\PrivateProperties\Joystick\OEM\VID_046D&PID_C29B`
  - `OEMForceFeedback\CLSID` = o CLSID acima; `Attributes` (DIFFDEVICEATTRIBUTES: flags 0, período 1000 µs, resolução 1000 µs).
  - `OEMForceFeedback\Effects\{GUID}` para os 11 efeitos padrão: nome + `Attributes` (DIEFFECTATTRIBUTES: id 0–10, tipo, parâmetros `0x3FF`, coordenadas `0x70`). O id é o `dwEffectId` que a DLL recebe.
  - `Axes\0\Attributes` = `01 81 00 00 01 00 30 00` (eixo X é atuador de FFB).
- Num PC sem LGS (como o do John) essa árvore **não existe**; o `g27cli ffb register` cria e o `unregister` apaga (estado original em `HKCU\Software\G27Hub\FFB`).

## Armadilhas
- O volante só manda relatório quando algo muda: o estado inicial é lido com `HidD_GetInputReport` (funciona no G27).
- Desligar/desconectar volta para ~200°: o app reaplica modo nativo + rotação sozinho.
- O jogo deve abrir **depois** do modo nativo.
- Steam Input pode "roubar" o volante.
