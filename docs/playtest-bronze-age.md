# Bronze Age playtest (pieces 1 to 4)

One sitting, about 20 minutes. Everything below runs on the `bronze` preset: real rules (gathering,
blueprints, research), but no match ever starts. Both teams start in the Bronze Age with its basic techs
researched and 60 Bronze Age points. Each player has bronze tools, a bow and 12 arrows. Each team has a
stocked depot a few blocks from its spawn: planks, logs, stone, clay, bronze, iron ore, charcoal, and copper
and tin ingots.

```sh
./build.sh server --port=7777 --preset=bronze --dev
./build.sh client
./build.sh client
```

Put one client on Red and one on Blue. `--dev` allows noclip (F), which saves walking. Tick each box, and
note anything that feels off: costs, speeds, distances, wording.

## 1. Health, death and the bow (piece 1)

- [ ] `Health 100 / 100` bottom left. Select the bow (the last hotbar slot, key 0); its count shows 12.
- [ ] Left click shoots an arrow in an arc. Hit the other team's player four times:
  - they see `Hit! -25 health` each time, and you see `Your arrow hit!`;
  - on the fourth hit their screen goes dark red with a 5 s countdown.
- [ ] While dead they can't move or act and are invisible to you. They come back at their island at full
  health.
- [ ] Where they died there's a small brown dropped backpack. Either team can open it with E and take things
  out; it disappears once empty.

## 2. Building by blueprint (piece 2)

- [ ] Open your depot with E and take 6 planks.
- [ ] Select the crate and right click: a see-through blueprint goes down for free.
  - The prompt says what it needs.
  - E on it delivers your planks, and it becomes a crate.
- [ ] Place a depot blueprint (12 planks, 20 stone). Deliver part of the cost:
  - a solid part fills up from the bottom and the label shows the percentage;
  - E on it delivers instead of opening storage.
- [ ] Take a part-built blueprint down (hold left click): you get back what was delivered.
- [ ] Is hauling from a depot to a blueprint fun, or a chore? How far apart did things end up?

## 3. The dock and the galley (piece 3)

- [ ] R opens research. Research Shipbuilding (20 points; needs Bronze tools, already researched).
- [ ] Take the dock to the shore. The preview turns green only when:
  - you look out to sea from the beach;
  - the water in front of its far end is open.
  Place it, then haul 20 planks to it.
- [ ] Aiming at the built dock shows the galley's needs: 30 planks, 6 logs, 2 bronze ingots. That's two
  trips with a 40-item backpack.
- [ ] The last delivery launches the galley in front of the dock.
- [ ] Board it with E:
  - W rows, S backs water, A/D turn;
  - it stops against land;
  - E puts you back on the dock or the shore.
- [ ] From the other client: the galley moves smoothly, and Blue can't board Red's galley.
- [ ] How does sailing feel? Speed (top 7 blocks/s), turning, the camera, getting on and off.

## 4. The blast furnace and the Iron Age (piece 4)

- [ ] The research window's goal line reads `To reach the Iron Age: research Blast furnace, then build a
  blast furnace`.
- [ ] Research Blast furnace (35 points). You stay in the Bronze Age, and the goal line now says only `build
  a blast furnace`.
- [ ] Place the blast furnace (2 x 3 x 2) and haul 30 stone, 10 clay and 4 bronze ingots to it. The last
  delivery starts the Iron Age: a banner shows and the era line top right changes.
- [ ] Iron ore (from the depot) has a Smelt button in the blast furnace but not in an ordinary furnace.
- [ ] With charcoal as fuel, one ore makes one iron ingot in 8 s. The era line shows Iron Age points going
  up (2 per ingot).
- [ ] Research Smithing (20 Iron Age points, already given). The workbench then makes an iron pickaxe and
  an iron axe from 3 ingots and 2 planks each, worth 4 points each.

## Calls for you

These alpha defaults were picked to keep moving. Each is noted in `docs/decisions.md`.

- **Blueprints:**
  - only backpacks deliver, not team storage;
  - deliveries can't be taken back out;
  - a team can put down any number of blueprints.
- **Galley:**
  - one player sails it, with no rowers, passengers or cargo yet (your notes say a navigator and two
    rowers);
  - ships take no damage and pass through each other;
  - the dock has no "pick a ship" menu until there's a second ship.
- **Blast furnace:** a single structure for now, not a multiblock.
- **Costs and points:** all first guesses. They're in `data/structures.json`, `data/techs.json` and
  `data/items.json`.
- **Still to come:**
  - dock upgrades by age;
  - points for damaging enemy structures, players and ships;
  - the rest of the Iron Age: reinforced structures, ballista and catapult, war galley, glass, telescope,
    upgraded furnace.

## If you have extra time

- `--preset=practice` is creative: the dock and galley cost nothing, which is good for just sailing around.
- `--preset=quick` is a real match from the Stone Age, with nothing given.
