# Children of Morta 4P

Mod para **Children of Morta** (Steam) que aumenta o co-op online de 2 para **atÃ© 4 jogadores**.

Status: **v0.9.4, experimental**. A seleÃ§Ã£o de personagem e o HUD jÃ¡ foram testados no solo com a prÃ©via (F9), mas uma partida real com 3 ou 4 jogadores ainda nÃ£o foi testada. FaÃ§a backup dos seus saves.

## InstalaÃ§Ã£o

FaÃ§a isto **em todos os PCs** que vÃ£o jogar juntos.

### Jeito fÃ¡cil: instalador

1. Feche o jogo.
2. Baixe o `Morta4P-Installer.exe` em [Releases](../../releases). Basta baixar uma vez: ele sempre busca a versÃ£o mais recente do mod no GitHub.
3. Execute. Ele procura o Children of Morta nas bibliotecas da Steam.
4. Escolha uma opÃ§Ã£o:
   - **[1] Instalar:** instala o BepInEx (se ainda nÃ£o tiver) e a versÃ£o mais recente do mod.
   - **[2] Atualizar mod:** baixa e instala a versÃ£o mais recente do mod.
   - **[3] Desinstalar:** remove o mod e, se vocÃª quiser, o BepInEx tambÃ©m.

ObservaÃ§Ãµes:
- O Windows pode mostrar "O Windows protegeu o computador", porque o instalador nÃ£o tem assinatura digital. Clique em **Mais informaÃ§Ãµes â†’ Executar assim mesmo**.
- Sem internet, ele instala a versÃ£o do mod que vem dentro do prÃ³prio instalador.
- Se ele nÃ£o achar o jogo, cole o caminho da pasta do jogo quando ele pedir.
- Se o jogo estiver numa pasta protegida, ele pede permissÃ£o de administrador.

### InstalaÃ§Ã£o manual

#### 1. Instalar o BepInEx

