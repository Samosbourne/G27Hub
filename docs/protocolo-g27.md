# Protocolo do G27

Relatórios de saída HID de 7 bytes (o app envia com o report ID `00` na frente). Fatos levantados do `hid-lg4ff` (Linux) e dos projetos MIT citados no plano; nenhum código GPL copiado. Implementação: `src/G27Hub.Core/G27Commands.cs`.

| Função | Bytes | Status |
|---|---|---|
| Reverter modo ao resetar USB | `F8 0A 00 00 00 00 00` | a validar |
| Trocar para modo nativo (com detach) | `F8 09 04 01 00 00 00` | a validar (esperado: reenumera `C294` → `C29B`) |
| Rotação 40°–900° | `F8 81 <LSB> <MSB> 00 00 00` | a validar |
| LEDs de RPM | `F8 12 <máscara 5 bits> 00 00 00 00` | a validar (ordem dos bits) |
| Desligar autocentro | `F5 00 00 00 00 00 00` | a validar |
| Configurar autocentro | `FE 0D <k> <k> <força> 00 00` + ativar `14 00 00 00 00 00 00` | a validar |
| Efeitos de força | família "classic" (slots 0–3) | Fase 2 |

## Autocentro
Força 0–100% vira magnitude `0..0xFFFF`. Até `0xAAAA` (2/3): `k = 0x0C·m`, `força = 0x80·m`; acima disso as inclinações passam a `0x06` e `0xFF`. No G27 (não-MOMO) `k` vai pela metade. Os dois são divididos por `0xAAAA`. Resultado: 100% → `k=07 força=FF`, 50% → `k=04 força=5F`.

## IDs
- `046D:C294` — como o G27 aparece ao ligar (modo de compatibilidade Driving Force EX: 200°, pedais combinados).
- `046D:C29B` — G27 em modo nativo.

## Entradas
Decodificadas pelo descritor HID (parser do HidSharp), não por offsets fixos. Mapeamento a confirmar no teste da Fase 1:

| Usage | Controle |
|---|---|
| X | volante (esperado 14 bits, 0–16383) |
| ? | acelerador |
| ? | freio |
| ? | embreagem |
| Botões ? | marchas 1–6 e ré do câmbio H |
| Hat | direcional do câmbio |

## Armadilhas
- O volante só manda relatório quando algo muda: o estado inicial é lido com `HidD_GetInputReport`.
- Desligar/desconectar volta para ~200°: o app reaplica modo nativo + rotação sozinho.
- O jogo deve abrir **depois** do modo nativo.
- Steam Input pode "roubar" o volante.
