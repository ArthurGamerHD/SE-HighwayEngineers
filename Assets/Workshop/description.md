[h1]Highway Engineer - (Alpha)[/h1]

Road planning and construction for planets, straight from an LCD

Pick the "Highway Engineer" script on any LCD panel, look at it and click: place nodes on the planet map, shape the curves, check the price, and let the contractors do the digging

Requires [url=https://steamcommunity.com/sharedfiles/filedetails/?id=3788827631]Arthur's Procedural Cubemap Api[/url] (the roads are baked into the planet itself, so no grids, no lag, and they survive anything a voxel can survive)

Recommended [url=https://steamcommunity.com/sharedfiles/filedetails/?id=3735691793]HD LCD Displays[/url] for better resolution on vanilla lcd screens

[hr][/hr]
[h2]Features:[/h2]

[h3]Road Planner[/h3]
- Interactive planet map on any LCD (Satellite, Terrain, Materials and Biomes layers)
- Click to place nodes, drag to move them, drag the tangent handles to shape the curves
- Auto Road: finds a path between two points, weighting distance, inclination and earthwork to your taste
- Road settings: material, width, depth, max slope, embankment angle, smoothing distance, follow terrain or keep a steady grade
- Ctrl+Z / Ctrl+Y to undo and redo while looking at the panel

[h3]Bridges[/h3]
- Valleys and canyons are detected automatically and bridged
- Two designs: Beam (straight pillars) or Arch (one rib springing from the canyon walls), by default whichever needs less concrete
- Approach ramps on both ends, meeting the road
- Automatic mode did not see your gap? [b]Hold a node and scroll the mouse wheel[/b] to lift it (and force a bridge), or use [b]+[/b] / [b]-[/b] on the selected node

[h3]Contracts[/h3]
- The server quotes every road before you commit: earthwork in Space Credits, and materials as items (Gravel, Steel Rebar, and Concrete Mix when the [url=https://steamcommunity.com/sharedfiles/filedetails/?id=396679430]Concrete Tool[/url] is loaded)
- Itemized invoice, with the faction that takes the job (nearest trade station)
- Deliver the materials to the Road Logistic Depot and the road gets built
- Built roads and bridges are kept in a shared road library, synced to every player

[h3]Maintenance[/h3]
- Bulldoze: remove roads and bridges (partial refund within the first hour, a fee after that)
- Repair: repave with a new material, fill holes dug into the road, rebuild its bridges, and re-paint the guide lines

[h3]Guide Lines[/h3]
- Double yellow centre line and white edge lines painted along the road (decals only, the terrain is untouched)
- Off by default: turn "Guide Lines" on in the road settings, or "Re-Paint Guide Lines" in the repair dialog

[h3]Other Mods[/h3]
- Built-in LCD touch support (look and click), shared with [url=https://steamcommunity.com/sharedfiles/filedetails/?id=TODO_LCD_MOD_ID]Arthur's Lcd Mod[/url]: whichever loads first serves the other
- [url=https://steamcommunity.com/sharedfiles/filedetails/?id=2200451495]Water Mod[/url]: water shown on the map
- [url=https://steamcommunity.com/sharedfiles/filedetails/?id=396679430]Concrete Tool[/url]: bridges are made of its Concrete, paid in Concrete Mix

[hr][/hr]
[h2]Server Admins[/h2]

Prices are in [b]HighwayEngineerCosts.xml[/b] and limits (quotes, open contracts per player, segments per road) in [b]HighwayEngineerServer.xml[/b], both in the world storage folder of the mod

Chat commands:
[b]/road free[/b] : roads without cost, on or off (admin)
[b]/road forcecomplete[/b] : completes every contract still waiting for delivery or payment (admin)
[b]/road debugDropoff[/b] : shows the delivery boxes of your contracts

[hr][/hr]
[h2]Known Issues[/h2]
- The approach ramp always leaves the bridge straight, so a road that turns right after a bridge can meet the ramp off-center
- Roads and ramps can't be shaped to the exact same width
- The planet map is drawn with LCD sprites, so on small screens it is a bit pixelated (it's a feature, retro look)

[hr][/hr]

Localization:
en-us: Native
cs-CZ: Strojově přeloženo
es, es-419: Traducido por máquina
fi: Koneella käännetty
hu-HU: Géppel fordítva
ja-JP: 機械翻訳済み
no, da: Maskinoversatt
tr-TR: Makineyle çevrildi
fr: Traduit par machine
is-IS: Vélþýtt
lv: Mašīntulkots
pl-PL: Przetłumaczone maszynowo
ro: Tradus de mașină
sk-SK: Strojovo preložené
uk: Перекладено машиною
ca-AD: Traduït per màquina
de: Maschinell übersetzt
et-EE: Masintõlgitud
hr-HR: Strojno prevedeno
it: Tradotto da macchina
nl: Machinaal vertaald
pt-BR: Traduzido por máquina
ru: Переведено машиной
sv: Maskinöversatt
zh: 机器翻译

[hr][/hr]

[h2]WARNING, this is in ALPHA, so bugs are expected, please report it below in the comments[/h2]
Roads are planet modifications, removing the mod (or the Cubemap Api) won't remove the roads already built, but the road library, contracts and bridge bulldozing go with it

[hr][/hr]

Thanks for:

[url=https://steamcommunity.com/id/hunterdigi]@Digi[/url] - Because if at some point you made a mod for this game, you need to thank this guy (and for the [url=https://steamcommunity.com/sharedfiles/filedetails/?id=396679430]Concrete Tool[/url])

[url=https://steamcommunity.com/id/mal-ware]@Malware[/url] - [url=https://github.com/malforge/mdk2]Mdk²[/url] is still amazing

@Jakaria - For the [url=https://steamcommunity.com/sharedfiles/filedetails/?id=2200451495]Water Mod[/url] API
