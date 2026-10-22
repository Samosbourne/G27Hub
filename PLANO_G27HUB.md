# G27 Hub — Plano do projeto (v3)

> Documento de planejamento para o Claude Code (VS Code).
> Objetivo: um app próprio para Windows 10/11, **visualmente igual ao G HUB com um G29 conectado**, que faça pelo Logitech G27 tudo o que o LGS 5.10 / Logitech Profiler fazia, com luzes de RPM, console de diagnóstico, detecção inteligente de jogos e uma base de compatibilidade embutida.
> "G27 Hub" é um nome provisório (uso pessoal). Se um dia for publicar: trocar nome, ícones e cor de destaque, e não usar "Logitech", "G HUB", logos ou fotos oficiais.

---

## 1. Objetivo e regras de ouro

**O app deve:**
- reconhecer o G27 e colocá-lo em **modo nativo** (900°, pedais separados, embreagem, câmbio H) automaticamente, a cada conexão;
- ter as configurações do G HUB (G29) + LGS 5.10: rotação, sensibilidade, FFB, mola, amortecedor, força central, pedais combinados, "permitir que o jogo ajuste";
- mostrar **tudo animado em tempo real**: volante girando com o ângulo, barras dos pedais, câmbio, botões;
- ter **perfis por jogo**, aplicados sozinhos;
- entregar **force feedback** aos jogos, inclusive jogos de **32 bits**;
- acender as **luzes de RPM** com telemetria;
- ter um **console de diagnóstico** escondido, aberto só quando o usuário quiser;
- **detectar o jogo que está abrindo** e avisar se ele é compatível, se falta algo, ou se é **recomendado** ativar o Modo Compatibilidade Total;
- vir com uma **base de compatibilidade de jogos** já preenchida.

**Regras que o Claude Code deve seguir sempre:**
1. **Não escrever driver de kernel próprio.** O Modo Padrão é 100% modo usuário e funciona com a Integridade de Memória **ligada**.
2. O **Modo Compatibilidade Total (vJoy + HidHide) vem DESATIVADO** e só é ativado pelo usuário, manualmente, com confirmação. O app **nunca** ativa sozinho; no máximo **recomenda**.
3. Modo Padrão grava só em **HKCU**, sem admin. O Modo Compatibilidade Total pede admin e reinício, com aviso claro antes.
4. Tudo **reversível**. Ponto de restauração antes de instalar qualquer driver.
5. Não copiar código, ícones ou imagens do G HUB/LGS. O visual é reproduzido com recursos próprios.
6. Não copiar código GPL do Linux (`hid-lg4ff`); usar só os **fatos do protocolo**.
7. Código MIT de terceiros (`gcampa/g27-ffb-driverless`, `Jabelius/OpenG27`, SDK do vJoy) pode ser reaproveitado **com crédito**.
8. Todo comando HID novo é testado primeiro no `g27cli`.
9. **Tudo gera log** (seção 6), para o console mostrar o que deu certo ou errado.
10. O Claude Code **não tem o volante**: cada fase termina com um roteiro de teste que o John executa e reporta.

---

## 2. Arquitetura

```
┌───────────────────────────────────────────────────────────────────────┐
│                     G27Hub.App (WPF, .NET 10)                          │
│  Interface G HUB · bandeja · perfis · atribuições · LEDs               │
│  Console de diagnóstico · Detector de jogos · Notificações             │
│  Base de compatibilidade (compat-db.json + compat-user.json)           │
│  Gerenciador de modos:  [Padrão ✔ ativo]   [Compatibilidade Total ✖]    │
└───────┬──────────────────────────────┬───────────────────────┬────────┘
        │ HID (HidSharp)               │ memória compartilhada │ pipe de log
        │ modo nativo, rotação,        │ "Local\G27Hub_Shared" │ "\\.\pipe\G27Hub_Log"
        │ LEDs, autocentro, leitura    ▼                       ▼
        ▼                     ┌───────────────────────────────────────┐
┌──────────────┐   FFB        │ g27ffb.dll (C++, x86 + x64)            │
│  G27 (USB)   │◄─────────────│ DirectInput effect driver OEM          │
│ 046D:C29B    │              │ roda DENTRO do jogo · usa ffb-core     │
└──────────────┘              └───────────────────────────────────────┘

MODO COMPATIBILIDADE TOTAL (opcional, desligado de fábrica):
G27 real ─(HidHide esconde dos jogos)─► App aplica curvas/sensibilidade/pedais
combinados ─► vJoy (volante virtual) ─► jogo
jogo ─FFB─► vJoy ─► App ─► ffb-core ─► G27 real
```

