"""Author size-specific vector pictograms and promote unchanged native portraits.

Python/Pillow are offline dependencies only. No source parsing enters the player.
"""
import argparse
import hashlib
import importlib.util
import json
import math
from pathlib import Path
import re
import shutil

from PIL import Image, ImageDraw, ImageFont, ImageOps

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "assets/source/ui/directorate/artwork"
SIZES = (16, 24, 32, 48, 64)
spec = importlib.util.spec_from_file_location("skin_authoring", ROOT / "build/Author-DirectorateUiSkin.py")
skin = importlib.util.module_from_spec(spec)
spec.loader.exec_module(skin)
PALETTE = skin.palette()
INK = PALETTE["TextPrimary"]
EDGE = (*PALETTE["BackgroundDeep"][:3], 255)


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


class Glyph:
    def __init__(self, size, drawing=None, factor=1, offset=(0, 0)):
        self.size = size
        self.drawing = drawing or skin.Drawing(size, size)
        self.factor, self.offset = factor, offset

    def points(self, points):
        return [(round((x * self.factor + self.offset[0]) * self.size / 64),
                 round((y * self.factor + self.offset[1]) * self.size / 64)) for x, y in points]

    def line(self, points, color=INK, width=3):
        points = self.points(points)
        self.drawing.line(points, EDGE, max(1, round((width + 3) * self.factor * self.size / 64)))
        self.drawing.line(points, color, max(1, round(width * self.factor * self.size / 64)))

    def polygon(self, points, color=INK):
        points = self.points(points)
        self.drawing.polygon(points, color)
        self.drawing.line(points + [points[0]], EDGE, max(1, round(2 * self.factor * self.size / 64)))

    def rect(self, x, y, width, height, color=INK):
        self.polygon([(x, y), (x + width, y), (x + width, y + height), (x, y + height)], color)

    def circle(self, x, y, radius, color=INK, width=3):
        self.line([(x + math.cos(i * math.tau / 40) * radius, y + math.sin(i * math.tau / 40) * radius) for i in range(41)], color, width)

    def child(self, factor=.48, offset=(30, 30)):
        return Glyph(self.size, self.drawing, self.factor * factor,
                     (self.offset[0] + offset[0] * self.factor, self.offset[1] + offset[1] * self.factor))


def arrow(g, direction="right"):
    points = [(9, 26), (36, 26), (36, 13), (56, 32), (36, 51), (36, 38), (9, 38)]
    if direction == "left": points = [(64-x, y) for x, y in points]
    if direction == "up": points = [(y, 64-x) for x, y in points]
    if direction == "down": points = [(y, x) for x, y in points]
    g.polygon(points)


