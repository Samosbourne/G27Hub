# Teste da Fase 6 — Detector inteligente + base de compatibilidade

O detector vem **desligado**. Ligue em: engrenagem do topo → **DETECTOR INTELIGENTE DE JOGOS**.
A troca automática de perfil (Fase 5a) funciona com ele ligado ou desligado.

Já validado pelo Claude Code (21/10/2026):
- 57 testes automáticos (base embutida sem .exe repetido, busca por nome, regras de aviso, heurística XInput, bibliotecas da Steam, 👍/👎 salvos);
- busca de jogos instalados no seu PC: achou o **BeamNG.drive** (Steam) em 0,35 s;
- um "Forza Horizon 5" falso aberto com o detector ligado gerou: compatível (lista oficial), lembrete do Steam Input (Steam aberta) e rotação sugerida 540° × app em 900°; fechado com menos de 2 min, sem pergunta.

## 1. Jogos instalados
- [ ] DESKTOP: Padrão (topo) → **PROCURAR JOGOS INSTALADOS** → aparece a janela com os jogos da base que estão no PC (Steam, Epic, Xbox).
- [ ] **CRIAR PERFIL** num jogo: o perfil vem com o nome certo, todos os .exe conhecidos e a rotação sugerida já marcada em "configurações especiais".
- [ ] **+ ADICIONAR JOGO** com o .exe de um jogo da base também preenche assim.

## 2. Aviso ao abrir um jogo (detector ligado)
- [ ] Abra um jogo da base (ex.: BeamNG, Forza, AC): em ~2 s aparece o aviso com a compatibilidade e as dicas; com o app minimizado, vem também a notificação do Windows.
- [ ] Com um controle de Xbox ligado, num Forza: aviso para desconectar (Device 1).
- [ ] **NÃO AVISAR PARA ESTE JOGO**: na próxima vez não aparece.

## 3. Depois de jogar
- [ ] Jogue 2 min ou mais e feche: aparece "O volante funcionou bem neste jogo?" com 👍 / 👎. Um 👎 faz o próximo aviso lembrar disso.

## 4. Jogo fora da base
- [ ] Um jogo que você adicionou e não está na base: ~10 s depois de abrir, se ele só usa controle de Xbox, aparece o aviso "parece aceitar apenas controle de Xbox".

## 4b. BeamNG (corrigido em 21/10)
- O mapa do G27 que vem no BeamNG foi feito para o LGS (pedais e botões em outra ordem). O app já gravou a correção no
  seu `c29b046d.diff` (backup: `c29b046d.diff.antes-do-g27hub`, na mesma pasta). Botão CORRIGIR/DESFAZER na janela de jogos encontrados.
- [ ] Feche e abra o BeamNG: acelerador, freio, embreagem e marchas 1–6/ré certos.
- [ ] Force feedback da direção: puxa para o centro? Se empurrar para o lado, desmarque "inverter força" do volante no BeamNG.

## 5. Desligado
- [ ] Com o detector desligado, abrir jogos não mostra avisos (só a troca de perfil).

## Aceite
Abrir o Forza mostra "Compatível — Modo Padrão"; um jogo `precisa-compat-total` mostra a recomendação sem ativar nada; com um controle de Xbox conectado, aparece o aviso do Device 1.
