# Funcionalidades do G HUB (G29) para o G27 Hub

> Complemento do `PLANO_G27HUB.md`. Lista tudo o que o G HUB oferece para o G29, o que o LGS/Profiler tinha a mais, e extras úteis, já classificado para o G27.
> **Legenda:** ✅ mapeia direto no G27 · 🔧 precisa adaptação · ❌ impossível no G27
> **Prioridade:** P1 essencial · P2 importante · P3 avançado/opcional

---

## 1. Telas gerais do G HUB

| Funcionalidade | G27 | Prioridade | Como fazer no G27 Hub |
|---|---|---|---|
| Tela vazia "Conecte seu equipamento" | ✅ | P1 | Texto central; troca sozinha quando o G27 conecta |
| Lista de dispositivos com alternância grade/lista | ✅ | P1 | Card do G27 com miniatura, nome, status e engrenagem |
| Engrenagem do card → configurações do dispositivo | ✅ | P1 | Abre a página de configurações do dispositivo (seção 6) |
| Menu lateral (☰) | ✅ | P2 | Itens: Jogos e apps, Configurações, Console, Sobre |
| Ícone de usuário/conta | 🔧 | P3 | Sem conta online; usar para "Configurações do app" |
| Ícone ⓘ de informações em cada tela | ✅ | P2 | Dicas curtas da seção aberta |

---

## 2. Perfis (seletor do topo)

| Funcionalidade | G27 | Prioridade | Como fazer |
|---|---|---|---|
| Perfil "Desktop: Padrão" | ✅ | P1 | Perfil base, usado fora dos jogos |
| Detecção automática de jogos instalados | ✅ | P1 | Varrer bibliotecas Steam, Epic, Xbox/Microsoft Store e pastas comuns |
| **Troca automática de perfil ao abrir o jogo** | ✅ | P1 | Detector de processos (já previsto) + toast "Perfil: Jogo X" |
| **Cadeado (travar perfil)** | ✅ | P1 | Ícone de cadeado no painel: trava o perfil atual para todos os jogos, sem troca automática |
| Perfil persistente (configuração global) | ✅ | P2 | Opção em Configurações do app: "Sempre usar este perfil" |
| Criar, duplicar, renomear, excluir perfil | ✅ | P1 | Menu do seletor de perfil |
| Adicionar jogo/app manualmente | ✅ | P1 | Escolher `.exe`; ícone extraído do arquivo |
| Exportar/importar perfil | ✅ | P2 | Arquivo `.g27profile` (JSON) |
| Perfis da comunidade / nuvem (conta Logitech) | 🔧 | P3 | Sem nuvem; opcional: pasta compartilhada (OneDrive/Drive) |
| Ícone do jogo no seletor | ✅ | P1 | Extrair do `.exe` |

---

## 3. Volante (aba Volante)

| Funcionalidade | G27 | Prioridade | Como fazer |
|---|---|---|---|
| **Limites operacionais (ângulo)** 40–900° | ✅ | P1 | Comando HID de rotação |
| **Sensibilidade** (50 = linear) | 🔧 | P1 | Curva de resposta: completa no Modo Compatibilidade Total; no Modo Padrão desabilitado com dica |
| **Centralização da força da mola** | ✅ | P1 | Autocentro do hardware |
| ☐ Mola centralizadora para jogos sem FFB | ✅ | P1 | Ligar autocentro só quando o jogo não criar efeitos |
| Restaurar configuração original | ✅ | P1 | Botão por seção |
| Volante animado + arco + ângulo ao vivo | ✅ | P1 | Já previsto |
| TRUEFORCE (G923) | ❌ | — | O G27 não tem esse hardware |

---

## 4. Pedais (aba Pedais)