def draw(g, name):
    n = name.split(".")[-1]
    if name.startswith("cursor."):
        g.polygon([(8, 6), (8, 46), (18, 36), (26, 52), (34, 48), (26, 32), (40, 30)])
        if n != "default": draw(g.child(), "cursor_badge." + n)
        return
    if n in ("move", "right", "forward", "increase", "next_item"): arrow(g)
    elif n in ("left", "back", "decrease"): arrow(g, "left")
    elif n in ("up", "scroll_up", "expand_priority"): arrow(g, "up")
    elif n in ("down", "scroll_down"): arrow(g, "down")
    elif n == "ferrous_ore":
        g.polygon([(8, 42), (14, 22), (30, 10), (49, 18), (56, 39), (41, 52), (22, 54)])
        g.line([(14, 22), (34, 28), (49, 18)], EDGE, 2); g.line([(34, 28), (41, 52)], EDGE, 2)
    elif n == "volatiles":
        g.polygon([(32, 7), (15, 29), (11, 42), (19, 53), (42, 53), (53, 42), (48, 27)])
        g.circle(31, 37, 8, EDGE, 3)
    elif n in ("silicates", "resource"):
        g.polygon([(31, 7), (51, 23), (45, 49), (26, 56), (12, 39), (16, 17)])
        g.line([(31, 7), (34, 33), (26, 56)], EDGE, 2); g.line([(16, 17), (34, 33), (51, 23)], EDGE, 2)
    elif n == "rare_elements":
        for x, y, r in [(21, 25, 14), (43, 25, 12), (33, 45, 12)]: g.polygon([(x, y-r), (x+r, y), (x, y+r), (x-r, y)])
    elif n == "steel":
        g.polygon([(9, 35), (23, 18), (48, 18), (56, 37), (42, 49), (14, 49)])
        g.line([(9, 35), (40, 35), (48, 18)], EDGE, 2); g.line([(40, 35), (42, 49)], EDGE, 2)
    elif n in ("fuel",):
        g.rect(16, 17, 31, 38); g.rect(24, 9, 16, 9); g.line([(21, 26), (42, 47)], EDGE, 3); g.line([(42, 26), (21, 47)], EDGE, 3)
    elif n == "electronics":
        g.rect(20, 20, 24, 24)
        for p in (23, 32, 41):
            for points in [[(p, 9), (p, 19)], [(p, 45), (p, 55)], [(9, p), (19, p)], [(45, p), (55, p)]]: g.line(points)
        g.rect(27, 27, 10, 10, EDGE)
    elif n == "ammunition":
        for x in (12, 27, 42):
            g.polygon([(x, 53), (x, 25), (x+5, 12), (x+10, 25), (x+10, 53)])
            g.line([(x, 45), (x+10, 45)], EDGE, 2)
    elif n in ("infantry", "units"):
        g.polygon([(12, 54), (17, 40), (25, 35), (39, 35), (47, 40), (52, 54)])
        g.polygon([(20, 29), (19, 19), (25, 10), (39, 10), (46, 20), (44, 29)])
        g.line([(18, 27), (47, 27)], EDGE, 3)
    elif n in ("reconnaissance", "sensors"):
        g.line([(7, 32), (20, 19), (44, 19), (57, 32), (44, 45), (20, 45), (7, 32)])
        g.circle(32, 32, 8)
    elif n in ("armor", "unit_production"):
        g.rect(10, 24, 12, 31); g.rect(42, 24, 12, 31); g.rect(20, 28, 24, 22); g.rect(26, 21, 12, 18); g.line([(32, 22), (32, 7)], width=5)
        if n == "unit_production": g.line([(45, 12), (57, 12)]); g.line([(51, 6), (51, 18)])
    elif n in ("artillery", "fire_mission"):
        g.circle(19, 47, 7); g.circle(41, 47, 7); g.line([(14, 39), (45, 39)], width=6); g.line([(27, 35), (51, 10)], width=7)
        if n == "fire_mission": g.line([(8, 20), (15, 14), (24, 12)], width=2)
    elif n in ("logistics", "truck"):
        g.rect(8, 22, 29, 23); g.polygon([(38, 28), (47, 28), (57, 38), (57, 45), (38, 45)]); g.circle(19, 48, 6); g.circle(47, 48, 6)
    elif n in ("repair", "maintenance"):
        g.polygon([(12, 50), (18, 56), (42, 32), (50, 31), (56, 21), (52, 12), (43, 22), (36, 16), (44, 7), (32, 10), (27, 20), (30, 29)])
    elif n in ("command", "focus_base", "home"):
        g.polygon([(9, 29), (32, 9), (55, 29), (48, 29), (48, 54), (16, 54), (16, 29)])
        g.rect(28, 36, 8, 18, EDGE)
    elif n == "extraction":
        g.line([(12, 53), (43, 14)], width=6); g.line([(14, 17), (29, 9), (44, 12), (55, 23)], width=6)
    elif n in ("processing", "process", "settings"):
        for i in range(8):
            a=i*math.tau/8; g.line([(32+math.cos(a)*16,32+math.sin(a)*16),(32+math.cos(a)*24,32+math.sin(a)*24)], width=7)
        g.circle(32, 32, 17, width=7); g.circle(32, 32, 6, EDGE, 3)
    elif n in ("factory", "production_pause"):
        g.polygon([(8, 54), (8, 25), (23, 15), (23, 25), (38, 15), (38, 30), (51, 30), (51, 54)])
        g.rect(45, 8, 9, 23)
        for x in (17, 30, 43): g.rect(x, 39, 5, 7, EDGE)
        if n == "production_pause": draw(g.child(.5, (32, 32)), "utility.pause")
    elif n in ("storage", "stock", "supply", "depot", "automatic_resupply"):
        g.rect(12, 18, 40, 36); g.line([(12, 28), (52, 28)], EDGE, 2); g.line([(32, 18), (32, 54)], EDGE, 3)
        if n in ("supply", "depot"): g.line([(23, 41), (41, 41)]); g.line([(32, 33), (32, 49)])
        if n == "stock": g.line([(8, 12), (40, 12)], width=3)
        if n == "automatic_resupply": draw(g.child(.48, (32, 30)), "utility.rotate")
    elif n == "power": g.polygon([(34, 6), (12, 36), (29, 36), (24, 58), (53, 25), (35, 25)])
    elif n in ("attack", "target"):
        g.circle(32, 32, 15)
        for pts in [[(32, 6),(32,22)],[(32,42),(32,58)],[(6,32),(22,32)],[(42,32),(58,32)]]: g.line(pts)
    elif n == "attack_move":
        draw(g.child(.7, (4, 0)), "command.attack"); g.line([(29, 51), (54, 51), (46, 43)]); g.line([(54, 51), (46, 59)])
    elif n == "stop": g.polygon([(21, 8), (43, 8), (56, 21), (56, 43), (43, 56), (21, 56), (8, 43), (8, 21)]); g.rect(23,23,18,18,EDGE)
    elif n in ("hold", "guard"):
        g.polygon([(12, 13), (32, 7), (52, 13), (49, 39), (32, 56), (15, 39)]); g.rect(28,18,8,23,EDGE)
    elif n in ("patrol", "rotate"):
        g.line([(12, 26), (16, 15), (30, 10), (45, 15), (51, 26)]); g.polygon([(43,24),(56,26),(53,13)])
        g.line([(52, 38), (48, 49), (34, 54), (19, 49), (13, 38)]); g.polygon([(21,40),(8,38),(11,51)])
    elif n in ("build",): g.line([(13, 53), (43, 18)], width=7); g.polygon([(26, 12), (33, 7), (55, 27), (48, 35)])
    elif n in ("cancel", "close", "remove", "rejected"):
        g.line([(15, 15), (49, 49)], width=6); g.line([(49, 15), (15, 49)], width=6)
        if n == "rejected": g.circle(32, 32, 25)
    elif n == "unavailable":
        g.circle(32,23,13,width=5);g.rect(13,27,38,29);g.rect(29,36,6,12,EDGE)
    elif n == "unsupplied":
        g.rect(10,17,44,36);g.line([(10,29),(54,29)],EDGE,3);g.line([(12,54),(52,14)],width=5)
    elif n == "invalid":
        g.circle(32, 32, 23); g.line([(15, 49), (49, 15)], width=5)
    elif n in ("select", "drag_select", "selected_group", "selection_corner"):
        if n == "selection_corner": g.line([(10, 54), (10, 10), (54, 10)], width=4)
        else:
            for x,y,sx,sy in [(10,10,1,1),(54,10,-1,1),(10,54,1,-1),(54,54,-1,-1)]: g.line([(x,y+sy*13),(x,y),(x+sx*13,y)])
            if n == "drag_select": g.line([(22,44),(42,24)], width=2)
    elif n == "pan":
        g.line([(10,32),(54,32)]); g.line([(32,10),(32,54)])
        for points in [[(10,32),(18,24)],[(10,32),(18,40)],[(54,32),(46,24)],[(54,32),(46,40)],[(32,10),(24,18)],[(32,10),(40,18)],[(32,54),(24,46)],[(32,54),(40,46)]]:g.line(points)
    elif n in ("supplied", "accepted", "activate"):
        g.line([(12,32),(25,46),(53,16)], width=6)
        if n == "supplied": g.circle(32,32,26,width=2)
    elif n == "low":
        g.line([(11,12),(11,53),(53,53),(53,12),(11,12)]);g.rect(18,38,28,9)
    elif n == "damaged":
        g.polygon([(12,13),(32,7),(52,13),(49,39),(32,56),(15,39)]);g.line([(34,10),(26,27),(39,30),(27,51)],EDGE,4)
    elif n in ("critical", "warning", "alert", "attack_notification"):
        g.polygon([(32,7),(58,54),(6,54)])
        g.rect(29,23,6,17,EDGE); g.rect(29,45,6,5,EDGE)
        if n in ("critical", "attack_notification"): g.line([(9,59),(55,59)],width=3)
    elif n in ("operational", "construction", "idle", "unpowered", "destroyed", "partial"):
        if n == "operational":draw(g,"status.accepted");g.circle(32,32,25,width=2)
        elif n == "construction":draw(g,"command.build");g.line([(10,58),(54,58)],width=3)
        elif n == "idle":draw(g,"utility.pause");g.circle(32,32,26,width=2)
        elif n == "unpowered":draw(g,"building_role.power");g.line([(8,12),(56,54)],width=5)
        elif n == "destroyed":g.line([(10,54),(20,34),(30,47),(42,23),(55,54),(10,54)]);draw(g.child(.45,(31,2)),"utility.cancel")
        else:draw(g.child(.65,(0,14)),"status.accepted");draw(g.child(.55,(31,3)),"status.warning")
    elif n == "pending":
        g.line([(15,10),(49,10),(43,22),(24,43),(15,54),(49,54),(40,43),(22,23),(15,10)])
    elif n == "health":
        g.polygon([(25,8),(39,8),(39,25),(56,25),(56,39),(39,39),(39,56),(25,56),(25,39),(8,39),(8,25),(25,25)])
    elif n in ("friendly_unit", "building"):
        g.rect(13,13,38,38, PALETTE["TextPrimary"])
        if n == "building": g.rect(23,23,18,18,EDGE)
        else:g.rect(20,20,24,24,EDGE)
    elif n == "visible_enemy": g.polygon([(32,9),(57,53),(7,53)], PALETTE["Critical"])
    elif n in ("detected_contact", "contact"):
        g.circle(32,32,18, PALETTE["Warning"],4)
        for p in [[(32,6),(32,10)],[(32,54),(32,58)],[(6,32),(10,32)],[(54,32),(58,32)]]:g.line(p)
    elif n == "command_structure":g.polygon([(32,6),(56,24),(47,54),(17,54),(8,24)])
    elif n == "objective":g.polygon([(32,7),(57,32),(32,57),(7,32)]);g.polygon([(32,21),(43,32),(32,43),(21,32)],EDGE)
    elif n in ("research", "technology"):
        g.line([(24,9),(40,9)]);g.polygon([(27,10),(37,10),(37,27),(53,52),(11,52),(27,27)]);g.line([(22,36),(43,36)],EDGE,3)
    elif n == "controls":
        g.rect(7,18,50,31)
        for y in (25,35):
            for x in (14,25,36,47):g.rect(x,y,4,4,EDGE)
        g.line([(21,44),(43,44)],EDGE,3)
    elif n in ("audio", "muted"):
        g.polygon([(9,25),(20,25),(35,12),(35,52),(20,39),(9,39)])
        if n == "audio":g.line([(44,22),(50,27),(50,37),(44,42)]);g.line([(52,14),(59,25),(59,39),(52,50)])
        else:g.line([(44,24),(58,40)]);g.line([(58,24),(44,40)])
    elif n in ("pause",):g.rect(15,12,12,40);g.rect(37,12,12,40)
    elif n in ("play", "continue", "resume"):g.polygon([(20,9),(54,32),(20,55)])
    elif n in ("new_game",):g.rect(12,12,40,40);g.line([(20,32),(44,32)],EDGE,4);g.line([(32,20),(32,44)],EDGE,4)
    elif n in ("load", "load_game"):
        g.polygon([(8,18),(25,18),(29,24),(55,24),(51,51),(10,51)]);g.line([(32,7),(32,38),(24,30)],EDGE,4);g.line([(32,38),(40,30)],EDGE,4)
    elif n in ("save", "save_game", "save_return"):
        g.polygon([(10,9),(47,9),(55,17),(55,55),(10,55)]);g.rect(19,9,25,18,EDGE);g.rect(19,37,27,18,EDGE)
        if n=="save_return":draw(g.child(.45,(34,34)),"utility.back")
    elif n in ("exit", "return_menu"):
        g.line([(31,9),(12,9),(12,55),(31,55)]);draw(g.child(.6,(20,12)),"command.move")
    elif n in ("credits", "info"):
        g.circle(32,32,24);g.rect(29,27,6,21);g.rect(29,15,6,6)
    elif n in ("display",):g.rect(8,12,48,32);g.line([(32,44),(32,54),(19,54),(45,54)])
    elif n in ("general", "primary_setting", "secondary_setting"):
        for y,x in [(16,22),(32,43),(48,28)]:g.line([(9,y),(55,y)]);g.rect(x-4,y-6,8,12)
        if n=="secondary_setting":g.circle(52,10,5)
    elif n == "bindings":
        g.circle(21,23,12);g.line([(30,32),(52,54)],width=6);g.line([(42,44),(49,37)],width=5)
    elif n in ("formation", "group"):
        for x,y in [(32,12),(16,35),(48,35),(32,52)]:g.rect(x-5,y-5,10,10)
    elif n in ("overlay", "all", "navigation"):
        for y in (18,32,46):g.line([(9,y),(32,y-9),(55,y),(32,y+9),(9,y)])
    elif n == "minimap":
        g.line([(8,15),(24,9),(42,15),(56,9),(56,49),(42,55),(24,49),(8,55),(8,15)]);g.line([(24,9),(24,49)]);g.line([(42,15),(42,55)])
    elif n in ("expand", "collapse"):
        for x,y,sx,sy in [(10,10,1,1),(54,10,-1,1),(10,54,1,-1),(54,54,-1,-1)]:
            if n=="expand":g.line([(x,y+sy*14),(x,y),(x+sx*14,y)])
            else:g.line([(x,y),(x+sx*14,y+sy*14),(x,y+sy*14),(x+sx*14,y+sy*14),(x+sx*14,y)])
    elif n in ("retreat", "recovery"):
        arrow(g,"left");g.line([(40,11),(54,11),(54,53),(40,53)])
        if n=="recovery":draw(g.child(.42,(35,34)),"status.health")
    elif n == "surrender":g.line([(16,57),(16,8)],width=4);g.polygon([(18,9),(49,9),(43,27),(18,27)])
    elif n.startswith("cancel_") or n=="remove_stock":
        base={"cancel_research":"research","cancel_production":"factory","cancel_unit_production":"armor","cancel_fire_mission":"artillery","remove_stock":"stock"}[n]
        draw(g.child(.75,(2,0)),"base."+base);draw(g.child(.45,(34,34)),"utility.cancel")
    elif n in ("priority",):g.line([(14,31),(32,13),(50,31)]);g.line([(14,49),(32,31),(50,49)])
    elif n in ("ring_segment",):
        g.line([(32+math.cos(a*math.pi/180)*23,32+math.sin(a*math.pi/180)*23) for a in range(200,341,5)],width=4)
    else: raise ValueError("Unimplemented production role: "+name)