1. Baixe o **BepInEx 5.4.23.x para Windows x64**, o arquivo `BepInEx_win_x64_5.4.23.x.zip`, na [pÃ¡gina de releases do BepInEx](https://github.com/BepInEx/BepInEx/releases).
2. Abra a pasta do jogo: na Steam, clique com o botÃ£o direito em Children of Morta â†’ **Gerenciar** â†’ **Navegar pelos arquivos locais**. Normalmente Ã© `...\steamapps\common\ChildrenOfMorta`.
3. Extraia o conteÃºdo do zip **dentro dessa pasta**, ao lado do `ChildrenOfMorta.exe`. Devem aparecer `winhttp.dll`, `doorstop_config.ini` e a pasta `BepInEx`.
4. Abra o jogo uma vez e feche. Isso cria as pastas `BepInEx\plugins` e `BepInEx\config`.

#### 2. Instalar o mod

1. Baixe o `Morta4P.dll` da Ãºltima versÃ£o em [Releases](../../releases).
2. Crie a pasta `BepInEx\plugins\Morta4P\` dentro da pasta do jogo e coloque o `Morta4P.dll` nela.
3. Abra o jogo pela Steam.

Para conferir se carregou, abra `BepInEx\LogOutput.log`. Deve aparecer a linha `Morta4P 0.9.4 loaded. MaxPlayers=4`.

### Jogar

1. O host entra no jogo e, na casa, convida os amigos pelo convite da Steam, **um de cada vez**.
2. Todos escolhem personagem. A partida sÃ³ comeÃ§a quando os 4 tiverem escolhido.
3. Todos precisam da **mesma versÃ£o do jogo e do mod**, e do mesmo `MaxPlayers` (veja abaixo). Quem estiver sem o mod pode ver status errados ou ter erro no lobby.

> **Personagens desbloqueados:** cada jogador precisa de um personagem diferente; dois jogadores nÃ£o podem usar o mesmo. A famÃ­lia tem atÃ© 8 personagens (John, Mark, Kevin, Linda, Lucy, Joey, Apon e Bec), mas eles sÃ£o desbloqueados ao longo da campanha.
>
> - Com 3 ou mais jogadores online, o mod libera **John, Linda, Mark e Kevin** na seleÃ§Ã£o, mesmo que o save ainda nÃ£o tenha desbloqueado. O save nÃ£o Ã© alterado, e a cena da histÃ³ria que desbloqueia cada um continua acontecendo normalmente. Para desligar, use a opÃ§Ã£o `UnlockStarterCharacters`.
> - Os outros personagens (Lucy, Joey, Apon e Bec) sÃ³ aparecem se jÃ¡ estiverem desbloqueados no save.
> - Em algumas passagens da histÃ³ria, parte da famÃ­lia vira companheiro controlado pelo jogo e sai da lista de escolha. Para ter mais opÃ§Ãµes, prefira um save mais avanÃ§ado na campanha.

### Desinstalar

- Para remover sÃ³ o mod: apague `BepInEx\plugins\Morta4P`.
- Para tirar tudo: apague tambÃ©m `winhttp.dll`, `doorstop_config.ini`, `.doorstop_version`, `changelog.txt` e a pasta `BepInEx`.

## ConfiguraÃ§Ã£o

O arquivo `BepInEx\config\morta4p.cfg` Ã© criado na primeira execuÃ§Ã£o. Se existir o `com.william.morta4p.cfg` de versÃµes anteriores, ele Ã© renomeado automaticamente e os valores sÃ£o mantidos.

| SeÃ§Ã£o | OpÃ§Ã£o | PadrÃ£o | O que faz |
|---|---|---|---|
| `General` | `MaxPlayers` | `4` | total de jogadores online, contando o host (2 a 4). Tem que ser igual em todos os PCs |
| `CharacterSelect` | `PanelScale` | `0.5` | tamanho dos painÃ©is de personagem quando hÃ¡ mais de 2 jogadores |
| `Gameplay` | `XPSplitMax` | `2` | a XP de cada inimigo Ã© dividida pelo nÃºmero de jogadores, atÃ© este limite, e cada um recebe a parte. Com `2`, 10 de XP dÃ¡ 5 para cada um dos 4. `0` divide por todos (original) |
| `CharacterSelect` | `UnlockStarterCharacters` | `true` | com 3 ou mais jogadores online, libera John, Linda, Mark e Kevin na seleÃ§Ã£o mesmo que o save nÃ£o tenha desbloqueado. O save nÃ£o Ã© alterado |
| `CharacterSelect` | `PanelSpacing` | `4` | espaÃ§o entre o painel de cima e o de baixo |
| `HUD` | `Spacing` | `14` | espaÃ§o entre os HUDs dos parceiros |
| `Lobby` | `Spacing` | `4` | espaÃ§o extra entre os jogadores no widget do lobby (menu de pausa) |
| `HUD` | `StackDirection` | `Auto` | para onde os HUDs extras empilham: `Auto`, `Up`, `Down`, `Left` ou `Right` |
| `Debug` | `PreviewKey` | `F9` | prÃ©via solo do layout para 4 jogadores |
| `Debug` | `ReloadKey` | `F10` | recarrega o arquivo de config sem fechar o jogo |

Com o **F9**, na seleÃ§Ã£o de personagem aparecem os 4 painÃ©is e as marcaÃ§Ãµes P3/P4 em dois botÃµes. Dentro de uma partida aparecem 3 HUDs de parceiro, todos com o seu personagem. Com o menu de pausa aberto, o widget do lobby mostra 3 jogadores. Isso serve para ajustar o layout sem precisar de outros jogadores.

## O que o mod muda

- **Lobby:** a Steam aceita 4 membros, e o jogo aceita atÃ© 3 convidados em vez de 1 (`CheckLobbyIsFull` e `CheckIsJoiningPossible`).
- **SeleÃ§Ã£o de personagem:** cada convidado usa o nÃºmero de jogador que o host atribuiu, em vez de ser sempre P2. O host pode continuar convidando enquanto houver vaga, e a partida espera todos escolherem. Com 3 ou mais jogadores, cada lado da tela mostra dois painÃ©is: P1 em cima e P3 embaixo Ã  esquerda, P2 em cima e P4 embaixo Ã  direita.
- **XP:** a XP Ã© dividida por 2 e cada jogador recebe a parte, mesmo com 3 ou 4 (10 de XP dÃ¡ 5 para cada um). No jogo original, com 4, cada um ganharia sÃ³ 2,5.
- **Personagens iniciais:** cada jogador precisa de um personagem diferente. Com 3 ou mais jogadores online, John, Linda, Mark e Kevin ficam liberados na seleÃ§Ã£o mesmo num save do comeÃ§o da campanha. O desbloqueio da histÃ³ria continua acontecendo normalmente.
- **HUD:** cada jogador remoto ganha o prÃ³prio HUD de parceiro. No jogo original existia sÃ³ um.
- **Status do lobby:** "Pronto", "Escolhendo personagem", "Na masmorra" e os outros status aparecem no HUD do jogador certo. O host repassa o status de cada convidado para os demais, entÃ£o todo mundo vÃª o status de todo mundo.
- **Widget do lobby (menu de pausa):** mostra todos os jogadores remotos, um embaixo do outro, cada um com status e submenu de perfil.
- **CÃ¢mera:** sem mudanÃ§a. No online, cada PC jÃ¡ segue sÃ³ o prÃ³prio jogador. O tremor de cÃ¢mera tambÃ©m chega para P3 e P4.
- **Revive:** todos os convidados veem o aviso de "reviver" e a seta de quem caiu, nÃ£o sÃ³ o host. Se o Ãºltimo jogador em pÃ© sair da partida com o resto caÃ­do, a partida termina em vez de travar.
- **Itens:** relÃ­quias, consumÃ­veis e runas que o jogo dava para "os dois jogadores" agora valem para os quatro. A loja de cura cura cada jogador em pÃ© uma vez, em vez de curar o comprador vÃ¡rias vezes.
- **PosiÃ§Ãµes:** inÃ­cios de fase, passagens e esperas de cutscene que sÃ³ tinham 2 posiÃ§Ãµes ganham posiÃ§Ãµes extras ao lado, em vez de fechar o jogo.
- **Casa:** cada convidado recebe do host sÃ³ os prÃ³prios dados de cutscene e animaÃ§Ãµes da casa, sem duplicar.

## LimitaÃ§Ãµes conhecidas

- **Cutscenes:** as cenas foram animadas para 2 jogadores. P3 e P4 nÃ£o tÃªm papel nelas e podem ficar parados ou escondidos. Fora da cena, quem nÃ£o Ã© o "protagonista" pode aparecer no mesmo ponto que os outros.
- **Dificuldade:** o jogo nÃ£o aumenta inimigos nem vida por nÃºmero de jogadores. Com 4, a dificuldade Ã© a mesma do co-op de 2. No Endless, o co-op sÃ³ liga um ajuste fixo.
- **Companheiros controlados pelo jogo** (passagens, chefe final) usam o nÃºmero de jogador P4 quando hÃ¡ 4 jogadores. O jogo original fecharia nesse caso.
- **Fim de fase do chefe:** a cutscene de saÃ­da verifica sÃ³ o jogador do host. Isso jÃ¡ era assim no jogo original.

## Reportar problema

Se algo quebrar, abra uma issue anexando os dois logs do host e do jogador que teve o problema:

- `BepInEx\LogOutput.log`, na pasta do jogo
- `%USERPROFILE%\AppData\LocalLow\Dead Mage\Children of Morta\Player.log`

## Compilar

Ã‰ preciso ter o .NET SDK 8 e o BepInEx instalado na pasta do jogo.

```bash
dotnet build src/Morta4P/Morta4P.csproj -c Release -p:GamePath="C:\caminho\para\ChildrenOfMorta"
```

`GamePath` aponta para a pasta do jogo, onde ficam as DLLs de referÃªncia. Depois do build, o DLL Ã© copiado automaticamente para `BepInEx\plugins\Morta4P`.

Para gerar o instalador (compila o mod e embute o DLL e o zip do BepInEx, baixado na primeira vez):

```bash
dotnet build src/Installer/Installer.csproj -c Release -p:GamePath="C:\caminho\para\ChildrenOfMorta"
```

O executÃ¡vel fica em `src/Installer/bin/Release/net472/Morta4P-Installer.exe`.