| Funcionalidade | G27 | Prioridade | Como fazer |
|---|---|---|---|
| Sensibilidade da embreagem / freio / acelerador | 🔧 | P1 | Curva por pedal (Modo Compatibilidade Total); no Padrão, só calibração |
| ☐ Pedais combinados | 🔧 | P1 | Compat. Total (eixo virtual) ou modo DFEX por perfil |
| Medidores ao vivo | ✅ | P1 | Já previsto |
| Embreagem presente | ✅ | P1 | O G27 tem embreagem (G29 também) |

---

## 5. Atribuições (aba "+")

O G HUB deixa arrastar uma ação para um botão na imagem do volante.

| Funcionalidade | G27 | Prioridade | Como fazer |
|---|---|---|---|
| Atribuir **teclas** (simples e com modificadores) | ✅ | P1 | `SendInput` |
| **Macros**: gravar, atrasos, repetir enquanto segura, alternar liga/desliga, sequência | ✅ | P2 | Editor de macro com gravação |
| **Ações do sistema**: mídia (play/pause, volume), abrir programa, abrir site | ✅ | P2 | Comandos do Windows |
| **Trocar de perfil** por botão do volante | ✅ | P2 | Ação interna do app |
| **Integrações** (OBS, Discord etc.) | 🔧 | P3 | Atalhos de teclado configurados nesses apps ou OBS WebSocket |
| Arrastar e soltar na imagem | ✅ | P1 | Já previsto (pontos clicáveis + arrastar) |
| Camada secundária (segurar um botão = "shift" para os outros) | ✅ | P2 | Botão modificador por perfil: dobra as funções |
| Desativar atribuições dentro do jogo | ✅ | P1 | Por perfil: "Botões enviam só o padrão do volante" |
| Botão PS / seletor giratório vermelho do G29 | ❌ | — | O G27 não tem; no lugar, usar os botões do câmbio |

**Botões do G27 a mapear** (confirmados no volante em 21/10/2026): 2 borboletas, 6 botões vermelhos no volante (3 em cada lateral), 4 vermelhos + 4 pretos na base do câmbio, direcional (PV), câmbio H (marchas 1–6 + ré). Números em `docs/protocolo-g27.md`.

---

## 6. Configurações do dispositivo (engrenagem do card)

| Funcionalidade | G27 | Prioridade | Como fazer |
|---|---|---|---|
| Informações do dispositivo (nome, versão de firmware) | 🔧 | P2 | Ler versão do descritor USB; sem atualização |
| Atualizar firmware | ❌ | — | Não existe firmware novo para o G27 |
| Modo PS3/PS4 | ❌ | — | O G27 não tem chave de modo |
| Calibração | ✅ | P1 | Assistente (já previsto) |
| Teste de LEDs | ✅ | P1 | Animação de teste |
| Modo de compatibilidade (Padrão / Compat. Total) | ✅ | P1 | Já previsto |

---

## 7. Configurações do app

| Funcionalidade | G27 | Prioridade |
|---|---|---|
| Iniciar com o Windows (minimizado) | ✅ | P1 |
| Detecção automática de jogos liga/desliga | ✅ | P1 |
| Perfil persistente | ✅ | P2 |
| Notificações liga/desliga (in-app e Windows) | ✅ | P1 |
| Idioma (PT/EN) | ✅ | P2 |
| **Permitir que os jogos controlem os LEDs** | 🔧 | P2 |
| Verificar atualizações do app | 🔧 | P3 |
| Tema (só escuro, como o G HUB) | ✅ | — |

---

## 8. O que os jogos pedem pelo SDK de volantes da Logitech

Jogos que usam o SDK da Logitech pedem ao software (G HUB/LGS) coisas que o G27 Hub pode imitar no extra "Compatibilidade com SDK" (P3, experimental).

**Efeitos prontos:**
- mola, amortecedor, força constante
- colisão lateral e frontal
- estrada de terra, estrada esburacada, estrada escorregadia
- carro no ar, batente suave (soft stop)

**LEDs:** o jogo manda o RPM atual, o RPM em que o primeiro LED acende e o RPM de corte.

**Propriedades** (o que "Permitir que o jogo ajuste" libera): faixa do volante, FFB ligado, ganho geral, ganho da mola, ganho do amortecedor, pedais combinados, mola padrão.