def enum_members(path, name):
    source=(ROOT/path).read_text(encoding="utf-8-sig")
    body=re.search(r"enum "+name+r"\s*(?::[^\{]+)?\{([^}]+)\}",source,re.S).group(1)
    return [part.strip().split("=")[0].strip() for part in body.split(",") if part.strip()]


GAMEPLAY = dict(zip("Build Process Units Logistics Supply Combat Technology NextItem PrimarySetting SecondarySetting Decrease Increase Activate CancelJob FocusBase Formation Overlay Minimap Pause CorePlacement PowerPlacement ExtractorPlacement StoragePlacement SmelterPlacement RotatePlacement Demolish".split(),
                    "command.build building_role.processing unit_role.infantry unit_role.logistics command.supply command.attack utility.research utility.next_item utility.primary_setting utility.secondary_setting utility.decrease utility.increase status.activate command.cancel building_role.command utility.formation utility.overlay utility.minimap utility.pause building_role.command building_role.power building_role.extraction building_role.storage building_role.processing utility.rotate status.destroyed".split()))
REQUESTS = dict(zip("BeginBuildingPlacement QueueProduction SetProductionPaused CancelProduction QueueUnitProduction CancelUnitProduction SetStockPolicy RemoveStockPolicy SetAutomaticResupplyPolicy RequestResupply BeginAttackTargeting BeginAttackMoveTargeting SubmitStopCombat SubmitHoldPosition BeginRetreatTargeting BeginFireMissionTargeting CancelFireMission SubmitAttack SubmitAttackMove SubmitRetreat SubmitFireMissionCoordinate SubmitFireMissionContact Surrender SetSupplyPriority SubmitRetreatToRecovery StartTechnologyResearch CancelTechnologyResearch CreateCargoConvoy DissolveCargoConvoy".split(),
                    "command.build building_role.factory command.production_pause command.cancel_production command.unit_production command.cancel_unit_production command.stock command.remove_stock command.automatic_resupply command.supply command.attack command.attack_move command.stop command.hold command.retreat command.fire_mission command.cancel_fire_mission command.attack command.attack_move command.retreat command.fire_mission command.fire_mission command.surrender command.priority command.recovery utility.research command.cancel_research utility.group command.cancel".split()))