**Componentes:**
- **G27Hub.Core** (C#): protocolo, detecção, leitura de entradas, perfis.
- **G27Hub.Logging** (C#): log central (arquivo + buffer em memória para o console + servidor do pipe que recebe logs da DLL).
- **ffb-core** (C++): motor de efeitos + mixer ~2 ms. Usado pela DLL (Modo Padrão) e pelo app via P/Invoke (Modo Compatibilidade Total).
- **g27ffb.dll** (C++, x86/x64): DirectInput effect driver OEM.
- **G27Hub.Games** (C#): detector de processos, base de compatibilidade, motor de recomendações.
- **G27Hub.Virtual** (C#): ponte vJoy + HidHide (isolada, para poder trocar no futuro).
- **G27Hub.Telemetry** (C#): provedores por jogo.
- **G27Hub.App** (WPF + WPF-UI).
- **G27Hub.Cli**: `g27cli`.

**Stack:** .NET 10 (LTS), WPF + WPF-UI, HidSharp, Serilog, Microsoft.Toolkit/CommunityToolkit para notificações do Windows; C++17 + MSVC + CMake.
**Dados:** perfis/config em `%APPDATA%\G27Hub\`; logs em `%LOCALAPPDATA%\G27Hub\logs\`.

**Estrutura de pastas:**
```
G27Hub/
├─ CLAUDE.md
├─ README.md
├─ docs/
│  ├─ protocolo-g27.md
│  ├─ investigacao-lgs.md      # resultado da Fase 0 (Forza + Process Explorer)
│  ├─ referencia-ui/           # prints do G HUB (G29) e do LGS/Profiler
│  └─ testes/
├─ assets/                     # fotos/SVG próprios do G27, pedais, câmbio
├─ data/
│  └─ compat-db.json           # base de compatibilidade embutida (seção 8)
├─ src/
│  ├─ G27Hub.Core/
│  ├─ G27Hub.Logging/
│  ├─ G27Hub.Games/
│  ├─ G27Hub.Virtual/
│  ├─ G27Hub.Telemetry/
│  ├─ G27Hub.App/
│  ├─ G27Hub.Cli/
│  └─ native/
│     ├─ ffb-core/
│     └─ g27ffb/
└─ tests/
   ├─ G27Hub.Core.Tests/
   └─ G27Hub.Games.Tests/
```

---

## 3. Protocolo do G27 (base de partida — VALIDAR no hardware)

Relatórios de saída HID de 7 bytes. Fonte: `hid-lg4ff` (Linux) e projetos MIT citados.

| Função | Bytes | Observação |
|---|---|---|
| Reverter modo ao resetar USB | `F8 0A 00 00 00 00 00` | primeiro passo do modo nativo |
| Trocar para G27 nativo (com detach) | `F8 09 04 01 00 00 00` | pode reenumerar de `C294` para `C29B` — confirmar |
| Rotação (40°–900°) | `F8 81 <LSB> <MSB> 00 00 00` | graus, little-endian |
| LEDs de RPM | `F8 12 <máscara 5 bits> 00 00 00 00` | confirmar a ordem dos bits |
| Desligar autocentro | `F5 00 00 00 00 00 00` | |
| Configurar autocentro | `FE 0D <k1> <k2> <força> 00 00` | validar sequência |
| Efeitos de força | família "classic" (slots 0–3) | estudar a `g27ffb.dll` do gcampa |

**Armadilhas:**
- Ler o estado inicial com `HidD_GetInputReport` (o volante só manda relatório quando algo muda).
- Ao desligar/desconectar volta para ~200°: reaplicar modo nativo sozinho.
- O jogo deve abrir **depois** do modo nativo.
- Steam Input pode "roubar" o volante.

---

## 4. Modos de compatibilidade

### 4.1 Modo Padrão (sem driver) — ATIVO de fábrica
- O jogo vê o **G27 real** (`046D:C29B`) → presets automáticos dos jogos funcionam (Forza, F1, WRC etc. reconhecem o G27 por VID/PID).
- Modo nativo, rotação, autocentro e LEDs pelo app; FFB pela `g27ffb.dll`.
- Integridade de Memória ligada, sem admin.
- Limitação: sensibilidade/curvas e pedais combinados por software ficam restritos (calibração do DirectInput e modo DFEX por perfil — validar).

### 4.2 Modo Compatibilidade Total (vJoy + HidHide) — DESATIVADO de fábrica
- Só para jogos **sem suporte nativo** ao G27 ou para quem quer sensibilidade/curvas/pedais combinados de verdade.
- Ativação **manual** em Configurações → Compatibilidade, ou **por perfil de jogo**. Passos: aviso claro → ponto de restauração → instala vJoy 2.2.2.0 (BrunnerInnovation, assinado) e HidHide (Nefarius) → reinicia.
- Aviso fixo na interface: **"Neste modo o jogo verá 'vJoy Device', não G27. Jogos com suporte nativo ao G27 (ex.: Forza) funcionam melhor no Modo Padrão."**
- Libera os sliders de Sensibilidade (Volante e Pedais).
- Desativar: desfaz tudo e reinicia.

### 4.3 LGS oficial — não é um modo do app
- O app **detecta** drivers antigos da Logitech (WingMan/LG*) e avisa que conflitam com o Modo Padrão, com guia de remoção.
- Guia de texto (não automatizado) para quem quiser usar o LGS 5.10 por conta própria, avisando que exige a Integridade de Memória desligada.

### 4.4 Resumo

| Recurso | Padrão | Compat. Total |
|---|---|---|
| Ativo de fábrica | ✅ | ❌ (manual) |
| Jogo reconhece "G27" | ✅ | ❌ (vê vJoy) |
| 900° / rotação | ✅ | ✅ |
| FFB DirectInput | ✅ | ✅ |
| Sensibilidade / curvas | ⚠️ limitado | ✅ |
| Pedais combinados | ⚠️ via DFEX | ✅ |
| Integridade de Memória ligada | ✅ | ✅ (validar vJoy no PC) |
| Precisa reiniciar | não | sim |

---

## 5. Interface — réplica do G HUB (G29)

Referência: prints em `docs/referencia-ui/`. **Antes de programar a interface, montar um protótipo estático e o John aprova lado a lado com os prints.**

### 5.1 Estilo geral
- Fundo **preto puro** (`#000000`), painéis **cinza-escuro** (`~#2A2A2A`) arredondados.
- Destaque **azul G HUB** (`~#1A9BFF`, ajustar pelo print): item selecionado da barra lateral, arco de rotação, checkboxes.
- Títulos grandes em negrito; rótulos de slider em **CAIXA ALTA, cinza-claro, pequenos**; valor numérico à direita.
- Sliders finos com bolinha branca; checkboxes quadradas.
- Janela sem borda, botões minimizar/maximizar/fechar finos no canto.

### 5.2 Tela inicial (sem volante)
- Fundo preto, texto central: **"Conecte seu volante G27"**.
- Superior esquerdo: ícone de menu. Superior direito: **seletor de perfil** ("Desktop: Padrão" ▾) e ícone de usuário/configurações.

### 5.3 Lista de dispositivos
- Aparece sozinha ao conectar o G27 (animação suave).
- Botões grade/lista no canto. **Card largo cinza-escuro**: miniatura do G27, nome **"G27"**, ícone de status, **engrenagem** à direita.

### 5.4 Página do volante
- Cabeçalho **"← G27"**; perfil e usuário à direita.
- **Barra lateral de ícones** (selecionado em quadrado azul): **+ Atribuições**, **Volante**, **Pedais**, **Força**, **Câmbio**, **LEDs**, **Teste/Calibração**. No rodapé: **engrenagem** (configurações) e **ícone de terminal** (console, seção 6).
- Painel à esquerda (título + cadeado), visual animado à direita, botão "ⓘ" no canto inferior direito.

### 5.5 Seções

**Volante** (igual ao G29)
- SENSIBILIDADE (desabilitada no Modo Padrão, com dica) · LIMITES OPERACIONAIS (ÂNGULO) 40–900 · CENTRALIZAÇÃO DA FORÇA DA MOLA · ☐ MOLA CENTRALIZADORA PARA JOGOS SEM FEEDBACK DE FORÇA · [RESTAURAR A CONFIGURAÇÃO ORIGINAL]
- Direita: G27 **girando em tempo real**, **arco de riscos azuis** do tamanho do limite, **ângulo atual** no topo com triângulo.

**Pedais** (igual ao G29)
- SENSIBILIDADE DA EMBREAGEM / FREIO / ACELERADOR (Modo Compatibilidade Total) · ☐ PEDAIS COMBINADOS · botão "Calibrar pedais"
- Direita: três **medidores verticais segmentados** enchendo ao vivo + imagem dos pedais.

**Força** (do LGS 5.10)
- ☑ ATIVAR FORCE FEEDBACK · INTENSIDADE GLOBAL · INTENSIDADE DA MOLA · INTENSIDADE DO AMORTECIMENTO · ☑ FORÇA CENTRAL + INTENSIDADE · ☑ PERMITIR QUE O JOGO AJUSTE CONFIGURAÇÕES · INVERTER FORÇA
- Direita: gráfico ao vivo da força + indicador de clipping.

**Câmbio**: desenho do câmbio H com marcha atual destacada (1–6, R, N), botões acendendo, modo H/sequencial.

**Atribuições** (Profiler no visual G HUB): pontos clicáveis em cada botão; ao clicar → Padrão / Tecla / Combinação / Macro / Desativar; o botão apertado no volante físico pisca na imagem.

**LEDs**: 5 LEDs sobre o volante, limites em %, modo (progressivo / troca / limitador), fonte de telemetria, "Testar".

**Teste/Calibração**: tudo ao vivo (volante, pedais, embreagem, direcional, câmbio, botões) + assistente de calibração.

### 5.6 Perfis
- "Desktop: Padrão" + perfis por jogo (ícone do `.exe`) + "Adicionar jogo".
- Ao adicionar um jogo, o app **já consulta a base de compatibilidade** e preenche o perfil sugerido (rotação, modo, telemetria, checklist).

### 5.7 Notificações no estilo G HUB
- Toast escuro arredondado no canto inferior direito do app **e** notificação do Windows (quando o app está minimizado).
- Ícone do jogo à esquerda, título em negrito, 1–2 linhas, botões de ação em azul.

### 5.8 Animações
- Leitura em thread própria; UI a 60 fps com interpolação do ângulo.
- Transições de 150–250 ms; hover e brilho azul no selecionado.

---

## 6. Console de diagnóstico

**Escondido por padrão.** Abre só quando o usuário clicar.

**Como abrir:**
- ícone de **terminal** no rodapé da barra lateral, ou
- atalho **Ctrl + Shift + L**, ou
- clique numa notificação de erro ("Ver detalhes").

Quando há erro e o console está fechado, aparece só uma **bolinha vermelha** no ícone.

**Aparência:** painel que desliza de baixo (estilo console de desenvolvedor), fundo `#0A0A0A`, fonte monoespaçada, pode ser destacado em janela própria.

**Conteúdo:**
- Linhas com horário, origem e nível colorido: `INFO` (cinza), `OK` (verde), `AVISO` (amarelo), `ERRO` (vermelho).
- Origens: `HID`, `MODO`, `FFB` (vindo da DLL dentro do jogo, pelo pipe), `JOGO` (detector), `PERFIL`, `LED`, `TELEMETRIA`, `VJOY`.
- Exemplos:
  ```
  14:02:11  OK     HID     G27 conectado (046D:C294) → enviando modo nativo
  14:02:12  OK     HID     G27 em modo nativo (046D:C29B), rotação 900°
  14:05:40  INFO   JOGO    ForzaHorizon5.exe iniciado → perfil "Forza Horizon 5"
  14:05:41  AVISO  JOGO    Controle de Xbox conectado — o Forza pode usá-lo como "Device 1"
  14:05:52  OK     FFB     g27ffb.dll (x64) carregada no ForzaHorizon5.exe
  14:05:53  INFO   FFB     Efeito Constant criado · Download mag=-3120
  14:20:02  ERRO   FFB     Falha ao escrever no HID (dispositivo desconectado)
  ```

**Ferramentas do console:**
- filtros por nível e origem, busca, pausar, limpar;
- **Copiar** e **Exportar .txt** (para mandar ao Claude Code quando algo der errado);
- **modo detalhado** (verbose) liga/desliga;
- **Testes rápidos**, cada um mostra ✓ ou ✗:
  - volante conectado · modo nativo ativo · rotação aplicada
  - DLL de FFB registrada (32 e 64 bits)
  - pulso de força de teste (leve) · teste dos LEDs
  - Integridade de Memória (ligada/desligada) · drivers antigos da Logitech presentes
  - vJoy / HidHide (instalados? ativos?)

**Implementação:**
- `G27Hub.Logging` com Serilog: arquivo rotativo + buffer em memória (últimas ~5.000 linhas) para o console.
- A `g27ffb.dll` manda logs pelo pipe `\\.\pipe\G27Hub_Log` (sem travar o jogo: fila não bloqueante; se o app estiver fechado, grava em `logs\ffb-<pid>.log`).

---

## 7. Detector inteligente de jogos

**O que faz:** percebe quando um jogo está abrindo, consulta a base de compatibilidade e os perfis, e **avisa** o usuário. **Nunca ativa nada sozinho** (exceto aplicar o perfil do jogo, que é o comportamento normal).

**Detecção:**
- Monitor de processos leve (eventos WMI de criação de processo ou varredura a cada ~2 s), sem admin.
- Compara o nome do `.exe` (e, se preciso, a pasta) com `compat-db.json` + `compat-user.json` + perfis.

**Verificações ao abrir um jogo (pré-voo):**
1. **Status do jogo na base** (seção 8).
2. **Modo atual × modo recomendado:**
   - jogo precisa de Compatibilidade Total e ela está desligada → **recomendar ativar**;
   - jogo tem suporte nativo ao G27 e a Compatibilidade Total está ligada → **recomendar desativar** (senão o jogo não vê o G27).
3. **Volante em modo nativo?** Se não estiver, aplicar e avisar: "reinicie o jogo para ele detectar os 900°".
4. **Outros controles conectados** (ex.: controle de Xbox) em jogos que escolhem o "Device 1" (Forza) → avisar.
5. **Steam Input** ligado para o jogo (ler a configuração da Steam quando possível) → avisar.
6. **Jogo desconhecido**: análise leve depois de alguns segundos — se o jogo carregou só bibliotecas de controle de Xbox (XInput) e nenhuma de DirectInput, avisar "este jogo parece aceitar apenas controle de Xbox". **Não fazer essa análise em jogos marcados com anti-cheat.**

**Exemplos de notificação:**
- ✅ **Forza Horizon 5** — "Compatível com o G27 no Modo Padrão. Perfil aplicado: 900°."
- ⚠️ **Forza Horizon 5** — "Um controle de Xbox está conectado. Desconecte-o para o Forza usar o G27 como Device 1."
- 💡 **Jogo X** — "Este jogo não tem suporte nativo ao G27. **Recomendado:** ativar o Modo Compatibilidade Total (requer reiniciar o PC)." Botões: **[Ver como ativar]** **[Agora não]** **[Não avisar para este jogo]**
- ⚠️ **Need for Speed Unbound** — "Suporte a volante limitado neste jogo. O FFB pode não funcionar em nenhum modo."

**Depois que o jogo fecha:** pergunta discreta e opcional "O volante funcionou bem no Jogo X? 👍 / 👎". A resposta vai para `compat-user.json` e melhora as próximas recomendações daquele PC.

---

## 8. Base de compatibilidade de jogos

**Arquivos:**
- `data/compat-db.json`: base embutida (vem com o app).
- `%APPDATA%\G27Hub\compat-user.json`: resultados do próprio usuário (sobrescrevem a base).
- Futuro (opcional): atualizar a base a partir de um arquivo num repositório GitHub próprio.

**Formato de uma entrada:**
```json
{
  "id": "forza-horizon-5",
  "nome": "Forza Horizon 5",
  "exes": ["ForzaHorizon5.exe"],
  "status": "nativo",
  "modoRecomendado": "padrao",
  "confianca": "oficial",
  "fonte": "https://support.forza.net/hc/en-us/articles/4409263414547",
  "rotacaoSugerida": 540,
  "telemetria": "forza-dataout",
  "antiCheat": false,
  "checklist": ["desligar-steam-input", "desconectar-outros-controles"],
  "notas": "Reconhece o G27 por VID/PID (PC). FFB vai para o Device 1."
}
```

**Valores de `status`:**
- `nativo` — o jogo tem preset oficial para o G27 → Modo Padrão.
- `directinput` — sem preset, mas aceita volante DirectInput (configura os controles uma vez) → Modo Padrão.
- `precisa-compat-total` — só funciona bem com volante virtual → recomendar Compatibilidade Total.
- `limitado` — suporte fraco a volante; nenhum modo resolve totalmente.
- `desconhecido`.

**Valores de `confianca`:** `oficial` (lista da desenvolvedora), `comunidade` (relatos de usuários), `testado` (o John testou), `a-confirmar`.

**Base inicial (pesquisada em out/2026 — os nomes de `.exe` devem ser conferidos no PC):**

| Jogo | Status | Confiança | Observação |
|---|---|---|---|
| Forza Horizon 5 | nativo | oficial | G27 só no PC; reconhecido por VID/PID |
| Forza Horizon 6 | nativo | oficial | G27 com "legacy support" (PC) |
| Forza Motorsport (2023) | nativo | oficial | G27 na lista de PC |
| F1 25 | nativo | oficial | G27 na lista de PC da EA, com preset |
| EA Sports WRC | nativo | oficial | G27 na lista de PC |
| The Crew Motorfest | nativo | oficial | G27 na lista de PC da Ubisoft |
| Assetto Corsa | nativo | comunidade | Preset G27; CSP tem extensão de LEDs do G27 |
| Assetto Corsa Competizione | nativo | comunidade | FFB testado com driverless (gcampa) |
| Assetto Corsa EVO | directinput | comunidade | Relatos de FFB funcionando no G27 |
| iRacing | nativo | comunidade | Ajustar 900° no app |
| rFactor 2 / Le Mans Ultimate | directinput | a-confirmar | Conflito conhecido com restos do SDK do G HUB |
| Euro Truck Simulator 2 / ATS | directinput | comunidade | Atenção à mola de centragem |
| BeamNG.drive | directinput | a-confirmar | |
| Automobilista 2 | directinput | a-confirmar | |
| DiRT Rally 2.0 / DiRT 5 | nativo | comunidade | DiRT 5: sem restos do G HUB |
| Wreckfest / Wreckfest 2 | directinput | comunidade | OpenG27 tem FFB por telemetria no Wreckfest 2 |
| Need for Speed Unbound | limitado | comunidade | Relato: G27 reconhecido como controle de Xbox, sem FFB |

**Tarefa da Fase 6:** o Claude Code transforma essa tabela em `compat-db.json`, confere os nomes de `.exe` e adiciona mais jogos com fonte. Entradas sem fonte ficam como `a-confirmar`.

---

## 9. Fases de desenvolvimento

Cada fase termina com **critérios de aceite** e roteiro em `docs/testes/faseN.md`.

### Fase 0 — Preparação (John)
- [ ] Guardar `lgs510_x64.exe` (a Logitech retira do site em 31/10/2026).
- [ ] **Investigação do Forza com o LGS funcionando:** Process Explorer → DLLs do processo do Forza (procurar `Wm`, `Logi`, `LG`, `dinput8`, `Windows.Gaming.Input`) + exportar as chaves `...\MediaProperties\PrivateProperties\Joystick\OEM\VID_046D&PID_C29B` (HKLM e HKCU). Salvar em `docs/investigacao-lgs.md`.
- [ ] Salvar os prints em `docs/referencia-ui/`; fotos do G27/pedais/câmbio em `assets/`.
- [ ] Ponto de restauração; remover LGS/G HUB e drivers WingMan/LG*; confirmar G27 como "Dispositivo de entrada USB".
- [ ] Instalar .NET 10 SDK, Visual Studio Build Tools (C++), CMake, Git.

### Fase 1 — Núcleo + CLI + log
- [ ] Detecção, leitura e decodificação das entradas; modo nativo, rotação, autocentro, LEDs; reconexão automática.
- [ ] `G27Hub.Logging` (arquivo + buffer em memória) usado desde o início.
- [ ] `g27cli`: `list`, `native`, `range`, `autocenter`, `leds`, `monitor`, `log`.
- [ ] Testes unitários; `docs/protocolo-g27.md`.

**Aceite:** 900°, pedais separados e embreagem no `joy.cpl`; LEDs na ordem certa; log em arquivo mostrando cada passo.

### Fase 2 — Force feedback (Modo Padrão)
- [ ] `ffb-core` + `g27ffb.dll` x86/x64; registro HKCU; `g27cli ffb register|unregister`.
- [ ] Memória compartilhada + logs da DLL pelo pipe.

**Aceite:** FFB em AC/ACC, num jogo 32 bits e **no Forza Horizon 5/6** (o log precisa mostrar a DLL carregada e efeitos chegando).

### Fase 3 — Protótipo da interface
- [ ] Mockup estático das telas 5.2–5.5 + console (6) + notificação (5.7), comparado com os prints. John aprova.

### Fase 4 — Interface funcional + console
- [ ] Telas ligadas ao Core, com animações em tempo real.
- [ ] Console de diagnóstico completo (seção 6), incluindo testes rápidos.
- [ ] Bandeja e iniciar com o Windows.

**Aceite:** girar o volante gira a imagem com o ângulo certo; pedais enchem as barras; o console mostra conexão, modo nativo e erros.

### Feito entre a Fase 4 e a 5 (out/2026)
- [x] Minijogo 3D de force feedback (teste automático + direção livre infinita).
- [x] Mapa de botões do G27 confirmado no volante (ver `docs/protocolo-g27.md`).
- [x] Força mínima (zona morta do centro), "Permitir que o jogo ajuste" (ganho do jogo) e mola devolvida no valor do app quando o jogo fecha.

> Itens abaixo marcados com (F) vêm de `FUNCIONALIDADES_G29_PARA_G27.md` (complemento deste plano).

### Fase 5a — Perfis
- [x] Perfis JSON; "Desktop: Padrão" + perfis por jogo; troca automática.
- [x] (F) Criar, duplicar, renomear, excluir perfil; adicionar jogo pelo `.exe` com ícone extraído.
- [x] (F) Cadeado: trava o perfil atual (sem troca automática); perfil persistente (Configurações do app).
- [x] (F) Exportar/importar `.g27profile`.
- [x] (F) "Usar configurações especiais do volante" por perfil (rotação, mola, FFB sobrescrevem o global).
- [x] (F) Menu ☰ (Jogos e apps, Configurações, Console, Sobre); página da engrenagem do card (nome, versão USB, teste de LEDs, calibração, modo).
- [x] (F) Configurações do app: notificações liga/desliga.

### Fase 5b — Atribuições
- [x] Teclas simples e com modificadores (`SendInput` com scan code); escolher o botão clicando na imagem, na lista ou apertando o botão.
- [ ] Arrastar e soltar ações na imagem (o G HUB faz assim; hoje é clique).
- [x] (F) Macros: gravar, atrasos, repetir enquanto segura, liga/desliga, sequência.
- [x] (F) Ações do sistema: mídia, volume, abrir programa, abrir site; trocar de perfil pelo volante.
- [x] (F) Camada secundária (segurar um botão = "shift" que dobra as funções).
- [x] (F) Desativar atribuições dentro do jogo (por perfil).
- [x] (F) Pedal como botão (passar de X% = tecla); câmbio H como sequencial (marcha acima/abaixo = teclas).
- [x] (F) Atalho de rotação no volante (combinação de botões alterna 180/270/540/900, com toast e LEDs).

### Fase 6 — Detector inteligente + base de compatibilidade
> Decisão do John (21/10/2026): o detector é **opcional e vem desligado** (Configurações do app). A troca automática de perfil (5a) é independente.
- [x] `compat-db.json` a partir da seção 8 (com fontes).
- [x] Monitor de processos, verificações de pré-voo, notificações (in-app + Windows), "Não avisar para este jogo". Steam Input: lembrete quando a Steam está aberta (não lê a config da Steam).
- [x] Pergunta pós-sessão 👍/👎 → `compat-user.json`.
- [x] "Adicionar jogo" já preenche o perfil pela base.
- [x] (F) Detectar jogos instalados (Steam, Epic, Xbox/Microsoft Store), por botão; detector liga/desliga.
- [x] (F) Dicas por jogo ("configurações recomendadas no jogo") e aviso de steer lock diferente do app.

**Aceite:** abrir o Forza mostra "Compatível — Modo Padrão"; abrir um jogo marcado `precisa-compat-total` mostra a recomendação sem ativar nada; com um controle de Xbox conectado, o aviso do Device 1 aparece.

### Fase 7 — LEDs de RPM
- [x] Provedores: AC/ACC, F1 22–25, BeamNG (mod próprio com a RPM máxima de cada carro), ETS2/ATS (plugin próprio do SCS SDK), LFS (OutGauge), Forza. Editor (modo + 5 limites) e modo teste.
- [ ] iRacing, rFactor 2: ficam para quando o John jogar algum (precisam de plugin de terceiros ou não dá para testar aqui).
- [x] (F) "Permitir que os jogos controlem os LEDs"; limitador de box, combustível baixo, ponto de troca por carro (pela RPM máxima). Bandeiras: não (nenhum jogo instalado manda).
- [ ] (F) Rotação automática por carro: não feito. BeamNG e AC já fazem trava virtual pelo FFB; mudar a rotação no meio do jogo tira a direção do 1:1.

### Fase 8 — Modo Compatibilidade Total (opcional, desligado de fábrica)
> Dividida em 8a (instalar/desinstalar), 8b (volante virtual + HidHide) e 8c (FFB de volta). Decisão do John (22/10/2026):
> o app funciona normal e só instala os drivers quando ele clica em ativar.
- [x] 8a: gerenciador: detectar/instalar/desinstalar vJoy 2.2.2.0 e HidHide 1.5.230 (instaladores oficiais fixados por SHA-256 + assinatura), ponto de restauração, reinício.
- [x] 8b: volante virtual (vJoy 1: X volante, Y/Z/Rx pedais, Ry combinados, 24 botões, POV contínuo, FFB habilitado), HidHide escondendo o G27 e liberando o app; liga/desliga no card do G27 com borda vermelha + selo (pedido do John). Global (não por perfil, por enquanto); desligado ao fechar o app.
- [x] Pipeline com curvas/sensibilidade/pedais combinados (a cada relatório do G27) e FFB de volta ao G27 (8c: pacotes PID do vJoy → IDirectInputEffectDriver da g27ffb.dll carregada no app → mesmo motor do Modo Padrão).
- [x] Avisos da seção 4.2 (confirmação ao ativar, selo/borda); detector recomenda voltar ao Padrão em jogo nativo, com botão.
- [ ] Seletor por perfil: não feito (o jogo enumera os dispositivos antes do app perceber que ele abriu; troca manual).
- [~] (F) Mapeamento por eixo: sensibilidade feita; zona morta/faixa/inverter ficam com as opções do próprio jogo.

**Aceite:** num jogo de teste funciona com sensibilidade e FFB; desativar devolve tudo ao normal após reiniciar; o detector recomenda desligar ao abrir um jogo `nativo`.

### Fase 9 — Assistente de primeira execução + polimento
- [x] **Assistente na primeira abertura:** verifica drivers antigos da Logitech, Integridade de Memória, Steam Input, coloca o volante em modo nativo e faz um teste rápido de FFB e LEDs.
- [x] Assistente de calibração dos pedais (calibração do usuário do DirectInput em HKCU: vale para todos os jogos); README com créditos.
- [x] Instalador (Inno Setup, por usuário, sem admin, .NET embutido, PT/EN) + desinstalação limpa (`--uninstall-cleanup`): installer/build.ps1.
- [x] (F) Botão ⓘ com dicas de cada tela.
- [x] (F) Idioma PT/EN: botão do globo no topo; reabre o app no idioma escolhido (log técnico continua em português).

### Fase 10 — Extras
Ver seção 10. Somam-se (F): linearização do FFB (LUT), overlay de entradas para stream, painel no celular, vibração extra por telemetria, compatibilidade com o SDK da Logitech, verificar atualizações, perfis numa pasta compartilhada (OneDrive/Drive).

**Fora (decidido em 21/10/2026):** TRUEFORCE, atualização de firmware e modo PS (o G27 não tem); batente virtual (o G27 já trava no limite pelo comando de rotação).

---

## 10. Funcionalidades extras

| Extra | O que faz | Dificuldade |
|---|---|---|
| **Desfazer alterações** | Guarda um "antes" de cada mudança importante; botão "voltar ao estado anterior" | Baixa |
| **Batente virtual** | "Parede" de força no fim do curso configurado | Média |
| **Medidor de clipping do FFB** | Mostra quando a força satura, com dica de ajuste | Baixa |
| **Rotação automática por carro** | Ajusta os graus ao carro (AC, ACC, iRacing) | Média |
| **LEDs em modo notificação** | Pisca para bandeira amarela, combustível baixo, limitador | Média |
| **Shift light na tela** | Overlay com as luzes, para gravar vídeo | Média |
| **Atalho "reaplicar tudo"** | Corrige na hora se o volante voltar para 200° | Baixa |
| **Modo DFEX por jogo** | Volante como Driving Force EX para jogos muito antigos | Baixa (testar) |
| **Vibração extra por telemetria** | Zebra / motor somados ao FFB | Alta |
| **Compatibilidade com SDK da Logitech** | DLL substituta da API do SDK para jogos que dependem dela. Experimental | Alta |
| **Estatísticas de uso** | Horas por jogo etc. | Baixa |
| **Backup dos perfis** | Exportar para OneDrive/Google Drive | Baixa |

---

## 11. Riscos e pontos a validar

1. **Bytes dos comandos**: confirmar um por um no hardware.
2. **Reenumeração** `C294 → C29B`: tratar os dois casos.
3. **FFB do Forza pela DLL user-mode**: maior incerteza do projeto; teste obrigatório na Fase 2. A investigação da Fase 0 indica o caminho.
4. **Calibração pelo registro do DirectInput**: confirmar se os jogos respeitam.
5. **vJoy**: manutenção limitada; testar com a Integridade de Memória ligada antes da Fase 8. Código isolado em `G27Hub.Virtual`.
6. **Anti-cheat**: não fazer análise de módulos em jogos com anti-cheat; testar DLL de FFB caso a caso.
7. **Jogos com SDK da Logitech**: só com o extra "Compatibilidade com SDK".
8. **Detector de jogos**: nomes de `.exe` mudam entre versões (Steam × Microsoft Store); aceitar vários nomes por jogo.
9. **Conflito de escrita HID** entre app e DLL: com jogo ativo e "permitir jogo ajustar", o app não mexe no autocentro.
10. **Política de drivers do Windows** mais rígida em 2026 (Integridade de Memória ligada por padrão desde outubro): mais um motivo para o Modo Padrão ser o principal.

---

## 12. Referências

- gcampa/g27-ffb-driverless (MIT): https://github.com/gcampa/g27-ffb-driverless
- Jabelius/OpenG27 (MIT): https://github.com/Jabelius/OpenG27
- `hid-lg4ff` (só protocolo): https://codebrowser.dev/linux/linux/drivers/hid/hid-lg4ff.c.html
- vJoy (BrunnerInnovation): https://github.com/BrunnerInnovation/vJoy
- HidHide (Nefarius): https://github.com/nefarius/HidHide
- HidSharp: https://www.zer7.com/software/hidsharp
- WPF-UI: https://github.com/lepoco/wpfui
- Forza Horizon 5 — volantes suportados: https://support.forza.net/hc/en-us/articles/4409263414547-FH5-Supported-Wheels-and-Devices
- Forza Horizon 6 — volantes suportados: https://support.forza.net/hc/en-us/articles/51674028831251-FH6-Supported-Wheels-and-Devices
- Forza Motorsport — volantes suportados: https://support.forza.net/hc/en-us/articles/21424060393107-Forza-Motorsport-Supported-Wheels-and-Devices
- F1 25 — controles e volantes: https://help.ea.com/en/articles/f1/f1-25/controllers-wheels-and-vr-support/
- EA Sports WRC — dispositivos suportados: https://www.overtake.gg/threads/ea-sports-wrc-%E2%80%93-supported-pc-and-console-controller-devices.265028/
- The Crew Motorfest — volantes suportados: https://www.ubisoft.com/en-us/game/the-crew/motorfest/news-updates/nV9xO7iPHNa7eHP3I2oBs/the-crew-motorfest-steering-wheel-and-peripheral-support
- NFS Unbound — relato com G27: https://steamcommunity.com/app/1846380/discussions/0/4637114422843801428

---

## 13. Primeiro prompt sugerido para o Claude Code

> Leia `PLANO_G27HUB.md`. Crie o `CLAUDE.md` resumindo as regras da seção 1, os modos da seção 4, o console da seção 6 e a estrutura de pastas da seção 2. Depois execute **somente a Fase 1**: crie a solução .NET 10 com `G27Hub.Core`, `G27Hub.Logging`, `G27Hub.Cli` e `G27Hub.Core.Tests`, implemente detecção, leitura de entradas, os comandos da seção 3 e o log em arquivo, e escreva `docs/testes/fase1.md` para eu testar no volante. Não avance para a Fase 2 até eu confirmar os testes.