Implementar como DLL substituta tem riscos legais e técnicos; deixar por último.

---

## 9. Do LGS 5.10 / Profiler (o que o G HUB não tem)

| Funcionalidade | Prioridade | Observação |
|---|---|---|
| Intensidade global do FFB | P1 | Aba Força |
| Intensidade da mola e do amortecedor | P1 | Aba Força |
| Força central em jogos com FFB + intensidade | P1 | Aba Força |
| Permitir que o jogo ajuste configurações | P1 | Aba Força |
| "Usar configurações especiais do volante" por perfil | P1 | Perfil sobrescreve o global |
| Página de teste (volante, PV, embreagem, pedais) | P1 | Aba Teste, mais bonita |
| Mapeamento por eixo (volante, acelerador, freio, embreagem) com sensibilidade, zona morta e faixa por eixo | P2 | Compat. Total para alterar o sinal; no Padrão só calibração |
| Câmbio esquerdo/direito (borboletas) e PV1 mapeáveis | P1 | Aba Atribuições |
| Inverter eixos | P2 | Compat. Total |

---

## 10. Extras da comunidade (SimHub, Oversteer, CSP, ferramentas de FFB)

### Essenciais (P1)
- **Força mínima global:** compensa a zona morta do centro, problema conhecido do G27/G29. Slider na aba Força.
- **Medidor de clipping:** mostra quando o FFB satura.
- **Dicas por jogo:** cada perfil mostra "Configurações recomendadas no jogo" (ex.: Forza com mola central em 0; ACC com steer lock igual ao do app e força mínima). Vem da base de compatibilidade.
- **Aviso de steer lock:** lembrar que a rotação do jogo deve bater com a do app.
- **Atalho de rotação no volante:** combinação de botões (ex.: segurar os 2 botões vermelhos + borboleta) troca 180/270/540/900, com toast e LEDs piscando a quantidade.

### Importantes (P2)
- **Linearização do FFB (LUT):** curva que corrige a resposta não linear das engrenagens. Assistente simples ou importar arquivo LUT.
- **Batente virtual (soft lock).**
- **Rotação automática por carro** (AC, ACC, iRacing via telemetria).
- **Pedal como botão:** passar de X% num pedal = tecla (ex.: embreagem vira freio de mão em jogos de rally). Funciona no Modo Padrão via `SendInput`.
- **Câmbio H em modo sequencial** por perfil (marcha acima/abaixo = teclas).
- **Trocar de perfil pelo volante.**
- **LEDs avançados:** limitador de box, bandeiras, combustível baixo, ponto de troca por carro.

### Avançados (P3)
- **Overlay de entradas para gravar/stream:** volante, pedais e marcha na tela.
- **Painel no celular:** RPM, marcha e LEDs numa página web local.
- **Vibração extra por telemetria** (zebra, motor).
- **Compatibilidade com SDK da Logitech** (seção 8).

---

## 11. Comportamentos da interface do G HUB para copiar

- Troca de perfil automática com toast discreto.
- Cadeado no canto do painel para travar o perfil.
- Arrastar e soltar ações em cima da imagem do volante.
- "Restaurar configuração original" em cada aba.
- Valores mudam **ao vivo** no volante enquanto arrasta o slider (sem botão "Aplicar").
- Card do dispositivo com engrenagem; alternância grade/lista.
- Animações curtas e discretas; destaque azul no item ativo.

---

## 12. Lista para colar no Claude Code

> Leia `PLANO_G27HUB.md` e `FUNCIONALIDADES_G29_PARA_G27.md`. Adicione ao plano, nas fases certas, todos os itens P1 e P2 marcados com ✅ ou 🔧. Não implemente os itens ❌. Os itens P3 ficam para a fase de extras. Para cada item 🔧, siga a coluna "Como fazer". Antes de começar, me mostre em que fase cada item novo entrou e o que muda no `CLAUDE.md`.