UTILITY = "settings controls audio muted pause play new_game load save save_return exit return_menu credits display general bindings group rotate research next_item primary_setting secondary_setting decrease increase expand collapse scroll_up scroll_down back forward info formation overlay minimap".split()
STATUS = "health fuel ammunition power alert damaged maintenance warning critical pending accepted rejected invalid unavailable supplied low unsupplied activate operational construction idle unpowered destroyed partial".split()
MARKERS = "friendly_unit visible_enemy detected_contact building command_structure resource depot objective selected_group attack_notification".split()


def validate_command_bindings():
    for domain,path,mapping in [
        ('GameplayAction','src/ForgeLine.Input/GameplayBindingRegistry.cs',GAMEPLAY),
        ('PlayerActionRequestKind','src/ForgeLine.Game/PlayerActionRequests.cs',REQUESTS)]:
        expected=set(enum_members(path,domain))-{'None'}
        missing=expected-set(mapping)
        unexpected=set(mapping)-expected
        if missing or unexpected:
            raise ValueError(f'{domain} artwork bindings differ: missing {sorted(missing)}, unexpected {sorted(unexpected)}')


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--portraits",type=Path,required=True)
    parser.add_argument("--preview",type=Path,required=True)
    args=parser.parse_args()
    validate_command_bindings()
    OUT.mkdir(parents=True,exist_ok=True)
    source=(ROOT/'src/ForgeLine.Presentation/RtsInformationLayer.cs').read_text()
    legacy=re.findall(r'Add\(RtsUiIcon\.(\w+), "ui.icon.([^"]+)"',source)
    roles={role for _,role in legacy}|set(GAMEPLAY.values())|set(REQUESTS.values())
    roles|={"utility."+x for x in UTILITY}|{"status."+x for x in STATUS}|{"marker."+x for x in MARKERS}
    roles|={"world.selection_corner","world.ring_segment","world.target","world.move","world.attack_move"}
    entries=[]
    for role in sorted(roles):
        family,name=role.split('.',1)
        folder=OUT/family;folder.mkdir(exist_ok=True)
        master=folder/(name+'.svg')
        g=Glyph(64);draw(g,role)
        master.write_text('\n'.join(g.drawing.svg+['</svg>','']), encoding='utf-8', newline='\n')
        unavailable=role=='command.patrol'
        for size in SIZES:
            # Independent vector rasterization per target size; no upscaled bitmap.
            g=Glyph(size*4);draw(g,role)
            image=g.drawing.image.resize((size,size),Image.Resampling.LANCZOS)
            # Reserve explicit transparent exterior and suppress tiny resampling tails.
            pixels=image.load()
            for y in range(size):
                for x in range(size):
                    if x in (0,size-1) or y in (0,size-1) or pixels[x,y][3]<4:pixels[x,y]=(0,0,0,0)
            file=folder/f'{name}_{size}.png';image.save(file)
            identifier=f'ui.artwork.directorate.{role}.p{size}'
            entry={'id':identifier,'logicalRole':role,'file':file.relative_to(OUT).as_posix(),'editableSource':master.relative_to(OUT).as_posix(),
                   'width':size,'height':size,'insets':[0]*4,'contentPadding':[0]*4,'minimumSize':[size,size],
                   'scaling':'fixed','edgePolicy':'stretch','centerPolicy':'stretch','alphaMode':'straight',
                   'sha256':sha(file),'editableSha256':sha(master),'visibleBounds':list(image.getbbox()),
                   'availability':'intentionally_unavailable' if unavailable else 'inspection_only' if family=='cursor' and size==16 else 'runtime_required'}
            if family=='cursor':
                entry['hotspot']=[round(8*size/64),round(6*size/64)];entry['activation']='source_only_no_cursor_enabled'
                entry['minimumIntendedDisplaySize']=24
            entries.append(entry)
            if entry['availability']=='runtime_required':
                (file.with_suffix('.asset.json')).write_text(json.dumps({'id':identifier,'type':'texture','source':file.name,'textureUsage':'displayImage','textureColorSpace':'linear','textureGenerateMipmaps':False,'textureFormat':'rgba8Unorm'},indent=2)+'\n')
            elif file.with_suffix('.asset.json').exists():
                file.with_suffix('.asset.json').unlink()
    capture=json.loads((args.portraits/'portraits.json').read_text())
    portrait_dir=OUT/'portrait';portrait_dir.mkdir(exist_ok=True)
    for item in capture['captures']:
        original=args.portraits/item['file'];assert sha(original)==item['sha256']
        target=portrait_dir/original.name;shutil.copyfile(original,target)
        identifier=f"ui.artwork.directorate.portrait.{item['kind']}_{item['gameplayId']}.p{item['size']}"
        entries.append({'id':identifier,'logicalRole':f"portrait.{item['kind']}_{item['gameplayId']}",'file':target.relative_to(OUT).as_posix(),
                        'width':item['size'],'height':item['size'],'insets':[0]*4,'contentPadding':[0]*4,'minimumSize':[item['size']]*2,'scaling':'fixed',
                        'alphaMode':'opaque_native_capture','sha256':sha(target),'availability':'runtime_required','gameplayKind':item['kind'],'gameplayId':item['gameplayId'],
                        'gameplayKey':item['key'],'meshAssetId':item['meshAssetId'],'meshRuntimeHash':item['meshRuntimeHash'],
                        'requiresIdentifiedPermittedReadModel':True,'nativeCaptureFile':item['file']})
        target.with_suffix('.asset.json').write_text(json.dumps({'id':identifier,'type':'texture','source':target.name,'textureUsage':'displayImage','textureColorSpace':'linear','textureGenerateMipmaps':False,'textureFormat':'rgba8Unorm'},indent=2)+'\n')
    bindings=[{'domain':'RtsUiIcon','member':member,'role':role,'legacyAssetId':'ui.icon.'+role,'availability':'intentionally_unavailable' if role=='command.patrol' else 'runtime_required'} for member,role in legacy]
    for domain,mapping in [('GameplayAction',GAMEPLAY),('PlayerActionRequestKind',REQUESTS)]:
        bindings.extend({'domain':domain,'member':member,'role':role,'availability':'runtime_required'} for member,role in mapping.items())
    for domain,path,mapping in [
        ('MainMenuCommand','src/ForgeLine.UI/MainMenuModel.cs',{'Continue':'utility.play','NewGame':'utility.new_game','LoadGame':'utility.load','Settings':'utility.settings','Credits':'utility.credits','Exit':'utility.exit','Controls':'utility.controls'}),
        ('PauseMenuCommand','src/ForgeLine.UI/PauseMenuModel.cs',{'Resume':'utility.play','SaveGame':'utility.save','SaveAndReturnToMenu':'utility.save_return','ReturnToMenu':'utility.return_menu','Controls':'utility.controls','Settings':'utility.settings'}),
        ('StrategicOverlayMode','src/ForgeLine.Presentation/RtsInformationLayer.cs',{'Logistics':'unit_role.logistics','Supply':'command.supply','Sensors':'unit_role.reconnaissance','Navigation':'utility.overlay','Power':'building_role.power','All':'utility.overlay'}),
        ('FrontendSettingsSection','src/ForgeLine.UI/FrontendInteraction.cs',{'General':'utility.general','Graphics':'utility.display','Audio':'utility.audio','Bindings':'utility.bindings'}),
        ('TacticalTargetingMode','src/ForgeLine.Presentation/RtsTacticalTargetingController.cs',{'Attack':'command.attack','AttackMove':'command.attack_move','Retreat':'command.retreat','FireMission':'command.fire_mission'}),
        ('BattlefieldSupplyStatus','src/ForgeLine.Game/BattlefieldSupplyContracts.cs',{'Supplied':'status.supplied','LowSupply':'status.low','Critical':'status.critical','Unsupplied':'status.unsupplied'}),
        ('PlayerCommandFeedbackState','src/ForgeLine.Game/PlayerExperienceSnapshot.cs',{'Accepted':'status.accepted','Partial':'status.partial','Rejected':'status.rejected'}),
        ('UnitPresentationDamageState','src/ForgeLine.Presentation/UnitPresentationCatalog.cs',{'Intact':'status.operational','Damaged':'status.damaged','Critical':'status.critical','Wreck':'status.destroyed'}),
        ('BuildingPresentationState','src/ForgeLine.Presentation/BuildingPresentationCatalog.cs',{'ConstructionFoundation':'status.construction','ConstructionFrame':'status.construction','ConstructionShell':'status.construction','Operational':'status.operational','Idle':'status.idle','Unpowered':'status.unpowered','Damaged':'status.damaged','Critical':'status.critical','Destroyed':'status.destroyed'})]:
        assert set(mapping)==set(enum_members(path,domain))-{'None'}
        bindings.extend({'domain':domain,'member':member,'role':role,'availability':'runtime_required'} for member,role in mapping.items())
    bindings.extend({'domain':'RtsCursorKind','member':name,'role':f'cursor.{name.lower() if name not in ("AttackMove","DragSelect") else {"AttackMove":"attack_move","DragSelect":"drag_select"}[name]}','availability':'runtime_required'} for name in enum_members('src/ForgeLine.Presentation/RtsInformationLayer.cs','RtsCursorKind'))
    bindings.extend({'domain':'RtsMinimapSymbolKind','member':member,'role':role.replace('minimap.','marker.'),'availability':'runtime_required'} for member,role in [(name.removeprefix('Minimap'),role) for name,role in legacy if role.startswith('minimap.')])
    for member in enum_members('src/ForgeLine.UI/FrontendInteraction.cs','FrontendSettingsField'):
        role='utility.audio' if member.startswith(('UiSound', 'WorldSound', 'MasterSound', 'AlertSound', 'AmbienceSound')) else 'utility.bindings' if member.startswith('Binding') else 'utility.info' if member=='Onboarding' else 'utility.general' if member in ('CameraSpeed','EdgeScroll','ReducedUiMotion') else 'utility.display'
        bindings.append({'domain':'FrontendSettingsField','member':member,'role':role,'availability':'runtime_required','dynamicLabelRequired':True})
    for identity,role in [(1,'building_role.processing'),(2,'unit_role.logistics'),(3,'unit_role.armor'),(4,'unit_role.reconnaissance')]:
        bindings.append({'domain':'TechnologyId','member':str(identity),'role':role,'availability':'runtime_required','dynamicLabelRequired':True})
    catalog={'schemaVersion':1,'purpose':'ui-artwork-authoring-contract','baseSkinCatalog':'../skin.catalog.json',
             'runtimeLoader':'none; ordinary texture compiler definitions only','reviewStatus':'pending_human_review',
             'provenance':'Independently drawn vector pictograms; portraits are unchanged native production-model captures.',
             'rightsEvidence':'No reference pixels copied and no additional license or reference permissions asserted.',
             'sizes':list(SIZES),'portraitSizes':[48,64,96,128,256],'sprites':entries,'bindings':bindings,
             'referenceOnly':[{'concept':'railway','runtimeRegistered':False},{'concept':'aviation','runtimeRegistered':False}],
             'intentionalExceptions':[{'domain':'PlayerActionRequestKind','member':'None','reason':'No action'},
                                      {'domain':'RtsUiIcon','member':'None','reason':'No icon'},
                                      {'domain':'StrategicOverlayMode','member':'None','reason':'No overlay'},
                                      {'domain':'TacticalTargetingMode','member':'None','reason':'No targeting'},
                                      {'domain':'PlayerCommandFeedbackState','member':'None','reason':'No feedback'},
                                      {'domain':'BuildingPresentationState','member':'None','reason':'No building'},
                                      {'role':'cursor.*.p16','reason':'Inspection-only: action badge is too small; intended cursor sizes begin at 24 pixels; no runtime definitions.'},
                                      {'role':'command.patrol','reason':'Legacy icon role exists; no implemented patrol submission; source-only, no texture registration.'}],
             'privacy':'Detected contacts use only generic contact geometry; portraits require permitted identification. Markers never infer an enemy class.',
             'nativePortraitCapture':capture}
    (OUT/'artwork.catalog.json').write_text(json.dumps(catalog,indent=2)+'\n')
    args.preview.mkdir(parents=True,exist_ok=True)
    font=ImageFont.load_default(size=12)
    for family in sorted({e['logicalRole'].split('.')[0] for e in entries}):
        rows=sorted({e['logicalRole'] for e in entries if e['logicalRole'].startswith(family+'.')})
        row_height=280 if family=='portrait' else 100
        sheet=Image.new('RGB',(1050 if family=='portrait' else 950,row_height*len(rows)), '#273033');paint=ImageDraw.Draw(sheet)
        for row,role in enumerate(rows):
            paint.text((8,row*row_height+8),role,font=font,fill='white')
            x=260
            for entry in sorted([e for e in entries if e['logicalRole']==role],key=lambda e:e['width']):
                im=Image.open(OUT/entry['file']).convert('RGBA')
                for background in (('#11191c',) if family=='portrait' else ('#11191c','#d9d7cf')):
                    tile=Image.new('RGBA',im.size,background);tile.alpha_composite(im)
                    sheet.paste(tile.convert('RGB'),(x,row*row_height+4));x+=im.width+7
        sheet.save(args.preview/(family+'.png'))
        if family=='portrait':
            for first in range(0,len(rows),5):sheet.crop((0,first*row_height,sheet.width,min(len(rows),first+5)*row_height)).save(args.preview/f'portrait-page-{first//5+1}.png')
        if family in ('marker','status'):
            ImageOps.grayscale(sheet).save(args.preview/(family+'-grayscale.png'))
            # Diagnostic channel-loss proxies, not calibrated human vision simulations.
            sheet.convert('RGB',(.5,.5,0,0,.5,.5,0,0,0,0,1,0)).save(args.preview/(family+'-red-green-reduced.png'))
            sheet.convert('RGB',(1,0,0,0,0,.5,.5,0,0,.5,.5,0)).save(args.preview/(family+'-blue-green-reduced.png'))
    print(f'Authored {len(roles)} vector roles and 20 roster portraits; {len(entries)} size-specific sprites, {sum(e["availability"]=="runtime_required" for e in entries)} registered textures.')


if __name__=='__main__':main()
