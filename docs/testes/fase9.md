# Teste da Fase 9: assistente, calibração, dicas e README

Já validado pelo Claude Code (22/10/2026):
- calibração: gravar uma faixa no freio fez o DirectInput (como um jogo lê) passar a ler o pedal solto como meio;
  remover voltou ao normal. Instâncias dos eixos medidas no seu PC: X 0, Y 1 (embreagem), Z 2 (acelerador), Rz 5 (freio);
- seu acelerador solto às vezes lê 63.700 de 65.535 (não volta totalmente a zero): a calibração resolve;
- assistente desenhado (prints) e 94 testes automáticos.

## 1. Assistente da primeira abertura
- [ ] Ao abrir o app aparece "Bem-vindo ao G27 Hub". **COMEÇAR**: ele confere drivers antigos da Logitech, o volante,
      o modo nativo, ativa o force feedback, mostra dicas de Steam Input e Integridade de Memória, gira o volante um
      pouco e acende os LEDs. **PRONTO** fecha e não aparece mais.
- [ ] Menu ☰ → **Assistente de configuração** abre de novo quando quiser.

## 2. Calibrar pedais
- [ ] Aba Pedais → **CALIBRAR PEDAIS** → pés fora dos pedais → PRÓXIMO → pise cada pedal até o fundo → SALVAR.
- [ ] Depois: as barras da aba Pedais vão de 0 a 100% certinho; no `joy.cpl` também (o Windows aplica para todos os jogos).
- [ ] **REMOVER CALIBRAÇÃO** volta ao de fábrica.

## 3. Dicas
- [ ] Botão ⓘ no canto inferior direito de cada aba: abre as dicas daquela aba.

## 4. Idioma (pedido do John)
Validado: telas geradas em inglês sem nenhum texto faltando; 560 textos traduzidos; 96 testes.
- [ ] Botão do **globo** no topo (ao lado do perfil) → **English** → Sim: o app reabre em inglês.
- [ ] Navegue pelas abas, pelo menu ☰, pela engrenagem e pelo assistente: tudo em inglês.
- [ ] Globo → **Português**: volta. (O console de diagnóstico continua em português: é o log técnico.)

## 5. Instalador
`installer\out\G27Hub-Setup-1.0.0.exe` (50,6 MB, gerado por `installer\build.ps1`).
Validado pelo Claude Code: instalação silenciosa sem erro (pasta, atalho no Iniciar, "G27 Hub 1.0.0" em Aplicativos,
DLLs de força x64/x86 e plugin do ETS2 dentro); o app instalado abre sozinho com o .NET embutido; reinstalar por cima
(atualização) funciona. Já está instalado no seu PC (sem "iniciar com o Windows", para não brigar com a versão de teste).
- [x] Abra pelo menu Iniciar → **G27 Hub** (John, 22/10: instalou e funcionou).
- [ ] Rode o instalador de novo em inglês: as telas do instalador e o app aparecem em inglês.
- [x] **Desinstalar** (John, 22/10: funcionou) (Configurações → Aplicativos → G27 Hub): remove o app e desfaz o que ele mudou: registro do
      force feedback, iniciar com o Windows, calibração, mod do BeamNG, plugin do ETS2 e (se você disser Sim) vJoy/HidHide.
      Atenção: depois disso o force feedback só volta instalando de novo (o assistente reativa).
      Não testei a desinstalação aqui porque ela desfaria a sua configuração.

## 6. Créditos (pedido do John)
- [ ] Menu ☰ → Sobre: "Idealizado e desenvolvido por John Salim / Samosbourne, junto com IA (Claude, da Anthropic)". Também no README e no instalador.

## 7. Testar volante (pedido do John, como o LGS)
Validado pelo Claude Code no volante (`g27cli wheeltest`, 22/10): esquerda devagar até −441°, direita devagar até +442°,
centro, de uma vez +444°, de uma vez −445°, centro (0,7° do meio); 13 s no total; a mola volta no fim.
- [ ] Aba Volante → **TESTAR VOLANTE** (solte o volante): faz a sequência acima; o texto embaixo mostra cada passo.
- [ ] Clicar de novo (**PARAR TESTE**) interrompe na hora e solta o volante.
- [ ] Com um jogo usando o force feedback aberto, o teste não roda (aparece um aviso).
